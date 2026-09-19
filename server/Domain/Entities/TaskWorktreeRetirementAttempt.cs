namespace Antiphon.Server.Domain.Entities;

/// <summary>One bounded retirement execution, including a spent command slot that never resets.</summary>
public sealed class TaskWorktreeRetirementAttempt
{
    public Guid Id { get; set; }
    public Guid RetirementId { get; set; }
    public Guid? SweepRunId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime NotBefore { get; set; }
    public Guid ReleasedTaskRevision { get; set; }
    public Guid? CommandIntentId { get; set; }
    public DateTime? CommandIntentAt { get; set; }
    public string? CommandResult { get; set; }
    public bool? DirectoryRemoved { get; set; }
    public bool? RegistrationRemoved { get; set; }
    public bool? BranchRemoved { get; set; }
    public string? Residue { get; set; }
}
