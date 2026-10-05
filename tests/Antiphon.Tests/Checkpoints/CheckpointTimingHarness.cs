using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Shouldly;

namespace Antiphon.Tests.Checkpoints;

/// <summary>One enclosing watchdog; receipts, rather than elapsed time, prove each phase.</summary>
internal sealed class CheckpointTimingHarness : IDisposable
{
    internal static readonly TimeSpan WorkBudget = TimeSpan.FromSeconds(290);
    internal static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(10);
    private readonly TimeProvider _clock;
    private readonly long _started;
    private readonly string _method;
    private readonly string _identity;
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationTokenSource _linked;
    private string _lastPhase = "entry";

    public CheckpointTimingHarness(string method, string identity, CancellationToken cancellationToken,
        TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _started = _clock.GetTimestamp();
        _method = method;
        _identity = identity;
        _deadline = new CancellationTokenSource(WorkBudget, _clock);
        _linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token);
    }

    public CancellationToken Token => _linked.Token;

    public async Task PhaseAsync(string phase, Task receipt, Task? execution = null)
    {
        // WaitAsync owns its cancellation registration, including a successful receipt.
        try
        {
            var observed = receipt.WaitAsync(Token);
            if (execution is not null && execution != receipt)
            {
                await Task.WhenAny(observed, execution);
                if (!receipt.IsCompleted && execution.IsCompleted)
                {
                    await execution; // Preserve the original executor fault.
                    throw Missing(phase, "execution finished before receipt");
                }
            }
            await observed;
        }
        catch (OperationCanceledException) when (Token.IsCancellationRequested)
        {
            throw Missing(phase, "deadline or incoming cancellation");
        }
        receipt.IsCompletedSuccessfully.ShouldBeTrue(Diagnostic(phase, "condition never completed"));
        _lastPhase = phase;
        Console.WriteLine($"C820 PHASE method={_method} run={_identity} phase={phase} elapsed={_clock.GetElapsedTime(_started).TotalMilliseconds:F3}ms");
    }

    private ShouldAssertException Missing(string phase, string cause) => new(Diagnostic(phase, cause));
    private string Diagnostic(string phase, string cause)
    {
        var elapsed = _clock.GetElapsedTime(_started);
        return $"phase={phase} condition never completed; method={_method} run/row={_identity} " +
            $"elapsed={elapsed} remaining={WorkBudget - elapsed} last={_lastPhase}; {cause}";
    }

    public void Dispose()
    {
        _linked.Cancel();
        _linked.Dispose();
        _deadline.Dispose();
    }
}

internal static class CheckpointTimingAssertions
{
    // Shouldly 4.3's generic ThrowAsync deliberately rethrows assertion exceptions.
    // Capture this expected type directly, and still fail when the guard returns success.
    public static async Task<ShouldAssertException> CaptureAsync(Task operation, string label,
        CancellationToken cancellationToken)
    {
        ShouldAssertException? failure = null;
        try { await operation.WaitAsync(cancellationToken); }
        catch (ShouldAssertException ex) { failure = ex; }
        failure.ShouldNotBeNull(label);
        return failure;
    }
}

/// <summary>Each acknowledged request needs a separate permit. Cancellation never moves time.</summary>
internal sealed class CheckpointStepClock
{
    private readonly object _gate = new();
    private readonly Channel<DelayRequest> _requests = Channel.CreateUnbounded<DelayRequest>();
    private DateTimeOffset _now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
    public DateTimeOffset Now() { lock (_gate) return _now; }
    public Task<DelayRequest> NextAsync(CancellationToken cancellationToken) =>
        _requests.Reader.ReadAsync(cancellationToken).AsTask();

    public async Task Delay(TimeSpan span, CancellationToken cancellationToken)
    {
        var request = new DelayRequest(span);
        await _requests.Writer.WriteAsync(request, cancellationToken);
        await request.Permit.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _now += span;
        request.Completed.TrySetResult();
    }

    internal sealed class DelayRequest(TimeSpan span)
    {
        public TimeSpan Span { get; } = span;
        internal TaskCompletionSource Permit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Advance() => Permit.TrySetResult();
    }
}

/// <summary>Registers immediately and owns abort, all fixture gates, and the completed join.</summary>
internal sealed class CheckpointExecutionOwner : IAsyncDisposable
{
    private readonly CancellationTokenSource _abort;
    private readonly Action _release;
    private readonly TimeProvider _cleanupClock;
    private Task? _disposal;
    public Task<int> Execution { get; }
    public TaskCompletionSource DisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CheckpointExecutionOwner(CheckpointTimingHarness timing, Func<CancellationToken, Task<int>> start,
        Action<Task> register, Action release, TimeProvider? cleanupClock = null)
    {
        _abort = CancellationTokenSource.CreateLinkedTokenSource(timing.Token);
        _release = release;
        _cleanupClock = cleanupClock ?? TimeProvider.System;
        Execution = start(_abort.Token);
        register(Execution);
    }

    public async Task VerifyAsync(Func<Task> assertions)
    {
        Exception? primary = null;
        try { await assertions(); }
        catch (Exception ex) { primary = ex; }
        try { await DisposeAsync(); }
        catch (Exception cleanup) when (primary is not null)
        { throw new AggregateException("checkpoint assertion and cleanup failed", primary, cleanup); }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    public ValueTask DisposeAsync() => new(_disposal ??= AbortAndJoinAsync());
    private async Task AbortAndJoinAsync()
    {
        using var cleanup = new CancellationTokenSource(CheckpointTimingHarness.CleanupBudget, _cleanupClock);
        _abort.Cancel();
        DisposalEntered.TrySetResult();
        _release();
        try { await Execution.WaitAsync(cleanup.Token); }
        finally
        {
            // Incomplete work stays registered; scope teardown will retain every root.
            Console.WriteLine($"C820 JOIN completed={Execution.IsCompleted} status={Execution.Status}");
            if (Execution.IsCompleted) _abort.Dispose();
        }
    }

    // Independent rescue for negative fixture proofs, even if normal disposal is broken.
    public void RescueCancel()
    {
        if (!Execution.IsCompleted) _abort.Cancel();
    }
}
