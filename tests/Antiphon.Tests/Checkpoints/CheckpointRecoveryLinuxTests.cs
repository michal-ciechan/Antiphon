using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class CheckpointRecoveryLinuxTests : CheckpointTestBase
{
    [Test]
    public async Task native_identity_keeps_live_then_removes_dead()
    {
        if (!Linux()) return;
        var run = Run();
        using var child = Sleep();
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
            var changed = new ProcessIdentityProbe().Capture(child.Id) with { StartUtcTicks = 1 };
            new ProcessIdentityProbe().Observe(changed).Verdict.ShouldBe(ProcessVerdict.ReusedPid);
            await Stop(child);
            cleanup.Remove(run).Outcome.ShouldBe("Removed");
            deletes.ShouldBe(1);
        }
        finally { await Stop(child); }
    }

    [Test]
    public async Task detached_staged_copy_survives_starter_exit()
    {
        if (!Linux()) return;
        var sandbox = TempDir();
        var source = Path.Combine(CheckpointFixtures.RepoRoot, "tools", "Antiphon.Checkpoints", "bin-c804-final");
        if (!Directory.Exists(source)) source = Path.GetDirectoryName(typeof(DetachedLauncher).Assembly.Location)!;
        var staged = Path.Combine(sandbox, "staged");
        ShadowCopy.CopyToolOutput(source, staged);
        var marker = Path.Combine(sandbox, "done");
        var psi = new ProcessStartInfo("setsid")
        {
            ArgumentList = { "dotnet", Path.Combine(staged, "Antiphon.Checkpoints.dll"), "smoke-detach", marker },
            RedirectStandardOutput = true, UseShellExecute = false, WorkingDirectory = sandbox,
        };
        using var starter = Process.Start(psi)!;
        RegisterCheckpointChild(starter);
        var line = await starter.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        line.ShouldStartWith("executor=");
        using var executor = Process.GetProcessById(int.Parse(line![9..]));
        RegisterCheckpointChild(executor);
        try
        {
            NativeKill(-starter.Id, 15).ShouldBe(0);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(marker) && DateTime.UtcNow < deadline) await Task.Delay(50);
            File.Exists(marker).ShouldBeTrue();
            File.Exists(Path.Combine(staged, "Antiphon.Checkpoints.deps.json")).ShouldBeTrue();
        }
        finally { await Stop(executor); await Stop(starter); }
    }

    [Test]
    public async Task done_live_executor_retains_image()
    {
        if (!Linux()) return;
        var run = Run();
        using var child = Sleep();
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
        if (!Linux()) return;
        var run = Run();
        using var starter = Sleep();
        try
        {
            Own(run, starter, "preparing");
            File.WriteAllText(Path.Combine(run, "tool", "partial"), "copy began");
            new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Retained");
            await Stop(starter);
            new ToolCopyCleanup().Remove(run).Outcome.ShouldBe("Removed");
            Directory.Exists(Path.Combine(run, "tool")).ShouldBeFalse();
        }
        finally { await Stop(starter); }
    }

    [Test]
    public async Task executor_death_after_ack_before_progress_recovers()
    {
        if (!Linux()) return;
        var run = Run();
        using var child = Sleep();
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
            Directory.Exists(Path.Combine(run, "tool")).ShouldBeFalse();
        }
        finally { await Stop(child); }
    }

    [Test]
    public async Task dead_test_host_live_nested_executor_retains_root()
    {
        if (!Linux()) return;
        var sandbox = TempDir();
        var root = Root(sandbox, alive: false);
        var run = Path.Combine(root, "results", "nested");
        Directory.CreateDirectory(Path.Combine(run, "tool"));
        using var child = Sleep();
        try
        {
            Own(run, child, "launched");
            ExecutorOwnershipStore.Write(run, new ProcessIdentityProbe().Capture(child.Id));
            var sentinel = Path.Combine(run, "tool", "sentinel");
            File.WriteAllText(sentinel, "keep");
            var sweep = Sweep(sandbox);
            sweep.SweepOnce().CompletedRoots.ShouldBe(0);
            File.ReadAllText(sentinel).ShouldBe("keep");
            await Stop(child);
            sweep.SweepOnce().CompletedRoots.ShouldBe(1);
            Directory.Exists(root).ShouldBeFalse();
        }
        finally { await Stop(child); }
    }

    [Test]
    public async Task other_worktree_live_owner_survives_sweep()
    {
        if (!Linux()) return;
        var sandbox = TempDir();
        var root = Root(sandbox, alive: true);
        using var child = Sleep();
        try
        {
            var run = Path.Combine(root, "other-worktree", "run");
            Directory.CreateDirectory(Path.Combine(run, "tool"));
            Own(run, child, "launched");
            var sentinel = Path.Combine(run, "tool", "live-owner");
            File.WriteAllText(sentinel, "keep");
            Sweep(sandbox).SweepOnce().CompletedRoots.ShouldBe(0);
            File.ReadAllText(sentinel).ShouldBe("keep");
            new ProcessIdentityProbe().Observe(new ProcessIdentityProbe().Capture(child.Id)).Verdict
                .ShouldBe(ProcessVerdict.AliveSame);
        }
        finally { await Stop(child); }
    }

    [Test]
    public async Task concurrent_sweepers_cannot_delete_new_registration()
    {
        if (!Linux()) return;
        var sandbox = TempDir();
        var old = Root(sandbox, alive: false);
        var first = Sweep(sandbox);
        var second = Sweep(sandbox);
        var live = Root(sandbox, alive: true);
        var sentinel = Path.Combine(live, "sentinel");
        File.WriteAllText(sentinel, "live");
        var a = Task.Run(() => first.SweepOnce());
        var b = Task.Run(() => second.SweepOnce());
        RegisterCheckpointWork(a);
        RegisterCheckpointWork(b);
        await Task.WhenAll(a, b);
        Directory.Exists(old).ShouldBeFalse();
        File.ReadAllText(sentinel).ShouldBe("live");
        (a.Result.CompletedRoots + b.Result.CompletedRoots).ShouldBe(1);
    }

    [Test]
    public void native_links_keep_targets_unchanged()
    {
        if (!Linux()) return;
        var sandbox = TempDir();
        var target = Path.Combine(sandbox, "target");
        Directory.CreateDirectory(target);
        var data = Path.Combine(target, "sentinel");
        File.WriteAllBytes(data, RandomNumberGenerator.GetBytes(1024));
        var hash = SHA256.HashData(File.ReadAllBytes(data));
        var root = Root(sandbox, alive: false);
        Directory.CreateSymbolicLink(Path.Combine(root, "linked"), target);
            Sweep(sandbox).SweepOnce().CompletedRoots.ShouldBe(0);
            SHA256.HashData(File.ReadAllBytes(data)).ShouldBe(hash);
            Directory.Exists(root).ShouldBeTrue();
            Directory.Delete(Path.Combine(root, "linked"));
    }

    [Test]
    public async Task launch_unknown_survives_until_late_ack()
    {
        if (!Linux()) return;
        var run = Run();
        using var child = Sleep();
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

    private static bool Linux()
    {
        if (OperatingSystem.IsLinux()) return true;
        Skip.Test("native Linux custody proof requires Linux");
        return false;
    }

    private Process Sleep()
    {
        var child = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false })!;
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
        var path = Path.Combine(TempDir(), "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "tool"));
        return path;
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
            AttemptId = Guid.NewGuid().ToString("N"), AssemblyInvocationId = "native-linux",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            Owner = alive ? owner : owner with { Pid = int.MaxValue, StartUtcTicks = 1 },
        });
        Sweep(sandbox).Register(root);
        return root;
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
    private static extern int NativeKill(int pid, int signal);
}
