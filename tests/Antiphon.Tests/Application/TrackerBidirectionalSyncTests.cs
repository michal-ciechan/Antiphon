using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.ApiKeys;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0166 S4–S6: comments IN/OUT, labels, state, creates, loop + conflict pins.</summary>
[Category("Integration")]
[NotInParallel]
public class TrackerBidirectionalSyncTests
{
    [Test]
    public async Task Comments_IN_land_as_External_CardComments_and_loop_pin_a_holds()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues);
            fake.Candidates =
            [
                Issue("acme/app#1", "open", "Title", "Body", ["backend"])
            ];
            fake.CommentsSince =
            [
                new TrackedIssueComment(
                    "101",
                    "acme/app#1",
                    "alice",
                    "Hello from GH",
                    "https://github.test/acme/app/issues/1#issuecomment-101",
                    clock.GetUtcNow().UtcDateTime.AddMinutes(-1),
                    clock.GetUtcNow().UtcDateTime.AddMinutes(-1))
            ];

            var sut = NewSut(db, fake, clock);
            var first = await sut.RunAsync(graph.Board.Id, CancellationToken.None);
            first.Boards.Single().CommentsIn.ShouldBe(1);

            await using (var verify = CreateContext())
            {
                var rows = await verify.CardComments.Where(c => c.CardId == graph.Card.Id).ToListAsync();
                rows.Count.ShouldBe(1);
                rows[0].Origin.ShouldBe(CardCommentOrigin.External);
                rows[0].Author.ShouldBe("alice");
                rows[0].ExternalCommentId.ShouldBe("101");
            }

            // Loop pin (a): subsequent RunAsync must not PostComment the inbound row.
            fake.PostCommentCalls.Clear();
            fake.CommentsSince =
            [
                new TrackedIssueComment(
                    "101",
                    "acme/app#1",
                    "alice",
                    "Hello from GH",
                    "https://github.test/acme/app/issues/1#issuecomment-101",
                    clock.GetUtcNow().UtcDateTime.AddMinutes(-1),
                    clock.GetUtcNow().UtcDateTime.AddMinutes(-1))
            ];
            var second = await sut.RunAsync(graph.Board.Id, CancellationToken.None);
            second.Boards.Single().CommentsIn.ShouldBe(0);
            fake.PostCommentCalls.Count.ShouldBe(0);
            (await db.CardComments.CountAsync(c => c.CardId == graph.Card.Id)).ShouldBe(1);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Loop_pin_b_Antiphon_comment_out_echo_stamps_ExternalCommentId_with_zero_new_rows()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            var commentId = Guid.NewGuid();
            db.CardComments.Add(new CardComment
            {
                Id = commentId,
                CardId = graph.Card.Id,
                Body = "From Antiphon",
                Author = "operator",
                Origin = CardCommentOrigin.Antiphon,
                CreatedAt = clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])],
                EchoPostedComments = true
            };

            var sut = NewSut(db, fake, clock);
            var first = await sut.RunAsync(graph.Board.Id, CancellationToken.None);
            first.Boards.Single().CommentsOut.ShouldBe(1);
            fake.PostCommentCalls.Count.ShouldBe(1);
            fake.PostCommentCalls[0].Body.ShouldContain($"<!-- antiphon:comment={commentId:N} -->");

            var stored = await db.CardComments.SingleAsync(c => c.Id == commentId);
            stored.SyncedAt.ShouldNotBeNull();
            stored.ExternalCommentId.ShouldNotBeNull();

            // Echo on next pull — marker match, zero new rows
            fake.PostCommentCalls.Clear();
            var second = await sut.RunAsync(graph.Board.Id, CancellationToken.None);
            second.Boards.Single().CommentsIn.ShouldBe(0);
            fake.PostCommentCalls.Count.ShouldBe(0);
            (await db.CardComments.CountAsync(c => c.CardId == graph.Card.Id)).ShouldBe(1);

            // Steady-state: third run zero writes
            fake.ClearWriteCounters();
            var third = await sut.RunAsync(graph.Board.Id, CancellationToken.None);
            fake.WriteCallCount.ShouldBe(0);
            third.Boards.Single().CommentsOut.ShouldBe(0);
            third.Boards.Single().LabelsChanged.ShouldBe(0);
            third.Boards.Single().StateChanges.ShouldBe(0);
            third.Boards.Single().Creates.ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Claim_before_post_clears_SyncedAt_when_PostComment_throws()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            var commentId = Guid.NewGuid();
            db.CardComments.Add(new CardComment
            {
                Id = commentId,
                CardId = graph.Card.Id,
                Body = "Will fail",
                Author = "operator",
                Origin = CardCommentOrigin.Antiphon,
                CreatedAt = clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])],
                ThrowOnPostComment = true
            };
            var sut = NewSut(db, fake, clock);
            await sut.RunAsync(graph.Board.Id, CancellationToken.None);

            var stored = await db.CardComments.SingleAsync(c => c.Id == commentId);
            stored.SyncedAt.ShouldBeNull();
            stored.ExternalCommentId.ShouldBeNull();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Terminal_Done_closes_with_completed_and_conflict_import_origin_reopens_card()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock, origin: ExternalIssueOrigin.ExternalImport);
            // Move card to Done
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            graph.Card.CompletedAt = clock.GetUtcNow().UtcDateTime;
            graph.Card.ExternalIssueRef!.LastKnownExternalState = "open";
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", ["status:backlog"])]
            };
            var sut = NewSut(db, fake, clock);
            var result = await sut.RunAsync(graph.Board.Id, CancellationToken.None);
            result.Boards.Single().StateChanges.ShouldBe(1);
            fake.SetStateCalls.Single().State.ShouldBe("closed");
            fake.SetStateCalls.Single().StateReason.ShouldBe("completed");
            // CARD-0171: the close is itemised, named by card and tracker key.
            var closed = result.Boards.Single().Changes
                .Single(c => c.Kind == TrackerSyncChangeKind.ClosedOnGitHub);
            closed.CardIdentifier.ShouldBe(graph.Card.Identifier);
            closed.ExternalKey.ShouldBe("#1");

            // Conflict pin: import-origin, issue reopened externally while card was completed.
            graph.Card.ExternalIssueRef!.LastKnownExternalState = "closed";
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            graph.Card.CompletedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
            fake.Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])];
            fake.ClearWriteCounters();

            var reopenRun = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();

            // CARD-0171 gap pin: before this card an external reopen incremented NOTHING, so a run
            // that only moved a card back out of Done looked like a no-op to every counter.
            reopenRun.ExternalReopens.ShouldBe(1);
            var reopenChange = reopenRun.Changes
                .Single(c => c.Kind == TrackerSyncChangeKind.ReopenedFromGitHub);
            reopenChange.CardIdentifier.ShouldBe(graph.Card.Identifier);
            reopenChange.ExternalKey.ShouldBe("#1");

            await using var verify = CreateContext();
            var card = await verify.Cards
                .Include(c => c.BoardColumn)
                .Include(c => c.Revisions)
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.Id == graph.Card.Id);
            card.BoardColumn.IsTerminal.ShouldBeFalse();
            card.ExternalIssueRef!.LastKnownExternalState.ShouldBe("open");
            var reopen = card.Revisions.Single(r => r.Kind == CardRevisionKind.Reopen);
            reopen.EditedBy.ShouldBe("external-tracker");
            reopen.Reason.ShouldContain("superseded local completion");
            // Issue must NOT be re-closed
            fake.SetStateCalls.ShouldBeEmpty();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Terminal_close_and_reopen_comment_echoes_create_zero_External_CardComments()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            graph.Card.CompletedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])],
                EchoPostedComments = true
            };
            var sut = NewSut(db, fake, clock);

            var close = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();
            close.StateChanges.ShouldBe(1);
            fake.PostCommentCalls.Single().Body.ShouldContain(
                $"<!-- antiphon:system-comment={graph.Card.Id:N} -->");

            var closeEcho = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();
            closeEcho.CommentsIn.ShouldBe(0);

            clock.Advance(TimeSpan.FromMinutes(1));
            graph.Card.BoardColumnId = graph.BacklogColumn.Id;
            graph.Card.BoardColumn = graph.BacklogColumn;
            graph.Card.Status = CardStatus.Backlog;
            graph.Card.CompletedAt = null;
            db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                RevisionNumber = ++graph.Card.RevisionCount,
                Kind = CardRevisionKind.Reopen,
                Reason = "continue work",
                EditedBy = "operator",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                Card = graph.Card
            });
            await db.SaveChangesAsync();

            fake.ClearWriteCounters();
            var reopen = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();
            reopen.StateChanges.ShouldBe(1);
            fake.PostCommentCalls.Single().Body.ShouldContain(
                $"<!-- antiphon:system-comment={graph.Card.Id:N} -->");

            var reopenEcho = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();
            reopenEcho.CommentsIn.ShouldBe(0);
            (await db.CardComments.CountAsync(c => c.CardId == graph.Card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Same_pass_status_label_rewrite_does_not_swallow_out_reopen()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            var closedAt = clock.GetUtcNow().UtcDateTime;
            graph.Card.ExternalIssueRef!.LastKnownExternalState = "closed";
            graph.Card.ExternalIssueRef.LastOutboundSyncedAt = closedAt;
            await db.SaveChangesAsync();

            clock.Advance(TimeSpan.FromMinutes(1));
            db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                RevisionNumber = ++graph.Card.RevisionCount,
                Kind = CardRevisionKind.Reopen,
                Reason = "continue work",
                EditedBy = "operator",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                Card = graph.Card
            });
            await db.SaveChangesAsync();

            // Production shape: the reopen revision is older than this pass's utcNow.
            clock.Advance(TimeSpan.FromMinutes(1));

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "closed", "Title", "Body", ["status:done"])]
            };
            var sut = NewSut(db, fake, clock);
            var result = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();

            // Both must fire in this pass. Labels-first stamps LastOutboundSyncedAt = utcNow and
            // the reopen gate (CreatedAt > LastOutboundSyncedAt) then fails; asserting both
            // pins the state-before-labels order.
            result.StateChanges.ShouldBe(1);
            result.LabelsChanged.ShouldBeGreaterThanOrEqualTo(1);
            result.Changes.ShouldContain(c => c.Kind == TrackerSyncChangeKind.ReopenedOnGitHub);
            result.Changes.ShouldContain(c => c.Kind == TrackerSyncChangeKind.LabelsChanged);
            fake.SetStateCalls.ShouldHaveSingleItem();
            fake.SetStateCalls[0].State.ShouldBe("open");
            fake.SetStateCalls[0].StateReason.ShouldBe("reopened");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Same_pass_content_edit_comment_does_not_swallow_out_reopen()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock, origin: ExternalIssueOrigin.ExternalImport);
            var closedAt = clock.GetUtcNow().UtcDateTime;
            graph.Card.ExternalIssueRef!.LastKnownExternalState = "closed";
            graph.Card.ExternalIssueRef.LastOutboundSyncedAt = closedAt;
            await db.SaveChangesAsync();

            clock.Advance(TimeSpan.FromMinutes(1));
            db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                RevisionNumber = ++graph.Card.RevisionCount,
                Kind = CardRevisionKind.ContentEdit,
                Reason = "clarify the acceptance criteria",
                EditedBy = "operator",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                Card = graph.Card
            });
            db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                RevisionNumber = ++graph.Card.RevisionCount,
                Kind = CardRevisionKind.Reopen,
                Reason = "continue work",
                EditedBy = "operator",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                Card = graph.Card
            });
            await db.SaveChangesAsync();

            // Production shape: both local revisions predate this pass's utcNow.
            clock.Advance(TimeSpan.FromMinutes(1));

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "closed", "Title", "Body", ["status:backlog"])]
            };
            var sut = NewSut(db, fake, clock);
            var result = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();

            // Content-edit comments stamp LastOutboundSyncedAt. If they run before state, the
            // reopen gate fails; this pins the state-before-content-edit-comment ordering.
            result.StateChanges.ShouldBeGreaterThanOrEqualTo(1);
            result.CommentsOut.ShouldBe(1);
            result.Changes.ShouldContain(c => c.Kind == TrackerSyncChangeKind.ReopenedOnGitHub);
            result.Changes.ShouldContain(c => c.Kind == TrackerSyncChangeKind.CommentOut);
            fake.SetStateCalls.ShouldHaveSingleItem();
            fake.SetStateCalls[0].State.ShouldBe("open");
            fake.SetStateCalls[0].StateReason.ShouldBe("reopened");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task External_tracker_content_edit_is_not_echoed_as_a_GitHub_comment()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock, origin: ExternalIssueOrigin.ExternalImport);
            db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                RevisionNumber = ++graph.Card.RevisionCount,
                Kind = CardRevisionKind.ContentEdit,
                Title = "old title",
                Reason = "External tracker #1 changed: title.",
                EditedBy = "external-tracker",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                Card = graph.Card
            });
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])]
            };
            var sut = NewSut(db, fake, clock);
            var result = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();

            result.CommentsOut.ShouldBe(0);
            fake.PostCommentCalls.ShouldBeEmpty();
            (await db.ExternalIssueRefs.SingleAsync(r => r.CardId == graph.Card.Id))
                .LastRevisionSynced.ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Default_mode_reopen_lands_in_backlog_not_active()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock, origin: ExternalIssueOrigin.ExternalImport);
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            graph.Card.CompletedAt = clock.GetUtcNow().UtcDateTime;
            graph.Card.StartedAt = clock.GetUtcNow().UtcDateTime.AddHours(-2);
            graph.Card.ExternalIssueRef!.LastKnownExternalState = "closed";
            await db.SaveChangesAsync();
            var startedAt = graph.Card.StartedAt;

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])]
            };
            var sut = NewSut(db, fake, clock);
            var reopenRun = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();
            reopenRun.ExternalReopens.ShouldBe(1);

            await using var verify = CreateContext();
            var card = await verify.Cards.Include(c => c.BoardColumn).SingleAsync(c => c.Id == graph.Card.Id);
            card.BoardColumnId.ShouldBe(graph.BacklogColumn.Id);
            card.Status.ShouldBe(CardStatus.Backlog);
            card.StartedAt.ShouldBe(startedAt);
            card.BoardColumn.IsActive.ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Import_column_active_reopen_lands_in_the_first_active_column()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(
                db, tempRoot, clock,
                origin: ExternalIssueOrigin.ExternalImport,
                importColumn: "active");
            graph.Card.BoardColumnId = graph.DoneColumn.Id;
            graph.Card.BoardColumn = graph.DoneColumn;
            graph.Card.Status = CardStatus.Done;
            graph.Card.CompletedAt = clock.GetUtcNow().UtcDateTime;
            graph.Card.ExternalIssueRef!.LastKnownExternalState = "closed";
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])]
            };
            var sut = NewSut(db, fake, clock);
            var reopenRun = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();
            reopenRun.ExternalReopens.ShouldBe(1);

            await using var verify = CreateContext();
            var card = await verify.Cards.Include(c => c.BoardColumn).SingleAsync(c => c.Id == graph.Card.Id);
            card.BoardColumnId.ShouldBe(graph.ActiveColumn.Id);
            card.BoardColumn.IsActive.ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Creates_are_gated_and_orphan_marker_relinks_without_duplicate()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedBoardAsync(db, tempRoot, clock, syncOutCreate: false);
            // Unlinked card after activation
            var card = NewCard(graph, clock.GetUtcNow().UtcDateTime.AddMinutes(1));
            db.Cards.Add(card);
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues) { Candidates = [] };
            var sut = NewSut(db, fake, clock);
            var off = await sut.RunAsync(graph.Board.Id, CancellationToken.None);
            off.Boards.Single().Creates.ShouldBe(0);
            fake.CreateIssueCalls.Count.ShouldBe(0);

            // Enable creates + orphan re-link
            graph.Board.WorkflowDefinitions.Single().Content = WorkflowYaml(syncOutCreate: true);
            await db.SaveChangesAsync();

            var orphanBody = TrackerSyncMarkers.AppendCardMarkerFooter("desc", card.Id, card.Identifier, null);
            fake.Candidates =
            [
                Issue("acme/app#99", "open", card.Title, orphanBody, [])
            ];
            var linked = await sut.RunAsync(graph.Board.Id, CancellationToken.None);
            fake.CreateIssueCalls.Count.ShouldBe(0); // linked, never POSTed a new issue
            // Creates counter may be 0 when the read-sync orphan link fires first, or 1 from the
            // create-phase pre-check — either way the card must end with exactly one export ref.

            await using var verify = CreateContext();
            var refRow = await verify.ExternalIssueRefs.SingleAsync(r => r.CardId == card.Id);
            refRow.ExternalId.ShouldBe("acme/app#99");
            refRow.Origin.ShouldBe(ExternalIssueOrigin.AntiphonExport);
            (await verify.Cards.CountAsync(c => c.BoardId == graph.Board.Id)).ShouldBe(1);
            _ = linked;
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Creates_only_cards_after_watermark_when_sync_out_create_on()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var activatedAt = clock.GetUtcNow().UtcDateTime;
            var graph = await SeedBoardAsync(db, tempRoot, clock, syncOutCreate: true, trackerActivatedAt: activatedAt);
            var legacy = NewCard(graph, activatedAt.AddHours(-1));
            var fresh = NewCard(graph, activatedAt.AddMinutes(5));
            db.Cards.AddRange(legacy, fresh);
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues) { Candidates = [] };
            var sut = NewSut(db, fake, clock);
            var result = await sut.RunAsync(graph.Board.Id, CancellationToken.None);
            result.Boards.Single().Creates.ShouldBe(1);
            fake.CreateIssueCalls.Count.ShouldBe(1);
            fake.CreateIssueCalls[0].Title.ShouldBe(fresh.Title);
            fake.CreateIssueCalls[0].Labels.ShouldNotContain(l => l.StartsWith("priority:"));
            // CARD-0171
            result.Boards.Single().Changes
                .Single(c => c.Kind == TrackerSyncChangeKind.Created)
                .CardIdentifier.ShouldBe(fresh.Identifier);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Export_create_writes_the_importance_name_as_the_priority_label()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var activatedAt = clock.GetUtcNow().UtcDateTime;
            var graph = await SeedBoardAsync(db, tempRoot, clock, syncOutCreate: true, trackerActivatedAt: activatedAt);
            var fresh = NewCard(graph, activatedAt.AddMinutes(5));
            fresh.Importance = CardImportance.Critical;
            db.Cards.Add(fresh);
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues) { Candidates = [] };
            var sut = NewSut(db, fake, clock);
            await sut.RunAsync(graph.Board.Id, CancellationToken.None);

            fake.CreateIssueCalls.ShouldHaveSingleItem();
            fake.CreateIssueCalls[0].Labels.ShouldContain("priority:critical");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Import_origin_Human_High_adds_priority_high()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.Importance = CardImportance.High;
            graph.Card.ImportanceProvenance = CardImportanceProvenance.Human;
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", ["status:backlog"])]
            };
            var sut = NewSut(db, fake, clock);
            await sut.RunAsync(graph.Board.Id, CancellationToken.None);

            fake.AddLabelCalls.ShouldBe(1);
            fake.Candidates.Single().Labels.ShouldContain("priority:high");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Import_origin_Auto_does_not_write_a_priority_label()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.Importance = CardImportance.High;
            graph.Card.ImportanceProvenance = CardImportanceProvenance.Auto;
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", ["status:backlog"])]
            };
            var sut = NewSut(db, fake, clock);
            await sut.RunAsync(graph.Board.Id, CancellationToken.None);

            fake.AddLabelCalls.ShouldBe(0);
            fake.Candidates.Single().Labels.ShouldNotContain(l => l.StartsWith("priority:", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public void PriorityLabel_emits_the_importance_name_and_omits_normal()
    {
        TrackerSyncMarkers.PriorityLabel(CardImportance.Critical).ShouldBe("priority:critical");
        TrackerSyncMarkers.PriorityLabel(CardImportance.High).ShouldBe("priority:high");
        TrackerSyncMarkers.PriorityLabel(CardImportance.Low).ShouldBe("priority:low");
        TrackerSyncMarkers.PriorityLabel(CardImportance.Normal).ShouldBeNull();
    }

    [Test]
    public async Task Changes_itemise_comments_and_labels_and_a_steady_state_run_records_none()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            db.CardComments.Add(new CardComment
            {
                Id = Guid.NewGuid(),
                CardId = graph.Card.Id,
                Body = "From Antiphon",
                Author = "operator",
                Origin = CardCommentOrigin.Antiphon,
                CreatedAt = clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                // No status label yet -> the label arm writes exactly once for this issue.
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", [])],
                CommentsSince =
                [
                    new TrackedIssueComment(
                        "101", "acme/app#1", "alice", "Hello from GH",
                        "https://github.test/acme/app/issues/1#issuecomment-101",
                        clock.GetUtcNow().UtcDateTime.AddMinutes(-1),
                        clock.GetUtcNow().UtcDateTime.AddMinutes(-1))
                ]
            };

            var sut = NewSut(db, fake, clock);
            var first = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();

            first.Changes.Count(c => c.Kind == TrackerSyncChangeKind.CommentIn).ShouldBe(1);
            first.Changes.Count(c => c.Kind == TrackerSyncChangeKind.CommentOut).ShouldBe(1);
            // One LabelsChanged per ISSUE that had any label write, while the counter counts writes.
            first.Changes.Count(c => c.Kind == TrackerSyncChangeKind.LabelsChanged).ShouldBe(1);
            first.LabelsChanged.ShouldBeGreaterThanOrEqualTo(1);
            var labelChange = first.Changes.Single(c => c.Kind == TrackerSyncChangeKind.LabelsChanged);
            labelChange.Added.ShouldBe(["status:backlog"]);
            labelChange.Removed.ShouldBeEmpty();
            // Every change names the card and its tracker key. The identifier is read back off the
            // card AFTER the run: the read-side upsert rewrites an import-origin card's identifier
            // to the tracker's own key, so the pre-run value is not what the message would carry.
            var identifier = (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == graph.Card.Id)).Identifier;
            first.Changes.ShouldAllBe(c => c.CardIdentifier == identifier);
            first.Changes.ShouldAllBe(c => c.ExternalKey == "#1");
            first.Changes.ShouldAllBe(c => c.Url != null && c.Url.StartsWith("https://github.test/"));

            // Steady state: IssuesPulled is not a change, so the gate stays shut.
            fake.CommentsSince = [];
            var second = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();
            second.Changes.ShouldBeEmpty();
            second.ExternalReopens.ShouldBe(0);
            second.IssuesPulled.ShouldBeGreaterThanOrEqualTo(1);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Export_label_replace_attaches_the_sorted_set_delta_after_the_write()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock, origin: ExternalIssueOrigin.AntiphonExport);
            graph.Card.LabelsJson = """["backend"]""";
            graph.Card.ExternalIssueRef!.LastOutboundSyncedAt = clock.GetUtcNow().UtcDateTime;
            graph.Card.ExternalIssueRef.Url = "https://github.test/acme/app/1";
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", ["frontend", "status:active"])]
            };
            var sut = NewSut(db, fake, clock);
            var first = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();

            fake.ReplaceLabelCalls.ShouldBe(1);
            var delta = first.Changes.Single(c => c.Kind == TrackerSyncChangeKind.LabelsChanged);
            delta.Added.ShouldBe(["backend", "status:backlog"]);
            delta.Removed.ShouldBe(["frontend", "status:active"]);

            fake.ClearWriteCounters();
            var second = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();
            second.Changes.ShouldNotContain(c => c.Kind == TrackerSyncChangeKind.LabelsChanged);
            fake.ReplaceLabelCalls.ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Import_label_delta_records_stale_removal_and_status_add()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", ["status:done", "backend"])]
            };
            var sut = NewSut(db, fake, clock);
            var result = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();

            fake.RemoveLabelCalls.ShouldBe(1);
            fake.AddLabelCalls.ShouldBe(1);
            var delta = result.Changes.Single(c => c.Kind == TrackerSyncChangeKind.LabelsChanged);
            delta.Added.ShouldBe(["status:backlog"]);
            delta.Removed.ShouldBe(["status:done"]);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Case_insensitive_label_steady_state_records_no_delta()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock, origin: ExternalIssueOrigin.AntiphonExport);
            graph.Card.LabelsJson = """["backend"]""";
            graph.Card.ExternalIssueRef!.LastOutboundSyncedAt = clock.GetUtcNow().UtcDateTime;
            graph.Card.ExternalIssueRef.Url = "https://github.test/acme/app/1";
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", ["Backend", "STATUS:BACKLOG"])]
            };
            var sut = NewSut(db, fake, clock);
            var result = (await sut.RunAsync(graph.Board.Id, CancellationToken.None)).Boards.Single();

            result.Changes.ShouldNotContain(c => c.Kind == TrackerSyncChangeKind.LabelsChanged);
            result.LabelsChanged.ShouldBe(0);
            fake.ReplaceLabelCalls.ShouldBe(0);
            fake.AddLabelCalls.ShouldBe(0);
            fake.RemoveLabelCalls.ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Import_with_a_differing_label_set_keeps_the_card_diagnosis_labels()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
            graph.Card.LabelsJson = BoardService.SerializeLabels(
                ["bug", "complexity:medium", "ui:no"]);
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", ["frontend"])]
            };
            var sut = NewSut(db, fake, clock);
            await sut.RunAsync(graph.Board.Id, CancellationToken.None);

            var labels = BoardService.ParseLabels(
                (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == graph.Card.Id)).LabelsJson);
            labels.ShouldContain("frontend");
            labels.ShouldContain("complexity:medium");
            labels.ShouldContain("ui:no");
            labels.ShouldNotContain("bug");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Export_label_replace_never_sends_diagnosis_labels()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var graph = await SeedLinkedBoardAsync(db, tempRoot, clock, origin: ExternalIssueOrigin.AntiphonExport);
            graph.Card.LabelsJson = BoardService.SerializeLabels(
                ["backend", "complexity:easy", "ui:yes"]);
            graph.Card.ExternalIssueRef!.LastOutboundSyncedAt = clock.GetUtcNow().UtcDateTime;
            graph.Card.ExternalIssueRef.Url = "https://github.test/acme/app/1";
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues)
            {
                Candidates = [Issue("acme/app#1", "open", "Title", "Body", ["frontend", "status:active"])]
            };
            var sut = NewSut(db, fake, clock);
            await sut.RunAsync(graph.Board.Id, CancellationToken.None);

            fake.ReplaceLabelCalls.ShouldBe(1);
            var sent = fake.Candidates.Single().Labels;
            sent.ShouldContain("backend");
            sent.ShouldContain("status:backlog");
            sent.ShouldNotContain(l => l.StartsWith("complexity:", StringComparison.OrdinalIgnoreCase));
            sent.ShouldNotContain(l => l.StartsWith("ui:", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Export_create_never_sends_diagnosis_labels()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));
        try
        {
            var activatedAt = clock.GetUtcNow().UtcDateTime;
            var graph = await SeedBoardAsync(db, tempRoot, clock, syncOutCreate: true, trackerActivatedAt: activatedAt);
            var fresh = NewCard(graph, activatedAt.AddMinutes(5));
            fresh.LabelsJson = BoardService.SerializeLabels(["backend", "complexity:hard", "ui:no"]);
            db.Cards.Add(fresh);
            await db.SaveChangesAsync();

            var fake = new FakeBidirectionalTracker(TrackerKind.GitHubIssues) { Candidates = [] };
            var sut = NewSut(db, fake, clock);
            await sut.RunAsync(graph.Board.Id, CancellationToken.None);

            var sent = fake.CreateIssueCalls.ShouldHaveSingleItem().Labels;
            sent.ShouldContain("backend");
            sent.ShouldNotContain(l => l.StartsWith("complexity:", StringComparison.OrdinalIgnoreCase));
            sent.ShouldNotContain(l => l.StartsWith("ui:", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    [Test]
    public async Task Content_edit_outbound_echo_and_repeated_pull_create_no_discussion_rows()
    {
        var tempRoot = NewTempRoot();
        var clock = EchoClock();
        try
        {
            var (graph, _) = await SeedEchoGraphAsync(tempRoot, clock, pendingEdit: true);
            var fake = EchoTracker();
            fake.EchoPostedComments = true;
            var first = await RunEchoPassAsync(graph, fake, clock);
            AssertContentEditPost(graph, fake);
            AssertEchoResult(graph, fake, first, commentsOut: 1);
            var postedAt = clock.GetUtcNow().UtcDateTime;
            await AssertEchoCursorAsync(graph, clock, 1, postedAt);
            (await ReadCommentSnapshotsAsync(graph.Card.Id)).ShouldBeEmpty();
            var echo = fake.CommentsSince.Single();
            echo.Body.ShouldBe(fake.PostCommentCalls.Single().Body);

            for (var pass = 0; pass < 2; pass++)
            {
                fake.ClearWriteCounters();
                fake.CommentsSince.ShouldBe(new[] { echo });
                AssertEchoResult(graph, fake, await RunEchoPassAsync(graph, fake, clock));
                (await ReadCommentSnapshotsAsync(graph.Card.Id)).ShouldBeEmpty();
                await AssertEchoCursorAsync(graph, clock, 1, postedAt);
            }
        }
        finally { await CleanupAsync(tempRoot); }
    }

    [Test]
    public async Task Legacy_content_edit_revision_marker_is_ignored_on_repeated_pulls()
    {
        var tempRoot = NewTempRoot();
        var clock = EchoClock();
        try
        {
            var (graph, revision) = await SeedEchoGraphAsync(tempRoot, clock);
            var fake = EchoTracker();
            var inbound = EchoComments(clock, "alice",
                TrackerSyncMarkers.AppendCommentMarker("Historical content edit", revision.Id));
            fake.CommentsSince = inbound;
            var outboundAt = clock.GetUtcNow().UtcDateTime;
            for (var pass = 0; pass < 3; pass++)
            {
                if (pass == 2)
                {
                    await using var update = CreateContext();
                    var issueRef = await update.ExternalIssueRefs.SingleAsync(r => r.CardId == graph.Card.Id);
                    issueRef.Origin = ExternalIssueOrigin.AntiphonExport;
                    issueRef.LastOutboundSyncedAt = outboundAt = clock.GetUtcNow().UtcDateTime;
                    await update.SaveChangesAsync();
                }
                fake.ClearWriteCounters();
                fake.CommentsSince.ShouldBe(inbound);
                AssertEchoResult(graph, fake, await RunEchoPassAsync(graph, fake, clock));
                (await ReadCommentSnapshotsAsync(graph.Card.Id)).ShouldBeEmpty();
                await AssertEchoCursorAsync(graph, clock, 1, outboundAt);
            }
        }
        finally { await CleanupAsync(tempRoot); }
    }

    [Test]
    public async Task Legacy_marker_requires_same_card_and_ContentEdit_kind()
    {
        var tempRoot = NewTempRoot();
        var clock = EchoClock();
        try
        {
            var (graph, _) = await SeedEchoGraphAsync(tempRoot, clock);
            Guid foreignCardId;
            Guid[] markerIds;
            await using (var seed = CreateContext())
            {
                var card = await seed.Cards.SingleAsync(c => c.Id == graph.Card.Id);
                var move = AddEchoRevision(seed, card, clock, CardRevisionKind.Move);
                var reopen = AddEchoRevision(seed, card, clock, CardRevisionKind.Reopen);
                var foreign = NewCard(graph, clock.GetUtcNow().UtcDateTime);
                foreign.Identifier = graph.Card.Identifier + "-foreign";
                foreign.Board = null!;
                foreign.BoardColumn = null!;
                seed.Cards.Add(foreign);
                foreignCardId = foreign.Id;
                var foreignEdit = AddEchoRevision(seed, foreign, clock, CardRevisionKind.ContentEdit);
                var unknown = Guid.NewGuid();
                (await seed.CardRevisions.AnyAsync(r => r.Id == unknown)).ShouldBeFalse();
                (await seed.CardComments.AnyAsync(c => c.Id == unknown)).ShouldBeFalse();
                // The ID-predicate control must name the unknown row before any other missing row.
                markerIds = [unknown, foreignEdit.Id, move.Id, reopen.Id];
                var issueRef = await seed.ExternalIssueRefs.SingleAsync(r => r.CardId == card.Id);
                issueRef.LastRevisionSynced = card.RevisionCount;
                await seed.SaveChangesAsync();
            }
            var fake = EchoTracker();
            var inbound = EchoComments(clock, "alice", markerIds.Select(id =>
                TrackerSyncMarkers.AppendCommentMarker("Visible revision reference", id)).ToArray());
            fake.CommentsSince = inbound;
            var first = await RunEchoPassAsync(graph, fake, clock);
            var original = await AssertImportedCommentsAsync(graph, inbound);
            (await ReadCommentSnapshotsAsync(foreignCardId)).ShouldBeEmpty();
            AssertEchoResult(graph, fake, first, commentsIn: 4);
            fake.ClearWriteCounters();
            fake.CommentsSince.ShouldBe(inbound);
            AssertEchoResult(graph, fake, await RunEchoPassAsync(graph, fake, clock));
            (await AssertImportedCommentsAsync(graph, inbound)).ShouldBe(original);
            (await ReadCommentSnapshotsAsync(foreignCardId)).ShouldBeEmpty();
        }
        finally { await CleanupAsync(tempRoot); }
    }

    [Test]
    public async Task Same_author_human_comment_imports_beside_content_edit_echo()
    {
        var tempRoot = NewTempRoot();
        var clock = EchoClock();
        try
        {
            var (graph, revision) = await SeedEchoGraphAsync(tempRoot, clock, pendingEdit: true);
            var fake = EchoTracker();
            var first = await RunEchoPassAsync(graph, fake, clock);
            AssertContentEditPost(graph, fake);
            AssertEchoResult(graph, fake, first, commentsOut: 1);
            await AssertEchoCursorAsync(graph, clock, 1, clock.GetUtcNow().UtcDateTime);
            (await ReadCommentSnapshotsAsync(graph.Card.Id)).ShouldBeEmpty();
            var inbound = EchoComments(EchoClock(), "michal-ciechan",
                fake.PostCommentCalls.Single().Body,
                TrackerSyncMarkers.AppendCommentMarker("Historical content edit", revision.Id),
                "Antiphon content edit by michal-ciechan: I wrote this comment myself.");
            fake.CommentsSince = inbound;
            fake.ClearWriteCounters();
            AssertEchoResult(graph, fake, await RunEchoPassAsync(graph, fake, clock), commentsIn: 1);
            var original = await AssertImportedCommentsAsync(graph, [inbound[2]]);
            fake.ClearWriteCounters();
            fake.CommentsSince.ShouldBe(inbound);
            AssertEchoResult(graph, fake, await RunEchoPassAsync(graph, fake, clock));
            (await AssertImportedCommentsAsync(graph, [inbound[2]])).ShouldBe(original);
        }
        finally { await CleanupAsync(tempRoot); }
    }

    [Test]
    public async Task Unrecognized_marker_shapes_remain_visible_and_deduplicate()
    {
        var tempRoot = NewTempRoot();
        var clock = EchoClock();
        try
        {
            var (graph, revision) = await SeedEchoGraphAsync(tempRoot, clock);
            var otherCardId = Guid.NewGuid();
            var revId = revision.Id;
            var shortId = revId.ToString("N")[..31];
            var badHex = shortId + "z";
            (string Name, string Suffix, bool System, Guid? ParsedId)[] matrix =
            [
                ("wrong-card-system", $"<!-- antiphon:system-comment={otherCardId:N} -->", true, otherCardId),
                ("nonhex-revision", $"<!-- antiphon:comment={badHex} -->", false, null),
                ("hyphenated-revision", $"<!-- antiphon:comment={revId:D} -->", false, null),
                ("short-revision", $"<!-- antiphon:comment={shortId} -->", false, null),
                ("unclosed-revision", $"<!-- antiphon:comment={revId:N}", false, null),
                ("nontrailing-revision", $"<!-- antiphon:comment={revId:N} -->\nHuman follow-up.", false, null),
                ("nontrailing-system", $"<!-- antiphon:system-comment={graph.Card.Id:N} -->\nHuman follow-up.", true, null),
                ("valid-legacy-control", $"<!-- antiphon:comment={revId.ToString("N").ToUpperInvariant()} -->\r\n \t", false, revId),
                ("valid-system-control", $"<!-- antiphon:system-comment={graph.Card.Id:N} -->\r\n \t", true, graph.Card.Id)
            ];
            var bodies = matrix.Select(row => "Visible text\n\n" + row.Suffix).ToArray();
            for (var i = 0; i < matrix.Length; i++)
            {
                Guid parsedId;
                var parsed = matrix[i].System
                    ? TrackerSyncMarkers.TryReadTrailingSystemCommentMarker(bodies[i], out parsedId)
                    : TrackerSyncMarkers.TryReadTrailingCommentMarker(bodies[i], out parsedId);
                parsed.ShouldBe(matrix[i].ParsedId.HasValue, matrix[i].Name);
                if (parsed)
                    parsedId.ShouldBe(matrix[i].ParsedId!.Value, matrix[i].Name);
            }
            var inbound = EchoComments(clock, "alice", bodies);
            var fake = EchoTracker();
            fake.CommentsSince = inbound;
            var first = await RunEchoPassAsync(graph, fake, clock);
            var original = await AssertImportedCommentsAsync(graph, inbound.Take(7).ToArray());
            AssertEchoResult(graph, fake, first, commentsIn: 7);
            fake.ClearWriteCounters();
            fake.CommentsSince.ShouldBe(inbound);
            AssertEchoResult(graph, fake, await RunEchoPassAsync(graph, fake, clock));
            (await AssertImportedCommentsAsync(graph, inbound.Take(7).ToArray())).ShouldBe(original);
        }
        finally { await CleanupAsync(tempRoot); }
    }

    [Test]
    public async Task Existing_external_revision_echo_is_left_unchanged()
    {
        var tempRoot = NewTempRoot();
        var clock = EchoClock();
        try
        {
            var (graph, revision) = await SeedEchoGraphAsync(tempRoot, clock);
            var inbound = EchoComments(clock, "michal-ciechan",
                TrackerSyncMarkers.AppendCommentMarker("Historical content edit", revision.Id),
                "An unrelated human comment.");
            await using (var seed = CreateContext())
            {
                foreach (var comment in inbound)
                    seed.CardComments.Add(new CardComment
                    {
                        Id = Guid.NewGuid(), CardId = graph.Card.Id, Body = comment.Body,
                        Author = comment.Author, Origin = CardCommentOrigin.External,
                        ExternalCommentId = comment.ExternalCommentId, ExternalUrl = comment.Url,
                        CreatedAt = comment.CreatedAt, SyncedAt = null
                    });
                await seed.SaveChangesAsync();
            }
            var original = await AssertImportedCommentsAsync(graph, inbound);
            original.ShouldAllBe(c => c.Id != revision.Id);
            var fake = EchoTracker();
            fake.CommentsSince = [inbound[0]];
            for (var pass = 0; pass < 2; pass++)
            {
                fake.ClearWriteCounters();
                fake.CommentsSince.ShouldBe(new[] { inbound[0] });
                AssertEchoResult(graph, fake, await RunEchoPassAsync(graph, fake, clock));
                (await ReadCommentSnapshotsAsync(graph.Card.Id)).ShouldBe(original);
                await using var verify = CreateContext();
                (await verify.CardRevisions.AnyAsync(r => r.Id == revision.Id
                    && r.CardId == graph.Card.Id && r.Kind == CardRevisionKind.ContentEdit)).ShouldBeTrue();
            }
        }
        finally { await CleanupAsync(tempRoot); }
    }

    [Test]
    public async Task Known_discussion_marker_repairs_missing_link_before_legacy_lookup()
    {
        var tempRoot = NewTempRoot();
        var clock = EchoClock();
        try
        {
            var (graph, revision) = await SeedEchoGraphAsync(tempRoot, clock);
            await using (var seed = CreateContext())
            {
                seed.CardComments.Add(new CardComment
                {
                    Id = revision.Id, CardId = graph.Card.Id, Body = "Original discussion",
                    Author = "operator", Origin = CardCommentOrigin.Antiphon,
                    CreatedAt = clock.GetUtcNow().UtcDateTime.AddMinutes(-2),
                    SyncedAt = clock.GetUtcNow().UtcDateTime
                });
                await seed.SaveChangesAsync();
            }
            var original = (await ReadCommentSnapshotsAsync(graph.Card.Id)).Single();
            var inbound = EchoComments(clock, "alice",
                TrackerSyncMarkers.AppendCommentMarker("Remote discussion echo", revision.Id));
            var fake = EchoTracker();
            fake.CommentsSince = inbound;
            for (var pass = 0; pass < 2; pass++)
            {
                fake.ClearWriteCounters();
                fake.CommentsSince.ShouldBe(inbound);
                AssertEchoResult(graph, fake, await RunEchoPassAsync(graph, fake, clock));
                var stored = (await ReadCommentSnapshotsAsync(graph.Card.Id)).Single();
                stored.ExternalCommentId.ShouldBe(inbound[0].ExternalCommentId);
                stored.ExternalUrl.ShouldBe(inbound[0].Url);
                stored.ShouldBe(original with
                {
                    ExternalCommentId = inbound[0].ExternalCommentId, ExternalUrl = inbound[0].Url
                });
            }
        }
        finally { await CleanupAsync(tempRoot); }
    }

    private static FakeTimeProvider EchoClock() =>
        new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));

    private static FakeBidirectionalTracker EchoTracker() => new(TrackerKind.GitHubIssues)
    {
        Candidates = [Issue("acme/app#1", "open", "Title", "Body", ["status:backlog"])]
    };

    private static async Task<(Graph Graph, CardRevision Revision)> SeedEchoGraphAsync(
        string tempRoot, FakeTimeProvider clock, bool pendingEdit = false)
    {
        await using var db = CreateContext();
        var graph = await SeedLinkedBoardAsync(db, tempRoot, clock);
        graph.Card.Title = "Title";
        graph.Card.Description = "Body";
        graph.Card.ImportanceProvenance = CardImportanceProvenance.Auto;
        var revision = AddEchoRevision(db, graph.Card, clock, CardRevisionKind.ContentEdit);
        graph.Card.ExternalIssueRef!.LastRevisionSynced = pendingEdit ? 0 : graph.Card.RevisionCount;
        graph.Card.ExternalIssueRef.LastOutboundSyncedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        return (graph, revision);
    }

    private static CardRevision AddEchoRevision(
        AppDbContext db, Card card, FakeTimeProvider clock, CardRevisionKind kind)
    {
        var revision = new CardRevision
        {
            Id = Guid.NewGuid(), CardId = card.Id, Card = card,
            RevisionNumber = ++card.RevisionCount, Kind = kind,
            EditedBy = "operator", Reason = "clarify acceptance",
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            Title = kind == CardRevisionKind.ContentEdit ? "Previous title" : null,
            Description = kind == CardRevisionKind.ContentEdit ? "Previous body" : null
        };
        db.CardRevisions.Add(revision);
        return revision;
    }

    private static TrackedIssueComment[] EchoComments(FakeTimeProvider clock, string author, params string[] bodies)
    {
        var ids = new HashSet<string>();
        return bodies.Select(body =>
        {
            string id;
            do { id = Random.Shared.NextInt64(1_000_000_000_000, long.MaxValue).ToString(); }
            while (!ids.Add(id));
            var createdAt = clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
            return new TrackedIssueComment(id, "acme/app#1", author, body,
                $"https://github.test/acme/app/issues/1#issuecomment-{id}", createdAt, createdAt);
        }).ToArray();
    }

    // Every pass owns a new context/SUT; replay cannot depend on tracked cursor or revision state.
    private static async Task<TrackerSyncBoardResult> RunEchoPassAsync(
        Graph graph, FakeBidirectionalTracker fake, FakeTimeProvider clock)
    {
        clock.Advance(TimeSpan.FromMinutes(1));
        await using var db = CreateContext();
        var run = await NewSut(db, fake, clock).RunAsync(graph.Board.Id, CancellationToken.None);
        run.ConcurrentRunSkipped.ShouldBeFalse();
        run.Boards.ShouldHaveSingleItem();
        var result = run.Boards.Single();
        result.BoardId.ShouldBe(graph.Board.Id);
        result.Error.ShouldBeNull();
        result.Skips.ShouldBeEmpty();
        result.IssuesPulled.ShouldBe(1);
        await using var verify = CreateContext();
        (await verify.Boards.SingleAsync(b => b.Id == graph.Board.Id))
            .TrackerCommentsPulledAt.ShouldBe(clock.GetUtcNow().UtcDateTime);
        return result;
    }

    private static void AssertEchoResult(Graph graph, FakeBidirectionalTracker fake,
        TrackerSyncBoardResult result, int commentsIn = 0, int commentsOut = 0)
    {
        result.CommentsIn.ShouldBe(commentsIn);
        result.CommentsOut.ShouldBe(commentsOut);
        result.LabelsChanged.ShouldBe(0);
        result.StateChanges.ShouldBe(0);
        result.Creates.ShouldBe(0);
        result.ExternalReopens.ShouldBe(0);
        result.Changes.Select(c => c.Kind).ShouldBe(
            Enumerable.Repeat(TrackerSyncChangeKind.CommentIn, commentsIn)
                .Concat(Enumerable.Repeat(TrackerSyncChangeKind.CommentOut, commentsOut)));
        foreach (var change in result.Changes)
        {
            change.CardIdentifier.ShouldBe(graph.Card.Identifier);
            change.ExternalKey.ShouldBe("#1");
        }
        fake.WriteCallCount.ShouldBe(commentsOut);
    }

    private static void AssertContentEditPost(Graph graph, FakeBidirectionalTracker fake)
    {
        var post = fake.PostCommentCalls.ShouldHaveSingleItem();
        post.ExternalId.ShouldBe("acme/app#1");
        post.Body.ShouldBe($"Antiphon content edit by operator: clarify acceptance\n\n"
            + $"**Title:** {graph.Card.Title}\n\n{graph.Card.Description}\n\n"
            + "_The issue body remains authoritative on this import-origin link._\n\n"
            + $"<!-- antiphon:system-comment={graph.Card.Id:N} -->");
        TrackerSyncMarkers.TryReadTrailingSystemCommentMarker(post.Body, out var cardId).ShouldBeTrue();
        cardId.ShouldBe(graph.Card.Id);
        TrackerSyncMarkers.TryReadTrailingCommentMarker(post.Body, out _).ShouldBeFalse();
    }

    private static async Task AssertEchoCursorAsync(
        Graph graph, FakeTimeProvider clock, int revisionNumber, DateTime outboundAt)
    {
        await using var verify = CreateContext();
        var issueRef = await verify.ExternalIssueRefs.SingleAsync(r => r.CardId == graph.Card.Id);
        issueRef.LastRevisionSynced.ShouldBe(revisionNumber);
        issueRef.LastOutboundSyncedAt.ShouldBe(outboundAt);
        (await verify.Boards.SingleAsync(b => b.Id == graph.Board.Id))
            .TrackerCommentsPulledAt.ShouldBe(clock.GetUtcNow().UtcDateTime);
    }

    private sealed record CommentSnapshot(Guid Id, Guid CardId, CardCommentOrigin Origin,
        string Body, string? Author, string? ExternalCommentId, string? ExternalUrl,
        DateTime CreatedAt, DateTime? SyncedAt);

    private static async Task<CommentSnapshot[]> ReadCommentSnapshotsAsync(Guid cardId)
    {
        await using var verify = CreateContext();
        return await verify.CardComments.Where(c => c.CardId == cardId).OrderBy(c => c.Id)
            .Select(c => new CommentSnapshot(c.Id, c.CardId, c.Origin, c.Body, c.Author,
                c.ExternalCommentId, c.ExternalUrl, c.CreatedAt, c.SyncedAt)).ToArrayAsync();
    }

    private static async Task<CommentSnapshot[]> AssertImportedCommentsAsync(
        Graph graph, IReadOnlyList<TrackedIssueComment> expected)
    {
        var rows = await ReadCommentSnapshotsAsync(graph.Card.Id);
        // Check individual IDs before the aggregate so card/kind/unknown-identity mutants name the lost row.
        foreach (var comment in expected)
        {
            var matches = rows.Where(c => c.ExternalCommentId == comment.ExternalCommentId).ToArray();
            matches.Length.ShouldBe(1, $"Missing or duplicate remote comment: {comment.Body}");
            var row = matches.Single();
            row.Id.ShouldNotBe(Guid.Empty);
            row.ShouldBe(new CommentSnapshot(row.Id, graph.Card.Id, CardCommentOrigin.External,
                comment.Body, comment.Author, comment.ExternalCommentId, comment.Url, comment.CreatedAt, null));
        }
        rows.Length.ShouldBe(expected.Count);
        return rows;
    }

    private static TrackerBidirectionalSyncService NewSut(
        AppDbContext db,
        FakeBidirectionalTracker fake,
        TimeProvider clock)
    {
        var eventBus = new MockEventBus();
        var readSync = new ExternalTrackerSyncService(
            db, [fake], eventBus, NullLogger<ExternalTrackerSyncService>.Instance);
        var tokens = new TrackerTokenResolver(
            db, new ApiKeyStoreTests.FakeApiKeyProtector(), NullLogger<TrackerTokenResolver>.Instance);
        var push = new TrackerCardStatePushService(
            db, tokens, [fake], Options.Create(new TrackerSettings()),
            NullLogger<TrackerCardStatePushService>.Instance, clock);
        return new TrackerBidirectionalSyncService(
            db, readSync, tokens, [fake], eventBus,
            NullLogger<TrackerBidirectionalSyncService>.Instance, clock, push);
    }

    private static async Task<Graph> SeedLinkedBoardAsync(
        AppDbContext db,
        string tempRoot,
        FakeTimeProvider clock,
        ExternalIssueOrigin origin = ExternalIssueOrigin.ExternalImport,
        string? importColumn = null)
    {
        var graph = await SeedBoardAsync(db, tempRoot, clock, syncOutCreate: false, importColumn: importColumn);
        var card = NewCard(graph, clock.GetUtcNow().UtcDateTime);
        var issueRef = new ExternalIssueRef
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            TrackerKind = TrackerKind.GitHubIssues,
            ExternalId = "acme/app#1",
            ExternalKey = "#1",
            Url = "https://github.test/acme/app/issues/1",
            RawPayloadJson = "{}",
            LastSyncedAt = clock.GetUtcNow().UtcDateTime,
            Origin = origin,
            LastKnownExternalState = "open",
            LastRevisionSynced = 0,
            Card = card
        };
        card.ExternalIssueRef = issueRef;
        db.Cards.Add(card);
        db.ExternalIssueRefs.Add(issueRef);
        await db.SaveChangesAsync();
        return graph with { Card = card };
    }

    private static async Task<Graph> SeedBoardAsync(
        AppDbContext db,
        string tempRoot,
        FakeTimeProvider clock,
        bool syncOutCreate,
        DateTime? trackerActivatedAt = null,
        string? importColumn = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"BiSync Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath!);

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"BiSync Board {Guid.NewGuid():N}",
            TrackerKind = TrackerKind.GitHubIssues,
            TrackerActivatedAt = trackerActivatedAt ?? now,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        var backlog = Col(board, "backlog", "Backlog", 0, CardStatus.Backlog, active: false, terminal: false, now);
        var active = Col(board, "in_progress", "In Progress", 1, CardStatus.InProgress, active: true, terminal: false, now);
        var done = Col(board, "done", "Done", 2, CardStatus.Done, active: false, terminal: true, now);
        board.Columns.Add(backlog);
        board.Columns.Add(active);
        board.Columns.Add(done);

        board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Tracked",
            Content = WorkflowYaml(syncOutCreate, importColumn),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        });

        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return new Graph(project, board, backlog, active, done, Card: null!);
    }

    private static Card NewCard(Graph graph, DateTime createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            BoardId = graph.Board.Id,
            BoardColumnId = graph.BacklogColumn.Id,
            Identifier = $"CARD-{Random.Shared.Next(1000, 9999)}",
            Title = $"Card {Guid.NewGuid():N}"[..20],
            Description = "desc",
            Importance = CardImportance.Normal,
            LabelsJson = "[]",
            Status = CardStatus.Backlog,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            Board = graph.Board,
            BoardColumn = graph.BacklogColumn
        };

    private static BoardColumn Col(
        Board board, string key, string name, int order, CardStatus status,
        bool active, bool terminal, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = key,
            Name = name,
            ColumnOrder = order,
            CardStatus = status,
            IsActive = active,
            IsTerminal = terminal,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };

    private static string WorkflowYaml(bool syncOutCreate, string? importColumn = null) =>
        string.Join('\n',
            "---",
            "tracker:",
            "  kind: github_issues",
            "  repository: acme/app",
            "  active_states: [open]",
            $"  sync_out_create: {syncOutCreate.ToString().ToLowerInvariant()}",
            importColumn is null ? "" : $"  import_column: {importColumn}",
            "---",
            "Work on {{ issue.identifier }}.");

    private static TrackedIssue Issue(
        string externalId, string state, string title, string body, IReadOnlyList<string> labels) =>
        new(externalId, "#" + externalId.Split('#')[^1], title, body, state, 0, labels, [],
            $"https://github.test/{externalId.Replace('#', '/')}", "{}");

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-bisync-{Guid.NewGuid():N}");

    private static async Task CleanupAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
            return;

        var boardIds = await db.Boards.Where(b => projectIds.Contains(b.ProjectId)).Select(b => b.Id).ToListAsync();
        var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
        await db.CardComments.Where(c => cardIds.Contains(c.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { /* best effort */ }
    }

    private sealed record Graph(
        Project Project,
        Board Board,
        BoardColumn BacklogColumn,
        BoardColumn ActiveColumn,
        BoardColumn DoneColumn,
        Card Card);
}
