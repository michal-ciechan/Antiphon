using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class C1514HostLoadScenarioTests
{
    [Test]
    public async Task DeadlineCancelsAndJoinsHeldWorkBeforeReturning()
    {
        var time = new FakeTimeProvider();
        using var scenario = new C1514HostLoadScenario(TimeSpan.FromSeconds(10), time: time);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var joined = false;
        Task? held = null;
        var run = scenario.RunAsync(ct => held = HoldAsync(ct));
        try
        {
            await entered.Task;
            time.Advance(TimeSpan.FromSeconds(10));
            await Task.WhenAny(run, cancelled.Task);
            cancelled.Task.IsCompleted.ShouldBeTrue("the held operation must observe cancellation");
            run.IsCompleted.ShouldBeFalse("teardown must await the held operation's cleanup");
            releaseCleanup.TrySetResult();
            await Should.ThrowAsync<TimeoutException>(() => run);
            joined.ShouldBeTrue();
        }
        finally
        {
            scenario.Cancel(); releaseCleanup.TrySetResult();
            if (held is not null) try { await held; } catch (OperationCanceledException) { }
            try { await run; } catch (Exception) { }
        }
        async Task HoldAsync(CancellationToken ct)
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { cancelled.TrySetResult(); await releaseCleanup.Task; joined = true; }
        }
    }

    [Test]
    public void ReceiptsRenewTheQuietBudgetButPollingDoesNot()
    {
        var time = new FakeTimeProvider();
        using var scenario = new C1514HostLoadScenario(TimeSpan.FromSeconds(10), time: time);
        time.Advance(TimeSpan.FromSeconds(9)); scenario.Observe();
        time.Advance(TimeSpan.FromSeconds(9)); scenario.Token.IsCancellationRequested.ShouldBeFalse();
        // Merely checking the same receipt is not another completed operation.
        time.Advance(TimeSpan.FromSeconds(1)); scenario.Token.IsCancellationRequested.ShouldBeTrue();
    }
}
