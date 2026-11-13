namespace Antiphon.Tests.Checkpoints;

// rc run 97150a0b g109: deadline for a fixture child that works through a known list of steps.
// RED STUB: mirrors the pre-fix shape, one fixed budget over the whole child.
internal sealed class CheckpointStepDeadline
{
    private readonly TimeProvider _time;
    private readonly TimeSpan _startup;
    private readonly long _started;

    public CheckpointStepDeadline(int total, TimeSpan startup, TimeSpan perStep, TimeProvider time)
    {
        _ = total; _ = perStep;
        _time = time;
        _startup = startup;
        _started = time.GetTimestamp();
    }

    public int Completed { get; private set; }

    public void Observe(int completed)
    {
        if (completed > Completed) Completed = completed;
    }

    public bool Expired => _time.GetElapsedTime(_started) > _startup;

    public string Describe(IReadOnlyList<string> steps) => "exceeded " + _startup.TotalMilliseconds + " ms";
}
