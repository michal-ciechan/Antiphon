namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// CARD-0079 lifecycle of one silent auto-compaction continuation episode.
/// Values are append-only. Unresolved rows stay out of ordinary retention.
/// </summary>
public enum CheckCompactionRecoveryState
{
    Confirmed = 0,
    AbortedProgress = 1,
    Superseded = 2,
    NeedsDecision = 3,
    StopRequested = 4,
    Stopped = 5,
    ResumeReserved = 6,
    AwaitingCheck = 7,
    Recovered = 8,
    SupersededByOperator = 9,
    DisabledNeedsDecision = 10,
}

public static class CheckCompactionRecoveryStates
{
    public static readonly CheckCompactionRecoveryState[] Unresolved =
    [
        CheckCompactionRecoveryState.Confirmed,
        CheckCompactionRecoveryState.NeedsDecision,
        CheckCompactionRecoveryState.StopRequested,
        CheckCompactionRecoveryState.Stopped,
        CheckCompactionRecoveryState.ResumeReserved,
        CheckCompactionRecoveryState.AwaitingCheck,
        CheckCompactionRecoveryState.DisabledNeedsDecision,
    ];

    public static bool IsUnresolved(CheckCompactionRecoveryState state)
    {
        foreach (var candidate in Unresolved)
        {
            if (candidate == state)
                return true;
        }

        return false;
    }

    /// <summary>Terminal action audit. Retained at least 90 days, then eligible for pruning.</summary>
    public const int TerminalAuditDays = 90;
}
