namespace Antiphon.Checkpoints;

// One monotonic clock per invocation, retained through optional baseline work.
internal sealed class RunBudgetClock : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _ended = new();
    private readonly ITimer _executionTimer;
    private readonly ITimer _wallTimer;
    private readonly TimeSpan _executionLimit;
    private readonly TimeSpan _wallLimit;
    private long _last;
    private TimeSpan _wall, _admission;
    private int _activities, _waiting;
    private bool _schedulerIdle;
    private string? _reason;
    public CancellationToken Token => _ended.Token;
    public TimeProvider Time => _time;
    public DateTimeOffset HardWallTimeoutAt { get; }
    public string? Reason { get { lock (_gate) { Update(); return _reason; } } }
    public TimeSpan RemainingExecution { get { lock (_gate) { Update(); return Max(_executionLimit - (_wall - _admission)); } } }
    public TimeSpan RemainingWall { get { lock (_gate) { Update(); return Max(_wallLimit - _wall); } } }

    public RunBudgetClock(TimeProvider time, TimeSpan executionLimit, TimeSpan wallLimit)
    {
        Validate(executionLimit, "total-timeout"); Validate(wallLimit, "hard-wall-timeout");
        _time=time; _executionLimit=executionLimit; _wallLimit=wallLimit;
        _last=time.GetTimestamp(); HardWallTimeoutAt=time.GetUtcNow()+wallLimit;
        _executionTimer=time.CreateTimer(_=>Expire(),null,executionLimit,Timeout.InfiniteTimeSpan);
        _wallTimer=time.CreateTimer(_=>Expire(),null,wallLimit,Timeout.InfiniteTimeSpan);
    }
    internal static void Validate(TimeSpan duration,string name)
    {
        if(duration<=TimeSpan.Zero || duration.TotalMilliseconds>uint.MaxValue-1)
            throw new ManifestValidationException(name,"must be a positive finite timer duration");
    }
    private static TimeSpan Max(TimeSpan value)=>value<TimeSpan.Zero?TimeSpan.Zero:value;
    private bool AdmissionOnly => _schedulerIdle && _waiting>0 && _waiting==_activities;
    private void Update()
    {
        var now=_time.GetTimestamp(); var elapsed=_time.GetElapsedTime(_last,now); _last=now;
        _wall+=elapsed; if(AdmissionOnly) _admission+=elapsed;
    }
    private void Expire()
    {
        lock(_gate)
        {
            Update();
            if(_reason is not null) return;
            if(_wall>=_wallLimit) _reason="hard-wall-deadline-exceeded";
            else if(_wall-_admission>=_executionLimit) _reason="execution-budget-exceeded";
            if(_reason is not null) _ended.Cancel();
            else Rearm();
        }
    }
    private void Rearm()
    {
        if(_reason is not null) return;
        var remaining=Max(_executionLimit-(_wall-_admission));
        _executionTimer.Change(AdmissionOnly?Timeout.InfiniteTimeSpan:remaining,Timeout.InfiniteTimeSpan);
    }
    public void SchedulerIdle(bool idle)
    {
        lock(_gate) { Update(); _schedulerIdle=idle; Rearm(); }
    }
    public Activity EnterActivity()
    {
        lock(_gate) { Update(); _activities++; Rearm(); return new Activity(this); }
    }
    public void Capture(RunState state)
    {
        lock(_gate)
        {
            Update(); state.BudgetWallSeconds=_wall.TotalSeconds; state.AdmissionOnlySeconds=_admission.TotalSeconds;
            state.ExecutionSeconds=(_wall-_admission).TotalSeconds; state.ExecutionLimitSeconds=_executionLimit.TotalSeconds;
            state.HardWallLimitSeconds=_wallLimit.TotalSeconds; state.RemainingExecutionSeconds=Max(_executionLimit-(_wall-_admission)).TotalSeconds;
            state.HardWallTimeoutAt=HardWallTimeoutAt;
            if(_reason is not null) state.Reason=_reason;
        }
    }
    public void Dispose() { _wallTimer.Dispose(); _executionTimer.Dispose(); _ended.Dispose(); }
    internal sealed class Activity(RunBudgetClock owner):IDisposable
    {
        private bool _waiting, _disposed;
        public void Admission(bool waiting)
        {
            lock(owner._gate)
            {
                if(_disposed) return;
                owner.Update(); if(_waiting!=waiting) owner._waiting+=waiting?1:-1;
                _waiting=waiting; owner.Rearm();
            }
        }
        public void Dispose()
        {
            lock(owner._gate)
            {
                if(_disposed) return;
                owner.Update(); owner._activities--; if(_waiting) owner._waiting--;
                _disposed=true; owner.Rearm();
            }
        }
    }
}

internal static class BudgetAdmission
{
    // Also bounds custom clients: a canceled acquisition is joined, never abandoned.
    internal static async Task<SlotLease> AcquireAsync(IBuildSlotClient slots, SlotSession session, string label,
        RunBudgetClock? budget, RunBudgetClock.Activity? activity, TimeProvider time, TimeSpan wait,
        Action<int>? observedWait, CancellationToken cancellationToken)
    {
        RunBudgetClock.Validate(wait,"slotWaitMinutes");
        using var deadline=new CancellationTokenSource(wait,time);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,deadline.Token);
        var started=time.GetTimestamp();
        activity?.Admission(true);
        try
        {
            var lease=await slots.AcquireAsync(session,label,linked.Token).ConfigureAwait(false);
            observedWait?.Invoke(Math.Max(lease.WaitedSeconds,(int)time.GetElapsedTime(started).TotalSeconds));
            if(linked.IsCancellationRequested)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return Expired((int)time.GetElapsedTime(started).TotalSeconds);
            }
            return lease;
        }
        catch(OperationCanceledException) when(deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        { return Expired((int)time.GetElapsedTime(started).TotalSeconds); }
        finally
        {
            observedWait?.Invoke((int)time.GetElapsedTime(started).TotalSeconds);
            activity?.Admission(false);
        }
    }
    private static SlotLease Expired(int waited)=>new()
    {
        State="timeout",ExitCode=ExitCodes.SlotTimeout,WaitedSeconds=waited,
        SlotReason="slot-admission-deadline-exceeded",MaxCpuCount=0,
    };
}
