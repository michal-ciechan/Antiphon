using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0738 V-12 to V-15: a started task under a closed card is one warning row.</summary>
[Category("Integration")]
public sealed class CardClosedAttentionTests
{
    [Test]
    public async Task a_working_task_on_a_closed_card_is_one_warning_row()
    {
        var now = Second(DateTime.UtcNow);
        var completedAt = now.AddDays(-2);
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("CARD-0091", world.Done, completedAt: completedAt, terminalReason: "shipped the fix");
        var sessionId = await world.SeedSessionAsync();
        var task = await world.SeedOpenTaskAsync(card.Id, sessionId, createdAt: now.AddDays(-3), dispatchedAt: now.AddDays(-3));
        await world.SeedTranscriptAsync(sessionId);

        var item = (await world.RowsAsync(now, sessionId, task.Id)).ShouldHaveSingleItem();
        item.Kind.ShouldBe(AttentionKind.CardClosedWhileWorking);
        ((int)item.Kind).ShouldBe(48);
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.CardId.ShouldBe(card.Id);
        item.BoardId.ShouldBe(card.BoardId);
        item.SinceUtc.ShouldBe(completedAt);
        item.Evidence.ShouldContain("shipped the fix");
        item.Actions.ShouldBe([AttentionAction.OpenCard, AttentionAction.OpenDrawer, AttentionAction.Cancel]);
        item.ConditionKey.ShouldBe($"card-closed:{task.Id:N}");
    }

    [Test]
    public async Task a_task_created_after_the_close_is_not_listed()
    {
        var now = Second(DateTime.UtcNow);
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("CARD-0091", world.Done, completedAt: now.AddDays(-2), terminalReason: "already done");
        var sessionId = await world.SeedSessionAsync();
        var task = await world.SeedOpenTaskAsync(card.Id, sessionId, createdAt: now.AddDays(-1), dispatchedAt: now.AddHours(-1));
        await world.SeedTranscriptAsync(sessionId);

        (await world.RowsAsync(now, sessionId, task.Id)).ShouldBeEmpty();
    }

    [Test]
    public async Task an_archived_live_card_counts_as_closed()
    {
        var now = Second(DateTime.UtcNow);
        var archivedAt = now.AddDays(-1);
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(
            "CARD-0004", world.Backlog, archivedAt: archivedAt, archivedReason: "Duplicate of CARD-0002");
        var sessionId = await world.SeedSessionAsync();
        var task = await world.SeedOpenTaskAsync(card.Id, sessionId, createdAt: now.AddDays(-3), dispatchedAt: now.AddHours(-2));
        await world.SeedTranscriptAsync(sessionId);

        var item = (await world.RowsAsync(now, sessionId, task.Id)).ShouldHaveSingleItem();
        item.Headline.ShouldContain("archived");
        item.SinceUtc.ShouldBe(archivedAt);
    }

    [Test]
    public async Task an_open_task_on_an_in_progress_card_is_not_listed()
    {
        var now = Second(DateTime.UtcNow);
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("CARD-0005", world.InProgress);
        var sessionId = await world.SeedSessionAsync();
        var task = await world.SeedOpenTaskAsync(card.Id, sessionId, createdAt: now.AddHours(-1), dispatchedAt: now.AddHours(-1));
        await world.SeedTranscriptAsync(sessionId);

        (await world.RowsAsync(now, sessionId, task.Id)).ShouldBeEmpty();
    }

    private static DateTime Second(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }

    private static SessionRunnerSessionDto Running(Guid sessionId) =>
        new(sessionId, 1234, DateTime.UtcNow.AddMinutes(-45), "Running", null, AgentExitReason.Unknown, 900);

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        public BoardColumn Backlog { get; }
        public BoardColumn InProgress { get; }
        public BoardColumn Done { get; }

        private World(IsolatedTestSchema schema, BoardColumn backlog, BoardColumn inProgress, BoardColumn done)
        {
            _schema = schema;
            Backlog = backlog;
            InProgress = inProgress;
            Done = done;
        }

        public static async Task<World> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            await using var db = Connect(schema);
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = $"c738-att {Guid.NewGuid():N}",
                GitRepositoryUrl = "https://example.test/c738.git",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "attention",
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

        public async Task<Card> SeedCardAsync(
            string identifier,
            BoardColumn column,
            DateTime? completedAt = null,
            string? terminalReason = null,
            DateTime? archivedAt = null,
            string? archivedReason = null)
        {
            await using var db = Connect(_schema);
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
            return card;
        }

        public async Task<Guid> SeedSessionAsync()
        {
            await using var db = Connect(_schema);
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

        public async Task<AgentTask> SeedOpenTaskAsync(Guid cardId, Guid sessionId, DateTime createdAt, DateTime dispatchedAt)
        {
            await using var db = Connect(_schema);
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "still working",
                Goal = "still working",
                Role = AgentTaskRole.Code,
                CardId = cardId,
                AgentSessionId = sessionId,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentTaskStatus.Dispatched,
                DispatchedAt = dispatchedAt,
                CreatedAt = createdAt,
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        public async Task SeedTranscriptAsync(Guid sessionId)
        {
            await using var db = Connect(_schema);
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 1,
                Kind = TranscriptKinds.UserPrompt,
                Text = "go",
                Timestamp = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task<List<AttentionItemDto>> RowsAsync(DateTime now, Guid sessionId, Guid taskId)
        {
            await using var db = Connect(_schema);
            var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
            var result = await new AttentionService(
                db,
                new AttentionServiceTests.FakeRunnerClient { Sessions = [Running(sessionId)] },
                Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings()),
                clock,
                NullLogger<AttentionService>.Instance)
                .GetAsync(CancellationToken.None, includeProgressProbe: false);
            return result.Items
                .Where(i => i.TaskId == taskId && i.Kind == AttentionKind.CardClosedWhileWorking)
                .ToList();
        }

        public async ValueTask DisposeAsync() => await _schema.DisposeAsync();

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
