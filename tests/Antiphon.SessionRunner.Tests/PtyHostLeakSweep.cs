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
        SessionRunnerRuntime runtime, Guid sessionId, int? hostPid)
    {
        try { await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(5), CancellationToken.None); }
        catch { /* A partially started session still needs the caller's pid fallback. */ }

        if (hostPid is not int pid)
            return;

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < until && IsAlive(pid))
            await Task.Delay(100);

        if (IsAlive(pid))
        {
            try { Process.GetProcessById(pid).Kill(entireProcessTree: true); }
            catch { /* Already gone. */ }
        }
    }

    private static bool IsAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch { return false; }
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
        var match = System.Text.RegularExpressions.Regex.Match(full, @"^(.*\\antiphon-(?:cpu-watchdog-tests|liveness-tests|adoption-tests|bufbounds-tests|backend-seam|first-write-race|0180-dto)-[0-9a-f]{32})\\pty-hosts\\bin\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

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
}
