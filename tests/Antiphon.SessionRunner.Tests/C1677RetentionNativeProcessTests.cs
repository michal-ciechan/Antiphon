using System.Diagnostics;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C1677RetentionScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1677 S1 CP-2 (linux-native). The real fail-closed /proc holder scan keeps a tree a live child holds
/// (cwd or open descriptor), removes it once the child is gone, and reports an unreadable proc root as an
/// incomplete scan while the slot pool's probe stays fail-open (S-3). The test process never holds its own
/// fixture (the probe skips its own pid), so each case starts a child (M-17).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
public sealed class C1677RetentionNativeProcessTests
{
    [Test]
    [NativeLinuxOnly("CARD-1677 Linux-only by construction: the retention holder scan reads /proc/*/cwd of a real child")]
    public async Task Cwd_holder_keeps_the_tree(CancellationToken ct)
    {
        // V-20 (G-24).
        using var s = Create();
        var m = s.Mirror();
        var before = Tree(m.Path);
        using var child = Child("sleep", ["30"], m.Path);
        try
        {
            await WaitForHolderAsync(m.Path, ct);
            var response = await Engine(s).RetainAsync(Request(m), ct);
            s.Kept(response, WorkspaceRetentionCodes.LiveProcess, m, before);
            var holder = response.Holders.ShouldNotBeNull().ShouldHaveSingleItem();
            holder.Pid.ShouldBe(child.Id);
            holder.Argv0.ShouldEndWith("sleep");
            holder.Kind.ShouldBe("cwd");
        }
        finally
        {
            Stop(child);
        }
    }

    [Test]
    [NativeLinuxOnly("CARD-1677 Linux-only by construction: the retention holder scan reads /proc/*/fd of a real child")]
    public async Task Open_fd_holder_keeps_the_tree(CancellationToken ct)
    {
        // V-21 (G-24). The child's cwd is outside the tree; only its descriptor is inside.
        using var s = Create();
        var m = s.Mirror();
        Plant(m.Path, "held.log", "held");
        Git(m.Path, "add", "held.log");
        Git(m.Path, "commit", "-q", "-m", "held");
        Git(m.Path, "push", "-q", "origin", "HEAD:refs/heads/" + m.Branch);
        var before = Tree(m.Path);
        using var child = Child("tail", ["-f", m.Path + "/held.log"], s.Root);
        try
        {
            await WaitForHolderAsync(m.Path, ct);
            var response = await Engine(s).RetainAsync(Request(m), ct);
            s.Kept(response, WorkspaceRetentionCodes.LiveProcess, m with { Head = Git(m.Path, "rev-parse", "HEAD").Trim() }, before);
            response.Holders.ShouldNotBeNull().ShouldHaveSingleItem().Kind.ShouldBe("fd");
        }
        finally
        {
            Stop(child);
        }
    }

    [Test]
    [NativeLinuxOnly("CARD-1677 Linux-only by construction: a real /proc scan after the holder child exits")]
    public async Task Tree_is_removed_after_the_holder_exits(CancellationToken ct)
    {
        // V-22. The same tree, once its holder is stopped and reaped, is removed.
        using var s = Create();
        var m = s.Mirror();
        var child = Child("sleep", ["30"], m.Path);
        await WaitForHolderAsync(m.Path, ct);
        (await Engine(s).RetainAsync(Request(m), ct)).Reason.ShouldBe(WorkspaceRetentionCodes.LiveProcess);
        Stop(child);
        child.Dispose();
        var response = await Engine(s).RetainAsync(Request(m), ct);
        response.Verdict.ShouldBe(WorkspaceRetentionCodes.Removed, response.Detail);
        Directory.Exists(m.Path).ShouldBeFalse();
    }

    [Test]
    [NativeLinuxOnly("CARD-1677 Linux-only by construction: an unreadable proc root against the real /proc")]
    public async Task Unreadable_proc_root_is_unknown(CancellationToken ct)
    {
        // V-23 (G-26). Retention's scan fails closed; the slot pool's Holders() keeps its fail-open result.
        using var s = Create();
        var m = s.Mirror();
        var unreadable = new ProcWorkspaceSlotProcessProbe(procRoot: s.Root + "/no-proc");
        unreadable.Scan(m.Path).Complete.ShouldBeFalse();
        var real = new ProcWorkspaceSlotProcessProbe();
        var scan = real.Scan(m.Path);
        scan.Complete.ShouldBeTrue();
        scan.Holders.ShouldBeEmpty();
        unreadable.Holders(m.Path).ShouldBeEmpty("the slot probe stays fail-open");
        await Task.CompletedTask;
    }

    private static RunnerWorkspaceRetention Engine(C1677RetentionScratch s) =>
        new(s.Service(), new ProcWorkspaceSlotProcessProbe(), retiringRoot: s.Retiring);

    private static async Task WaitForHolderAsync(string path, CancellationToken ct)
    {
        var probe = new ProcWorkspaceSlotProcessProbe();
        for (var i = 0; i < 100 && probe.Scan(path).Holders.Count == 0; i++)
            await Task.Delay(50, ct);
    }

    private static Process Child(string file, string[] args, string cwd)
    {
        var psi = new ProcessStartInfo { FileName = file, WorkingDirectory = cwd, UseShellExecute = false };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        return Process.Start(psi)!;
    }

    private static void Stop(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        process.WaitForExit(10_000);
    }
}
