namespace Antiphon.Tests.Checkpoints;

// rc run 97150a0b g109: deadline for a fixture child that works through a known list of steps.
// `startup` covers process start, script load and the first step; after that each further step
// gets `perStep` from the previous one. Total time follows host load instead of failing on it,
// and a child that stops is caught within one budget with the phase it stopped in.
internal sealed class CheckpointStepDeadline
{
    private readonly int _total;
    private readonly TimeSpan _startup;
    private readonly TimeSpan _perStep;
    private readonly TimeProvider _time;
    private readonly long _started;
    private long _armed;
    private TimeSpan _firstStep;
    private TimeSpan _slowestStep;

    public CheckpointStepDeadline(int total, TimeSpan startup, TimeSpan perStep, TimeProvider time)
    {
        _total = total;
        _startup = startup;
        _perStep = perStep;
        _time = time;
        _started = _armed = time.GetTimestamp();
    }

    public int Completed { get; private set; }

    public void Observe(int completed)
    {
        if (completed <= Completed) return;
        var now = _time.GetTimestamp();
        var gap = _time.GetElapsedTime(_armed, now);
        if (Completed == 0) _firstStep = gap;
        else if (gap > _slowestStep) _slowestStep = gap;
        Completed = completed;
        _armed = now;
    }

    public bool Expired => _time.GetElapsedTime(_armed) > (Completed == 0 ? _startup : _perStep);

    public string Describe(IReadOnlyList<string> steps)
    {
        var elapsed = (int)_time.GetElapsedTime(_started).TotalMilliseconds;
        if (Completed == 0)
            return "before the first step finished (process start, script load, first step) after " + elapsed
                + " ms; startup budget " + (int)_startup.TotalMilliseconds + " ms; 0/" + _total + " steps";
        var phase = Completed >= _total ? "finished " + Completed + "/" + _total + " steps"
            : "after " + Completed + "/" + _total + " steps; stalled in step '" + (Completed < steps.Count ? steps[Completed] : "?")
              + "' beyond the per-step budget " + (int)_perStep.TotalMilliseconds + " ms";
        return phase + "; first step at " + (int)_firstStep.TotalMilliseconds + " ms, slowest later step "
            + (int)_slowestStep.TotalMilliseconds + " ms, elapsed " + elapsed + " ms";
    }
}
