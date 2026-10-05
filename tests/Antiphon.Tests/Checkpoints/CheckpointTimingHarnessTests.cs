using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointTimingHarnessTests : CheckpointTestBase
{
    [Test]
    public async Task single_step_delay_never_opens_future_requests(CancellationToken cancellationToken)
    {
        using var timing = new CheckpointTimingHarness(nameof(single_step_delay_never_opens_future_requests), "clock", cancellationToken);
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(timing.Token);
        var clock = new CheckpointStepClock();
        var before = clock.Now();
        var first = clock.Delay(TimeSpan.FromSeconds(3), abort.Token);
        Task? second = null;
        try
        {
            var next = clock.NextAsync(timing.Token);
            await timing.PhaseAsync("owner-delay", next);
            (await next).Advance();
            await timing.PhaseAsync("owner-delay", first);
            second = clock.Delay(TimeSpan.FromSeconds(3), abort.Token);
            var pending = clock.NextAsync(timing.Token);
            await timing.PhaseAsync("owner-delay", pending);
            second.IsCompleted.ShouldBeFalse("second-delay-pending");
            clock.Now().ShouldBe(before + TimeSpan.FromSeconds(3), "second-delay-pending");
        }
        finally
        {
            abort.Cancel();
            using var cleanup = new CancellationTokenSource(CheckpointTimingHarness.CleanupBudget);
            try { await first.WaitAsync(cleanup.Token); }
            catch (OperationCanceledException) when (first.IsCanceled) { }
            if (second is not null)
            {
                try { await second.WaitAsync(cleanup.Token); }
                catch (OperationCanceledException) when (second.IsCanceled) { }
            }
        }
    }

    [Test]
    public async Task single_step_delay_cancellation_does_not_advance_time(CancellationToken cancellationToken)
    {
        using var timing = new CheckpointTimingHarness(nameof(single_step_delay_cancellation_does_not_advance_time), "clock", cancellationToken);
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(timing.Token);
        var clock = new CheckpointStepClock();
        var before = clock.Now();
        var delay = clock.Delay(TimeSpan.FromSeconds(3), abort.Token);
        try
        {
            await timing.PhaseAsync("owner-delay", clock.NextAsync(timing.Token));
            abort.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => delay.WaitAsync(timing.Token));
            clock.Now().ShouldBe(before, "canceled-delay-clock-unchanged");
        }
        finally
        {
            abort.Cancel();
            using var cleanup = new CancellationTokenSource(CheckpointTimingHarness.CleanupBudget);
            try { await delay.WaitAsync(cleanup.Token); }
            catch (OperationCanceledException) when (delay.IsCanceled) { }
        }
    }

    [Test]
    public async Task phase_wait_surfaces_early_execution_failure(CancellationToken cancellationToken)
    {
        using var timing = new CheckpointTimingHarness(nameof(phase_wait_surfaces_early_execution_failure), "CP-1", cancellationToken);
        var missing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execute = Task.FromException(new IOException("synthetic-executor-fault"));
        var failure = await Should.ThrowAsync<IOException>(() => timing.PhaseAsync("driver-entered", missing.Task, execute));
        failure.Message.Contains("synthetic-executor-fault", StringComparison.Ordinal).ShouldBeTrue("early-execution-fault");
    }

    [Test]
    public async Task phase_deadline_is_finite_and_shared(CancellationToken cancellationToken)
    {
        var clock = new ControlledTimeProvider();
        using var timing = new CheckpointTimingHarness(nameof(phase_deadline_is_finite_and_shared), "CP-1", cancellationToken, clock);
        await timing.PhaseAsync("driver-entered", Task.CompletedTask);
        clock.Advance(CheckpointTimingHarness.WorkBudget - TimeSpan.FromTicks(1));
        timing.Token.IsCancellationRequested.ShouldBeFalse("fixture-deadline-not-early");
        var before = clock.Events.Count(e => e.Action == "create");
        var missing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var phase = timing.PhaseAsync("release-entered", missing.Task);
        clock.Events.Count(e => e.Action == "create").ShouldBe(before, "fixture-deadline-shared");
        clock.Advance(TimeSpan.FromTicks(1));
        timing.Token.IsCancellationRequested.ShouldBeTrue("fixture-deadline-fired");
        using var rescue = new CancellationTokenSource(CheckpointTimingHarness.CleanupBudget);
        var failure = await Should.ThrowAsync<ShouldAssertException>(() => phase.WaitAsync(rescue.Token));
        failure.Message.ShouldContain("phase=release-entered condition never completed");
        clock.Events.Any(e => e.Action == "create" && e.DueTime == CheckpointTimingHarness.WorkBudget).ShouldBeTrue();
    }

    [Test]
    public async Task phase_deadline_links_test_cancellation(CancellationToken cancellationToken)
    {
        using var incoming = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timing = new CheckpointTimingHarness(nameof(phase_deadline_links_test_cancellation), "CP-1", incoming.Token);
        var missing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var phase = timing.PhaseAsync("driver-entered", missing.Task);
        incoming.Cancel();
        timing.Token.IsCancellationRequested.ShouldBeTrue("test-cancel-linked");
        using var rescue = new CancellationTokenSource(CheckpointTimingHarness.CleanupBudget);
        var failure = await Should.ThrowAsync<ShouldAssertException>(() => phase.WaitAsync(rescue.Token));
        failure.Message.Contains("phase=driver-entered condition never completed", StringComparison.Ordinal).ShouldBeTrue("test-cancel-linked");
    }

    [Test]
    [Arguments("owner-delay")]
    [Arguments("driver-entered")]
    [Arguments("slot-entered")]
    [Arguments("slot-canceled")]
    [Arguments("owner-unverified")]
    [Arguments("driver-canceled")]
    [Arguments("release-entered")]
    [Arguments("execution-finished")]
    [Arguments("renew-delay")]
    [Arguments("renew-http")]
    [Arguments("renew-next-delay")]
    [Arguments("renew-stop")]
    [Arguments("renew-diagnostic")]
    [Arguments("drain-entered")]
    [Arguments("drain-canceled")]
    [Arguments("lease-disposed")]
    public async Task missing_phase_reports_condition_and_joins_work(string phase, CancellationToken cancellationToken)
    {
        var clock = new ControlledTimeProvider();
        using var timing = new CheckpointTimingHarness(nameof(missing_phase_reports_condition_and_joins_work), "CP-1/" + phase, cancellationToken, clock);
        await timing.PhaseAsync(phase, Task.CompletedTask);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var missing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Held work ignores cancellation on purpose; disposal must release and join it.
        var owner = new CheckpointExecutionOwner(timing, async _ => { await held.Task; return 0; },
            RegisterCheckpointWork, () => held.TrySetResult());
        var verify = owner.VerifyAsync(() => timing.PhaseAsync(phase, missing.Task, owner.Execution));
        try
        {
            clock.Events.Any(e => e.Action == "create" && e.DueTime == CheckpointTimingHarness.WorkBudget).ShouldBeTrue();
            clock.Advance(CheckpointTimingHarness.WorkBudget);
            using var rescue = new CancellationTokenSource(CheckpointTimingHarness.CleanupBudget);
            var failure = await Should.ThrowAsync<ShouldAssertException>(() => verify.WaitAsync(rescue.Token), "missing-phase-" + phase);
            failure.Message.Contains($"phase={phase} condition never completed", StringComparison.Ordinal).ShouldBeTrue("missing-phase-" + phase);
            owner.Execution.IsCompleted.ShouldBeTrue("missing-phase-cleanup-joined");
            (await owner.Execution).ShouldBe(0);
        }
        finally
        {
            owner.RescueCancel();
            held.TrySetResult();
            using var rescue = new CancellationTokenSource(CheckpointTimingHarness.CleanupBudget);
            await owner.Execution.WaitAsync(rescue.Token);
            // Observe the verification even on a failing/mutated assertion path.
            try { await verify.WaitAsync(rescue.Token); }
            catch (ShouldAssertException) { }
        }
    }
}
