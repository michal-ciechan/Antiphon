namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-1082 D-5. One settled attempt's owed desktop fast-forward. The sweep owns the mutable
/// state; settlement evidence stays immutable. No cascading foreign keys.
/// </summary>
public enum AgentTaskSyncDebtState
{
    Pending = 0,
    Ready = 1,
    Held = 2,
    Superseded = 3,
}

public sealed class AgentTaskSyncDebt
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public int Attempt { get; set; }
    public Guid SettlementEventId { get; set; }
    public string? RunnerId { get; set; }
    public string? WorktreePath { get; set; }
    public string? RemoteWorktreePath { get; set; }
    public string? RepositoryPath { get; set; }
    public string? FullRef { get; set; }
    public string? BaselineSha { get; set; }
    public string? SourceSha { get; set; }
    public string? DesktopBeforeSha { get; set; }
    public string? EndpointFingerprint { get; set; }
    public AgentTaskSyncDebtState State { get; set; }
    public string ReasonCode { get; set; } = "runner_sync_lease_busy";
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? SourceReadyAt { get; set; }
    public string? ConfirmedSha { get; set; }
    public long Revision { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
