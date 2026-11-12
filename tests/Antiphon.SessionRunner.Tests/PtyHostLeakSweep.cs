using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using Antiphon.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0206: roots owned by host-spawning fixtures in this assembly.</summary>
public static class TestSessionLogRoot
{
    private static readonly ConcurrentDictionary<string, byte> Roots = new(StringComparer.OrdinalIgnoreCase);

    public static readonly string[] KnownPrefixes =
    [
        "cpu-watchdog-tests", "liveness-tests", "adoption-tests", "bufbounds-tests",
        "backend-seam", "first-write-race", "0180-dto",
        "card0395-adoption", "card0395-write-fault", "card0395-http", "c420-barrier",
        "c502-v25", "c502-v26", "c502-v26-manifest", "c502-v26-herdr", "c502-v27",
        "c514-gen", "c514-seq", "c514-gate", "c514-dest", "c649-prompt",
        "c79-stop", "c628-token", "c710-platform", "c1636-adopt",
        "c1653-adopt", "c1653-launch", "c1653-sock", "c1653-peer",
        "c1778-trust",
    ];

    public static string Create(string prefix)
    {
        if (!KnownPrefixes.Contains(prefix, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(prefix), prefix, "Unknown pty-host test root prefix.");

        var root = Path.Combine(Path.GetTempPath(), $"antiphon-{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Roots.TryAdd(root, 0);
        return root;
    }

    internal static IEnumerable<string> RegisteredRoots => Roots.Keys;
}

public static class TestSessionTeardown
{
    public static async Task KillAndAwaitHostExitAsync(
        SessionRunnerRuntime runtime, Guid sessionId, int? hostPid, DateTime? hostStartedAtUtc = null)
    {
        // CARD-1696: the pid is only the host while its start time is the host's. Without one from
        // the caller it is read before the kill below can let the host exit and the pid be reused.
        var hostStart = hostStartedAtUtc ?? (hostPid is int known ? TryGetStartTimeUtc(known) : null);

        var killed = false;
        try { await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None); killed = true; }
        catch { /* A partially started session still needs the caller's pid fallback. */ }

        if (hostPid is not int pid || hostStart is not DateTime start)
            return;

        // CARD-1682: a session this runtime never owned (a failed adoption) was sent no kill, so
        // its host is killed at once. Waiting here outlasts a 10 s class timeout, which then hides
        // the failing assertion and leaves the host running.
        var until = DateTime.UtcNow + (killed ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
        while (DateTime.UtcNow < until && IsHost(pid, start))
            await Task.Delay(100);

        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited && IsHostStart(process.StartTime.ToUniversalTime(), start))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
        }
        catch { /* Already gone. */ }
    }

    /// <summary>Hang task d268c50c: the leak check after the teardown. A host still alive at its
    /// recorded start outlives the test, and the assembly, and holds the chunk harness open
    /// (run ae84478d session-runner all-group-4 completed-but-hung on a live Antiphon.PtyHost).</summary>
    public static void AssertHostGone(int hostPid, DateTime hostStartedAtUtc) =>
        IsHost(hostPid, hostStartedAtUtc).ShouldBeFalse($"pty-host pid {hostPid} must not outlive the test");

    private static bool IsHost(int pid, DateTime hostStart) =>
        TryGetStartTimeUtc(pid) is DateTime start && IsHostStart(start, hostStart);

    // Linux derives a start time from boot time read at each call, so two reads of one process
    // differ by milliseconds; a reused pid starts later than the host by far more.
    private static bool IsHostStart(DateTime start, DateTime hostStart) =>
        (start - hostStart).Duration() <= TimeSpan.FromSeconds(1);

    private static DateTime? TryGetStartTimeUtc(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return process.HasExited ? null : process.StartTime.ToUniversalTime(); }
        catch { return null; }
    }
}

