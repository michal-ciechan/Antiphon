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
            Change(TrackerSyncChangeKind.LabelsChanged, "CARD-0170", "#10"),
            Change(TrackerSyncChangeKind.LabelsChanged, "CARD-0171", "#15"),
            Change(TrackerSyncChangeKind.LabelsChanged, "CARD-0172", "#16"));

        var result = TrackerSyncSummaryFormatter.Format([(board, GitHubConfig())]);

        result.ShouldBe(string.Join('\n',
            "Antiphon <-> GitHub sync: Antiphon board",
            "- 2 comments in from GitHub: CARD-0170, CARD-0171",
            "- 1 comment posted to GitHub: CARD-0166",
            "- 1 issue closed on GitHub: CARD-0166 (#14)",
            "- 1 issue reopened from GitHub: CARD-0150 (#9)",
            "- 1 issue created on GitHub: CARD-0172 (#16)",
            "- labels updated on 3 issues",
            "https://github.com/michal-ciechan/Antiphon/issues"));
    }

    [Test]
    public void Labels_line_is_count_only_and_comes_last()
    {
        var board = Board("B",
            Change(TrackerSyncChangeKind.LabelsChanged, "CARD-0001", "#1"),
            Change(TrackerSyncChangeKind.CommentIn, "CARD-0002", "#2"));

        var lines = TrackerSyncSummaryFormatter.Format([(board, GitHubConfig())])!.Split('\n');

        lines[1].ShouldBe("- 1 comment in from GitHub: CARD-0002");
        lines[2].ShouldBe("- labels updated on 1 issue");
        lines[2].ShouldNotContain("CARD-0001");
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
    public void Identifiers_dedupe_and_cap_at_five_with_a_plus_n_more()
    {
        var changes = Enumerable.Range(1, 7)
            .Select(i => Change(TrackerSyncChangeKind.CommentIn, $"CARD-000{i}", $"#{i}"))
            // A second comment on the first card must not list it twice.
            .Append(Change(TrackerSyncChangeKind.CommentIn, "CARD-0001", "#1"))
            .ToArray();

        var text = TrackerSyncSummaryFormatter.Format([(Board("B", changes), GitHubConfig())])!;

        text.ShouldContain(
            "- 8 comments in from GitHub: CARD-0001, CARD-0002, CARD-0003, CARD-0004, CARD-0005, +2 more");
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
    public void The_message_is_capped_at_3500_chars_with_an_ellipsis()
    {
        // 400 boards, each a couple of lines — comfortably past the cap.
        var boards = Enumerable.Range(1, 400)
            .Select(i => (
                Board($"Board number {i} with a deliberately long name",
                    Change(TrackerSyncChangeKind.CommentIn, $"CARD-{i:0000}", $"#{i}")),
                (IssueTrackerConfig?)GitHubConfig()))
            .ToList();

        var text = TrackerSyncSummaryFormatter.Format(boards)!;

        text.Length.ShouldBe(TrackerSyncSummaryFormatter.MaxChars + 1);
        text.ShouldEndWith("…");
        text.Length.ShouldBeLessThan(4096, "Telegram rejects anything longer");
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static TrackerSyncBoardResult Board(string name, params TrackerSyncChange[] changes) =>
        new(Guid.NewGuid(), name, 0, 0, 0, 0, 0, 0, []) { Changes = changes };

    private static TrackerSyncChange Change(TrackerSyncChangeKind kind, string identifier, string key) =>
        new(kind, identifier, key, $"https://github.com/michal-ciechan/Antiphon/issues/{key.TrimStart('#')}");

    private static IssueTrackerConfig GitHubConfig(
        string? baseUrl = "https://api.github.com",
        string? repository = "michal-ciechan/Antiphon") =>
        new(TrackerKind.GitHubIssues, baseUrl ?? "", null, repository, ["open"], null, null,
            new Dictionary<string, string>());
}
