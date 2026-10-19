using System.Collections.Concurrent;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1365 D-5: a <see cref="SynchronizationContext"/> drained by one dedicated background thread,
/// so a callback that blocks that thread blocks every continuation posted after it, the way a
/// pinned thread-pool thread does. Shape of <c>TurnEndGapDeliveryHarness.ContinuationQueue</c>
/// (Antiphon.Tests); CARD-1362 S1 may link this file instead of writing its own.
/// </summary>
internal sealed class SingleThreadContinuationPump : SynchronizationContext, IAsyncDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;

    public SingleThreadContinuationPump()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = nameof(SingleThreadContinuationPump) };
        _thread.Start();
    }

    /// <summary>True once the pump thread has drained and exited; false when dispose abandoned it.</summary>
    public bool Drained { get; private set; }

    public override void Post(SendOrPostCallback callback, object? state)
    {
        try { _queue.Add((callback, state)); }
        catch (InvalidOperationException) { throw new ObjectDisposedException(nameof(SingleThreadContinuationPump)); }
    }

    public override void Send(SendOrPostCallback callback, object? state) =>
        throw new NotSupportedException("Send would deadlock a single-thread pump.");

    /// <summary>Starts <paramref name="action"/> on the pump thread and returns its outcome.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> action)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ => _ = CompleteAsync(action, done), null);
        return done.Task;
    }

    private static async Task CompleteAsync<T>(Func<Task<T>> action, TaskCompletionSource<T> done)
    {
        try { done.TrySetResult(await action()); }
        catch (Exception ex) { done.TrySetException(ex); }
    }

    private void Run()
    {
        SetSynchronizationContext(this);
        foreach (var item in _queue.GetConsumingEnumerable())
            item.Callback(item.State);
    }

    /// <summary>
    /// Completes the queue and waits at most 1 s for the thread. A thread still blocked inside a
    /// synchronous read (the red case before the fixture releases its child) is abandoned as a
    /// background thread and reported through <see cref="Drained"/>; dispose never throws, so the
    /// witness's own assertion stays the failure message.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _queue.CompleteAdding();
        Drained = await Task.Run(() => _thread.Join(TimeSpan.FromSeconds(1)));
    }
}
