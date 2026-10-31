using System.Diagnostics;
using System.Globalization;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>Pure planner and injected executor for the owned git tree kill.</summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class OwnedProcessTreeTests
{
    private static readonly CapturedProcessIdentity Root = new(10, 1_000);

    [Test]
    public async Task C448_ReusedPidWithLaterStartTicksIsNotSignalled()
    {
        var killed = new List<int>();
        var log = new List<string>();
        OwnedProcessTree.Execute(
            () => Table(Row(10, 1, 1_000), Row(20, 10, 2_000)),
            pid => pid == 20 ? 9_000 : 1_000,
            killed.Add,
            () => { },
            Root,
            serverPid: 7,
            ownerPid: 8,
            rootAlreadyExited: false,
            log.Add);
        killed.ShouldNotContain(20);
        killed.ShouldContain(10);
        log.ShouldContain(line => line.Contains("pid=20", StringComparison.Ordinal) && line.Contains("reason=reused", StringComparison.Ordinal));
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_StaleParentPidExcludesTheChildAndItsSubtree()
    {
        var plan = OwnedProcessTree.Plan(
            [
                Row(10, 1, 1_000),
                Row(20, 10, 500),
                Row(30, 20, 1_500),
                Row(21, 10, 1_000),
                Row(22, 21, 1_100),
                Row(23, 21, 900),
            ],
            Root,
            serverPid: 7,
            ownerPid: 8);
        plan.BlockReason.ShouldBeNull();
        Descendants(plan).ShouldBe(new[] { 21, 22 });
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_ServerAndOwnerPidsAreExcluded()
    {
        var plan = OwnedProcessTree.Plan(
            [
                Row(10, 1, 1_000),
                Row(50, 10, 1_100),
                Row(51, 50, 1_200),
                Row(60, 10, 1_100),
                Row(61, 60, 1_200),
                Row(70, 10, 1_100),
            ],
            Root,
            serverPid: 50,
            ownerPid: 60);
        plan.BlockReason.ShouldBeNull();
        Descendants(plan).ShouldBe(new[] { 70 });
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_UnreadableStartTimeStopsTheWalk()
    {
        var plan = OwnedProcessTree.Plan(
            [
                Row(10, 1, 1_000),
                new(20, 10, null),
                Row(30, 20, 1_500),
                Row(40, 10, 1_200),
            ],
            Root,
            serverPid: 7,
            ownerPid: 8);
        plan.BlockReason.ShouldBeNull();
        Descendants(plan).ShouldBe(new[] { 40 });
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_SnapshotIsSealedBeforeTheFirstKill()
    {
        var rows = new List<ProcessTableRow> { Row(10, 1, 1_000), Row(20, 10, 1_100) };
        var reads = 0;
        var killed = new List<int>();
        OwnedProcessTree.Execute(
            () =>
            {
                reads++;
                if (reads == 2)
                    rows.Add(Row(30, 20, 1_200));
                return new ProcessSnapshot(rows.ToArray(), null);
            },
            pid => rows.Last(row => row.Pid == pid).StartTicks,
            pid =>
            {
                killed.Add(pid);
                if (rows.All(row => row.Pid != 99))
                    rows.Add(Row(99, 10, 1_300));
            },
            () => { },
            Root,
            serverPid: 7,
            ownerPid: 8,
            rootAlreadyExited: false,
            _ => { });
        reads.ShouldBeGreaterThan(1);
        killed.ShouldBe(new[] { 10, 20, 30 });
        killed.ShouldNotContain(99);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_TreeSizeIsBounded()
    {
        var rows = new List<ProcessTableRow> { Row(10, 1, 1_000) };
        for (var depth = 1; depth <= OwnedProcessTree.MaxDescendants + 8; depth++)
            rows.Add(Row(10 + depth, 9 + depth, 1_000 + depth));
        var plan = OwnedProcessTree.Plan(rows, Root, serverPid: 7, ownerPid: 8);
        plan.BlockReason.ShouldBeNull();
        plan.Kills.Count.ShouldBe(OwnedProcessTree.MaxDescendants);
        plan.Kills.ShouldNotContain(row => row.Pid == 10 + OwnedProcessTree.MaxDescendants);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_RootIdentityMismatchWalksNothing()
    {
        var killed = new List<int>();
        var handleKills = 0;
        var log = new List<string>();
        OwnedProcessTree.Execute(
            () => Table(Row(10, 1, 5_000), Row(20, 10, 5_100), Row(30, 20, 5_200)),
            pid => pid == 10 ? 5_000 : 5_100,
            killed.Add,
            () => handleKills++,
            Root,
            serverPid: 7,
            ownerPid: 8,
            rootAlreadyExited: false,
            log.Add);
        killed.ShouldBeEmpty();
        handleKills.ShouldBe(1);
        log.ShouldContain(line => line.Contains("reason=root_identity_mismatch", StringComparison.Ordinal));
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_LinuxExitedRootIsNotWalked()
    {
        var reads = 0;
        var killed = new List<int>();
        var log = new List<string>();
        OwnedProcessTree.Execute(
            () =>
            {
                reads++;
                return Table(Row(10, 1, 1_000), Row(20, 10, 1_100));
            },
            pid => pid == 10 ? 1_000 : 1_100,
            killed.Add,
            () => killed.Add(-1),
            Root,
            serverPid: 7,
            ownerPid: 8,
            rootAlreadyExited: true,
            log.Add);
        reads.ShouldBe(0);
        killed.ShouldBeEmpty();
        log.ShouldContain(line => line.Contains("reason=root_gone", StringComparison.Ordinal));
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_SnapshotFailureKillsOnlyTheOwnedRoot()
    {
        var killed = new List<int>();
        var handleKills = 0;
        var log = new List<string>();
        OwnedProcessTree.Execute(
            () => ProcessSnapshot.Failure("invalid_handle:win32_6"),
            _ => 1_000,
            killed.Add,
            () => handleKills++,
            Root,
            serverPid: 7,
            ownerPid: 8,
            rootAlreadyExited: false,
            log.Add);
        killed.ShouldBeEmpty();
        handleKills.ShouldBe(1);
        log.ShouldContain(line => line.Contains("repository_child_tree_kill snapshot_failed", StringComparison.Ordinal)
            && line.Contains("pid=10", StringComparison.Ordinal)
            && line.Contains("reason=invalid_handle:win32_6", StringComparison.Ordinal)
            && line.Contains("fallback=owned_root_only", StringComparison.Ordinal)
            && line.Contains("repository_child_tree_kill_snapshot_failed", StringComparison.Ordinal));
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_KillOrderSignalsTheRootFirst()
    {
        var killed = new List<int>();
        OwnedProcessTree.Execute(
            () => Table(Row(10, 1, 1_000), Row(20, 10, 1_100), Row(30, 20, 1_200)),
            pid => pid switch { 10 => 1_000L, 20 => 1_100L, 30 => 1_200L, _ => null },
            killed.Add,
            () => { },
            Root,
            serverPid: 7,
            ownerPid: 8,
            rootAlreadyExited: false,
            _ => { });
        killed.ShouldBe(new[] { 10, 20, 30 });
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_StartAfterTheSnapshotClockIsNotKilled()
    {
        var killed = new List<int>();
        var snapshot = new DateTime(3_000, DateTimeKind.Utc);
        OwnedProcessTree.Execute(
            () => Table(Row(10, 1, 1_000), Row(20, 10, 3_000), Row(30, 10, 3_001), Row(40, 30, 3_002)),
            pid => pid switch
            {
                10 => 1_000L,
                20 => 3_000L,
                30 => 3_001L,
                40 => 3_002L,
                _ => null,
            },
            killed.Add,
            () => { },
            Root,
            serverPid: 7,
            ownerPid: 8,
            rootAlreadyExited: false,
            _ => { },
            () => snapshot);
        killed.ShouldBe(new[] { 10, 20 });
        killed.ShouldNotContain(30);
        killed.ShouldNotContain(40);
        await Task.CompletedTask;
    }

    [Test]
    public async Task C448_TruncatedTreeIsLogged()
    {
        var rows = new List<ProcessTableRow> { Row(10, 1, 1_000) };
        for (var depth = 1; depth <= OwnedProcessTree.MaxDescendants + 8; depth++)
            rows.Add(Row(10 + depth, 9 + depth, 1_000 + depth));
        var log = new List<string>();
        OwnedProcessTree.Execute(
            () => new ProcessSnapshot(rows, null),
            pid => rows.Last(row => row.Pid == pid).StartTicks,
            _ => { },
            () => { },
            Root,
            serverPid: 7,
            ownerPid: 8,
            rootAlreadyExited: false,
            log.Add);
        log.ShouldContain(line => line.Contains("truncated=true count=63", StringComparison.Ordinal));

        log.Clear();
        OwnedProcessTree.Execute(
            () => Table(Row(10, 1, 1_000), Row(20, 10, 1_100)),
            pid => pid == 10 ? 1_000L : 1_100L,
            _ => { },
            () => { },
            Root,
            serverPid: 7,
            ownerPid: 8,
            rootAlreadyExited: false,
            log.Add);
        log.ShouldNotContain(line => line.Contains("truncated=", StringComparison.Ordinal));
        await Task.CompletedTask;
    }

    /// <summary>The production fallback must signal only the owned child.
    /// <c>Kill(entireProcessTree: true)</c> also kills the grandchild.</summary>
    [Test]
    [Timeout(30_000)]
    public async Task C448_SnapshotFailureKillsTheChildAndLeavesTheGrandchild(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("Live Linux grandchild. Native rows remain C448_V28_CancellationAwaitsOwnedGitBeforeReleasingExclusion and C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal.");
            return;
        }

        var start = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("sleep 120 >/dev/null 2>&1 & echo $!; wait");
        using var child = Process.Start(start);
        child.ShouldNotBeNull();
        var grandPid = 0;
        var grandTicks = 0L;
        try
        {
            var read = child.StandardOutput.ReadLineAsync(ct).AsTask();
            var finished = await Task.WhenAny(read, Task.Delay(5_000, ct));
            (finished == read).ShouldBeTrue("the grandchild pid was not printed");
            grandPid = int.Parse((await read)!.Trim(), CultureInfo.InvariantCulture);
            grandPid.ShouldBeGreaterThan(1);
            using (var grand = Process.GetProcessById(grandPid))
                RepositoryChildJournal.TryStartTicks(grand, out grandTicks).ShouldBeTrue();

            OwnedProcessTree.Kill(child, () => ProcessSnapshot.Failure("injected"));
            child.WaitForExit(2_000).ShouldBeTrue();

            using var still = Process.GetProcessById(grandPid);
            still.HasExited.ShouldBeFalse();
            RepositoryChildJournal.TryStartTicks(still, out var live).ShouldBeTrue();
            live.ShouldBe(grandTicks);
        }
        finally
        {
            try
            {
                if (child is { HasExited: false })
                    child.Kill(entireProcessTree: false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            KillExactPid(grandPid, grandTicks);
        }
    }

    [Test]
    [Timeout(30_000)]
    public async Task C448_ProductionKillPassesTheServerPid(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("The production server-pid arguments are witnessed by a live Linux kill.");
            return;
        }

        var start = new ProcessStartInfo
        {
            FileName = "/bin/sleep",
            UseShellExecute = false,
        };
        start.ArgumentList.Add("30");
        using var sleep = Process.Start(start);
        sleep.ShouldNotBeNull();
        (int Server, int Owner)? seen = null;
        try
        {
            OwnedProcessTree.Kill(sleep, observeWiredPids: (server, owner) => seen = (server, owner));
            sleep.WaitForExit(2_000).ShouldBeTrue();
            seen.ShouldNotBeNull();
            seen.Value.Server.ShouldBe(Environment.ProcessId);
            seen.Value.Owner.ShouldBe(Environment.ProcessId);
        }
        finally
        {
            try
            {
                if (sleep is { HasExited: false })
                    sleep.Kill(entireProcessTree: false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }
        await Task.CompletedTask;
    }

    private static void KillExactPid(int pid, long startTicks)
    {
        if (pid <= 1) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (startTicks != 0)
            {
                if (!RepositoryChildJournal.TryStartTicks(process, out var live) || live != startTicks)
                    return;
            }
            process.Kill(entireProcessTree: false);
            process.WaitForExit(2_000);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static ProcessTableRow Row(int pid, int parent, long ticks) => new(pid, parent, ticks);

    private static ProcessSnapshot Table(params ProcessTableRow[] rows) => new(rows, null);

    private static int[] Descendants(KillPlan plan) => plan.Kills
        .Where(row => row.Reason == "descendant")
        .Select(row => row.Pid)
        .OrderBy(id => id)
        .ToArray();
}
