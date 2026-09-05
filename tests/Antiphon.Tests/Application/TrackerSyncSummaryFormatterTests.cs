using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0171 §5: the change summary a channel receives. Pure — no DB, no channel catalog.
/// </summary>
[Category("Unit")]
public class TrackerSyncSummaryFormatterTests
{
    [Test]
    public void No_changes_formats_to_null()
    {
        var result = TrackerSyncSummaryFormatter.Format(
        [
            (Board("Antiphon board"), GitHubConfig())
        ]);

        result.ShouldBeNull();
    }

    [Test]
    public void The_full_template_renders_exactly_as_specified()
    {
        var board = Board("Antiphon board",
            Change(TrackerSyncChangeKind.CommentIn, "CARD-0170", "#10"),
            Change(TrackerSyncChangeKind.CommentIn, "CARD-0171", "#15"),
            Change(TrackerSyncChangeKind.CommentOut, "CARD-0166", "#14"),
            Change(TrackerSyncChangeKind.ClosedOnGitHub, "CARD-0166", "#14"),
            Change(TrackerSyncChangeKind.ReopenedFromGitHub, "CARD-0150", "#9"),
            Change(TrackerSyncChangeKind.Created, "CARD-0172", "#16"),
            Change(TrackerSyncChangeKind.LabelsChanged, "CARD-0170", "#10",
                added: ["status:done"], removed: ["status:active"]),
            Change(TrackerSyncChangeKind.LabelsChanged, "CARD-0171", "#15", added: ["bug"]),
            Change(TrackerSyncChangeKind.LabelsChanged, "CARD-0172", "#16", removed: ["wontfix"]));

        var result = TrackerSyncSummaryFormatter.Format([(board, GitHubConfig())]);

        result.ShouldBe(string.Join('\n',
            "Antiphon <-> GitHub sync: Antiphon board",
            "- 2 comments in from GitHub: CARD-0170, CARD-0171",
            "- 1 comment posted to GitHub: CARD-0166",
            "- 1 issue closed on GitHub: CARD-0166 (#14)",
            "- 1 issue reopened from GitHub: CARD-0150 (#9)",
            "- 1 issue created on GitHub: CARD-0172 (#16)",
            "- labels updated on 3 issues:",
            "  CARD-0170 (#10): +status:done, -status:active",
            "  CARD-0171 (#15): +bug",
            "  CARD-0172 (#16): -wontfix",
            "https://github.com/michal-ciechan/Antiphon/issues"));
    }

    [Test]
    public void Labels_line_comes_last_with_nested_add_remove()
    {
        var board = Board("B",
            Change(TrackerSyncChangeKind.LabelsChanged, "CARD-0001", "#1",
                added: ["status:done"], removed: ["status:active"]),
            Change(TrackerSyncChangeKind.CommentIn, "CARD-0002", "#2"));

        var lines = TrackerSyncSummaryFormatter.Format([(board, GitHubConfig())])!.Split('\n');

        lines[1].ShouldBe("- 1 comment in from GitHub: CARD-0002");
        lines[2].ShouldBe("- labels updated on 1 issue:");
        lines[3].ShouldBe("  CARD-0001 (#1): +status:done, -status:active");
    }

    [Test]
    public void Reopened_on_GitHub_and_content_pushed_render_their_own_lines()
    {
        var board = Board("B",
            Change(TrackerSyncChangeKind.ReopenedOnGitHub, "CARD-0003", "#3"),
            Change(TrackerSyncChangeKind.ContentPushed, "CARD-0004", "#4"));

        var text = TrackerSyncSummaryFormatter.Format([(board, GitHubConfig())])!;

        text.ShouldContain("- 1 issue reopened on GitHub: CARD-0003 (#3)");
        text.ShouldContain("- content updated on GitHub: CARD-0004 (#4)");
    }

    [Test]
    public void Identifiers_dedupe_and_list_all_twenty_distinct_cards()
    {
        var changes = Enumerable.Range(1, 7)
            .Select(i => Change(TrackerSyncChangeKind.CommentIn, $"CARD-{i:0000}", $"#{i}"))
            // A second comment on the first card must not list it twice.
            .Append(Change(TrackerSyncChangeKind.CommentIn, "CARD-0001", "#1"))
            .ToArray();

        var text = TrackerSyncSummaryFormatter.Format([(Board("B", changes), GitHubConfig())])!;

        text.ShouldContain(
            "- 8 comments in from GitHub: CARD-0001, CARD-0002, CARD-0003, CARD-0004, CARD-0005, CARD-0006, CARD-0007");
        text.ShouldNotContain("more");
    }

