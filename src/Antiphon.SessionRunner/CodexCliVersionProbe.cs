using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner;

/// <summary>Bounded, unauthenticated version-only children and their boot-bound memory evidence.</summary>
public sealed class CodexCliVersionProbe : IDisposable
{
    public const string Capability = "codex-cli-version-v1";
    public const int DescriptorFieldLimit = 32768;
    public const int OutputByteLimit = 4096;
    public const int CacheLimit = 32;
    private readonly TimeProvider _clock;
    private readonly Guid _boot;
    private readonly CodexCliVersionSettings _settings;
    private readonly Func<ProcessStartInfo, Process> _start;
    private readonly Func<string, int, byte[]> _read;
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<RunnerCodexCliVersionDto>> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _children = new(1, 1);
    private readonly List<(Process Process, Task Completion, string Scratch)> _cleanup = [];
    private RunnerCodexCliVersionDto _snapshot = new();

    public CodexCliVersionProbe(TimeProvider clock, PhoneHomeProcessIdentity identity,
        IOptions<CodexCliVersionSettings> settings,
        Func<ProcessStartInfo, Process>? start = null,
        Func<string, int, byte[]>? read = null)
    {
        _clock = clock;
        _boot = identity.BootId;
        _settings = settings.Value;
        _settings.Validate();
        _start = start ?? (info => Process.Start(info) ?? throw new IOException("Process unavailable."));
        _read = read ?? ReadPrefix;
    }

    public RunnerCodexCliVersionDto Snapshot { get { lock (_gate) return _snapshot; } }
    internal int CacheCount { get { lock (_gate) return _cache.Count; } }
    internal int OwnedCleanupCount { get { lock (_gate) return _cleanup.Count; } }
    /// <summary>Process-I/O seam for primary cleanup refusal. Retained children use independent reaping.</summary>
    internal Func<Process, CancellationToken, Task<bool>>? StopTreeAsync { get; set; }

    public async Task RefreshDefaultAsync(CancellationToken ct)
    {
        var sample = await ProbeAsync(new(_settings.Executable, _settings.ResolutionCwd), true, ct);
        lock (_gate) _snapshot = sample;
    }

