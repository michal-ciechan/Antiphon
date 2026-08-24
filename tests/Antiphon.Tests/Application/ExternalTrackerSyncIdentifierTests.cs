using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0175: every card gets a <c>CARD-nnnn</c> identifier, including one a tracker sync
/// imported; the tracker's own key lives only on <c>ExternalIssueRef.ExternalKey</c>.
/// </summary>
/// <remarks>
/// Before this, an imported card's <c>Identifier</c> WAS the tracker key ("#3"), which
/// <c>WorktreeManager.ValidateCardId</c> rejects — so eleven live cards could not start an agent
/// at all, and <c>#5</c> resolved ambiguously against the manual <c>CARD-0005</c> on the same
/// board. Every assertion here is scoped to the board the test made: the assembly shares one
/// Postgres (AGENTS.md) and other suites are writing cards throughout.
/// </remarks>
[Category("Integration")]
public class ExternalTrackerSyncIdentifierTests
{
    [Test]
    public async Task Imported_issue_gets_a_card_identifier_and_keeps_the_tracker_key_on_the_ref()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var ns = NewNamespace();
            var board = await SeedBoardAsync(db, tempRoot);
            var tracker = new FakeTracker([Issue(Ext(ns, 3), "#3", "Imported issue")]);

            var synced = await NewSut(db, tracker).SyncAsync(DateTime.UtcNow, board.Id, CancellationToken.None);

