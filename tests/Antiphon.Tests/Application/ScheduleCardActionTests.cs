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
/// CARD-0057 S4 — card-kind fire arm, spend acknowledgement, skip reasons, last-word.
/// Isolated schema: last-word drives the global evidence sweep.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ScheduleCardActionTests
{
    [Test]
    public async Task start_none_moves_through_apply_automated_move_with_actor_scheduler_and_sets_the_hold()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        var schedule = await world.SeedDueCardAsync(card.Id, CardStatus.InProgress, ScheduleStart.None);

        await world.ClaimAndFireAsync(schedule.Id);

        var moved = await world.ReadCardAsync(card.Id);
        moved.Status.ShouldBe(CardStatus.InProgress);
        moved.AutoDispatchHeldAt.ShouldNotBeNull();
        (await world.SessionCountForAsync(card.Id)).ShouldBe(0);

        var revision = await world.LatestMoveAsync(card.Id);
        revision.EditedBy.ShouldBe("scheduler");
        revision.ToStatus.ShouldBe(CardStatus.InProgress);

        var fire = (await world.FiresAsync(schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.Moved);
    }

    [Test]
    public async Task start_release_clears_the_hold_and_the_orchestrator_candidate_query_sees_the_card()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        var schedule = await world.SeedDueCardAsync(card.Id, CardStatus.InProgress, ScheduleStart.Release);

        await world.ClaimAndFireAsync(schedule.Id);

        var released = await world.ReadCardAsync(card.Id);
        released.Status.ShouldBe(CardStatus.InProgress);
        released.AutoDispatchHeldAt.ShouldBeNull();
        released.OwnerSessionId.ShouldBeNull();
        released.ArchivedAt.ShouldBeNull();

        (await world.OrchestratorWouldSeeAsync(card.Id)).ShouldBeTrue();

        var fire = (await world.FiresAsync(schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.Released);
        fire.Detail.ShouldNotBeNull();
        fire.Detail!.ShouldContain("auto-dispatch hold released");
    }

    [Test]
    public async Task start_spawn_without_accept_spend_is_422_with_the_preview()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.Backlog);

        var ex = await Should.ThrowAsync<SpendUnacknowledgedException>(() =>
            world.Schedules.CreateAsync(
                new CreateScheduleRequest(
                    Name: "spawn thursday",
                    Kind: ScheduleKind.Card,
                    Repeat: ScheduleRepeat.Once,
                    FireAt: DateTime.UtcNow.AddHours(1),
                    CardId: card.Id.ToString(),
                    TargetStatus: CardStatus.InProgress,
                    Start: ScheduleStart.Spawn,
                    AcceptSpend: false),
                CancellationToken.None));

        ex.Code.ShouldBe("spend_unacknowledged");
        ex.Preview.Spend.ShouldBe("immediate-session");
        ex.Preview.WillStartSession.ShouldBeTrue();
        ex.Preview.Target.CardId.ShouldBe(card.Id);
        (await world.CountSchedulesForAsync(card.Id)).ShouldBe(0);
    }

    [Test]
    public async Task start_spawn_with_accept_spend_calls_spawn_async()
    {
        var fake = new RecordingScheduledCardActions();
        await using var world = await World.CreateAsync(cardActions: fake);
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        var created = await world.Schedules.CreateAsync(
            new CreateScheduleRequest(
                Name: "spawn now",
                Kind: ScheduleKind.Card,
                Repeat: ScheduleRepeat.Once,
                FireAt: DateTime.UtcNow.AddMinutes(-5),
                CardId: card.Id.ToString(),
                TargetStatus: CardStatus.InProgress,
                Start: ScheduleStart.Spawn,
                AcceptSpend: true,
                CreatedBy: "operator"),
            CancellationToken.None);
        created.SpendAcceptedAt.ShouldNotBeNull();

        await world.ClaimAndFireAsync(created.Id);

        fake.SpawnCalls.ShouldBe(1);
        fake.LastSpawnCardId.ShouldBe(card.Id);
        var fire = (await world.FiresAsync(created.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.Spawned);
        fire.SpawnedSessionId.ShouldBe(fake.SpawnedSessionId);
    }

    [Test]
    public async Task an_archived_owned_terminal_or_needs_decision_card_is_skipped_target_gone()
    {
        await using var world = await World.CreateAsync();
        var archived = await world.SeedCardAsync(CardStatus.Backlog, archived: true);
        var owned = await world.SeedCardAsync(CardStatus.InProgress, owned: true);
        var done = await world.SeedCardAsync(CardStatus.Done);
        var decided = await world.SeedCardAsync(CardStatus.NeedsDecision);

        foreach (var card in new[] { archived, owned, done, decided })
        {
            var schedule = await world.SeedDueCardAsync(card.Id, CardStatus.InProgress, ScheduleStart.None);
            await world.ClaimAndFireAsync(schedule.Id);
            var fire = (await world.FiresAsync(schedule.Id)).ShouldHaveSingleItem();
            fire.Outcome.ShouldBe(ScheduleFireOutcome.SkippedTargetGone, card.Status.ToString());
        }
    }

    [Test]
    public async Task a_once_card_action_that_skips_is_disabled()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.Backlog, archived: true);
        var schedule = await world.SeedDueCardAsync(
            card.Id, CardStatus.InProgress, ScheduleStart.None, repeat: ScheduleRepeat.Once);

        await world.ClaimAndFireAsync(schedule.Id);

        var after = await world.ReloadScheduleAsync(schedule.Id);
        after.Enabled.ShouldBeFalse();
        after.LastOutcome.ShouldBe(ScheduleFireOutcome.SkippedTargetGone);
        after.NextFireAt.ShouldBeNull();
    }

    [Test]
    public async Task the_evidence_sweep_does_not_undo_a_scheduler_move()
    {
        await using var world = await World.CreateAsync();
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        await world.SeedTaskAsync(
            card.Id, AgentTaskStatus.Dispatched, dispatchedAt: world.Now.AddHours(-1));

        var schedule = await world.SeedDueCardAsync(card.Id, CardStatus.Review, ScheduleStart.None);
        await world.ClaimAndFireAsync(schedule.Id);
        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.Review);

        (await world.ScanTransitionsAsync()).ShouldBe(0);
        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.Review);
    }

    [Test]
    public async Task a_quota_409_at_spawn_records_refused_and_never_reroutes()
    {
        var fake = new RecordingScheduledCardActions
        {
            SpawnException = new SubscriptionQuotaLowException(
                new SubscriptionQuotaVerdict(
                    AgentKind.ClaudeCode,
                    "claude:default",
                    "Pro",
                    RemainingPercent: 2,
                    ResetsAt: DateTime.UtcNow.AddHours(3),
                    TimeToReset: TimeSpan.FromHours(3),
                    ObservedAt: DateTime.UtcNow,
                    RuleName: "warn-below-5")),
        };
        await using var world = await World.CreateAsync(cardActions: fake);
        var card = await world.SeedCardAsync(CardStatus.Backlog);
        var schedule = await world.SeedDueCardAsync(card.Id, CardStatus.InProgress, ScheduleStart.Spawn);

        await world.ClaimAndFireAsync(schedule.Id);

        fake.SpawnCalls.ShouldBe(1);
        fake.RerouteCalls.ShouldBe(0);
        var fire = (await world.FiresAsync(schedule.Id)).ShouldHaveSingleItem();
        fire.Outcome.ShouldBe(ScheduleFireOutcome.Refused);
        fire.Detail.ShouldNotBeNull();
        fire.Detail!.ShouldContain(SubscriptionQuotaLowException.ErrorCode);
        fire.SpawnedSessionId.ShouldBeNull();
        (await world.ReadCardAsync(card.Id)).Status.ShouldBe(CardStatus.Backlog);
    }

    private sealed class RecordingScheduledCardActions : IScheduledCardActions
    {
        public int SpawnCalls { get; private set; }
        public int RerouteCalls { get; private set; }
        public Guid? LastSpawnCardId { get; private set; }
        public Guid SpawnedSessionId { get; } = Guid.NewGuid();
        public Exception? SpawnException { get; init; }

        public Task<bool> ApplyAutomatedMoveAsync(
            Guid cardId, CardStatus target, string reason, string movedBy, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> ReleaseAutoDispatchHoldAsync(
            Guid cardId, string reason, string actor, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<SpawnCardResult> SpawnAsync(Guid cardId, SpawnCardRequest request, CancellationToken ct)
        {
            SpawnCalls++;
            LastSpawnCardId = cardId;
            if (SpawnException is not null)
                throw SpawnException;
            return Task.FromResult(new SpawnCardResult(cardId, SpawnedSessionId));
        }
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly ServiceProvider _provider;
        private readonly string _tempRoot;
        private int _cardNumber;

        private World(IsolatedTestSchema schema, ServiceProvider provider, string tempRoot, Guid boardId)
        {
            _schema = schema;
            _provider = provider;
            _tempRoot = tempRoot;
            BoardId = boardId;
            Schedules = provider.CreateScope().ServiceProvider.GetRequiredService<ScheduleService>();
            Queue = provider.GetRequiredService<ScheduleFireQueue>();
        }

        public Guid BoardId { get; }
        public DateTime Now { get; } = DateTime.UtcNow;
        public ScheduleService Schedules { get; }
        public ScheduleFireQueue Queue { get; }

        public static async Task<World> CreateAsync(IScheduledCardActions? cardActions = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-sched-card-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(schema.ConnectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                }));
            var eventBus = new MockEventBus();
            services.AddSingleton(eventBus);
            services.AddSingleton<IEventBus>(eventBus);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(new AgentSessionSettings
            {
                FirstDeltaTimeoutMs = 1_000,
                KillGraceMs = 100,
                SignalRMaxChunkChars = 16 * 1024,
                ReplayBufferMaxChars = 128 * 1024,
                SessionLogPath = Path.Combine(tempRoot, "session-logs"),
            }));
            services.AddSingleton(Options.Create(new OrchestratorSettings
            {
                InternalTrackerRepositoryPathPrefix = tempRoot,
            }));
            services.AddSingleton(Options.Create(new DelegationSettings()));
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new CardWorkTransitionSettings { Enabled = true }));
            services.AddSingleton(Options.Create(new ScheduleSettings()));
            services.AddSingleton(Options.Create(new DigestSettings { TimeZone = "Europe/London" }));
            services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                new TransitionOptionsMonitor<AgentRegistrySettings>(new AgentRegistrySettings
                {
                    DefaultDefinition = "fake",
                    Definitions =
                    {
                        ["fake"] = new AgentDefinition
                        {
                            Kind = "Raw",
                            Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                        },
                    },
                }));
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<IWorktreeManager>(new NoWorktreeManager());
            services.AddSingleton<IAgentProtocolAdapterFactory>(new NoAdapterFactory());
            services.AddSingleton<IWorkspaceHookRunner>(
                new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
            services.AddScoped<WorkspaceHookService>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddScoped<AgentSessionService>();
            services.AddScoped<AgentSessionLaunchComposer>();
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
            services.AddGitWorkspaceService();
            services.AddScoped<AgentReviewCheckpointService>();
            services.AddScoped<CardService>();
            if (cardActions is not null)
                services.AddSingleton(cardActions);
            else
                services.AddScoped<IScheduledCardActions>(sp => sp.GetRequiredService<CardService>());
            services.AddScoped<CardWorkTransitionService>();
            services.AddSingleton<ScheduleFireQueue>();
            services.AddScoped<ScheduleService>();
            services.AddLogging();

            var provider = services.BuildServiceProvider();
            var boardId = await SeedBoardAsync(provider, tempRoot);
            return new World(schema, provider, tempRoot, boardId);
        }

        public async Task ClaimAndFireAsync(Guid scheduleId)
        {
            await Schedules.ClaimDueAsync(CancellationToken.None);
            while (Queue.TryDequeue(out var claim))
            {
                if (claim.ScheduleId == scheduleId)
                    await Schedules.FireAsync(claim, CancellationToken.None);
            }
        }

        public async Task<int> ScanTransitionsAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CardWorkTransitionService>()
                .ScanAsync(CancellationToken.None);
        }

        private static async Task<Guid> SeedBoardAsync(ServiceProvider provider, string tempRoot)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "Schedule card project",
                GitRepositoryUrl = "https://example.test/sched-card.git",
                LocalRepositoryPath = Path.Combine(tempRoot, "repo"),
                BaseBranch = "main",
                CreatedAt = now,
                UpdatedAt = now,
            };
            var board = new Board
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Name = "Schedule cards",
                MaxConcurrentSessions = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.AddRange(project, board);
            var columns = new (string Key, string Name, CardStatus Status, bool Active, bool Terminal)[]
            {
                ("backlog", "Backlog", CardStatus.Backlog, false, false),
                ("in-progress", "In Progress", CardStatus.InProgress, true, false),
                ("review", "Review", CardStatus.Review, false, false),
                ("needs-decision", "Needs decision", CardStatus.NeedsDecision, false, false),
                ("done", "Done", CardStatus.Done, false, true),
                ("canceled", "Canceled", CardStatus.Canceled, false, true),
            };
            for (var i = 0; i < columns.Length; i++)
            {
                var (key, name, status, active, terminal) = columns[i];
                db.BoardColumns.Add(new BoardColumn
                {
                    Id = Guid.NewGuid(),
                    BoardId = board.Id,
                    StateKey = key,
                    Name = name,
                    ColumnOrder = i,
                    CardStatus = status,
                    IsActive = active,
                    IsTerminal = terminal,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }

            await db.SaveChangesAsync();
            return board.Id;
        }

        public async Task<Card> SeedCardAsync(
            CardStatus status,
            bool archived = false,
            bool owned = false)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var column = await db.BoardColumns.FirstAsync(c => c.BoardId == BoardId && c.CardStatus == status);

            Guid? ownerSessionId = null;
            if (owned)
            {
                var session = new AgentSession
                {
                    Id = Guid.NewGuid(),
                    DefinitionName = "fake",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running,
                    Cwd = _tempRoot,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = Now,
                    StartedAt = Now,
                    LastSeenAt = Now,
                };
                db.AgentSessions.Add(session);
                await db.SaveChangesAsync();
                ownerSessionId = session.Id;
            }

            var card = new Card
            {
                Id = Guid.NewGuid(),
                BoardId = BoardId,
                BoardColumnId = column.Id,
                Identifier = $"CARD-{Interlocked.Increment(ref _cardNumber):0000}",
                Title = $"Schedule card {status}",
                Description = "S4 test.",
                Status = status,
                OwnerSessionId = ownerSessionId,
                ArchivedAt = archived ? Now : null,
                ArchivedReason = archived ? "S4 test." : null,
                CreatedAt = Now.AddDays(-30),
                UpdatedAt = Now.AddDays(-30),
            };
            db.Cards.Add(card);
            await db.SaveChangesAsync();
            return card;
        }

        public async Task<AgentTask> SeedTaskAsync(Guid cardId, AgentTaskStatus status, DateTime? dispatchedAt)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Depth = 0,
                Title = "Bound task",
                Goal = "S4 last-word.",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                CardId = cardId,
                ModelLevel = AgentModelLevel.High,
                WorkingDirectory = _tempRoot,
                Status = status,
                DispatchedAt = dispatchedAt,
                CreatedAt = dispatchedAt ?? Now,
            });
            await db.SaveChangesAsync();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        public async Task<Schedule> SeedDueCardAsync(
            Guid cardId,
            CardStatus target,
            ScheduleStart start,
            ScheduleRepeat repeat = ScheduleRepeat.Once)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dueAt = DateTime.UtcNow.AddMinutes(-5);
            var schedule = new Schedule
            {
                Id = Guid.NewGuid(),
                Name = $"card-{start}",
                Kind = ScheduleKind.Card,
                Repeat = repeat,
                TimeZoneId = "Europe/London",
                NextFireAt = dueAt,
                Enabled = true,
                MissedGraceMinutes = ScheduleRecurrence.DefaultMissedGraceMinutes(repeat, null),
                CreatedAt = Now,
                UpdatedAt = Now,
                ConcurrencyToken = Guid.NewGuid(),
                CardId = cardId,
                TargetStatus = target,
                Start = start,
                FireAt = repeat == ScheduleRepeat.Once ? dueAt : null,
                SpendAcceptedAt = start is ScheduleStart.Release or ScheduleStart.Spawn ? Now : null,
                SpendAcceptedBy = start is ScheduleStart.Release or ScheduleStart.Spawn ? "operator" : null,
            };
            db.Schedules.Add(schedule);
            await db.SaveChangesAsync();
            return schedule;
        }

        public async Task<Card> ReadCardAsync(Guid cardId)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Cards.AsNoTracking().FirstAsync(c => c.Id == cardId);
        }

        public async Task<Schedule> ReloadScheduleAsync(Guid id)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Schedules.AsNoTracking().SingleAsync(s => s.Id == id);
        }

        public async Task<List<ScheduleFire>> FiresAsync(Guid scheduleId)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.ScheduleFires.AsNoTracking()
                .Where(f => f.ScheduleId == scheduleId)
                .OrderBy(f => f.FireNumber)
                .ToListAsync();
        }

        public async Task<CardRevision> LatestMoveAsync(Guid cardId)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.CardRevisions.AsNoTracking()
                .Where(r => r.CardId == cardId && r.Kind == CardRevisionKind.Move)
                .OrderByDescending(r => r.RevisionNumber)
                .FirstAsync();
        }

        public async Task<int> SessionCountForAsync(Guid cardId)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.AgentSessions.AsNoTracking().CountAsync(s => s.CardId == cardId);
        }

        public async Task<int> CountSchedulesForAsync(Guid cardId)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Schedules.CountAsync(s => s.CardId == cardId);
        }

        public async Task<bool> OrchestratorWouldSeeAsync(Guid cardId)
        {
            await using var scope = _provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Cards.AsNoTracking()
                .Where(c => c.Id == cardId)
                .Where(c => c.BoardColumn.IsActive && !c.BoardColumn.IsTerminal)
                .Where(c => c.ArchivedAt == null)
                .Where(c => c.AutoDispatchHeldAt == null)
                .Where(c => c.OwnerSessionId == null)
                .AnyAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _schema.DisposeAsync();
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class NoAdapterFactory : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            throw new InvalidOperationException("S4 card-action tests must not launch a session.");
    }

    private sealed class NoWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct) =>
            throw new InvalidOperationException("S4 card-action tests must not create a worktree.");

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class TransitionOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TransitionOptionsMonitor(T currentValue) => CurrentValue = currentValue;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
