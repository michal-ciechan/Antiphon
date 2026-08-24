using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0175 S1: imported cards get CARD-nnnn identifiers; tracker key stays on the ref.</summary>
[Category("Integration")]
[NotInParallel]
public class ExternalTrackerSyncIdentifierTests
{
    [Test]
    public async Task Imported_issue_gets_a_card_identifier_and_keeps_the_tracker_key_on_the_ref()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = await SeedTrackedBoardAsync(db, tempRoot, TrackerKind.GitHubIssues);
            var tracker = new FakeIssueTracker(TrackerKind.GitHubIssues, [
                Issue("acme/app#3", "#3", "Imported", "body")
            ]);
            var sut = NewSut(db, tracker);

            await sut.SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            var card = await verify.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == graph.Board.Id);
            card.Identifier.ShouldBe("CARD-0001");
            card.ExternalIssueRef.ShouldNotBeNull();
            card.ExternalIssueRef!.ExternalKey.ShouldBe("#3");
            Should.NotThrow(() => WorktreeManager.ValidateCardId(card.Identifier));
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task A_batch_of_imports_gets_distinct_consecutive_identifiers()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = await SeedTrackedBoardAsync(db, tempRoot, TrackerKind.GitHubIssues);
            var tracker = new FakeIssueTracker(TrackerKind.GitHubIssues, [
                Issue("acme/app#3", "#3", "One", "a"),
                Issue("acme/app#4", "#4", "Two", "b"),
                Issue("acme/app#5", "#5", "Three", "c")
            ]);
            var sut = NewSut(db, tracker);

            await sut.SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using (var verify = CreateContext())
            {
                var cards = await verify.Cards
                    .Include(c => c.ExternalIssueRef)
                    .Where(c => c.BoardId == graph.Board.Id)
                    .OrderBy(c => c.Identifier)
                    .ToListAsync();
                cards.Select(c => c.Identifier).ShouldBe(["CARD-0001", "CARD-0002", "CARD-0003"]);
                cards.Select(c => c.ExternalIssueRef!.ExternalKey).ShouldBe(["#3", "#4", "#5"]);
            }

            await sut.SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var again = CreateContext();
            var second = await again.Cards
                .Where(c => c.BoardId == graph.Board.Id)
                .Select(c => c.Identifier)
                .OrderBy(i => i)
                .ToListAsync();
            second.ShouldBe(["CARD-0001", "CARD-0002", "CARD-0003"]);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Hash_N_resolves_to_the_card_not_the_import()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = await SeedTrackedBoardAsync(db, tempRoot, TrackerKind.GitHubIssues);
            var n = await NextUnusedNumberAsync();
            var manual = NewCard(graph, $"CARD-{n:0000}", graph.Backlog);
            db.Cards.Add(manual);
            await db.SaveChangesAsync();

            var tracker = new FakeIssueTracker(TrackerKind.GitHubIssues, [
                Issue($"acme/app#{n}", $"#{n}", "Imported twin", "body")
            ]);
            var sut = NewSut(db, tracker);
            await sut.SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            var cards = NewCardService(db);
            var resolved = await cards.ResolveCardIdAsync($"#{n}", CancellationToken.None);
            resolved.ShouldBe(manual.Id);

            await using var verify = CreateContext();
            var imported = await verify.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == graph.Board.Id && c.Id != manual.Id);
            imported.ExternalIssueRef!.ExternalKey.ShouldBe($"#{n}");
            imported.Identifier.ShouldNotBe($"#{n}");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Foreign_key_resolves_through_the_external_ref()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var graph = await SeedTrackedBoardAsync(db, tempRoot, TrackerKind.Linear);
            var key = await NextUnusedForeignKeyAsync();
            var tracker = new FakeIssueTracker(TrackerKind.Linear, [
                Issue($"lin-{key}", key, "Linear import", "body")
            ]);
            var sut = NewSut(db, tracker);
            await sut.SyncAsync(DateTime.UtcNow, graph.Board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            var card = await verify.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == graph.Board.Id);
            card.Identifier.ShouldStartWith("CARD-");
            card.ExternalIssueRef!.ExternalKey.ShouldBe(key);

            var cards = NewCardService(verify);
            (await cards.ResolveCardIdAsync(key, CancellationToken.None)).ShouldBe(card.Id);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task A_lost_identifier_race_is_reported_with_the_constraint_name_and_retried_next_tick()
    {
        var tempRoot = NewTempRoot();
        var logs = new List<string>();
        try
        {
            Guid boardId;
            Guid columnId;
            await using (var seed = CreateContext())
            {
                var graph = await SeedTrackedBoardAsync(seed, tempRoot, TrackerKind.GitHubIssues);
                boardId = graph.Board.Id;
                columnId = graph.Backlog.Id;
            }

            var interceptor = new CollideOnFirstCardInsertInterceptor(boardId, columnId);
            await using var db = new AppDbContext(CreateInterceptedOptions(interceptor));
            var tracker = new FakeIssueTracker(TrackerKind.GitHubIssues, [
                Issue("acme/app#3", "#3", "Racy import", "body")
            ]);
            var sut = new ExternalTrackerSyncService(
                db, [tracker], new MockEventBus(), new ListLogger<ExternalTrackerSyncService>(logs));

            await sut.SyncAsync(DateTime.UtcNow, boardId, CancellationToken.None);

            logs.ShouldContain(l => l.Contains("IX_Cards_BoardId_Identifier", StringComparison.Ordinal));
            await using (var afterFail = CreateContext())
            {
                (await afterFail.Cards.CountAsync(c => c.BoardId == boardId && c.ExternalIssueRef != null))
                    .ShouldBe(0);
            }

            await using var retryDb = CreateContext();
            var retry = NewSut(retryDb, tracker);
            await retry.SyncAsync(DateTime.UtcNow, boardId, CancellationToken.None);

            await using var verify = CreateContext();
            var imported = await verify.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == boardId && c.ExternalIssueRef != null);
            imported.Identifier.ShouldBe("CARD-0002");
            imported.ExternalIssueRef!.ExternalKey.ShouldBe("#3");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    private static DbContextOptions<AppDbContext> CreateInterceptedOptions(SaveChangesInterceptor interceptor) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            })
            .AddInterceptors(interceptor)
            .Options;

    private static ExternalTrackerSyncService NewSut(AppDbContext db, IIssueTracker tracker) =>
        new(db, [tracker], new MockEventBus(), NullLogger<ExternalTrackerSyncService>.Instance);

    private static CardService NewCardService(AppDbContext db) =>
        new(db, null!, null!, null!, new MockEventBus(), TimeProvider.System, null!);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-id-sync-{Guid.NewGuid():N}");

    private static async Task<Graph> SeedTrackedBoardAsync(AppDbContext db, string tempRoot, TrackerKind kind)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"IdSync Project {Guid.NewGuid():N}",
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
            Name = $"IdSync Board {Guid.NewGuid():N}",
            TrackerKind = kind,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        var backlog = Col(board, "backlog", "Backlog", 0, CardStatus.Backlog, active: false, terminal: false, now);
        var active = Col(board, "in-progress", "In Progress", 1, CardStatus.InProgress, active: true, terminal: false, now);
        var done = Col(board, "done", "Done", 2, CardStatus.Done, active: false, terminal: true, now);
        board.Columns.Add(backlog);
        board.Columns.Add(active);
        board.Columns.Add(done);

        var yaml = kind == TrackerKind.GitHubIssues
            ? """
                ---
                tracker:
                  kind: github_issues
                  repository: acme/app
                  active_states: [open]
                ---
                Work on {{ issue.identifier }}.
                """
            : """
                ---
                tracker:
                  kind: linear
                  project: Antiphon
                  active_states: [open]
                ---
                Work on {{ issue.identifier }}.
                """;
        board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Tracked",
            Content = yaml,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        });

        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return new Graph(project, board, backlog, active, done);
    }

    private static Card NewCard(Graph graph, string identifier, BoardColumn column)
    {
        var now = DateTime.UtcNow;
        return new Card
        {
            Id = Guid.NewGuid(),
            BoardId = graph.Board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = "Manual card",
            Description = "",
            Status = column.CardStatus,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
            Board = graph.Board,
            BoardColumn = column
        };
    }

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

    private static async Task<int> NextUnusedNumberAsync()
    {
        await using var db = CreateContext();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var n = Random.Shared.Next(4_000, 9_999);
            var identifier = $"CARD-{n:0000}";
            var hash = $"#{n}";
            if (!await db.Cards.AnyAsync(c => c.Identifier == identifier)
                && !await db.ExternalIssueRefs.AnyAsync(r => r.ExternalKey == hash))
                return n;
        }

        throw new InvalidOperationException("No unused CARD-nnnn available.");
    }

    private static async Task<string> NextUnusedForeignKeyAsync()
    {
        await using var db = CreateContext();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = $"ANT-{Random.Shared.Next(1_000, 9_999)}";
            if (!await db.Cards.AnyAsync(c => c.Identifier == candidate)
                && !await db.ExternalIssueRefs.AnyAsync(r => r.ExternalKey == candidate))
                return candidate;
        }

        throw new InvalidOperationException("No unused foreign key available.");
    }

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
        await db.RetrySchedules.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
        await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
        await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
        await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { /* best effort */ }
    }

    private sealed record Graph(
        Project Project,
        Board Board,
        BoardColumn Backlog,
        BoardColumn Active,
        BoardColumn Done);

    private sealed class FakeIssueTracker(TrackerKind kind, IReadOnlyList<TrackedIssue> issues) : IIssueTracker
    {
        public TrackerKind Kind { get; } = kind;
        public IReadOnlyList<TrackedIssue> Candidates { get; } = issues;

        public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
            IssueTrackerConfig config, CancellationToken ct) =>
            Task.FromResult(Candidates);

        public Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
            IssueTrackerConfig config, IReadOnlyList<string> states, CancellationToken ct) =>
            Task.FromResult(Candidates);

        public Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
            IssueTrackerConfig config, IReadOnlyList<string> externalIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TrackedIssue>>([]);
    }

    private sealed class CollideOnFirstCardInsertInterceptor(Guid boardId, Guid columnId) : SaveChangesInterceptor
    {
        private int _armed = 1;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is null)
                return result;
            var addingCard = eventData.Context.ChangeTracker.Entries<Card>()
                .Any(e => e.State == EntityState.Added);
            if (!addingCard)
                return result;
            if (Interlocked.Exchange(ref _armed, 0) == 0)
                return result;

            var now = DateTime.UtcNow;
            await using var other = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            other.Cards.Add(new Card
            {
                Id = Guid.NewGuid(),
                BoardId = boardId,
                BoardColumnId = columnId,
                Identifier = "CARD-0001",
                Title = "colliding create",
                Description = "",
                Status = CardStatus.Backlog,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now
            });
            await other.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (sink)
                sink.Add($"[{logLevel}] {formatter(state, exception)}");
        }
    }
}
