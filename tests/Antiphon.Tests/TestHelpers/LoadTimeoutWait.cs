namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Waits for a test hook with a budget that follows <see cref="TimeProvider"/> and a
/// separate finite wall cap. A test clock that is not advanced does not spend the
/// wall cap. A hook that never runs fails when the wall cap elapses.
/// </summary>
public static class LoadTimeoutWait
{
    public static Gate Open(
        TimeProvider? clock,
        TimeSpan? budget,
        TimeSpan wallCap,
        CancellationToken cancellationToken = default,
        TimeProvider? wallClock = null,
        string label = "The hook")
    {
        if (wallCap <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(wallCap), wallCap, "The wall cap must be positive.");
        if (budget is { } stated && stated <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(budget), stated, "The test-clock budget must be positive.");
        ArgumentNullException.ThrowIfNull(label);
        return new Gate(clock, budget, wallCap, cancellationToken, wallClock ?? TimeProvider.System, label);
    }

    public static Task UntilSignaledAsync(
        Task signal,
        TimeProvider? clock,
        TimeSpan? budget,
        TimeSpan wallCap,
        CancellationToken cancellationToken = default,
        TimeProvider? wallClock = null,
        string label = "The hook")
    {
        ArgumentNullException.ThrowIfNull(signal);
        return UntilSignaledAsync(ct => signal.WaitAsync(ct), clock, budget, wallCap, cancellationToken, wallClock, label);
    }

    public static Task<T> UntilSignaledAsync<T>(
        Task<T> signal,
        TimeProvider? clock,
        TimeSpan? budget,
        TimeSpan wallCap,
        CancellationToken cancellationToken = default,
        TimeProvider? wallClock = null,
        string label = "The hook")
    {
        ArgumentNullException.ThrowIfNull(signal);
        return UntilSignaledAsync(ct => signal.WaitAsync(ct), clock, budget, wallCap, cancellationToken, wallClock, label);
    }

    public static Task UntilSignaledAsync(
        Func<CancellationToken, Task> operation,
        TimeProvider? clock,
        TimeSpan? budget,
        TimeSpan wallCap,
        CancellationToken cancellationToken = default,
        TimeProvider? wallClock = null,
        string label = "The hook")
    {
        ArgumentNullException.ThrowIfNull(operation);
        return UntilSignaledAsync<object?>(async ct =>
        {
            await operation(ct);
            return null;
        }, clock, budget, wallCap, cancellationToken, wallClock, label);
    }

    public static async Task<T> UntilSignaledAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeProvider? clock,
        TimeSpan? budget,
        TimeSpan wallCap,
        CancellationToken cancellationToken = default,
        TimeProvider? wallClock = null,
        string label = "The hook")
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var gate = Open(clock, budget, wallCap, cancellationToken, wallClock, label);
        try
        {
            return await operation(gate.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            gate.ThrowIfTimedOut();
            throw;
        }
    }

    public sealed class Gate : IDisposable
    {
        private readonly CancellationTokenSource _logical;
        private readonly CancellationTokenSource _wall;
        private readonly CancellationTokenSource _clocks;
        private readonly CancellationTokenSource _linked;
        private readonly TimeSpan _wallCap;
        private readonly string _label;
        private readonly bool _hasBudget;

        internal Gate(
            TimeProvider? clock,
            TimeSpan? budget,
            TimeSpan wallCap,
            CancellationToken cancellationToken,
            TimeProvider wallClock,
            string label)
        {
            _wallCap = wallCap;
            _label = label;
            _hasBudget = budget is not null;
            _logical = budget is { } stated
                ? new CancellationTokenSource(stated, clock ?? TimeProvider.System)
                : new CancellationTokenSource();
            _wall = new CancellationTokenSource(wallCap, wallClock);
            _clocks = CancellationTokenSource.CreateLinkedTokenSource(_logical.Token, _wall.Token);
            _linked = CancellationTokenSource.CreateLinkedTokenSource(_clocks.Token, cancellationToken);
        }

        public CancellationToken Token => _linked.Token;

        public bool WallElapsed => _wall.IsCancellationRequested;

        public bool LogicalElapsed => _hasBudget && _logical.IsCancellationRequested;

        public void ThrowIfTimedOut()
        {
            if (WallElapsed)
                throw new TimeoutException($"{_label} was not signaled before the {_wallCap} wall cap.");
            if (LogicalElapsed)
                throw new TimeoutException($"{_label} was not signaled before the test-clock budget elapsed.");
        }

        public void Dispose()
        {
            _linked.Dispose();
            _clocks.Dispose();
            _wall.Dispose();
            _logical.Dispose();
        }
    }
}
