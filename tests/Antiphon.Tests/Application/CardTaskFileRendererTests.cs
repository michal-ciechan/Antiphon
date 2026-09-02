using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0004 S1: the pure renderer. No database, no I/O — filename, frontmatter, body and INDEX
/// shape are pinned here so the writer can treat the output as bytes.
/// </summary>
public class CardTaskFileRendererTests
{
    [Test]
    public void AutoCommit_defaults_false_so_the_first_restart_cannot_commit_unreviewed()
    {
        var settings = new CardFileSyncSettings();
        settings.AutoCommit.ShouldBeFalse(
            "operator decision over the plan's original true: ~315 files must not land on master unreviewed");
        settings.Enabled.ShouldBeTrue();
        settings.IntervalSeconds.ShouldBe(60);
    }

    [Test]
    public void Frontmatter_carries_every_record_field_and_quotes_a_hostile_title()
    {
        var id = Guid.Parse("86b6542a-5f1e-4107-b04b-46d81c636225");
        var card = MakeCard(
            id,
            "CARD-0004",
            title: """Card: "the #1" task""",
            description: "Line1\r\n---\r\nLine3",
            status: CardStatus.NeedsDecision,
            importance: CardImportance.Normal,
            labelsJson: """["bug,grok,delegation","cards"]""",
            created: new DateTime(2026, 8, 9, 15, 5, 43, DateTimeKind.Utc),
            started: new DateTime(2026, 8, 20, 9, 12, 0, DateTimeKind.Utc),
            completed: new DateTime(2026, 8, 21, 17, 40, 3, DateTimeKind.Utc));
        card.ExternalIssueRef = new ExternalIssueRef
        {
            TrackerKind = TrackerKind.GitHubIssues,
            ExternalKey = "#12",
            Url = "https://github.com/example/repo/issues/12",
        };
        card.ArchivedAt = new DateTime(2026, 8, 25, 8, 0, 0, DateTimeKind.Utc);
        card.ArchivedBy = "operator";
        card.ArchivedReason = "duplicate of CARD-0210";
        card.TerminalReason = "shipped as the plan";
        card.OwnerSessionId = Guid.NewGuid();
        card.AssignedAgentId = Guid.NewGuid();
        card.AgentQueuePosition = 3;
        card.ConcurrencyToken = Guid.NewGuid();
        card.UpdatedAt = DateTime.UtcNow;
        card.AutoDispatchHeldAt = DateTime.UtcNow;
        card.DecisionNotifiedAt = DateTime.UtcNow;
        card.RevisionCount = 9;

        var rendered = CardTaskFileRenderer.RenderCard(card);

        rendered.ShouldContain("id: 86b6542a-5f1e-4107-b04b-46d81c636225");
        rendered.ShouldContain("identifier: CARD-0004");
        rendered.ShouldContain("title: \"Card: \\\"the #1\\\" task\"");
        rendered.ShouldContain("status: NeedsDecision");
        rendered.ShouldContain("importance: Normal");
        rendered.ShouldContain("urgency: Normal");
        rendered.ShouldContain("labels: [\"bug,grok,delegation\", \"cards\"]");
        rendered.ShouldContain("created: 2026-08-09T15:05:43Z");
        rendered.ShouldContain("started: 2026-08-20T09:12:00Z");
        rendered.ShouldContain("completed: 2026-08-21T17:40:03Z");
        rendered.ShouldContain("external_tracker: GitHubIssues");
        rendered.ShouldContain("external_key: \"#12\"");
        rendered.ShouldContain("external_url: \"https://github.com/example/repo/issues/12\"");
        rendered.ShouldContain("archived: 2026-08-25T08:00:00Z");
        rendered.ShouldContain("archived_by: \"operator\"");
        rendered.ShouldContain("archived_reason: \"duplicate of CARD-0210\"");
        rendered.ShouldContain("# CARD-0004 — Card: \"the #1\" task");
        rendered.ShouldContain("Line1\n---\nLine3");
        rendered.ShouldContain("## Outcome");
        rendered.ShouldContain("shipped as the plan");
        rendered.ShouldNotContain("\r");
        rendered.ShouldEndWith("\n");
        rendered.ShouldNotEndWith("\n\n");

        rendered.ShouldNotContain("OwnerSession");
        rendered.ShouldNotContain("AssignedAgent");
        rendered.ShouldNotContain("AgentQueue");
        rendered.ShouldNotContain("ConcurrencyToken");
        rendered.ShouldNotContain("updated:");
        rendered.ShouldNotContain("UpdatedAt");
        rendered.ShouldNotContain("AutoDispatch");
        rendered.ShouldNotContain("DecisionNotified");
        rendered.ShouldNotContain("RevisionCount");
        rendered.ShouldNotContain("Worktree");
        rendered.ShouldNotContain("WorkflowRun");
    }

