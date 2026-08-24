using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0170 S2: imports land in Backlog; tracker owns only the terminal boundary by default.</summary>
[Category("Integration")]
[NotInParallel]
public class ExternalTrackerSyncLandingColumnTests
{
    [Test]
    public async Task Default_import_lands_in_the_backlog_column()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = await SeedTrackedBoardAsync(db, tempRoot);
            var tracker = new FakeIssueTracker(TrackerKind.GitHubIssues, [
                Issue("acme/app#3", "#3", "Imported", "body")
            ]);
            var sut = NewSut(db, tracker);
            await sut.SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.BoardId == graph.Board.Id);
            card.BoardColumnId.ShouldBe(graph.Backlog.Id);
            card.Status.ShouldBe(CardStatus.Backlog);
            card.StartedAt.ShouldBeNull();
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
            var graph = await SeedTrackedBoardAsync(db, tempRoot);
            var tracker = new FakeIssueTracker(TrackerKind.GitHubIssues, [
                Issue("acme/app#3", "#3", "Imported", "body")
            ]);
            await NewSut(db, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            Guid cardId;
            await using (var move = CreateContext())
            {
                var card = await move.Cards.SingleAsync(c => c.BoardId == graph.Board.Id);
                cardId = card.Id;
                card.BoardColumnId = graph.Active.Id;
                card.Status = CardStatus.InProgress;
                await move.SaveChangesAsync();
            }

            await using (var sync2 = CreateContext())
                await NewSut(sync2, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using (var move2 = CreateContext())
            {
                var card = await move2.Cards.SingleAsync(c => c.Id == cardId);
                card.BoardColumnId = graph.Review.Id;
                card.Status = CardStatus.Review;
                await move2.SaveChangesAsync();
            }

            await using (var sync3 = CreateContext())
                await NewSut(sync3, tracker).SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            var after = await verify.Cards
                .Include(c => c.Revisions)
                .SingleAsync(c => c.Id == cardId);
            after.BoardColumnId.ShouldBe(graph.Review.Id);
            after.Revisions.Count(r => r.EditedBy == "external-tracker").ShouldBe(0);
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
            var graph = await SeedTrackedBoardAsync(db, tempRoot);
            var open = Issue("acme/app#9", "#9", "Will close", "body");
            var tracker = new FakeIssueTracker(TrackerKind.GitHubIssues, [open]);
            var sut = NewSut(db, tracker);
            await sut.SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            tracker.Candidates = [];
            tracker.LookupIssues =
            [
                new TrackedIssue(
                    "acme/app#9",
                    "#9",
                    "Will close",
                    "body",
                    "closed",
                    0,
                    [],
                    [],
                    "https://github.test/acme/app/issues/9",
                    "{}")
            ];
            await sut.SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.BoardId == graph.Board.Id);
            card.BoardColumnId.ShouldBe(graph.Done.Id);
            card.Status.ShouldBe(CardStatus.Done);
            card.TerminalReason.ShouldNotBeNull();
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
            var graph = await SeedTwoColumnBoardAsync(db, tempRoot);
            var tracker = new FakeIssueTracker(TrackerKind.GitHubIssues, [
                Issue("acme/app#3", "#3", "Imported", "body")
            ]);
            var sut = NewSut(db, tracker);
            await sut.SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            var card = await verify.Cards.SingleAsync(c => c.BoardId == graph.Board.Id);
            card.BoardColumnId.ShouldBe(graph.Todo.Id);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public void Resolve_prefers_backlog_then_waiting_then_first_column()
    {
        var board = new Board { Id = Guid.NewGuid() };
        var todo = Col(board, "todo", "Todo", 0, CardStatus.InProgress, active: true, terminal: false, DateTime.UtcNow);
        var done = Col(board, "done", "Done", 1, CardStatus.Done, active: false, terminal: true, DateTime.UtcNow);
        board.Columns.Add(todo);
        board.Columns.Add(done);

        var config = new IssueTrackerConfig(
            TrackerKind.GitHubIssues,
            "https://api.github.com",
            null,
            "acme/app",
            ["open"],
            null,
            null,
            new Dictionary<string, string>());

        TrackerLandingColumn.Resolve(board, config)!.Id.ShouldBe(todo.Id);
    }

    private static ExternalTrackerSyncService NewSut(AppDbContext db, IIssueTracker tracker) =>
        new(db, [tracker], new MockEventBus(), NullLogger<ExternalTrackerSyncService>.Instance);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-land-sync-{Guid.NewGuid():N}");

    private static async Task<DefaultGraph> SeedTrackedBoardAsync(AppDbContext db, string tempRoot)
    {
        var now = DateTime.UtcNow;
        var project = NewProject(tempRoot, now);
        var board = NewBoard(project, now, TrackerKind.GitHubIssues);
        var backlog = Col(board, "backlog", "Backlog", 0, CardStatus.Backlog, active: false, terminal: false, now);
        var active = Col(board, "in-progress", "In Progress", 1, CardStatus.InProgress, active: true, terminal: false, now);
        var review = Col(board, "review", "Review", 2, CardStatus.Review, active: false, terminal: false, now);
        var done = Col(board, "done", "Done", 3, CardStatus.Done, active: false, terminal: true, now);
        board.Columns.Add(backlog);
        board.Columns.Add(active);
        board.Columns.Add(review);
        board.Columns.Add(done);
        board.WorkflowDefinitions.Add(Workflow(board, now, """
            ---
            tracker:
              kind: github_issues
              repository: acme/app
              active_states: [open]
            ---
            Work on {{ issue.identifier }}.
            """));
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return new DefaultGraph(project, board, backlog, active, review, done);
    }

    private static async Task<TwoColumnGraph> SeedTwoColumnBoardAsync(AppDbContext db, string tempRoot)
    {
        var now = DateTime.UtcNow;
        var project = NewProject(tempRoot, now);
        var board = NewBoard(project, now, TrackerKind.GitHubIssues);
        var todo = Col(board, "todo", "Todo", 0, CardStatus.InProgress, active: true, terminal: false, now);
        var done = Col(board, "done", "Done", 1, CardStatus.Done, active: false, terminal: true, now);
        board.Columns.Add(todo);
        board.Columns.Add(done);
        board.WorkflowDefinitions.Add(Workflow(board, now, """
            ---
            tracker:
              kind: github_issues
              repository: acme/app
              active_states: [open]
            ---
            Work on {{ issue.identifier }}.
            """));
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return new TwoColumnGraph(project, board, todo, done);
    }

    private static Project NewProject(string tempRoot, DateTime now)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"LandSync Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath!);
        return project;
    }

    private static Board NewBoard(Project project, DateTime now, TrackerKind kind)
    {
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"LandSync Board {Guid.NewGuid():N}",
            TrackerKind = kind,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);
        return board;
    }

    private static BoardWorkflowDefinition Workflow(Board board, DateTime now, string content) =>
        new()
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Tracked",
            Content = content,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
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

    private static TrackedIssue Issue(string externalId, string key, string title, string body) =>
        new(externalId, key, title, body, "open", 0, [], [],
            $"https://github.test/{externalId.Replace('#', '/')}", "{}");

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
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.RetrySchedules.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { /* best effort */ }
    }

    private sealed record DefaultGraph(
        Project Project, Board Board, BoardColumn Backlog, BoardColumn Active, BoardColumn Review, BoardColumn Done);

    private sealed record TwoColumnGraph(Project Project, Board Board, BoardColumn Todo, BoardColumn Done);

    private sealed class FakeIssueTracker(TrackerKind kind, IReadOnlyList<TrackedIssue> issues) : IIssueTracker
    {
        public TrackerKind Kind { get; } = kind;
        public IReadOnlyList<TrackedIssue> Candidates { get; set; } = issues;
        public IReadOnlyList<TrackedIssue> LookupIssues { get; set; } = issues;

        public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
            IssueTrackerConfig config, CancellationToken ct) =>
            Task.FromResult(Candidates);

        public Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
            IssueTrackerConfig config, IReadOnlyList<string> states, CancellationToken ct) =>
            Task.FromResult(Candidates);

        public Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
            IssueTrackerConfig config, IReadOnlyList<string> externalIds, CancellationToken ct)
        {
            var requested = externalIds.ToHashSet(StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<TrackedIssue>>(
                LookupIssues.Where(i => requested.Contains(i.ExternalId)).ToList());
        }
    }
}
