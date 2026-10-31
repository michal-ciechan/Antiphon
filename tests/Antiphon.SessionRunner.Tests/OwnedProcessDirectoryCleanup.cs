using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.SessionRunner.Tests;

/// <summary>Keep native process handles alive until the test's executable directory is removable.</summary>
internal static class OwnedProcessDirectoryCleanup
{
    internal static IReadOnlyList<Process> Capture(IEnumerable<TestOwnedPtyHost.Owned> owned)
    {
        var handles = new List<Process>();
        try
        {
            foreach (var record in owned)
            {
                // Pin the already-attributed generation before the runtime can shut it down.
                // These handles only witness exit; control remains with TestOwnedPtyHost.
                if (record.Process.Handle!.HasExited) continue;
                Process handle;
                try { handle = Process.GetProcessById(record.Process.Pid); }
                catch (ArgumentException) { continue; }
                handles.Add(handle);
                try { _ = handle.SafeHandle; }
                catch (InvalidOperationException) when (record.Process.Handle.HasExited) { }
                // The original pinned handle must still be alive after opening this one.
                // That proves the PID was not recycled, without comparing converted wall clocks.
                if (record.Process.Handle.HasExited)
                {
                    handles.Remove(handle);
                    handle.Dispose();
                }
            }
            return handles;
        }
        catch
        {
            foreach (var handle in handles) handle.Dispose();
            throw;
        }
    }

    // Native rerun e960ffeb: after every captured process exited, deleting the staged
    // Antiphon.Agents.Pty.dll was denied, and the directory was gone about one minute later.
    // 75s is that hold plus the last backoff step.
    internal static readonly TimeSpan StagedDeleteBudget = TimeSpan.FromSeconds(75);

    internal static async Task RunAsync(Func<Task> body, Func<Task> stop,
        Func<IReadOnlyList<Process>> capture, Action delete,
        TimeSpan? deleteBudget = null, Action<string>? deleteTrace = null)
    {
        IReadOnlyList<Process> handles = [];
        var captured = false;
        var stopped = false;
        try
        {
            await TestOwnedPtyHost.ScopeAsync(body, async () =>
            {
                await TestOwnedPtyHost.ScopeAsync(
                    () => TestOwnedPtyHost.ScopeAsync(() =>
                    {
                        handles = capture();
                        captured = true;
                        return Task.CompletedTask;
                    }, async () =>
                    {
                        await stop();
                        stopped = true;
                    }),
                    async () =>
                    {
                        if (!captured) return; // Unknown ownership retains the directory.
                        var started = Stopwatch.StartNew();
                        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        var failures = new List<Exception>();
                        foreach (var handle in handles)
                        {
                            try { await handle.WaitForExitAsync(deadline.Token); }
                            catch (Exception ex)
                            {
                                failures.Add(new IOException(
                                    $"owned process did not exit pid={handle.Id} name={handle.ProcessName} elapsed={started.Elapsed} budget=00:00:10; directory retained", ex));
                            }
                        }
                        if (failures.Count > 0) throw new AggregateException(failures);
                        if (!stopped) return; // A cleanup ownership error also retains the directory.
                        if (deleteBudget is { Ticks: <= 0 })
                            throw new ArgumentOutOfRangeException(nameof(deleteBudget));
                        await DeleteWhenReleasedAsync(delete, deleteBudget ?? StagedDeleteBudget, deleteTrace);
                    });
            });
        }
        finally
        {
            foreach (var handle in handles) handle.Dispose();
        }
    }

    private static async Task DeleteWhenReleasedAsync(Action delete, TimeSpan budget, Action<string>? trace)
    {
        var started = Stopwatch.StartNew();
        var attempt = 0;
        while (true)
        {
            try
            {
                delete();
                if (attempt > 0)
                    Report(trace, $"C1022_STAGED_DELETE_RELEASED attempts={attempt + 1} elapsedSeconds={WholeSeconds(started.Elapsed)} budgetSeconds={WholeSeconds(budget)}");
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (IsTransientHold(ex))
            {
                var elapsed = started.Elapsed;
                var holders = DescribeHolders(ex);
                Report(trace, $"C1022_STAGED_DELETE_HELD attempt={attempt} elapsedSeconds={WholeSeconds(elapsed)} budgetSeconds={WholeSeconds(budget)} holders={holders} denied={OneLine(ex.Message)}");
                if (elapsed >= budget)
                    throw new IOException(
                        $"staged host directory remained held after owned exit elapsedSeconds={WholeSeconds(elapsed)} budgetSeconds={WholeSeconds(budget)} holders={holders} denied={OneLine(ex.Message)}",
                        ex);
                var delay = Backoff(attempt);
                var remaining = budget - elapsed;
                if (delay > remaining) delay = remaining;
                attempt++;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay);
            }
        }
    }

