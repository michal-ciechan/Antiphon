namespace Antiphon.Server.Domain.Entities;

/// <summary>Append-only history of explicit caller decisions and admission refusals.</summary>
public sealed class PipelineHandoffObservation
{
    public Guid Id { get; set; }
    public Guid ActionId { get; set; }
    public long Sequence { get; set; }
    public string ReasonCode { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTime ObservedAt { get; set; }
    public DateTime? NextObservationAt { get; set; }
}
