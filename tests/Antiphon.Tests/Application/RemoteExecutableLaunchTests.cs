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
/// CARD-1778 S1. A rooted executable that is absent on the server disk is refused while
/// <c>SessionRunner:VerifyExecutablesOnServer</c> is true, and is handed to the runner unchanged
/// when the setting is false. A runner-bound agent never reaches that check.
/// </summary>
[Category("Unit")]
[NotInParallel("RemoteExecutableLaunch")]
public class RemoteExecutableLaunchTests
{
    private static string MissingExe => OperatingSystem.IsWindows()
        ? @"C:\antiphon-no-such\claude.exe"
        : "/antiphon-no-such/claude";

    /// <summary>V-1. The unset setting is the property default, true, and the missing exe never starts.</summary>
    [Test]
    public async Task Default_true_refuses_a_rooted_exe_missing_on_the_server()
    {
        File.Exists(MissingExe).ShouldBeFalse();
        var tempRoot = NewTempRoot();
        var adapter = new FakeAgentProtocolAdapter();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            await using var harness = BuildHarness(tempRoot, [adapter], MissingExe);

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Missing on the server", workspace), CancellationToken.None);

            var ex = await Should.ThrowAsync<ConflictException>(
                () => harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None));
            ex.Message.ShouldContain(MissingExe);

            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            adapter.Started.ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    /// <summary>V-2. False leaves the rooted exe unchanged and asks the runner to spawn it once.</summary>
    [Test]
    public async Task False_hands_the_launch_to_the_runner()
    {
        File.Exists(MissingExe).ShouldBeFalse();
        var tempRoot = NewTempRoot();
        var adapter = new FakeAgentProtocolAdapter();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            await using var harness = BuildHarness(
                tempRoot, [adapter], MissingExe, verifyExecutablesOnServer: false);

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Sidecar owns the binary", workspace), CancellationToken.None);

            ConflictException? refused = null;
            try
            {
                await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            }
            catch (ConflictException ex)
            {
                refused = ex;
            }

            refused.ShouldBeNull();
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            adapter.Started.ShouldBeTrue();
            adapter.StartedExe.ShouldBe(MissingExe);
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    /// <summary>
    /// V-3. Phone-home projection skips <c>EnsureSpawnable</c> for both flag values.
    /// The runner sees the projected file name, not the missing server path.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Runner_bound_agents_are_unaffected(bool verifyExecutablesOnServer)
    {
        File.Exists(MissingExe).ShouldBeFalse();
        var tempRoot = NewTempRoot();
        var adapter = new FakeAgentProtocolAdapter();
        try
        {
            var workspace = Path.Combine(tempRoot, "agent-workspace");
            Directory.CreateDirectory(workspace);
            await using var harness = BuildHarness(
                tempRoot,
                [adapter],
                MissingExe,
                verifyExecutablesOnServer,
                phoneHome: new PhoneHomeRunnerSettings
                {
                    Enabled = true,
                    AllowedRunnerId = "server2",
                    RunnerWorkspace = "/work",
                },
                runnerDirectory: new LiveStoreDirectory(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")));

            var agent = await harness.AgentService.CreateAsync(
                new CreateAgentRequest("Runner bound", workspace, RunnerId: "server2"), CancellationToken.None);

            ConflictException? refused = null;
            try
            {
                await harness.Control.StartAsync(agent.Id, new StartAgentRequest(), CancellationToken.None);
            }
            catch (ConflictException ex)
            {
                refused = ex;
            }

            refused.ShouldBeNull();
            await harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            adapter.Started.ShouldBeTrue();
            adapter.StartedExe.ShouldBe("claude");
        }
        finally
        {
            await CleanupAsync(tempRoot);
        }
    }

    /// <summary>R-1. A fresh settings object keeps the desktop check on.</summary>
    [Test]
    public void Setting_default_is_true()
    {
        new SessionRunnerSettings().VerifyExecutablesOnServer.ShouldBeTrue();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static Harness BuildHarness(
        string tempRoot,
        IReadOnlyList<IAgentProtocolAdapter> adapters,
        string exe,
        bool? verifyExecutablesOnServer = null,
        PhoneHomeRunnerSettings? phoneHome = null,
        ISessionRunnerDirectory? runnerDirectory = null)
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
            Definitions = { ["fake"] = new AgentDefinition { Kind = "Raw", Exe = exe } }
        }));
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<IWorktreeManager>(new FakeWorktreeManager(Path.Combine(tempRoot, "worktrees")));
        services.AddSingleton<IAgentProtocolAdapterFactory>(new QueueAdapterFactory(adapters));
        services.AddSingleton<IWorkspaceHookRunner>(new WorkspaceHookRunner(NullLogger<WorkspaceHookRunner>.Instance));
        services.AddScoped<WorkspaceHookService>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddScoped<AgentSessionService>();
        services.AddScoped<RetryScheduler>();
        services.AddScoped<ExternalTrackerSyncService>();
        services.AddSingleton<OrchestratorControlState>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings()));
        services.AddScoped<AgentSessionLaunchComposer>();
        services.AddScoped<OrchestratorService>();
        services.AddScoped<CardWorkflowRunFactory>();
        services.AddScoped<AgentService>();
        if (verifyExecutablesOnServer is bool verify)
        {
            services.AddSingleton<IOptions<SessionRunnerSettings>>(Options.Create(new SessionRunnerSettings
            {
                VerifyExecutablesOnServer = verify,
            }));
        }

        if (phoneHome is not null)
        {
            services.AddSingleton<IOptions<PhoneHomeRunnerSettings>>(Options.Create(phoneHome));
            services.AddSingleton<PhoneHomeLaunchPolicy>();
        }

        if (runnerDirectory is not null)
            services.AddSingleton(runnerDirectory);

        services.AddScoped<AgentControlService>();
        services.AddSingleton<Antiphon.Server.Application.Interfaces.IDirectoryWriter>(
            new Antiphon.Server.Infrastructure.FileSystem.FileSystemDirectoryWriter(new System.IO.Abstractions.FileSystem()));
        services.AddScoped<BoardService>();
        services.AddGitWorkspaceService();
        services.AddScoped<AgentReviewCheckpointService>();
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
        Path.Combine(Path.GetTempPath(), $"antiphon-remote-exe-tests-{Guid.NewGuid():N}");

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

    private sealed class LiveStoreDirectory : ISessionRunnerDirectory
    {
        public LiveStoreDirectory(Guid storeId) => StoreId = storeId;

        public Guid StoreId { get; }

        public ISessionRunnerClient Local => throw new NotSupportedException();

        public ISessionRunnerClient Resolve(string? runnerId) => throw new NotSupportedException();

        // Start only probes this to refuse a draining runner. The return is discarded.
        public ISessionRunnerClient ResolveForNewWork(string? runnerId) => null!;

        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            throw new NotSupportedException();

        public IReadOnlyList<string> KnownRunnerIds => [];

        public Guid? GetLiveStoreId(string? runnerId) => StoreId;
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
