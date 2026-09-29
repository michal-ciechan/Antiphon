using System.Diagnostics;
using System.Security.Cryptography;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointRecoveryWindowsTests : CheckpointTestBase
{
    [Test]
    public async Task native_identity_keeps_live_then_removes_dead()
    {
        if (!Windows()) return;
        var run = Run();
        using var child = Hold();
        try
        {
            Own(run, child, "launched");
            var sentinel = Path.Combine(run, "tool", "sentinel");
            File.WriteAllText(sentinel, "live");
            var deletes = 0;
            var cleanup = new ToolCopyCleanup(beforeDelete: _ => deletes++);
            cleanup.Remove(run).Outcome.ShouldBe("Retained");
            deletes.ShouldBe(0);
            File.ReadAllText(sentinel).ShouldBe("live");
            var reused = new ProcessIdentityProbe().Capture(child.Id) with { StartUtcTicks = 1 };
            new ProcessIdentityProbe().Observe(reused).Verdict.ShouldBe(ProcessVerdict.ReusedPid);
            await Stop(child);
            cleanup.Remove(run).Outcome.ShouldBe("Removed");
        }
        finally { await Stop(child); }
    }

    [Test]
    public async Task detached_staged_copy_survives_starter_exit()
    {
        if (!Windows()) return;
        var sandbox = TempDir();
        var source = Path.Combine(CheckpointFixtures.RepoRoot, "tools", "Antiphon.Checkpoints", "bin-c804-windows");
        if (!Directory.Exists(source)) source = Path.GetDirectoryName(typeof(DetachedLauncher).Assembly.Location)!;
        var staged = Path.Combine(sandbox, "staged");
        ShadowCopy.CopyToolOutput(source, staged);
        var pid = new DetachedLauncher(new FakePlatform { IsWindows = true }).Start(new LaunchRequest(
            "dotnet", [Path.Combine(staged, "Antiphon.Checkpoints.dll"), "hold"], sandbox));
        using var child = Process.GetProcessById(pid);
        RegisterCheckpointChild(child);
        try
        {
            await Task.Delay(200);
            new ProcessIdentityProbe().Observe(new ProcessIdentityProbe().Capture(pid)).Verdict
                .ShouldBe(ProcessVerdict.AliveSame);
            File.Exists(Path.Combine(staged, "Antiphon.Checkpoints.deps.json")).ShouldBeTrue();
        }
        finally { await Stop(child); }
    }

    [Test]
    public async Task done_live_executor_retains_image()
    {
        if (!Windows()) return;
        var run = Run();
        using var child = Hold();
        try
        {
            Own(run, child, "launched");
            ExecutorOwnershipStore.Write(run, new ProcessIdentityProbe().Capture(child.Id));
            new RunStateStore().Write(Path.Combine(run, "state.json"), new RunState
            { RunId = Path.GetFileName(run), Phase = "done", ExecutorPid = child.Id, ExitCode = 0 });
            (await new WaitCommand().WaitAsync(run, TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(1),
                new StringWriter(), CancellationToken.None)).ShouldBe(ExitCodes.StillRunning);
            Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
            await Stop(child);
            (await new WaitCommand().WaitAsync(run, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
                new StringWriter(), CancellationToken.None)).ShouldBe(0);
            Directory.Exists(Path.Combine(run, "tool")).ShouldBeFalse();
        }
        finally { await Stop(child); }
    }

    [Test]
    public async Task starter_death_before_launch_reclaims_partial_copy()
    {
        if (!Windows()) return;
        var run = Run();
        using var starter = Hold();
        try
        {
            Own(run, starter, "preparing");
            File.WriteAllText(Path.Combine(run, "tool", "partial"), "copy began");
            new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Retained");
            await Stop(starter);
            new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Removed");
        }
        finally { await Stop(starter); }
    }

    [Test]
    public async Task executor_death_after_ack_before_progress_recovers()
    {
        if (!Windows()) return;
        var run = Run();
        using var child = Hold();
        try
        {
            Own(run, child, "launch-attempted");
            ExecutorOwnershipStore.Write(run, new ProcessIdentityProbe().Capture(child.Id));
            var report = Path.Combine(run, "report.md");
            File.WriteAllText(report, "crash evidence");
            File.WriteAllText(Path.Combine(run, "executor.log"), "native crash marker");
            await Stop(child);
            var output = new StringWriter();
            (await new WaitCommand().WaitAsync(run, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1),
                output, CancellationToken.None)).ShouldBe(ExitCodes.ExecutorCrashed);
            output.ToString().ShouldContain("native crash marker");
            File.ReadAllText(report).ShouldBe("crash evidence");
        }
        finally { await Stop(child); }
    }

    [Test]
    public async Task dead_test_host_live_nested_executor_retains_root()
    {
        if (!Windows()) return;
        var sandbox = TempDir();
        var root = Root(sandbox, alive: false);
        var run = Path.Combine(root, "nested", "run");
        Directory.CreateDirectory(Path.Combine(run, "tool"));
        using var child = Hold();
        try
        {
            Own(run, child, "launched");
            ExecutorOwnershipStore.Write(run, new ProcessIdentityProbe().Capture(child.Id));
            var sentinel = Path.Combine(run, "tool", "sentinel");
            File.WriteAllText(sentinel, "keep");
            Sweep(sandbox).SweepOnce().CompletedRoots.ShouldBe(0);
            File.ReadAllText(sentinel).ShouldBe("keep");
            await Stop(child);
            Sweep(sandbox).SweepOnce().CompletedRoots.ShouldBe(1);
        }
        finally { await Stop(child); }
    }

    [Test]
    public async Task other_worktree_live_owner_survives_sweep()
    {
        if (!Windows()) return;
        var sandbox = TempDir();
        var root = Root(sandbox, alive: true);
        using var child = Hold();
        try
        {
            var run = Path.Combine(root, "other-worktree", "run");
            Directory.CreateDirectory(Path.Combine(run, "tool"));
            Own(run, child, "launched");
            var sentinel = Path.Combine(run, "tool", "sentinel");
            File.WriteAllText(sentinel, "live");
            Sweep(sandbox).SweepOnce().CompletedRoots.ShouldBe(0);
            File.ReadAllText(sentinel).ShouldBe("live");
        }
        finally { await Stop(child); }
    }

    [Test]
    public async Task concurrent_sweepers_cannot_delete_new_registration()
    {
        if (!Windows()) return;
        var sandbox = TempDir();
        var dead = Root(sandbox, alive: false);
        var live = Root(sandbox, alive: true);
        var sentinel = Path.Combine(live, "sentinel");
        File.WriteAllText(sentinel, "live");
        var a = Task.Run(() => Sweep(sandbox).SweepOnce());
        var b = Task.Run(() => Sweep(sandbox).SweepOnce());
        RegisterCheckpointWork(a);
        RegisterCheckpointWork(b);
        await Task.WhenAll(a, b);
        Directory.Exists(dead).ShouldBeFalse();
        File.ReadAllText(sentinel).ShouldBe("live");
        (a.Result.CompletedRoots + b.Result.CompletedRoots).ShouldBe(1);
    }

    [Test]
    public void native_links_keep_targets_unchanged()
    {
        if (!Windows()) return;
        var sandbox = TempDir();
        var target = Path.Combine(sandbox, "target");
        Directory.CreateDirectory(target);
        var data = Path.Combine(target, "sentinel");
        File.WriteAllBytes(data, RandomNumberGenerator.GetBytes(1024));
        var hash = SHA256.HashData(File.ReadAllBytes(data));
        var root = Root(sandbox, alive: false);
        var link = Path.Combine(root, "linked");
        Directory.CreateSymbolicLink(link, target);
        Sweep(sandbox).SweepOnce().CompletedRoots.ShouldBe(0);
        SHA256.HashData(File.ReadAllBytes(data)).ShouldBe(hash);
        Directory.Delete(link);
    }

    [Test]
    public async Task launch_unknown_survives_until_late_ack()
    {
        if (!Windows()) return;
        var run = Run();
        using var child = Hold();
        try
        {
            Own(run, child, "launch-attempted", includeLaunch: false);
            var sentinel = Path.Combine(run, "tool", "sentinel");
            File.WriteAllText(sentinel, "unknown");
            new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Retained");
            File.ReadAllText(sentinel).ShouldBe("unknown");
            ExecutorOwnershipStore.Write(run, new ProcessIdentityProbe().Capture(child.Id));
            new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Retained");
            await Stop(child);
            new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Removed");
        }
        finally { await Stop(child); }
    }

    [Test]
    public void locked_tool_delete_is_reported_then_retried()
    {
        if (!Windows()) return;
        var run = Run();
        CheckpointFixtures.MarkRun(run, alive: false);
        var path = Path.Combine(run, "tool", "locked.bin");
        File.WriteAllText(path, "lock");
        using (var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Failed");
            Directory.Exists(Path.Combine(run, "tool")).ShouldBeTrue();
        }
        new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Removed");
    }

    private static bool Windows()
    {
        if (OperatingSystem.IsWindows()) return true;
        Skip.Test("native Windows custody proof requires Windows");
        return false;
    }

    private Process Hold()
    {
        var child = Process.Start(new ProcessStartInfo("cmd", "/c ping -n 30 127.0.0.1 >nul")
        { UseShellExecute = false, CreateNoWindow = true })!;
        RegisterCheckpointChild(child);
        return child;
    }

    private static async Task Stop(Process child)
    {
        try
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (InvalidOperationException) { }
    }

    private string Run()
    {
        var run = Path.Combine(TempDir(), "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(run, "tool"));
        return run;
    }

    private static void Own(string run, Process child, string phase, bool includeLaunch = true)
    {
        var probe = new ProcessIdentityProbe();
        RunOwnershipStore.Write(run, new RunOwnership
        {
            RunId = Path.GetFileName(run), RunDirectory = Path.GetFullPath(run),
            Phase = phase, Starter = phase == "preparing" ? probe.Capture(child.Id) : probe.Current(),
            Launched = includeLaunch ? probe.Capture(child.Id) : null,
        });
    }

    private static CheckpointTempRootSweep Sweep(string sandbox) => new(sandbox,
        options: new CheckpointSweepOptions { Grace = TimeSpan.Zero, Interval = TimeSpan.Zero });

    private static string Root(string sandbox, bool alive)
    {
        var root = Path.Combine(sandbox, "c723-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var owner = new ProcessIdentityProbe().Current();
        TestRootGuard.Write(root, new CheckpointRootMarker
        {
            RootId = Path.GetFileName(root)[5..], RootPath = root,
            AttemptId = Guid.NewGuid().ToString("N"), AssemblyInvocationId = "native-windows",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Owner = alive ? owner : owner with { Pid = int.MaxValue, StartUtcTicks = 1 },
        });
        Sweep(sandbox).Register(root);
        return root;
    }
}
