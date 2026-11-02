using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C0824SlotScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0824 S1 CP-9. Cold resets only when asked or scheduled, and never on a leased slot.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
public sealed class C0824WorkspaceSlotColdResetTests
{
    private static readonly string[] Native = ["src/P/bin/Debug/a.dll", "src/P/obj/project.assets.json", "node_modules/x/i", "client/dist/i.js"];

    private static void PlantNative(string slot)
    {
        foreach (var path in Native)
            Plant(slot, path, "native", OldMtime);
    }

    private static void NativeAbsent(string slot)
    {
        foreach (var path in new[] { "src/P/bin", "src/P/obj", "node_modules", "client/dist" })
            Path.Exists(Path.Combine(slot, path)).ShouldBeFalse(path + " absent");
    }

    private static void NativePresent(string slot)
    {
        foreach (var path in Native)
            File.Exists(Path.Combine(slot, path)).ShouldBeTrue(path + " present");
    }

    [Test]
    public async Task C0824_ColdLeaseWipesNativeOutput(CancellationToken ct)
    {
        // V-28a.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        PlantNative(a.Path);
        await Release(pool, a, TaskA);
        var b = await pool.LeaseAsync(scratch.RequestB(cold: true), ct);
        b.Path.ShouldBe(a.Path);
        NativeAbsent(b.Path);
        pool.Ledger().Slot("ws-01")!.LastResetKind.ShouldBe("cold");
    }

    [Test]
    public async Task C0824_OperatorColdResetFreeSlotWipes(CancellationToken ct)
    {
        // V-28b. Free -> ColdResetting -> Free under the lock; the request is journaled and idempotent.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        PlantNative(a.Path);
        await Release(pool, a, TaskA);
        string? stateDuring = null;
        pool.PhaseCut = phase =>
        {
            if (phase == "cold-resetting")
                stateDuring = pool.Ledger().Slot("ws-01")!.State;
        };
        var requestId = Guid.NewGuid();
        var response = await pool.ColdResetAsync(new PhoneHomeWorkspaceSlotColdResetRequest(requestId, ["ws-01"], false, "operator-x", "stale dlls"), ct);
        response.Slots.ShouldHaveSingleItem().Outcome.ShouldBe("cold_reset");
        stateDuring.ShouldBe(WorkspaceSlotStates.ColdResetting);
        NativeAbsent(a.Path);
        var slot = pool.Ledger().Slot("ws-01")!;
        slot.State.ShouldBe(WorkspaceSlotStates.Free);
        slot.LastColdReset!.RequestId.ShouldBe(requestId.ToString("D"));
        slot.LastColdReset.Requester.ShouldBe("operator-x");
        slot.LastColdReset.Reason.ShouldBe("stale dlls");

        PlantNative(a.Path);
        var repeat = await pool.ColdResetAsync(new PhoneHomeWorkspaceSlotColdResetRequest(requestId, ["ws-01"], false, "operator-x", "stale dlls"), ct);
        repeat.Slots.ShouldHaveSingleItem().Outcome.ShouldBe("duplicate");
        NativePresent(a.Path);
    }

    [Test]
    public async Task C0824_LeaseWithoutColdKeepsNative(CancellationToken ct)
    {
        // V-28c. The control for V-28a.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        PlantNative(a.Path);
        await Release(pool, a, TaskA);
        var b = await pool.LeaseAsync(scratch.RequestB(), ct);
        NativePresent(b.Path);
        pool.Ledger().Slot("ws-01")!.LastResetKind.ShouldBe("warm");
    }

    [Test]
    [Arguments("operator")]
    [Arguments("schedule")]
    public async Task C0824_ColdRequestOnLeasedSlotPendsUntilRelease(string source, CancellationToken ct)
    {
        // V-29. A cold reset on a leased slot only pends; it runs inside the release, before Free.
        using var scratch = Create();
        var pool = scratch.Pool();
        await pool.MaintainAsync(ct);
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        PlantNative(a.Path);
        var before = Tree(a.Path);
        if (source == "operator")
        {
            var response = await pool.ColdResetAsync(new PhoneHomeWorkspaceSlotColdResetRequest(Guid.NewGuid(), null, true, "rc", "rc qualification"), ct);
            response.Slots.ShouldHaveSingleItem().Outcome.ShouldBe("pending");
        }
        else
        {
            scratch.Clock.Advance(TimeSpan.FromDays(6));
            await pool.MaintainAsync(ct);
        }

        Tree(a.Path).ShouldBe(before, "the lessee bin/ present");
        pool.Ledger().Slot("ws-01")!.ColdResetPending.ShouldNotBeNull();
        var nativeAtFree = true;
        pool.PhaseCut = phase =>
        {
            if (phase == "free")
                nativeAtFree = Directory.Exists(Path.Combine(a.Path, "src/P/bin"));
        };
        await Release(pool, a, TaskA);
        nativeAtFree.ShouldBeFalse("no bin/ at the free phase-cut");
        var slot = pool.Ledger().Slot("ws-01")!;
        slot.ColdResetPending.ShouldBeNull();
        slot.LastResetKind.ShouldBe("cold");
    }