    [Test]
    public void Nullable_keys_and_outcome_are_omitted_on_a_live_card()
    {
        var card = MakeCard(
            Guid.NewGuid(),
            "CARD-0001",
            title: "plain",
            description: "body",
            status: CardStatus.Backlog);

        var rendered = CardTaskFileRenderer.RenderCard(card);

        rendered.ShouldNotContain("started:");
        rendered.ShouldNotContain("completed:");
        rendered.ShouldNotContain("external_");
        rendered.ShouldNotContain("archived");
        rendered.ShouldNotContain("## Outcome");
        rendered.ShouldContain("# CARD-0001 — plain");
        rendered.ShouldContain("\n\nbody\n");
    }

    [Test]
    public void Outcome_is_present_only_with_a_terminal_reason()
    {
        var with = MakeCard(Guid.NewGuid(), "CARD-0002", "t", "d", CardStatus.Done);
        with.TerminalReason = "done because X";
        var without = MakeCard(Guid.NewGuid(), "CARD-0003", "t", "d", CardStatus.Done);

        CardTaskFileRenderer.RenderCard(with).ShouldContain("## Outcome");
        CardTaskFileRenderer.RenderCard(without).ShouldNotContain("## Outcome");
    }

    [Test]
    public void Index_group_order_omits_empty_groups_and_orders_inside_a_group()
    {
        var backlogHigh = MakeCard(Guid.NewGuid(), "CARD-0002", "later id, more important", "", CardStatus.Backlog, CardImportance.High);
        var backlogLow = MakeCard(Guid.NewGuid(), "CARD-0001", "earlier id, less important", "", CardStatus.Backlog, CardImportance.Low);
        var backlogMid = MakeCard(Guid.NewGuid(), "CARD-0003", "same importance, later id", "", CardStatus.Backlog, CardImportance.High);
        var done = MakeCard(Guid.NewGuid(), "CARD-0004", "finished", "", CardStatus.Done, CardImportance.Normal);
        var archived = MakeCard(Guid.NewGuid(), "CARD-0005", "gone", "", CardStatus.Done, CardImportance.Low);
        archived.ArchivedAt = new DateTime(2026, 8, 25, 8, 0, 0, DateTimeKind.Utc);
        var inProgress = MakeCard(Guid.NewGuid(), "CARD-0006", "working", "", CardStatus.InProgress, CardImportance.Normal,
            labelsJson: """["reliability"]""");

        var cards = new[] { backlogLow, done, archived, backlogHigh, inProgress, backlogMid };
        var names = cards.ToDictionary(c => c.Id, c => CardTaskFileRenderer.CardFileName(c.Identifier, c.Title));

        var index = CardTaskFileRenderer.RenderIndex("Antiphon", cards, names);

        index.ShouldContain("# Antiphon — cards");
        index.ShouldContain("6 cards, 1 archived.");
        index.ShouldNotContain("## Needs decision");
        index.ShouldNotContain("## Review");
        index.ShouldNotContain("## Canceled");

        var inProgressAt = index.IndexOf("## In progress (1)", StringComparison.Ordinal);
        var backlogAt = index.IndexOf("## Backlog (3)", StringComparison.Ordinal);
        var doneAt = index.IndexOf("## Done (1)", StringComparison.Ordinal);
        var archivedAt = index.IndexOf("## Archived (1)", StringComparison.Ordinal);
        inProgressAt.ShouldBeGreaterThan(0);
        backlogAt.ShouldBeGreaterThan(inProgressAt);
        doneAt.ShouldBeGreaterThan(backlogAt);
        archivedAt.ShouldBeGreaterThan(doneAt);

        var backlogBlock = index[backlogAt..doneAt];
        var highPos = backlogBlock.IndexOf("CARD-0002", StringComparison.Ordinal);
        var midPos = backlogBlock.IndexOf("CARD-0003", StringComparison.Ordinal);
        var lowPos = backlogBlock.IndexOf("CARD-0001", StringComparison.Ordinal);
        highPos.ShouldBeLessThan(midPos, "equal rank then CreatedAt then identifier");
        midPos.ShouldBeLessThan(lowPos);

        index.ShouldContain($"- [CARD-0006]({names[inProgress.Id]}) — working `reliability`");
        index.ShouldContain("`high`");
        index.ShouldNotContain("\r");
        index.ShouldEndWith("\n");
        index.ShouldNotEndWith("\n\n");
    }

