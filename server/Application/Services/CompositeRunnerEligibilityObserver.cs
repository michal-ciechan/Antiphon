using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0822. Fan-out for <see cref="IRunnerEligibilityObserver"/>: the alarm queue keeps its
/// existing wake, and the instructions file hears the same eligibility change.
/// </summary>
public sealed class CompositeRunnerEligibilityObserver(
    AlarmWakeQueue wake,
    IOrchestratorInstructionsSignals signals) : IRunnerEligibilityObserver
{
    public void Changed(string runnerId)
    {
        wake.Changed(runnerId);
        signals.Signal($"runner-eligibility {runnerId}");
    }
}
