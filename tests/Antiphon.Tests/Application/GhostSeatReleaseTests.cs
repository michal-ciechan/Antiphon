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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using System.Data.Common;

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
    [Arguments("transcript")]
    [Arguments("session")]
    [Arguments("codex")]
    public async Task Completed_followup_resets_idle_until_ten_minutes_after_last_activity(string activity)
    {
        await using var world = await World.StartAsync();
        await world.SeedFailedAsync(30, Mirror("equal", false), AgentTaskFailureCode.CompletedWithoutProgress);
        await world.ActivityAsync(activity, minutesAgo: 9);

        await world.RetireAsync();
        world.Client.SlotReleases.ShouldBeEmpty("V-1: a completed followup restarts the idle clock");
        world.Advance(TimeSpan.FromMinutes(1));
        await world.RetireAsync();
        world.Client.SlotReleases.Count.ShouldBe(1, "V-1: release at the activity idle bound");
        world.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task Failed_owner_temporarily_blocked_during_candidate_read_keeps_the_seat()
    {
        var boundary = new CandidateReadBoundary();
        await using var world = await World.StartAsync(boundary);
        var task = await world.SeedFailedAsync(30, Mirror("equal", false), AgentTaskFailureCode.CompletedWithoutProgress);
        boundary.BeforeRead = () => world.ChangeStatusAsync(task.Id, AgentTaskStatus.Blocked);
        boundary.OnRead = () => world.ChangeStatusAsync(task.Id, AgentTaskStatus.Failed);

        await world.RetireAsync();

        boundary.BeforeCalls.ShouldBe(1, "G05: change status after owner and busy snapshots, before candidate SQL");
        boundary.Calls.ShouldBe(1, "restore Failed only after the candidate SQL snapshot is fixed");
        boundary.MaterializedStatuses.ShouldBe([AgentTaskStatus.Blocked]);
        (await world.TaskStatusAsync()).ShouldBe(AgentTaskStatus.Failed,
            "later guards see Failed, so they cannot mask a missing candidate-status guard");
        world.Client.SlotReleases.ShouldBeEmpty("G05: the materialized Blocked candidate cannot authorize release");
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Running);
        world.Stopper.Killed.ShouldBeEmpty();

        await world.RetireAsync();
        world.Client.SlotReleases.ShouldHaveSingleItem().SessionId.ShouldBe(world.SessionId,
            "a fresh Failed snapshot releases the otherwise eligible seat on the next sweep");
    }

    [Test]
    [Arguments(AgentTaskStatus.Queued)]
    [Arguments(AgentTaskStatus.Dispatched)]
    [Arguments(AgentTaskStatus.Working)]
    [Arguments(AgentTaskStatus.Blocked)]
    public async Task Older_open_owner_closed_after_busy_snapshot_keeps_the_seat_for_this_sweep(AgentTaskStatus status)
    {
        var boundary = new CandidateReadBoundary();
        await using var world = await World.StartAsync(boundary);
        var failed = await world.SeedFailedAsync(30, Mirror("equal", false), AgentTaskFailureCode.CompletedWithoutProgress);
        var older = await world.SeedOlderOpenOwnerAsync(failed, status);
        boundary.OnRead = () => world.ChangeStatusAsync(older, AgentTaskStatus.Completed);

        await world.RetireAsync();

        boundary.Calls.ShouldBe(1, "G07: the newer Failed task remains the current owner and reaches candidate loading");
        boundary.MaterializedStatuses.ShouldBe([AgentTaskStatus.Failed]);
        (await world.StatusAsync(older)).ShouldBe(AgentTaskStatus.Completed,
            "the later open-task query cannot mask a missing busy-snapshot guard");
        world.Client.SlotReleases.ShouldBeEmpty("G07: an agent busy in this sweep's snapshot keeps its seat");
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Running);
        world.Stopper.Killed.ShouldBeEmpty();

        await world.RetireAsync();
        world.Client.SlotReleases.ShouldHaveSingleItem().SessionId.ShouldBe(world.SessionId,
            "the next sweep observes the closed older owner and releases the eligible seat");
    }

    [Test]
    [Arguments("dirty")]
    [Arguments("unpushed")]
    [Arguments("unknown")]
    public async Task Mirror_changed_after_candidate_read_keeps_the_seat(string shape)
    {
        var boundary = new CandidateReadBoundary();
        await using var world = await World.StartAsync(boundary);
        var task = await world.SeedFailedAsync(30, Mirror("equal", false), AgentTaskFailureCode.CompletedWithoutProgress);
        boundary.OnRead = () => world.ChangeMirrorAsync(task.Id, shape switch
        {
            "dirty" => Mirror("equal", true),
            "unpushed" => Mirror("descends", false),
            _ => null,
        });

        await world.RetireAsync();

        boundary.Calls.ShouldBe(1, "V-2: change the record after the candidate snapshot was taken");
        world.Client.SlotReleases.ShouldBeEmpty("V-2: stale clean evidence cannot authorize release");
        (await world.SessionStatusAsync()).ShouldBe(SessionStatus.Running);
    }

    [Test]
    public async Task One_sweep_releases_at_most_five_seats_and_the_next_sweep_drains_the_remainder()
    {
        await using var world = await World.StartAsync();
        for (var i = 0; i < 7; i++)
            await world.SeedFailedAsync(30, Mirror("equal", false), AgentTaskFailureCode.CompletedWithoutProgress);

        await world.RetireAsync();
        world.Client.SlotReleases.Count.ShouldBe(5, "V-3: runner API calls are bounded per sweep");
        await world.RetireAsync();
        world.Client.SlotReleases.Count.ShouldBe(7, "V-3: deferred seats remain eligible next sweep");
        world.Client.SlotReleases.Select(r => r.SessionId).Distinct().Count().ShouldBe(7);
        world.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task Held_attention_describes_conditional_release_and_preservation()
    {
        await using var world = await World.StartAsync();
        await world.SeedFailedAsync(9, Mirror("equal", false), AgentTaskFailureCode.CompletedWithoutProgress);

        var row = (await world.HeldAsync()).ShouldHaveSingleItem();
        row.Evidence.ShouldContain("10 idle minutes");
        row.Evidence.ShouldContain("dirty, unpushed or unknown");
        row.Evidence.ShouldNotContain("never killed");
        row.Evidence.ShouldContain("mirror=clean-equal");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Automatic_coordinator_owns_both_hold_and_release_without_force_fallback(bool held)
    {
        await using var f = await RunnerSeatReleaseFixture.CreateAsync(AgentTaskStatus.Failed);
        await f.EditAsync((task, agent) =>
        {
            task.CompletedAt = f.Now.AddMinutes(-30);
            task.CompletionProgressEvidenceJson = Mirror("equal", false);
            agent.PoolIdleSince = f.Now.AddMinutes(-30);
        });
        if (held)
            f.Wire.Qualified = f.Wire.Qualified with { Status = TerminalSeatQualificationStatus.Working, Token = null };
        using (var scope = f.Harness.Provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IOptions<TerminalRunnerSeatReleaseOptions>>()
                .Value.AutomaticEnabled.ShouldBeTrue();
            scope.ServiceProvider.GetRequiredService<TerminalRunnerSeatReleaseService>()
                .OwnsAutomaticPath(await f.TaskAsync()).ShouldBeTrue();
        }

        await f.SweepAsync(janitor: true);
        await f.SweepAsync(janitor: true);

        f.Wire.ForceCommands.ShouldBe(0, "V-5: the ghost janitor never falls through the coordinator");
        f.Wire.ConditionalCommands.ShouldBe(held ? 0 : 1, "V-5: no gap and no double release");
        f.RecordedStops.Killed.ShouldBeEmpty();
        await using var db = f.Db();
        var release = await db.RunnerSeatReleases.SingleAsync();
        if (held)
            release.ReasonCode.ShouldBe("Working");
        else
        {
            release.State.ShouldBe(RunnerSeatReleaseState.Confirmed);
            release.OutcomeCode.ShouldBe(nameof(TerminalSeatReleaseOutcome.Released));
        }
    }

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

        world.Advance(TimeSpan.FromMinutes(1));
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

        public static async Task<World> StartAsync(CandidateReadBoundary? boundary = null)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var clock = new FakeTimeProvider(Now);
            var client = new FakeSessionRunnerClient();
            var stopper = new RecordingSessionStopper { StopsSessionsIn = schema.ConnectionString };
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o =>
            {
                o.UseNpgsql(schema.ConnectionString);
                if (boundary is not null) o.AddInterceptors(boundary);
            });
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
            db.Projects.Add(new Project { Id = projectId, Name = "c1719-" + projectId.ToString("N"), CreatedAt = now, UpdatedAt = now });
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
                CreatedAt = now.AddMinutes(-idleMinutes - 1),
                StartedAt = now.AddMinutes(-idleMinutes - 1),
                LastSeenAt = now.AddMinutes(-idleMinutes),
            });
            db.Agents.Add(new Agent
            {
                Id = AgentId,
                Name = "task-ghost",
                Slug = "task-ghost-" + AgentId.ToString("N"),
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

        public void Advance(TimeSpan elapsed) => _clock.Advance(elapsed);

        public async Task ActivityAsync(string activity, int minutesAgo)
        {
            await using var db = Open();
            var at = _clock.GetUtcNow().UtcDateTime.AddMinutes(-minutesAgo);
            if (activity == "session")
            {
                await db.AgentSessions.Where(s => s.Id == SessionId)
                    .ExecuteUpdateAsync(s => s.SetProperty(s => s.LastSeenAt, at));
                return;
            }
            if (activity == "codex")
            {
                await db.AgentSessions.Where(s => s.Id == SessionId)
                    .ExecuteUpdateAsync(s => s.SetProperty(s => s.CodexTokenCountAt, at));
                return;
            }
            db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = SessionId,
                Sequence = 3, Kind = TranscriptKinds.UserPrompt, Text = "operator followup", CreatedAt = at.AddSeconds(-1) });
            db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = SessionId,
                Sequence = 4, Kind = TranscriptKinds.TurnEnd, StopReason = "end_turn", CreatedAt = at });
            await db.SaveChangesAsync();
        }

        public async Task ChangeMirrorAsync(Guid taskId, string? evidence)
        {
            await using var db = Open();
            await db.AgentTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CompletionProgressEvidenceJson, evidence));
        }

        public async Task<Guid> SeedOlderOpenOwnerAsync(AgentTask failed, AgentTaskStatus status)
        {
            await using var db = Open();
            var older = new AgentTask
            {
                Id = Guid.NewGuid(), Title = "older open owner", Goal = "keep the agent busy",
                AgentId = AgentId, AgentSessionId = SessionId, Status = status,
                CreatedAt = failed.CreatedAt.AddMinutes(-1),
                DispatchedAt = failed.DispatchedAt!.Value.AddMinutes(-1),
            };
            db.AgentTasks.Add(older);
            await db.SaveChangesAsync();
            return older.Id;
        }

        public async Task ChangeStatusAsync(Guid taskId, AgentTaskStatus status)
        {
            await using var db = Open();
            await db.AgentTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status));
        }

        public async Task<AgentTaskStatus> StatusAsync(Guid taskId)
        {
            await using var db = Open();
            return await db.AgentTasks.Where(t => t.Id == taskId).Select(t => t.Status).SingleAsync();
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

    // A real database read returns the old candidate snapshot while another writer commits
    // new settlement evidence. No production hook or timing race is needed for this witness.
    private sealed class CandidateReadBoundary : DbCommandInterceptor, IMaterializationInterceptor
    {
        public Func<Task>? BeforeRead { get; set; }
        public Func<Task>? OnRead { get; set; }
        public int BeforeCalls { get; private set; }
        public int Calls { get; private set; }
        public List<AgentTaskStatus> MaterializedStatuses { get; } = [];
        private bool _captureMaterialization;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (BeforeRead is { } callback && IsCandidateRead(command))
            {
                BeforeRead = null;
                BeforeCalls++;
                await callback();
            }
            return result;
        }

        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (_captureMaterialization && entity is AgentTask task)
                MaterializedStatuses.Add(task.Status);
            return entity;
        }

        private static bool IsCandidateRead(DbCommand command) => command.Parameters.Cast<DbParameter>()
            .Any(p => p.ParameterName.Contains("ownerIds", StringComparison.Ordinal));

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            _captureMaterialization = IsCandidateRead(command);
            if (OnRead is { } callback && _captureMaterialization)
            {
                OnRead = null;
                Calls++;
                await callback();
            }
            return result;
        }
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
