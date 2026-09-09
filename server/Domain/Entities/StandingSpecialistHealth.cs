using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class StandingSpecialistHealth
{
    public Guid AgentId { get; set; }
    public StandingSpecialistHealthStatus Status { get; set; }
    public Guid? ActiveCandidateId { get; set; }
    public DateTime? ActiveSince { get; set; }
    public DateTime? FirstFailureAt { get; set; }
    public DateTime? StarvedSince { get; set; }
    public DateTime? UnavailableSince { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? LastValidCheckAt { get; set; }
    public int ConsecutiveFailedRequests { get; set; }
    public Guid? LastRequestId { get; set; }
    public DateTime? LastRequestStartedAt { get; set; }
    public Guid? LastAttemptTaskId { get; set; }
    public string? Reason { get; set; }
    public string? CandidateSummary { get; set; }
    public DateTime UpdatedAt { get; set; }
}
