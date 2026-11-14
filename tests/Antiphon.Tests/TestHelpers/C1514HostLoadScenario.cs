namespace Antiphon.Tests.TestHelpers;

// Fixture-owned inactivity deadline. Only completed operations renew it.
internal sealed class C1514HostLoadScenario : IDisposable
{
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationTokenSource _stop;
    private readonly TimeSpan _quiet;
    public CancellationToken Token => _stop.Token;
    public C1514HostLoadScenario(TimeSpan quiet, CancellationToken cancellationToken = default, TimeProvider? time = null)
    {
        _quiet = quiet;
        _deadline = new(quiet, time ?? TimeProvider.System);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token);
    }
    public void Observe() => _deadline.CancelAfter(_quiet);
    public void Cancel() => _stop.Cancel();
    public async Task RunAsync(Func<CancellationToken, Task> action)
    {
        await action(Token).WaitAsync(Token);
    }
    public void Dispose() { _stop.Dispose(); _deadline.Dispose(); }
}
