using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Per-client authority over ordinary test hosts, never production lifecycle policy.</summary>
internal sealed class TestOwnedPtyHost
{
    internal enum State { Alive, Dead, Unknown, Reused }
    internal sealed record ProcessIdentity(int Pid, long Generation, DateTime StartedUtc, string Image,
        int ParentPid = 0, Process? Handle = null);
    internal sealed record Observation(State State, long Generation, string? Reason = null);
    internal sealed record Owned(string Root, Guid SessionId, string ManifestPath,
        ProcessIdentity Process, bool Host, bool Attributed);

    // Instance-local I/O boundaries. Tests use the real capture and cleanup algorithm.
    internal sealed class Operations
    {
        public TimeProvider Clock { get; init; } = TimeProvider.System;
        public Func<string, PtyHostManifest?> ReadManifest { get; set; } = ReadOwnedManifest;
        public Func<int, ProcessIdentity> Capture { get; set; } = CaptureProcess;
        public Func<ProcessIdentity, IReadOnlyList<ProcessIdentity>> Descendants { get; set; } = CaptureDescendants;
        public Func<ProcessIdentity, Observation> Observe { get; set; } = ObserveProcess;
        public Action<ProcessIdentity> Kill { get; set; } = p => p.Handle!.Kill(entireProcessTree: true);
        public Func<ProcessIdentity, TimeSpan, CancellationToken, Task>? Wait { get; set; }
        public Action<ProcessIdentity> Release { get; set; } = p => p.Handle?.Dispose();
        public Action<string, Owned?> Record { get; set; } = (operation, owned) => Console.WriteLine($"C1020 cleanup operation={operation} session={owned?.SessionId} pid={owned?.Process.Pid} generation={owned?.Process.Generation} at={DateTime.UtcNow:O}");
        public bool VerifyHostCommand { get; init; } = true;
    }

    private readonly Operations _io;
    private readonly List<Owned> _owned = [];
    private readonly List<Exception> _errors = [];
    private readonly object _gate = new();
    private Task? _disposal;
    internal string Root { get; }
    internal IReadOnlyList<Owned> Captured => _owned;

    internal TestOwnedPtyHost(string root, Operations? operations = null)
    {
        Root = Canonical(root);
        _io = operations ?? new();
        _io.Wait ??= WaitNativeAsync;
    }

    internal static bool Eligible(string? backend, bool verificationBound) =>
        !verificationBound && (backend is null or SessionBackends.PtyHost);

    internal bool Authorized(Owned candidate, Observation current) =>
        SamePath(candidate.Root, Root)
        && SamePath(candidate.ManifestPath, PtyHostManifest.PathFor(Path.Combine(Root, "manifests"), candidate.SessionId))
        && Under(candidate.ManifestPath, Root)
        && candidate.SessionId != Guid.Empty
        && candidate.Process.Pid > 0 && candidate.Process.Generation > 0
        && current.State == State.Alive && candidate.Process.Generation == current.Generation
        && (!candidate.Host || (Under(candidate.Process.Image, Path.Combine(Root, "bin"))
            && string.Equals(Path.GetFileName(candidate.Process.Image), PtyHostLauncher.HostExeName, PathComparison)))
        && candidate.Attributed;

    internal bool Retain(Owned candidate, Guid expectedSession)
    {
        if (candidate.SessionId != expectedSession || !Authorized(candidate, Observe(candidate.Process)))
        {
            _io.Record("rejected", candidate);
            _io.Release(candidate.Process);
            return false;
        }
        if (_owned.Any(x => x.Process.Pid == candidate.Process.Pid && x.Process.Generation == candidate.Process.Generation))
        {
            _io.Release(candidate.Process);
            return true;
        }
        _owned.Add(candidate);
        _io.Record("captured", candidate);
        return true;
    }

    internal void CaptureTracked(IEnumerable<RunnerSessionDto> sessions, IEnumerable<RunnerLaunchRequest> requests)
    {
        foreach (var session in sessions)
            Capture(session.SessionId, session.Backend, session.VerificationBinding is not null);
        foreach (var request in requests)
            Capture(request.SessionId, request.Backend, request.VerificationBinding is not null);
    }

    internal void Capture(Guid sessionId, string? backend, bool verificationBound)
    {
        if (!Eligible(backend, verificationBound)) return;
        var path = PtyHostManifest.PathFor(Path.Combine(Root, "manifests"), sessionId);
        try
        {
            if (!Under(path, Root)) throw new IOException("manifest escaped owned root");
            var manifest = _io.ReadManifest(path);
            if (manifest is null || manifest.VerificationBinding is not null) return;
            if (manifest.SessionId != sessionId) throw new IOException("foreign session manifest");
            ProcessIdentity host;
            try { host = _io.Capture(manifest.HostPid); }
            catch (ArgumentException) { return; } // Confirmed absent PID, not an access failure.
            var attributed = !_io.VerifyHostCommand || HostMatchesManifest(host, manifest, path);
            var ownedHost = new Owned(Root, sessionId, path, host, true, attributed);
            if (!Retain(ownedHost, sessionId)) throw new IOException($"host identity rejected pid={manifest.HostPid}");
            // Keep every generation even after a runtime replacement or manifest deletion.
            foreach (var child in _io.Descendants(host))
            {
                var attributedChild = child.ParentPid == host.Pid || _owned.Any(x =>
                    x.SessionId == sessionId && x.Process.Pid == child.ParentPid);
                Retain(new(Root, sessionId, path, child, false, attributedChild), sessionId);
            }
        }
        catch (Exception ex) { _errors.Add(new IOException($"capture session={sessionId:D} manifest={path}", ex)); }
    }

