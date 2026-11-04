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

    // CARD-1637 D-1 r2: Npgsql writes Detail with strict UTF-8, so half a surrogate pair left by a
    // cut throws EncoderFallbackException at save and the land request stays Running.
    private const string Emoji = "\U0001F600";

    private static void ShouldBeWholeUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
            char.IsSurrogate(text[i]).ShouldBeFalse($"lone surrogate 0x{(int)text[i]:X4} at index {i} of {text.Length}");
        }
        Should.NotThrow(() => new System.Text.UTF8Encoding(false, true).GetBytes(text));
    }

    [Test]
    public void A_clipped_name_never_splits_a_surrogate_pair()
    {
        // The high surrogate sits at index 196, the last character the 197-character cut keeps.
        var name = "long/".PadRight(AgentTaskLandingProtocol.ConflictNameMaxLength - 4, 'x') + Emoji;
        name = name.PadRight(300, 'y');
        char.IsHighSurrogate(name[AgentTaskLandingProtocol.ConflictNameMaxLength - 4]).ShouldBeTrue();

        var summary = AgentTaskLandingProtocol.ConflictFileSummary([name]);

        ShouldBeWholeUtf16(summary);
        summary.ShouldEndWith("...");
        summary.Length.ShouldBeLessThanOrEqualTo(AgentTaskLandingProtocol.ConflictNameMaxLength);
        summary.ShouldStartWith(name[..(AgentTaskLandingProtocol.ConflictNameMaxLength - 4)]);
    }

    [Test]
    public void The_list_cap_never_splits_a_surrogate_pair()
    {
        // Emoji-bearing names packed against the 2000-character list budget at every offset.
        for (var pad = 0; pad < 8; pad++)
        {
            var names = Enumerable.Range(0, 20)
                .Select(i => ($"e/{i:D2}-" + new string('x', pad + i % 3)).PadRight(95, 'z') + Emoji).ToList();

            var summary = AgentTaskLandingProtocol.ConflictFileSummary(names);

            ShouldBeWholeUtf16(summary);
            summary.Length.ShouldBeLessThanOrEqualTo(AgentTaskLandingProtocol.ConflictListMaxLength);
            ShouldBeWholeUtf16(AgentTaskLandingProtocol.ConflictFileSummary(names, 1000 + pad));
        }
    }

    [Test]
    public void The_merge_role_hard_cut_never_splits_a_surrogate_pair()
    {
        // "refs/heads/" + 3388 characters puts the high surrogate at index 3399, the last kept one.
        var op = new AgentTaskLanding
        {
            SourceFullRef = "refs/heads/" + new string('s', AgentTaskLandingProtocol.MergeRoleDetailMaxLength - 12) + Emoji
                + new string('s', 2000),
            TargetFullRef = "refs/heads/master",
            OriginalSourceSha = new string('a', 64),
            TargetBeforeSha = new string('b', 64),
        };
        char.IsHighSurrogate(op.SourceFullRef[AgentTaskLandingProtocol.MergeRoleDetailMaxLength - 1]).ShouldBeTrue();

        var detail = AgentTaskLandingProtocol.MergeRoleDetail(op, true, [Emoji + "/a.cs"]);

        ShouldBeWholeUtf16(detail);
        detail.Length.ShouldBeLessThanOrEqualTo(AgentTaskLandingProtocol.MergeRoleDetailMaxLength);
        detail.Length.ShouldBe(AgentTaskLandingProtocol.MergeRoleDetailMaxLength - 1, "one char is given up rather than a lone surrogate");
    }
}
