namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-0147 S3: result of a detection-only worktree health sweep.</summary>
public sealed record WorktreeHealthReportDto(
    int FindingCount,
    IReadOnlyList<WorktreeHealthFindingDto> Findings);

/// <summary>One uncleared finding the orchestrator can act on (cancel, land, or inspect) — never auto-fixed.</summary>
public sealed record WorktreeHealthFindingDto(
    Guid Id,
    string RepoPath,
    string Branch,
    string Path,
    Guid? TaskId,
    string? ShortId,
    string Shape,
    string Detail,
    string Severity,
    DateTime FirstSeenAt,
    DateTime LastSeenAt);
