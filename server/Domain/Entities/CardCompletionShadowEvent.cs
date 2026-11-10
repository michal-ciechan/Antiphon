namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One shadow publication-close proposal. A later adjudication fills the expected close;
/// this row is not a completion receipt and does not move the card.
/// </summary>
public sealed class CardCompletionShadowEvent
{
    public Guid Id { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public Guid CardId { get; set; }
    public Guid AcceptanceId { get; set; }
    public Guid OperationId { get; set; }
    public bool ProposedClose { get; set; }
    public bool? ExpectedClose { get; set; }
    public string? Adjudicator { get; set; }
    public DateTime? AdjudicatedAt { get; set; }
    public string Actor { get; set; } = "";
    public DateTime RecordedAt { get; set; }
}
