using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Application.ExternalTrackerSyncIdentifierTests;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0170: a tracker sync lands cards where MANUAL creates land, and owns only the terminal
/// boundary — unless a board opts back in with <c>tracker.import_column: active</c>.
/// </summary>
/// <remarks>
/// <c>BoardColumn.IsActive</c> means "auto-dispatch MAY start an agent here"; it never meant
/// "new work lands here". Reading it as intake made "an issue is open on GitHub" equivalent to
/// "start an agent on it" on every default-shaped board, and — worse — dragged any unowned
/// non-terminal card back to the active column on every 30-minute tick, so a manual move to
/// Backlog did not stick (measured live at 15:07:25 on all eleven imported cards).
/// </remarks>
[Category("Integration")]
public class ExternalTrackerSyncLandingColumnTests
{
    private const string ActiveModeWorkflow = """
        ---
        tracker:
          kind: github_issues
          repository: acme/app
          active_states: [open]
          import_column: active
        ---
        Work on {{ issue.identifier }}.
        """;

    [Test]
    public async Task Default_import_lands_in_the_backlog_column_and_is_not_started()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var ns = NewNamespace();
            var board = await SeedBoardAsync(db, tempRoot);
            var backlog = board.Columns.Single(c => c.StateKey == "backlog");
            var tracker = new FakeTracker([Issue(Ext(ns, 3), "#3", "Fresh import")]);

            await NewSut(db, tracker).SyncAsync(DateTime.UtcNow, board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.BoardId == board.Id);
            card.BoardColumnId.ShouldBe(backlog.Id);
            card.Status.ShouldBe(CardStatus.Backlog);
            // Not in an active column, so the orchestrator tick never even sees it as a candidate
            // (LoadEligibleCandidatesAsync filters on BoardColumn.IsActive) and nothing started it.
            card.StartedAt.ShouldBeNull();
            card.OwnerSessionId.ShouldBeNull();
            (await verify.AgentSessions.CountAsync(s => s.CardId == card.Id)).ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Default_mode_never_moves_a_non_terminal_card_for_an_open_issue()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var board = await SeedBoardAsync(db, tempRoot);
            var inProgress = board.Columns.Single(c => c.StateKey == "in_progress");
            var review = board.Columns.Single(c => c.StateKey == "review");
            var ns = NewNamespace();
            var card = await SeedLinkedCardAsync(db, board, "CARD-0001", Ext(ns, 3), "#3");
            var tracker = new FakeTracker([Issue(Ext(ns, 3), "#3", "Still open")]);

