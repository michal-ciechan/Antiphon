namespace Antiphon.Server.Domain.Entities;

/// <summary>Disposable derived transcript metadata. Only valid, recognized versions may serve reads.</summary>
public sealed class SessionTranscriptSummary
{
    public Guid AgentSessionId { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public bool Valid { get; set; }
    public DateTime? NextBackfillAttemptAt { get; set; }
    public long Count { get; set; }
    public DateTime? NewestEffectiveTimestamp { get; set; }
    public long LastSequence { get; set; }
    public Guid? LastEntryId { get; set; }
    public string? LastKind { get; set; }
    public DateTime? LastTimestamp { get; set; }
    public DateTime? LastCreatedAt { get; set; }
    public long? EndSequence { get; set; }
    public DateTime? EndTimestamp { get; set; }
    public bool HasActivity { get; set; }
    public bool HasNullTimestampActivity { get; set; }
    public DateTime? ActivityTimestamp { get; set; }
    public long? LastTurnEndSequence { get; set; }
    public long? LastTurnTitleSequence { get; set; }
    public long? LastUserPromptSequence { get; set; }
}
