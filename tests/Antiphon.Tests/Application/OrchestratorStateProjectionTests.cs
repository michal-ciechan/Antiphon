using System.Runtime.CompilerServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0092 S1: the /orchestrator Running Sessions projection. Isolated schema per test so
/// assertions name the rows they created. Read-only — not <c>[NotInParallel]</c>.
/// </summary>
[Category("Integration")]
public class OrchestratorStateProjectionTests
{
    [Test]
    public async Task active_session_plus_working_unbound_task_is_a_delegation_row()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(cardId: null);
        var task = await world.SeedTaskAsync(
            session.Id, cardId: null, AgentTaskStatus.Working,
            tokensIn: 42, tokensOut: 7, costUsd: 0.5m, agentName: "unbound-code");

        var row = (await world.GetAsync()).Running.Single(r => r.SessionId == session.Id);

        row.Source.ShouldBe(OrchestratorSessionSource.Delegation);
        row.CardId.ShouldBeNull();
        row.CardIdentifier.ShouldBeNull();
        row.CardTitle.ShouldBeNull();
        row.BoardId.ShouldBeNull();
        row.BoardName.ShouldBeNull();
        row.Task.ShouldNotBeNull();
        row.Task!.TaskId.ShouldBe(task.Id);
        row.Task.ShortId.ShouldBe(DelegationReportFormatter.Short(task.Id));
        row.Task.Role.ShouldBe(AgentTaskRole.Code);
        row.Task.Kind.ShouldBe(AgentTaskKind.Worker);
        row.Task.Status.ShouldBe(AgentTaskStatus.Working);
        row.TokensIn.ShouldBe(42);
        row.TurnCount.ShouldBe(0);
        row.Phase.ShouldBeNull();
        row.AttemptNumber.ShouldBeNull();
        row.RunAttemptId.ShouldBeNull();
    }

    [Test]
    public async Task dispatched_task_bound_to_a_card_carries_the_card_fields()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("Bound card");
        var session = await world.SeedSessionAsync(cardId: null);
        await world.SeedTaskAsync(
            session.Id, card.Id, AgentTaskStatus.Dispatched, title: "Bound dispatch");

        var row = (await world.GetAsync()).Running.Single(r => r.SessionId == session.Id);

        row.Source.ShouldBe(OrchestratorSessionSource.Delegation);
        row.CardId.ShouldBe(card.Id);
        row.CardIdentifier.ShouldBe(card.Identifier);
        row.CardTitle.ShouldBe("Bound card");
        row.BoardId.ShouldBe(world.BoardId);
        row.BoardName.ShouldBe("Home");
        row.Task.ShouldNotBeNull();
        row.Task!.Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task blocked_task_is_absent_until_it_returns_to_working()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(cardId: null);
        var task = await world.SeedTaskAsync(session.Id, cardId: null, AgentTaskStatus.Blocked);

        (await world.GetAsync()).Running.Any(r => r.SessionId == session.Id).ShouldBeFalse();

        await world.SetTaskStatusAsync(task.Id, AgentTaskStatus.Working);

        (await world.GetAsync()).Running.Single(r => r.SessionId == session.Id)
            .Source.ShouldBe(OrchestratorSessionSource.Delegation);
    }

    [Test]
    public async Task check_role_task_is_absent()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(cardId: null);
        await world.SeedTaskAsync(
            session.Id, cardId: null, AgentTaskStatus.Working, role: AgentTaskRole.Check);

        (await world.GetAsync()).Running.Any(r => r.SessionId == session.Id).ShouldBeFalse();
    }

    [Test]
    public async Task cardless_session_with_no_task_is_absent()
    {
        await using var world = await World.CreateAsync();
        var session = await world.SeedSessionAsync(cardId: null);

        (await world.GetAsync()).Running.Any(r => r.SessionId == session.Id).ShouldBeFalse();
    }

    [Test]
    public async Task card_spawn_session_is_unchanged_source_card_depth_zero_no_task()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync("Spawned card");
        var session = await world.SeedSessionAsync(card.Id);
        await world.SeedRunAttemptAsync(session.Id, card.Id, tokensIn: 11, tokensOut: 3, costUsd: 0.2m);

        var row = (await world.GetAsync()).Running.Single(r => r.SessionId == session.Id);

        row.Source.ShouldBe(OrchestratorSessionSource.Card);
        row.Depth.ShouldBe(0);
        row.Task.ShouldBeNull();
        row.CardId.ShouldBe(card.Id);
        row.CardIdentifier.ShouldBe(card.Identifier);
        row.CardTitle.ShouldBe("Spawned card");
        row.BoardName.ShouldBe("Home");
        row.TokensIn.ShouldBe(11);
        row.TokensOut.ShouldBe(3);
        row.CostUsd.ShouldBe(0.2m);
        row.TurnCount.ShouldBe(1);
        row.AttemptNumber.ShouldBe(1);
        row.Phase.ShouldBe(nameof(RunPhase.StreamingTurn));
    }

    [Test]
    public async Task orchestrator_and_working_children_are_a_family_stopped_child_absent()
    {
        await using var world = await World.CreateAsync();
        var parentSession = await world.SeedSessionAsync(cardId: null, startedAt: world.Now);
        var childASession = await world.SeedSessionAsync(cardId: null, startedAt: world.Now.AddSeconds(1));
        var childBSession = await world.SeedSessionAsync(cardId: null, startedAt: world.Now.AddSeconds(3));
        var stoppedSession = await world.SeedSessionAsync(
            cardId: null, startedAt: world.Now.AddSeconds(2), status: SessionStatus.Stopped);

        var parent = await world.SeedTaskAsync(
            parentSession.Id, cardId: null, AgentTaskStatus.Working,
            kind: AgentTaskKind.Orchestrator, title: "Parent orch");
        var childA = await world.SeedTaskAsync(
            childASession.Id, cardId: null, AgentTaskStatus.Working,
            parent: parent, title: "Child A");
        var childB = await world.SeedTaskAsync(
            childBSession.Id, cardId: null, AgentTaskStatus.Working,
            parent: parent, title: "Child B");
        await world.SeedTaskAsync(
            stoppedSession.Id, cardId: null, AgentTaskStatus.Working,
            parent: parent, title: "Stopped child");

        var running = (await world.GetAsync()).Running.ToList();
        var parentRow = running.Single(r => r.SessionId == parentSession.Id);
        var childARow = running.Single(r => r.SessionId == childASession.Id);
        var childBRow = running.Single(r => r.SessionId == childBSession.Id);

        running.Any(r => r.SessionId == stoppedSession.Id).ShouldBeFalse();
        running.IndexOf(parentRow).ShouldBeLessThan(running.IndexOf(childARow));
        running.IndexOf(childARow).ShouldBeLessThan(running.IndexOf(childBRow));
        running.IndexOf(childBRow).ShouldBe(running.IndexOf(parentRow) + 2);
        parentRow.Depth.ShouldBe(0);
        childARow.Depth.ShouldBe(1);
        childBRow.Depth.ShouldBe(1);
        childARow.Task!.ParentTaskId.ShouldBe(parent.Id);
        childBRow.Task!.ParentTaskId.ShouldBe(parent.Id);
        childA.Id.ShouldNotBe(childB.Id);
    }

    [Test]
    public async Task child_working_parent_blocked_is_depth_zero()
    {
        await using var world = await World.CreateAsync();
        var parentSession = await world.SeedSessionAsync(cardId: null);
        var childSession = await world.SeedSessionAsync(cardId: null, startedAt: world.Now.AddSeconds(1));
        var parent = await world.SeedTaskAsync(
            parentSession.Id, cardId: null, AgentTaskStatus.Blocked,
            kind: AgentTaskKind.Orchestrator);
        await world.SeedTaskAsync(
            childSession.Id, cardId: null, AgentTaskStatus.Working, parent: parent);

        var running = (await world.GetAsync()).Running.ToList();
        running.Any(r => r.SessionId == parentSession.Id).ShouldBeFalse();
        var child = running.Single(r => r.SessionId == childSession.Id);
        child.Depth.ShouldBe(0);
        child.Source.ShouldBe(OrchestratorSessionSource.Delegation);
    }

    [Test]
    public async Task scope_prefix_keeps_cardless_delegates_and_drops_out_of_scope_internal_cards()
    {
        await using var world = await World.CreateAsync(pathPrefix: "C:\\in-scope");
        var outsideBoard = await world.SeedBoardAsync("Outside", "C:\\other\\repo");
        var outsideCard = await world.SeedCardAsync("Outside card", boardId: outsideBoard);

        var unboundSession = await world.SeedSessionAsync(cardId: null);
        await world.SeedTaskAsync(unboundSession.Id, cardId: null, AgentTaskStatus.Working);

        var boundSession = await world.SeedSessionAsync(cardId: null);
        await world.SeedTaskAsync(boundSession.Id, outsideCard.Id, AgentTaskStatus.Working);

        var spawnSession = await world.SeedSessionAsync(outsideCard.Id);
        await world.SeedRunAttemptAsync(spawnSession.Id, outsideCard.Id);

        var running = (await world.GetAsync()).Running.ToList();
        running.Single(r => r.SessionId == unboundSession.Id).Source
            .ShouldBe(OrchestratorSessionSource.Delegation);
        running.Any(r => r.SessionId == boundSession.Id).ShouldBeFalse();
        running.Any(r => r.SessionId == spawnSession.Id).ShouldBeFalse();
    }

    [Test]
    public async Task own_rows_increment_the_matching_running_counters()
    {
        await using var world = await World.CreateAsync();
        var baseline = await world.GetAsync();

        var card = await world.SeedCardAsync("Counter card");
        var cardSession = await world.SeedSessionAsync(card.Id);
        var delegateSession = await world.SeedSessionAsync(cardId: null);
        await world.SeedTaskAsync(delegateSession.Id, cardId: null, AgentTaskStatus.Working);

        var after = await world.GetAsync();
        after.Running.Single(r => r.SessionId == cardSession.Id).Source
            .ShouldBe(OrchestratorSessionSource.Card);
        after.Running.Single(r => r.SessionId == delegateSession.Id).Source
            .ShouldBe(OrchestratorSessionSource.Delegation);
        after.RunningCardSessions.ShouldBe(baseline.RunningCardSessions + 1);
        after.RunningDelegateSessions.ShouldBe(baseline.RunningDelegateSessions + 1);
        after.RunningSessions.ShouldBe(baseline.RunningSessions + 2);
        (after.RunningCardSessions + after.RunningDelegateSessions).ShouldBe(after.RunningSessions);
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly ServiceProvider _provider;
        private int _cardNumber;

        private World(IsolatedTestSchema schema, ServiceProvider provider, Guid boardId)
        {
            _schema = schema;
            _provider = provider;
            BoardId = boardId;
        }

        public Guid BoardId { get; }
        public DateTime Now { get; } = DateTime.UtcNow;

        public static async Task<World> CreateAsync(string? pathPrefix = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var repoPath = pathPrefix is null
                ? Path.Combine(Path.GetTempPath(), $"antiphon-orch-state-{Guid.NewGuid():N}")
                : Path.Combine(pathPrefix, "repo");

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(schema.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                }));
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IEventBus>(new MockEventBus());
            services.AddSingleton<OrchestratorControlState>();
            services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
            {
                InternalTrackerRepositoryPathPrefix = pathPrefix
            }));
            services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings()));
            services.AddLogging();
            services.AddScoped(CreateOrchestrator);

            var provider = services.BuildServiceProvider();
            var boardId = await SeedDefaultBoardAsync(provider, repoPath);
            return new World(schema, provider, boardId);
        }

        private static OrchestratorService CreateOrchestrator(IServiceProvider sp)
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var settings = sp.GetRequiredService<IOptions<OrchestratorSettings>>();
            var delegation = sp.GetRequiredService<IOptions<DelegationSettings>>();
            var registry = new AgentRegistry(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
            {
                DefaultDefinition = "fake",
                Definitions = { ["fake"] = new AgentDefinition { Kind = "Raw", Exe = "cmd.exe" } }
            }));
            return new OrchestratorService(
                db,
                registry,
                new AgentSessionLaunchComposer(
                    db, delegation, registry, NullLogger<AgentSessionLaunchComposer>.Instance),
                (AgentSessionService)RuntimeHelpers.GetUninitializedObject(typeof(AgentSessionService)),
                new AgentSessionLaunchQueue(
                    sp.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<AgentSessionLaunchQueue>.Instance),
                new RetryScheduler(settings),
                new ExternalTrackerSyncService(
                    db, [], sp.GetRequiredService<IEventBus>(),
                    NullLogger<ExternalTrackerSyncService>.Instance),
                sp.GetRequiredService<OrchestratorControlState>(),
                sp.GetRequiredService<IEventBus>(),
                settings,
                delegation,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<OrchestratorService>>());
        }

        private static async Task<Guid> SeedDefaultBoardAsync(ServiceProvider provider, string repoPath)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await InsertBoardAsync(db, "Orch state project", repoPath);
        }

        public async Task<Guid> SeedBoardAsync(string name, string? localRepositoryPath)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await InsertBoardAsync(db, name, localRepositoryPath);
        }

        private static async Task<Guid> InsertBoardAsync(
            AppDbContext db, string projectName, string? localRepositoryPath)
        {
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = projectName,
                GitRepositoryUrl = "https://example.test/orch-state.git",
                LocalRepositoryPath = localRepositoryPath,
                BaseBranch = "main",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Home",
                TrackerKind = TrackerKind.Internal,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AddRange(project, board);
            db.BoardColumns.Add(new BoardColumn
            {
                Id = Guid.NewGuid(),
                BoardId = board.Id,
                StateKey = "in-progress",
                Name = "In Progress",
                ColumnOrder = 0,
                CardStatus = CardStatus.InProgress,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
            return board.Id;
        }

        public async Task<OrchestratorStateDto> GetAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OrchestratorService>()
                .GetStateAsync(CancellationToken.None);
        }

        public async Task<Card> SeedCardAsync(string title, Guid? boardId = null)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var targetBoard = boardId ?? BoardId;
            var column = await db.BoardColumns.FirstAsync(c => c.BoardId == targetBoard);
            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = targetBoard,
                BoardColumnId = column.Id,
                Identifier = $"CARD-{Interlocked.Increment(ref _cardNumber):0000}",
                Title = title,
                Status = CardStatus.InProgress,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            return card;
        }

        public async Task<AgentSession> SeedSessionAsync(
            Guid? cardId,
            DateTime? startedAt = null,
            SessionStatus status = SessionStatus.Running)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var at = startedAt ?? Now;
            var session = new AgentSession
            {
                Id = Guid.NewGuid(),
                CardId = cardId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = status,
                Cwd = "C:\\tmp",
                CreatedAt = at,
                StartedAt = at,
                LastSeenAt = at,
            };
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync();
            return session;
        }

        public async Task<AgentTask> SeedTaskAsync(
            Guid sessionId,
            Guid? cardId,
            AgentTaskStatus status,
            AgentTaskRole role = AgentTaskRole.Code,
            AgentTaskKind kind = AgentTaskKind.Worker,
            AgentTask? parent = null,
            string title = "Orch task",
            string? agentName = "task-code",
            long tokensIn = 0,
            long tokensOut = 0,
            decimal costUsd = 0)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = Guid.NewGuid();
            var task = new AgentTask
            {
                Id = id,
                RootTaskId = parent?.RootTaskId ?? id,
                ParentTaskId = parent?.Id,
                Depth = parent is null ? 0 : parent.Depth + 1,
                Title = title,
                Goal = "Projection fixture.",
                Kind = kind,
                Role = role,
                CardId = cardId,
                WorkingDirectory = "C:\\tmp",
                Status = status,
                AgentSessionId = sessionId,
                AgentName = agentName,
                TokensIn = tokensIn,
                TokensOut = tokensOut,
                CostUsd = costUsd,
                CreatedAt = Now,
                DispatchedAt = Now,
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            return task;
        }

        public async Task SetTaskStatusAsync(Guid taskId, AgentTaskStatus status)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.AgentTasks.FirstAsync(t => t.Id == taskId);
            task.Status = status;
            await db.SaveChangesAsync();
        }

        public async Task SeedRunAttemptAsync(
            Guid sessionId,
            Guid cardId,
            long tokensIn = 0,
            long tokensOut = 0,
            decimal costUsd = 0)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var attempt = new RunAttempt
            {
                Id = Guid.NewGuid(),
                CardId = cardId,
                AgentSessionId = sessionId,
                AttemptNumber = 1,
                Phase = RunPhase.StreamingTurn,
                CreatedAt = Now,
                StartedAt = Now,
                LastEventAt = Now,
                PhaseStartedAt = Now,
                Prompt = "work",
                TokenUsage = new TokenUsage
                {
                    Id = Guid.NewGuid(),
                    TokensIn = tokensIn,
                    TokensOut = tokensOut,
                    CostUsd = costUsd,
                    ModelName = "opus",
                    CreatedAt = Now,
                },
            };
            db.RunAttempts.Add(attempt);
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _schema.DisposeAsync();
        }

        private sealed class OptionsMonitorStub<T>(T currentValue) : IOptionsMonitor<T>
        {
            public T CurrentValue { get; } = currentValue;
            public T Get(string? name) => CurrentValue;
            public IDisposable? OnChange(Action<T, string?> listener) => null;
        }
    }
}
