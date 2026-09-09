using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>Retained candidate identity/readiness. No configuration write can confer qualification.</summary>
public class StandingSpecialistCandidateState
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public AgentKind AgentKind { get; set; }
    public AgentModelLevel ModelLevel { get; set; }
    public Guid? PhysicalAgentId { get; set; }
    public bool Enabled { get; set; }
    public string ModelAlias { get; set; } = string.Empty;
    public StandingSpecialistCandidateStatus Status { get; set; }
    public string? Reason { get; set; }
    public DateTime DeclaredAt { get; set; }
    public DateTime? UnprovisionedAt { get; set; }
    public DateTime? LastAdmissionRefusedAt { get; set; }
    public DateTime? NextEligibleAt { get; set; }
    public Guid? SessionId { get; set; }
    public DateTime? SessionStartedAt { get; set; }
    public Guid? ProfileRevisionId { get; set; }
    public string? Fingerprint { get; set; }
    public string? CapabilityFingerprint { get; set; }
    public int? MaxInputUtf8Bytes { get; set; }
    public string? QualificationEvidenceJson { get; set; }
    public DateTime? QualifiedAt { get; set; }
    public int TransientFailures { get; set; }
    public Guid QualificationAuthorization { get; set; }
    public Guid? ClaimedQualificationAuthorization { get; set; }
    public DateTime UpdatedAt { get; set; }
}
