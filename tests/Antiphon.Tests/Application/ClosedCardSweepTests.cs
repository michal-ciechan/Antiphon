using Antiphon.Server.Application.Dtos;
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

/// <summary>CARD-0738 V-17 and V-18: the closed-card sweep lists, then cancels only what the hook would.</summary>
[Category("Integration")]
public sealed class ClosedCardSweepTests
{
    [Test]
    public async Task the_sweep_lists_open_tasks_on_closed_and_archived_cards_and_changes_nothing()
    {
        await using var world = await World.CreateAsync();
        var seeded = await world.SeedAsync();
        var before = await world.StatusesAsync(seeded.All);

        await using var db = world.Connect();
        var sweep = await world.Settlement(db, new RecordingSessionStopper()).SweepAsync(apply: false, CancellationToken.None);

        sweep.Applied.ShouldBeFalse();
        var rows = sweep.Rows.Where(r => seeded.Listed.Contains(r.TaskId)).ToList();
        rows.Count.ShouldBe(4);
        rows.ShouldAllBe(r => r.Action == ClosedCardSweepAction.Listed);
        rows.Single(r => r.TaskId == seeded.AfterClose).CreatedAfterClose.ShouldBeTrue();
        rows.Single(r => r.TaskId == seeded.Started).Started.ShouldBeTrue();
        sweep.Rows.ShouldNotContain(r => r.TaskId == seeded.Control);
        (await world.StatusesAsync(seeded.All)).ShouldBe(before);
    }

    [Test]
    public async Task apply_cancels_only_unstarted_rows_created_before_the_close()
    {
        await using var world = await World.CreateAsync();
        var seeded = await world.SeedAsync();
        var stopper = new RecordingSessionStopper();

        await using var db = world.Connect();
        var sweep = await world.Settlement(db, stopper).SweepAsync(apply: true, CancellationToken.None);

        sweep.CanceledCount.ShouldBe(2);
        sweep.LeftOpenCount.ShouldBe(2);
        var rows = sweep.Rows.Where(r => seeded.Listed.Contains(r.TaskId)).ToList();
        rows.Single(r => r.TaskId == seeded.Queued).Action.ShouldBe(ClosedCardSweepAction.Canceled);
        rows.Single(r => r.TaskId == seeded.Blocked).Action.ShouldBe(ClosedCardSweepAction.Canceled);
        rows.Single(r => r.TaskId == seeded.Started).Action.ShouldBe(ClosedCardSweepAction.LeftOpenStarted);
        rows.Single(r => r.TaskId == seeded.AfterClose).Action.ShouldBe(ClosedCardSweepAction.LeftOpenCreatedAfterClose);

        var queued = await world.ReloadAsync(seeded.Queued);
        var blocked = await world.ReloadAsync(seeded.Blocked);
        queued.Status.ShouldBe(AgentTaskStatus.Canceled);
        blocked.Status.ShouldBe(AgentTaskStatus.Canceled);
        queued.FailureReason.ShouldStartWith("Closed-card sweep: Card");
        blocked.FailureReason.ShouldStartWith("Closed-card sweep: Card");
        (await world.ReloadAsync(seeded.Started)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await world.ReloadAsync(seeded.AfterClose)).Status.ShouldBe(AgentTaskStatus.Queued);
        stopper.Killed.ShouldNotContain(seeded.StartedSession);
    }

    private sealed record Seed(
        Guid Queued,
        Guid Blocked,
        Guid Started,
        Guid AfterClose,
        Guid Control,
        Guid StartedSession)
    {
        public Guid[] Listed => [Queued, Blocked, Started, AfterClose];
        public Guid[] All => [Queued, Blocked, Started, AfterClose, Control];
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly BoardColumn _backlog;
        private readonly BoardColumn _inProgress;
        private readonly BoardColumn _done;

        private World(IsolatedTestSchema schema, BoardColumn backlog, BoardColumn inProgress, BoardColumn done)
        {
            _schema = schema;
            _backlog = backlog;
            _inProgress = inProgress;
            _done = done;
        }

        public static async Task<World> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var db = Connect(schema);
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"c738-sweep {Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/c738.git",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "sweep",
                MaxConcurrentSessions = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
            var backlog = Column(board, "backlog", 0, CardStatus.Backlog, false, false, now);
            var inProgress = Column(board, "in-progress", 1, CardStatus.InProgress, true, false, now);
            var done = Column(board, "done", 2, CardStatus.Done, false, true, now);
            board.Columns.Add(backlog);
            board.Columns.Add(inProgress);
            board.Columns.Add(done);
            project.Boards.Add(board);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            return new World(schema, backlog, inProgress, done);
        }

