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
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Session health watch (spec slice 3): RC bridge re-arm -> restart-when-idle escalation and the
/// liveness probes, driven entirely through fakes so verdicts are deterministic. Idle gating is
/// pinned: a session with moving output is never probed or repaired.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentSupervision")]
public class SessionHealthTests
{
    [Test]
    public async Task Rc_degraded_re_arms_then_restarts_when_idle()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var harness = BuildHarness(tempRoot);
            var (agent, sessionId) = await CreateSupervisedRunningAgentAsync(harness, tempRoot);
            harness.Runner.Sessions = [RunnerDto(sessionId, pid: 4242, lastSeq: 10)];
            harness.Probe.Connections = 0;

            // Threshold 2: first zero-probe waits, second re-arms.
            (await harness.Health().TickAsync(CancellationToken.None)).ShouldBe(0);
            (await harness.Health().TickAsync(CancellationToken.None)).ShouldBe(1);
            harness.Actions.EnqueuedWhenIdle.ShouldContain(x => x.SessionId == sessionId && x.Text == "/remote-control");

            // Still dead after the single allowed re-arm: streak rebuilds, then restart.
            (await harness.Health().TickAsync(CancellationToken.None)).ShouldBe(0);
            (await harness.Health().TickAsync(CancellationToken.None)).ShouldBe(1);
            harness.Actions.KilledSessions.ShouldContain(sessionId);

            await using var verify = CreateContext();
            (await verify.AgentIncidents.AnyAsync(
                i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.RcReArmed)).ShouldBeTrue();
            (await verify.AgentIncidents.AnyAsync(
                i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.RcRestart)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Healthy_bridge_resets_streaks_and_takes_no_action()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var harness = BuildHarness(tempRoot);
            var (agent, sessionId) = await CreateSupervisedRunningAgentAsync(harness, tempRoot);
            harness.Runner.Sessions = [RunnerDto(sessionId, pid: 4242, lastSeq: 10)];
            harness.Probe.Connections = 2;

            for (var i = 0; i < 5; i++)
                (await harness.Health().TickAsync(CancellationToken.None)).ShouldBe(0);

            harness.Actions.EnqueuedWhenIdle.ShouldBeEmpty();
            harness.Actions.KilledSessions.ShouldBeEmpty();
            await using var verify = CreateContext();
            (await verify.AgentIncidents.AnyAsync(i => i.AgentId == agent.Id)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Busy_session_is_never_probed_or_repaired()
    {
        var tempRoot = NewTempRoot();
        try
        {
            // Idle gate of 5 min: a session whose sequence moves every tick never becomes idle.
            await using var harness = BuildHarness(tempRoot, idleQuietMinutes: 5);
            var (agent, sessionId) = await CreateSupervisedRunningAgentAsync(harness, tempRoot);
            harness.Probe.Connections = 0;

            for (var seq = 1; seq <= 5; seq++)
            {
                harness.Runner.Sessions = [RunnerDto(sessionId, pid: 4242, lastSeq: seq)];
                (await harness.Health().TickAsync(CancellationToken.None)).ShouldBe(0);
            }

            harness.Actions.EnqueuedWhenIdle.ShouldBeEmpty();
            harness.Actions.KilledSessions.ShouldBeEmpty();
            await using var verify = CreateContext();
            (await verify.AgentIncidents.AnyAsync(i => i.AgentId == agent.Id)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    // NOTE: there are deliberately NO periodic liveness probes here. The TUI echo probe was
    // removed 2026-07-21 (false-positive-killed healthy idle sessions) and the round-trip "pong"
    // probe was removed 2026-07-23 (spent model turns on healthy idle sessions). Deadness is only
    // checked when a real message delivery misbehaves; see
    // SessionMessageQueueDeliveryVerificationTests.

    [Test]
    public async Task No_probe_prompts_are_ever_sent_to_an_idle_session()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var harness = BuildHarness(tempRoot);
            var (agent, sessionId) = await CreateSupervisedRunningAgentAsync(harness, tempRoot);
            harness.Runner.Sessions = [RunnerDto(sessionId, pid: 4242, lastSeq: 10)];

            // Days of idle silence with a healthy bridge: no synthetic prompts, no restarts.
            for (var i = 0; i < 5; i++)
            {
                harness.Clock.Advance(TimeSpan.FromHours(12));
                (await harness.Health().TickAsync(CancellationToken.None)).ShouldBe(0);
            }

            harness.Actions.EnqueuedWhenIdle.ShouldBeEmpty();
            harness.Actions.KilledSessions.ShouldBeEmpty();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    // ---------- helpers ----------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-sessionhealth-tests-{Guid.NewGuid():N}");

    private static SessionRunnerSessionDto RunnerDto(Guid sessionId, int pid, long lastSeq) =>
        new(sessionId, pid, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, lastSeq);

    private static async Task<(AgentDetailDto Agent, Guid SessionId)> CreateSupervisedRunningAgentAsync(
        Harness harness, string tempRoot)
    {
        var workspace = Path.Combine(tempRoot, $"agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var agent = await harness.Scope.ServiceProvider.GetRequiredService<AgentService>()
            .CreateAsync(new CreateAgentRequest("Health", workspace), CancellationToken.None);

        var db = harness.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = harness.Clock.GetUtcNow().UtcDateTime;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = workspace,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        };
        db.AgentSessions.Add(session);
        var entity = await db.Agents.SingleAsync(a => a.Id == agent.Id);
        entity.AlwaysOn = true;
        entity.RemoteControlEnabled = true;
        entity.PersistentSessionId = session.Id.ToString("D");
        await db.SaveChangesAsync();
        return (agent, session.Id);
    }

    private static Harness BuildHarness(
        string tempRoot,
        int idleQuietMinutes = 0)
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var runner = new ControllableRunnerClient();
        var probe = new FakeRcProbe();
        var actions = new RecordingHealthActions();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(TestDbFixture.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        var eventBus = new MockEventBus();
        services.AddSingleton(eventBus);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings
        {
            RcWatch = new RcWatchSettings
            {
                Enabled = true,
                IdleQuietMinutes = idleQuietMinutes,
                ConsecutiveFailedProbesBeforeAction = 2,
                ReArmAttemptsBeforeRestart = 1,
                ReArmSettleMinutes = 0,
            },
        }));
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            SessionLogPath = Path.Combine(tempRoot, "session-logs"),
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            InternalTrackerRepositoryPathPrefix = tempRoot,
        }));
        services.AddSingleton<ISessionRunnerClient>(runner);
        services.AddSingleton<IRcBridgeProbe>(probe);
        services.AddSingleton<ISessionHealthActions>(actions);
        services.AddSingleton<SessionHealthStateStore>();
        services.AddScoped<SessionHealthService>();
        services.AddScoped<AgentSupervisorService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IAlertRouter, NullAlertRouter>();
        services.AddScoped<AgentControlService>();
        services.AddScoped<AgentService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<BoardService>();
        services.AddScoped<CardService>();
        services.AddScoped<OrchestratorService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
            new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
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
        services.AddSingleton<IAgentProtocolAdapterFactory>(new ThrowingAdapterFactory());
        services.AddSingleton<IWorktreeManager>(new NoWorktreeManager());
        services.AddSingleton<IWorkspaceHookRunner>(
            new Antiphon.Server.Infrastructure.WorkspaceHooks.WorkspaceHookRunner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<
                    Antiphon.Server.Infrastructure.WorkspaceHooks.WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<Antiphon.Server.Application.Interfaces.IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(
                new System.IO.Abstractions.FileSystem()));
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(provider, scope, runner, probe, actions, clock);
    }