    [Test]
    public async Task C0824_WeeklyBoundaryMarksOnce(CancellationToken ct)
    {
        // V-30a. Crossing Sunday 02:00 UTC marks every slot pending exactly once over three ticks.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        await Release(pool, a, TaskA);
        await pool.LeaseAsync(scratch.RequestB(), ct);
        await pool.LeaseAsync(scratch.RequestC(), ct);
        await pool.MaintainAsync(ct);
        pool.Ledger().ScheduledMarks.ShouldBe(0);
        scratch.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 8, 2, 0, 1, TimeSpan.Zero));
        for (var i = 0; i < 3; i++)
            await pool.MaintainAsync(ct);
        var ledger = pool.Ledger();
        ledger.ScheduledMarks.ShouldBe(1, "mark count 1");
        ledger.Slots.ShouldAllBe(slot => slot.ColdResetPending != null);
    }

    [Test]
    public async Task C0824_ScheduleOffMarksNone(CancellationToken ct)
    {
        // V-30b.
        using var scratch = Create();
        var pool = scratch.Pool(Settings(settings => settings.ColdResetSchedule = "Off"));
        await pool.LeaseAsync(scratch.RequestA(), ct);
        await pool.MaintainAsync(ct);
        scratch.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 8, 2, 0, 1, TimeSpan.Zero));
        await pool.MaintainAsync(ct);
        pool.Ledger().ScheduledMarks.ShouldBe(0, "mark count 0");
        pool.Ledger().Slots.ShouldAllBe(slot => slot.ColdResetPending == null);
    }

    [Test]
    public async Task C0824_MissedBoundaryRunsOnceAtStart(CancellationToken ct)
    {
        // V-30c. A runner down across the boundary marks once at its next start, then not again.
        using var scratch = Create();
        var pool = scratch.Pool();
        await pool.LeaseAsync(scratch.RequestA(), ct);
        await pool.MaintainAsync(ct);
        scratch.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero));
        var restarted = scratch.Pool();
        await restarted.StartAsync(ct);
        restarted.Ledger().ScheduledMarks.ShouldBe(1, "mark count 1");
        await restarted.MaintainAsync(ct);
        restarted.Ledger().ScheduledMarks.ShouldBe(1);
    }

    [Test]
    public async Task C0824_ColdResetRemovesDeletedProjectResidue(CancellationToken ct)
    {
        // V-53. A warm reset keeps a deleted project's bin/ (documented residue); a cold reset removes it.
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        File.Exists(Path.Combine(a.Path, "src/Q/Q.csproj")).ShouldBeTrue();
        Plant(a.Path, "src/Q/bin/Q.dll", "stale");
        await Release(pool, a, TaskA);
        var c = await pool.LeaseAsync(scratch.RequestC(), ct);
        File.Exists(Path.Combine(c.Path, "src/Q/Q.csproj")).ShouldBeFalse();
        File.Exists(Path.Combine(c.Path, "src/Q/bin/Q.dll")).ShouldBeTrue("warm residue documented");
        await Release(pool, c, TaskC);
        var b = await pool.LeaseAsync(scratch.RequestB(cold: true), ct);
        File.Exists(Path.Combine(b.Path, "src/Q/bin/Q.dll")).ShouldBeFalse();
    }

    [Test]
    [Arguments("untracked")]
    [Arguments("ignored-evidence")]
    [Arguments("reflog-only-commit")]
    public async Task C0824_ColdResetQuarantinesWhatALeaseWould(string late, CancellationToken ct)
    {
        // D-2 (review 3a354688). The cold reset runs the lease's preflight (ownership, free invariant, unpushed
        // reflog) BEFORE its clean: a Free slot a lease would quarantine is quarantined with every byte in place,
        // keep-list native output included. Only keep-list output is what a cold reset removes (V-28b).
        using var scratch = Create();
        var pool = scratch.Pool();
        var a = await pool.LeaseAsync(scratch.RequestA(), ct);
        PlantNative(a.Path);
        await Release(pool, a, TaskA);
        string? lost = null;
        switch (late)
        {
            case "untracked":
                Plant(a.Path, "late.txt", "late writer");
                break;
            case "ignored-evidence":
                Plant(a.Path, ".antiphon/late-report.md", "late evidence");
                break;
            default:
                Plant(a.Path, "side.txt", "reflog only");
                Git(a.Path, "add", "side.txt");
                Git(a.Path, "commit", "-q", "-m", "late commit");
                lost = Git(a.Path, "rev-parse", "HEAD").Trim();
                Git(a.Path, "checkout", "-q", "--detach", scratch.Base);
                break;
        }

        var before = Tree(a.Path);
        var response = await pool.ColdResetAsync(new PhoneHomeWorkspaceSlotColdResetRequest(Guid.NewGuid(), ["ws-01"], false, "operator-x", "stale dlls"), ct);

        response.Slots.ShouldHaveSingleItem().Outcome.ShouldBe("quarantined");
        Tree(a.Path).ShouldBe(before, "nothing was cleaned");
        NativePresent(a.Path);
        if (lost is not null)
            Git(a.Path, "reflog", "show", "--format=%H", "HEAD").ShouldContain(lost);
        var entry = pool.Ledger().QuarantinedDirectories.ShouldHaveSingleItem();
        entry.Path.ShouldBe(a.Path);
        entry.Reason.ShouldBe(lost is null ? "dirty" : "unpushed");
        pool.Ledger().Slot("ws-01")!.Path.ShouldBe(scratch.Slot("ws-01-d2"));
    }
}
