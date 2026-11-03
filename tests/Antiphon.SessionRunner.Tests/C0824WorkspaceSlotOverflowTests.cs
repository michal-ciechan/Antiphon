using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C0824SlotScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0824 S1 CP-7. Overflow growth, its count and disk bounds, and idle retirement (fake clock).</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
[NativeLinuxOnly("CARD-0824 Linux-only by construction: S1 workspace slots are Linux/server2 only")]
public sealed class C0824WorkspaceSlotOverflowTests
{
    [Test]
    public async Task C0824_AllLeasedGrowsOverflowSlot(CancellationToken ct)
    {
        // V-31a. Every base slot leased and none Releasing: the lease grows ws-05 as a normal slot.
        using var scratch = Create();
        var pool = scratch.Pool();
        await scratch.LeaseExtras(pool, 4);
        var overflow = await pool.LeaseAsync(scratch.Extra(5), ct);
        overflow.SlotId.ShouldBe("ws-05");
        Directory.Exists(scratch.Slot("ws-05")).ShouldBeTrue("ws-05 exists");
        var slot = pool.Ledger().Slot("ws-05").ShouldNotBeNull();
        slot.CreatedAs.ShouldBe("overflow");
        slot.CreatedByTask.ShouldBe(ExtraTask(5));
        slot.CreatedAt.ShouldBe(scratch.Clock.GetUtcNow());
        pool.Ledger().Attention.Count(item => item.Key == WorkspaceSlotCodes.Overflow).ShouldBe(1);
        pool.Ledger().Slot("ws-01")!.CreatedAs.ShouldBe("base");
    }

    [Test]
    [Arguments("completes")]
    [Arguments("expires")]
    public async Task C0824_ReleasingSlotWaitsBeforeGrowing(string mode, CancellationToken ct)
    {
        // V-31b. With one slot Releasing the lease waits up to OverflowWaitSeconds before growing.
        using var scratch = Create();
        var pool = scratch.Pool();
        var leases = await scratch.LeaseExtras(pool, 4);
        var holdRelease = new ManualResetEventSlim();
        var releasing = new TaskCompletionSource();
        pool.PhaseCut = phase =>
        {
            if (phase == "before-clean")
            {
                releasing.TrySetResult();
                holdRelease.Wait(TimeSpan.FromSeconds(20));
            }
        };
        var release = Task.Run(() => Release(pool, leases[0], ExtraTask(1)), ct);
        await releasing.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        var waiting = new TaskCompletionSource();
        pool.WaitingForRelease = () => waiting.TrySetResult();
        var lease = Task.Run(() => pool.LeaseAsync(scratch.Extra(5), ct), ct);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        if (mode == "completes")
        {
            holdRelease.Set();
            (await release).Outcome.ShouldBe("released");
            var got = await lease;
            got.SlotId.ShouldBe(leases[0].SlotId);
            Directory.Exists(scratch.Slot("ws-05")).ShouldBeFalse("no ws-05");
        }
        else
        {
            scratch.Clock.Advance(TimeSpan.FromSeconds(10));
            var got = await lease;
            got.SlotId.ShouldBe("ws-05");
            holdRelease.Set();
            await release;
        }
    }

