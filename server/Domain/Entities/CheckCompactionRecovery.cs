using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One automatic compaction-continuation recovery, keyed by the physical seat,
/// session, accepted generation and native boundary. The row is the audit;
/// incident pruning cannot recreate or erase it.
/// </summary>
public class CheckCompactionRecovery
{
    public const int EvidenceMaxLength = 1000;
    public const string Actor = "check-compaction-recovery";
    public const string Authorization = "CARD-0079/fed4b83c";

    public Guid Id { get; set; }
    public Guid PhysicalAgentId { get; set; }
    public Guid SessionId { get; set; }
    public DateTime AcceptedStartedAt { get; set; }
    public string BoundaryIdentity { get; set; } = "";
    public long? BoundarySequence { get; set; }
    public long? ContinuationSequence { get; set; }
    public string? NativeContinuationIdentity { get; set; }
    public Guid? OwningCheckTaskId { get; set; }
    public long? OrdinaryPromptSequence { get; set; }
    public DateTime? BoundaryTimestamp { get; set; }
    public DateTime BoundaryCreatedAt { get; set; }
    public DateTime? ContinuationTimestamp { get; set; }
    public DateTime ContinuationCreatedAt { get; set; }
    public int ConfiguredThresholdMinutes { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? ObservedAt { get; set; }
    public string? EvidenceJson { get; set; }
    public CheckCompactionRecoveryState State { get; set; }
    public string? Reason { get; set; }
    public Guid? AttemptId { get; set; }
    public DateTime? StopRequestedAt { get; set; }
    public DateTime? StopOutcomeAt { get; set; }
    public Guid? ResumeSessionId { get; set; }
    public DateTime? ResumeAcceptedStartedAt { get; set; }
    public string? LaunchOutcome { get; set; }
    public Guid? UsefulCheckTaskId { get; set; }
    public Guid? CallerReceiptNotificationId { get; set; }
    public long? ConfirmingPromptSequence { get; set; }
    public string? ObservationBindingIdentity { get; set; }
    public long? ObservationTranscriptRevision { get; set; }
    public long? ObservationOutputRevision { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    public Agent? PhysicalAgent { get; set; }
}