    internal Task DisposeAsync(bool kill, Action supplement, Func<CancellationToken, Task> stop,
        Func<Task> disposeRuntime)
    {
        lock (_gate) return _disposal ??= DisposeCoreAsync(kill, supplement, stop, disposeRuntime);
    }

    private async Task DisposeCoreAsync(bool kill, Action supplement, Func<CancellationToken, Task> stop, Func<Task> disposeRuntime)
    {
        var started = _io.Clock.GetTimestamp();
        _io.Record("dispose-core", null);
        var failures = new List<Exception>();
        try
        {
            if (kill)
            {
                try { supplement(); } catch (Exception ex) { failures.Add(ex); }
                _io.Record("stop-sessions", null);
                using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2), _io.Clock))
                {
                    try { await stop(deadline.Token); }
                    catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
                    catch (Exception ex) { failures.Add(ex); }
                }
                try { await CleanupAsync(); } catch (Exception ex) { failures.Add(ex); }
            }
        }
        finally
        {
            try { await disposeRuntime(); } catch (Exception ex) { failures.Add(ex); }
            finally
            {
                foreach (var record in _owned)
                {
                    try { _io.Release(record.Process); } catch (Exception ex) { failures.Add(ex); }
                }
            }
        }
        if (failures.Count > 0)
            throw new AggregateException($"test-owned disposal unresolved elapsed={_io.Clock.GetElapsedTime(started)}", failures);
    }

    internal async Task CleanupAsync()
    {
        var started = _io.Clock.GetTimestamp();
        var failures = new List<Exception>(_errors);
        await WaitPhaseAsync(TimeSpan.FromSeconds(2), failures);
        var fallbackStarted = _io.Clock.GetTimestamp();
        foreach (var record in _owned)
        {
            try { Force(record); }
            catch (Exception ex) { failures.Add(Failure(record, "kill", started, ex)); }
        }
        await WaitPhaseAsync(Remaining(fallbackStarted, TimeSpan.FromSeconds(5)), failures);
        foreach (var record in _owned)
        {
            try
            {
                var observed = Observe(record.Process);
                if (observed.State == State.Dead) _io.Record("observed-exit", record);
                if (observed.State != State.Dead)
                    failures.Add(Failure(record, "survivor/" + observed.State, started));
            }
            catch (Exception ex) { failures.Add(Failure(record, "probe", started, ex)); }
        }
        if (failures.Count > 0)
            throw new AggregateException($"cleanup-unresolved elapsed={_io.Clock.GetElapsedTime(started)}", failures);
    }

    internal void Force(Owned record)
    {
        var current = Observe(record.Process);
        if (current.State == State.Dead) return;
        // Recheck the generation at the control boundary, including retained descendants.
        if (!Authorized(record, current))
        {
            _io.Record("rejected-control", record);
            throw new IOException($"unresolved ownership pid={record.Process.Pid} generation={record.Process.Generation} state={current.State}");
        }
        _io.Record("kill", record);
        _io.Kill(record.Process);
    }

    private async Task WaitPhaseAsync(TimeSpan budget, List<Exception> failures)
    {
        var started = _io.Clock.GetTimestamp();
        foreach (var record in _owned)
        {
            var remaining = Remaining(started, budget);
            if (remaining <= TimeSpan.Zero) break;
            try
            {
                using var deadline = new CancellationTokenSource(remaining, _io.Clock);
                await _io.Wait!(record.Process, remaining, deadline.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { failures.Add(Failure(record, "wait", started, ex)); }
        }
    }

    private TimeSpan Remaining(long started, TimeSpan budget) =>
        TimeSpan.FromTicks(Math.Max(0, (budget - _io.Clock.GetElapsedTime(started)).Ticks));

    private IOException Failure(Owned record, string phase, long started, Exception? inner = null) =>
        new($"session={record.SessionId:D} pid={record.Process.Pid} generation={record.Process.Generation} phase={phase} elapsed={_io.Clock.GetElapsedTime(started)}", inner);

    private Observation Observe(ProcessIdentity identity)
    {
        try { return _io.Observe(identity); }
        catch (Exception ex) { return new(State.Unknown, identity.Generation, ex.GetType().Name); }
    }

    private async Task WaitNativeAsync(ProcessIdentity identity, TimeSpan remaining, CancellationToken ct)
    {
        while (true)
        {
            var observation = Observe(identity);
            if (observation.State == State.Dead) return;
            if (observation.State != State.Alive) throw new IOException($"process observation {observation.State}: {observation.Reason}");
            await Task.Delay(TimeSpan.FromMilliseconds(20), _io.Clock, ct);
        }
    }

    internal static async Task ScopeAsync(Func<Task> body, Func<Task> cleanup)
    {
        Exception? primary = null;
        try { await body(); } catch (Exception ex) { primary = ex; }
        try { await cleanup(); }
        catch (Exception ex)
        {
            if (primary is not null) throw new AggregateException("body and cleanup both failed", primary, ex);
            throw;
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    internal static void ValidateWitnesses(IEnumerable<(string Role, bool Alive)> observations, bool windows)
    {
        var witnesses = observations.ToArray();
        if (!witnesses.Any(x => x.Role == "host" && x.Alive)) throw new IOException("missing live host witness");
        if (!witnesses.Any(x => x.Role == "child" && x.Alive)) throw new IOException("missing live child witness");
        if (windows && !witnesses.Any(x => x.Role == "console" && x.Alive)) throw new IOException("missing live Windows console witness");
        if (witnesses.Any(x => !x.Alive)) throw new IOException("witness was not alive before cleanup");
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal static bool SamePath(string a, string b) => string.Equals(Canonical(a), Canonical(b), PathComparison);
    internal static bool Under(string path, string root) => Canonical(path).StartsWith(Canonical(root) + Path.DirectorySeparatorChar, PathComparison);
    private static string Canonical(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null) current = info.ResolveLinkTarget(true)!.FullName;
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    internal static ProcessIdentity CaptureProcess(int pid)
    {
        var process = Process.GetProcessById(pid);
        try
        {
            var start = process.StartTime.ToUniversalTime();
            var generation = OperatingSystem.IsLinux() ? Proc(pid).Generation : start.Ticks;
            return new(pid, generation, start, process.MainModule!.FileName, Handle: process);
        }
        catch { process.Dispose(); throw; }
    }

    private static PtyHostManifest? ReadOwnedManifest(string path)
    {
        try { return JsonSerializer.Deserialize<PtyHostManifest>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static bool HostMatchesManifest(ProcessIdentity host, PtyHostManifest manifest, string manifestPath)
    {
        if (OperatingSystem.IsWindows()) return host.StartedUtc == manifest.HostStartTimeUtc;
        // Linux Process.StartTime UTC conversion varies between observers. The kernel start token
        // is stable; bind initial attribution to the actual host's exact launch arguments as well.
        var args = File.ReadAllText($"/proc/{host.Pid}/cmdline").Split('\0');
        var session = Array.IndexOf(args, "--session");
        var directory = Array.IndexOf(args, "--manifest-dir");
        return session >= 0 && session + 1 < args.Length && Guid.TryParse(args[session + 1], out var id) && id == manifest.SessionId
            && directory >= 0 && directory + 1 < args.Length && SamePath(args[directory + 1], Path.GetDirectoryName(manifestPath)!);
    }

    internal static Observation ObserveProcess(ProcessIdentity identity)
    {
        try
        {
            if (identity.Handle?.HasExited == true) return new(State.Dead, identity.Generation);
            using var process = Process.GetProcessById(identity.Pid);
            var (generation, state, _) = OperatingSystem.IsLinux() ? Proc(identity.Pid)
                : (process.StartTime.ToUniversalTime().Ticks, process.HasExited ? 'X' : 'R', 0);
            if (generation != identity.Generation) return new(State.Reused, generation);
            return new(state is 'Z' or 'X' || process.HasExited ? State.Dead : State.Alive, generation);
        }
        catch (ArgumentException) { return new(State.Dead, identity.Generation); }
        catch (FileNotFoundException) { return new(State.Dead, identity.Generation); }
        catch (DirectoryNotFoundException) { return new(State.Dead, identity.Generation); }
    }

    private static (long Generation, char State, int Parent) Proc(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (long.Parse(fields[19], CultureInfo.InvariantCulture), fields[0][0], int.Parse(fields[1], CultureInfo.InvariantCulture));
    }

    private static IReadOnlyList<ProcessIdentity> CaptureDescendants(ProcessIdentity host)
    {
        var parents = new List<(int Pid, int Parent)>();
        if (OperatingSystem.IsWindows())
        {
            using var query = new ManagementObjectSearcher("SELECT ProcessId,ParentProcessId FROM Win32_Process");
            using var rows = query.Get();
            foreach (ManagementObject row in rows) parents.Add((Convert.ToInt32(row["ProcessId"]), Convert.ToInt32(row["ParentProcessId"])));
        }
        else
        {
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), out var pid)) continue;
                try { parents.Add((pid, Proc(pid).Parent)); }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }
        var result = new List<ProcessIdentity>();
        try
        {
            var known = new HashSet<int> { host.Pid };
            bool added;
            do
            {
                added = false;
                foreach (var row in parents.Where(x => known.Contains(x.Parent)).ToArray())
                {
                    if (!known.Add(row.Pid)) continue;
                    added = true;
                    try { result.Add(CaptureProcess(row.Pid) with { ParentPid = row.Parent }); }
                    catch (ArgumentException) { }
                }
            } while (added);
            return result;
        }
        catch { foreach (var process in result) process.Handle?.Dispose(); throw; }
    }
}
