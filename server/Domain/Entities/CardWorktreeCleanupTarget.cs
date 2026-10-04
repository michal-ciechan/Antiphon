namespace Antiphon.Server.Domain.Entities;

/// <summary>One exact ordinary workspace of one attempt, linked to the existing local executor.</summary>
public sealed class CardWorktreeCleanupTarget
{
    public Guid Id { get; set; }
    public Guid CleanupId { get; set; }
    public Guid TaskId { get; set; }
    public int TaskAttempt { get; set; }
    public string WorkspaceIdentity { get; set; } = "";
    public string RepositoryPath { get; set; } = "";
    public string WorktreePath { get; set; } = "";
    public string SourceFullRef { get; set; } = "";
    public Guid? RetirementId { get; set; }
    public Guid? LandingOperationId { get; set; }
    public string? ExclusionReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public CardWorktreeCleanup Cleanup { get; set; } = null!;
    public ICollection<CardWorktreeCleanupEndpoint> Endpoints { get; set; } = [];
}

public enum CardWorktreeCleanupEndpointState { Pending, IntentRecorded, Partial, Complete, Refused, Revoked }

/// <summary>Local and each proven mirror retain separate command identities and outcomes.</summary>
public sealed class CardWorktreeCleanupEndpoint
{
    public Guid Id { get; set; }
    public Guid TargetId { get; set; }
    public string EndpointIdentity { get; set; } = "";
    public string? RunnerId { get; set; }
    public string? RunnerStoreId { get; set; }
    public string RepositoryPath { get; set; } = "";
    public string WorktreePath { get; set; } = "";
    public string SourceFullRef { get; set; } = "";
    public string? SourceSha { get; set; }
    public string? CommonDirectory { get; set; }
    public string? GitDirectory { get; set; }
    public string? ReportDigest { get; set; }
    public Guid? OperationId { get; set; }
    public string? RequestDigest { get; set; }
    public DateTime? IntentAt { get; set; }
    public CardWorktreeCleanupEndpointState State { get; set; }
    public string? Reason { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public bool Retryable { get; set; }
    public bool? DirectoryRemoved { get; set; }
    public bool? RegistrationRemoved { get; set; }
    public bool? BranchRemoved { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public CardWorktreeCleanupTarget Target { get; set; } = null!;
}
