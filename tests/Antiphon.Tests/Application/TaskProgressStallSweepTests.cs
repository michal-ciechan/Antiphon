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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0153 S1 — <c>AgentTaskDispatcher.DetectStalledProgressAsync</c>. Fleet-global sweep
/// against the shared test Postgres, so <c>[NotInParallel]</c> with NO group key, and every
/// assertion is scoped to the session this test seeded.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class TaskProgressStallSweepTests
{
    [Test]
    public async Task Dedup_one_episode()
    {
        var (harness, _) = CreateHarness();
        await using var scenario = new Scenario();
        await scenario.SeedStalledAsync(dispatchedMinutesAgo: 110, spanMinutes: 100);

        await harness.DetectStalledProgressAsync(CancellationToken.None);
        (await scenario.IncidentsAsync()).ShouldHaveSingleItem().Severity.ShouldBe(AlertSeverity.Warning);

        // Second tick steps Warning → Error; a third does not write a third row.
        await harness.DetectStalledProgressAsync(CancellationToken.None);
        await harness.DetectStalledProgressAsync(CancellationToken.None);
        var rows = await scenario.IncidentsAsync();
        rows.Count.ShouldBe(2);
        rows.Select(i => i.Severity).ShouldBe([AlertSeverity.Warning, AlertSeverity.Error]);
    }

    [Test]
    public async Task Dedup_two_episodes()
    {
        var (harness, _) = CreateHarness();
        await using var scenario = new Scenario();
        await scenario.SeedStalledAsync();

        await harness.DetectStalledProgressAsync(CancellationToken.None);
        (await scenario.IncidentsAsync()).Count.ShouldBe(1);

        // The incident must predate the next novel row so the second stall is a NEW episode,
        // not "same episode, incident still after lastProgressAt".
        await scenario.AgeIncidentsAsync(minutesAgo: 80);
        await scenario.SeedEntriesAsync(
            (TranscriptKinds.ToolCall, "Edit", "{\"path\":\"resume.cs\",\"old\":\"a\",\"new\":\"b\"}", null, 35));
        await scenario.SeedLoopAsync(rows: 8, spanMinutes: 30, endMinutesAgo: 2);
        await harness.DetectStalledProgressAsync(CancellationToken.None);

        var rows = await scenario.IncidentsAsync();
        rows.Count.ShouldBe(2, "a novel row then another stall is a new episode");
        rows.ShouldAllBe(i => i.Severity == AlertSeverity.Warning);
    }

    [Test]
    public async Task Dedup_survives_a_restart()
    {
        var (first, _) = CreateHarness();
        await using var scenario = new Scenario();
        await scenario.SeedStalledAsync();
        await first.DetectStalledProgressAsync(CancellationToken.None);
        (await scenario.IncidentsAsync()).Count.ShouldBe(1);

        var (second, _) = CreateHarness();
        await second.DetectStalledProgressAsync(CancellationToken.None);
        (await scenario.IncidentsAsync()).Count.ShouldBe(1, "a fresh sweep instance must not re-raise");
    }

    [Test]
    public async Task Pull_before_raise_withholds_when_novel_rows_land()
    {
        var (harness, _) = CreateHarness();
        await using var scenario = new Scenario();
        await scenario.SeedStalledAsync();
        harness.CatchUpOverride = async (sessionId, ct) =>
        {
            await using var db = CreateContext();
            var seq = (await db.TranscriptEntries
                .Where(e => e.AgentSessionId == sessionId)
                .MaxAsync(e => (long?)e.Sequence, ct)) ?? 0;
            var at = DateTime.UtcNow.AddMinutes(-1);
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = seq + 1,
                Kind = TranscriptKinds.ToolCall,
                Uuid = $"pull-{Guid.NewGuid():N}",
                ToolName = "Edit",
                ToolInput = "{\"path\":\"novel.cs\",\"old\":\"a\",\"new\":\"b\"}",
                Timestamp = at,
                CreatedAt = at,
            });
            await db.SaveChangesAsync(ct);
        };

        var raised = await harness.DetectStalledProgressAsync(CancellationToken.None);
        raised.ShouldBe(0);
        (await scenario.IncidentsAsync()).Count.ShouldBe(0);
    }

    [Test]
    public async Task Channel_bound_is_Critical_at_the_Error_step_only()
    {
        var (harness, _) = CreateHarness();
        await using var scenario = new Scenario();
        await scenario.SeedStalledAsync(channelBound: true, dispatchedMinutesAgo: 110, spanMinutes: 100);

        await harness.DetectStalledProgressAsync(CancellationToken.None);
        (await scenario.IncidentsAsync()).ShouldHaveSingleItem().Severity.ShouldBe(AlertSeverity.Warning);

        await harness.DetectStalledProgressAsync(CancellationToken.None);
        var rows = await scenario.IncidentsAsync();
        rows.Count.ShouldBe(2);
        rows[^1].Severity.ShouldBe(AlertSeverity.Critical);
    }

    [Test]
    public async Task Unbound_owner_is_Error_at_the_Error_step()
    {
        var (harness, _) = CreateHarness();
        await using var scenario = new Scenario();
        await scenario.SeedStalledAsync(dispatchedMinutesAgo: 110, spanMinutes: 100);

        await harness.DetectStalledProgressAsync(CancellationToken.None);
        await harness.DetectStalledProgressAsync(CancellationToken.None);
        var rows = await scenario.IncidentsAsync();
        rows.Count.ShouldBe(2);
        rows[^1].Severity.ShouldBe(AlertSeverity.Error);
    }

    [Test]
    public async Task A_stalled_task_is_never_killed_escalated_or_failed_by_the_stall_sweep()
    {
        var (harness, stopper) = CreateHarness();
        await using var scenario = new Scenario();
        var taskId = await scenario.SeedStalledAsync();

        for (var i = 0; i < 10; i++)
            await harness.DetectStalledProgressAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Working);
        var session = await verify.AgentSessions.SingleAsync(s => s.Id == task.AgentSessionId);
        session.Status.ShouldBe(SessionStatus.Running);
        var agent = await verify.Agents.SingleAsync(a => a.Id == task.AgentId);
        agent.Status.ShouldBe(AgentStatus.Running);
        stopper.Killed.ShouldBeEmpty();
        (await scenario.IncidentsAsync()).Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task Disabled_means_silent()
    {
        var (harness, _) = CreateHarness(s => s.StallDetection.Enabled = false);
        await using var scenario = new Scenario();
        await scenario.SeedStalledAsync();

        var raised = await harness.DetectStalledProgressAsync(CancellationToken.None);
        raised.ShouldBe(0);
        (await scenario.IncidentsAsync()).Count.ShouldBe(0);
    }

    [Test]
    public async Task TickResult_counts_the_ninth_clock()
    {
        var (harness, _) = CreateHarness(s =>
        {
            s.MaxConcurrentTasks = 0;
            s.RolePolicy.Clear();
            s.FinalMessageGraceSeconds = 0;
            s.SubagentGraceMinutes = 0;
            s.PoolIdleRetireMinutes = 525_600;
            s.PoolMaxIdlePerDirectory = int.MaxValue;
            s.CheckEnabled = false;
        });
        harness.ProgressStallSweepFault = new InvalidOperationException("ninth clock exploded");

        var result = await harness.TickAsync(CancellationToken.None);
        result.SweepFailures.ShouldBeGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// CARD-1072. The sweep used to probe every open task before the stall gates.
    /// One tick must probe a past-stall mid-turn directory once, and must not probe
    /// a younger task, an idle session, or a runner-bound desktop checkout.
    /// </summary>
    [Test]
    public async Task a_stalled_directory_is_probed_once_and_a_young_idle_or_remote_task_is_not()
    {
        var (harness, _) = CreateHarness();
        var probe = new CountingProbe();
        harness.WorkspaceProbeOverride = probe;
        await using var world = new ProbeWorld();

        var stallMinutes = new DelegationSettings().StallDetection.StallMinutes;
        var fileAt = DateTime.UtcNow.AddHours(-5);
        var commitAt = DateTime.UtcNow.AddHours(-6);
        var quiet = new WorkspaceProgressArm(true, fileAt, commitAt, false);

        var youngDir = world.Directory("young");
        var idleDir = world.Directory("idle");
        var remoteDir = world.Directory("main");
        var localDir = world.Directory("local");
        var sharedDir = world.Directory("shared");
        var sharedAlias = sharedDir + Path.DirectorySeparatorChar;

        var young = await world.SeedWorkingAsync(youngDir, dispatchedMinutesAgo: Math.Max(1, stallMinutes / 6));
        var idle = await world.SeedIdleAsync(idleDir, dispatchedMinutesAgo: stallMinutes + 20);
        var remote = await world.SeedWorkingAsync(
            remoteDir, dispatchedMinutesAgo: stallMinutes + 20, runnerId: "server2",
            worktreePath: world.Directory("remote-wt"));
        var local = await world.SeedWorkingAsync(
            localDir, dispatchedMinutesAgo: stallMinutes + 20, workspace: WorkspaceMode.Worktree);
        var sharedA = await world.SeedWorkingAsync(
            sharedDir, dispatchedMinutesAgo: stallMinutes + 20, workspace: WorkspaceMode.Worktree);
        var sharedB = await world.SeedWorkingAsync(
            sharedAlias, dispatchedMinutesAgo: stallMinutes + 20, workspace: WorkspaceMode.Worktree);

        probe.Arm(localDir, quiet);
        probe.Arm(sharedDir, quiet);

        await harness.DetectStalledProgressAsync(CancellationToken.None);

        probe.Count(youngDir).ShouldBe(0);
        probe.Count(idleDir).ShouldBe(0);
        probe.Count(remoteDir).ShouldBe(0);
        probe.Count(localDir).ShouldBe(1);
        probe.Count(sharedDir).ShouldBe(1);
        probe.SharedFlag(localDir).ShouldBe(false);

        (await world.IncidentAsync(young.SessionId)).ShouldBeNull();
        (await world.IncidentAsync(idle.SessionId)).ShouldBeNull();

        var remoteIncident = await world.IncidentAsync(remote.SessionId);
        remoteIncident.ShouldNotBeNull();
        remoteIncident.Message.ShouldContain("no workspace arm");

        var localIncident = await world.IncidentAsync(local.SessionId);
        localIncident.ShouldNotBeNull();
        localIncident.Message.ShouldContain("last file change");
        localIncident.Message.ShouldContain("last commit");
        localIncident.FailureReason.ShouldNotBeNull();
        localIncident.FailureReason.ShouldContain("files=");
        localIncident.FailureReason.ShouldNotContain("files=none");
        localIncident.FailureReason.ShouldContain("commits=");
        localIncident.FailureReason.ShouldNotContain("commits=none");

        foreach (var sessionId in new[] { sharedA.SessionId, sharedB.SessionId })
        {
            var incident = await world.IncidentAsync(sessionId);
            incident.ShouldNotBeNull();
            incident.Message.ShouldContain("last file change");
        }
    }

    private static (AgentTaskDispatcher Dispatcher, RecordingSessionStopper Stopper) CreateHarness(
        Action<DelegationSettings>? configure = null)
    {
        var stopper = new RecordingSessionStopper();
        var settings = new DelegationSettings();
        configure?.Invoke(settings);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton(Options.Create(settings));
        services.AddOptions<AgentRegistrySettings>();
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-stall-wt"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddSingleton<AgentTaskReplyService>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), stopper);
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly Guid _sessionId = Guid.NewGuid();
        private readonly Guid _agentId = Guid.NewGuid();
        private readonly List<Guid> _tasks = [];
        private readonly List<Guid> _channels = [];
        private long _seq;

        public async Task<Guid> SeedStalledAsync(
            bool channelBound = false, int dispatchedMinutesAgo = 50, int spanMinutes = 40)
        {
            var dispatched = DateTime.UtcNow.AddMinutes(-dispatchedMinutesAgo);
            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = _sessionId,
                DefinitionName = "stall-sweep-test",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = dispatched,
                StartedAt = dispatched,
                LastSeenAt = DateTime.UtcNow,
            });
            var name = $"stall-{_agentId:N}"[..16];
            db.Agents.Add(new Agent
            {
                Id = _agentId,
                Name = name,
                Slug = name,
                WorkingDirectory = Path.GetTempPath(),
                Details = "CARD-0153 stall sweep test delegate.",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.Frontier,
                IsPoolDelegate = true,
                PersistentSessionId = _sessionId.ToString("D"),
                CreatedAt = dispatched,
                UpdatedAt = dispatched,
            });
            if (channelBound)
            {
                var channelId = Guid.NewGuid();
                db.ChatChannels.Add(new ChatChannel
                {
                    Id = channelId,
                    Provider = "telegram",
                    ExternalId = $"stall-{Guid.NewGuid():N}",
                    Kind = ChatChannelKind.Direct,
                    AgentId = _agentId,
                    Enabled = true,
                    CreatedAt = dispatched,
                    UpdatedAt = dispatched,
                });
                _channels.Add(channelId);
            }

            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "stall sweep test",
                Goal = "loop",
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                AgentId = _agentId,
                AgentSessionId = _sessionId,
                Status = AgentTaskStatus.Working,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            });
            await db.SaveChangesAsync();
            _tasks.Add(id);
            // Plant the loop's fingerprints near dispatch so lastProgressAt can age past the
            // 90-minute Error step; the recent 14-row loop keeps MinRowsInWindow satisfied.
            await SeedEntriesAsync(
                (TranscriptKinds.ToolCall, "Read", "{\"path\":\"src/loop.cs\"}", null, dispatchedMinutesAgo - 2),
                (TranscriptKinds.ToolResult, null, null, "file contents of loop.cs", dispatchedMinutesAgo - 3));
            await SeedLoopAsync(rows: 14, spanMinutes: Math.Min(spanMinutes, 40));
            return id;
        }

        public async Task SeedLoopAsync(int rows, int spanMinutes, int endMinutesAgo = 2)
        {
            var startMinutesAgo = endMinutesAgo + spanMinutes;
            var step = spanMinutes / Math.Max(rows - 1, 1.0);
            for (var i = 0; i < rows; i++)
            {
                var ago = startMinutesAgo - (int)Math.Round(i * step);
                var kind = i % 3 == 0 ? TranscriptKinds.ToolCall
                    : i % 3 == 1 ? TranscriptKinds.ToolResult
                    : TranscriptKinds.Thinking;
                await SeedEntriesAsync(kind switch
                {
                    TranscriptKinds.ToolCall => (kind, "Read", "{\"path\":\"src/loop.cs\"}", null, ago),
                    TranscriptKinds.ToolResult => (kind, null, null, "file contents of loop.cs", ago),
                    _ => (kind, null, null, $"thinking pass {i}", ago),
                });
            }
        }

        public async Task SeedEntriesAsync(
            params (string Kind, string? ToolName, string? ToolInput, string? Text, int MinutesAgo)[] entries)
        {
            await using var db = CreateContext();
            foreach (var (kind, toolName, toolInput, text, minutesAgo) in entries)
            {
                var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = _sessionId,
                    Sequence = ++_seq,
                    Kind = kind,
                    Uuid = $"stall-{Guid.NewGuid():N}",
                    Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
                    Text = text,
                    ToolName = toolName,
                    ToolInput = toolInput,
                    Timestamp = at,
                    CreatedAt = at,
                    StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
                });
            }
            await db.SaveChangesAsync();
        }

        public async Task AgeIncidentsAsync(int minutesAgo)
        {
            await using var db = CreateContext();
            var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
            await db.AgentIncidents
                .Where(i => i.SessionId == _sessionId && i.Kind == AgentIncidentKind.TaskProgressStalled)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.CreatedAt, at));
        }

        public async Task<List<AgentIncident>> IncidentsAsync()
        {
            await using var db = CreateContext();
            return await db.AgentIncidents.AsNoTracking()
                .Where(i => i.SessionId == _sessionId && i.Kind == AgentIncidentKind.TaskProgressStalled)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.AgentIncidents.Where(i => i.AgentId == _agentId).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(e => e.AgentSessionId == _sessionId).ExecuteDeleteAsync();
            await db.AgentTaskEvents.Where(e => _tasks.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => _channels.Contains(c.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == _sessionId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == _agentId).ExecuteDeleteAsync();
        }
    }

    private sealed record ProbeTask(Guid SessionId, Guid TaskId);

    /// <summary>CARD-1072 fixture. Each task has its own session so the sweep's working check is per row.</summary>
    private sealed class ProbeWorld : IAsyncDisposable
    {
        private readonly List<Guid> _sessions = [];
        private readonly List<Guid> _agents = [];
        private readonly List<Guid> _tasks = [];

        public string Directory(string name) =>
            Path.Combine(Path.GetTempPath(), $"c1072-{name}-{Guid.NewGuid():N}");

        public async Task<ProbeTask> SeedIdleAsync(string workingDirectory, int dispatchedMinutesAgo)
        {
            var dispatched = DateTime.UtcNow.AddMinutes(-dispatchedMinutesAgo);
            var ids = await InsertTaskAsync(
                workingDirectory, dispatched, WorkspaceMode.Worktree, runnerId: null, worktreePath: workingDirectory);
            return ids;
        }

        public async Task<ProbeTask> SeedWorkingAsync(
            string workingDirectory, int dispatchedMinutesAgo, string? runnerId = null,
            string? worktreePath = null, WorkspaceMode workspace = WorkspaceMode.Worktree)
        {
            var ids = await SeedIdleAsync(workingDirectory, dispatchedMinutesAgo);
            if (runnerId is not null || worktreePath is not null || workspace != WorkspaceMode.Worktree)
            {
                await using var db = CreateContext();
                var task = await db.AgentTasks.SingleAsync(t => t.Id == ids.TaskId);
                task.RunnerId = runnerId;
                task.WorktreePath = worktreePath ?? workingDirectory;
                task.RepoPath = workingDirectory;
                task.Workspace = workspace;
                await db.SaveChangesAsync();
            }

            await SeedLoopAsync(ids.SessionId, dispatchedMinutesAgo);
            return ids;
        }

        public async Task<AgentIncident?> IncidentAsync(Guid sessionId)
        {
            await using var db = CreateContext();
            return await db.AgentIncidents.AsNoTracking()
                .Where(i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.TaskProgressStalled)
                .OrderBy(i => i.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.AgentIncidents.Where(i => i.AgentId != null && _agents.Contains(i.AgentId.Value)).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(e => _sessions.Contains(e.AgentSessionId)).ExecuteDeleteAsync();
            await db.AgentTaskEvents.Where(e => _tasks.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => _sessions.Contains(s.Id)).ExecuteDeleteAsync();
            await db.Agents.Where(a => _agents.Contains(a.Id)).ExecuteDeleteAsync();
        }

        private async Task<ProbeTask> InsertTaskAsync(
            string workingDirectory, DateTime dispatched, WorkspaceMode workspace,
            string? runnerId, string? worktreePath)
        {
            var sessionId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var name = $"c1072-{agentId:N}"[..20];
            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId,
                DefinitionName = "stall-probe-test",
                AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running,
                Cwd = workingDirectory,
                Cols = 120,
                Rows = 30,
                CreatedAt = dispatched,
                StartedAt = dispatched,
                LastSeenAt = DateTime.UtcNow,
            });
            db.Agents.Add(new Agent
            {
                Id = agentId,
                Name = name,
                Slug = name,
                WorkingDirectory = workingDirectory,
                Details = "CARD-1072 stall probe test delegate.",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.Frontier,
                IsPoolDelegate = true,
                PersistentSessionId = sessionId.ToString("D"),
                RunnerId = runnerId,
                CreatedAt = dispatched,
                UpdatedAt = dispatched,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId,
                RootTaskId = taskId,
                Title = "stall probe test",
                Goal = "loop",
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = workspace,
                WorkingDirectory = workingDirectory,
                WorktreePath = worktreePath,
                RepoPath = workingDirectory,
                RunnerId = runnerId,
                AgentId = agentId,
                AgentSessionId = sessionId,
                Status = AgentTaskStatus.Working,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            });
            await db.SaveChangesAsync();
            _sessions.Add(sessionId);
            _agents.Add(agentId);
            _tasks.Add(taskId);
            return new ProbeTask(sessionId, taskId);
        }

        private async Task SeedLoopAsync(Guid sessionId, int dispatchedMinutesAgo)
        {
            await using var db = CreateContext();
            long seq = 0;
            void Add(string kind, string? toolName, string? toolInput, string? text, int minutesAgo)
            {
                var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = ++seq,
                    Kind = kind,
                    Uuid = $"c1072-{Guid.NewGuid():N}",
                    Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
                    Text = text,
                    ToolName = toolName,
                    ToolInput = toolInput,
                    Timestamp = at,
                    CreatedAt = at,
                });
            }

            Add(TranscriptKinds.ToolCall, "Read", "{\"path\":\"src/loop.cs\"}", null, dispatchedMinutesAgo - 2);
            Add(TranscriptKinds.ToolResult, null, null, "file contents of loop.cs", dispatchedMinutesAgo - 3);
            const int rows = 14;
            const int spanMinutes = 40;
            const int endMinutesAgo = 2;
            var startMinutesAgo = endMinutesAgo + spanMinutes;
            var step = spanMinutes / (double)(rows - 1);
            for (var i = 0; i < rows; i++)
            {
                var ago = startMinutesAgo - (int)Math.Round(i * step);
                var kind = i % 3 == 0 ? TranscriptKinds.ToolCall
                    : i % 3 == 1 ? TranscriptKinds.ToolResult
                    : TranscriptKinds.Thinking;
                if (kind == TranscriptKinds.ToolCall)
                    Add(kind, "Read", "{\"path\":\"src/loop.cs\"}", null, ago);
                else if (kind == TranscriptKinds.ToolResult)
                    Add(kind, null, null, "file contents of loop.cs", ago);
                else
                    Add(kind, null, null, $"thinking pass {i}", ago);
            }

            await db.SaveChangesAsync();
        }
    }

    private sealed class CountingProbe : IWorkspaceProgressProbe
    {
        private readonly Dictionary<string, WorkspaceProgressArm> _arms = new(PathComparer);
        private readonly List<(string? Directory, bool Shared)> _calls = [];

        public void Arm(string directory, WorkspaceProgressArm arm) =>
            _arms[Normalise(directory)] = arm;

        public int Count(string directory)
        {
            var key = Normalise(directory);
            return _calls.Count(call => call.Directory is not null && Normalise(call.Directory) == key);
        }

        public bool? SharedFlag(string directory)
        {
            var key = Normalise(directory);
            var call = _calls.FirstOrDefault(item => item.Directory is not null && Normalise(item.Directory) == key);
            return _calls.Any(item => item.Directory is not null && Normalise(item.Directory) == key)
                ? call.Shared
                : null;
        }

        public Task<WorkspaceProgressArm> ProbeProgressAsync(
            string? workingDirectory, DateTime since, bool sharedCheckout, CancellationToken ct)
        {
            _calls.Add((workingDirectory, sharedCheckout));
            if (workingDirectory is not null && _arms.TryGetValue(Normalise(workingDirectory), out var arm))
                return Task.FromResult(arm with { SharedCheckout = sharedCheckout });
            return Task.FromResult(new WorkspaceProgressArm(false, null, null, sharedCheckout));
        }

        private static string Normalise(string directory) =>
            DelegationWorkspaceResolver.NormalizeSeparators(Path.GetFullPath(directory));

        private static StringComparer PathComparer { get; } =
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }
}
