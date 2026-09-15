using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0527 D-2. Structural eligibility before policy.</summary>
public static class CommitOnSettleEligibility
{
    public static bool IsEligible(AgentTask task) =>
        task.Status == AgentTaskStatus.Succeeded
        && task.Workspace == WorkspaceMode.Shared
        && task.SourceLandingOperationId is null
        && task.Role is not (AgentTaskRole.Commit or AgentTaskRole.Merge or AgentTaskRole.Mutation
            or AgentTaskRole.Check or AgentTaskRole.Distill or AgentTaskRole.Diagnose)
        && !string.IsNullOrWhiteSpace(task.RepoPath);
}
