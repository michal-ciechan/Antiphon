using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

public sealed record WorktreeHandoffDispositionDto(
    PipelineHandoffKind? NextStage,
    WorktreeHandoffDispositionKind Kind,
    Guid? ConsumedByTaskId,
    Guid? EvidenceId,
    string Reason);

public sealed record ReleaseWorktreeRetirementRequest(
    Guid ExpectedTaskRevision,
    string SourceSha,
    string? ReportDigest,
    bool NoFurtherWorkspaceUse,
    string Reason,
    IReadOnlyList<WorktreeHandoffDispositionDto>? HandoffDispositions = null,
    bool MissingReportReviewed = false);

public sealed record WorktreeRetirementDto(
    Guid Id,
    Guid TaskId,
    int TaskAttempt,
    WorktreeRetirementState State,
    Guid ReleasedTaskRevision,
    string SourceSha,
    string SourceFullRef,
    string WorktreePath,
    DateTime ReleasedAt,
    DateTime? ClaimedAt,
    DateTime? CommandStartedAt,
    bool? DirectoryRemoved,
    bool? RegistrationRemoved,
    bool? BranchRemoved,
    string? LastReason);

public sealed record WorktreeResiduePreviewRequest(
    Guid? ProjectId = null,
    Guid? BoardId = null,
    string? Unscoped = null);

public sealed record WorktreeResidueRunDto(
    Guid Id,
    DateTime StartedAt,
    DateTime? FinishedAt,
    bool Execute,
    bool Preview,
    int ActionBudget,
    int ActionsAccepted,
    int Candidates,
    int Held,
    int Deferred,
    int Queued,
    int Refused,
    int Partial,
    int Removed,
    IReadOnlyList<WorktreeResidueCandidateDto> Rows,
    int Page,
    int PageSize,
    int TotalRows);

public sealed record WorktreeResidueCandidateDto(
    Guid Id,
    string Lane,
    string Outcome,
    string ReasonCode,
    Guid? TaskId,
    Guid? RetirementId,
    Guid? LandingOperationId,
    Guid? LandRequestId,
    string? Path,
    string? Branch,
    bool? DirectoryRemoved,
    bool? RegistrationRemoved,
    bool? BranchRemoved);

public sealed record WorkspaceReservationKey(
    string CanonicalPath,
    string SourceFullRef,
    string CommonDirectory)
{
    /// <summary>
    /// One coordinate system for every reservation producer: worktree path, refs/heads branch,
    /// and the repository working tree (never git-common-dir). Session sites that only have Cwd
    /// still match a retirement of that path because <see cref="Same"/> treats an empty ref and a
    /// CommonDirectory equal to CanonicalPath as the same workspace.
    /// </summary>
    public static WorkspaceReservationKey For(string? canonicalPath, string? sourceFullRef, string? commonDirectory)
    {
        var path = NormalizePath(canonicalPath);
        return new(path, NormalizeRef(sourceFullRef), NormalizeRepository(commonDirectory, path));
    }

    public static WorkspaceReservationKey ForTask(string? worktreePath, string? workingDirectory, string? branch, string? repoPath)
    {
        var path = string.IsNullOrWhiteSpace(worktreePath) ? workingDirectory : worktreePath;
        var repo = string.IsNullOrWhiteSpace(repoPath) ? path : repoPath;
        return For(path, branch, repo);
    }

    public WorkspaceReservationKey Normalized() => For(CanonicalPath, SourceFullRef, CommonDirectory);

    public static bool Same(string leftPath, string leftRef, string leftCommon, WorkspaceReservationKey key) =>
        Same(For(leftPath, leftRef, leftCommon), key.Normalized());

    public static bool Same(WorkspaceReservationKey left, WorkspaceReservationKey right)
    {
        if (!PathsEqual(left.CanonicalPath, right.CanonicalPath)) return false;
        if (!RefsCompatible(left.SourceFullRef, right.SourceFullRef)) return false;
        if (PathsEqual(left.CommonDirectory, right.CommonDirectory)) return true;
        return PathsEqual(left.CommonDirectory, left.CanonicalPath)
            || PathsEqual(right.CommonDirectory, right.CanonicalPath);
    }

    public static string NormalizeRef(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var trimmed = value.Trim();
        return trimmed.StartsWith("refs/", StringComparison.Ordinal) ? trimmed : "refs/heads/" + trimmed;
    }

    public static string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return value.Trim();
        }
    }

    public static string NormalizeRepository(string? commonDirectory, string canonicalPath)
    {
        var path = NormalizePath(string.IsNullOrWhiteSpace(commonDirectory) ? canonicalPath : commonDirectory);
        if (string.IsNullOrWhiteSpace(path)) return canonicalPath ?? "";
        if (Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent)) return NormalizePath(parent);
        }

        return path;
    }

    public static bool PathsEqual(string left, string right) => string.Equals(
        NormalizePath(left),
        NormalizePath(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool RefsCompatible(string left, string right) =>
        string.IsNullOrWhiteSpace(left)
        || string.IsNullOrWhiteSpace(right)
        || string.Equals(left, right, StringComparison.Ordinal);
}

public sealed record WorkspaceReservationSnapshot(
    Guid Id,
    int Generation,
    WorkspaceReservationKind Kind,
    Guid? TaskId,
    Guid? SessionId,
    Guid? RetirementId,
    bool Active);

public sealed record WorkspaceReservationCommand(
    WorkspaceReservationKey Key,
    WorkspaceReservationKind Kind,
    Guid? TaskId = null,
    Guid? SessionId = null,
    Guid? RetirementId = null,
    int? ExpectedGeneration = null);

public sealed record WorkspaceReservationCommitResult(bool Accepted, WorkspaceReservationSnapshot? Snapshot, string? Reason);
