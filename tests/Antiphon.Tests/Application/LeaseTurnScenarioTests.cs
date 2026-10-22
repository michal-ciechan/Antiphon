using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1427 V-3: back-to-back lands on the repository mutation lease versus one queued worktree
/// cut, with a fake lease and a fake clock. The land model is the admission path of
/// <c>AgentTaskLandService.YieldToDispatchAsync</c> through <see cref="RepositoryLeaseWaiters.DecideYield"/>
/// (D-8); the dispatcher model is the tick's lease refusal, waiter registration and end-of-tick
/// reconcile, with the inspection alternating pending/ready exactly as observed on 2026-10-21
/// (pending on the tick after each refusal, before CARD-1428). Red while the reconcile drops a
/// waiter on an inspection-pending tick: land 2 then finds an empty registry one second after
/// land 1 releases and the cut waits behind all four lands.
/// </summary>
[Category("Unit")]
public sealed class LeaseTurnScenarioTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 21, 0, 0, TimeSpan.Zero);
    private static readonly string Common = Path.Combine(Path.GetTempPath(), "c1427-repo", ".git");
    private const int LandHoldSeconds = 180;
    private const int LandYieldMaxSeconds = 90;
    private const int LandSweepSeconds = 5;
    private const int LandRepickSeconds = 1;
    private const int DispatchTickSeconds = 10;
    private const int CutHoldSeconds = 15;

    [Test]
    public async Task C1427_back_to_back_lands_give_a_waiting_cut_its_turn()
    {
        var clock = new FakeTimeProvider(T0);
        var waiters = new RepositoryLeaseWaiters(
            Options.Create(new DelegationSettings { LandYieldToDispatchMaxSeconds = LandYieldMaxSeconds }), clock);
        var lease = new FakeRepositoryLease();
        var lands = new LandQueueModel(waiters, lease, clock, landCount: 4);
        var cut = new CutModel(waiters, lease, clock, firstTickAt: T0.AddSeconds(5));

        // One second per step; the land queue never sleeps, so it acts before the dispatcher.
        for (var second = 0; second <= 1500; second++)
        {
            await lands.StepAsync();
            await cut.StepAsync();
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        lands.AdmittedAt.Count.ShouldBe(4, "no land starves");
        var registered = cut.RegisteredAt.ShouldNotBeNull("the cut was refused the lease while land 1 held it");
        var acquired = cut.AcquiredAt.ShouldNotBeNull("the cut must get the lease");
        acquired.ShouldBeLessThan(lands.AdmittedAt[1],
            $"the waiting cut is cut before land 2 is admitted (lands admitted at {Describe(lands.AdmittedAt)}; cut registered {registered - T0}, acquired {acquired - T0})");
        acquired.ShouldBeLessThanOrEqualTo(registered.AddSeconds(LandHoldSeconds + LandYieldMaxSeconds),
            "the cut waits at most one land interval plus one yield budget");
        (lands.AdmittedAt[1] - lands.ReleasedAt[0]).ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(LandYieldMaxSeconds),
            "land 2 is admitted at most one yield budget after land 1 releases");
        for (var i = 1; i < 4; i++)
            lands.AdmittedAt[i].ShouldBeGreaterThan(lands.ReleasedAt[i - 1], "one land at a time");
    }

    private static string Describe(IEnumerable<DateTimeOffset> instants) =>
        string.Join(", ", instants.Select(i => (i - T0).TotalSeconds + "s"));

    /// <summary>The single global land reader: the next request is picked 1 s after the previous returns.</summary>
    private sealed class LandQueueModel(RepositoryLeaseWaiters waiters, FakeRepositoryLease lease, FakeTimeProvider clock, int landCount)
    {
        private readonly Queue<Guid> _requests = new(Enumerable.Range(0, landCount).Select(_ => Guid.NewGuid()));
        private Guid _current = Guid.Empty;
        private RepositoryLease? _held;
        private DateTimeOffset _releaseAt;
        private DateTimeOffset _nextPick = clock.GetUtcNow();
        public List<DateTimeOffset> AdmittedAt { get; } = [];
        public List<DateTimeOffset> ReleasedAt { get; } = [];

        public async Task StepAsync()
        {
            var now = clock.GetUtcNow();
            if (_held is not null)
            {
                if (now < _releaseAt) return;
                await _held.DisposeAsync();
                _held = null;
                ReleasedAt.Add(now);
                _nextPick = now.AddSeconds(LandRepickSeconds);
                return;
            }
            if (now < _nextPick) return;
            if (_current == Guid.Empty)
            {
                if (_requests.Count == 0) return;
                _current = _requests.Dequeue();
            }

            // AgentTaskLandService.YieldToDispatchAsync: decide before acquiring; Held re-picks on the sweep.
            var decision = waiters.DecideYield(Common, _current, now, LandYieldMaxSeconds);
            if (decision.Outcome == LandYieldOutcome.Yield)
            {
                _nextPick = now.AddSeconds(LandSweepSeconds);
                return;
            }

            var acquired = await lease.TryAcquireAsync(Common, new RepositoryLeaseOwnerTag(_current, RepositoryLeasePurposes.Land), default);
            if (acquired is null)
            {
                // HoldOnBusyLeaseAsync: Held on the busy lease, re-picked by the sweep.
                _nextPick = now.AddSeconds(LandSweepSeconds);
                return;
            }

            // Admission ends the budget (AgentTaskLandService :591).
            waiters.EndYield(_current);
            _current = Guid.Empty;
            _held = acquired;
            _releaseAt = now.AddSeconds(LandHoldSeconds);
            AdmittedAt.Add(now);
        }
    }

    /// <summary>
    /// One queued worktree task on a dispatcher that ticks every 10 s. A ready inspection plus a
    /// refused lease registers the waiter; the refused attempt consumes the observation, so the next
    /// tick is inspection-pending (CARD-1428's defect, modelled as observed). Every tick ends with
    /// <see cref="RepositoryLeaseWaiters.ReconcileDispatch"/> given the lease-refused set and the
    /// inspection-pending set.
    /// </summary>
    private sealed class CutModel(RepositoryLeaseWaiters waiters, FakeRepositoryLease lease, FakeTimeProvider clock, DateTimeOffset firstTickAt)
    {
        private readonly Guid _task = Guid.NewGuid();
        private DateTimeOffset _nextTick = firstTickAt;
        private bool _inspectionReady = true;
        private RepositoryLease? _held;
        private DateTimeOffset _releaseAt;
        public DateTimeOffset? RegisteredAt { get; private set; }
        public DateTimeOffset? AcquiredAt { get; private set; }

        public async Task StepAsync()
        {
            var now = clock.GetUtcNow();
            if (_held is not null)
            {
                if (now < _releaseAt) return;
                await _held.DisposeAsync();
                _held = null;
                return;
            }
            if (AcquiredAt is not null || now < _nextTick) return;
            _nextTick = now.AddSeconds(DispatchTickSeconds);
            var considered = new List<Guid> { _task };

            if (!_inspectionReady)
            {
                // HoldKind.SiblingInspectionPending: the owned worker completes before the next tick.
                _inspectionReady = true;
                waiters.ReconcileDispatch(new HashSet<Guid>(), considered, prerequisitePending: new HashSet<Guid> { _task });
                return;
            }

            var acquired = await lease.TryAcquireAsync(Common, new RepositoryLeaseOwnerTag(_task, RepositoryLeasePurposes.Dispatch), default);
            if (acquired is null)
            {
                waiters.Register(Common, _task, RepositoryLeasePurposes.Dispatch, now, verificationCompatible: true);
                RegisteredAt ??= now;
                _inspectionReady = false;
                waiters.ReconcileDispatch(new HashSet<Guid> { _task }, considered);
                return;
            }

            AcquiredAt = now;
            _held = acquired;
            _releaseAt = now.AddSeconds(CutHoldSeconds);
            waiters.ReconcileDispatch(new HashSet<Guid>(), considered);
        }
    }
}
