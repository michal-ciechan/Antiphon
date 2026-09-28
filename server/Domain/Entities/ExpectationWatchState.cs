namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One row per directive. Serializes episode and nudge writes and remembers the last good scan.
/// </summary>
public class ExpectationWatchState
{
    public Guid Id { get; set; }
    public string DirectiveId { get; set; } = string.Empty;
    public string ConfigDigest { get; set; } = string.Empty;
    public DateTime? LastSuccessfulScanAt { get; set; }
    public DateTime? NextNudgeAt { get; set; }
    public string? LastObservationError { get; set; }
    public DateTime? NoteOutboxCursorAt { get; set; }
    public Guid? NoteOutboxCursorId { get; set; }
    public DateTime? NoteQueueCursorAt { get; set; }
    public Guid? NoteQueueCursorId { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
