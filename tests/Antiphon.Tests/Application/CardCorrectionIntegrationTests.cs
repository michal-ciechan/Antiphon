using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0019: the card SURFACE is correctable, the card RECORD is append-only. These tests cover
/// the ceilings that used to answer 500, the revision history behind every correction and move,
/// and archive-instead-of-delete.
/// </summary>
/// <remarks>
/// Every assertion is scoped to rows this test created — one Postgres testcontainer is shared by
/// the whole assembly and other suites are writing rows throughout, so an unscoped count would
/// also be asserting "nobody else has data right now".
/// </remarks>
[Category("Integration")]
[NotInParallel("CardCorrection")]
public class CardCorrectionIntegrationTests
{
    // A live 500, not a hypothetical: Cards.Description was varchar(4000) with no application
    // check, so an over-long description reached Postgres, came back as 22001 "value too long"
    // inside a DbUpdateException — which is not an HttpException — and the middleware answered a
    // raw 500 naming nothing.
    [Test]
    public async Task Create_answers_a_validation_error_for_an_over_ceiling_description_instead_of_a_500()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Ceiling board"), CancellationToken.None);

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.CreateAsync(
                    board.Id,
                    new CreateCardRequest(null, "Too long", new string('x', CardService.MaxDescriptionLength + 1)),
                    CancellationToken.None));

            var message = ex.Errors[nameof(CreateCardRequest.Description)].Single();
            message.ShouldContain("20,000");
            message.ShouldContain("20,001");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // Pins that the varchar(4000) -> text migration actually ran: this body is five times the old
    // column width and must round-trip whole.
    [Test]
    public async Task A_description_just_under_the_ceiling_round_trips_whole()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Wide board"), CancellationToken.None);
            var description = new string('d', CardService.MaxDescriptionLength - 1);

            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Long but legal", description),
                CancellationToken.None);

            card.Description.Length.ShouldBe(CardService.MaxDescriptionLength - 1);
            await using var verify = CreateContext();
            var stored = await verify.Cards.SingleAsync(c => c.Id == card.Id);
            stored.Description.ShouldBe(description);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Create_answers_a_validation_error_for_a_title_past_the_column_width()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Title board"), CancellationToken.None);

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.CreateAsync(
                    board.Id,
                    new CreateCardRequest(null, new string('t', CardService.MaxTitleLength + 1)),
                    CancellationToken.None));

            ex.Errors[nameof(CreateCardRequest.Title)].Single().ShouldContain("300");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // TerminalReason was varchar(1000) and overflowed with a raw 500 twice while closing CARD-0042
    // and CARD-0046; a review verdict had to be hand-trimmed to exactly 1000 characters to fit.
    [Test]
    public async Task A_terminal_move_stores_a_reason_far_longer_than_the_old_thousand_character_column()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Verdict board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Close with a real verdict"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            var verdict = new string('v', 3_500);

            var moved = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, verdict),
                CancellationToken.None);

            moved.Card.TerminalReason.ShouldBe(verdict);
            await using var verify = CreateContext();
            var stored = await verify.Cards.SingleAsync(c => c.Id == card.Id);
            stored.TerminalReason.ShouldBe(verdict);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task A_move_reason_past_the_ceiling_is_a_validation_error_not_a_500()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Long reason board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Reason too long"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.MoveAsync(
                    card.Id,
                    new MoveCardRequest(
                        doneColumn.Id, card.ConcurrencyToken, new string('r', CardService.MaxReasonLength + 1)),
                    CancellationToken.None));

            ex.Errors[nameof(MoveCardRequest.Reason)].Single().ShouldContain("4,000");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // Before CARD-0019 a non-terminal move's reason was accepted and then dropped: TerminalReason
    // was the only place a reason could go, and it means something else. This is the fix.
    [Test]
    public async Task A_non_terminal_move_persists_its_reason_as_a_move_revision()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Move history board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Pulled straight into review"), CancellationToken.None);
            var backlogColumn = board.Columns.Single(c => c.StateKey == "backlog");
            var reviewColumn = board.Columns.Single(c => c.StateKey == "review");

            var moved = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(reviewColumn.Id, card.ConcurrencyToken, "The work already existed; skipping ahead."),
                CancellationToken.None);

            moved.Card.TerminalReason.ShouldBeNull();
            await using var verify = CreateContext();
            var revision = await verify.CardRevisions.SingleAsync(r => r.CardId == card.Id);
            revision.Kind.ShouldBe(CardRevisionKind.Move);
            revision.RevisionNumber.ShouldBe(1);
            revision.FromColumnId.ShouldBe(backlogColumn.Id);
            revision.ToColumnId.ShouldBe(reviewColumn.Id);
            revision.FromStatus.ShouldBe(CardStatus.Backlog);
            revision.ToStatus.ShouldBe(CardStatus.Review);
            revision.Reason.ShouldBe("The work already existed; skipping ahead.");
            revision.Title.ShouldBeNull();
            revision.Description.ShouldBeNull();
            var stored = await verify.Cards.SingleAsync(c => c.Id == card.Id);
            stored.RevisionCount.ShouldBe(1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // A terminal move keeps stamping TerminalReason as the cheap-to-read summary it is today, AND
    // records the transition — the two are not alternatives.
    [Test]
    public async Task A_terminal_move_records_both_the_revision_and_the_terminal_reason()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Terminal history board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Closed as part of another card"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");

            var moved = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "Fixed as part of CARD-0041."),
                CancellationToken.None);

            moved.Card.TerminalReason.ShouldBe("Fixed as part of CARD-0041.");
            await using var verify = CreateContext();
            var revision = await verify.CardRevisions.SingleAsync(r => r.CardId == card.Id);
            revision.Kind.ShouldBe(CardRevisionKind.Move);
            revision.ToStatus.ShouldBe(CardStatus.Done);
            revision.Reason.ShouldBe("Fixed as part of CARD-0041.");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // Every move, not just the ones a human asked for: a history missing "the session finished, so
    // the card went to Review" is missing the most common transition on the board.
    [Test]
    public async Task A_system_driven_move_to_review_records_a_revision_of_its_own()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "System move board"), CancellationToken.None);
            var created = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Session finishes"), CancellationToken.None);
            var inProgressColumn = board.Columns.Single(c => c.StateKey == "in-progress");

            await using (var move = CreateContext())
            {
                var card = await move.Cards
                    .Include(c => c.Board).ThenInclude(b => b.Columns)
                    .Include(c => c.BoardColumn)
                    .SingleAsync(c => c.Id == created.Id);
                card.BoardColumnId = inProgressColumn.Id;
                card.BoardColumn = await move.BoardColumns.SingleAsync(c => c.Id == inProgressColumn.Id);
                card.Status = CardStatus.InProgress;
                await move.SaveChangesAsync();

                // The path AgentSessionLaunchQueue and OrchestratorService take when a run attempt
                // succeeds — it never goes near CardService.ApplyColumnMove.
                CardLifecycleTransitions.TryMoveToReview(card, DateTime.UtcNow).ShouldBeTrue();
                await move.SaveChangesAsync();
            }

            await using var verify = CreateContext();
            var revision = await verify.CardRevisions.SingleAsync(r => r.CardId == created.Id);
            revision.Kind.ShouldBe(CardRevisionKind.Move);
            revision.FromStatus.ShouldBe(CardStatus.InProgress);
            revision.ToStatus.ShouldBe(CardStatus.Review);
            revision.EditedBy.ShouldBe("system");
            revision.Reason.ShouldNotBeNullOrWhiteSpace();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task An_edit_supersedes_the_text_and_archives_what_it_replaced()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Correction board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Retry beats the cold-launch race", "As claimed in CARD-0018.", 1, ["bug"]),
                CancellationToken.None);

            var updated = await harness.CardService.UpdateContentAsync(
                card.Id,
                new UpdateCardContentRequest(
                    card.ConcurrencyToken,
                    "Disproven by task a6e163fe attempt 2, which spawned a new session and lost its prompt.",
                    Title: "Retry does NOT beat the cold-launch race",
                    Description: "Attempt 2 lost its prompt the same way.",
                    Priority: 0,
                    Labels: ["bug", "corrected"],
                    EditedBy: "operator"),
                CancellationToken.None);

            updated.Title.ShouldBe("Retry does NOT beat the cold-launch race");
            updated.Description.ShouldBe("Attempt 2 lost its prompt the same way.");
            updated.Priority.ShouldBe(0);
            updated.Labels.ShouldBe(["bug", "corrected"]);
            updated.RevisionCount.ShouldBe(1);
            updated.ConcurrencyToken.ShouldNotBe(card.ConcurrencyToken);
            updated.UpdatedAt.ShouldBeGreaterThanOrEqualTo(card.UpdatedAt);
            harness.EventBus.PublishedEvents
                .Count(e => e.Group is null
                    && e.EventName == "CardChanged"
                    && HasPayloadValue(e.Payload, "cardId", card.Id))
                .ShouldBeGreaterThanOrEqualTo(1);

            // The revision holds the SUPERSEDED values, not the new ones.
            await using var verify = CreateContext();
            var revision = await verify.CardRevisions.SingleAsync(r => r.CardId == card.Id);
            revision.Kind.ShouldBe(CardRevisionKind.ContentEdit);
            revision.RevisionNumber.ShouldBe(1);
            revision.Title.ShouldBe("Retry beats the cold-launch race");
            revision.Description.ShouldBe("As claimed in CARD-0018.");
            revision.Priority.ShouldBe(1);
            revision.LabelsJson.ShouldBe("[\"bug\"]");
            revision.Reason.ShouldNotBeNull().ShouldContain("a6e163fe");
            revision.EditedBy.ShouldBe("operator");
            revision.FromColumnId.ShouldBeNull();
            revision.ToColumnId.ShouldBeNull();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task An_edit_leaves_the_fields_it_was_not_given_alone()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Partial edit board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Keep my title", "Keep my description", 2, ["keep"]),
                CancellationToken.None);

            var updated = await harness.CardService.UpdateContentAsync(
                card.Id,
                new UpdateCardContentRequest(
                    card.ConcurrencyToken, "Only the description was wrong.", Description: "Rewritten."),
                CancellationToken.None);

            updated.Title.ShouldBe("Keep my title");
            updated.Description.ShouldBe("Rewritten.");
            updated.Priority.ShouldBe(2);
            updated.Labels.ShouldBe(["keep"]);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task An_edit_with_a_stale_token_is_a_conflict_and_writes_no_revision()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Stale edit board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Concurrent edit"), CancellationToken.None);

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.UpdateContentAsync(
                    card.Id,
                    new UpdateCardContentRequest(Guid.NewGuid(), "Someone else got there first.", Title: "Mine"),
                    CancellationToken.None));

            ex.Message.ShouldContain("modified by another operation");
            await using var verify = CreateContext();
            (await verify.CardRevisions.CountAsync(r => r.CardId == card.Id)).ShouldBe(0);
            (await verify.Cards.SingleAsync(c => c.Id == card.Id)).Title.ShouldBe("Concurrent edit");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task An_edit_without_a_reason_or_without_any_content_field_is_rejected()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Reason required board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Needs a reason"), CancellationToken.None);

            var noReason = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.UpdateContentAsync(
                    card.Id,
                    new UpdateCardContentRequest(card.ConcurrencyToken, "   ", Title: "Silent rewrite"),
                    CancellationToken.None));
            noReason.Errors.ShouldContainKey(nameof(UpdateCardContentRequest.Reason));

            var noContent = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.UpdateContentAsync(
                    card.Id,
                    new UpdateCardContentRequest(card.ConcurrencyToken, "Nothing to change."),
                    CancellationToken.None));
            noContent.Errors.ShouldContainKey(nameof(UpdateCardContentRequest.Title));

            await using var verify = CreateContext();
            (await verify.CardRevisions.CountAsync(r => r.CardId == card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // The correction mechanism must not ship with the same landmine it exists to remove.
    [Test]
    public async Task An_edit_past_the_description_ceiling_is_a_validation_error_not_a_500()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Edit ceiling board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Grows with every correction"), CancellationToken.None);

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.UpdateContentAsync(
                    card.Id,
                    new UpdateCardContentRequest(
                        card.ConcurrencyToken,
                        "Appending context.",
                        Description: new string('x', CardService.MaxDescriptionLength + 1)),
                    CancellationToken.None));

            ex.Errors[nameof(UpdateCardContentRequest.Description)].Single().ShouldContain("20,000");

            // And one character under the ceiling goes through, on the update path too.
            var updated = await harness.CardService.UpdateContentAsync(
                card.Id,
                new UpdateCardContentRequest(
                    card.ConcurrencyToken,
                    "Appending context.",
                    Description: new string('x', CardService.MaxDescriptionLength - 1)),
                CancellationToken.None);
            updated.Description.Length.ShouldBe(CardService.MaxDescriptionLength - 1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // One monotonic sequence across kinds is the point of a single table: the history reads in the
    // order things actually happened, edits and moves interleaved.
    [Test]
    public async Task Revisions_number_one_sequence_across_kinds_and_read_newest_first()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Interleaved board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "First"), CancellationToken.None);
            var reviewColumn = board.Columns.Single(c => c.StateKey == "review");

            var first = await harness.CardService.UpdateContentAsync(
                card.Id,
                new UpdateCardContentRequest(card.ConcurrencyToken, "First correction.", Title: "Second"),
                CancellationToken.None);
            var moved = await harness.CardService.MoveAsync(
                first.Id,
                new MoveCardRequest(reviewColumn.Id, first.ConcurrencyToken, "Ready to look at."),
                CancellationToken.None);
            var third = await harness.CardService.UpdateContentAsync(
                moved.Card.Id,
                new UpdateCardContentRequest(moved.Card.ConcurrencyToken, "Second correction.", Title: "Third"),
                CancellationToken.None);

            third.Title.ShouldBe("Third");
            third.RevisionCount.ShouldBe(3);

            var history = await harness.CardService.GetRevisionsAsync(card.Id, CancellationToken.None);
            history.Select(r => r.RevisionNumber).ShouldBe([3, 2, 1]);
            history.Select(r => r.Kind).ShouldBe(
                [CardRevisionKind.ContentEdit, CardRevisionKind.Move, CardRevisionKind.ContentEdit]);
            // Each ContentEdit holds the title it superseded.
            history[0].Title.ShouldBe("Second");
            history[2].Title.ShouldBe("First");
            history[1].Reason.ShouldBe("Ready to look at.");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // CARD-0026's closing note was known-wrong and uncorrectable — the case that motivated the
    // whole card. A done card must still be editable.
    [Test]
    public async Task A_card_in_a_terminal_column_can_still_be_corrected()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Done but wrong board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id,
                new CreateCardRequest(null, "Codex question detection", "The failure is load-flaky."),
                CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            var closed = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "Fixed."),
                CancellationToken.None);

            var corrected = await harness.CardService.UpdateContentAsync(
                closed.Card.Id,
                new UpdateCardContentRequest(
                    closed.Card.ConcurrencyToken,
                    "The diagnosis was disproven; the fix landed in f078dd2.",
                    Description: "Checkout-path dependent: the prompt echo wrapped under long worktree paths."),
                CancellationToken.None);

            corrected.Status.ShouldBe(CardStatus.Done);
            corrected.Description.ShouldContain("Checkout-path dependent");
            var history = await harness.CardService.GetRevisionsAsync(card.Id, CancellationToken.None);
            history[0].Kind.ShouldBe(CardRevisionKind.ContentEdit);
            history[0].Description.ShouldBe("The failure is load-flaky.");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Revisions_of_an_unknown_card_are_a_not_found()
    {
        await using var harness = BuildHarness(NewTempRoot());

        await Should.ThrowAsync<NotFoundException>(() =>
            harness.CardService.GetRevisionsAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Test]
    public async Task Archiving_hides_a_card_from_the_board_but_keeps_it_one_query_away()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Archive board"), CancellationToken.None);
            var kept = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Still wanted"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Filed by mistake"), CancellationToken.None);

            var archived = await harness.CardService.ArchiveAsync(
                card.Id,
                new ArchiveCardRequest(card.ConcurrencyToken, "Duplicate of CARD-0001.", "operator"),
                CancellationToken.None);

            archived.ArchivedAt.ShouldNotBeNull();
            archived.ArchivedReason.ShouldBe("Duplicate of CARD-0001.");
            archived.ArchivedBy.ShouldBe("operator");

            var visible = await harness.BoardService.GetByIdAsync(board.Id, CancellationToken.None);
            visible.Columns.SelectMany(c => c.Cards).Select(c => c.Id).ShouldBe([kept.Id]);

            var withArchived = await harness.BoardService.GetByIdAsync(
                board.Id, includeArchived: true, CancellationToken.None);
            withArchived.Columns.SelectMany(c => c.Cards).Select(c => c.Id)
                .OrderBy(id => id).ShouldBe(new[] { kept.Id, card.Id }.OrderBy(id => id));

            // The row is still there, and so is the reason.
            await using var verify = CreateContext();
            var revision = await verify.CardRevisions.SingleAsync(r => r.CardId == card.Id);
            revision.Kind.ShouldBe(CardRevisionKind.Archive);
            revision.Reason.ShouldBe("Duplicate of CARD-0001.");
            revision.EditedBy.ShouldBe("operator");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Unarchive_restores_the_card_and_leaves_both_acts_in_the_history()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Unarchive board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Archived too eagerly"), CancellationToken.None);
            var archived = await harness.CardService.ArchiveAsync(
                card.Id,
                new ArchiveCardRequest(card.ConcurrencyToken, "Thought it was done."),
                CancellationToken.None);

            var restored = await harness.CardService.UnarchiveAsync(
                archived.Id,
                new UnarchiveCardRequest(archived.ConcurrencyToken, "It was not done after all."),
                CancellationToken.None);

            restored.ArchivedAt.ShouldBeNull();
            restored.ArchivedReason.ShouldBeNull();
            var visible = await harness.BoardService.GetByIdAsync(board.Id, CancellationToken.None);
            visible.Columns.SelectMany(c => c.Cards).Select(c => c.Id).ShouldContain(card.Id);

            // Clearing ArchivedReason is not a loss — the history holds both acts.
            var history = await harness.CardService.GetRevisionsAsync(card.Id, CancellationToken.None);
            history.Select(r => r.Kind).ShouldBe([CardRevisionKind.Unarchive, CardRevisionKind.Archive]);
            history[1].Reason.ShouldBe("Thought it was done.");
            history[0].Reason.ShouldBe("It was not done after all.");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // Archiving the record out from under a working agent is the one genuinely destructive shape
    // here, and unlike everything else on this card it cannot be undone by reading the history.
    [Test]
    public async Task Archive_is_refused_while_a_live_owner_session_holds_the_card()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Live session board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Being worked right now"), CancellationToken.None);

            await using (var claim = CreateContext())
            {
                var now = DateTime.UtcNow;
                var session = new AgentSession
                {
                    Id = Guid.NewGuid(),
                    CardId = card.Id,
                    DefinitionName = "fake",
                    AgentKind = AgentKind.Raw,
                    Status = SessionStatus.Running,
                    Cwd = tempRoot,
                    CreatedAt = now,
                    StartedAt = now,
                    LastSeenAt = now
                };
                claim.AgentSessions.Add(session);
                var row = await claim.Cards.SingleAsync(c => c.Id == card.Id);
                row.OwnerSessionId = session.Id;
                await claim.SaveChangesAsync();
            }

            // A second harness, because the first one's DbContext still has the card cached from
            // before the claim — a request-scoped context in production always reads it fresh.
            await using var claimAware = BuildHarness(tempRoot);
            var ex = await Should.ThrowAsync<ConflictException>(() =>
                claimAware.CardService.ArchiveAsync(
                    card.Id,
                    new ArchiveCardRequest(card.ConcurrencyToken, "Tidying the board."),
                    CancellationToken.None));

            ex.Message.ShouldContain("live owner session");
            await using var verify = CreateContext();
            (await verify.Cards.SingleAsync(c => c.Id == card.Id)).ArchivedAt.ShouldBeNull();
            (await verify.CardRevisions.CountAsync(r => r.CardId == card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // CARD-0005 regression pin. The identifier allocator learns a number is taken by seeing the
    // row; a global EF query filter on ArchivedAt would hide it from NextIdentifierAsync too and
    // hand the freed number to the next card, silently repointing every reference to the old one.
    [Test]
    public async Task Archiving_the_highest_card_does_not_free_its_identifier()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Identifier guard board"), CancellationToken.None);

            var first = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "First"), CancellationToken.None);
            first.Identifier.ShouldBe("CARD-0001");

            await harness.CardService.ArchiveAsync(
                first.Id,
                new ArchiveCardRequest(first.ConcurrencyToken, "Wrong board."),
                CancellationToken.None);

            var second = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Second"), CancellationToken.None);

            second.Identifier.ShouldBe("CARD-0002");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task An_archived_card_is_out_of_play_until_it_is_unarchived()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Out of play board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Off the board"), CancellationToken.None);
            var archived = await harness.CardService.ArchiveAsync(
                card.Id,
                new ArchiveCardRequest(card.ConcurrencyToken, "Not real work."),
                CancellationToken.None);
            var activeColumn = board.Columns.Single(c => c.StateKey == "in-progress");

            // Moving it into an active column would otherwise spawn an agent on it.
            var move = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.MoveAsync(
                    archived.Id,
                    new MoveCardRequest(activeColumn.Id, archived.ConcurrencyToken),
                    CancellationToken.None));
            move.Message.ShouldContain("archived");

            var spawn = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.SpawnAsync(
                    archived.Id, new SpawnCardRequest("fake"), CancellationToken.None));
            spawn.Message.ShouldContain("archived");

            var again = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.ArchiveAsync(
                    archived.Id,
                    new ArchiveCardRequest(archived.ConcurrencyToken, "Twice."),
                    CancellationToken.None));
            again.Message.ShouldContain("already archived");

            // But it can still be CORRECTED — the record of a card taken off the board can be
            // wrong just like any other.
            var corrected = await harness.CardService.UpdateContentAsync(
                archived.Id,
                new UpdateCardContentRequest(
                    archived.ConcurrencyToken, "Recording what it actually was.", Title: "Off the board (duplicate)"),
                CancellationToken.None);
            corrected.Title.ShouldBe("Off the board (duplicate)");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Archive_and_unarchive_require_a_reason_and_a_matching_token()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Archive contract board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Contract"), CancellationToken.None);

            var noReason = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.ArchiveAsync(
                    card.Id, new ArchiveCardRequest(card.ConcurrencyToken, "  "), CancellationToken.None));
            noReason.Errors.ShouldContainKey(nameof(ArchiveCardRequest.Reason));

            var staleToken = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.ArchiveAsync(
                    card.Id, new ArchiveCardRequest(Guid.NewGuid(), "Tidy."), CancellationToken.None));
            staleToken.Message.ShouldContain("modified by another operation");

            var notArchived = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.UnarchiveAsync(
                    card.Id, new UnarchiveCardRequest(card.ConcurrencyToken, "Undo."), CancellationToken.None));
            notArchived.Message.ShouldContain("not archived");

            await using var verify = CreateContext();
            (await verify.CardRevisions.CountAsync(r => r.CardId == card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    // Two writers who both read RevisionCount = n both allocate n + 1, and the unique
    // (CardId, RevisionNumber) index rejects the loser — as a DbUpdateException, which is NOT a
    // concurrency exception and escaped as an unexplained 500 until SaveCardWriteAsync learned the
    // shape. Two concurrent channel delegations of one card did exactly that.
    [Test]
    public async Task Two_concurrent_writers_leave_one_winner_and_one_conflict_never_a_500()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var seed = BuildHarness(tempRoot);
            var board = await seed.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Race board"), CancellationToken.None);
            var card = await seed.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Contended"), CancellationToken.None);
            var reviewColumn = board.Columns.Single(c => c.StateKey == "review");

            await using var first = BuildHarness(tempRoot);
            await using var second = BuildHarness(tempRoot);
            var request = new MoveCardRequest(reviewColumn.Id, card.ConcurrencyToken, "Racing.");
            var outcomes = await Task.WhenAll(
                CaptureAsync(() => first.CardService.MoveAsync(card.Id, request, CancellationToken.None)),
                CaptureAsync(() => second.CardService.MoveAsync(card.Id, request, CancellationToken.None)));

            outcomes.Count(o => o.Error is null).ShouldBe(1);
            outcomes.Count(o => o.Error is ConflictException).ShouldBe(1);

            await using var verify = CreateContext();
            (await verify.CardRevisions.CountAsync(r => r.CardId == card.Id)).ShouldBe(1);
            (await verify.Cards.SingleAsync(c => c.Id == card.Id)).RevisionCount.ShouldBe(1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reopen_writes_one_revision_with_the_superseded_terminal_facts_and_clears_them()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Reopen facts board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Closed too soon"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            var backlogColumn = board.Columns.Single(c => c.StateKey == "backlog");
            var closed = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "Fixed as part of CARD-0041."),
                CancellationToken.None);
            closed.Card.CompletedAt.ShouldNotBeNull();
            var originalCompletedAt = closed.Card.CompletedAt!.Value;

            var reopened = await harness.CardService.ReopenAsync(
                card.Id,
                new ReopenCardRequest(closed.Card.ConcurrencyToken, "The close was wrong.", ReopenedBy: "operator"),
                CancellationToken.None);

            reopened.Status.ShouldBe(CardStatus.Backlog);
            reopened.BoardColumnId.ShouldBe(backlogColumn.Id);
            reopened.CompletedAt.ShouldBeNull();
            reopened.TerminalReason.ShouldBeNull();
            reopened.ConcurrencyToken.ShouldNotBe(closed.Card.ConcurrencyToken);
            reopened.RevisionCount.ShouldBe(2);

            await using var verify = CreateContext();
            var revisions = await verify.CardRevisions
                .Where(r => r.CardId == card.Id)
                .OrderBy(r => r.RevisionNumber)
                .ToListAsync();
            revisions.Count.ShouldBe(2);
            revisions[0].Kind.ShouldBe(CardRevisionKind.Move);
            var reopen = revisions[1];
            reopen.Kind.ShouldBe(CardRevisionKind.Reopen);
            reopen.FromStatus.ShouldBe(CardStatus.Done);
            reopen.ToStatus.ShouldBe(CardStatus.Backlog);
            reopen.FromColumnId.ShouldBe(doneColumn.Id);
            reopen.ToColumnId.ShouldBe(backlogColumn.Id);
            reopen.TerminalReason.ShouldBe("Fixed as part of CARD-0041.");
            reopen.CompletedAt.ShouldBe(originalCompletedAt);
            reopen.Reason.ShouldBe("The close was wrong.");
            reopen.EditedBy.ShouldBe("operator");
            revisions.Count(r => r.Kind == CardRevisionKind.Move).ShouldBe(1);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reopen_defaults_to_the_backlog_column_and_never_spawns()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Reopen spawn board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Do not start me"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            var backlogColumn = board.Columns.Single(c => c.StateKey == "backlog");
            var activeColumn = board.Columns.Single(c => c.StateKey == "in-progress");

            var closed = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "Parked."),
                CancellationToken.None);
            var defaulted = await harness.CardService.ReopenAsync(
                card.Id,
                new ReopenCardRequest(closed.Card.ConcurrencyToken, "Back to the pile."),
                CancellationToken.None);
            defaulted.BoardColumnId.ShouldBe(backlogColumn.Id);
            defaulted.OwnerSessionId.ShouldBeNull();

            var closedAgain = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, defaulted.ConcurrencyToken, "Parked again."),
                CancellationToken.None);
            var intoActive = await harness.CardService.ReopenAsync(
                card.Id,
                new ReopenCardRequest(
                    closedAgain.Card.ConcurrencyToken,
                    "Into the active column, still do not start.",
                    BoardColumnId: activeColumn.Id),
                CancellationToken.None);

            intoActive.Status.ShouldBe(CardStatus.InProgress);
            intoActive.BoardColumnId.ShouldBe(activeColumn.Id);
            intoActive.OwnerSessionId.ShouldBeNull();
            await using var verify = CreateContext();
            (await verify.AgentSessions.CountAsync(s => s.CardId == card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reopen_of_a_card_closed_before_revisions_existed_still_keeps_its_terminal_facts()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Pre-history reopen board"), CancellationToken.None);
            var created = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Closed before CARD-0019"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            // Postgres timestamptz stores microseconds; DateTime.UtcNow has 100ns ticks.
            var utc = DateTime.UtcNow.AddDays(-40);
            var completedAt = new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);

            await using (var seed = CreateContext())
            {
                var row = await seed.Cards.SingleAsync(c => c.Id == created.Id);
                row.BoardColumnId = doneColumn.Id;
                row.Status = CardStatus.Done;
                row.CompletedAt = completedAt;
                row.TerminalReason = "Closed before revisions existed.";
                row.RevisionCount = 0;
                await seed.SaveChangesAsync();
            }

            await using var reopenHarness = BuildHarness(tempRoot);
            var token = (await reopenHarness.CardService.GetByIdAsync(created.Id, CancellationToken.None))
                .ConcurrencyToken;
            var reopened = await reopenHarness.CardService.ReopenAsync(
                created.Id,
                new ReopenCardRequest(token, "The record still has to keep those facts."),
                CancellationToken.None);

            reopened.Status.ShouldBe(CardStatus.Backlog);
            reopened.CompletedAt.ShouldBeNull();
            reopened.TerminalReason.ShouldBeNull();
            reopened.RevisionCount.ShouldBe(1);

            await using var verify = CreateContext();
            var revision = await verify.CardRevisions.SingleAsync(r => r.CardId == created.Id);
            revision.Kind.ShouldBe(CardRevisionKind.Reopen);
            revision.TerminalReason.ShouldBe("Closed before revisions existed.");
            revision.CompletedAt.ShouldBe(completedAt);
            (await verify.CardRevisions.CountAsync(r => r.CardId == created.Id && r.Kind == CardRevisionKind.Move))
                .ShouldBe(0);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task A_reopened_card_recloses_with_a_fresh_completion()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Reclose board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Will close twice"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");

            var firstClose = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "First verdict."),
                CancellationToken.None);
            var firstCompletedAt = firstClose.Card.CompletedAt;
            firstCompletedAt.ShouldNotBeNull();

            var reopened = await harness.CardService.ReopenAsync(
                card.Id,
                new ReopenCardRequest(firstClose.Card.ConcurrencyToken, "Not actually done."),
                CancellationToken.None);

            // A later timestamp so ??= cannot accidentally keep the original by equality.
            await Task.Delay(20);
            var secondClose = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, reopened.ConcurrencyToken, "Second verdict."),
                CancellationToken.None);

            secondClose.Card.TerminalReason.ShouldBe("Second verdict.");
            secondClose.Card.CompletedAt.ShouldNotBeNull();
            secondClose.Card.CompletedAt.ShouldNotBe(firstCompletedAt);
            secondClose.Card.RevisionCount.ShouldBe(3);

            var history = await harness.CardService.GetRevisionsAsync(card.Id, CancellationToken.None);
            history.Select(r => r.RevisionNumber).ShouldBe([3, 2, 1]);
            history.Select(r => r.Kind).ShouldBe(
                [CardRevisionKind.Move, CardRevisionKind.Reopen, CardRevisionKind.Move]);
            history[1].TerminalReason.ShouldBe("First verdict.");
            history[1].CompletedAt.ShouldBe(firstCompletedAt);
            history[0].Reason.ShouldBe("Second verdict.");
            history[2].Reason.ShouldBe("First verdict.");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reopen_with_a_stale_token_is_a_conflict_and_writes_no_revision()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Stale reopen board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Token race"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            var closed = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "Closed."),
                CancellationToken.None);

            var ex = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.ReopenAsync(
                    card.Id,
                    new ReopenCardRequest(Guid.NewGuid(), "Someone else got there first."),
                    CancellationToken.None));

            ex.Message.ShouldContain("modified by another operation");
            await using var verify = CreateContext();
            (await verify.CardRevisions.CountAsync(r => r.CardId == card.Id && r.Kind == CardRevisionKind.Reopen))
                .ShouldBe(0);
            var stored = await verify.Cards.SingleAsync(c => c.Id == card.Id);
            stored.Status.ShouldBe(CardStatus.Done);
            stored.CompletedAt.ShouldNotBeNull();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reopen_of_a_live_card_or_into_a_terminal_column_or_without_a_reason_is_rejected()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Reopen reject board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Still live"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");

            var live = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.ReopenAsync(
                    card.Id,
                    new ReopenCardRequest(card.ConcurrencyToken, "Not closed."),
                    CancellationToken.None));
            live.Message.ShouldContain("is not closed");

            var closed = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "Now closed."),
                CancellationToken.None);

            var intoTerminal = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.ReopenAsync(
                    card.Id,
                    new ReopenCardRequest(
                        closed.Card.ConcurrencyToken, "Back into Done.", BoardColumnId: doneColumn.Id),
                    CancellationToken.None));
            intoTerminal.Errors.ShouldContainKey(nameof(ReopenCardRequest.BoardColumnId));

            var noReason = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.ReopenAsync(
                    card.Id,
                    new ReopenCardRequest(closed.Card.ConcurrencyToken, "   "),
                    CancellationToken.None));
            noReason.Errors.ShouldContainKey(nameof(ReopenCardRequest.Reason));

            await using var verify = CreateContext();
            (await verify.CardRevisions.CountAsync(r => r.CardId == card.Id && r.Kind == CardRevisionKind.Reopen))
                .ShouldBe(0);
            (await verify.Cards.SingleAsync(c => c.Id == card.Id)).Status.ShouldBe(CardStatus.Done);
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reopen_of_an_archived_card_is_refused_until_unarchive()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Archived reopen board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Closed and shelved"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            var closed = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "Done."),
                CancellationToken.None);
            var archived = await harness.CardService.ArchiveAsync(
                card.Id,
                new ArchiveCardRequest(closed.Card.ConcurrencyToken, "Off the board."),
                CancellationToken.None);

            var refused = await Should.ThrowAsync<ConflictException>(() =>
                harness.CardService.ReopenAsync(
                    card.Id,
                    new ReopenCardRequest(archived.ConcurrencyToken, "Want it live again."),
                    CancellationToken.None));
            refused.Message.ShouldContain("unarchive it before reopening");

            var restored = await harness.CardService.UnarchiveAsync(
                card.Id,
                new UnarchiveCardRequest(archived.ConcurrencyToken, "Back on the board."),
                CancellationToken.None);
            var reopened = await harness.CardService.ReopenAsync(
                card.Id,
                new ReopenCardRequest(restored.ConcurrencyToken, "And back to live."),
                CancellationToken.None);
            reopened.Status.ShouldBe(CardStatus.Backlog);
            reopened.ArchivedAt.ShouldBeNull();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reopen_deletes_no_review_checkpoint()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            var now = DateTime.UtcNow;
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = $"Reopen checkpoint {Guid.NewGuid():N}",
                Slug = $"reopen-cp-{Guid.NewGuid():N}",
                WorkingDirectory = tempRoot,
                Details = "checkpoint pin",
                CreatedAt = now,
                UpdatedAt = now
            };
            var checkpoint = new AgentReviewCheckpoint
            {
                Id = Guid.NewGuid(),
                AgentId = agent.Id,
                Reason = "Card completed (seeded)",
                CreatedAt = now
            };
            db.Agents.Add(agent);
            db.AgentReviewCheckpoints.Add(checkpoint);
            await db.SaveChangesAsync();

            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Checkpoint reopen board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Signed off then reopened"), CancellationToken.None);

            await using (var assign = CreateContext())
            {
                var row = await assign.Cards.SingleAsync(c => c.Id == card.Id);
                row.AssignedAgentId = agent.Id;
                await assign.SaveChangesAsync();
            }

            await using var closeHarness = BuildHarness(tempRoot);
            var fresh = await closeHarness.CardService.GetByIdAsync(card.Id, CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            var closed = await closeHarness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, fresh.ConcurrencyToken, "Signed off."),
                CancellationToken.None);
            var reopened = await closeHarness.CardService.ReopenAsync(
                card.Id,
                new ReopenCardRequest(closed.Card.ConcurrencyToken, "Need another pass."),
                CancellationToken.None);
            reopened.Status.ShouldBe(CardStatus.Backlog);

            await using var verify = CreateContext();
            (await verify.AgentReviewCheckpoints.AnyAsync(c => c.Id == checkpoint.Id)).ShouldBeTrue();
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task A_terminal_move_rejection_names_the_reopen_endpoint()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "Courtesy message board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Stuck in Done"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            var backlogColumn = board.Columns.Single(c => c.StateKey == "backlog");
            var closed = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "Closed."),
                CancellationToken.None);

            var ex = await Should.ThrowAsync<ValidationException>(() =>
                harness.CardService.MoveAsync(
                    card.Id,
                    new MoveCardRequest(backlogColumn.Id, closed.Card.ConcurrencyToken, "Try to drag it out."),
                    CancellationToken.None));

            var message = ex.Errors[nameof(BoardColumn.CardStatus)].Single();
            message.ShouldContain("POST /cards/{id}/reopen");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    [Test]
    public async Task Reopen_when_no_live_column_exists_is_a_conflict()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var project = NewProject(tempRoot);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            await using var harness = BuildHarness(tempRoot);
            var board = await harness.BoardService.CreateAsync(
                new CreateBoardRequest(project.Id, "No live column board"), CancellationToken.None);
            var card = await harness.CardService.CreateAsync(
                board.Id, new CreateCardRequest(null, "Nowhere to go"), CancellationToken.None);
            var doneColumn = board.Columns.Single(c => c.StateKey == "done");
            var closed = await harness.CardService.MoveAsync(
                card.Id,
                new MoveCardRequest(doneColumn.Id, card.ConcurrencyToken, "Closed."),
                CancellationToken.None);

            await using (var mutate = CreateContext())
            {
                var live = await mutate.BoardColumns
                    .Where(c => c.BoardId == board.Id && !c.IsTerminal)
                    .ToListAsync();
                foreach (var column in live)
                {
                    column.IsTerminal = true;
                    column.CardStatus = CardStatus.Done;
                }

                await mutate.SaveChangesAsync();
            }

            await using var reopenHarness = BuildHarness(tempRoot);
            var token = (await reopenHarness.CardService.GetByIdAsync(card.Id, CancellationToken.None))
                .ConcurrencyToken;
            var ex = await Should.ThrowAsync<ConflictException>(() =>
                reopenHarness.CardService.ReopenAsync(
                    card.Id,
                    new ReopenCardRequest(token, "Need a live column."),
                    CancellationToken.None));
            ex.Message.ShouldContain("no live column");
        }
        finally
        {
            await CleanupProjectsByTempRootAsync(tempRoot);
            DeleteDirectoryBestEffort(tempRoot);
        }
    }

    private static async Task<MoveOutcome> CaptureAsync(Func<Task<MoveCardResult>> move)
    {
        try
        {
            return new MoveOutcome(await move(), null);
        }
        catch (Exception ex)
        {
            return new MoveOutcome(null, ex);
        }
    }

    private sealed record MoveOutcome(MoveCardResult? Move, Exception? Error);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(string tempRoot)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 1_000,
            KillGraceMs = 100,
            SignalRMaxChunkChars = 16 * 1024,
            ReplayBufferMaxChars = 128 * 1024,
            SessionLogPath = Path.Combine(tempRoot, "session-logs")
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            InternalTrackerRepositoryPathPrefix = tempRoot
        }));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new OptionsMonitorStub<AgentRegistrySettings>(
            new AgentRegistrySettings
            {
                DefaultDefinition = "fake",
                Definitions =
                {
                    ["fake"] = new AgentDefinition
                    {
                        Kind = "Raw",
                        Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe")
                    }
                }
            }));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IWorktreeManager>(new StubWorktreeManager(Path.Combine(tempRoot, "worktrees")));
        services.AddSingleton<IAgentProtocolAdapterFactory>(new NoAdapterFactory());
        services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddScoped<OrchestratorService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<AgentService>();
        services.AddSingleton<IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(
                new System.IO.Abstractions.FileSystem()));
        services.AddScoped<BoardService>();
        services.AddSingleton<GitWorkspaceService>();
        services.AddScoped<AgentReviewCheckpointService>();
        services.AddScoped<CardService>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<BoardService>(),
            scope.ServiceProvider.GetRequiredService<CardService>(),
            provider.GetRequiredService<AgentSessionLaunchQueue>(),
            eventBus);
    }

    private static Project NewProject(string tempRoot)
    {
        var repoPath = Path.Combine(tempRoot, "repo");
        Directory.CreateDirectory(repoPath);
        var now = DateTime.UtcNow;
        return new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = repoPath,
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-card-correction-{Guid.NewGuid():N}");

    /// <summary>Deletes only the rows this test's temp root owns. Revisions go with their card.</summary>
    private static async Task CleanupProjectsByTempRootAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count == 0)
            return;

        var boardIds = await db.Boards
            .Where(b => projectIds.Contains(b.ProjectId))
            .Select(b => b.Id)
            .ToListAsync();
        var cardIds = await db.Cards
            .Where(c => boardIds.Contains(c.BoardId))
            .Select(c => c.Id)
            .ToListAsync();
        var sessionIds = await db.AgentSessions
            .Where(s => s.CardId != null && cardIds.Contains(s.CardId.Value))
            .Select(s => s.Id)
            .ToListAsync();
        var attemptIds = await db.RunAttempts
            .Where(a => cardIds.Contains(a.CardId))
            .Select(a => a.Id)
            .ToListAsync();

        var agentIds = await db.Agents
            .Where(a => a.WorkingDirectory.StartsWith(tempRoot))
            .Select(a => a.Id)
            .ToListAsync();

        await db.Cards
            .Where(c => cardIds.Contains(c.Id))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(c => c.OwnerSessionId, (Guid?)null)
                .SetProperty(c => c.CurrentWorktreeId, (Guid?)null)
                .SetProperty(c => c.AssignedAgentId, (Guid?)null)
                .SetProperty(c => c.ActiveWorkflowRunId, (Guid?)null));
        if (agentIds.Count > 0)
        {
            await db.AgentReviewCheckpoints.Where(c => agentIds.Contains(c.AgentId)).ExecuteDeleteAsync();
            await db.Agents.Where(a => agentIds.Contains(a.Id)).ExecuteDeleteAsync();
        }
        await db.TokenUsages.Where(t => attemptIds.Contains(t.RunAttemptId)).ExecuteDeleteAsync();
        await db.RunAttempts.Where(a => attemptIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.RetrySchedules.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Worktrees.Where(w => cardIds.Contains(w.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
    }

    private static bool HasPayloadValue<T>(object payload, string propertyName, T expected)
    {
        var value = payload.GetType().GetProperty(propertyName)?.GetValue(payload);
        return value is T typed && EqualityComparer<T>.Default.Equals(typed, expected);
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp worktree/session directories.
        }
    }

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        BoardService BoardService,
        CardService CardService,
        AgentSessionLaunchQueue LaunchQueue,
        MockEventBus EventBus) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    /// <summary>No test here spawns a session; asking for an adapter is a bug in the test.</summary>
    private sealed class NoAdapterFactory : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            throw new InvalidOperationException("No agent session should be launched by these tests.");
    }

    private sealed class StubWorktreeManager : IWorktreeManager
    {
        private readonly string _worktreeRoot;
        private readonly List<WorktreeInfo> _worktrees = [];

        public StubWorktreeManager(string worktreeRoot)
        {
            _worktreeRoot = worktreeRoot;
        }

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(_worktreeRoot);
            var worktreePath = Path.Combine(_worktreeRoot, $"card-{cardId}");
            Directory.CreateDirectory(worktreePath);
            var now = DateTimeOffset.UtcNow;
            var info = new WorktreeInfo(cardId, repoPath, worktreePath, $"feat/card-{cardId}", baseRef, now, now);
            _worktrees.Add(info);
            return Task.FromResult(info);
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>(_worktrees.ToList());

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
    {
        public OptionsMonitorStub(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
