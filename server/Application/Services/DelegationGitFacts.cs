using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Shared git-facts helpers for settlement (CARD-0159 S3) and the check digest, so both agree
/// on the range base: <see cref="AgentTask.MergeTargetRef"/> when set, otherwise the SHA
/// captured at worktree creation.
/// </summary>
internal static class DelegationGitFacts
{
    public static readonly AgentTaskRole[] CodeProducingRoles =
    [
        AgentTaskRole.Code,
        AgentTaskRole.Test,
        AgentTaskRole.Commit,
        AgentTaskRole.Coverage,
        AgentTaskRole.Debug,
        AgentTaskRole.Docs,
    ];

    public static string? ResolveBase(AgentTask task) =>
        !string.IsNullOrWhiteSpace(task.MergeTargetRef) ? task.MergeTargetRef
        : task.WorktreeBaseSha;

    public static string FormatHeader(int commits, int files) =>
        commits == 0 && files == 0
            ? "no changes"
            : $"{commits} commits, {files} files";

    public static bool MentionsNoChanges(string report) =>
        report.Contains("no changes", StringComparison.OrdinalIgnoreCase)
        || report.Contains("nothing to change", StringComparison.OrdinalIgnoreCase);

    public static bool IsCodeProducing(AgentTaskRole role) =>
        Array.IndexOf(CodeProducingRoles, role) >= 0;
}
