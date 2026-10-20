using TUnit.Core;

namespace C1375.Probe;

public class C1375Producer
{
    [Test, Timeout(100)]
    public async Task TimeoutProbe(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    }

    [Test]
    public async Task CancelledProbe()
    {
        using var cancellation = new CancellationTokenSource();
        var wait = Task.Delay(Timeout.Infinite, cancellation.Token);
        cancellation.Cancel();
        try
        {
            await wait;
            throw new InvalidOperationException("The local wait did not cancel.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            TestContext.Current!.Execution.OverrideResult(
                TestState.Cancelled, "CARD-1375 controlled cancellation");
        }
    }
}
