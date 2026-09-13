using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-0418: local work record for an asynchronous outbound conversion step. Not a second
/// messaging bus. File bytes live under the server-owned outbound store, not a task worktree.
/// </summary>
public class ChannelOutboundDelivery
{
    public Guid Id { get; set; }

    /// <summary>
    /// Unique source/destination key: session, owning prompt sequence, text window, send kind and target.
    /// </summary>
    public string SourceKey { get; set; } = "";

    public Guid? SessionId { get; set; }
    public long? PromptSequence { get; set; }
    public long? TextWindowStart { get; set; }
    public long? TextWindowEnd { get; set; }
    public ChannelOutboundSendKind SendKind { get; set; }
    public string ChannelProvider { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string? ReplyHandle { get; set; }
    public string? ReplyToMessageId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? ProjectId { get; set; }
    public ChannelOutboundOrigin Origin { get; set; }
    public ChannelOutboundDeliveryState State { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime DeadlineAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public string? LeaseOwner { get; set; }
    public Guid? ConversionTaskId { get; set; }
    public string? ProfileName { get; set; }
    public string? ProfileSnapshotJson { get; set; }
    public string InputHash { get; set; } = "";
    public string? FrozenReplyJson { get; set; }
    public string? OutputManifestJson { get; set; }
    public string? SealedPayloadJson { get; set; }
    public string? SealedPayloadHash { get; set; }
    public int PublishAttempts { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? PublishedAt { get; set; }
    public bool SourceComplete { get; set; }
    public bool ConversionSucceeded { get; set; }
}