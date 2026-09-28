namespace Antiphon.Server.Domain.Entities;

/// <summary>A frozen obligation to publish one turn interval to one captured channel target.</summary>
public sealed class ChannelOutboundPublication
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid? AgentId { get; set; }
    public long PromptSequence { get; set; }
    public long FirstTextSequence { get; set; }
    public long LastTextSequence { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string? ReplyHandle { get; set; }
    public string OriginalResponse { get; set; } = string.Empty;
    public string EnvelopeJson { get; set; } = string.Empty;
    public string State { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public int AttemptCount { get; set; }
    public Guid? AttemptOwner { get; set; }
    public DateTime? AttemptExpiresAt { get; set; }
    public string? LastFailure { get; set; }
    public string? FailureStage { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? MetadataStampedAt { get; set; }
    public string BundleTaskIdsJson { get; set; } = "[]";
    public Guid? IncidentId { get; set; }
    public List<ChannelOutboundPublicationSource> Sources { get; set; } = [];
}

/// <summary>The source queue row that owns a publication; retained until a durable outcome.</summary>
public sealed class ChannelOutboundPublicationSource
{
    public Guid PublicationId { get; set; }
    public Guid QueueMessageId { get; set; }
    public string Path { get; set; } = string.Empty;
    public long FirstTextSequence { get; set; }
    public long LastTextSequence { get; set; }
    public ChannelOutboundPublication Publication { get; set; } = null!;
}