    private static async Task CleanupAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var agentIds = await db.Agents
            .Where(a => a.WorkingDirectory.StartsWith(tempRoot))
            .Select(a => a.Id)
            .ToListAsync();
        await db.AgentIncidents.Where(i => agentIds.Contains(i.AgentId)).ExecuteDeleteAsync();
        await db.Alerts.Where(a => a.AgentId != null && agentIds.Contains(a.AgentId.Value)).ExecuteDeleteAsync();
        await db.AgentSupervisionStates.Where(s => agentIds.Contains(s.AgentId)).ExecuteDeleteAsync();
        await db.Agents.Where(a => agentIds.Contains(a.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
        await db.AgentSessions.Where(s => s.CardId == null && s.Cwd.StartsWith(tempRoot)).ExecuteDeleteAsync();
        await db.Agents.Where(a => agentIds.Contains(a.Id)).ExecuteDeleteAsync();
        try
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort.
        }
    }

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        ControllableRunnerClient Runner,
        FakeRcProbe Probe,
        RecordingHealthActions Actions,
        MutableTimeProvider Clock) : IAsyncDisposable
    {
        public SessionHealthService Health()
        {
            var scope = Provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<SessionHealthService>();
        }

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeRcProbe : IRcBridgeProbe
    {
        public int Connections { get; set; } = 2;

        public RcProbeResult Probe(int pid) => new(Armed: true, Connections, StateFileFound: true);
    }

    private sealed class RecordingHealthActions : ISessionHealthActions
    {
        public List<(Guid SessionId, string Text)> EnqueuedWhenIdle { get; } = [];
        public List<Guid> KilledSessions { get; } = [];
        public List<(Guid SessionId, string Input)> RawInputs { get; } = [];
        public string ScreenText { get; set; } = "screen";

        public Task EnqueueWhenIdleAsync(Guid sessionId, string text, CancellationToken ct)
        {
            EnqueuedWhenIdle.Add((sessionId, text));
            return Task.CompletedTask;
        }

        public Task KillSessionAsync(Guid sessionId, CancellationToken ct)
        {
            KilledSessions.Add(sessionId);
            return Task.CompletedTask;
        }

        public Task<string> SnapshotScreenAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(ScreenText);

        public Task SendRawInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            RawInputs.Add((sessionId, input));
            return Task.CompletedTask;
        }
    }

    private sealed class ControllableRunnerClient : ISessionRunnerClient
    {
        public IReadOnlyList<SessionRunnerSessionDto> Sessions { get; set; } = [];

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult(Sessions);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
            => throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class ThrowingAdapterFactory : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) =>
            throw new NotSupportedException("Session health tests never launch processes.");
    }

    private sealed class NoWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
