using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>One OS process from the WMI (or injected) census. CARD-0298 Class B.</summary>
public sealed record ZombieOsProcess(
    int ProcessId,
    int ParentProcessId,
    string Name,
    string ExecutablePath,
    string CommandLine,
    string Cwd,
    DateTimeOffset? CreationUtc,
    long WorkingSetBytes,
    double? CpuDeltaPercent);

/// <summary>Read-only AgentSessions projection used by the classifier.</summary>
public sealed record ZombieCensusSessionRow(
    Guid Id,
    SessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Cwd,
    AgentKind AgentKind,
    DateTimeOffset? ActivityUtc);

/// <summary>Read-only Agents projection used by the classifier.</summary>
public sealed record ZombieCensusAgentRow(
    Guid Id,
    string Name,
    string Slug,
    bool IsPoolDelegate,
    AgentStatus Status,
    Guid? PersistentSessionId,
    string WorkingDirectory);

/// <summary>Read-only AgentTasks projection used by the classifier.</summary>
public sealed record ZombieCensusTaskRow(
    Guid Id,
    Guid? AgentId,
    Guid? AgentSessionId,
    AgentTaskStatus Status,
    DateTimeOffset? CompletedAt,
    WorkspaceMode Workspace,
    string WorkingDirectory,
    string? WorktreePath);

public sealed record ZombieCensusDbSnapshot(
    IReadOnlyList<ZombieCensusSessionRow> Sessions,
    IReadOnlyList<ZombieCensusAgentRow> Agents,
    IReadOnlyList<ZombieCensusTaskRow> Tasks);

public sealed record ZombieCensusThresholds(
    int MinDoneMinutes,
    int QuietHours,
    int PidReuseToleranceSeconds);

/// <summary>OS-census labels from the PowerShell script. Not <c>AttentionKind.AgentOutlivedTask</c>.</summary>
public enum ZombieCensusClass
{
    None = 0,
    Ignored = 1,
    Unclaimed = 2,
    PoolExpired = 3,
    ReconcilerOwned = 4,
    EndedButAlive = 5
}

public enum ZombieIdentityMethod
{
    None = 0,
    I1 = 1,
    I2 = 2,
    I3 = 3,
    I4 = 4,
    I5 = 5
}

/// <summary>
/// How a later, separately approved execution slice would act. v1 never follows this path;
/// there is no configuration switch that can turn it on.
/// </summary>
public enum ZombieFutureAction
{
    None = 0,
    /// <summary>Owned runner-claimed PoolExpired: <c>AgentSessionService</c> / runner kill.</summary>
    ServerSessionKill = 1,
    /// <summary>Orphan tree: <c>RunnerProcessCleanup.KillTree</c> from the top Antiphon ancestor.</summary>
    ProcessTreeKill = 2
}

public sealed record ZombieCensusRow(
    int Pid,
    string Exe,
    DateTimeOffset? StartUtc,
    double WorkingSetGb,
    double? CpuDeltaPercent,
    ZombieIdentityMethod IdentityMethod,
    Guid? SessionId,
    string DbStatus,
    string AgentName,
    ZombieCensusClass Class,
    IReadOnlyList<string> FailedRules,
    ZombieFutureAction FutureAction,
    bool RunnerClaimed,
    int TreeKillPid,
    bool IsCandidate);

public sealed record ZombieCensusCounts(
    int PoolExpired,
    int ReconcilerOwned,
    int EndedButAlive,
    int Unclaimed,
    int Ignored,
    int Unidentified,
    int Candidates);

public sealed record ZombieCensusResult(
    DateTimeOffset GeneratedAtUtc,
    TimeSpan Duration,
    ZombieCensusCounts Counts,
    IReadOnlyList<ZombieCensusRow> Rows,
    IReadOnlyList<ZombieCensusRow> Candidates,
    IReadOnlyList<string> PrerequisiteFailures);
