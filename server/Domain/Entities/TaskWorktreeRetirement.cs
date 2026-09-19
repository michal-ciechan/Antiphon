using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>One caller-reviewed workspace release and the retirement operation it authorizes.</summary>
public sealed class TaskWorktreeRetirement
{
    public Guid Id { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public Guid TaskId { get; set; }
    public int TaskAttempt { get; set; }
    public AgentTaskStatus TerminalStatus { get; set; }
    public DateTime TaskCompletedAt { get; set; }
    public string? ReportDigest { get; set; }
    public bool MissingReportReviewed { get; set; }
    public Guid ReleasedTaskRevision { get; set; }
    public Guid? CallerSessionId { get; set; }
    public Guid? CallerTaskId { get; set; }
    public string CallerIdentity { get; set; } = "";
    public string ReleaseReason { get; set; } = "";
    public DateTime ReleasedAt { get; set; }
    public string HandoffDispositionJson { get; set; } = "[]";
    public string RepositoryPath { get; set; } = "";
    public string CommonDirectory { get; set; } = "";
    public string WorktreePath { get; set; } = "";
    public string GitDirectory { get; set; } = "";
    public string SourceFullRef { get; set; } = "";
    public string SourceSha { get; set; } = "";
    public string TargetFullRef { get; set; } = "";
    public string RemoteName { get; set; } = "origin";
    public string DestinationFullRef { get; set; } = "";
    public string RemoteFingerprint { get; set; } = "";
    public string? ObservedTargetSha { get; set; }
    public string? ResultPreservationPath { get; set; }
    public string? DeliverablePreservationPath { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public Guid? ClaimAttemptId { get; set; }
    public DateTime? CommandStartedAt { get; set; }
    public Guid? CommandIntentId { get; set; }
    public DateTime? DirectoryRemovedAt { get; set; }
    public DateTime? RegistrationRemovedAt { get; set; }
    public DateTime? BranchRemovedAt { get; set; }
    public DateTime? RetirementCompletedAt { get; set; }
    public WorktreeRetirementState State { get; set; } = WorktreeRetirementState.Released;
    public bool Active { get; set; } = true;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTime UpdatedAt { get; set; }
    public string? LastReason { get; set; }
}
