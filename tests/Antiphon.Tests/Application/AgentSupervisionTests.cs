using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
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

/// <summary>
/// Always-on supervision (spec: 2026-07-20-always-on-agents-and-alerting.md, slice 1):
/// AlwaysOn agents are started when down, restarted after crashes on a never-give-up backoff
/// ladder (30-day cap) with tier-escalation incidents, respect the user-stop suspend latch, and
/// never touch healthy or non-always-on agents.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentSupervision")]
public class AgentSupervisionTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task AlwaysOn_agent_with_no_session_is_scheduled_then_started()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var harness = BuildHarness(tempRoot, [new FakeAgentProtocolAdapter()]);
            var agent = await CreateAlwaysOnAgentAsync(harness, tempRoot);

            // Tick 1: schedules the boot start (attempt 1, base backoff).
            (await harness.Supervisor().TickAsync(CancellationToken.None)).ShouldBe(1);
            await using (var verify = CreateContext())
            {
                var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == agent.Id);
                state.NextRestartAt.ShouldNotBeNull();
                (await verify.AgentIncidents.CountAsync(
                    i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.RestartScheduled)).ShouldBe(1);
            }

            // Tick 2 (past the due time): actually starts the agent.
            harness.Clock.Advance(TimeSpan.FromSeconds(10));
            (await harness.Supervisor().TickAsync(CancellationToken.None)).ShouldBe(1);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            var detail = await harness.Scope.ServiceProvider.GetRequiredService<AgentService>()
                .GetByIdAsync(agent.Id, CancellationToken.None);
            detail.Status.ShouldBe(AgentStatus.Working);
            detail.LiveSession.ShouldNotBeNull();

            await using (var verify = CreateContext())
            {
                var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == agent.Id);
                state.NextRestartAt.ShouldBeNull();
            }
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Crash_is_recorded_and_restart_scheduled_with_backoff_details()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var harness = BuildHarness(
                tempRoot, [new FakeAgentProtocolAdapter(), new FakeAgentProtocolAdapter()]);
            var agent = await CreateAlwaysOnAgentAsync(harness, tempRoot);

            // Boot it via supervision, then crash it via the runtime exit path (exit 1 => Failed).
            await harness.Supervisor().TickAsync(CancellationToken.None);
            harness.Clock.Advance(TimeSpan.FromSeconds(10));
            await harness.Supervisor().TickAsync(CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            var sessionId = Guid.Parse((await harness.Scope.ServiceProvider
                .GetRequiredService<AgentService>().GetByIdAsync(agent.Id, CancellationToken.None))
                .PersistentSessionId!);
            var runtime = harness.Provider.GetRequiredService<AgentSessionRuntime>();
            await runtime.ObserveExitAsync(sessionId, 1, AgentExitReason.ProcessExited, CancellationToken.None);

            // Next tick records the crash and schedules the retry.
            await harness.Supervisor().TickAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var incidents = await verify.AgentIncidents
                .Where(i => i.AgentId == agent.Id)
                .OrderBy(i => i.CreatedAt)
                .ToListAsync();
            incidents.ShouldContain(i => i.Kind == AgentIncidentKind.Crash);
            var scheduled = incidents.Last(i => i.Kind == AgentIncidentKind.RestartScheduled);
            scheduled.Message.ShouldContain("attempt");
            scheduled.Message.ShouldContain("scheduled for");
            scheduled.Message.ShouldContain("backing off");

            var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == agent.Id);
            state.NextRestartAt.ShouldNotBeNull();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Stop_suspends_supervision_until_manual_start()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var harness = BuildHarness(
                tempRoot, [new FakeAgentProtocolAdapter(), new FakeAgentProtocolAdapter()]);
            var agent = await CreateAlwaysOnAgentAsync(harness, tempRoot);

            await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await harness.Control.StopAsync(agent.Id, CancellationToken.None);

            await using (var verify = CreateContext())
            {
                var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == agent.Id);
                state.Suspended.ShouldBeTrue();
                (await verify.AgentIncidents.AnyAsync(
                    i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.SuspendedByUser)).ShouldBeTrue();
            }

            // Suspended: ticks do nothing, forever.
            harness.Clock.Advance(TimeSpan.FromDays(2));
            (await harness.Supervisor().TickAsync(CancellationToken.None)).ShouldBe(0);

            // Manual start lifts the latch.
            await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            await using (var verify = CreateContext())
            {
                var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == agent.Id);
                state.Suspended.ShouldBeFalse();
                (await verify.AgentIncidents.AnyAsync(
                    i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.ResumedByUser)).ShouldBeTrue();
            }
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Backoff_ladder_reaches_30_day_cap_and_escalates_once_per_tier()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var harness = BuildHarness(tempRoot, []);
            var supervisor = harness.Supervisor();

            // The curve itself: doubling from 5s, capped at 30 days.
            supervisor.Backoff(0).ShouldBe(TimeSpan.FromSeconds(5));
            supervisor.Backoff(4).ShouldBe(TimeSpan.FromSeconds(80));
            supervisor.Backoff(10).ShouldBe(TimeSpan.FromSeconds(5 * 1024));
            supervisor.Backoff(19).ShouldBe(TimeSpan.FromDays(30));
            supervisor.Backoff(40).ShouldBe(TimeSpan.FromDays(30));

            // Escalation fires once per tier: fabricate a failing state deep in the ladder.
            var agent = await CreateAlwaysOnAgentAsync(harness, tempRoot);
            await using (var db = CreateContext())
            {
                db.AgentSupervisionStates.Add(new AgentSupervisionState
                {
                    AgentId = agent.Id,
                    ConsecutiveFailures = 10, // Backoff(11) after increment ≈ 2.8h => hourly tier
                    LastAttemptAt = harness.Clock.GetUtcNow().UtcDateTime.AddMinutes(-1),
                    UpdatedAt = harness.Clock.GetUtcNow().UtcDateTime,
                });
                await db.SaveChangesAsync();
            }

            await harness.Supervisor().TickAsync(CancellationToken.None); // schedules; crosses hourly tier
            await using (var verify = CreateContext())
            {
                var escalations = await verify.AgentIncidents
                    .Where(i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.BackoffEscalated)
                    .ToListAsync();
                escalations.Count.ShouldBe(1);
                escalations[0].Severity.ShouldBe(AlertSeverity.Warning);
            }

            // Same tier again -> no duplicate escalation incident.
            await harness.Supervisor().TickAsync(CancellationToken.None);
            await using (var verify = CreateContext())
            {
                (await verify.AgentIncidents.CountAsync(
                    i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.BackoffEscalated)).ShouldBe(1);
            }

            // Deep ladder crosses the daily tier -> one Critical escalation.
            await using (var db = CreateContext())
            {
                var state = await db.AgentSupervisionStates.SingleAsync(s => s.AgentId == agent.Id);
                state.ConsecutiveFailures = 17;
                state.NextRestartAt = null;
                await db.SaveChangesAsync();
            }

            var tick3Actions = await harness.Supervisor().TickAsync(CancellationToken.None);
            await using (var verify = CreateContext())
            {
                tick3Actions.ShouldBe(1,
                    "tick 3 should have scheduled the deep-ladder retry; supervisor log:\n"
                    + string.Join("\n", harness.SupervisorLog));
                var after = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == agent.Id);
                after.ConsecutiveFailures.ShouldBe(18, "tick should have incremented 17 -> 18");
                after.NextRestartAt.ShouldNotBeNull("tick should have scheduled the deep-ladder retry");
                after.LastEscalationTier.ShouldBe(2, $"delay was {after.NextRestartAt - harness.Clock.GetUtcNow().UtcDateTime}");

                var critical = await verify.AgentIncidents
                    .Where(i => i.AgentId == agent.Id
                        && i.Kind == AgentIncidentKind.BackoffEscalated
                        && i.Severity == AlertSeverity.Critical)
                    .ToListAsync();
                critical.Count.ShouldBe(1);
            }
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Healthy_uptime_resets_the_ladder()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var harness = BuildHarness(tempRoot, []);
            var agent = await CreateAlwaysOnAgentAsync(harness, tempRoot);
            var now = harness.Clock.GetUtcNow().UtcDateTime;

            await using (var db = CreateContext())
            {
                var session = new AgentSession
                {
                    Id = Guid.NewGuid(),
                    DefinitionName = "fake",
                    AgentKind = AgentKind.Raw,
                    Status = SessionStatus.Running,
                    Cwd = tempRoot,
                    Cols = 100,
                    Rows = 25,
                    CreatedAt = now.AddMinutes(-30),
                    StartedAt = now.AddMinutes(-30),
                    LastSeenAt = now,
                };
                db.AgentSessions.Add(session);
                db.AgentSupervisionStates.Add(new AgentSupervisionState
                {
                    AgentId = agent.Id,
                    ConsecutiveFailures = 7,
                    LastEscalationTier = 1,
                    UpdatedAt = now,
                });
                var dbAgent = await db.Agents.SingleAsync(a => a.Id == agent.Id);
                dbAgent.PersistentSessionId = session.Id.ToString("D");
                await db.SaveChangesAsync();
            }

            await harness.Supervisor().TickAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var state = await verify.AgentSupervisionStates.SingleAsync(s => s.AgentId == agent.Id);
            state.ConsecutiveFailures.ShouldBe(0);
            state.LastEscalationTier.ShouldBe(0);
            (await verify.AgentIncidents.AnyAsync(
                i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.Recovered)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Non_always_on_and_healthy_agents_are_untouched()
    {
        var tempRoot = NewTempRoot();
        try
        {
            await using var harness = BuildHarness(tempRoot, []);

            // Non-always-on agent: ignored entirely.
            var workspace = Path.Combine(tempRoot, "plain-agent");
            Directory.CreateDirectory(workspace);
            var plain = await harness.Scope.ServiceProvider.GetRequiredService<AgentService>()
                .CreateAsync(new CreateAgentRequest("Plain", workspace), CancellationToken.None);

            (await harness.Supervisor().TickAsync(CancellationToken.None)).ShouldBe(0);

            await using var verify = CreateContext();
            (await verify.AgentSupervisionStates.AnyAsync(s => s.AgentId == plain.Id)).ShouldBeFalse();
            (await verify.AgentIncidents.AnyAsync(i => i.AgentId == plain.Id)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    // ---------- helpers ----------

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-supervision-tests-{Guid.NewGuid():N}");

    private static async Task<AgentDetailDto> CreateAlwaysOnAgentAsync(Harness harness, string tempRoot)
    {
        var workspace = Path.Combine(tempRoot, $"agent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var agent = await harness.Scope.ServiceProvider.GetRequiredService<AgentService>()
            .CreateAsync(new CreateAgentRequest("Supervised", workspace), CancellationToken.None);

        // Flip AlwaysOn through the SAME scope context so the harness's tracked instance (used
        // by Control.Start/Stop) agrees with the database.
        var db = harness.Scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await db.Agents.SingleAsync(a => a.Id == agent.Id);
        entity.AlwaysOn = true;
        await db.SaveChangesAsync();
        return agent;
    }

    private static Harness BuildHarness(string tempRoot, IReadOnlyList<IAgentProtocolAdapter> adapters)
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var supervisorLog = new List<string>();
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
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 1_000,
            KillGraceMs = 100,
            SessionLogPath = Path.Combine(tempRoot, "session-logs")
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            InternalTrackerRepositoryPathPrefix = tempRoot
        }));
        services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings
        {
            TickSeconds = 1,
            BackoffBaseSeconds = 5,
            HealthyUptimeResetMinutes = 10,
            FreshAfterResumeFailures = 2,
        }));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
            new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
            {
                DefaultDefinition = "fake",
                Definitions = { ["fake"] = new AgentDefinition { Kind = "Raw", Exe = Cmd } }
            }));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IWorktreeManager>(new NoWorktreeManager());
        services.AddSingleton<IAgentProtocolAdapterFactory>(new QueueAdapterFactory(adapters));
        services.AddSingleton<IWorkspaceHookRunner>(
            new Antiphon.Server.Infrastructure.WorkspaceHooks.WorkspaceHookRunner(
                NullLogger<Antiphon.Server.Infrastructure.WorkspaceHooks.WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddScoped<OrchestratorService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<AgentService>();
        services.AddScoped<AgentControlService>();
        services.AddScoped<AgentSupervisorService>();
        services.AddSingleton<ISessionRunnerClient>(new StubRunnerClient());
        services.AddSingleton<Antiphon.Server.Application.Interfaces.IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(
                new System.IO.Abstractions.FileSystem()));
        services.AddScoped<BoardService>();
        services.AddScoped<CardService>();
        services.AddLogging();
        // Capture the supervisor's own log lines (its per-agent catch would otherwise swallow
        // failures invisibly in tests).
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<AgentSupervisorService>>(
            new ListLogger<AgentSupervisorService>(supervisorLog));

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<AgentControlService>(),
            provider.GetRequiredService<AgentSessionLaunchQueue>(),
            eventBus,
            clock,
            supervisorLog);
    }

    private static async Task CleanupAsync(string tempRoot)
    {
        await using var db = CreateContext();
        var agentIds = await db.Agents
            .Where(a => a.WorkingDirectory.StartsWith(tempRoot))
            .Select(a => a.Id)
            .ToListAsync();
        await db.AgentIncidents.Where(i => agentIds.Contains(i.AgentId)).ExecuteDeleteAsync();
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
            // Best-effort temp cleanup.
        }
    }

    private sealed record Harness(
        ServiceProvider Provider,
        IServiceScope Scope,
        AgentControlService Control,
        AgentSessionLaunchQueue LaunchQueue,
        MockEventBus EventBus,
        MutableTimeProvider Clock,
        List<string> SupervisorLog) : IAsyncDisposable
    {
        // Fresh scope per call: a reused scope's DbContext identity-resolves to stale tracked
        // entities, hiding writes made through other contexts (and real ticks run in fresh
        // scopes via the hosted service anyway).
        public AgentSupervisorService Supervisor()
        {
            var scope = Provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
        }

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class ListLogger<T>(List<string> sink) : Microsoft.Extensions.Logging.ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (sink)
                sink.Add($"[{logLevel}] {formatter(state, exception)}{(exception is null ? "" : $" :: {exception}")}");
        }
    }

    private sealed class OptionsMonitorStub<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class QueueAdapterFactory(IEnumerable<IAgentProtocolAdapter> adapters)
        : IAgentProtocolAdapterFactory
    {
        private readonly Queue<IAgentProtocolAdapter> _adapters = new(adapters);

        public IAgentProtocolAdapter Create(AgentKind kind) =>
            _adapters.TryDequeue(out var adapter)
                ? adapter
                : throw new InvalidOperationException("No fake adapter was queued for dispatch.");
    }

    private sealed class NoWorktreeManager : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw new NotSupportedException("Supervision tests never spawn card work.");

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class StubRunnerClient : ISessionRunnerClient
    {
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));

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

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct)
            => throw new NotSupportedException();
    }
}
