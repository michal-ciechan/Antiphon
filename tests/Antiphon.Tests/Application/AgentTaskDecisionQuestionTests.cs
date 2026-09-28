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

    [Test]
    [Arguments(InternalDecisionCategory.LineEndings)]
    [Arguments(InternalDecisionCategory.ShellTransport)]
    [Arguments(InternalDecisionCategory.BuildTestHarness)]
    public void Granted_category_returns_continue(InternalDecisionCategory category)
    {
        using var workspace = new DecisionTempWorkspace();
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(
                categories: [category], paths: ["scripts/deploy.ps1"]),
            AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        var answer = InternalDecisionQuestionPolicy.Evaluate(policy,
            Sample() with { Category = category, Paths = ["scripts/deploy.ps1"],
                AttributeTargets = null, Attributes = null }, workspace.Path);
        answer.Disposition.ShouldBe(InternalDecisionDisposition.Continue);
    }

    [Test]
    public void One_named_grant_must_cover_all_effects()
    {
        using var workspace = new DecisionTempWorkspace();
        var policy = InternalDecisionPolicy.Normalize(new InternalDecisionPolicyRequest(1,
            [
                new InternalDecisionGrantRequest("first", [InternalDecisionCategory.ShellTransport],
                    ["scripts/first.ps1"], null, "Preserve arguments."),
                new InternalDecisionGrantRequest("second", [InternalDecisionCategory.ShellTransport],
                    ["scripts/second.ps1"], null, "Preserve arguments."),
            ]), AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        var request = Sample() with { GrantId = "first", Category = InternalDecisionCategory.ShellTransport,
            Paths = ["scripts/first.ps1", "scripts/second.ps1"], AttributeTargets = null, Attributes = null };
        InternalDecisionQuestionPolicy.Evaluate(null, request, workspace.Path).Reason.ShouldBe("no_grant");
        InternalDecisionQuestionPolicy.Evaluate(policy, request with { GrantId = "unknown" }, workspace.Path)
            .Reason.ShouldBe("no_grant");
        InternalDecisionQuestionPolicy.Evaluate(policy, request with { Category = InternalDecisionCategory.LineEndings }, workspace.Path)
            .Reason.ShouldBe("category_not_granted");
        InternalDecisionQuestionPolicy.Evaluate(policy, request, workspace.Path).Reason.ShouldBe("path_not_granted");
        InternalDecisionQuestionPolicy.Evaluate(policy, request with { RequestId = Guid.NewGuid() }, workspace.Path)
            .Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman);
    }

    [Test]
    public void Repository_path_boundary_rechecks_links_and_rejects_directories()
    {
        using var repository = new DecisionTempWorkspace();
        using var outside = new DecisionTempWorkspace();
        Directory.CreateDirectory(Path.Combine(repository.Path, "scripts"));
        File.WriteAllText(Path.Combine(outside.Path, "outside.ps1"), "outside");
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(
                categories: [InternalDecisionCategory.ShellTransport],
                paths: ["scripts/target.ps1", "scripts/linked/outside.ps1", "scripts/linked/new.ps1", "scripts"]),
            AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        var request = Sample() with { Category = InternalDecisionCategory.ShellTransport,
            Paths = ["scripts/target.ps1"], AttributeTargets = null, Attributes = null };
        InternalDecisionQuestionPolicy.Evaluate(policy, request, repository.Path)
            .Disposition.ShouldBe(InternalDecisionDisposition.Continue);
        InternalDecisionQuestionPolicy.Evaluate(policy, request with { Paths = ["scripts"] }, repository.Path)
            .Reason.ShouldBe("path_not_granted");
        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(Path.Combine(repository.Path, "scripts", "target.ps1"),
                Path.Combine(outside.Path, "outside.ps1"));
            InternalDecisionQuestionPolicy.Evaluate(policy, request, repository.Path)
                .Reason.ShouldBe("path_not_granted");
            File.Delete(Path.Combine(outside.Path, "outside.ps1"));
            InternalDecisionQuestionPolicy.Evaluate(policy, request, repository.Path)
                .Reason.ShouldBe("path_not_granted");
        }
        using var link = DirectoryLink.TryCreate(Path.Combine(repository.Path, "scripts", "linked"), outside.Path);
        link.ShouldNotBeNull("the native directory link or junction is required for this boundary test");
        foreach (var path in new[] { "scripts/linked/outside.ps1", "scripts/linked/new.ps1" })
            InternalDecisionQuestionPolicy.Evaluate(policy, request with { Paths = [path] }, repository.Path)
                .Reason.ShouldBe("path_not_granted");
    }

    [Test]
    public void Two_attribute_targets_must_both_be_exact_and_line_endings_only()
    {
        using var workspace = new DecisionTempWorkspace();
        var policy = InternalDecisionPolicy.Normalize(InternalDecisionFixtures.Sample(
                paths: ["scripts/one.ps1", "scripts/two.ps1", ".gitattributes"],
                attributeTargets: ["scripts/one.ps1", "scripts/two.ps1"]),
            AgentTaskRole.Code, WorkspaceMode.Worktree,
            InternalDecisionFixtures.ManualGrantor(), InternalDecisionFixtures.GrantedAt)!;
        var request = Sample() with
        {
            Paths = ["scripts/one.ps1", "scripts/two.ps1", ".gitattributes"],
            AttributeTargets = ["scripts/one.ps1", "scripts/two.ps1"],
        };
        InternalDecisionQuestionPolicy.Evaluate(policy, request, workspace.Path)
            .Disposition.ShouldBe(InternalDecisionDisposition.Continue);
        foreach (var target in new[] { "scripts/third.ps1", "scripts/*.ps1" })
        {
            var variant = request with { AttributeTargets = ["scripts/one.ps1", target] };
            if (target.Contains('*'))
                Should.Throw<Antiphon.Server.Application.Exceptions.ValidationException>(() =>
                    InternalDecisionQuestionPolicy.Evaluate(policy, variant, workspace.Path));
            else
                InternalDecisionQuestionPolicy.Evaluate(policy, variant, workspace.Path)
                    .Reason.ShouldBe("attribute_not_granted");
        }
        foreach (var attribute in new[] { "filter", "diff", "merge", "working-tree-encoding" })
            InternalDecisionQuestionPolicy.Evaluate(policy, request with { Attributes = [attribute] }, workspace.Path)
                .Reason.ShouldBe("attribute_not_granted");
        foreach (var category in new[] { InternalDecisionCategory.ShellTransport,
                     InternalDecisionCategory.BuildTestHarness })
            InternalDecisionQuestionPolicy.Evaluate(policy, request with { Category = category }, workspace.Path)
                .Disposition.ShouldBe(InternalDecisionDisposition.NeedsHuman);
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