/// <summary>CARD-0206: final safety net for detached hosts left by this test assembly.</summary>
public class PtyHostLeakSweep
{
    private static readonly DateTime TestProcessStartedAtUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    [After(Assembly)]
    public static Task SweepLeakedPtyHostsAsync()
    {
        if (!OperatingSystem.IsWindows())
            return Task.CompletedTask;

        var swept = new List<(int Pid, string Root, bool ThisRun, bool ChildAlive)>();
        var roots = new HashSet<string>(TestSessionLogRoot.RegisteredRoots, StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcessesByName("Antiphon.PtyHost"))
        {
            try
            {
                var exePath = TryGetExePath(process);
                var root = RootFromHostPath(exePath);
                if (root is null)
                    continue;

                var thisRun = roots.Contains(root);
                var childAlive = HasLiveNonConsoleChild(process.Id);
                var earlierRun = !thisRun
                    && process.StartTime.ToUniversalTime() < TestProcessStartedAtUtc
                    && (!childAlive || DateTime.UtcNow - process.StartTime.ToUniversalTime() > TimeSpan.FromMinutes(30));
                if (!thisRun && !earlierRun)
                    continue;

                roots.Add(root);
                Console.WriteLine($"[CARD-0206] sweeping pty-host pid {process.Id} root {root} ({(thisRun ? "this run" : "earlier run")}, child alive: {childAlive})");
                try { process.Kill(entireProcessTree: true); process.WaitForExit(10_000); } catch { /* Best effort. */ }
                swept.Add((process.Id, root, thisRun, childAlive));
            }
            catch { /* Access denied / exited while scanned. */ }
            finally { process.Dispose(); }
        }

        foreach (var root in roots)
        {
            try { Directory.Delete(root, recursive: true); } catch { /* Best effort after host kill. */ }
        }

        if (swept.Count == 0)
            Console.WriteLine("[CARD-0206] no leaked pty-hosts");
        else
            Console.WriteLine($"[CARD-0206] swept {swept.Count} pty-host(s) this assembly left behind (this run: {swept.Count(x => x.ThisRun)}, earlier runs: {swept.Count(x => !x.ThisRun)})");
        return Task.CompletedTask;
    }

