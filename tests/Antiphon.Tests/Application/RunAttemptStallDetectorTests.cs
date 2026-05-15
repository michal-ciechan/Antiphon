using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class RunAttemptStallDetectorTests
{
    [Test]
    public async Task StallDetector_fires_after_configured_idle()
    {
        await using var db = CreateContext();
        var now = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero);
        var sessionId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var graph = CreateGraph(now.UtcDateTime, sessionId, attemptId);
        db.Add(graph.Project);
        await db.SaveChangesAsync();

        var eventBus = new MockEventBus();
        await using var provider = BuildProvider();
        var clock = new MutableTimeProvider(now);
        var runtime = new AgentSessionRuntime(
            eventBus,
            Options.Create(new AgentSessionSettings { StallTimeoutMs = 1_000, KillGraceMs = 100 }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<AgentSessionRuntime>.Instance);
        var adapter = new FakeAgentProtocolAdapter();
        runtime.Register(sessionId, adapter);
        var detector = new RunAttemptStallDetector(
            db,
            runtime,
            eventBus,
            Options.Create(new AgentSessionSettings { StallTimeoutMs = 1_000, KillGraceMs = 100 }),
            clock,
            NullLogger<RunAttemptStallDetector>.Instance);

        clock.SetUtcNow(now.AddSeconds(5));
        var stalled = await detector.ScanAsync(CancellationToken.None);

        stalled.ShouldBe(1);
        adapter.Killed.ShouldBeTrue();
        adapter.Disposed.ShouldBeTrue();
        var attempt = await db.RunAttempts.SingleAsync(a => a.Id == attemptId);
        attempt.Phase.ShouldBe(RunPhase.Stalled);
        attempt.CompletedAt.ShouldNotBeNull();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.Status.ShouldBe(SessionStatus.Failed);
        eventBus.PublishedEvents.Single(e => e.EventName == "RunAttemptStalled")
            .Group.ShouldBe(AgentSessionGroups.Session(sessionId));
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        return services.BuildServiceProvider();
    }

    private static Graph CreateGraph(DateTime now, Guid sessionId, Guid attemptId)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"Project {Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/repo.git",
            LocalRepositoryPath = "D:/repo",
            CreatedAt = now,
            UpdatedAt = now
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Board {Guid.NewGuid():N}",
            CreatedAt = now,
            UpdatedAt = now,
            Project = project
        };
        project.Boards.Add(board);
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = "in-progress",
            Name = "In Progress",
            ColumnOrder = 1,
            CardStatus = CardStatus.InProgress,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board
        };
        board.Columns.Add(column);
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = $"E05-{Guid.NewGuid():N}",
            Title = "Stall detector card",
            Status = CardStatus.InProgress,
            CreatedAt = now,
            UpdatedAt = now,
            Board = board,
            BoardColumn = column
        };
        board.Cards.Add(card);
        column.Cards.Add(card);
        var session = new AgentSession
        {
            Id = sessionId,
            CardId = card.Id,
            DefinitionName = "raw",
            AgentKind = AgentKind.Raw,
            Status = SessionStatus.Running,
            Cwd = "D:/repo",
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            Card = card
        };
        card.AgentSessions.Add(session);
        var attempt = new RunAttempt
        {
            Id = attemptId,
            CardId = card.Id,
            AgentSessionId = session.Id,
            AttemptNumber = 1,
            Phase = RunPhase.StreamingTurn,
            CreatedAt = now,
            StartedAt = now,
            LastEventAt = now,
            PhaseStartedAt = now,
            Prompt = "test",
            Card = card,
            AgentSession = session
        };
        card.RunAttempts.Add(attempt);
        session.RunAttempts.Add(attempt);

        return new Graph(project);
    }

    private sealed record Graph(Project Project);

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }
    }
}
