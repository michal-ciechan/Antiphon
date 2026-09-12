using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class SessionReAdoptionStateTests
{
    [Test]
    public async Task A_refused_reservation_does_not_increment()
    {
        var state = new SessionReAdoptionState();
        await using (var first = await state.TryReserveAsync(Guid.NewGuid(), cap: 1, CancellationToken.None))
        {
            first.Allowed.ShouldBeTrue();
            first.Commit();
        }

        var sessionId = Guid.NewGuid();
        await using (var a = await state.TryReserveAsync(sessionId, 1, CancellationToken.None))
        {
            a.Allowed.ShouldBeTrue();
            a.Commit();
        }

        await using var refused = await state.TryReserveAsync(sessionId, 1, CancellationToken.None);
        refused.Allowed.ShouldBeFalse();
        refused.EscalationEligible.ShouldBeTrue();
        state.CountFor(sessionId).ShouldBe(1);
        state.CountFor(sessionId).ShouldBe(refused.Cap == 1 ? 1 : refused.SuccessfulReAdoptions);
        refused.SuccessfulReAdoptions.ShouldBe(1);
    }

    [Test]
    public async Task A_released_lease_without_commit_does_not_consume_a_slot()
    {
        var state = new SessionReAdoptionState();
        var sessionId = Guid.NewGuid();
        await using (var lease = await state.TryReserveAsync(sessionId, 3, CancellationToken.None))
        {
            lease.Allowed.ShouldBeTrue();
        }

        state.CountFor(sessionId).ShouldBe(0);
        await using var again = await state.TryReserveAsync(sessionId, 3, CancellationToken.None);
        again.Allowed.ShouldBeTrue();
        again.Commit();
        state.CountFor(sessionId).ShouldBe(1);
    }

    [Test]
    public async Task Commit_increments_once_and_only_while_the_lease_is_held()
    {
        var state = new SessionReAdoptionState();
        var sessionId = Guid.NewGuid();
        var lease = await state.TryReserveAsync(sessionId, 3, CancellationToken.None);
        lease.Commit();
        lease.Commit();
        state.CountFor(sessionId).ShouldBe(1);
        await lease.DisposeAsync();
        Should.Throw<InvalidOperationException>(() => lease.Commit());
        state.CountFor(sessionId).ShouldBe(1);
    }

    [Test]
    public async Task Escalation_is_eligible_only_after_all_slots_are_used_and_a_further_mismatch_arrives()
    {
        var state = new SessionReAdoptionState();
        var sessionId = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await using var lease = await state.TryReserveAsync(sessionId, 3, CancellationToken.None);
            lease.Allowed.ShouldBeTrue();
            lease.EscalationEligible.ShouldBeFalse();
            lease.Commit();
        }

        await using var next = await state.TryReserveAsync(sessionId, 3, CancellationToken.None);
        next.Allowed.ShouldBeFalse();
        next.EscalationEligible.ShouldBeTrue();
        state.CountFor(sessionId).ShouldBe(3);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task Cap_zero_escalates_on_the_first_mismatch_and_negative_normalizes_to_zero(int cap)
    {
        var state = new SessionReAdoptionState();
        var sessionId = Guid.NewGuid();
        await using var lease = await state.TryReserveAsync(sessionId, cap, CancellationToken.None);
        lease.Allowed.ShouldBeFalse();
        lease.EscalationEligible.ShouldBeTrue();
        lease.Cap.ShouldBe(0);
        state.CountFor(sessionId).ShouldBe(0);
    }

    [Test]
    public async Task Two_concurrent_reservations_serialize_and_only_one_commits()
    {
        var state = new SessionReAdoptionState();
        var sessionId = Guid.NewGuid();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = Task.Run(async () =>
        {
            await using var lease = await state.TryReserveAsync(sessionId, 1, CancellationToken.None);
            started.TrySetResult();
            await release.Task;
            if (lease.Allowed)
                lease.Commit();
            return lease.Allowed;
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Task.Run(async () =>
        {
            await using var lease = await state.TryReserveAsync(sessionId, 1, CancellationToken.None);
            if (lease.Allowed)
                lease.Commit();
            return lease.Allowed;
        });

        await Task.Delay(50);
        second.IsCompleted.ShouldBeFalse();
        release.TrySetResult();
        var allowed = await Task.WhenAll(first, second);
        allowed.Count(x => x).ShouldBe(1);
        state.CountFor(sessionId).ShouldBe(1);
    }
}
