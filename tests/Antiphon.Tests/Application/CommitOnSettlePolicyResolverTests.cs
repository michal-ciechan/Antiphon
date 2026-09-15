using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class CommitOnSettlePolicyResolverTests
{
    [Test]
    [Arguments(null, null, true, EffectiveCommitOnSettle.Tier1)]
    [Arguments(null, null, false, EffectiveCommitOnSettle.Off)]
    [Arguments(null, true, true, EffectiveCommitOnSettle.Tier1)]
    [Arguments(null, true, false, EffectiveCommitOnSettle.Tier1)]
    [Arguments(null, false, true, EffectiveCommitOnSettle.Off)]
    [Arguments(null, false, false, EffectiveCommitOnSettle.Off)]
    [Arguments(CommitOnSettlePolicy.Never, null, true, EffectiveCommitOnSettle.Off)]
    [Arguments(CommitOnSettlePolicy.Never, null, false, EffectiveCommitOnSettle.Off)]
    [Arguments(CommitOnSettlePolicy.Never, true, true, EffectiveCommitOnSettle.Off)]
    [Arguments(CommitOnSettlePolicy.Never, true, false, EffectiveCommitOnSettle.Off)]
    [Arguments(CommitOnSettlePolicy.Never, false, true, EffectiveCommitOnSettle.Off)]
    [Arguments(CommitOnSettlePolicy.Never, false, false, EffectiveCommitOnSettle.Off)]
    [Arguments(CommitOnSettlePolicy.Always, null, true, EffectiveCommitOnSettle.Tier1)]
    [Arguments(CommitOnSettlePolicy.Always, null, false, EffectiveCommitOnSettle.Tier1)]
    [Arguments(CommitOnSettlePolicy.Always, true, true, EffectiveCommitOnSettle.Tier1)]
    [Arguments(CommitOnSettlePolicy.Always, true, false, EffectiveCommitOnSettle.Tier1)]
    [Arguments(CommitOnSettlePolicy.Always, false, true, EffectiveCommitOnSettle.Tier1)]
    [Arguments(CommitOnSettlePolicy.Always, false, false, EffectiveCommitOnSettle.Tier1)]
    [Arguments(CommitOnSettlePolicy.Agent, null, true, EffectiveCommitOnSettle.AgentOnly)]
    [Arguments(CommitOnSettlePolicy.Agent, null, false, EffectiveCommitOnSettle.AgentOnly)]
    [Arguments(CommitOnSettlePolicy.Agent, true, true, EffectiveCommitOnSettle.AgentOnly)]
    [Arguments(CommitOnSettlePolicy.Agent, true, false, EffectiveCommitOnSettle.AgentOnly)]
    [Arguments(CommitOnSettlePolicy.Agent, false, true, EffectiveCommitOnSettle.AgentOnly)]
    [Arguments(CommitOnSettlePolicy.Agent, false, false, EffectiveCommitOnSettle.AgentOnly)]
    public void Resolve_task_then_project_then_global(CommitOnSettlePolicy? task, bool? project, bool global, EffectiveCommitOnSettle expected) =>
        CommitOnSettlePolicyResolver.Resolve(task, project, global).ShouldBe(expected);
}
