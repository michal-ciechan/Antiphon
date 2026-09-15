using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public enum EffectiveCommitOnSettle { Off, Tier1, AgentOnly }

public static class CommitOnSettlePolicyResolver
{
    public static EffectiveCommitOnSettle Resolve(CommitOnSettlePolicy? task, bool? project, bool global) => task switch
    {
        CommitOnSettlePolicy.Never => EffectiveCommitOnSettle.Off,
        CommitOnSettlePolicy.Always => EffectiveCommitOnSettle.Tier1,
        CommitOnSettlePolicy.Agent => EffectiveCommitOnSettle.AgentOnly,
        _ => (project ?? global) ? EffectiveCommitOnSettle.Tier1 : EffectiveCommitOnSettle.Off,
    };

    public static CommitOnSettlePolicy? Parse(string? value) => value switch
    {
        null => null,
        "Never" => CommitOnSettlePolicy.Never,
        "Always" => CommitOnSettlePolicy.Always,
        "Agent" => CommitOnSettlePolicy.Agent,
        _ => throw new ValidationException("CommitOnSettle", "Expected Never, Always, or Agent."),
    };
}

public static class CommitOnSettleEligibility
{
    public static bool IsEligible(AgentTask task) => task.Status == AgentTaskStatus.Succeeded
        && task.Workspace == WorkspaceMode.Shared && task.SourceLandingOperationId is null
        && task.Role is not (AgentTaskRole.Commit or AgentTaskRole.Merge or AgentTaskRole.Mutation
            or AgentTaskRole.Check or AgentTaskRole.Distill or AgentTaskRole.Diagnose);
}
