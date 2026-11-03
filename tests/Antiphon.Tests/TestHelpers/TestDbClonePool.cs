using System.Collections.Concurrent;
using System.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Keeps up to <see cref="Capacity"/> freshly cloned, never-opened test databases ready, so
/// <c>CreateIsolatedSchemaAsync</c> usually hands one out without cloning or queueing on the
/// per-process clone lock. <see cref="Workers"/> background fillers (one per clone-lock slot) clone
/// through the same lock and refill whenever databases are taken.
/// <para>
/// Unused pooled databases are not dropped: they live inside the test container, which teardown
/// removes. <c>ANTIPHON_TEST_PG_POOL</c> sets the size (default 20; <c>0</c> or <c>off</c> disables).
/// Trace evidence: <c>db-create pool</c> spans carry <c>antiphon.pool=hit|miss</c> and the pool depth;
/// background clones run under root <c>db-pool fill</c> spans, never inside a test's trace.
/// </para>
/// </summary>
internal sealed class TestDbClonePool
{
    public const string EnvVar = "ANTIPHON_TEST_PG_POOL";
    public const int DefaultCapacity = 20;

    private readonly ConcurrentQueue<string> _ready = new();
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<string, Task> _clone;
    private Task _filler = Task.CompletedTask;
    private int _filled;
    private int _inFlight;
    private int _failures;

    private TestDbClonePool(int capacity, Func<string, Task> clone, int workers)
    {
        Capacity = capacity;
        Workers = Math.Clamp(workers, 1, capacity);
        _clone = clone;
    }

    public int Capacity { get; }

    public int Workers { get; }

    public int Depth => _ready.Count;

    public int Filled => Volatile.Read(ref _filled);

    /// <summary>Pool size from <see cref="EnvVar"/>; 0 when disabled.</summary>
    public static int CapacityFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVar)?.Trim();
        if (string.IsNullOrEmpty(raw))
            return DefaultCapacity;
        if (string.Equals(raw, "off", StringComparison.OrdinalIgnoreCase))
            return 0;
        return int.TryParse(raw, out var n) && n > 0 ? Math.Min(n, 100) : 0;
    }

    /// <summary>
    /// Starts the filler. The execution context is not flowed: the first test's timing scope and
    /// trace span must not own clones made for other tests.
    /// </summary>
    public static TestDbClonePool Start(int capacity, Func<string, Task> clone, int workers = 1)
    {
        var pool = new TestDbClonePool(capacity, clone, workers);
        using (ExecutionContext.SuppressFlow())
            pool._filler = Task.WhenAll(Enumerable.Range(0, pool.Workers).Select(_ => Task.Run(pool.FillLoopAsync)));
        return pool;
    }

    public bool TryTake(out string databaseName)
    {
        var hit = _ready.TryDequeue(out databaseName!);
        // One permit per take, up to one per worker, so a burst wakes every filler.
        if (_wake.CurrentCount < Workers)
            _wake.Release();
        return hit;
    }

    /// <summary>Stops filling and waits for an in-flight clone. Never throws.</summary>
    public async Task StopAsync()
    {
        _stop.Cancel();
        try { await _filler.ConfigureAwait(false); }
        catch (Exception) { /* the loop reports its own failures */ }
    }

    /// <summary>Claims one fill slot while ready + in-flight is below capacity.</summary>
    private bool TryReserve()
    {
        while (true)
        {
            var inFlight = Volatile.Read(ref _inFlight);
            if (_ready.Count + inFlight >= Capacity)
                return false;
            if (Interlocked.CompareExchange(ref _inFlight, inFlight + 1, inFlight) == inFlight)
                return true;
        }
    }

    private async Task FillLoopAsync()
    {
        var ct = _stop.Token;
        while (!ct.IsCancellationRequested)
        {
            while (!ct.IsCancellationRequested && TryReserve())
            {
                var name = $"test_{Guid.NewGuid():N}";
                using var span = TestTrace.Span("db-pool fill");
                try
                {
                    await _clone(name).ConfigureAwait(false);
                    _ready.Enqueue(name);
                    Interlocked.Increment(ref _filled);
                    span?.SetTag("antiphon.pool.depth", _ready.Count);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    span?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
                    // A broken clone path must not spin; tests still clone directly on a miss.
                    if (Interlocked.Increment(ref _failures) <= 3)
                        Console.Error.WriteLine($"C1420 pg-pool fill failed: {ex.GetType().Name}: {ex.Message}");
                    try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
                catch (Exception)
                {
                    return; // stopping: the container is going away
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }
            }

            try { await _wake.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }
}
