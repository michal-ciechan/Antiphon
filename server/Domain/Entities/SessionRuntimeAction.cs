namespace Antiphon.Server.Domain.Entities;

/// <summary>Durable post-commit work. Transcript rows remain the only receipt authority.</summary>
public sealed class SessionRuntimeAction
{
    public Guid Id { get; set; }
    public Guid AgentSessionId { get; set; }
    public Guid? TranscriptEntryId { get; set; }
    public DateTime? AcceptedGeneration { get; set; }
    public Guid? AcceptedTaskId { get; set; }
    public int? AcceptedTaskAttempt { get; set; }
    public long Sequence { get; set; }
    public long ProducerSequence { get; set; }
    public string TriggerKind { get; set; } = "transcript";
    public bool TurnBoundary { get; set; }
    public bool ManualCompactBoundary { get; set; }
    public int Phase { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime DueAt { get; set; }
    public int Attempts { get; set; }
    public string? FailureReason { get; set; }
    public int UiProjectionVersion { get; set; } = 1;
    public byte[] UiProjectionDelta { get; set; } = [];
    public Guid[] LateConfirmedChannelMessageIds { get; set; } = [];
    /// <summary>
    /// CARD-1434: the destination (<c>{provider}:{conversationId}</c>) a channel debt row publishes to.
    /// Null means every destination: the turn addressed more than one conversation, or none is known.
    /// </summary>
    public string? ConversationKey { get; set; }
}