            foreach (var column in new[] { inProgress, review })
            {
                await using (var move = CreateContext())
                {
                    await move.Cards.Where(c => c.Id == card.Id).ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.BoardColumnId, column.Id)
                        .SetProperty(c => c.Status, column.CardStatus));
                }

                await using var sync = CreateContext();
                await NewSut(sync, tracker).SyncAsync(DateTime.UtcNow, board.Id, CancellationToken.None);

                await using var verify = CreateContext();
                var stored = await verify.Cards.SingleAsync(c => c.Id == card.Id);
                stored.BoardColumnId.ShouldBe(column.Id, $"the tracker moved the card out of {column.StateKey}");
            }

            await using var revisions = CreateContext();
            (await revisions.CardRevisions.CountAsync(r => r.CardId == card.Id && r.EditedBy == "external-tracker"))
                .ShouldBe(0);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Default_mode_still_moves_closed_to_terminal()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var board = await SeedBoardAsync(db, tempRoot);
            var inProgress = board.Columns.Single(c => c.StateKey == "in_progress");
            var done = board.Columns.Single(c => c.StateKey == "done");
            var ns = NewNamespace();
            var card = await SeedLinkedCardAsync(db, board, "CARD-0001", Ext(ns, 3), "#3", inProgress.Id);

            // No candidates: the issue left the active states. LookupIssues answers the reconcile.
            var tracker = new FakeTracker([]) { LookupIssues = [Issue(Ext(ns, 3), "#3", "Shipped", "closed")] };
            await NewSut(db, tracker).SyncAsync(DateTime.UtcNow, board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            var stored = await verify.Cards.SingleAsync(c => c.Id == card.Id);
            stored.BoardColumnId.ShouldBe(done.Id);
            stored.Status.ShouldBe(CardStatus.Done);
            stored.TerminalReason.ShouldNotBeNull();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Import_column_active_keeps_the_e10_behaviour()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var board = await SeedBoardAsync(db, tempRoot, ActiveModeWorkflow);
            var inProgress = board.Columns.Single(c => c.StateKey == "in_progress");
            var backlog = board.Columns.Single(c => c.StateKey == "backlog");
            var tracker = new FakeTracker([Issue(Ext(NewNamespace(), 3), "#3", "Tracker is the queue")]);

            await NewSut(db, tracker).SyncAsync(DateTime.UtcNow, board.Id, CancellationToken.None);

            await using var afterCreate = CreateContext();
            var card = await afterCreate.Cards.SingleAsync(c => c.BoardId == board.Id);
            card.BoardColumnId.ShouldBe(inProgress.Id);
            card.Status.ShouldBe(CardStatus.InProgress);

            // ...and the drag-back is the point of the mode: a hand-move off the tracker's column
            // is reverted on the next sync, which is what "the tracker owns this column" means.
            await using (var move = CreateContext())
            {
                await move.Cards.Where(c => c.Id == card.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.BoardColumnId, backlog.Id)
                    .SetProperty(c => c.Status, CardStatus.Backlog));
            }

            await using var second = CreateContext();
            await NewSut(second, tracker).SyncAsync(DateTime.UtcNow, board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.Cards.SingleAsync(c => c.Id == card.Id)).BoardColumnId.ShouldBe(inProgress.Id);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Landing_column_falls_back_when_the_board_has_no_backlog_status_column()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            // [Todo(InProgress, active), Done(terminal)] — no Backlog status, and no non-active
            // non-terminal column either, so the last fallback (the first column) is what lands.
            var board = await SeedBoardAsync(db, tempRoot, DefaultWorkflow, defaultColumns: false);
            var todo = board.Columns.Single(c => c.StateKey == "todo");

            TrackerLandingColumn.Resolve(board, TrackerImportColumn.Backlog)!.Id.ShouldBe(todo.Id);

            var tracker = new FakeTracker([Issue(Ext(NewNamespace(), 3), "#3", "Nowhere else to land")]);
            await NewSut(db, tracker).SyncAsync(DateTime.UtcNow, board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.Cards.SingleAsync(c => c.BoardId == board.Id)).BoardColumnId.ShouldBe(todo.Id);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public void Landing_column_prefers_the_backlog_status_then_the_first_non_active_non_terminal()
    {
        var board = new Board { Id = Guid.NewGuid(), Name = "shape" };
        var waiting = NewColumn(board, "waiting", "Waiting", 0, CardStatus.Blocked, active: false, terminal: false);
        var backlog = NewColumn(board, "backlog", "Backlog", 1, CardStatus.Backlog, active: false, terminal: false);
        var active = NewColumn(board, "doing", "Doing", 2, CardStatus.InProgress, active: true, terminal: false);
        var done = NewColumn(board, "done", "Done", 3, CardStatus.Done, active: false, terminal: true);
        board.Columns.Add(waiting);
        board.Columns.Add(backlog);
        board.Columns.Add(active);
        board.Columns.Add(done);

        // Backlog STATUS wins over column order — same rule as CardService.CreateAsync.
        TrackerLandingColumn.Resolve(board, TrackerImportColumn.Backlog)!.Id.ShouldBe(backlog.Id);
        TrackerLandingColumn.Resolve(board, TrackerImportColumn.Active)!.Id.ShouldBe(active.Id);

        board.Columns.Remove(backlog);
        TrackerLandingColumn.Resolve(board, TrackerImportColumn.Backlog)!.Id.ShouldBe(waiting.Id);
    }

    [Test]
    public void An_unknown_import_column_value_does_not_parse()
    {
        TrackerLandingColumn.TryParseMode(null, out var unset).ShouldBeTrue();
        unset.ShouldBe(TrackerImportColumn.Backlog);
        TrackerLandingColumn.TryParseMode("backlog", out var backlog).ShouldBeTrue();
        backlog.ShouldBe(TrackerImportColumn.Backlog);
        TrackerLandingColumn.TryParseMode(" ACTIVE ", out var active).ShouldBeTrue();
        active.ShouldBe(TrackerImportColumn.Active);
        TrackerLandingColumn.TryParseMode("in-progress", out _).ShouldBeFalse();
    }

    private static ExternalTrackerSyncService NewSut(AppDbContext db, FakeTracker tracker) =>
        new(db, [tracker], new MockEventBus(), NullLogger<ExternalTrackerSyncService>.Instance);
}
