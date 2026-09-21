using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-0079 captured legacy Check observation. Identity and Produced payload
/// are immutable; bookkeeping may move without rewriting them.
/// </summary>
public class LegacyCheckNotePublication
{
    public Guid Id { get; set; }
    public Guid CheckedTaskId { get; set; }
    public int CheckedTaskAttempt { get; set; }
    public DateTime CheckedTaskDispatchedAt { get; set; }
    public int CheckNumber { get; set; }
    public Guid RecoveryId { get; set; }
    public Guid PhysicalAgentId { get; set; }
    public Guid InterpreterSessionId { get; set; }
    public DateTime InterpreterAcceptedStartedAt { get; set; }
    public Guid ParentSessionId { get; set; }
    public DateTime CapturedAt { get; set; }
    public string FactsSnapshotJson { get; set; } = "";
    public string RenderContextJson { get; set; } = "";
    public Guid? InterpretationTaskId { get; set; }
    public DateTime InterpretationDeadlineAt { get; set; }
    public string? InterpretationSnapshotJson { get; set; }
    public LegacyCheckNoteState State { get; set; }
    public Guid SourceEventId { get; set; }
    public Guid NotificationId { get; set; }
    public DateTime? ProducedAt { get; set; }
    public string? Body { get; set; }
    public string? ContentDigest { get; set; }
    public string? EventDetail { get; set; }
    public string? SuppressionReason { get; set; }
    public DateTime? SuppressedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    public CheckCompactionRecovery? Recovery { get; set; }
}
