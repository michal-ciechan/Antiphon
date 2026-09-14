namespace Antiphon.Server.Domain.Entities;

public enum WorktreeCleanupCaptureState { NotNeeded, Pending, Captured, Interrupted }

/// <summary>One request's diagnostic history. Slot intents survive uncertainty and never reset.</summary>
public sealed class WorktreeCleanupAttempt
{
    public Guid Id { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public Guid RequestId { get; set; }
    public Guid OperationId { get; set; }
    public Guid TaskId { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public string RepositoryPath { get; set; } = "";
    public string WorktreePath { get; set; } = "";
    public string CommonDirectory { get; set; } = "";
    public string GitDirectory { get; set; } = "";
    public string SourceFullRef { get; set; } = "";
    public string TargetFullRef { get; set; } = "";
    public string SourceSha { get; set; } = "";
    public string TargetSha { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public Guid? InitialCommandId { get; set; }
    public DateTime? InitialIntentAt { get; set; }
    public DateTime? InitialCompletedAt { get; set; }
    public Guid? RetryCommandId { get; set; }
    public DateTime? RetryIntentAt { get; set; }
    public DateTime? RetryCompletedAt { get; set; }
    public string? FirstGitFailureJson { get; set; }
    public string? LastGitOutcomeJson { get; set; }
    public WorktreeCleanupCaptureState CaptureState { get; set; }
    public DateTime? CaptureAt { get; set; }
    public string? CaptureJson { get; set; }
    public string? Summary { get; set; }
    public string? RetryReason { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public Guid? TerminalEventId { get; set; }
    public bool DirectoryGone { get; set; }
    public bool Unregistered { get; set; }
    public bool BranchDeleted { get; set; }
    public string? Residue { get; set; }
}