    internal static bool IsOwnedHost(string exePath, DateTime startTimeUtc, bool hasLiveNonConsoleChild, IEnumerable<string> registeredRoots)
    {
        var root = RootFromHostPath(exePath);
        return root is not null && (registeredRoots.Contains(root, StringComparer.OrdinalIgnoreCase)
            || (startTimeUtc < TestProcessStartedAtUtc && (!hasLiveNonConsoleChild || DateTime.UtcNow - startTimeUtc > TimeSpan.FromMinutes(30))));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? TryGetExePath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch
        {
            if (!OperatingSystem.IsWindows())
                return null;
            try
            {
                using var query = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId={process.Id}");
                var commandLine = query.Get().Cast<ManagementObject>().FirstOrDefault()?["CommandLine"]?.ToString();
                var marker = "--manifest-dir \"";
                var start = commandLine?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) ?? -1;
                if (start < 0) return null;
                start += marker.Length;
                var end = commandLine!.IndexOf('"', start);
                return end < 0 ? null : Path.Combine(commandLine[start..end], "..", "bin", "unknown", "Antiphon.PtyHost.exe");
            }
            catch { return null; }
        }
    }

    private static string? RootFromHostPath(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(exePath);
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) return null;
        var match = HostRoot.Match(full);
        return match.Success ? match.Groups[1].Value : null;
    }

    // Hang task d268c50c: one list. A hand-written subset left every later prefix unswept.
    private static readonly System.Text.RegularExpressions.Regex HostRoot = new(
        @"^(.*[\\/]antiphon-(?:"
        + string.Join("|", TestSessionLogRoot.KnownPrefixes.Select(System.Text.RegularExpressions.Regex.Escape))
        + @")-[0-9a-f]{32})[\\/]pty-hosts[\\/]bin[\\/]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool HasLiveNonConsoleChild(int pid)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var query = new ManagementObjectSearcher($"SELECT Name FROM Win32_Process WHERE ParentProcessId={pid}");
            return query.Get().Cast<ManagementObject>().Select(x => x["Name"]?.ToString())
                .Any(name => !string.Equals(name, "conhost.exe", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, "OpenConsole.exe", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }
}

public class PtyHostLeakSweepTests
{
    [Test]
    public void A_host_under_a_registered_root_is_identified_and_a_production_path_is_not()
    {
        var root = TestSessionLogRoot.Create("cpu-watchdog-tests");
        var path = Path.Combine(root, "pty-hosts", "bin", "stamp", "Antiphon.PtyHost.exe");
        PtyHostLeakSweep.IsOwnedHost(path, DateTime.UtcNow, true, [root]).ShouldBeTrue();
        PtyHostLeakSweep.IsOwnedHost(@"C:\logs\antiphon\session-runner\pty-hosts\bin\stamp\Antiphon.PtyHost.exe", DateTime.UtcNow.AddHours(-1), false, [root]).ShouldBeFalse();
    }

    /// <summary>
    /// Hang task d268c50c (run ae84478d, session-runner all-group-4 completed-but-hung on a live
    /// Antiphon.PtyHost): the sweep recognised only the first seven prefixes, so a host left under
    /// any later root (c649-prompt, c1653-*, c514-*, ...) survived the assembly and held the chunk.
    /// </summary>
    [Test]
    public void A_host_under_any_registered_prefix_root_is_identified()
    {
        var roots = new List<string>();
        try
        {
            foreach (var prefix in TestSessionLogRoot.KnownPrefixes)
            {
                var root = TestSessionLogRoot.Create(prefix);
                roots.Add(root);
                var path = Path.Combine(root, "pty-hosts", "bin", "stamp", "Antiphon.PtyHost.exe");
                PtyHostLeakSweep.IsOwnedHost(path, DateTime.UtcNow, true, [root])
                    .ShouldBeTrue($"a host under a registered {prefix} root is this run's and must be swept");
            }
        }
        finally
        {
            foreach (var root in roots)
                try { Directory.Delete(root, recursive: true); } catch (IOException) { /* Best effort. */ }
        }
    }
}

/// <summary>
/// CARD-1682: a host the runtime never adopted has no session to kill, so the teardown must kill it
/// at once. A 15 s poll there outlasted the 10 s timeout of the real-host adoption witnesses: their
/// failure showed as a timeout instead of the assertion, and the host was left running.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(30_000)]
public class TestSessionTeardownTests
{
    [Test]
    public async Task A_never_adopted_host_is_killed_at_once_so_the_assertion_shows()
    {
        var root = TestSessionLogRoot.Create("adoption-tests");
        var runtime = new SessionRunnerRuntime(
            Microsoft.Extensions.Options.Options.Create(new SessionRunnerSettings { SessionLogPath = root }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionRunnerRuntime>.Instance);
        using var host = Process.Start(OperatingSystem.IsWindows()
            ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"), ["-n", "60", "127.0.0.1"])
            : new ProcessStartInfo("/bin/sleep", ["60"]))!;
        try
        {
            var clock = Stopwatch.StartNew();
            Exception? failure = null;
            try
            {
                try { 0.ShouldBe(1, "the adoption assertion"); }
                finally { await TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, Guid.NewGuid(), host.Id); }
            }
            catch (Exception error) { failure = error; }
            clock.Stop();

            failure.ShouldBeOfType<ShouldAssertException>().Message.ShouldContain("the adoption assertion");
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5),
                "a never-adopted host is killed at once, well inside the 10 s timeout of the adoption witnesses");
            host.HasExited.ShouldBeTrue("the teardown leaves no host running");
        }
        finally
        {
            try { host.Kill(entireProcessTree: true); } catch { /* Already gone. */ }
            await runtime.DisposeAsync();
        }
    }

    /// <summary>
    /// CARD-1696: the pid the teardown was given can belong to another process by the time it kills
    /// (the host exited and the pid was reused). A process whose start time is not the host's is
    /// left alone; one within the 1 s read tolerance is still the host and is killed.
    /// </summary>
    [Test]
    [Arguments(-60_000, false)]
    [Arguments(60_000, false)]
    [Arguments(300, true)]
    public async Task The_teardown_kills_the_pid_only_while_it_is_still_the_host(int startOffsetMs, bool killed)
    {
        var root = TestSessionLogRoot.Create("adoption-tests");
        var runtime = new SessionRunnerRuntime(
            Microsoft.Extensions.Options.Options.Create(new SessionRunnerSettings { SessionLogPath = root }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionRunnerRuntime>.Instance);
        using var process = Process.Start(OperatingSystem.IsWindows()
            ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"), ["-n", "60", "127.0.0.1"])
            : new ProcessStartInfo("/bin/sleep", ["60"]))!;
        try
        {
            var hostStartedAtUtc = process.StartTime.ToUniversalTime().AddMilliseconds(startOffsetMs);

            await TestSessionTeardown.KillAndAwaitHostExitAsync(runtime, Guid.NewGuid(), process.Id, hostStartedAtUtc);

            process.WaitForExit(killed ? 5_000 : 500);
            process.HasExited.ShouldBe(killed, killed
                ? "a start time inside the tolerance is the host the test started"
                : "a pid whose start time is not the host's belongs to another process and is not killed");
        }
        finally
        {
            try { process.Kill(entireProcessTree: true); } catch { /* Already gone. */ }
            await runtime.DisposeAsync();
        }
    }
}
