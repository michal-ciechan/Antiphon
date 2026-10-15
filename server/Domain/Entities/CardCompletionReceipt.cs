namespace Antiphon.Server.Domain.Entities;

/// <summary>Durable correlation of a committed terminal move with its acceptance authority.</summary>
public sealed class CardCompletionReceipt
{
    public Guid Id { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public Guid CardId { get; set; }
    public Guid OperationId { get; set; }
    public int AcceptanceRevision { get; set; }
    public Guid AcceptanceId { get; set; }
    public Guid ImplementationTaskId { get; set; }
    public Guid ReviewEvidenceId { get; set; }
    public string ReviewedSourceSha { get; set; } = "";
    public string LandedSha { get; set; } = "";
    public Guid OriginalCardToken { get; set; }
    public Guid ResultCardToken { get; set; }
    public Guid MoveRevisionId { get; set; }
    public string Actor { get; set; } = "";
    public DateTime RecordedAt { get; set; }
}
