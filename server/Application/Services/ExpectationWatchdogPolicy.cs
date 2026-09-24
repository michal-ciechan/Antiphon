using Antiphon.Server.Application.Settings;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// No-op until the S2 detection commit. A fenced queue produces no condition.
/// </summary>
public static class ExpectationWatchdogPolicy
{
    public static ExpectationEvaluation Evaluate(ExpectationSnapshot snapshot, ExpectationDirectiveSettings directive) =>
        new();
}
