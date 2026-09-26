using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0738 V-4 to V-11: closing or archiving a card settles the tasks bound to it.</summary>
[Category("Integration")]
public sealed class CardTaskSettlementTests
{
    [Test]
    public async Task closing_a_card_cancels_its_blocked_task_with_the_close_reason()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("CARD-0091", world.Backlog);
        var sessionId = await world.SeedSessionAsync();
        var task = await world.SeedTaskAsync(card.Id, AgentTaskStatus.Blocked, sessionId: sessionId);
        var stopper = new RecordingSessionStopper();

        await using var db = world.Open();
        var cards = world.Cards(db, stopper);
        var result = await cards.MoveAsync(
            card.Id,
            new MoveCardRequest(world.Done.Id, card.ConcurrencyToken, "Live data change; nothing to deploy."),
            CancellationToken.None);

        result.Card.Status.ShouldBe(CardStatus.Done);
        var row = await world.ReloadTaskAsync(task.Id);
        row.Status.ShouldBe(AgentTaskStatus.Canceled);
        row.FailureReason.ShouldBe("Card CARD-0091 closed (Done): Live data change; nothing to deploy.");
        result.TaskSettlement.ShouldNotBeNull();
        result.TaskSettlement.Canceled.ShouldBe([Short(task.Id)]);
        stopper.Killed.ShouldContain(sessionId);
        var canceled = await world.EventsAsync(task.Id, AgentTaskEventType.Canceled);
        canceled.ShouldHaveSingleItem().Detail.ShouldContain("CARD-0091");
    }

    [Test]
    public async Task closing_a_card_cancels_queued_and_unstarted_dispatched_tasks()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("CARD-0003", world.Backlog);
        var queued = await world.SeedTaskAsync(card.Id, AgentTaskStatus.Queued, createdAt: Utc(-10));
        var sessionId = await world.SeedSessionAsync();
        var dispatchedAt = Utc(-5);
        var dispatched = await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Dispatched, sessionId: sessionId, dispatchedAt: dispatchedAt, createdAt: Utc(-6));
        await world.SeedBriefAsync(sessionId, dispatched.Id, dispatchedAt, QueuedMessageStatus.Pending);

        await using var db = world.Open();
        var result = await world.Cards(db, new RecordingSessionStopper()).MoveAsync(
            card.Id,
            new MoveCardRequest(world.Done.Id, card.ConcurrencyToken, "finished"),
            CancellationToken.None);

        (await world.ReloadTaskAsync(queued.Id)).Status.ShouldBe(AgentTaskStatus.Canceled);
        (await world.ReloadTaskAsync(dispatched.Id)).Status.ShouldBe(AgentTaskStatus.Canceled);
        result.TaskSettlement.ShouldNotBeNull();
        result.TaskSettlement.Canceled.ShouldBe([Short(queued.Id), Short(dispatched.Id)], ignoreOrder: true);
    }

    [Test]
    public async Task closing_a_card_leaves_started_tasks_running_with_one_warning_event()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("CARD-0091", world.InProgress);
        var sentAt = Utc(-20);
        var sentSession = await world.SeedSessionAsync();
        var sent = await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Dispatched, sessionId: sentSession, dispatchedAt: sentAt, createdAt: Utc(-21));
        await world.SeedBriefAsync(sentSession, sent.Id, sentAt, QueuedMessageStatus.Sent);
        var workingSession = await world.SeedSessionAsync();
        var working = await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Working, sessionId: workingSession, dispatchedAt: Utc(-15), createdAt: Utc(-16));
        var stopper = new RecordingSessionStopper();

        await using var db = world.Open();
        var result = await world.Cards(db, stopper).MoveAsync(
            card.Id,
            new MoveCardRequest(world.Done.Id, card.ConcurrencyToken, "shipped"),
            CancellationToken.None);

        (await world.ReloadTaskAsync(sent.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await world.ReloadTaskAsync(working.Id)).Status.ShouldBe(AgentTaskStatus.Working);
        stopper.Killed.ShouldBeEmpty();
        foreach (var id in new[] { sent.Id, working.Id })
        {
            var warning = (await world.EventsAsync(id, AgentTaskEventType.Warning)).ShouldHaveSingleItem();
            warning.Detail.ShouldContain("CARD-0091");
            warning.Detail.ShouldContain("still working");
        }

        result.TaskSettlement.ShouldNotBeNull();
        result.TaskSettlement.LeftOpen.ShouldBe([Short(sent.Id), Short(working.Id)], ignoreOrder: true);
        result.TaskSettlement.Canceled.ShouldBeEmpty();
    }

    [Test]
    public async Task closing_a_card_touches_no_other_cards_tasks_and_no_settled_task()
    {
        await using var world = await World.CreateAsync();
        var closing = await world.SeedCardAsync("CARD-0010", world.Backlog);
        var other = await world.SeedCardAsync("CARD-0011", world.Backlog);
        var settled = await world.SeedTaskAsync(closing.Id, AgentTaskStatus.Succeeded, createdAt: Utc(-30));
        var outsider = await world.SeedTaskAsync(other.Id, AgentTaskStatus.Queued, createdAt: Utc(-3));
        var settledEvents = (await world.EventsAsync(settled.Id, type: null)).Count;
        var outsiderEvents = (await world.EventsAsync(outsider.Id, type: null)).Count;

        await using var db = world.Open();
        await world.Cards(db, new RecordingSessionStopper()).MoveAsync(
            closing.Id,
            new MoveCardRequest(world.Done.Id, closing.ConcurrencyToken, "done"),
            CancellationToken.None);

        (await world.ReloadTaskAsync(outsider.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        (await world.EventsAsync(outsider.Id, type: null)).Count.ShouldBe(outsiderEvents);
        var settledRow = await world.ReloadTaskAsync(settled.Id);
        settledRow.Status.ShouldBe(AgentTaskStatus.Succeeded);
        (await world.EventsAsync(settled.Id, type: null)).Count.ShouldBe(settledEvents);
    }

    [Test]
    public async Task archiving_a_card_cancels_its_queued_task_with_the_archive_reason()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("CARD-0001", world.Backlog);
        var task = await world.SeedTaskAsync(card.Id, AgentTaskStatus.Queued);

        await using var db = world.Open();
        var archived = await world.Cards(db, new RecordingSessionStopper()).ArchiveAsync(
            card.Id,
            new ArchiveCardRequest(card.ConcurrencyToken, "Duplicate of CARD-0002"),
            CancellationToken.None);

        archived.ArchivedAt.ShouldNotBeNull();
        var row = await world.ReloadTaskAsync(task.Id);
        row.Status.ShouldBe(AgentTaskStatus.Canceled);
        row.FailureReason.ShouldBe("Card CARD-0001 archived: Duplicate of CARD-0002");
    }

    [Test]
    public async Task a_move_to_review_settles_nothing_and_a_reopen_resurrects_nothing()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("CARD-0020", world.Backlog);
        var task = await world.SeedTaskAsync(card.Id, AgentTaskStatus.Queued);

        await using var db = world.Open();
        var cards = world.Cards(db, new RecordingSessionStopper());
        var reviewed = await cards.MoveAsync(
            card.Id,
            new MoveCardRequest(world.Review.Id, card.ConcurrencyToken, "ready for review"),
            CancellationToken.None);
        reviewed.TaskSettlement.ShouldBeNull();
        (await world.ReloadTaskAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Queued);

        var closed = await cards.MoveAsync(
            card.Id,
            new MoveCardRequest(world.Done.Id, reviewed.Card.ConcurrencyToken, "accepted"),
            CancellationToken.None);
        (await world.ReloadTaskAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Canceled);
        var eventsAfterClose = (await world.EventsAsync(task.Id, type: null)).Count;

        await cards.ReopenAsync(
            card.Id,
            new ReopenCardRequest(closed.Card.ConcurrencyToken, "look again"),
            CancellationToken.None);
        (await world.ReloadTaskAsync(task.Id)).Status.ShouldBe(AgentTaskStatus.Canceled);
        (await world.EventsAsync(task.Id, type: null)).Count.ShouldBe(eventsAfterClose);
    }

    [Test]
    public async Task a_task_settled_by_a_concurrent_writer_is_skipped_and_the_move_still_succeeds()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("CARD-0030", world.Backlog);
        var firstSession = await world.SeedSessionAsync();
        var first = await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Queued, sessionId: firstSession, createdAt: Utc(-8));
        var second = await world.SeedTaskAsync(card.Id, AgentTaskStatus.Queued, createdAt: Utc(-4));
        var stopper = new SucceedSecondOnKill(world.ConnectionString, firstSession, second.Id);

        await using var db = world.Open();
        var result = await world.Cards(db, stopper).MoveAsync(
            card.Id,
            new MoveCardRequest(world.Done.Id, card.ConcurrencyToken, "closing"),
            CancellationToken.None);

        (await world.ReloadTaskAsync(first.Id)).Status.ShouldBe(AgentTaskStatus.Canceled);
        result.Card.Status.ShouldBe(CardStatus.Done);
        (await world.ReloadTaskAsync(second.Id)).Status.ShouldBe(AgentTaskStatus.Succeeded);
        result.TaskSettlement.ShouldNotBeNull();
        result.TaskSettlement.Canceled.ShouldBe([Short(first.Id)]);
        result.TaskSettlement.LeftOpen.ShouldBeEmpty();
    }

    [Test]
    public async Task a_tracker_stale_close_cancels_the_cards_queued_task()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-c738-{Guid.NewGuid():N}");
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        Guid boardId;
        Guid cardId;
        Guid taskId;
        await using (var seed = Open(schema))
        {
            var graph = await SeedTrackedBoardAsync(seed, tempRoot);
            boardId = graph.Board.Id;
            var now = DateTime.UtcNow;
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = graph.Board.Id,
                BoardColumnId = graph.Backlog.Id,
                Identifier = "CARD-0001",
                Title = "Linked",
                Description = "",
                Status = CardStatus.Backlog,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            seed.Cards.Add(card);
            seed.ExternalIssueRefs.Add(new ExternalIssueRef
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                TrackerKind = TrackerKind.GitHubIssues,
                ExternalId = "acme/app#1",
                ExternalKey = "#1",
                Url = "https://github.test/acme/app/issues/1",
                RawPayloadJson = "{}",
                LastSyncedAt = now,
                LastKnownExternalState = "open",
            });
            var task = NewTask(card.Id, AgentTaskStatus.Queued, now);
            seed.AgentTasks.Add(task);
            await seed.SaveChangesAsync();
            cardId = card.Id;
            taskId = task.Id;
        }

        try
        {
            await using var db = Open(schema);
            var stopper = new RecordingSessionStopper();
            var tasks = TaskService(db, stopper);
            var settlement = new CardTaskSettlement(
                db, tasks, new MockEventBus(), TimeProvider.System, NullLogger<CardTaskSettlement>.Instance);
            var tracker = new EmptyIssueTracker(TrackerKind.GitHubIssues);
            var sync = new ExternalTrackerSyncService(
                db, [tracker], new MockEventBus(), NullLogger<ExternalTrackerSyncService>.Instance,
                taskSettlement: settlement);
            await sync.SyncAsync(DateTime.UtcNow, boardId, CancellationToken.None);

            await using var verify = Open(schema);
            var card = await verify.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId);
            card.Status.ShouldBe(CardStatus.Done);
            card.TerminalReason.ShouldBe("External tracker issue is no longer returned as active.");
            var task = await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            task.Status.ShouldBe(AgentTaskStatus.Canceled);
            task.FailureReason.ShouldBe(
                "Card CARD-0001 closed (Done): External tracker issue is no longer returned as active.");
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { /* best effort */ }
        }
    }

    private static string Short(Guid id) => DelegationReportFormatter.Short(id);

    private static DateTime Utc(int minutes) => DateTime.UtcNow.AddMinutes(minutes);

    private static AppDbContext Open(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static AgentTaskService TaskService(AppDbContext db, IDelegateSessionStopper stopper) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings()),
        new MockEventBus(),
        stopper,
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance);

    private static AgentTask NewTask(
        Guid cardId, AgentTaskStatus status, DateTime createdAt, Guid? sessionId = null, DateTime? dispatchedAt = null)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "bound",
            Goal = "bound",
            Role = AgentTaskRole.Code,
            CardId = cardId,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = status,
            AgentSessionId = sessionId,
            DispatchedAt = dispatchedAt,
            CreatedAt = createdAt,
        };
    }

    private sealed class SucceedSecondOnKill(string connectionString, Guid firstSessionId, Guid secondTaskId)
        : IDelegateSessionStopper
    {
        public async Task KillAsync(Guid sessionId, CancellationToken ct)
        {
            if (sessionId != firstSessionId)
                return;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
            var second = await db.AgentTasks.SingleAsync(t => t.Id == secondTaskId, ct);
            second.Status = AgentTaskStatus.Succeeded;
            second.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private sealed class EmptyIssueTracker(TrackerKind kind) : IIssueTracker
    {
        public TrackerKind Kind { get; } = kind;

        public Task<IReadOnlyList<TrackedIssue>> FetchCandidatesAsync(IssueTrackerConfig config, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TrackedIssue>>([]);

        public Task<IReadOnlyList<TrackedIssue>> FetchByStatesAsync(
            IssueTrackerConfig config, IReadOnlyList<string> states, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TrackedIssue>>([]);

        public Task<IReadOnlyList<TrackedIssue>> FetchByIdsAsync(
            IssueTrackerConfig config, IReadOnlyList<string> externalIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TrackedIssue>>([]);
    }

    private static async Task<TrackedGraph> SeedTrackedBoardAsync(AppDbContext db, string tempRoot)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"c738 {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
            BaseBranch = "main",
            CreatedAt = now,
            UpdatedAt = now,
        };
        Directory.CreateDirectory(project.LocalRepositoryPath);
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = "tracked",
            TrackerKind = TrackerKind.GitHubIssues,
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Project = project,
        };
        project.Boards.Add(board);
        var backlog = Column(board, "backlog", "Backlog", 0, CardStatus.Backlog, active: false, terminal: false, now);
        var active = Column(board, "in-progress", "In Progress", 1, CardStatus.InProgress, active: true, terminal: false, now);
        var done = Column(board, "done", "Done", 2, CardStatus.Done, active: false, terminal: true, now);
        board.Columns.Add(backlog);
        board.Columns.Add(active);
        board.Columns.Add(done);
        board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            Version = 1,
            Name = "Tracked",
            Content = """
                ---
                tracker:
                  kind: github_issues
                  repository: acme/app
                  active_states: [open]
                ---
                Work on {{ issue.identifier }}.
                """,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
        });
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return new TrackedGraph(board, backlog, done);
    }

    private static BoardColumn Column(
        Board board, string key, string name, int order, CardStatus status, bool active, bool terminal, DateTime now) =>
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
            Board = board,
        };

    private sealed record TrackedGraph(Board Board, BoardColumn Backlog, BoardColumn Done);

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        public BoardColumn Backlog { get; }
        public BoardColumn InProgress { get; }
        public BoardColumn Review { get; }
        public BoardColumn Done { get; }
        public string ConnectionString => _schema.ConnectionString;

        private World(
            IsolatedTestSchema schema, BoardColumn backlog, BoardColumn inProgress, BoardColumn review, BoardColumn done)
        {
            _schema = schema;
            Backlog = backlog;
            InProgress = inProgress;
            Review = review;
            Done = done;
        }

        public static async Task<World> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var db = Open(schema);
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"c738 {Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/c738.git",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "settlement",
                MaxConcurrentSessions = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var backlog = Column(board, "backlog", "Backlog", 0, CardStatus.Backlog, false, false, now);
            var inProgress = Column(board, "in-progress", "In Progress", 1, CardStatus.InProgress, true, false, now);
            var review = Column(board, "review", "Review", 2, CardStatus.Review, false, false, now);
            var done = Column(board, "done", "Done", 3, CardStatus.Done, false, true, now);
            board.Columns.Add(backlog);
            board.Columns.Add(inProgress);
            board.Columns.Add(review);
            board.Columns.Add(done);
            project.Boards.Add(board);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            return new World(schema, backlog, inProgress, review, done);
        }

        public AppDbContext Open() => CardTaskSettlementTests.Open(_schema);

        public CardService Cards(AppDbContext db, IDelegateSessionStopper stopper)
        {
            var tasks = TaskService(db, stopper);
            var settlement = new CardTaskSettlement(
                db, tasks, new MockEventBus(), TimeProvider.System, NullLogger<CardTaskSettlement>.Instance);
            return new CardService(
                db, null!, null!, null!, new MockEventBus(), TimeProvider.System, null!, taskSettlement: settlement);
        }

        public async Task<Card> SeedCardAsync(string identifier, BoardColumn column)
        {
            await using var db = Open();
            var now = DateTime.UtcNow;
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = column.BoardId,
                BoardColumnId = column.Id,
                Identifier = identifier,
                Title = identifier,
                Description = "",
                Status = column.CardStatus,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            return card;
        }

        public async Task<Guid> SeedSessionAsync()
        {
            await using var db = Open();
            var now = DateTime.UtcNow;
            var session = new AgentSession
            {
                Id = Guid.NewGuid(),
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            };
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            return session.Id;
        }

        public async Task<AgentTask> SeedTaskAsync(
            Guid cardId,
            AgentTaskStatus status,
            Guid? sessionId = null,
            DateTime? dispatchedAt = null,
            DateTime? createdAt = null)
        {
            await using var db = Open();
            var task = NewTask(cardId, status, createdAt ?? DateTime.UtcNow, sessionId, dispatchedAt);
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        public async Task SeedBriefAsync(Guid sessionId, Guid taskId, DateTime at, QueuedMessageStatus status)
        {
            await using var db = Open();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Body = $"{DelegationReportFormatter.TaskMarker(taskId)}\n\nDo the work.",
                Status = status,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Delegation,
                CreatedAt = at,
            });
            await db.SaveChangesAsync();
        }

        public async Task<AgentTask> ReloadTaskAsync(Guid id)
        {
            await using var db = Open();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        public async Task<List<AgentTaskEvent>> EventsAsync(Guid taskId, AgentTaskEventType? type)
        {
            await using var db = Open();
            var query = db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == taskId);
            if (type is { } kind)
                query = query.Where(e => e.Type == kind);
            return await query.ToListAsync();
        }

        public async ValueTask DisposeAsync() => await _schema.DisposeAsync();
    }
}
