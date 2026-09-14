using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The configured default-branch candidate and whether it currently names a commit in the repo.
/// Callers choose exactly one candidate (project, then Git settings, then <c>master</c>) and
/// probe it; a failed probe still carries the original name.
/// </summary>
public sealed record DefaultBranchProbe(string Ref, bool ResolvesToCommit);

/// <summary>The D-2 precedence answer. <see cref="UnresolvedDefault"/> is set only on the RepoHead arm.</summary>
public sealed record ResolvedBase(string Ref, WorktreeBaseSource Source, string? UnresolvedDefault);

/// <summary>
/// CARD-0508 S1. Pure precedence for a Worktree creation base. No config, DB, or Git I/O.
/// <see cref="WorktreeBaseSource.CardCurrent"/> is not produced here (S3 deferred).
/// </summary>
public static class WorktreeBaseResolver
{
    public const string HardDefaultBranch = "master";

    public static string ChooseConfiguredDefault(string? projectBaseBranch, string? gitDefaultBranch)
    {
        if (!string.IsNullOrWhiteSpace(projectBaseBranch))
            return projectBaseBranch;
        if (!string.IsNullOrWhiteSpace(gitDefaultBranch))
            return gitDefaultBranch;
        return HardDefaultBranch;
    }

    public static ResolvedBase Resolve(
        string? repairStartSha,
        string? requestedRef,
        string? mergeTargetRef,
        DefaultBranchProbe defaultBranch)
    {
        ArgumentNullException.ThrowIfNull(defaultBranch);
        if (!string.IsNullOrWhiteSpace(repairStartSha))
            return new ResolvedBase(repairStartSha, WorktreeBaseSource.Repair, null);
        if (!string.IsNullOrWhiteSpace(requestedRef))
            return new ResolvedBase(requestedRef, WorktreeBaseSource.Explicit, null);
        if (!string.IsNullOrWhiteSpace(mergeTargetRef))
            return new ResolvedBase(mergeTargetRef, WorktreeBaseSource.MergeTarget, null);
        if (defaultBranch.ResolvesToCommit)
            return new ResolvedBase(defaultBranch.Ref, WorktreeBaseSource.DefaultBranch, null);
        return new ResolvedBase("HEAD", WorktreeBaseSource.RepoHead, defaultBranch.Ref);
    }
}

/// <summary>Provisioning's recorded-or-fresh decision. Unresolved-default diagnostics fire only when newly recorded.</summary>
public sealed record WorktreeBaseDecision(ResolvedBase Resolved, bool NewlyRecorded);