    [Test]
    public void Twenty_distinct_cards_are_named_twenty_one_is_count_only()
    {
        var twenty = Enumerable.Range(1, 20)
            .Select(i => Change(TrackerSyncChangeKind.ClosedOnGitHub, $"CARD-{i:0000}", $"#{i}"))
            .ToArray();
        var named = TrackerSyncSummaryFormatter.Format([(Board("B", twenty), GitHubConfig())])!;
        named.ShouldContain("CARD-0001 (#1)");
        named.ShouldContain("CARD-0020 (#20)");
        named.ShouldContain("- 20 issues closed on GitHub: ");
        named.ShouldNotContain("more");

        var twentyOne = twenty
            .Append(Change(TrackerSyncChangeKind.ClosedOnGitHub, "CARD-0021", "#21"))
            .ToArray();
        var countOnly = TrackerSyncSummaryFormatter.Format([(Board("B", twentyOne), GitHubConfig())])!;
        countOnly.ShouldContain("- 21 issues closed on GitHub");
        countOnly.ShouldNotContain("CARD-");
        countOnly.ShouldNotContain("#21");
    }

    [Test]
    public void Comment_cap_counts_distinct_cards_not_raw_events()
    {
        var changes = Enumerable.Range(1, 20)
            .Select(i => Change(TrackerSyncChangeKind.CommentIn, $"CARD-{i:0000}", $"#{i}"))
            .Append(Change(TrackerSyncChangeKind.CommentIn, "CARD-0001", "#1"))
            .ToArray();

        var text = TrackerSyncSummaryFormatter.Format([(Board("B", changes), GitHubConfig())])!;

        text.ShouldContain("- 21 comments in from GitHub: CARD-0001");
        text.ShouldContain("CARD-0020");
        text.ShouldNotContain("more");
    }

    [Test]
    public void Twenty_one_label_changes_fall_back_to_count_only()
    {
        var changes = Enumerable.Range(1, 21)
            .Select(i => Change(TrackerSyncChangeKind.LabelsChanged, $"CARD-{i:0000}", $"#{i}",
                added: ["status:done"], removed: ["status:active"]))
            .ToArray();

        var text = TrackerSyncSummaryFormatter.Format([(Board("B", changes), GitHubConfig())])!;

        text.ShouldContain("- labels updated on 21 issues");
        text.ShouldNotContain("CARD-");
        text.ShouldNotContain("+status:done");
    }

    [Test]
    public void A_long_external_key_is_omitted_but_the_identifier_is_kept()
    {
        var board = Board("B",
            Change(TrackerSyncChangeKind.Created, "CARD-0005", "a-very-long-external-key"));

        var text = TrackerSyncSummaryFormatter.Format([(board, GitHubConfig())])!;

        text.ShouldContain("- 1 issue created on GitHub: CARD-0005");
        text.ShouldNotContain("a-very-long-external-key");
    }

    [Test]
    public void Multiple_boards_are_blank_line_separated_and_zero_change_boards_are_omitted()
    {
        var changed = Board("Board A", Change(TrackerSyncChangeKind.CommentIn, "CARD-0001", "#1"));
        var quiet = Board("Board B");
        var alsoChanged = Board("Board C", Change(TrackerSyncChangeKind.Created, "CARD-0002", "#2"));

        var text = TrackerSyncSummaryFormatter.Format(
        [
            (changed, GitHubConfig()),
            (quiet, GitHubConfig()),
            (alsoChanged, GitHubConfig())
        ])!;

        text.ShouldNotContain("Board B");
        var blocks = text.Split("\n\n");
        blocks.Length.ShouldBe(2);
        blocks[0].ShouldStartWith("Antiphon <-> GitHub sync: Board A");
        blocks[1].ShouldStartWith("Antiphon <-> GitHub sync: Board C");
    }

    [Test]
    public void The_issues_link_only_appears_for_github_com_with_a_known_repository()
    {
        var board = Board("B", Change(TrackerSyncChangeKind.CommentIn, "CARD-0001", "#1"));

        // Explicit github.com API base — the default.
        TrackerSyncSummaryFormatter.Format([(board, GitHubConfig())])!
            .ShouldContain("https://github.com/michal-ciechan/Antiphon/issues");

        // Unset base_url.
        TrackerSyncSummaryFormatter.Format([(board, GitHubConfig(baseUrl: ""))])!
            .ShouldContain("https://github.com/michal-ciechan/Antiphon/issues");

        // GitHub Enterprise — a github.com link would be a lie.
        TrackerSyncSummaryFormatter.Format([(board, GitHubConfig(baseUrl: "https://ghe.example.test/api/v3"))])!
            .ShouldNotContain("https://github.com/");

        // No repository configured.
        TrackerSyncSummaryFormatter.Format([(board, GitHubConfig(repository: null))])!
            .ShouldNotContain("https://github.com/");

        // No config at all (parse failed) — still a message, no link, generic tracker word.
        var noConfig = TrackerSyncSummaryFormatter.Format([(board, null)])!;
        noConfig.ShouldNotContain("https://github.com/");
        noConfig.ShouldStartWith("Antiphon <-> tracker sync: B");
    }

