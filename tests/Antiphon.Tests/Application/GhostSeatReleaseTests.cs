using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1719. A Failed task with a clean, origin-equal mirror loses its runner seat after the
/// janitor idle bound. A dirty, unpushed or unknown mirror keeps the seat and one attention
/// row. Blocked, standing and warm-pool sessions are left alone. Release is the operator slot
/// primitive, not a raw kill. The qualified terminal-seat coordinator stays off in this harness,
/// which is the production default of TerminalRunnerSeatRelease:AutomaticEnabled.
/// </summary>
[Category("Integration")]
[NotInParallel]
public sealed class GhostSeatReleaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Clean_equal_failed_seat_is_released_after_the_idle_bound()
    {
        await using var world = await World.StartAsync();
        var task = await world.SeedFailedAsync(idleMinutes: 9, mirror: Mirror("equal", dirty: false),
            failure: AgentTaskFailureCode.CompletedWithoutProgress);

        await world.RetireAsync();
        world.Client.SlotReleases.ShouldBeEmpty();
        world.Stopper.Killed.ShouldBeEmpty();
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Running);

        await world.AgeCompletionAsync(task.Id, minutes: 10);
        await world.RetireAsync();

        world.Client.SlotReleases.Count.ShouldBe(1);
        world.Client.SlotReleases[0].SessionId.ShouldBe(world.SessionId);
        world.Client.KillCalls.ShouldBe(0);
        world.Stopper.Killed.ShouldBeEmpty();
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Stopped);
        (await world.IncidentCountAsync(AgentIncidentKind.RunnerSlotForceReleased)).ShouldBe(1);

        await world.RetireAsync();
        world.Client.SlotReleases.Count.ShouldBe(1);
    }

    [Test]
    public async Task Other_failed_code_with_a_clean_equal_mirror_is_released_after_the_bound()
    {
        await using var world = await World.StartAsync();
        await world.SeedFailedAsync(idleMinutes: 10, mirror: Mirror("equal", dirty: false),
            failure: AgentTaskFailureCode.AuthenticationRequired);

        await world.RetireAsync();

        world.Client.SlotReleases.Count.ShouldBe(1);
        world.Stopper.Killed.ShouldBeEmpty();
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Stopped);
    }

    [Test]
    public async Task Dirty_mirror_keeps_the_seat_and_one_attention_item()
    {
        await using var world = await World.StartAsync();
        await world.SeedFailedAsync(idleMinutes: 30, mirror: Mirror("equal", dirty: true),
            failure: AgentTaskFailureCode.CompletedWithoutProgress);

        await world.RetireAsync();
        await world.RetireAsync();

        world.Client.SlotReleases.ShouldBeEmpty();
        world.Stopper.Killed.ShouldBeEmpty();
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Running);
        var rows = await world.HeldAsync();
        rows.Count.ShouldBe(1);
        rows[0].Evidence.ShouldContain("mirror=dirty");
        (await world.IncidentCountAsync(AgentIncidentKind.RunnerSlotForceReleased)).ShouldBe(0);
    }

    [Test]
    public async Task Unpushed_mirror_keeps_the_seat_and_one_attention_item()
    {
        await using var world = await World.StartAsync();
        await world.SeedFailedAsync(idleMinutes: 30, mirror: Mirror("descends", dirty: false),
            failure: AgentTaskFailureCode.CompletedWithoutProgress);

        await world.RetireAsync();
        await world.RetireAsync();

        world.Client.SlotReleases.ShouldBeEmpty();
        world.Stopper.Killed.ShouldBeEmpty();
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Running);
        var rows = await world.HeldAsync();
        rows.Count.ShouldBe(1);
        rows[0].Evidence.ShouldContain("mirror=unpushed");
    }

    [Test]
    [Arguments("absent")]
    [Arguments("inspection")]
    public async Task Unknown_mirror_keeps_the_seat(string shape)
    {
        await using var world = await World.StartAsync();
        var mirror = shape == "inspection"
            ? Mirror("equal", dirty: false, inspection: "inspection_unavailable")
            : null;
        await world.SeedFailedAsync(idleMinutes: 30, mirror: mirror,
            failure: AgentTaskFailureCode.CompletedWithoutProgress);

        await world.RetireAsync();

        world.Client.SlotReleases.ShouldBeEmpty();
        world.Stopper.Killed.ShouldBeEmpty();
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Running);
        var rows = await world.HeldAsync();
        rows.Count.ShouldBe(1);
        rows[0].Evidence.ShouldContain("mirror=unknown");
    }

    [Test]
    public async Task Blocked_task_is_untouched()
    {
        await using var world = await World.StartAsync();
        await world.SeedFailedAsync(idleMinutes: 30, mirror: Mirror("equal", dirty: false),
            failure: null, status: AgentTaskStatus.Blocked);

        await world.RetireAsync();

        world.Client.SlotReleases.ShouldBeEmpty();
        world.Stopper.Killed.ShouldBeEmpty();
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Running);
        (await world.TaskStatusAsync()).ShouldBe(AgentTaskStatus.Blocked);
        (await world.HeldAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task Standing_agent_is_untouched()
    {
        await using var world = await World.StartAsync();
        await world.SeedFailedAsync(idleMinutes: 30, mirror: Mirror("equal", dirty: false),
            failure: AgentTaskFailureCode.CompletedWithoutProgress, alwaysOn: true);

        await world.RetireAsync();

        world.Client.SlotReleases.ShouldBeEmpty();
        world.Stopper.Killed.ShouldBeEmpty();
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task Warm_pooled_session_is_unchanged()
    {
        await using var world = await World.StartAsync();
        await world.SeedWarmOnlyAsync(idleMinutes: 15);
        await world.SeedTranscriptAsync();

        await world.RetireAsync();

        world.Client.SlotReleases.ShouldBeEmpty();
        world.Client.KillCalls.ShouldBe(0);
        world.Stopper.Killed.ShouldBeEmpty();
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Running);
        (await world.AgentStatusAsync()).ShouldBe(AgentStatus.Idle);
    }

    private static string Mirror(string relation, bool dirty, string? inspection = null) =>
        TaskProgressJson.SerializeEvidence(new CompletionProgressEvidence(
            1,
            CompletionProgressAssessment.NoAttributedProgress,
            "runner_reported_commit_not_pushed",
            RemoteSync: new RemoteSyncEvidence(
                1,
                RemoteSettlementSyncState.Synchronized,
                "refs/heads/feat/card-task-ghost",
                ObservedSha: new string('a', 40),
                ConfirmedSha: new string('a', 40),
                MirrorSha: new string('a', 40),
                MirrorRelation: relation,
                MirrorDirty: dirty,
                MirrorInspection: inspection)));

    private sealed class World : IAsyncDisposable
    {
        private readonly IsolatedTestSchema _schema;
        private readonly ServiceProvider _provider;
        private readonly string _connectionString;
        private readonly FakeTimeProvider _clock;
        public FakeSessionRunnerClient Client { get; }
        public RecordingSessionStopper Stopper { get; }
        public Guid SessionId { get; private set; }
        public Guid AgentId { get; private set; }
        private Guid _taskId;

        private World(IsolatedTestSchema schema, ServiceProvider provider, string connectionString,
            FakeTimeProvider clock, FakeSessionRunnerClient client, RecordingSessionStopper stopper)
        {
            _schema = schema;
            _provider = provider;
            _connectionString = connectionString;
            _clock = clock;
            Client = client;
            Stopper = stopper;
        }

        public static async Task<World> StartAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var clock = new FakeTimeProvider(Now);
            var client = new FakeSessionRunnerClient();
            var stopper = new RecordingSessionStopper { StopsSessionsIn = schema.ConnectionString };
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings
            {
                PoolReservedForCallerMinutes = 2,
                PoolIdleRetireMinutes = 60,
                PoolMaxIdlePerDirectory = 3,
                MaxConcurrentTasks = 512,
            }));
            services.AddOptions<AgentRegistrySettings>().Configure(s =>
            {
                s.DefaultDefinition = "claude";
                s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
            });
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(stopper);
            services.AddSingleton<ISessionRunnerDirectory>(new SlotDirectory(client));
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-c1719-wt"),
            });
            services.AddSingleton(new SeatOccupancyState());
            services.AddScoped<AgentTaskService>();
            services.AddSingleton<AgentTaskReplyService>();
            services.AddScoped<AgentTaskDispatcher>();
            var provider = services.BuildServiceProvider();
            return new World(schema, provider, schema.ConnectionString, clock, client, stopper);
        }

        public async Task RetireAsync()
        {
            await using var scope = _provider.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            await dispatcher.RetireIdleWarmAgentsAsync(CancellationToken.None);
        }

        public async Task<AgentTask> SeedFailedAsync(
            int idleMinutes, string? mirror, AgentTaskFailureCode? failure,
            AgentTaskStatus status = AgentTaskStatus.Failed, bool alwaysOn = false)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            SessionId = Guid.NewGuid();
            AgentId = Guid.NewGuid();
            _taskId = Guid.NewGuid();
            var cardId = Guid.NewGuid();
            var directory = "/work/worktrees/task-ghost";
            await using var db = Open();
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var columnId = Guid.NewGuid();
            db.Projects.Add(new Project { Id = projectId, Name = "c1719", CreatedAt = now, UpdatedAt = now });
            db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "c1719", CreatedAt = now, UpdatedAt = now });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = columnId,
                BoardId = boardId,
                Name = "Doing",
                StateKey = "doing",
                CardStatus = CardStatus.InProgress,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Cards.Add(new Card
            {
                Id = cardId,
                BoardId = boardId,
                BoardColumnId = columnId,
                Identifier = "C1719-" + cardId.ToString("N")[..8],
                Title = "CARD-1719",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.AgentSessions.Add(new AgentSession
            {
                Id = SessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                RunnerId = "server2",
                RunnerStoreId = Guid.NewGuid(),
                RunnerCwd = directory,
                Cwd = directory,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            db.Agents.Add(new Agent
            {
                Id = AgentId,
                Name = "task-ghost",
                Slug = "task-ghost",
                WorkingDirectory = directory,
                Details = "Ghost seat delegate.",
                Status = AgentStatus.Idle,
                Kind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                PoolClaudeEffort = ClaudeLaunchArgs.Effort(AgentModelLevel.Medium),
                IsPoolDelegate = true,
                AlwaysOn = alwaysOn,
                PoolIdleSince = now.AddMinutes(-idleMinutes),
                PersistentSessionId = SessionId.ToString("D"),
                LaunchEnvJson = "{}",
                CreatedAt = now,
                UpdatedAt = now,
            });
            var task = new AgentTask
            {
                Id = _taskId,
                RootTaskId = _taskId,
                Title = "CARD-1719 ghost",
                Goal = "the ghost seat",
                Role = AgentTaskRole.Code,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = directory,
                WorktreePath = directory,
                WorktreeBranch = "feat/card-task-ghost",
                RunnerId = "server2",
                AgentId = AgentId,
                AgentSessionId = SessionId,
                Status = status,
                CardId = cardId,
                FailureCode = failure,
                FailureReason = "runner_reported_commit_not_pushed",
                CompletionProgressEvidenceJson = mirror,
                Attempt = 1,
                CreatedAt = now.AddMinutes(-idleMinutes - 1),
                DispatchedAt = now.AddMinutes(-idleMinutes),
                CompletedAt = status == AgentTaskStatus.Blocked ? null : now.AddMinutes(-idleMinutes),
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            await SeedTranscriptAsync();
            return task;
        }

        public async Task SeedWarmOnlyAsync(int idleMinutes)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            SessionId = Guid.NewGuid();
            AgentId = Guid.NewGuid();
            var directory = "/work/worktrees/task-warm";
            await using var db = Open();
            db.AgentSessions.Add(new AgentSession
            {
                Id = SessionId,
                DefinitionName = "fake",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                RunnerId = "server2",
                RunnerStoreId = Guid.NewGuid(),
                RunnerCwd = directory,
                Cwd = directory,
                Cols = 120,
                Rows = 30,
                CreatedAt = now,
                StartedAt = now,
                LastSeenAt = now,
            });
            db.Agents.Add(new Agent
            {
                Id = AgentId,
                Name = "task-warm",
                Slug = "task-warm",
                WorkingDirectory = directory,
                Details = "Warm pool delegate.",
                Status = AgentStatus.Idle,
                Kind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium,
                PoolClaudeEffort = ClaudeLaunchArgs.Effort(AgentModelLevel.Medium),
                IsPoolDelegate = true,
                PoolIdleSince = now.AddMinutes(-idleMinutes),
                PoolReservedForRootTaskId = Guid.NewGuid(),
                PersistentSessionId = SessionId.ToString("D"),
                LaunchEnvJson = "{}",
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        public async Task SeedTranscriptAsync()
        {
            var at = _clock.GetUtcNow().UtcDateTime.AddMinutes(-20);
            await using var db = Open();
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = SessionId,
                Sequence = 1,
                Kind = TranscriptKinds.UserPrompt,
                Text = "Keep working",
                CreatedAt = at,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = SessionId,
                Sequence = 2,
                Kind = TranscriptKinds.TurnEnd,
                StopReason = "end_turn",
                CreatedAt = at.AddMinutes(1),
            });
            await db.SaveChangesAsync();
        }

        public async Task AgeCompletionAsync(Guid taskId, int minutes)
        {
            await using var db = Open();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.CompletedAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(-minutes);
            await db.SaveChangesAsync();
        }

        public async Task<SessionStatus> SessionStatusAsync()
        {
            await using var db = Open();
            return (await db.AgentSessions.SingleAsync(s => s.Id == SessionId)).Status;
        }

        public async Task<AgentTaskStatus> TaskStatusAsync()
        {
            await using var db = Open();
            return (await db.AgentTasks.SingleAsync(t => t.Id == _taskId)).Status;
        }

        public async Task<AgentStatus> AgentStatusAsync()
        {
            await using var db = Open();
            return (await db.Agents.SingleAsync(a => a.Id == AgentId)).Status;
        }

        public async Task<int> IncidentCountAsync(AgentIncidentKind kind)
        {
            await using var db = Open();
            return await db.AgentIncidents.CountAsync(i => i.Kind == kind && i.SessionId == SessionId);
        }

        public async Task<List<AttentionItemDto>> HeldAsync()
        {
            await using var db = Open();
            var attention = await new AttentionService(
                db,
                new AttentionServiceTests.FakeRunnerClient(),
                Options.Create(new SupervisionSettings()),
                Options.Create(new DelegationSettings { PoolIdleRetireMinutes = 60 }),
                _clock,
                NullLogger<AttentionService>.Instance,
                attention: Options.Create(new AttentionSettings { SeatWatchEnabled = true }))
                .GetAsync(CancellationToken.None, includeProgressProbe: false);
            return attention.Items.Where(i => i.Kind == AttentionKind.FailedTaskSessionHeld).ToList();
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _schema.DisposeAsync();
        }

        private AppDbContext Open() => new(TestDbFixture.CreateDbContextOptions(_connectionString));
    }

    private sealed class SlotDirectory(FakeSessionRunnerClient client) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => client;
        public ISessionRunnerClient Resolve(string? runnerId) => client;
        public IReadOnlyList<string> KnownRunnerIds => ["server2"];
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
        public Guid? GetLiveStoreId(string? runnerId) => null;
    }
}