            synced.ShouldBe(1);
            await using var verify = CreateContext();
            var card = await verify.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == board.Id);
            card.Identifier.ShouldBe("CARD-0001");
            card.ExternalIssueRef!.ExternalKey.ShouldBe("#3");
            card.ExternalIssueRef.Origin.ShouldBe(ExternalIssueOrigin.ExternalImport);
            // The cross-boundary contract that was missing: an imported identifier must be
            // something the worktree layer will actually accept, or the card cannot launch.
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
            var ns = NewNamespace();
            var board = await SeedBoardAsync(db, tempRoot);
            var tracker = new FakeTracker([
                Issue(Ext(ns, 3), "#3", "First"),
                Issue(Ext(ns, 4), "#4", "Second"),
                Issue(Ext(ns, 5), "#5", "Third")
            ]);

            await NewSut(db, tracker).SyncAsync(DateTime.UtcNow, board.Id, CancellationToken.None);

            await using var afterFirst = CreateContext();
            var identifiers = await afterFirst.Cards
                .Where(c => c.BoardId == board.Id)
                .OrderBy(c => c.Identifier)
                .Select(c => c.Identifier)
                .ToListAsync();
            identifiers.ShouldBe(["CARD-0001", "CARD-0002", "CARD-0003"]);

            // The re-assertion at UpdateExisting is gone: a second sync must not rename anything
            // back to "#N". This is what made a one-off rename of the live cards revert on the
            // next 30-minute tick.
            await using var second = CreateContext();
            await NewSut(second, tracker).SyncAsync(DateTime.UtcNow, board.Id, CancellationToken.None);

            await using var afterSecond = CreateContext();
            var stillTheSame = await afterSecond.Cards
                .Where(c => c.BoardId == board.Id)
                .OrderBy(c => c.Identifier)
                .Select(c => c.Identifier)
                .ToListAsync();
            stillTheSame.ShouldBe(identifiers);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task A_lost_identifier_race_is_reported_with_the_constraint_name_and_retried_next_tick()
    {
        await using var db = CreateContext();
        var tempRoot = NewTempRoot();
        try
        {
            var ns = NewNamespace();
            var board = await SeedBoardAsync(db, tempRoot);
            // A pre-existing linked card puts the allocator's starting point at 1 AND gives the
            // stale-issue reconcile pass something to look up — which is the only hook that runs
            // after UpsertIssuesAsync has allocated and before SyncAsync saves.
            await SeedLinkedCardAsync(db, board, "CARD-0001", Ext(ns, 99), "#99");

            var logs = new List<string>();
            var tracker = new FakeTracker([Issue(Ext(ns, 3), "#3", "Imported issue")])
            {
                LookupIssues = [Issue(Ext(ns, 99), "#99", "Still open")]
            };
            tracker.OnFetchByIds = async () =>
            {
                // The race: a manual create wins CARD-0002 while this sync is mid-pass.
                await using var racer = CreateContext();
                racer.Cards.Add(NewCard(board, "CARD-0002", board.Columns.First().Id));
                await racer.SaveChangesAsync();
            };

            var synced = await NewSut(db, tracker, logs).SyncAsync(
                DateTime.UtcNow, board.Id, CancellationToken.None);

            synced.ShouldBe(1);
            // Never report a DB failure without the DB's own message (AGENTS.md).
            logs.ShouldContain(l => l.Contains("IX_Cards_BoardId_Identifier"), customMessage: string.Join(" | ", logs));
            logs.ShouldContain(l => l.StartsWith("[Warning]"));

            await using var afterFailure = CreateContext();
            (await afterFailure.ExternalIssueRefs.CountAsync(r => r.Card.BoardId == board.Id && r.ExternalId == Ext(ns, 3)))
                .ShouldBe(0);

            // The next tick re-reads and re-allocates around the number that was taken.
            await using var nextTick = CreateContext();
            await NewSut(nextTick, new FakeTracker([Issue(Ext(ns, 3), "#3", "Imported issue")]))
                .SyncAsync(DateTime.UtcNow, board.Id, CancellationToken.None);

            await using var verify = CreateContext();
            var externalId = Ext(ns, 3);
            var imported = await verify.Cards
                .Include(c => c.ExternalIssueRef)
                .SingleAsync(c => c.BoardId == board.Id && c.ExternalIssueRef!.ExternalId == externalId);
            imported.Identifier.ShouldBe("CARD-0003");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    private static ExternalTrackerSyncService NewSut(
        AppDbContext db,
        FakeTracker tracker,
        List<string>? logs = null) =>
        new(db,
            [tracker],
            new MockEventBus(),
            logs is null
                ? NullLogger<ExternalTrackerSyncService>.Instance
                : new ListLogger<ExternalTrackerSyncService>(logs));

    internal static async Task<Board> SeedBoardAsync(
        AppDbContext db,
        string tempRoot,
        string workflowYaml = DefaultWorkflow,
        bool defaultColumns = true)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Tracker Ident Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);

        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Tracker Ident Board {Guid.NewGuid():N}",
            TrackerKind = TrackerKind.GitHubIssues,
            TrackerActivatedAt = now,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);

        if (defaultColumns)
        {
            board.Columns.Add(NewColumn(board, "backlog", "Backlog", 0, CardStatus.Backlog, false, false));
            board.Columns.Add(NewColumn(board, "in_progress", "In Progress", 1, CardStatus.InProgress, true, false));
            board.Columns.Add(NewColumn(board, "review", "Review", 2, CardStatus.Review, false, false));
            board.Columns.Add(NewColumn(board, "done", "Done", 3, CardStatus.Done, false, true));
        }
        else
        {
            board.Columns.Add(NewColumn(board, "todo", "Todo", 0, CardStatus.InProgress, true, false));
            board.Columns.Add(NewColumn(board, "done", "Done", 1, CardStatus.Done, false, true));
        }

        board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Tracked",
            Content = workflowYaml,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        });

        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return board;
    }

    internal const string DefaultWorkflow = """
        ---
        tracker:
          kind: github_issues
          repository: acme/app
          active_states: [open]
        ---
        Work on {{ issue.identifier }}.
        """;

    internal static Card NewCard(Board board, string identifier, Guid columnId, DateTime? createdAt = null)
    {
        var now = createdAt ?? DateTime.UtcNow;
        return new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = columnId,
            Identifier = identifier,
            Title = $"Card {identifier}",
            Description = "seeded",
            Priority = 0,
            LabelsJson = "[]",
            Status = board.Columns.Single(c => c.Id == columnId).CardStatus,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    internal static async Task<Card> SeedLinkedCardAsync(
        AppDbContext db,
        Board board,
        string identifier,
        string externalId,
        string externalKey,
        Guid? columnId = null)
    {
        var card = NewCard(board, identifier, columnId ?? board.Columns.OrderBy(c => c.ColumnOrder).First().Id);
        var issueRef = new ExternalIssueRef
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            TrackerKind = TrackerKind.GitHubIssues,
            ExternalId = externalId,
            ExternalKey = externalKey,
            Url = $"https://github.test/{externalId.Replace('#', '/')}",
            RawPayloadJson = "{}",
            LastSyncedAt = DateTime.UtcNow,
            Origin = ExternalIssueOrigin.ExternalImport,
            LastKnownExternalState = "open",
            LastRevisionSynced = 0
        };
        db.Cards.Add(card);
        db.ExternalIssueRefs.Add(issueRef);
        await db.SaveChangesAsync();
        return card;
    }

    internal static BoardColumn NewColumn(
        Board board, string key, string name, int order, CardStatus status, bool active, bool terminal)
    {
        var now = DateTime.UtcNow;
        return new BoardColumn
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
    }

    /// <summary>
    /// A tracker id nothing else in the shared database uses. <c>UpsertIssuesAsync</c> looks up
    /// existing refs by <c>ExternalId</c> across ALL boards (deliberately — that is how it detects
    /// an issue already linked elsewhere), so two tests reusing "acme/app#3" make each other's
    /// imports look like a cross-board link and silently skip the create. AGENTS.md: scope every
    /// assertion, and every fixture, to the rows the test made.
    /// </summary>
    internal static string Ext(string ns, int number) => $"acme/{ns}#{number}";

    internal static string NewNamespace() => Guid.NewGuid().ToString("N")[..12];

    internal static TrackedIssue Issue(
        string externalId,
        string externalKey,
        string title,
        string state = "open",
        IReadOnlyList<string>? blockedBy = null) =>
        new(externalId, externalKey, title, "body", state, 0, [], blockedBy ?? [],
            $"https://github.test/{externalId.Replace('#', '/')}", "{}");

    internal static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    internal static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-tracker-ident-{Guid.NewGuid():N}");

    internal static async Task CleanupAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var projectIds = await db.Projects
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath.StartsWith(tempRoot))
            .Select(p => p.Id)
            .ToListAsync();
        if (projectIds.Count > 0)
        {
            var boardIds = await db.Boards.Where(b => projectIds.Contains(b.ProjectId)).Select(b => b.Id).ToListAsync();
            var cardIds = await db.Cards.Where(c => boardIds.Contains(c.BoardId)).Select(c => c.Id).ToListAsync();
            await db.ExternalIssueRefs.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
            await db.RetrySchedules.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
            await db.CardRevisions.Where(r => cardIds.Contains(r.CardId)).ExecuteDeleteAsync();
            await db.Cards.Where(c => cardIds.Contains(c.Id)).ExecuteDeleteAsync();
            await db.BoardWorkflowDefinitions.Where(d => boardIds.Contains(d.BoardId)).ExecuteDeleteAsync();
            await db.BoardColumns.Where(c => boardIds.Contains(c.BoardId)).ExecuteDeleteAsync();
            await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync();
            await db.Projects.Where(p => projectIds.Contains(p.Id)).ExecuteDeleteAsync();
        }

        try
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the DB rows are what the next test can trip over.
        }
    }

    internal sealed class FakeTracker(IReadOnlyList<TrackedIssue> issues) : IIssueTracker
    {
        public TrackerKind Kind => TrackerKind.GitHubIssues;

        public IReadOnlyList<TrackedIssue> Candidates { get; set; } = issues;

        public IReadOnlyList<TrackedIssue> LookupIssues { get; set; } = issues;

        /// <summary>Fires once, on the first id lookup — the window between allocate and save.</summary>
        public Func<Task>? OnFetchByIds { get; set; }

        public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(
            IssueTrackerConfig config, CancellationToken ct) => Task.FromResult(Candidates);

        public Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
            IssueTrackerConfig config, IReadOnlyList<string> states, CancellationToken ct) =>
            Task.FromResult(Candidates);

        public async Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
            IssueTrackerConfig config, IReadOnlyList<string> externalIds, CancellationToken ct)
        {
            if (OnFetchByIds is { } hook)
            {
                OnFetchByIds = null;
                await hook();
            }

            var requested = externalIds.ToHashSet(StringComparer.Ordinal);
            return LookupIssues.Where(i => requested.Contains(i.ExternalId)).ToList();
        }
    }

    internal sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Add($"[{logLevel}] {formatter(state, exception)} {exception?.InnerException?.Message}");
    }
}
