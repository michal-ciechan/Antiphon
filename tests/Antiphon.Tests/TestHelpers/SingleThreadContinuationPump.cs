using System.Collections.Concurrent;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1362 D-6: a stand-in for a saturated thread pool. One dedicated thread drains a queue of
/// posted continuations and sets itself as the current <see cref="SynchronizationContext"/>, so
/// every <c>await</c> inside work started through <see cref="InvokeAsync{T}"/> resumes on that
/// one thread. A read that blocks the thread (sync-over-async) holds every later continuation;
/// a read that is awaited frees it. <see cref="PostProbeAsync"/> is the unrelated continuation
/// whose completion the witness observes. Shape of <c>TurnEndGapDeliveryHarness.ContinuationQueue</c>,
/// on a thread instead of <c>Task.Run</c> so the pool is not part of the proof.
/// </summary>
internal sealed class SingleThreadContinuationPump : SynchronizationContext, IAsyncDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private Exception? _callbackFailure;

    public SingleThreadContinuationPump()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "c1362-continuation-pump" };
        _thread.Start();
    }

    /// <summary>The pump's own thread id, so a test can assert where a continuation ran.</summary>
    public int ThreadId => _thread.ManagedThreadId;

    public override void Post(SendOrPostCallback d, object? state)
    {
        try
        {
            _queue.Add((d, state));
        }
        catch (InvalidOperationException)
        {
            throw new ObjectDisposedException(nameof(SingleThreadContinuationPump));
        }
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("The pump only accepts posted continuations.");

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Starts <paramref name="function"/> on the pump thread; its awaits resume there.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> function)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ => _ = CompleteAsync(function, done), null);
        return done.Task;
    }

    private static async Task CompleteAsync<T>(Func<Task<T>> function, TaskCompletionSource<T> done)
    {
        try
        {
            done.TrySetResult(await function());
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
        }
    }

    /// <summary>Completes once the pump thread has run it: the unrelated server continuation.</summary>
    public Task PostProbeAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ => done.TrySetResult(), null);
        return done.Task;
    }

    private void Run()
    {
        SetSynchronizationContext(this);
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try
                {
                    item.Callback(item.State);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref _callbackFailure, ex, null);
                }
            }
        }
        finally
        {
            SetSynchronizationContext(null);
            _exited.TrySetResult();
        }
    }

    /// <summary>
    /// Stops accepting work; the thread exits once the queue is drained. The wait is bounded so a
    /// thread still blocked inside a sync-over-async read (the base shape) cannot hang disposal.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _queue.CompleteAdding();
        await Task.WhenAny(_exited.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        if (_exited.Task.IsCompleted)
            _queue.Dispose();
        if (_callbackFailure is { } failure)
            throw new InvalidOperationException("A continuation posted to the pump threw.", failure);
    }
}
