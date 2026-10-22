using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0672 V-13 (D-2): the in-memory lease-waiter registry the land yield reads.</summary>
[Category("Unit")]
public sealed class RepositoryLeaseWaitersTests
{
    private static readonly string Common = Path.Combine(Path.GetTempPath(), "c672-repo", ".git");
    private static readonly string Other = Path.Combine(Path.GetTempPath(), "c672-other", ".git");
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Register_snapshot_and_clear()
    {
        var waiters = new RepositoryLeaseWaiters();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        waiters.IsEmpty.ShouldBeTrue();

        waiters.Register(Common, a, RepositoryLeasePurposes.Dispatch, T0);
        waiters.Register(Common, b, RepositoryLeasePurposes.WorktreeSettlement, T0.AddSeconds(5));
        waiters.Register(Common, a, RepositoryLeasePurposes.Dispatch, T0.AddSeconds(10));

        waiters.IsEmpty.ShouldBeFalse();
        waiters.Any(Common).ShouldBeTrue();
        waiters.Any(Other).ShouldBeFalse();
        waiters.Snapshot(Common).ShouldBe([
            new RepositoryLeaseWaiter(a, RepositoryLeasePurposes.Dispatch, T0),
            new RepositoryLeaseWaiter(b, RepositoryLeasePurposes.WorktreeSettlement, T0.AddSeconds(5)),
        ]);
        waiters.Snapshot(Other).ShouldBeEmpty();

        waiters.Clear(a);
        waiters.Snapshot(Common).Select(w => w.TaskId).ShouldBe([b]);
        waiters.Clear(b);
        waiters.Any(Common).ShouldBeFalse();
        waiters.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void A_task_waits_on_one_repository_at_a_time()
    {
        var waiters = new RepositoryLeaseWaiters();
        var a = Guid.NewGuid();
        waiters.Register(Common, a, RepositoryLeasePurposes.Dispatch, T0);
        waiters.Register(Other, a, RepositoryLeasePurposes.Dispatch, T0.AddSeconds(1));

        waiters.Any(Common).ShouldBeFalse();
        waiters.Snapshot(Other).ShouldHaveSingleItem().Since.ShouldBe(T0.AddSeconds(1));
    }

    [Test]
    public void ReconcileDispatch_drops_absent_dispatch_waiters_and_keeps_settlement_waiters()
    {
        var waiters = new RepositoryLeaseWaiters();
        var stillHeld = Guid.NewGuid();
        var dispatched = Guid.NewGuid();
        var elsewhere = Guid.NewGuid();
        var settling = Guid.NewGuid();
        waiters.Register(Common, stillHeld, RepositoryLeasePurposes.Dispatch, T0);
        waiters.Register(Common, dispatched, RepositoryLeasePurposes.Dispatch, T0);
        waiters.Register(Other, elsewhere, RepositoryLeasePurposes.Dispatch, T0);
        waiters.Register(Common, settling, RepositoryLeasePurposes.WorktreeSettlement, T0);

        waiters.ReconcileDispatch(new HashSet<Guid> { stillHeld });

        waiters.Snapshot(Common).Select(w => w.TaskId).OrderBy(id => id)
            .ShouldBe(new[] { stillHeld, settling }.OrderBy(id => id));
        waiters.Any(Other).ShouldBeFalse();

        waiters.ReconcileDispatch(new HashSet<Guid>());
        waiters.Snapshot(Common).ShouldHaveSingleItem().TaskId.ShouldBe(settling);
    }

    [Test]
    public void KeyFor_trims_a_trailing_separator_and_folds_case_only_on_windows()
    {
        RepositoryLeaseWaiters.KeyFor(Common + Path.DirectorySeparatorChar)
            .ShouldBe(RepositoryLeaseWaiters.KeyFor(Common));

        var waiters = new RepositoryLeaseWaiters();
        waiters.Register(Common + Path.DirectorySeparatorChar, Guid.NewGuid(), RepositoryLeasePurposes.Dispatch, T0);
        waiters.Any(Common).ShouldBeTrue();

        var upper = Path.Combine(Path.GetTempPath(), "C672-REPO", ".GIT");
        waiters.Any(upper).ShouldBe(OperatingSystem.IsWindows());
    }

    [Test]
    public void FirstYield_is_stable_per_request_until_EndYield()
    {
        var waiters = new RepositoryLeaseWaiters();
        var request = Guid.NewGuid();
        var other = Guid.NewGuid();

        waiters.FirstYield(request, T0).ShouldBe(T0);
        waiters.FirstYield(request, T0.AddSeconds(30)).ShouldBe(T0);
        waiters.FirstYield(other, T0.AddSeconds(40)).ShouldBe(T0.AddSeconds(40));

        waiters.MarkExhausted(request).ShouldBeTrue();
        waiters.MarkExhausted(request).ShouldBeFalse();

        waiters.EndYield(request);
        waiters.FirstYield(request, T0.AddSeconds(60)).ShouldBe(T0.AddSeconds(60));
        waiters.MarkExhausted(request).ShouldBeTrue();
        waiters.FirstYield(other, T0.AddSeconds(90)).ShouldBe(T0.AddSeconds(40));
    }

    /// <summary>CARD-1427 V-1 (D-1): a registered waiter survives a tick whose only hold is a cut prerequisite, keeping its Since.</summary>
    [Test]
    public void C1427_prerequisite_pending_keeps_a_registered_waiter_and_its_since()
    {
        var waiters = new RepositoryLeaseWaiters();
        var task = Guid.NewGuid();
        waiters.Register(Common, task, RepositoryLeasePurposes.Dispatch, T0, verificationCompatible: true);

        // Inspection-pending tick: not refused the lease, but its prerequisite is pending.
        waiters.ReconcileDispatch(new HashSet<Guid>(), [task], prerequisitePending: new HashSet<Guid> { task });
        waiters.Snapshot(Common).ShouldHaveSingleItem()
            .ShouldBe(new RepositoryLeaseWaiter(task, RepositoryLeasePurposes.Dispatch, T0, VerificationCompatible: true));

        // A later refusal re-registers without resetting Since.
        waiters.Register(Common, task, RepositoryLeasePurposes.Dispatch, T0.AddSeconds(20), verificationCompatible: true);
        waiters.ReconcileDispatch(new HashSet<Guid> { task }, [task]);
        waiters.Snapshot(Common).ShouldHaveSingleItem().Since.ShouldBe(T0);

        // A tick with the task in neither set drops it (dispatched, failed, held for another reason).
        waiters.ReconcileDispatch(new HashSet<Guid>(), [task], prerequisitePending: new HashSet<Guid>());
        waiters.Any(Common).ShouldBeFalse();
        waiters.IsEmpty.ShouldBeTrue();
    }

    /// <summary>CARD-1427 V-2 (D-1): membership in the prerequisite set never registers a waiter; only a lease refusal does.</summary>
    [Test]
    public void C1427_prerequisite_pending_never_registers_a_new_waiter()
    {
        var waiters = new RepositoryLeaseWaiters();
        var unregistered = Guid.NewGuid();

        waiters.ReconcileDispatch(new HashSet<Guid>(), [unregistered], prerequisitePending: new HashSet<Guid> { unregistered });

        waiters.Snapshot(Common).ShouldBeEmpty();
        waiters.IsEmpty.ShouldBeTrue();
    }
}
