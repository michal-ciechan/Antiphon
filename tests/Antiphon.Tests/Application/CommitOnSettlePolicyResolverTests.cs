using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class CommitOnSettlePolicyResolverTests
{
    [Test]
    [Arguments(null, null, true, CommitOnSettleEffective.Tier1)]
    [Arguments(null, null, false, CommitOnSettleEffective.Off)]
    [Arguments(null, true, true, CommitOnSettleEffective.Tier1)]
    [Arguments(null, true, false, CommitOnSettleEffective.Tier1)]
    [Arguments(null, false, true, CommitOnSettleEffective.Off)]
    [Arguments(null, false, false, CommitOnSettleEffective.Off)]
    [Arguments(CommitOnSettlePolicy.Never, null, true, CommitOnSettleEffective.Off)]
    [Arguments(CommitOnSettlePolicy.Never, true, true, CommitOnSettleEffective.Off)]
    [Arguments(CommitOnSettlePolicy.Never, false, false, CommitOnSettleEffective.Off)]
    [Arguments(CommitOnSettlePolicy.Always, null, false, CommitOnSettleEffective.Tier1)]
    [Arguments(CommitOnSettlePolicy.Always, false, false, CommitOnSettleEffective.Tier1)]
    [Arguments(CommitOnSettlePolicy.Always, true, true, CommitOnSettleEffective.Tier1)]
    [Arguments(CommitOnSettlePolicy.Agent, null, true, CommitOnSettleEffective.AgentOnly)]
    [Arguments(CommitOnSettlePolicy.Agent, false, false, CommitOnSettleEffective.AgentOnly)]
    [Arguments(CommitOnSettlePolicy.Agent, true, true, CommitOnSettleEffective.AgentOnly)]
    public void Resolve_task_then_project_then_global(
        CommitOnSettlePolicy? task,
        bool? project,
        bool global,
        CommitOnSettleEffective expected)
    {
        CommitOnSettlePolicyResolver.Resolve(task, project, global).ShouldBe(expected);
    }
}