    public async Task<RunnerCodexCliVersionDto> ProbeAsync(
        RunnerCodexCliProbeRequest request, bool force, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Resolved resolved;
        try { resolved = Resolve(request); }
        catch (ProbeRefusal e) { return Unknown(e.Reason); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                     or NotSupportedException or CodexLaunchException)
        { return Unknown("launcher_unverified"); }

        Task<RunnerCodexCliVersionDto> flight;
        var ownsFlight = false;
        lock (_gate)
        {
            if (_cache.TryGetValue(resolved.Fingerprint, out var existing)
                && (!existing.IsCompleted || !force && existing.IsCompletedSuccessfully
                    && _clock.GetUtcNow() - existing.Result.CodexCliVersionCheckedAtUtc
                    < TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes)))
                flight = existing;
            else
            {
                if (!_cache.ContainsKey(resolved.Fingerprint) && _cache.Count >= CacheLimit)
                {
                    var expired = _cache.FirstOrDefault(pair => pair.Value.IsCompletedSuccessfully
                        && _clock.GetUtcNow() - pair.Value.Result.CodexCliVersionCheckedAtUtc
                        >= TimeSpan.FromMinutes(_settings.RefreshIntervalMinutes));
                    if (expired.Key is null) return Unknown("probe_busy", resolved.Fingerprint);
                    _cache.Remove(expired.Key);
                }
                // Start after publishing the flight, so synchronous seams also single-flight.
                var completion = new TaskCompletionSource<RunnerCodexCliVersionDto>(TaskCreationOptions.RunContinuationsAsynchronously);
                flight = completion.Task;
                _cache[resolved.Fingerprint] = flight;
                ownsFlight = true;
                _ = CompleteAsync(resolved, completion, ct);
            }
        }
        // The owner awaits cancellation cleanup; other readers may cancel just their wait.
        return ownsFlight ? await flight : await flight.WaitAsync(ct);
    }

    private async Task CompleteAsync(Resolved resolved, TaskCompletionSource<RunnerCodexCliVersionDto> completion, CancellationToken ct)
    {
        try { completion.TrySetResult(await RunAsync(resolved, ct)); }
        catch (OperationCanceledException)
        {
            lock (_gate)
                if (_cache.TryGetValue(resolved.Fingerprint, out var flight) && ReferenceEquals(flight, completion.Task))
                    _cache.Remove(resolved.Fingerprint);
            completion.TrySetCanceled(ct);
        }
        catch (Exception) { completion.TrySetResult(Unknown("launcher_unverified", resolved.Fingerprint)); }
    }

    private async Task<RunnerCodexCliVersionDto> RunAsync(Resolved resolved, CancellationToken ct)
    {
        using var waitBudget = new CancellationTokenSource(TimeSpan.FromSeconds(1), _clock);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct, waitBudget.Token);
        try { await _children.WaitAsync(wait.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Unknown("probe_busy", resolved.Fingerprint); }
        string? scratch = null;
        Process? process = null;
        Task? completion = null;
        var retained = false;
        try
        {
            await ReapAsync();
            if (OwnedCleanupCount != 0) return Unknown("probe_busy", resolved.Fingerprint);
            scratch = Path.Combine(Path.GetTempPath(), "antiphon-codex-version-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            var home = Path.Combine(scratch, "home");
            Directory.CreateDirectory(home);
            var info = new ProcessStartInfo(resolved.Executable)
            {
                UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = scratch
            };
            foreach (var argument in resolved.Args) info.ArgumentList.Add(argument);
            info.Environment.Clear();
            foreach (var key in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "LANG", "LC_ALL", "TZ" })
                if (Environment.GetEnvironmentVariable(key) is { } value) info.Environment[key] = value;
            info.Environment["CODEX_HOME"] = home;
            info.Environment["HOME"] = scratch;
            info.Environment["USERPROFILE"] = scratch;
            process = _start(info);
            process.StandardInput.Close();
            var stdout = CaptureAsync(process.StandardOutput.BaseStream);
            var stderr = CaptureAsync(process.StandardError.BaseStream);
            completion = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr);
            string? error = null;
            try { await completion.WaitAsync(TimeSpan.FromSeconds(5), _clock, ct); }
            catch (TimeoutException) { error = "timeout"; }
            catch (OperationCanceledException) { error = "cancelled"; }
            if (error is not null)
            {
                using var cleanupBudget = new CancellationTokenSource(TimeSpan.FromSeconds(2), _clock);
                try
                {
                    var confirmed = StopTreeAsync is null ? KillTree(process)
                        : await StopTreeAsync(process, cleanupBudget.Token).WaitAsync(cleanupBudget.Token);
                    if (!confirmed) throw new IOException("Cleanup is unconfirmed.");
                    await completion.WaitAsync(cleanupBudget.Token);
                }
                catch (Exception)
                {
                    lock (_gate) _cleanup.Add((process, completion, scratch));
                    retained = true;
                    error = "cleanup_unconfirmed";
                }
                ct.ThrowIfCancellationRequested();
                return Unknown(error, resolved.Fingerprint);
            }
            var output = await stdout;
            var diagnostic = await stderr;
            if (process.ExitCode != 0) return Unknown("nonzero_exit", resolved.Fingerprint);
            // A truncated terminal fragment could be only the prefix of a different version.
            // Only stdout truncation removes EOF evidence; stderr is never version evidence.
            var length = output.Truncated ? Array.LastIndexOf(output.Bytes, (byte)'\n') + 1 : output.Bytes.Length;
            var version = CodexCliVersion.ParseBanner(Encoding.UTF8.GetString(output.Bytes, 0, length));
            if (version is null)
                return Unknown(output.Truncated ? "output_truncated" : "invalid_output", resolved.Fingerprint);
            var advisory = output.Truncated || diagnostic.Truncated ? "output_truncated"
                : diagnostic.Bytes.Length != 0 ? "stderr_output" : null;
            return new(version.ToString(), _clock.GetUtcNow(), advisory, resolved.Fingerprint);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
                                     or InvalidOperationException)
        { return Unknown("executable_missing", resolved.Fingerprint); }
        finally
        {
            if (!retained)
            {
                if (process is not null)
                {
                    var owned = completion ?? process.WaitForExitAsync();
                    using var finalCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2), _clock);
                    try
                    {
                        if (!KillTree(process)) throw new IOException("Cleanup is unconfirmed.");
                        await owned.WaitAsync(finalCleanup.Token);
                    }
                    catch (Exception)
                    {
                        lock (_gate) _cleanup.Add((process, owned, scratch!));
                        retained = true;
                    }
                    if (!retained) process.Dispose();
                }
                if (!retained && scratch is not null) TryDeleteScratch(scratch);
            }
            _children.Release();
        }
    }

    internal async Task ReapAsync()
    {
        (Process Process, Task Completion, string Scratch)[] owned;
        lock (_gate) owned = _cleanup.ToArray();
        foreach (var child in owned)
        {
            KillTree(child.Process);
            if (!child.Completion.IsCompleted) continue;
            try { await child.Completion; } catch (Exception) { }
            child.Process.Dispose();
            TryDeleteScratch(child.Scratch);
            lock (_gate) _cleanup.Remove(child);
        }
    }

    private Resolved Resolve(RunnerCodexCliProbeRequest request)
    {
        foreach (var input in new[] { request.Executable, request.ResolutionCwd, request.Path, request.PathExt, request.CodexJsPrefix })
            if (input is not null && (input.Length > DescriptorFieldLimit || input.Contains('\0')
                || input.Contains("${secret:", StringComparison.OrdinalIgnoreCase)
                || input.Contains("{{key:", StringComparison.OrdinalIgnoreCase)))
                throw new ProbeRefusal("launcher_unverified");
        if (string.IsNullOrWhiteSpace(request.Executable)) throw new ProbeRefusal("launcher_unverified");
        var cwd = request.ResolutionCwd ?? Environment.CurrentDirectory;
        if (!Directory.Exists(cwd) && !Path.IsPathRooted(request.Executable))
            throw new ProbeRefusal("launcher_unverified");
        cwd = Path.GetFullPath(cwd);
        var path = request.Path ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathExt = request.PathExt ?? Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD";
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["PATH"] = path, ["PATHEXT"] = pathExt };
        var exe = ResolveExecutable(request.Executable, cwd, path, env)
            ?? throw new ProbeRefusal("executable_missing");
        string[] args = ["--version"];
        if (OperatingSystem.IsWindows())
        {
            if (Path.GetExtension(exe).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                if (!CodexWindowsLaunchPolicy.IsStockNpmCodexShimText(Encoding.UTF8.GetString(_read(exe, 8192))))
                    throw new ProbeRefusal("launcher_unverified");
            }
            var normalized = CodexWindowsLaunchPolicy.Apply(new RunnerLaunchRequest(
                Guid.NewGuid(), exe, request.CodexJsPrefix is null ? args : [request.CodexJsPrefix, "--version"], env, cwd,
                Cols: 120, Rows: 30, TranscriptFormat: TranscriptFormats.Codex), false);
            exe = ResolveExecutable(normalized.Exe, cwd, path, env) ?? throw new ProbeRefusal("executable_missing");
            args = normalized.Args.ToArray();
            if (args.Length == 2) args[0] = Path.GetFullPath(args[0], cwd);
        }
        else if (request.CodexJsPrefix is not null)
            throw new ProbeRefusal("launcher_unverified");
        // Native format, not a basename, distinguishes trusted installed native launchers from shell wrappers.
        var header = _read(exe, 4);
        var native = OperatingSystem.IsWindows() ? header.Length >= 2 && header[0] == 'M' && header[1] == 'Z'
            : header.SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' });
        if (!native) throw new ProbeRefusal("launcher_unverified");
        var identity = new List<string> { _boot.ToString("D"), request.Executable, cwd, path, pathExt, FileIdentity(exe) };
        foreach (var prefix in args.Take(args.Length - 1)) identity.Add(FileIdentity(prefix));
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0", identity))));
        return new(exe, args, fingerprint);
    }

    private static string? ResolveExecutable(string selector, string cwd, string path, IDictionary<string, string> env)
    {
        if (OperatingSystem.IsWindows())
        {
            var resolved = WindowsCommandLine.ResolveExecutable(selector, cwd, env);
            return File.Exists(resolved) ? Path.GetFullPath(resolved) : null;
        }
        var candidates = Path.IsPathRooted(selector) ? new[] { selector }
            : selector.Contains('/') ? new[] { Path.GetFullPath(selector, cwd) }
            : path.Split(Path.PathSeparator).Select(dir => Path.Combine(Path.GetFullPath(string.IsNullOrEmpty(dir) ? cwd : dir, cwd), selector));
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            var target = new FileInfo(candidate).ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName ?? Path.GetFullPath(candidate);
        }
        return null;
    }

    private static string FileIdentity(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new ProbeRefusal("executable_missing");
        return file.FullName + "\0" + file.Length + "\0" + file.LastWriteTimeUtc.Ticks;
    }

    private RunnerCodexCliVersionDto Unknown(string error, string? fingerprint = null) => new(null, _clock.GetUtcNow(), error, fingerprint);

    private static async Task<(byte[] Bytes, bool Truncated)> CaptureAsync(Stream stream)
    {
        using var result = new MemoryStream(OutputByteLimit);
        var buffer = new byte[1024];
        var truncated = false;
        int read;
        while ((read = await stream.ReadAsync(buffer)) != 0)
        {
            var allowed = Math.Min(read, OutputByteLimit - (int)result.Length);
            result.Write(buffer, 0, allowed);
            if (allowed < read) truncated = true;
        }
        return (result.ToArray(), truncated);
    }

    private static byte[] ReadPrefix(string path, int limit)
    {
        using var input = File.OpenRead(path);
        var bytes = new byte[(int)Math.Min(input.Length, limit)];
        input.ReadExactly(bytes);
        return bytes;
    }

    private static bool KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return false; }
    }

    private static void TryDeleteScratch(string path)
    {
        try { Directory.Delete(path, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        lock (_gate)
            foreach (var child in _cleanup) KillTree(child.Process);
    }

    private sealed record Resolved(string Executable, string[] Args, string Fingerprint);
    private sealed class ProbeRefusal(string reason) : Exception { public string Reason { get; } = reason; }
}

/// <summary>Same production POST mapping is used by random-port isolated test hosts.</summary>
public static class CodexCliVersionRoutes
{
    public static async Task PrepareAdvertisementAsync(CodexCliVersionProbe probe,
        IPhoneHomeAdoptionGate adoption, CancellationToken ct)
    {
        await probe.RefreshDefaultAsync(ct);
        adoption.SignalReady();
    }

    public static IEndpointRouteBuilder MapCodexCliVersionRoutes(this IEndpointRouteBuilder app)
    {
        app.MapPost("/capabilities/codex-cli-version", async (
            RunnerCodexCliProbeRequest request, CodexCliVersionProbe probe, HttpContext context) =>
            Results.Ok(await probe.ProbeAsync(request, false, context.RequestAborted)));
        return app;
    }
}
