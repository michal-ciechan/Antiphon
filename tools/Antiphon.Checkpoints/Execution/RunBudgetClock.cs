namespace Antiphon.Checkpoints;

/// <summary>Monotonic run wall time, less the union of admission-only intervals.</summary>
public sealed class RunBudgetClock : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _expired = new();
    private readonly ITimer? _executionTimer;
    private readonly ITimer? _wallTimer;
    private readonly long _started;
    private long _last;
    private TimeSpan _execution;
    private TimeSpan _admission;
    private int _work;
    private int _waiting;
    private string? _reason;

    public RunBudgetClock(TimeSpan? executionLimit, TimeSpan? hardWallLimit, TimeProvider? time = null)
    {
        Validate(executionLimit, "total-timeout");
        Validate(hardWallLimit, "hard-wall-timeout");
        _time = time ?? TimeProvider.System;
        ExecutionLimit = executionLimit;
        HardWallLimit = hardWallLimit;
        StartedAt = _time.GetUtcNow();
        _started = _last = _time.GetTimestamp();
        if (executionLimit is TimeSpan execution)
            _executionTimer = _time.CreateTimer(_ => Expire(), null, execution, Timeout.InfiniteTimeSpan);
        if (hardWallLimit is TimeSpan wall)
            _wallTimer = _time.CreateTimer(_ => Expire(), null, wall, Timeout.InfiniteTimeSpan);
    }

    public DateTimeOffset StartedAt { get; }
    public TimeSpan? ExecutionLimit { get; }
    public TimeSpan? HardWallLimit { get; }
    public CancellationToken Token => _expired.Token;
    public string? Reason { get { lock (_gate) { Accumulate(); return Cause(); } } }
    public TimeSpan RemainingExecution
    {
        get { lock (_gate) { Accumulate(); return Remaining(ExecutionLimit, _execution); } }
    }
    public TimeSpan RemainingWall => Remaining(HardWallLimit, _time.GetElapsedTime(_started));

    public RunBudgetEvidence Snapshot(TimeSpan? admissionLimit = null)
    {
        lock (_gate)
        {
            Accumulate();
            return new RunBudgetEvidence
            {
                BudgetWallSeconds = _time.GetElapsedTime(_started).TotalSeconds,
                AdmissionOnlySeconds = _admission.TotalSeconds,
                ExecutionSeconds = _execution.TotalSeconds,
                ExecutionLimitSeconds = ExecutionLimit?.TotalSeconds,
                HardWallLimitSeconds = HardWallLimit?.TotalSeconds,
                AdmissionLimitSeconds = admissionLimit?.TotalSeconds,
                RemainingExecutionSeconds = Remaining(ExecutionLimit, _execution).TotalSeconds,
                TerminalReason = Cause(),
            };
        }
    }

    public Activity Work() => new(this);

    internal static void Validate(TimeSpan? limit, string field)
    {
        if (limit is TimeSpan value && (value <= TimeSpan.Zero || value.TotalMilliseconds > uint.MaxValue - 1d))
            throw new ManifestValidationException(field, field + " must be a positive finite timer duration");
    }

    private static TimeSpan Remaining(TimeSpan? limit, TimeSpan elapsed) =>
        limit is TimeSpan value ? value > elapsed ? value - elapsed : TimeSpan.Zero : TimeSpan.MaxValue;

    private void Accumulate()
    {
        var now = _time.GetTimestamp();
        var elapsed = _time.GetElapsedTime(_last, now);
        if (_waiting > 0 && _work == 0) _admission += elapsed;
        else _execution += elapsed;
        _last = now;
    }

    private string? Cause() =>
        HardWallLimit is TimeSpan wall && _time.GetElapsedTime(_started) >= wall ? "hard-wall-deadline-exceeded"
        : ExecutionLimit is TimeSpan execution && _execution >= execution ? "execution-budget-exceeded" : _reason;

    private void Expire()
    {
        lock (_gate)
        {
            Accumulate();
            _reason = Cause();
        }
        if (_reason is not null) _expired.Cancel();
    }

    private void Transition(int work, int waiting)
    {
        bool cancel;
        lock (_gate)
        {
            Accumulate();
            _work += work;
            _waiting += waiting;
            _reason = Cause();
            cancel = _reason is not null;
            if (!cancel && ExecutionLimit is TimeSpan limit)
                _executionTimer?.Change(_waiting > 0 && _work == 0
                    ? Timeout.InfiniteTimeSpan : limit - _execution, Timeout.InfiniteTimeSpan);
        }
        if (cancel) _expired.Cancel();
    }

    public sealed class Activity : IDisposable
    {
        private readonly RunBudgetClock _owner;
        private bool _active = true;
        private bool _disposed;
        internal Activity(RunBudgetClock owner) { _owner = owner; owner.Transition(1, 0); }
        public void SetWorking(bool working)
        {
            if (_disposed || _active == working) return;
            _owner.Transition(working ? 1 : -1, 0);
            _active = working;
        }
        internal IDisposable Admission()
        {
            _owner.Transition(-1, 1);
            return new AdmissionScope(_owner);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_active) _owner.Transition(-1, 0);
        }
    }
    private sealed class AdmissionScope(RunBudgetClock owner) : IDisposable
    {
        public void Dispose() => owner.Transition(1, -1);
    }

    internal async Task<SlotLease> AcquireAsync(IBuildSlotClient slots, SlotSession session, string label,
        Activity activity, TimeSpan admissionLimit, Action<int> observedWait, CancellationToken cancellationToken)
    {
        Validate(admissionLimit, "slotWaitMinutes");
        var started = _time.GetTimestamp();
        using var deadline = new CancellationTokenSource(admissionLimit, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Token, deadline.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            // Charge synchronous client preflight and grant settlement. Only a
            // genuinely pending broker acquisition earns admission-only credit.
            var pending = slots.AcquireAsync(session, label, linked.Token);
            SlotLease lease;
            using (var admission = session.Mode == "enabled" && !pending.IsCompleted ? activity.Admission() : null)
                lease = await pending.ConfigureAwait(false);
            if (linked.IsCancellationRequested)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
            }
            observedWait(Math.Max(lease.WaitedSeconds, (int)_time.GetElapsedTime(started).TotalSeconds));
            return lease;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !Token.IsCancellationRequested)
        {
            return new SlotLease { State = "timeout", ExitCode = ExitCodes.SlotTimeout,
                SlotReason = "slot-admission-deadline-exceeded", WaitedSeconds = (int)_time.GetElapsedTime(started).TotalSeconds };
        }
        finally { observedWait((int)_time.GetElapsedTime(started).TotalSeconds); }
    }

    public void Dispose()
    {
        // Join timer callbacks before disposing the cancellation source they use.
        if (_executionTimer is not null) _executionTimer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (_wallTimer is not null) _wallTimer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _expired.Dispose();
    }
}

public sealed class RunBudgetEvidence
{
    public double BudgetWallSeconds { get; set; }
    public double AdmissionOnlySeconds { get; set; }
    public double ExecutionSeconds { get; set; }
    public double? ExecutionLimitSeconds { get; set; }
    public double? HardWallLimitSeconds { get; set; }
    public double? AdmissionLimitSeconds { get; set; }
    public double RemainingExecutionSeconds { get; set; }
    public string? TerminalReason { get; set; }
    public double? FinalizationSeconds { get; set; }
}
