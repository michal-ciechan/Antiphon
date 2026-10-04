using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>Durable preparation and publication intent for one reply to one conversation.</summary>
public sealed class ChannelOutboundDelivery
{
    public Guid Id { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public Guid ChannelId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid InboundAgentId { get; set; }
    public Guid SourceSessionId { get; set; }
    public long PromptSequence { get; set; }
    public long FirstTextSequence { get; set; }
    public long LastTextSequence { get; set; }
    public string SendKind { get; set; } = string.Empty;
    /// <summary>Versioned original intent and preparation descriptors; null on legacy deliveries.</summary>
    public string? CaptureJson { get; set; }
    /// <summary>Trailing fragments retain their original main/machine delivery as their root.</summary>
    public Guid? RootDeliveryId { get; set; }
    /// <summary>Last text sequence reserved by this root, committed together with its fragment.</summary>
    public long? ReservedThroughSequence { get; set; }
    /// <summary>Set only after the complete original prompt window has been examined.</summary>
    public DateTime? TailClosedAt { get; set; }
    public Guid? SourceTaskId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public Guid ConverterAgentId { get; set; }
    public string PromptRevision { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public int MaxPending { get; set; }
    public string InputPath { get; set; } = string.Empty;
    public string InputSha256 { get; set; } = string.Empty;
    public string? OutputPath { get; set; }
    public string? OutputSha256 { get; set; }
    public Guid? ConversionTaskId { get; set; }
    public ChannelOutboundDeliveryState State { get; set; } = ChannelOutboundDeliveryState.Pending;
    public long Version { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime DeadlineAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int PublicationAttempts { get; set; }
    /// <summary>Lifetime attempt count at the start of the current explicit authorization.</summary>
    public int PublicationAttemptBudgetBase { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public int PreparationAttempts { get; set; }
    /// <summary>Original obligation age bound, separate from the conversion deadline.</summary>
    public DateTime? PreparationDeadlineAt { get; set; }
    /// <summary>Projection completion after acceptance; null means repair, never permission to resend.</summary>
    public DateTime? MetadataAppliedAt { get; set; }
    public int FailureEpisode { get; set; }
    /// <summary>Durable loss dedupe survives pruning of incident history.</summary>
    public int FailureReportedEpisode { get; set; }
    public string? FailureReason { get; set; }
    public string? ConversionOutcome { get; set; }
    public ChatChannel Channel { get; set; } = null!;
}