    // 250ms, 500ms, 1s, 2s, 4s, 8s, 16s, then 32s. The sum through 32s is 63.75s,
    // inside the 75s budget that covers the measured one-minute hold.
    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(250 * (1 << Math.Min(attempt, 7)));

    private static int WholeSeconds(TimeSpan value) => (int)Math.Floor(value.TotalSeconds);

    private static void Report(Action<string>? trace, string line)
    {
        trace?.Invoke(line);
        Console.WriteLine(line);
    }

    private static bool IsTransientHold(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException) return true;
            if (current is IOException && (current.HResult & 0xFFFF) is 5 or 32 or 33) return true;
        }
        return false;
    }

    private static string DescribeHolders(Exception ex)
    {
        try
        {
            var denied = DeniedPath(ex);
            if (denied is null) return "path=unparsed";
            var full = Path.GetFullPath(denied);
            if (OperatingSystem.IsWindows()) return DescribeWindows(full);
            if (OperatingSystem.IsLinux()) return DescribeLinux(full);
            return "probe=unsupported-platform";
        }
        catch (Exception probe)
        {
            return "probe-failed=" + probe.GetType().Name;
        }
    }

    private static string? DeniedPath(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            var first = message.IndexOf('\'');
            var last = message.LastIndexOf('\'');
            if (first < 0 || last <= first + 1) continue;
            var path = message.Substring(first + 1, last - first - 1);
            if (path.Contains('/') || path.Contains('\\')) return path;
        }
        return null;
    }

    private static string DescribeLinux(string denied)
    {
        var root = Directory.Exists(denied) ? denied : Path.GetDirectoryName(denied) ?? denied;
        var hits = new List<string>();
        ScanProcess(Environment.ProcessId, denied, root, hits);
        if (hits.Count == 0)
        {
            var scanned = 0;
            foreach (var entry in Directory.EnumerateDirectories("/proc"))
            {
                if (++scanned > 2048 || hits.Count >= 4) break;
                var name = Path.GetFileName(entry);
                if (!int.TryParse(name, out var pid) || pid == Environment.ProcessId) continue;
                try { ScanProcess(pid, denied, root, hits); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return hits.Count == 0
            ? "none (no /proc fd or map under the staged path; transient scanner or kernel hold)"
            : string.Join("; ", hits.Take(4));
    }

    private static void ScanProcess(int pid, string denied, string root, List<string> hits)
    {
        if (hits.Count >= 4) return;
        var comm = ReadComm(pid);
        var fdDir = "/proc/" + pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/fd";
        if (Directory.Exists(fdDir))
        {
            var count = 0;
            foreach (var fd in Directory.EnumerateFileSystemEntries(fdDir))
            {
                if (++count > 1024 || hits.Count >= 4) break;
                string? target;
                try { target = ReadLink(fd); }
                catch (IOException) { continue; }
                if (target is null) continue;
                if (target.EndsWith(" (deleted)", StringComparison.Ordinal)) target = target[..^10];
                if (!Held(target, denied, root)) continue;
                hits.Add($"pid={pid} comm={comm} kind=OpenFile path={OneLine(target)}");
            }
        }
        if (hits.Count >= 4) return;
        var maps = "/proc/" + pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/maps";
        if (!File.Exists(maps)) return;
        using var stream = new FileStream(maps, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = 0;
        while (reader.ReadLine() is { } line && lines++ < 4000 && hits.Count < 4)
        {
            var path = MapPath(line);
            if (path is null || !Held(path, denied, root)) continue;
            hits.Add($"pid={pid} comm={comm} kind=MappedFile path={OneLine(path)}");
        }
    }

    private static string ReadComm(int pid)
    {
        try
        {
            var text = File.ReadAllText("/proc/" + pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/comm").Trim();
            return text.Length == 0 ? "unknown" : OneLine(text);
        }
        catch (IOException) { return "unknown"; }
        catch (UnauthorizedAccessException) { return "unknown"; }
    }

    private static string? ReadLink(string path)
    {
        var target = new FileInfo(path).LinkTarget;
        if (!string.IsNullOrEmpty(target)) return target;
        if (!OperatingSystem.IsLinux()) return null;
        var buffer = new byte[4096];
        var read = readlink(path, buffer, buffer.Length);
        if (read <= 0 || read >= buffer.Length) return null;
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static string? MapPath(string line)
    {
        var space = line.LastIndexOf(' ');
        if (space < 0 || space >= line.Length - 1) return null;
        var path = line[(space + 1)..];
        if (!path.StartsWith('/')) return null;
        if (path.EndsWith(" (deleted)", StringComparison.Ordinal)) path = path[..^10];
        return path;
    }

    [SupportedOSPlatform("windows")]
    private static string DescribeWindows(string denied)
    {
        var hits = new List<string>();
        try { AddSelfModules(denied, hits); }
        catch (Exception ex) { hits.Add("probe-failed=modules:" + ex.GetType().Name); }
        try { QueryRestartManager(denied, hits); }
        catch (Exception ex) { hits.Add("probe-failed=restart-manager:" + ex.GetType().Name); }
        var named = hits.Where(hit => hit.StartsWith("pid=", StringComparison.Ordinal)).Take(4).ToArray();
        if (named.Length > 0) return string.Join("; ", named);
        return hits.FirstOrDefault(hit => hit.StartsWith("probe-failed=", StringComparison.Ordinal))
            ?? "none (restart-manager reported no process; transient scanner or kernel image hold)";
    }

    [SupportedOSPlatform("windows")]
    private static void AddSelfModules(string denied, List<string> hits)
    {
        var root = Path.GetDirectoryName(denied) ?? denied;
        using var self = Process.GetCurrentProcess();
        foreach (ProcessModule module in self.Modules)
        {
            var file = module.FileName;
            if (file is null || !Held(file, denied, root)) continue;
            hits.Add($"pid={self.Id} comm={OneLine(self.ProcessName)} kind=LoadedModule path={OneLine(file)}");
            if (hits.Count >= 4) return;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void QueryRestartManager(string denied, List<string> hits)
    {
        var key = new StringBuilder(64);
        if (RmStartSession(out var session, 0, key) != 0)
        {
            hits.Add("probe-failed=RmStartSession");
            return;
        }
        try
        {
            if (RmRegisterResources(session, 1, [denied], 0, IntPtr.Zero, 0, IntPtr.Zero) != 0)
            {
                hits.Add("probe-failed=RmRegisterResources");
                return;
            }
            var count = 0u;
            var status = RmGetList(session, out var needed, ref count, null, out _);
            if (status == 234 && needed is > 0 and <= 64)
            {
                var buffer = new ProcessInfo[needed];
                count = needed;
                status = RmGetList(session, out needed, ref count, buffer, out _);
                if (status == 0)
                {
                    for (var index = 0; index < count && hits.Count < 4; index++)
                    {
                        var name = buffer[index].Application;
                        if (string.IsNullOrWhiteSpace(name)) name = "unknown";
                        hits.Add($"pid={buffer[index].Process.Pid} comm={OneLine(name)} kind=RestartManager path={OneLine(denied)}");
                    }
                    return;
                }
            }
            if (status != 0) hits.Add("probe-failed=RmGetList:" + status.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { _ = RmEndSession(session); }
    }

    private static bool Held(string candidate, string denied, string root)
    {
        string full;
        try { full = Path.GetFullPath(candidate); }
        catch (IOException) { full = candidate; }
        catch (ArgumentException) { full = candidate; }
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(full, denied, comparison)) return true;
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, comparison);
    }

    private static string OneLine(string value)
    {
        var trimmed = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300];
    }

    [SupportedOSPlatform("windows")]
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint session, uint flags, StringBuilder key);

    [SupportedOSPlatform("windows")]
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint session, uint files, string[] names, uint applications, IntPtr processes, uint services, IntPtr serviceNames);

    [SupportedOSPlatform("windows")]
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[]? info, out uint reasons);

    [SupportedOSPlatform("windows")]
    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint session);

    [DllImport("libc", SetLastError = true)]
    private static extern int readlink(string path, byte[] buffer, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct UniqueProcess
    {
        internal int Pid;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Start;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessInfo
    {
        internal UniqueProcess Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Application;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string Service;
        internal int Type;
        internal uint Status;
        internal uint SessionId;
        [MarshalAs(UnmanagedType.Bool)] internal bool Restartable;
    }
}
