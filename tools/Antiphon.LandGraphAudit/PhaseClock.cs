namespace Antiphon.LandGraphAudit;

/// <summary>One wait per phase. A single wait around pass 1, the copy and pass 2 is the combined-clock failure.</summary>
internal static class PhaseClock
{
    public static int Bound(TimeSpan budget, string solution, string phase, Func<int> work, Action? onTimeout = null)
    {
        var task = Task.Run(work);
        if (task.Wait(budget)) return task.GetAwaiter().GetResult();
        try { onTimeout?.Invoke(); }
        catch (Exception) { }
        CanaryRepository.DeleteActive();
        Program.Fail($"evaluating {solution} {phase} exceeded {budget.TotalSeconds:0} s");
        Environment.Exit(Program.EvaluationFailed);
        return Program.EvaluationFailed;
    }

    public static CanaryRepository BoundCopy(TimeSpan budget, string solution, Func<CanaryRepository> work)
    {
        var task = Task.Run(work);
        if (task.Wait(budget)) return task.GetAwaiter().GetResult();
        CanaryRepository.CancelCopy();
        try { task.Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception) { }
        CanaryRepository.DeleteActive();
        Program.Fail($"evaluating {solution} copy exceeded {budget.TotalSeconds:0} s");
        Environment.Exit(Program.EvaluationFailed);
        return null!;
    }
}

/// <summary>Test-only phase timings. The audit script never passes these on a real check.</summary>
internal sealed record WitnessRequest(
    TimeSpan Pass,
    TimeSpan Copy,
    TimeSpan HoldPass1,
    TimeSpan HoldCopy,
    TimeSpan HoldPass2,
    string? TouchRelative);
