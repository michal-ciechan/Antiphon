using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public readonly record struct SpecialistFailureDecision(int TransientFailures, bool Quarantine, bool Reset);

/// <summary>
/// Pure candidate consequence only. The attempt owner must apply once at its terminal transition,
/// under the consumer lock, after proving the current fingerprint/generation and request deadline.
/// This policy neither grants qualification nor resolves logical interpreter health.
/// </summary>
public static class SpecialistFailurePolicy
{
    public static SpecialistFailureDecision Evaluate(
        SpecialistAttemptOutcome outcome, int currentFailures, int threshold,
        bool authoritativeCurrentTransition, bool candidateClaimed)
    {
        if (threshold is < 2 or > 10) throw new ArgumentOutOfRangeException(nameof(threshold));
        if (currentFailures < 0) throw new ArgumentOutOfRangeException(nameof(currentFailures));
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        // An expiry terminal transition itself is in-budget ownership evidence; an eventual late
        // answer is not. The orchestration owner supplies that distinction, never its text.
        if (!authoritativeCurrentTransition || !candidateClaimed) return new(currentFailures, false, false);
        return outcome switch
        {
            SpecialistAttemptOutcome.InvalidReading or SpecialistAttemptOutcome.Empty
                or SpecialistAttemptOutcome.ToolAttempt or SpecialistAttemptOutcome.IdentityMismatch
                or SpecialistAttemptOutcome.InputUnsupported => new(currentFailures, true, false),
            SpecialistAttemptOutcome.TimedOutAfterDispatch or SpecialistAttemptOutcome.DeliveryUnconfirmed
                or SpecialistAttemptOutcome.ExpiredBeforeDispatch or SpecialistAttemptOutcome.TaskFailedTransport
                or SpecialistAttemptOutcome.TaskFailedUnknown => new(
                    currentFailures < threshold ? currentFailures + 1 : currentFailures,
                    currentFailures >= threshold - 1, false),
            SpecialistAttemptOutcome.ValidReading or SpecialistAttemptOutcome.ValidQualificationBatch => new(0, false, true),
            _ => new(currentFailures, false, false),
        };
    }
}
