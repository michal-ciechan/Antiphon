namespace Antiphon.LandGraphAudit;

/// <summary>
/// One wait per phase. The production tool supplies a wall clock; tests supply a clock whose
/// elapsed time moves only when the fake evaluator advances it. Neither clock can print approval.
/// </summary>
internal interface IPhaseClock
{
    /// <summary>Runs <paramref name="work"/>. Returns false when the budget elapses first.</summary>
    bool TryComplete(TimeSpan budget, Action work);
}

internal static class PhaseClock
{
    /// <summary>Starting result. Replaced only when the phase work returns.</summary>
    public const int Refusal = 2;

    public static int Bound(IPhaseClock clock, TimeSpan budget, string solution, string phase, Func<int> work,
        TextWriter output, Action? onTimeout = null)
    {
        var result = Refusal;
        if (clock.TryComplete(budget, () => result = work())) return result;
        try
        {
            onTimeout?.Invoke();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"canary delete failed: {ex.GetType().Name}: {ex.Message}");
        }

        output.WriteLine($"evaluation-failed: evaluating {solution} {phase} exceeded {budget.TotalSeconds:0} s");
        output.Flush();
        return Refusal;
    }
}

/// <summary>
/// Pass 1, the canary copy, then pass 2. Approval is printed here only after every phase
/// returned 0. A fake evaluator can return 0; it cannot skip this decision or emit approval itself.
/// </summary>
internal static class AuditPhases
{
    public const int Approved = 0;

    public static int Run(string solution, TextWriter output, IPhaseClock clock, TimeSpan passBudget,
        TimeSpan copyBudget, Func<int> pass1, Func<int> copy, Func<int> pass2, Action? onTimeout = null,
        IPhaseClock? copyClock = null)
    {
        var first = PhaseClock.Bound(clock, passBudget, solution, "pass 1", pass1, output, onTimeout);
        if (first != Approved) return first;
        var copied = PhaseClock.Bound(copyClock ?? clock, copyBudget, solution, "copy", copy, output, onTimeout);
        if (copied != Approved) return copied;
        var second = PhaseClock.Bound(clock, passBudget, solution, "pass 2", pass2, output, onTimeout);
        if (second != Approved) return second;
        output.WriteLine("approved");
        output.Flush();
        return Approved;
    }
}