    [Test]
    public void Compact_boards_that_cannot_fit_are_omitted_as_complete_blocks()
    {
        // 400 boards, each a couple of lines — comfortably past the cap.
        var boards = Enumerable.Range(1, 400)
            .Select(i => (
                Board($"Board number {i} with a deliberately long name",
                    Change(TrackerSyncChangeKind.CommentIn, $"CARD-{i:0000}", $"#{i}")),
                (IssueTrackerConfig?)GitHubConfig()))
            .ToList();

        var text = TrackerSyncSummaryFormatter.Format(boards)!;

        text.Length.ShouldBeLessThanOrEqualTo(TrackerSyncSummaryFormatter.MaxChars);
        text.Length.ShouldBeLessThan(4096, "Telegram rejects anything longer");
        text.ShouldNotEndWith("…");
        text.ShouldContain("more boards omitted)");
        text.ShouldContain("Board number 1 with a deliberately long name");
        text.ShouldNotContain("Board number 400 with a deliberately long name");

        var bodyBlocks = text.Split("\n\n");
        bodyBlocks[^1].ShouldMatch(@"^\(\+\d+ more boards omitted\)$");
        foreach (var block in bodyBlocks[..^1])
        {
            block.ShouldStartWith("Antiphon <-> GitHub sync: Board number ");
            block.ShouldContain("- 1 comment in from GitHub");
            block.ShouldEndWith("https://github.com/michal-ciechan/Antiphon/issues");
            block.ShouldNotContain("CARD-");
        }
    }

    [Test]
    public void Labels_and_comments_downgrade_before_state_transitions_lose_detail()
    {
        var longDelta = new string('x', 80);
        var noisy = Enumerable.Range(1, 12)
            .Select(board => Board($"Noisy {board}",
                Enumerable.Range(1, 20)
                    .Select(i => Change(TrackerSyncChangeKind.LabelsChanged, $"CARD-{board:00}{i:00}", $"#{board}{i}",
                        added: [longDelta], removed: [longDelta]))
                    .Concat(Enumerable.Range(1, 20)
                        .Select(i => Change(TrackerSyncChangeKind.CommentIn, $"CARD-NOISE-{board:00}-{i:00}", $"#{board}c{i}")))
                    .ToArray()))
            .ToArray();
        var important = Board("Important",
            Change(TrackerSyncChangeKind.ClosedOnGitHub, "CARD-9999", "#99"),
            Change(TrackerSyncChangeKind.LabelsChanged, "CARD-9001", "#9001",
                added: [longDelta], removed: [longDelta]));

        var boards = noisy
            .Select(b => (b, (IssueTrackerConfig?)GitHubConfig()))
            .Prepend((important, GitHubConfig()))
            .ToList();

        var text = TrackerSyncSummaryFormatter.Format(boards)!;

        text.Length.ShouldBeLessThanOrEqualTo(TrackerSyncSummaryFormatter.MaxChars);
        text.ShouldContain("- 1 issue closed on GitHub: CARD-9999 (#99)");
        text.ShouldContain("- labels updated on");
        text.ShouldNotContain("+" + longDelta);
        text.ShouldNotContain("CARD-NOISE-");
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static TrackerSyncBoardResult Board(string name, params TrackerSyncChange[] changes) =>
        new(Guid.NewGuid(), name, 0, 0, 0, 0, 0, 0, []) { Changes = changes };

    private static TrackerSyncChange Change(
        TrackerSyncChangeKind kind,
        string identifier,
        string key,
        IReadOnlyList<string>? added = null,
        IReadOnlyList<string>? removed = null) =>
        new(kind, identifier, key, $"https://github.com/michal-ciechan/Antiphon/issues/{key.TrimStart('#')}")
        {
            Added = added,
            Removed = removed
        };

    private static IssueTrackerConfig GitHubConfig(
        string? baseUrl = "https://api.github.com",
        string? repository = "michal-ciechan/Antiphon") =>
        new(TrackerKind.GitHubIssues, baseUrl ?? "", null, repository, ["open"], null, null,
            new Dictionary<string, string>());
}
