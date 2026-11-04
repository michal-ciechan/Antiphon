using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1637 D-1: the conflicted-file list and the Merge-role refusal text are bounded, so the
/// LandRefused, Warning and Conflicted events fit AgentTaskEvents.Detail (varchar 4000).
/// </summary>
public class LandConflictFileSummaryTests
{
    private static IReadOnlyList<string> Names(int count, int length) => Enumerable.Range(0, count)
        .Select(i => $"long/c1637-{i:D3}-".PadRight(length, 'x')).ToList();

    [Test]
    public void Short_list_is_the_plain_join()
    {
        AgentTaskLandingProtocol.ConflictFileSummary(["a.cs", "b/c.cs", "shared.md"]).ShouldBe("a.cs, b/c.cs, shared.md");
        AgentTaskLandingProtocol.ConflictFileSummary([]).ShouldBe("");
    }

    [Test]
    public void Twenty_names_are_kept_and_the_rest_are_counted()
    {
        var names = Names(80, 72);

        var summary = AgentTaskLandingProtocol.ConflictFileSummary(names);

        summary.ShouldBe(string.Join(", ", names.Take(20)) + ", ... and 60 more");
        summary.Length.ShouldBeLessThanOrEqualTo(AgentTaskLandingProtocol.ConflictListMaxLength);
        AgentTaskLandingProtocol.ConflictFileSummary(names.Take(20).ToList()).ShouldBe(string.Join(", ", names.Take(20)));
    }

    [Test]
    public void Very_long_names_are_clipped_and_the_length_bound_holds()
    {
        var names = Names(25, 4096);

        var summary = AgentTaskLandingProtocol.ConflictFileSummary(names);

        summary.Length.ShouldBeLessThanOrEqualTo(AgentTaskLandingProtocol.ConflictListMaxLength);
        summary.ShouldStartWith(names[0][..(AgentTaskLandingProtocol.ConflictNameMaxLength - 3)] + "...");
        summary.ShouldNotContain(names[0]);
        summary.ShouldMatch(@"\.\.\. and \d+ more$");
        var bounded = AgentTaskLandingProtocol.ConflictFileSummary(names, 40);
        bounded.ShouldBe("... and 25 more");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void Merge_role_detail_stays_under_its_bound_with_long_refs_shas_and_names(bool containsTarget)
    {
        var source = new string('a', 64);
        var target = new string('b', 64);
        var op = new AgentTaskLanding
        {
            SourceFullRef = "refs/heads/" + "feat/".PadRight(240, 's'),
            TargetFullRef = "refs/heads/" + "rc/".PadRight(240, 't'),
            OriginalSourceSha = source,
            TargetBeforeSha = target,
        };

        var detail = AgentTaskLandingProtocol.MergeRoleDetail(op, containsTarget, Names(80, 4096));

        detail.Length.ShouldBeLessThanOrEqualTo(AgentTaskLandingProtocol.MergeRoleDetailMaxLength);
        detail.ShouldContain(" more. ");
        detail.ShouldContain(target);
        detail.ShouldContain(containsTarget ? $"git push origin {source}:refs/heads/" : $"runs git merge {source}");
        detail.ShouldEndWith("No rebase resolver was spawned.");
        // The land prefixes the detail with "land refused: <reason>; " or the warning sentence.
        ("Landing not confirmed; no cleanup authorized by this result. merge_branch_needs_merge_role; " + detail)
            .Length.ShouldBeLessThanOrEqualTo(3500);
    }

    [Test]
    public void Merge_role_detail_is_hard_bounded_even_with_a_pathological_ref()
    {
        var op = new AgentTaskLanding
        {
            SourceFullRef = "refs/heads/" + new string('s', 5000),
            TargetFullRef = "refs/heads/master",
            OriginalSourceSha = new string('a', 64),
            TargetBeforeSha = new string('b', 64),
        };

        AgentTaskLandingProtocol.MergeRoleDetail(op, true, Names(80, 72)).Length
            .ShouldBeLessThanOrEqualTo(AgentTaskLandingProtocol.MergeRoleDetailMaxLength);
    }
}
