using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class SpecialistRequest
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public Guid? CheckedTaskId { get; set; }
    public int CheckNumber { get; set; }
    public SpecialistRequestPurpose Purpose { get; set; }
    public SpecialistRequestStatus Status { get; set; }
    public SpecialistAttemptOutcome? Outcome { get; set; }
    public Guid? ConfigurationRevision { get; set; }
    public Guid? QualificationCandidateId { get; set; }
    public Guid? QualificationAuthorization { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime DeadlineAt { get; set; }
    public string Title { get; set; } = "";
    public string Facts { get; set; } = "";
    public string? FactsSnapshotJson { get; set; }
    public Guid? WinnerAttemptId { get; set; }
    public string? Reading { get; set; }
    public string? Reason { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? HealthAppliedAt { get; set; }
    public Guid? CallerMessageId { get; set; }
    public DateTime? CallerPublishedAt { get; set; }
}

public class SpecialistAttempt
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid CandidateId { get; set; }
    public Guid PhysicalAgentId { get; set; }
    public Guid TaskId { get; set; }
    public int Ordinal { get; set; }
    public Guid SessionId { get; set; }
    public DateTime SessionStartedAt { get; set; }
    public string Fingerprint { get; set; } = "";
    public string CapabilityFingerprint { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime DeadlineAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public SpecialistAttemptOutcome? Outcome { get; set; }
    public string? Reason { get; set; }
    public string? Reading { get; set; }
    public long? PromptSequence { get; set; }
    public long? ReportSequence { get; set; }
    public decimal CostUsd { get; set; }
}