    [Test]
    public async Task C0824_OverflowCapRefuses(CancellationToken ct)
    {
        // V-32a. At target + MaxOverflowSlots the lease refuses overflow_cap and creates nothing.
        using var scratch = Create();
        var pool = scratch.Pool();
        await scratch.LeaseExtras(pool, 6);
        var directories = Directory.EnumerateDirectories(scratch.Worktrees).Count();
        var slots = pool.Ledger().Slots.Count;
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => pool.LeaseAsync(scratch.Extra(7), ct));
        refusal.Message.ShouldBe(WorkspaceSlotCodes.UnavailableDetail(WorkspaceSlotCodes.OverflowCap));
        Directory.EnumerateDirectories(scratch.Worktrees).Count().ShouldBe(directories);
        pool.Ledger().Slots.Count.ShouldBe(slots);
    }

    [Test]
    [Arguments("free_floor")]
    [Arguments("pool_budget_with_quarantine")]
    public async Task C0824_DiskBudgetRefuses(string bound, CancellationToken ct)
    {
        // V-32b. The volume floor and the pool budget (quarantined bytes included) bound overflow creation.
        using var scratch = Create();
        var pool = scratch.Pool();
        var leases = await scratch.LeaseExtras(pool, 4);
        if (bound == "free_floor")
        {
            scratch.Disk.Total = 100 * FakeDisk.GiB;
            scratch.Disk.Free = 25 * FakeDisk.GiB;
        }
        else
        {
            Plant(leases[0].Path, "dirty.txt", "keep me");
            (await Release(pool, leases[0], ExtraTask(1))).Outcome.ShouldBe("quarantined");
            await pool.LeaseAsync(scratch.Extra(5), ct);
            scratch.Disk.DefaultBytes = 10 * FakeDisk.GiB;
            scratch.Disk.Bytes[leases[0].Path] = 22 * FakeDisk.GiB;
        }

        var slots = pool.Ledger().Slots.Count;
        var refusal = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => pool.LeaseAsync(scratch.Extra(6), ct));
        refusal.Message.ShouldBe(WorkspaceSlotCodes.UnavailableDetail(WorkspaceSlotCodes.DiskBudget));
        pool.Ledger().Slots.Count.ShouldBe(slots);
        Directory.Exists(scratch.Slot("ws-05")).ShouldBeFalse();
    }

    [Test]
    public async Task C0824_IdleOverflowRetiredGuarded(CancellationToken ct)
    {
        // V-33a. Free overflow slots idle past IdleRetireHours are removed by git worktree remove, highest first.
        using var scratch = Create();
        var pool = scratch.Pool();
        var leases = await scratch.LeaseExtras(pool, 6);
        await Release(pool, leases[4], ExtraTask(5));
        await Release(pool, leases[5], ExtraTask(6));
        scratch.Clock.Advance(TimeSpan.FromHours(72) + TimeSpan.FromSeconds(1));
        int mark;
        lock (scratch.Argv)
            mark = scratch.Argv.Count;
        await pool.MaintainAsync(ct);
        Directory.Exists(scratch.Slot("ws-05")).ShouldBeFalse();
        Directory.Exists(scratch.Slot("ws-06")).ShouldBeFalse();
        List<string[]> removes;
        lock (scratch.Argv)
            removes = scratch.Argv.Skip(mark).Where(argv => argv.Length > 2 && argv[1] == "worktree" && argv[2] == "remove").ToList();
        removes.Count.ShouldBe(2);
        removes[0].Last().ShouldBe(scratch.Slot("ws-06"));
        removes[1].Last().ShouldBe(scratch.Slot("ws-05"));
        pool.Ledger().Slot("ws-05")!.State.ShouldBe(WorkspaceSlotStates.Retired);
        pool.Ledger().Slots.Count(slot => WorkspaceSlotStates.IsActive(slot.State)).ShouldBe(pool.Target);
    }

    [Test]
    public async Task C0824_LeasedRecentOrQuarantinedNeverRetired(CancellationToken ct)
    {
        // V-33b. A leased slot, a slot idle 71 h, the base pool at target and a quarantined directory survive.
        using var scratch = Create();
        var pool = scratch.Pool();
        var leases = await scratch.LeaseExtras(pool, 5);
        Plant(leases[0].Path, "dirty.txt", "keep");
        (await Release(pool, leases[0], ExtraTask(1))).Outcome.ShouldBe("quarantined");
        await Release(pool, leases[4], ExtraTask(5));
        scratch.Clock.Advance(TimeSpan.FromHours(71));
        await pool.MaintainAsync(ct);
        foreach (var lease in leases)
            Directory.Exists(lease.Path).ShouldBeTrue(lease.Path + " present");
        Directory.Exists(scratch.Slot("ws-01-d2")).ShouldBeTrue();
        pool.Ledger().Slots.Count(slot => slot.State == WorkspaceSlotStates.Retired).ShouldBe(0);

        scratch.Clock.Advance(TimeSpan.FromHours(2));
        await pool.MaintainAsync(ct);
        Directory.Exists(scratch.Slot("ws-05")).ShouldBeFalse("idle overflow retired");
        Directory.Exists(leases[0].Path).ShouldBeTrue("quarantined directory present");
        File.Exists(Path.Combine(leases[0].Path, "dirty.txt")).ShouldBeTrue();
        foreach (var lease in leases.Skip(1).Take(3))
            Directory.Exists(lease.Path).ShouldBeTrue("leased slot present");
        Directory.Exists(scratch.Slot("ws-01-d2")).ShouldBeTrue("base pool at target is kept");
    }

    [Test]
    [Arguments("untracked")]
    [Arguments("unpushed-head")]
    [Arguments("reflog-only")]
    public async Task C0824_IdleOverflowWithLateWorkQuarantinedNotRemoved(string shape, CancellationToken ct)
    {
        // CARD-0824 S1 mutation survivor X37. A Free overflow slot that gained work after its release (V-6b late
        // writer) is past IdleRetireHours: the retirement preflight quarantines it in place. `worktree remove --force`
        // would delete an untracked file and a detached commit alike, so no removal may run.
        using var scratch = Create();
        var pool = scratch.Pool();
        var leases = await scratch.LeaseExtras(pool, 5);
        (await Release(pool, leases[4], ExtraTask(5))).Outcome.ShouldBe("released");
        var path = scratch.Slot("ws-05");
        var parked = Git(path, "rev-parse", "HEAD").Trim();
        string? late = null;
        if (shape == "untracked")
            Plant(path, "x.txt", "late writer");
        else
        {
            Plant(path, "late.txt", "only copy");
            Git(path, "add", "late.txt");
            Git(path, "commit", "-q", "-m", "late commit");
            late = Git(path, "rev-parse", "HEAD").Trim();
            if (shape == "reflog-only")
                Git(path, "checkout", "-q", "--detach", parked);
        }

        var head = Git(path, "rev-parse", "HEAD").Trim();
        var before = Tree(path);
        scratch.Clock.Advance(TimeSpan.FromHours(72) + TimeSpan.FromSeconds(1));
        int mark;
        lock (scratch.Argv)
            mark = scratch.Argv.Count;
        await pool.MaintainAsync(ct);

        List<string[]> removes;
        lock (scratch.Argv)
            removes = scratch.Argv.Skip(mark).Where(argv => argv.Length > 2 && argv[1] == "worktree" && argv[2] == "remove").ToList();
        removes.ShouldBeEmpty("no worktree remove ran on the idle slot");
        Directory.Exists(path).ShouldBeTrue("the idle slot directory stays");
        Tree(path).ShouldBe(before, "the late work is untouched");
        Git(path, "rev-parse", "HEAD").Trim().ShouldBe(head);
        if (late is not null)
            Git(path, "reflog", "show", "--format=%H", "HEAD").ShouldContain(late, Case.Sensitive, "the late commit stays in the slot reflog");
        var entry = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem();
        entry.IsOpen.ShouldBeTrue();
        entry.Path.ShouldBe(path);
        entry.SlotId.ShouldBe("ws-05");
        entry.Class.ShouldBe("custody");
        entry.Reason.ShouldBe(shape switch { "untracked" => "dirty", "unpushed-head" => "unpublished_head", _ => "unpushed" });
        pool.Ledger().Slot("ws-05")!.State.ShouldBe(WorkspaceSlotStates.Retired);
    }

    [Test]
    public async Task C0824_SlotNumberNeverReusedWhileDirectoryExists(CancellationToken ct)
    {
        // V-50. After retiring ws-05 and ws-06 while ws-05's quarantined directory remains, the next overflow is ws-07.
        using var scratch = Create();
        var pool = scratch.Pool();
        var leases = await scratch.LeaseExtras(pool, 6);
        Plant(leases[4].Path, "dirty.txt", "keep");
        (await Release(pool, leases[4], ExtraTask(5))).Outcome.ShouldBe("quarantined");
        await Release(pool, leases[5], ExtraTask(6));
        scratch.Clock.Advance(TimeSpan.FromHours(73));
        await pool.MaintainAsync(ct);
        Directory.Exists(scratch.Slot("ws-06")).ShouldBeFalse();
        Directory.Exists(scratch.Slot("ws-05")).ShouldBeTrue("quarantined ws-05 still exists");
        var next = await pool.LeaseAsync(scratch.Extra(7), ct);
        next.SlotId.ShouldBe("ws-07");
        next.Path.ShouldBe(scratch.Slot("ws-07"));
    }
}
