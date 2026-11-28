namespace Antiphon.Checkpoints;

// A CLI runtime has at most one observation batch. The worker and the process
// exit wait share a deadline. Synchronous file I/O can outlive the deadline.
internal sealed class BuildObservationPublication
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);
    private long _startedAt;
    private int _queued;
    internal Task Completion { get; private set; } = Task.CompletedTask;

    internal void Queue(Func<CancellationToken, Task> publish)
    {
        if (Interlocked.Exchange(ref _queued, 1) != 0)
            return;
        _startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var stop = new CancellationTokenSource(Deadline);
        Completion = Task.Factory.StartNew(() =>
        {
            using (stop)
            {
                publish(stop.Token).GetAwaiter().GetResult();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    internal async Task FlushOnExitAsync()
    {
        if (Completion.IsCompletedSuccessfully)
            return;
        var remaining = Deadline - System.Diagnostics.Stopwatch.GetElapsedTime(_startedAt);
        if (remaining <= TimeSpan.Zero)
            return;
        try { await Completion.WaitAsync(remaining).ConfigureAwait(false); }
        catch (Exception)
        {
            // A timed-out or faulted observer has no authority over the CLI verdict.
        }
    }
}