    [Test]
    public void Slug_rules_cap_at_60_and_collapse_non_alphanumerics()
    {
        CardTaskFileRenderer.BoardSlug("Gym Stat").ShouldBe("gym-stat");
        CardTaskFileRenderer.BoardSlug("  Foo---Bar!! ").ShouldBe("foo-bar");
        CardTaskFileRenderer.Slugify(new string('a', 80)).Length.ShouldBe(60);
        CardTaskFileRenderer.Slugify(new string('a', 58) + "-xyz").ShouldBe(new string('a', 58) + "-x");

        var longTitle = new string('z', 80);
        var file = CardTaskFileRenderer.CardFileName("CARD-0001", longTitle);
        file.ShouldStartWith("CARD-0001-");
        file.ShouldEndWith(".md");
        file["CARD-0001-".Length..^".md".Length].Length.ShouldBe(60);
    }

    [Test]
    public void Identifier_sanitising_and_empty_title_slug()
    {
        CardTaskFileRenderer.SanitizeIdentifier("CARD-0004").ShouldBe("CARD-0004");
        CardTaskFileRenderer.SanitizeIdentifier("CARD:0004!").ShouldBe("CARD-0004-");
        CardTaskFileRenderer.CardFileName("CARD:0004!", "!!!").ShouldBe("CARD-0004-.md");
        CardTaskFileRenderer.CardFileName("CARD-0004", "Card -> repo task file sync")
            .ShouldBe("CARD-0004-card-repo-task-file-sync.md");
    }

    [Test]
    public void YamlQuote_escapes_backslash_and_quotes()
    {
        CardTaskFileRenderer.YamlQuote("a\\b\"c").ShouldBe("\"a\\\\b\\\"c\"");
    }

    private static Card MakeCard(
        Guid id,
        string identifier,
        string title,
        string description,
        CardStatus status,
        CardImportance importance = CardImportance.Normal,
        CardUrgency urgency = CardUrgency.Normal,
        string labelsJson = "[]",
        DateTime? created = null,
        DateTime? started = null,
        DateTime? completed = null) =>
        new()
        {
            Id = id,
            Identifier = identifier,
            Title = title,
            Description = description,
            Status = status,
            Importance = importance,
            Urgency = urgency,
            LabelsJson = labelsJson,
            CreatedAt = created ?? new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc),
            StartedAt = started,
            CompletedAt = completed,
        };
}
