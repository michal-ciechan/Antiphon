using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
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
/// Regression suite for the "phantom Working agent" incident: a Start whose process never spawned
/// (missing executable → runner 500) left the agent badged Working in the UI forever, with no live
/// session and no error. These tests pin the invariant that agent status must always be backed by
/// a live process: launch failures fail fast or roll the agent back, and observed exits close the
/// session row and reset the agent.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentStartRecovery")]
public class AgentStartRecoveryTests
{
    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Test]
    public async Task Interactive_start_failure_marks_agent_failed_not_working()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            var adapter = new FakeAgentProtocolAdapter
            {
                // The exact failure shape of the claude.cmd incident: ConPTY spawn throws.
                ThrowOnStart = new InvalidOperationException(
                    "Could not start terminal process: The system cannot find the file specified.")
            };
            await using var harness = BuildHarness(tempRoot, [adapter]);

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Doomed Claude", workspace), CancellationToken.None);

            var detail = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(), CancellationToken.None);
            // The Start API returns optimistically (launch is queued); Working here is expected.
            detail.Status.ShouldBe(AgentStatus.Working);

            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            // The background launch failed — the agent must NOT be left Working.
            var after = await harness.AgentService.GetByIdAsync(agent.Id, CancellationToken.None);
            after.Status.ShouldBe(AgentStatus.Failed);
            after.LiveSession.ShouldBeNull();

            await using var verify = CreateContext();
            var session = await verify.AgentSessions.SingleAsync(
                s => s.Id.ToString() == detail.PersistentSessionId);
            session.Status.ShouldBe(SessionStatus.Failed);
            session.FailureReason.ShouldNotBeNullOrWhiteSpace();
            session.FailureReason.ShouldContain("cannot find the file");

            harness.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "AgentChanged");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    public async Task Interactive_start_with_unresolvable_executable_fails_fast_and_agent_stays_idle()
    {
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            await using var harness = BuildHarness(
                tempRoot, [new FakeAgentProtocolAdapter()], exe: "antiphon-no-such-exe-49f1.cmd");

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Misconfigured Claude", workspace), CancellationToken.None);

            // The misconfigured executable must surface as an immediate API error…
            var ex = await Should.ThrowAsync<ConflictException>(
                () => harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None));
            ex.Message.ShouldContain("antiphon-no-such-exe-49f1.cmd");

            // …with NO state mutated: the agent stays Idle and no session row exists.
            var after = await harness.AgentService.GetByIdAsync(agent.Id, CancellationToken.None);
            after.Status.ShouldBe(AgentStatus.Idle);
            after.PersistentSessionId.ShouldBeNull();

            await using var verify = CreateContext();
            (await verify.AgentSessions.AnyAsync(s => s.Cwd == workspace)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    [Test]
    [Arguments(0, AgentStatus.Stopped, SessionStatus.Stopped)]
    [Arguments(1, AgentStatus.Failed, SessionStatus.Failed)]
    public async Task Observed_exit_closes_session_and_resets_agent(
        int exitCode, AgentStatus expectedAgentStatus, SessionStatus expectedSessionStatus)
    {
        var tempRoot = NewTempRoot();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            await using var harness = BuildHarness(tempRoot, [new FakeAgentProtocolAdapter()]);

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Exiting Claude", workspace), CancellationToken.None);
            var detail = await harness.Control.StartAsync(
                agent.Id, new StartAgentRequest(), CancellationToken.None);
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

            var sessionId = Guid.Parse(detail.PersistentSessionId!);
            var runtime = harness.Provider.GetRequiredService<AgentSessionRuntime>();

            // Simulate the runner's exit event arriving via the event pump.
            await runtime.ObserveExitAsync(sessionId, exitCode, AgentExitReason.Unknown, CancellationToken.None);

            await using var verify = CreateContext();
            var session = await verify.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.Status.ShouldBe(expectedSessionStatus);
            session.EndedAt.ShouldNotBeNull();

            var dbAgent = await verify.Agents.SingleAsync(a => a.Id == agent.Id);
            dbAgent.Status.ShouldBe(expectedAgentStatus);

            harness.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "SessionExited");
            harness.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "AgentChanged");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(
        string tempRoot, IReadOnlyList<IAgentProtocolAdapter> adapters, string? exe = null)
    {
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
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings
        {
            FirstDeltaTimeoutMs = 1_000,
            KillGraceMs = 100,
            SignalRMaxChunkChars = 16 * 1024,
            ReplayBufferMaxChars = 128 * 1024,
            SessionLogPath = Path.Combine(tempRoot, "session-logs")
        }));
        services.AddSingleton<IOptions<OrchestratorSettings>>(Options.Create(new OrchestratorSettings
        {
            InternalTrackerRepositoryPathPrefix = tempRoot
        }));
        services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
        {
            DefaultDefinition = "fake",
            // A real, always-present executable: the Start path now verifies spawnability up front.
            Definitions = { ["fake"] = new AgentDefinition { Kind = "Raw", Exe = exe ?? Cmd } }
        }));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IWorktreeManager>(new FakeWorktreeManager(Path.Combine(tempRoot, "worktrees")));
        services.AddSingleton<IAgentProtocolAdapterFactory>(new QueueAdapterFactory(adapters));
        services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
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
        services.AddSingleton<Antiphon.Server.Application.Interfaces.IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(new System.IO.Abstractions.FileSystem()));
        services.AddScoped<BoardService>();
        services.AddScoped<CardService>();
        services.AddLogging();

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return new Harness(
            provider,
            scope,
            scope.ServiceProvider.GetRequiredService<AgentService>(),
            scope.ServiceProvider.GetRequiredService<AgentControlService>(),
            provider.GetRequiredService<AgentSessionLaunchQueue>(),
            eventBus);
    }

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), $"antiphon-start-recovery-tests-{Guid.NewGuid():N}");

    private static async Task CleanupAsync(string tempRoot)
    {
        await using var db = CreateContext();
        await db.Agents
            .Where(a => a.WorkingDirectory.StartsWith(tempRoot))
            .ExecuteUpdateAsync(updates => updates.SetProperty(a => a.PersistentSessionId, (string?)null));
        await db.AgentSessions.Where(s => s.CardId == null && s.Cwd.StartsWith(tempRoot)).ExecuteDeleteAsync();
        await db.Agents.Where(a => a.WorkingDirectory.StartsWith(tempRoot)).ExecuteDeleteAsync();

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
        AgentService AgentService,
        AgentControlService Control,
        AgentSessionLaunchQueue LaunchQueue,
        MockEventBus EventBus) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
        }
    }

    private sealed class QueueAdapterFactory : IAgentProtocolAdapterFactory
    {
        private readonly Queue<IAgentProtocolAdapter> _adapters;

        public QueueAdapterFactory(IEnumerable<IAgentProtocolAdapter> adapters)
        {
            _adapters = new Queue<IAgentProtocolAdapter>(adapters);
        }

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            if (_adapters.TryDequeue(out var adapter))
                return adapter;

            throw new InvalidOperationException("No fake adapter was queued for dispatch.");
        }
    }

    private sealed class FakeWorktreeManager : IWorktreeManager
    {
        private readonly string _worktreeRoot;

        public FakeWorktreeManager(string worktreeRoot)
        {
            _worktreeRoot = worktreeRoot;
        }

        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(_worktreeRoot);
            var worktreePath = Path.Combine(_worktreeRoot, $"card-{cardId}");
            Directory.CreateDirectory(worktreePath);
            var now = DateTimeOffset.UtcNow;
            var info = new WorktreeInfo(cardId, repoPath, worktreePath, $"feat/card-{cardId}", baseRef, now, now);
            return Task.FromResult(info);
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);

        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;

        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
    {
        public OptionsMonitorStub(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
