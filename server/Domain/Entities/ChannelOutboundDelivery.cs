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
    public string? FailureReason { get; set; }
    public string? ConversionOutcome { get; set; }
    public ChatChannel Channel { get; set; } = null!;
}
