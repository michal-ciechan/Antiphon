using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using Land = Antiphon.Tests.Application.ExternalTrackerSyncLandingColumnTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0327 S1+S2: tracker sync must not clobber a Human importance; Auto follows the
/// author-aware ranking; every content overwrite writes a ContentEdit revision.
/// Reuses the <see cref="ExternalTrackerSyncLandingColumnTests"/> harness.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ExternalTrackerSyncImportanceProvenanceTests
{
    [Test]
    public async Task Import_lands_Normal_Auto_when_the_issue_has_no_priority_label()
    {
        await using var db = Land.CreateContext();
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot);
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [
                Land.Issue("acme/app#3", "#3", "Imported", "body")
            ]);
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = Land.CreateContext();
            var card = await verify.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == graph.Board.Id);
            card.Importance.ShouldBe(CardImportance.Normal);
            card.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Auto);
            card.RevisionCount.ShouldBe(0);
        }
        finally
        {
            await Land.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Human_High_survives_a_priority_zero_sync_with_no_new_revision()
    {
        await using var db = Land.CreateContext();
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot);
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [
                Land.Issue("acme/app#3", "#3", "Imported", "body")
            ]);
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            Guid cardId;
            await using (var mutate = Land.CreateContext())
            {
                var card = await mutate.Cards.SingleAsync(c => c.BoardId == graph.Board.Id);
                cardId = card.Id;
                card.Importance = CardImportance.High;
                card.ImportanceProvenance = CardImportanceProvenance.Human;
                await mutate.SaveChangesAsync();
            }

            await using (var sync2 = Land.CreateContext())
                await Land.NewSut(sync2, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = Land.CreateContext();
            var after = await verify.Cards
                .Include(c => c.Revisions)
                .SingleAsync(c => c.Id == cardId);
            after.Importance.ShouldBe(CardImportance.High);
            after.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Human);
            after.RevisionCount.ShouldBe(0);
            after.Revisions.ShouldBeEmpty();
        }
        finally
        {
            await Land.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Auto_follows_a_priority_critical_label_and_writes_one_external_tracker_revision()
    {
        await using var db = Land.CreateContext();
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot);
            var open = Land.Issue("acme/app#3", "#3", "Imported", "body");
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [open]);
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            tracker.Candidates =
            [
                Land.Issue("acme/app#3", "#3", "Imported", "body", priority: 5, labels: ["priority:critical"])
            ];
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = Land.CreateContext();
            var card = await verify.Cards
                .Include(c => c.Revisions)
                .SingleAsync(c => c.BoardId == graph.Board.Id);
            card.Importance.ShouldBe(CardImportance.Critical);
            card.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Auto);
            card.RevisionCount.ShouldBe(1);
            var revision = card.Revisions.ShouldHaveSingleItem();
            revision.Kind.ShouldBe(CardRevisionKind.ContentEdit);
            revision.EditedBy.ShouldBe("external-tracker");
            revision.Importance.ShouldBe(CardImportance.Normal);
            revision.Reason.ShouldNotBeNull().ShouldContain("importance");
            revision.Reason.ShouldContain("#3");
        }
        finally
        {
            await Land.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task GitHub_title_change_on_an_Auto_card_writes_one_revision_holding_the_old_title()
    {
        await using var db = Land.CreateContext();
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot);
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [
                Land.Issue("acme/app#3", "#3", "Original title", "body")
            ]);
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            tracker.Candidates =
            [
                Land.Issue("acme/app#3", "#3", "Renamed on GitHub", "body")
            ];
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = Land.CreateContext();
            var card = await verify.Cards
                .Include(c => c.Revisions)
                .SingleAsync(c => c.BoardId == graph.Board.Id);
            card.Title.ShouldBe("Renamed on GitHub");
            card.RevisionCount.ShouldBe(1);
            var revision = card.Revisions.ShouldHaveSingleItem();
            revision.Kind.ShouldBe(CardRevisionKind.ContentEdit);
            revision.EditedBy.ShouldBe("external-tracker");
            revision.Title.ShouldBe("Original title");
            revision.Reason.ShouldNotBeNull().ShouldContain("title");
        }
        finally
        {
            await Land.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Unchanged_issue_on_a_second_pass_writes_nothing()
    {
        await using var db = Land.CreateContext();
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot);
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [
                Land.Issue("acme/app#3", "#3", "Imported", "body")
            ]);
            var firstAt = new DateTime(2026, 9, 2, 16, 0, 0, DateTimeKind.Utc);
            await Land.NewSut(db, tracker).SyncAsync(firstAt, graph.Board.Id, CancellationToken.None);

            await using var afterFirst = Land.CreateContext();
            var first = await afterFirst.Cards.SingleAsync(c => c.BoardId == graph.Board.Id);
            var revisionCount = first.RevisionCount;
            var updatedAt = first.UpdatedAt;
            var token = first.ConcurrencyToken;

            await using (var sync2 = Land.CreateContext())
                await Land.NewSut(sync2, tracker).SyncAsync(firstAt.AddMinutes(30), graph.Board.Id, CancellationToken.None);

            await using var verify = Land.CreateContext();
            var second = await verify.Cards
                .Include(c => c.Revisions)
                .SingleAsync(c => c.Id == first.Id);
            second.RevisionCount.ShouldBe(revisionCount);
            second.UpdatedAt.ShouldBe(updatedAt);
            second.ConcurrencyToken.ShouldBe(token);
            second.Revisions.ShouldBeEmpty();
        }
        finally
        {
            await Land.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Export_origin_refs_are_untouched_for_title_and_importance()
    {
        await using var db = Land.CreateContext();
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot);
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [
                Land.Issue("acme/app#3", "#3", "Original", "body")
            ]);
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            Guid cardId;
            await using (var mutate = Land.CreateContext())
            {
                var card = await mutate.Cards.Include(c => c.ExternalIssueRef).SingleAsync(c => c.BoardId == graph.Board.Id);
                cardId = card.Id;
                card.Title = "Antiphon title";
                card.Importance = CardImportance.High;
                card.ExternalIssueRef!.Origin = ExternalIssueOrigin.AntiphonExport;
                await mutate.SaveChangesAsync();
            }

            tracker.Candidates =
            [
                Land.Issue("acme/app#3", "#3", "GitHub renamed", "body", priority: 5)
            ];
            await using (var sync2 = Land.CreateContext())
                await Land.NewSut(sync2, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = Land.CreateContext();
            var after = await verify.Cards
                .Include(c => c.Revisions)
                .SingleAsync(c => c.Id == cardId);
            after.Title.ShouldBe("Antiphon title");
            after.Importance.ShouldBe(CardImportance.High);
            after.Revisions.Count(r => r.EditedBy == "external-tracker" && r.Kind == CardRevisionKind.ContentEdit)
                .ShouldBe(0);
        }
        finally
        {
            await Land.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Operator_author_with_no_label_imports_High_Auto_and_second_pass_is_idempotent()
    {
        await using var db = Land.CreateContext();
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot, "[alice]");
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [
                Land.Issue("acme/app#31", "#31", "From operator", "body", author: "alice")
            ]);
            var firstAt = new DateTime(2026, 9, 2, 16, 0, 0, DateTimeKind.Utc);
            await Land.NewSut(db, tracker).SyncAsync(firstAt, graph.Board.Id, CancellationToken.None);

            await using var afterFirst = Land.CreateContext();
            var first = await afterFirst.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == graph.Board.Id);
            first.Importance.ShouldBe(CardImportance.High);
            first.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Auto);
            first.ExternalIssueRef!.Author.ShouldBe("alice");
            first.ExternalIssueRef.AuthorIsOperator.ShouldBe(true);
            first.RevisionCount.ShouldBe(0);
            var updatedAt = first.UpdatedAt;

            await using (var sync2 = Land.CreateContext())
                await Land.NewSut(sync2, tracker).SyncAsync(firstAt.AddMinutes(30), graph.Board.Id, CancellationToken.None);

            await using var verify = Land.CreateContext();
            var second = await verify.Cards.SingleAsync(c => c.Id == first.Id);
            second.Importance.ShouldBe(CardImportance.High);
            second.RevisionCount.ShouldBe(0);
            second.UpdatedAt.ShouldBe(updatedAt);
        }
        finally
        {
            await Land.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Operator_author_with_priority_low_imports_Low()
    {
        await using var db = Land.CreateContext();
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot, "[alice]");
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [
                new Antiphon.Server.Application.Interfaces.TrackedIssue(
                    "acme/app#99",
                    "#99",
                    "Low on purpose",
                    "body",
                    "open",
                    1,
                    ["priority:low"],
                    [],
                    "https://github.test/acme/app/issues/99",
                    "{}",
                    "alice")
            ]);
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = Land.CreateContext();
            var card = await verify.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == graph.Board.Id);
            card.ExternalIssueRef!.Author.ShouldBe("alice");
            card.ExternalIssueRef.AuthorIsOperator.ShouldBe(true);
            card.Importance.ShouldBe(CardImportance.Low);
            card.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Auto);
        }
        finally
        {
            await Land.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Non_operator_author_imports_Normal_with_AuthorIsOperator_false()
    {
        await using var db = Land.CreateContext();
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot, "[alice]");
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [
                Land.Issue("acme/app#32", "#32", "From outside", "body", author: "bob")
            ]);
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = Land.CreateContext();
            var card = await verify.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == graph.Board.Id);
            card.Importance.ShouldBe(CardImportance.Normal);
            card.ImportanceProvenance.ShouldBe(CardImportanceProvenance.Auto);
            card.ExternalIssueRef!.Author.ShouldBe("bob");
            card.ExternalIssueRef.AuthorIsOperator.ShouldBe(false);
        }
        finally
        {
            await Land.CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Operator_logins_unset_leaves_AuthorIsOperator_null_and_does_not_raise_High()
    {
        await using var db = Land.CreateContext();
        var tempRoot = Land.NewTempRoot();
        try
        {
            var graph = await Land.SeedTrackedBoardAsync(db, tempRoot);
            var tracker = new Land.FakeIssueTracker(TrackerKind.GitHubIssues, [
                Land.Issue("acme/app#33", "#33", "Anyone", "body", author: "alice")
            ]);
            await Land.NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = Land.CreateContext();
            var card = await verify.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == graph.Board.Id);
            card.Importance.ShouldBe(CardImportance.Normal);
            card.ExternalIssueRef!.Author.ShouldBe("alice");
            card.ExternalIssueRef.AuthorIsOperator.ShouldBeNull();
        }
        finally
        {
            await Land.CleanupAsync(tempRoot);
        }
    }
}
