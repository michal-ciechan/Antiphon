using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>One immutable producer identity and independently recoverable delivery debt.</summary>
public sealed class TaskSupervisionNotice
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public int Attempt { get; set; }
    public Guid SourceEventId { get; set; }
    public TaskSupervisionNoticePurpose Purpose { get; set; }
    public Guid? EpisodeId { get; set; }
    public Guid? SessionId { get; set; }
    public DateTime? SessionStartedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public Guid? ParentSessionId { get; set; }
    public DateTime? ParentStartedAt { get; set; }
    public Guid? RecipientSessionId { get; set; }
    public DateTime? RecipientStartedAt { get; set; }
    public long RecipientFloor { get; set; }
    public string Body { get; set; } = "";
    public string Digest { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public TaskSupervisionNoticeState State { get; set; }
    public string? ReasonCode { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public Guid? QueueMessageId { get; set; }
    public DateTime? ToldAt { get; set; }
    public long? ConfirmingPromptSequence { get; set; }
    public long FailureFrontier { get; set; }
    public long ReportFloor { get; set; }
    public long ScanFrontier { get; set; }
    public string? ReportText { get; set; }
    public string? ReportDigest { get; set; }
    public long? ReportFromSequence { get; set; }
    public long? ReportThroughSequence { get; set; }
    public DateTime? ReportForwardedAt { get; set; }
    public DateTime? ReportWatchClosedAt { get; set; }
    public string? ReportWatchCloseReason { get; set; }
    public DateTime? NextReportScanAt { get; set; }
    public int? ReportScanAttempts { get; set; }
}
