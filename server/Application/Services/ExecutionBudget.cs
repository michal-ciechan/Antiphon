namespace Antiphon.Server.Application.Services;

/// <summary>A single absolute deadline, including cooperative I/O and timer cancellation.</summary>
public sealed class ExecutionBudget : IDisposable
{
    private readonly CancellationTokenSource _timer;
    private readonly CancellationTokenSource _linked;

    public ExecutionBudget(DateTimeOffset deadline, TimeProvider clock, CancellationToken shutdown)
    {
        var remaining = deadline - clock.GetUtcNow();
        _timer = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, clock);
        _linked = CancellationTokenSource.CreateLinkedTokenSource(shutdown, _timer.Token);
        if (remaining <= TimeSpan.Zero) _timer.Cancel();
    }

    public CancellationToken Token => _linked.Token;
    public void Dispose() { _linked.Dispose(); _timer.Dispose(); }
}
