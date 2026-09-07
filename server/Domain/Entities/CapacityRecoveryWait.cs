using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One capacity-recovery episode for a logical consumer (CARD-0412). Id is the episode Id.
/// A new wall in an unfinished episode updates the blocker/action, not this identity.
/// </summary>
public class CapacityRecoveryWait
{
    public Guid Id { get; set; }

    public CapacityWaitConsumerKind ConsumerKind { get; set; }

    /// <summary>Stable consumer identity. Unique among unfinished episodes.</summary>
    public string ConsumerKey { get; set; } = string.Empty;

    public DateTime BlockedAt { get; set; }

    public Guid? AgentId { get; set; }
    public Guid? SessionId { get; set; }
    public DateTime? SessionStartedAt { get; set; }
    public Guid? TaskId { get; set; }
    public int? TaskAttempt { get; set; }
    public Guid? CardId { get; set; }

    public AgentKind RequestedKind { get; set; }
    public string? RequestedAlias { get; set; }

    /// <summary>Admission bucket — the kind whose provider clock this wait consumes.</summary>
    public AgentKind ExecutionKind { get; set; }

    public int ActionOrdinal { get; set; }
    public string ActionKey { get; set; } = string.Empty;

    public CapacityRecoveryWaitState State { get; set; } = CapacityRecoveryWaitState.WaitingForHold;

    public DateTime? DueAt { get; set; }
    public int AdmissionCount { get; set; }

    public Guid? SelectedMessageId { get; set; }
    public Guid? LaunchSessionId { get; set; }
    public string? LaunchReceipt { get; set; }
    public Guid? DispatchAttemptId { get; set; }

    public long? ConfirmedPromptSequence { get; set; }
    public string? Outcome { get; set; }
    public string? OutcomeReason { get; set; }

    public int Version { get; set; }

    public string? RefusalDigest { get; set; }
    public string? AuthorizationSnapshot { get; set; }

    public CapacityRecoveryCompatibilityResult? CompatibilityResult { get; set; }
    public int CompatibilityVersion { get; set; }

    public DateTime? LatestClearObservedAt { get; set; }
    public string? ObservedClearCauses { get; set; }

    public bool NeedsRevalidationGrant { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<CapacityRecoveryWaitHold> Holds { get; set; } = new List<CapacityRecoveryWaitHold>();
}
