using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class AgentTaskDecisionQuestionTests
{
    [Test]
    public void Exact_grant_continues_a_bounded_line_ending_repair()
    {
        using var workspace = new DecisionTempWorkspace();
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(),
            AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        var question = Sample();

        var answer = InternalDecisionQuestionPolicy.Evaluate(policy, question, workspace.Path);

        answer.Disposition.ShouldBe(InternalDecisionDisposition.Continue);
        answer.Reason.ShouldBe("dispatch_grant");
    }

    [Test]
    [Arguments(InternalDecisionImpact.ProductBehavior)]
    [Arguments(InternalDecisionImpact.Data)]
    [Arguments(InternalDecisionImpact.Ux)]
    [Arguments(InternalDecisionImpact.PublicContract)]
    [Arguments(InternalDecisionImpact.Security)]
    [Arguments(InternalDecisionImpact.OperationalPolicy)]
    [Arguments(InternalDecisionImpact.ExternalActionOrSpend)]
    [Arguments(InternalDecisionImpact.Mixed)]
    [Arguments(InternalDecisionImpact.Unknown)]
    public void Every_non_none_impact_requires_a_human(InternalDecisionImpact impact)
    {
        using var workspace = new DecisionTempWorkspace();
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(),
            AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;

        var answer = InternalDecisionQuestionPolicy.Evaluate(policy, Sample() with { Impact = impact }, workspace.Path);

        answer.Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman);
        answer.Reason.ShouldBe("human_impact");
    }

    [Test]
    public void A_sibling_path_and_a_second_grant_cannot_expand_the_named_grant()
    {
        using var workspace = new DecisionTempWorkspace();
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(),
            AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;

        InternalDecisionQuestionPolicy.Evaluate(policy,
            Sample() with { Paths = ["scripts/deploy-gym-stat.ps1.bak"] }, workspace.Path)
            .Reason.ShouldBe("path_not_granted");
    }

    [Test]
    public void Attribute_requests_are_exact()
    {
        using var workspace = new DecisionTempWorkspace();
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(),
            AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;

        InternalDecisionQuestionPolicy.Evaluate(policy,
            Sample() with { AttributeTargets = ["scripts/other.ps1"] }, workspace.Path)
            .Reason.ShouldBe("attribute_not_granted");
        InternalDecisionQuestionPolicy.Evaluate(policy,
            Sample() with { Attributes = ["filter"] }, workspace.Path)
            .Reason.ShouldBe("attribute_not_granted");
    }

    private static InternalDecisionQuestionRequest Sample() => new(
        Guid.NewGuid(), 1, "backup-transport", InternalDecisionCategory.LineEndings,
        ["scripts/deploy-gym-stat.ps1", ".gitattributes"],
        ["scripts/deploy-gym-stat.ps1"], ["text", "eol"],
        InternalDecisionImpact.None,
        "May I normalize this script to LF?",
        "Normalize only the script and pin its exact eol attribute.",
        "The intended command arguments remain the same; verify with an isolated argument capture.");
}
