namespace Antiphon.Server.Domain.Entities;

/// <summary>One durable residue inventory or executing sweep.</summary>
public sealed class WorktreeResidueRun
{
    public Guid Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public bool Execute { get; set; }
    public bool Preview { get; set; }
    public int ActionBudget { get; set; }
    public int ActionsAccepted { get; set; }
    public int Candidates { get; set; }
    public int Held { get; set; }
    public int Deferred { get; set; }
    public int Queued { get; set; }
    public int Refused { get; set; }
    public int Partial { get; set; }
    public int Removed { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? BoardId { get; set; }
    public string? Scope { get; set; }
}

/// <summary>One classified candidate belonging to a residue run.</summary>
public sealed class WorktreeResidueRunCandidate
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string Lane { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public Guid? TaskId { get; set; }
    public Guid? RetirementId { get; set; }
    public Guid? LandingOperationId { get; set; }
    public Guid? LandRequestId { get; set; }
    public string? Path { get; set; }
    public string? Branch { get; set; }
    public bool? DirectoryRemoved { get; set; }
    public bool? RegistrationRemoved { get; set; }
    public bool? BranchRemoved { get; set; }
    public DateTime EvaluatedAt { get; set; }
}

/// <summary>Persisted fairness/cooldown cursor keyed by candidate identity.</summary>
public sealed class WorktreeResidueCandidateCursor
{
    public Guid Id { get; set; }
    public string CandidateKey { get; set; } = "";
    public DateTime LastEvaluatedAt { get; set; }
    public DateTime? NotBefore { get; set; }
}
