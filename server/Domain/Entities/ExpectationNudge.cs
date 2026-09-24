using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Immutable prompt body for one directive ordinal, committed with its audit or not at all.
/// S1 does not deliver it. S4 owns direct send; S5 owns the operator outbox.
/// </summary>
public class ExpectationNudge
{
    public Guid Id { get; set; }
    public string DirectiveId { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string EpisodeIdsJson { get; set; } = "[]";
    public string EvidenceSnapshot { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string BodyDigest { get; set; } = string.Empty;
    public Guid? DestinationSessionId { get; set; }
    public DateTime? DestinationGeneration { get; set; }
    public long? BaselineSequence { get; set; }
    public ExpectationAttemptState AttemptState { get; set; }
    public DateTime? ReceiptAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public ExpectationOperatorOutboxState OperatorOutboxState { get; set; }
    public Guid? OperatorChannelId { get; set; }
    public DateTime? OperatorPublishedAt { get; set; }
    public int OperatorAttemptCount { get; set; }
    public DateTime? OperatorNextAttemptAt { get; set; }
    public string? OperatorLastError { get; set; }
    public Guid AuditCommentId { get; set; }
    public string CheckEventIdsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