        public AppDbContext Connect() => Connect(_schema);

        public CardTaskSettlement Settlement(AppDbContext db, IDelegateSessionStopper stopper) => new(
            db,
            new AgentTaskService(
                db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new DelegationSettings()),
                new MockEventBus(),
                stopper,
                TimeProvider.System,
                NullLogger<AgentTaskService>.Instance),
            new MockEventBus(),
            TimeProvider.System,
            NullLogger<CardTaskSettlement>.Instance);

        public async Task<Seed> SeedAsync()
        {
            var now = Second(DateTime.UtcNow);
            var doneCard = await SeedCardAsync("CARD-0100", _done, completedAt: now.AddMinutes(-10), terminalReason: "shipped");
            var archived = await SeedCardAsync(
                "CARD-0101", _backlog, archivedAt: now.AddMinutes(-8), archivedReason: "duplicate");
            var later = await SeedCardAsync("CARD-0102", _done, completedAt: now.AddMinutes(-10), terminalReason: "already closed");
            var live = await SeedCardAsync("CARD-0103", _inProgress);
            var queued = await SeedTaskAsync(doneCard, AgentTaskStatus.Queued, now.AddMinutes(-40));
            var blocked = await SeedTaskAsync(archived, AgentTaskStatus.Blocked, now.AddMinutes(-30));
            var session = await SeedSessionAsync();
            var startedAt = now.AddMinutes(-20);
            var started = await SeedTaskAsync(doneCard, AgentTaskStatus.Dispatched, now.AddMinutes(-20), session, startedAt);
            await SeedBriefAsync(session, started, startedAt);
            var after = await SeedTaskAsync(later, AgentTaskStatus.Queued, now.AddMinutes(-1));
            var control = await SeedTaskAsync(live, AgentTaskStatus.Queued, now.AddMinutes(-15));
            return new Seed(queued, blocked, started, after, control, session);
        }

        public async Task<Dictionary<Guid, AgentTaskStatus>> StatusesAsync(IEnumerable<Guid> ids)
        {
            var idList = ids.ToArray();
            await using var db = Connect();
            return await db.AgentTasks.AsNoTracking()
                .Where(t => idList.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Status);
        }

        public async Task<AgentTask> ReloadAsync(Guid id)
        {
            await using var db = Connect();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        public async ValueTask DisposeAsync() => await _schema.DisposeAsync();

        private async Task<Guid> SeedCardAsync(
            string identifier, BoardColumn column, DateTime? completedAt = null, string? terminalReason = null,
            DateTime? archivedAt = null, string? archivedReason = null)
        {
            await using var db = Connect();
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
                CompletedAt = completedAt,
                TerminalReason = terminalReason,
                ArchivedAt = archivedAt,
                ArchivedReason = archivedReason,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            return card.Id;
        }

        private async Task<Guid> SeedSessionAsync()
        {
            await using var db = Connect();
            var now = DateTime.UtcNow;
            var session = new AgentSession
            {
                Id = Guid.NewGuid(),
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            };
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            return session.Id;
        }

        private async Task<Guid> SeedTaskAsync(
            Guid cardId, AgentTaskStatus status, DateTime createdAt, Guid? sessionId = null, DateTime? dispatchedAt = null)
        {
            await using var db = Connect();
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = status.ToString(),
                Goal = "sweep",
                Role = AgentTaskRole.Code,
                CardId = cardId,
                AgentSessionId = sessionId,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = status,
                DispatchedAt = dispatchedAt,
                CreatedAt = createdAt,
            });
            await db.SaveChangesAsync();
            return id;
        }

        private async Task SeedBriefAsync(Guid sessionId, Guid taskId, DateTime at)
        {
            await using var db = Connect();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Body = $"{DelegationReportFormatter.TaskMarker(taskId)}\n\nDo the work.",
                Status = QueuedMessageStatus.Sent,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Delegation,
                CreatedAt = at,
            });
            await db.SaveChangesAsync();
        }

        private static DateTime Second(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        }

        private static AppDbContext Connect(IsolatedTestSchema schema) =>
            new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

        private static BoardColumn Column(
            Board board, string key, int order, CardStatus status, bool active, bool terminal, DateTime now) =>
            new()
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = key,
                Name = key,
                ColumnOrder = order,
                CardStatus = status,
                IsActive = active,
                IsTerminal = terminal,
                CreatedAt = now,
                UpdatedAt = now,
                Board = board,
            };
    }
}
