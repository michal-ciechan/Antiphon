namespace Antiphon.Server.Domain.Entities;

/// <summary>Declared routing for one logical specialist. Absence preserves its existing primary.</summary>
public class StandingSpecialistRouting
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string CandidatesJson { get; set; } = "[]";
    public bool Enabled { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
