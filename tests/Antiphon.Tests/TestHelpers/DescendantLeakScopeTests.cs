using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Hang task d268c50c: the leak check catches the run ae84478d all-group-118 shape on every
/// platform. A root starts a sleeper and exits before it, so the sleeper's parent pid names a
/// dead process and <c>Kill(entireProcessTree: true)</c> on the root reaches nothing. On Windows
/// that is how an msys <c>sleep</c> from a git hook outlived the test. Here the orphan is made
/// on purpose: <c>sh</c> backgrounds <c>sleep</c> on Linux, <c>cmd start /b</c> runs <c>ping</c> on Windows.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)]
public sealed class DescendantLeakScopeTests
{
    [Test]
    public async Task An_orphan_the_tree_kill_cannot_reach_fails_the_leak_check_and_is_killed(CancellationToken ct)
    {
        using var scope = DescendantLeakScope.Create();
        var orphans = await StartOrphanerAsync(scope, ct);
        try
        {
            // Should.ThrowAsync passes a ShouldAssertException through, so catch it here.
            ShouldAssertException? failure = null;
            try { await scope.AssertNoneAliveAsync(TimeSpan.FromMilliseconds(500), "witness"); }
            catch (ShouldAssertException caught) { failure = caught; }
            failure.ShouldNotBeNull("a survivor must fail the leak check");
            failure.Message.ShouldContain("survived the test's cleanup");
            failure.Message.ShouldContain($"pid={orphans[0]}/");
            await WaitGoneAsync(orphans, ct);
            scope.Alive().ShouldBeEmpty("the failed check kills what it found, so the failure leaks nothing");
        }
        finally { KillQuietly(orphans); }
    }

    [Test]
    public async Task Kill_all_reaches_the_orphan_and_the_check_then_passes(CancellationToken ct)
    {
        using var scope = DescendantLeakScope.Create();
        var orphans = await StartOrphanerAsync(scope, ct);
        try
        {
            scope.KillAll();
            await scope.AssertNoneAliveAsync(TimeSpan.FromSeconds(5), "KillAll reaches a member whose parent has exited");
            await WaitGoneAsync(orphans, ct);
        }
        finally { KillQuietly(orphans); }
    }

    [Test]
    public async Task Dispose_kills_a_member_still_running(CancellationToken ct)
    {
        int[] orphans;
        using (var scope = DescendantLeakScope.Create())
            orphans = await StartOrphanerAsync(scope, ct);
        try { await WaitGoneAsync(orphans, ct); }
        finally { KillQuietly(orphans); }
    }

    [Test]
    public async Task A_process_outside_the_scope_is_never_seen_or_killed(CancellationToken ct)
    {
        using var scope = DescendantLeakScope.Create();
        using var unrelated = Process.Start(Sleeper())!;
        try
        {
            scope.Alive().ShouldNotContain(unrelated.Id);
            scope.KillAll();
            await scope.AssertNoneAliveAsync(TimeSpan.FromSeconds(1), "an empty scope has no survivor");
            await Task.Delay(200, ct);
            unrelated.HasExited.ShouldBeFalse("only processes that carry the scope are killed");
        }
        finally
        {
            if (!unrelated.HasExited) unrelated.Kill(entireProcessTree: true);
            await unrelated.WaitForExitAsync(ct);
        }
    }

    /// <summary>Starts a root that orphans one sleeper, waits for the root to exit, and returns
    /// the scope's live members. The root waits briefly before it spawns so Windows can adopt it.</summary>
    private static async Task<int[]> StartOrphanerAsync(DescendantLeakScope scope, CancellationToken ct)
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", "ping -n 2 127.0.0.1 >nul & start /b ping -n 90 127.0.0.1 >nul"])
            : new ProcessStartInfo("/bin/sh", ["-c", "sleep 0.2; sleep 90 >/dev/null 2>&1 & exit 0"]);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        scope.Apply(start);
        using var root = Process.Start(start)!;
        scope.Adopt(root);
        await root.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(15), ct);
        // The root is gone, so Kill(entireProcessTree: true) on it reaches nothing (the C24 cleanup).
        var orphans = scope.Alive().ToArray();
        orphans.ShouldNotBeEmpty("the sleeper outlives its root, outside the root's process tree");
        return orphans;
    }

    private static ProcessStartInfo Sleeper() => OperatingSystem.IsWindows()
        ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"), ["-n", "60", "127.0.0.1"])
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }
        : new ProcessStartInfo("/bin/sleep", ["60"]) { UseShellExecute = false };

    private static async Task WaitGoneAsync(IReadOnlyList<int> pids, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (pids.Any(IsRunning))
        {
            clock.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "every orphan is killed");
            await Task.Delay(50, ct);
        }
    }

    /// <summary>A killed Linux orphan stays a zombie until its new parent reaps it; it runs no code.</summary>
    private static bool IsRunning(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var stat = File.ReadAllText($"/proc/{pid}/stat");
                return stat[(stat.LastIndexOf(')') + 1)..].TrimStart()[0] != 'Z';
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
    }

    private static void KillQuietly(IEnumerable<int> pids)
    {
        foreach (var pid in pids)
        {
            try { using var process = Process.GetProcessById(pid); process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }
}
