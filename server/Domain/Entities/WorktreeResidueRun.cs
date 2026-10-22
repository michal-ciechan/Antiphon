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

    /// <summary>CARD-1033 D-3. Null on rows written before admission existed. Unique with <see cref="RunId"/> when set.</summary>
    public string? ActionKey { get; set; }

    /// <summary>CARD-1033 D-3. Null until this run accepts the candidate. A migration must not fill it.</summary>
    public DateTime? AcceptedAt { get; set; }

    public Guid? CardId { get; set; }
    public Guid? DoneRevisionId { get; set; }
    public int? TaskAttempt { get; set; }
    public Guid? TargetId { get; set; }
    public Guid? EndpointId { get; set; }
    public string? EndpointIdentity { get; set; }
    public string? RunnerId { get; set; }
    public string? RunnerStoreId { get; set; }
    public Guid? OperationId { get; set; }
    public string? RequestDigest { get; set; }
    public CardWorktreeCleanupEndpointState? EndpointState { get; set; }
    public bool? Retryable { get; set; }
    public DateTime? NextAttemptAt { get; set; }
}

/// <summary>Persisted fairness/cooldown cursor keyed by candidate identity.</summary>
public sealed class WorktreeResidueCandidateCursor
{
    public Guid Id { get; set; }
    public string CandidateKey { get; set; } = "";
    public DateTime LastEvaluatedAt { get; set; }
    public DateTime? NotBefore { get; set; }

    /// <summary>CARD-1033 D-4. Digest of a permanent safety refusal. Null until that refusal is stored.</summary>
    public string? EvidenceDigest { get; set; }

    /// <summary>
    /// CARD-1446 D-5. <c>&lt;Lane&gt;|&lt;Outcome&gt;|&lt;ReasonCode&gt;</c> of the last non-preview evaluation of this
    /// candidate. A <c>Held</c> candidate row is written only when the key changes; action rows are always written
    /// and preview runs neither read nor write it. Null before the first evaluation after the column existed.
    /// </summary>
    public string? LastOutcomeKey { get; set; }
}
