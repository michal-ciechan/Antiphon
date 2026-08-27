namespace Antiphon.Server.Application.Services;

/// <summary>
/// Bounds concurrent git child processes so a busy home page cannot exhaust the machine's process
/// and handle budget. The counters are deliberately exposed for integration-test observations.
/// </summary>
public sealed class GitProcessGate
{
    private readonly SemaphoreSlim _semaphore;
    private int _inFlight;
    private int _started;
    private int _peakInFlight;

    public GitProcessGate(int maxConcurrentProcesses = 8)
    {
        _semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrentProcesses));
    }

    public int InFlight => Volatile.Read(ref _inFlight);
    public int Started => Volatile.Read(ref _started);
    public int PeakInFlight => Volatile.Read(ref _peakInFlight);

    public async ValueTask<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct);
        var inFlight = Interlocked.Increment(ref _inFlight);
        Interlocked.Increment(ref _started);
        UpdatePeak(inFlight);
        return new Lease(this);
    }

    private void UpdatePeak(int inFlight)
    {
        var observed = Volatile.Read(ref _peakInFlight);
        while (inFlight > observed)
        {
            var prior = Interlocked.CompareExchange(ref _peakInFlight, inFlight, observed);
            if (prior == observed)
                return;
            observed = prior;
        }
    }

    private void Exit()
    {
        Interlocked.Decrement(ref _inFlight);
        _semaphore.Release();
    }

    private sealed class Lease(GitProcessGate owner) : IDisposable
    {
        private GitProcessGate? _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Exit();
        }
    }
}
