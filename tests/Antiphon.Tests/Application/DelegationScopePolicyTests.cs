using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// The pair-weighted overlap policy (CARD-0063 §2.3). The severity of two tasks sharing an area is
/// mostly a property of the two WORKSPACES and only secondarily of the area: two worktrees collide
/// at merge and cost a rebase, two shared checkouts collide immediately and area-independently.
/// </summary>
[Category("Unit")]
public class ScopeOverlapPolicyTests
{
    [Test]
    public void shared_against_shared_serialises()
    {
        ScopeResolver.PolicyFor(WorkspaceMode.Shared, WorkspaceMode.Shared, allAllow: false)
            .ShouldBe(ScopeOverlapPolicy.Serialise);
    }

    [Test]
    public void a_worktree_on_either_side_only_warns()
    {
        // Blocking a worktree dispatch throws away the parallelism worktrees exist to give, and in
        // 623 live tasks the hold never once protected anything.
        ScopeResolver.PolicyFor(WorkspaceMode.Worktree, WorkspaceMode.Shared, allAllow: false)
            .ShouldBe(ScopeOverlapPolicy.Warn);
        ScopeResolver.PolicyFor(WorkspaceMode.Shared, WorkspaceMode.Worktree, allAllow: false)
            .ShouldBe(ScopeOverlapPolicy.Warn);
        ScopeResolver.PolicyFor(WorkspaceMode.Worktree, WorkspaceMode.Worktree, allAllow: false)
            .ShouldBe(ScopeOverlapPolicy.Warn);
    }

    [Test]
    public void read_only_on_either_side_allows()
    {
        ScopeResolver.PolicyFor(WorkspaceMode.ReadOnly, WorkspaceMode.Shared, allAllow: false)
            .ShouldBe(ScopeOverlapPolicy.Allow);
        ScopeResolver.PolicyFor(WorkspaceMode.Shared, WorkspaceMode.ReadOnly, allAllow: false)
            .ShouldBe(ScopeOverlapPolicy.Allow);
    }

    [Test]
    public void an_all_allow_intersection_allows_even_the_shared_pair()
    {
        ScopeResolver.PolicyFor(WorkspaceMode.Shared, WorkspaceMode.Shared, allAllow: true)
            .ShouldBe(ScopeOverlapPolicy.Allow, "two docs tasks cost a bullet-order rebase");
    }

    [Test]
    public void the_weight_is_a_downgrade_and_never_an_upgrade()
    {
        // There is no allAllow value that can turn a Warn pair into a Serialise one.
        foreach (var allAllow in new[] { true, false })
        {
            ((int)ScopeResolver.PolicyFor(WorkspaceMode.Worktree, WorkspaceMode.Worktree, allAllow))
                .ShouldBeLessThanOrEqualTo((int)ScopeOverlapPolicy.Warn);
        }
    }

    [Test]
    public void serialise_defaults_on()
    {
        new DelegationSettings().SerialiseSharedWriters.ShouldBeTrue();
    }

    [Test]
    public void a_pair_reads_as_a_sentence()
    {
        ScopeResolver.DescribePair(WorkspaceMode.Shared, WorkspaceMode.Worktree)
            .ShouldBe("Shared ↔ Worktree");
    }
}

/// <summary>
/// The completion note's <c>overlapping-running=</c> line — the whole of CARD-0063's merge-ordering
/// deliverable. The operator merges by hand (216 of 246 merge-backs are LeftForHuman), so naming
/// the live task that shares this one's ground is what lets them pick an order.
/// </summary>
[Category("Unit")]
public class CompletionHeaderOverlapTests
{
    [Test]
    public void the_header_names_the_overlapping_running_tasks()
    {
        var note = DelegationReportFormatter.BuildCompletionNote(
            Task(), new DelegationSettings(), "done", overlappingRunning: "3f2a1c9d,7b0e4411");

        note.Header.ShouldContain("overlapping-running=3f2a1c9d,7b0e4411");
    }

    [Test]
    public void nothing_overlapping_adds_nothing_to_the_header()
    {
        var note = DelegationReportFormatter.BuildCompletionNote(
            Task(), new DelegationSettings(), "done");

        note.Header.ShouldNotContain("overlapping-running");
    }

    [Test]
    public void the_brief_header_names_areas_rather_than_a_glob()
    {
        // The field holds `delivery,schema` far more often than it holds a glob; calling it
        // `scope=` in every brief was a standing lie.
        var task = Task();
        task.Scope = "delivery,schema";

        DelegationReportFormatter.BuildBrief(task, new DelegationSettings())
            .ShouldContain("areas=delivery,schema");
    }

    private static AgentTask Task() => new()
    {
        Id = Guid.NewGuid(),
        Title = "the work",
        Goal = "do the work",
        Role = AgentTaskRole.Code,
        ModelLevel = AgentModelLevel.Medium,
        Workspace = WorkspaceMode.Shared,
        WorkingDirectory = Path.Combine("C:", "src", "antiphon"),
        Status = AgentTaskStatus.Succeeded,
    };
}
