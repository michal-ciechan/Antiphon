using System.Net.WebSockets;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-5. A frozen requirement is checked again before launch and before reused input.
/// A downgraded runner is not retried through the legacy launch operation.
/// </summary>
[Category("Integration")]
public sealed class TaskPlatformDispatchTests
{
    [Test]
    public async Task Reconnect_platform_change_blocks_launch()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        var linux = Caps("linux");
        await using var first = await host.ConnectPeerAsync(platform: "linux", capabilities: linux);
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, RequiredPlatform.Linux);
        await first.DisposeAsync();
        await using var windows = await host.ConnectPeerAsync(platform: "windows", capabilities: Caps("windows"));
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        var (dispatcher, _) = CreateDispatcher(schema, host);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Blocked);
        task.FailureReason.ShouldContain(RunnerPlatformProblems.Mismatch);
        task.RunnerId.ShouldBe(host.AllowedRunnerId);
        task.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        windows.Launches.ShouldBeEmpty();
        windows.RequestCount(PhoneHomeOperation.Launch).ShouldBe(0);
        windows.RequestCount(PhoneHomeOperation.LaunchPlatformConstrained).ShouldBe(0);
    }

    [Test]
    public async Task Unavailable_and_unknown_hold_without_reroute()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync(platform: "linux", capabilities: Caps("linux"));
        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, RequiredPlatform.Linux);
        var (dispatcher, _) = CreateDispatcher(schema, host);

        await dispatcher.TickAsync(CancellationToken.None);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Queued);
        task.RunnerId.ShouldBe(host.AllowedRunnerId);
        task.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        task.FailureReason.ShouldBeNull();
        (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Held))
            .ShouldBeGreaterThan(0);
        peer.Launches.ShouldBeEmpty();
    }

    [Test]
    public async Task Retry_preserves_host_and_requirement()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var seed = kit.Context())
        {
            seed.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, Title = "retry", Goal = "retry the same host",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Medium, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = kit.RepoRoot, RunnerId = "server2",
                RequiredPlatform = RequiredPlatform.Linux, RequirementSource = RequirementSource.Request,
                ObservedPlatform = "linux", Status = AgentTaskStatus.Failed, FailureReason = "boom",
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
            });
            await seed.SaveChangesAsync();
        }

        await using var db = kit.Context();
        var summary = await kit.Service(db).RetryAsync(id, CancellationToken.None);
        summary.RunnerId.ShouldBe("server2");
        summary.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
        summary.ObservedPlatform.ShouldBe("linux");
        summary.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task Reroute_cannot_escape_platform()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null, allowedRunnerId: "server2");
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var seed = kit.Context())
        {
            seed.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, Title = "reroute", Goal = "stay on the frozen host",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.Grok,
                ModelLevel = AgentModelLevel.Medium, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = kit.RepoRoot, RunnerId = "server2",
                RequiredPlatform = RequiredPlatform.Linux, Status = AgentTaskStatus.Queued,
                ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
            });
            await seed.SaveChangesAsync();
        }

        await using var db = kit.Context();
        var summary = await kit.Service(db).RerouteAsync(id, AgentKind.Codex, AgentModelLevel.Frontier, CancellationToken.None);
        summary.AgentKind.ShouldBe(AgentKind.Codex);
        summary.RunnerId.ShouldBe("server2");
        summary.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
    }

    [Test]
    public async Task Reused_process_mismatch_sends_no_input()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, DefinitionName = "claude", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running, Cwd = workspace.Path, Cols = 80, Rows = 24,
                CreatedAt = now, StartedAt = now, LastSeenAt = now,
            });
            db.Agents.Add(new Agent
            {
                Id = agentId, Name = "warm-c710", Slug = "warm-c710", WorkingDirectory = workspace.Path,
                Details = "warm", Status = AgentStatus.Idle, Kind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium, IsPoolDelegate = true, PoolIdleSince = now.AddMinutes(-5),
                PersistentSessionId = sessionId.ToString("D"), LaunchEnvJson = "{}",
                CreatedAt = now, UpdatedAt = now,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "reuse", Goal = "do not type into the wrong OS",
                Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Medium, Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workspace.Path, AgentId = agentId, RequiredPlatform = RequiredPlatform.Linux,
                Status = AgentTaskStatus.Queued, ReplyTo = AgentTaskReplyTo.None,
                CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
            });
            await db.SaveChangesAsync();
        }

        var directory = new FlippingDirectory();
        var (dispatcher, sink) = CreateDispatcher(schema, directory);
        await dispatcher.TickAsync(CancellationToken.None);

        sink.Inputs.ShouldBeEmpty();
        await using var read = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await read.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(AgentTaskStatus.Blocked);
        task.FailureReason.ShouldContain(RunnerPlatformProblems.Mismatch);
        task.RequiredPlatform.ShouldBe(RequiredPlatform.Linux);
    }

    [Test]
    public async Task Downgrade_never_uses_legacy_launch()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString);
        await using var peer = await host.ConnectPeerAsync(platform: "linux", capabilities: Caps("linux"));
        peer.Reply = frame =>
        {
            if (frame.Operation != PhoneHomeOperation.LaunchPlatformConstrained)
                return null;
            return new PhoneHomeFrame(
                PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: PhoneHomeProblemTypes.UnsupportedOperation,
                ErrorDetail: "old peer", StatusCode: 404);
        };
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        var client = (PhoneHomeRunnerClient)host.Directory.Resolve(host.AllowedRunnerId);
        var refused = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(() =>
            client.StartAsync(Guid.NewGuid(), new AgentLaunchSpec(
                DefinitionName: "claude", Kind: AgentKind.ClaudeCode, Exe: "claude", Args: [],
                Env: new Dictionary<string, string>(), Cwd: "/work", Cols: 80, Rows: 24,
                RequiredPlatform: RunnerPlatformWire.Linux), CancellationToken.None));
        refused.Code.ShouldBe(RunnerPlatformProblems.EnforcementUnsupported);
        peer.RequestCount(PhoneHomeOperation.Launch).ShouldBe(0);
        peer.RequestCount(PhoneHomeOperation.LaunchPlatformConstrained).ShouldBe(1);

        using var workspace = new TempWorkspace();
        var taskId = await SeedAsync(schema, workspace.Path, host.AllowedRunnerId, RequiredPlatform.Linux);
        await peer.DisposeAsync();
        await using var old = await host.ConnectPeerAsync(platform: "linux");
        var (dispatcher, _) = CreateDispatcher(schema, host);
        await dispatcher.TickAsync(CancellationToken.None);
        old.RequestCount(PhoneHomeOperation.Launch).ShouldBe(0);
        old.RequestCount(PhoneHomeOperation.LaunchPlatformConstrained).ShouldBe(0);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.RunnerId.ShouldBe(host.AllowedRunnerId);
        task.Status.ShouldNotBe(AgentTaskStatus.Dispatched);
    }

    private static RunnerCapabilitiesDto Caps(string platform) => new(
        "InboxConhost", "inbox", "test", false, Features: [RunnerPlatformWire.Feature], Platform: platform);

    private static async Task<Guid> SeedAsync(
        IsolatedTestSchema schema, string path, string runnerId, RequiredPlatform platform)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        db.AgentTasks.Add(new AgentTask
        {
            Id = id, RootTaskId = id, Title = "dispatch", Goal = "dispatch",
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Code, AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium, Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = path, WorktreePath = path, WorktreeBranch = "feat/c710",
            RunnerId = runnerId, RequiredPlatform = platform, Status = AgentTaskStatus.Queued,
            ReplyTo = AgentTaskReplyTo.None, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static (AgentTaskDispatcher Dispatcher, RecordingSink Sink) CreateDispatcher(
        IsolatedTestSchema schema, PhoneHomeTestHost host) =>
        CreateDispatcher(schema, host.Directory, host.AllowedRunnerId);

    private static (AgentTaskDispatcher Dispatcher, RecordingSink Sink) CreateDispatcher(
        IsolatedTestSchema schema, ISessionRunnerDirectory directory, string allowedRunnerId = "server2")
    {
        var sink = new RecordingSink();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString, n =>
        {
            n.MigrationsAssembly("Antiphon.Server");
            n.SetPostgresVersion(16, 0);
        }));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings { MaxConcurrentTasks = 512, AllowedRoots = ["C:\\", "/"] }));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), $"antiphon-c710-dispatch-{Guid.NewGuid():N}"),
        });
        services.AddSingleton(Options.Create(new PhoneHomeRunnerSettings
        {
            Enabled = true, AllowedRunnerId = allowedRunnerId, AllowDelegatedTasks = true,
            HostWorkspaceRoot = "/", CallbackOrigin = "https://antiphon.test", SharedSecret = "x",
        }));
        services.AddSingleton<PhoneHomeLaunchPolicy>();
        services.AddSingleton(directory);
        services.AddSingleton<ILandingGit, NoGit>();
        services.AddSingleton<RemoteWorkspaceService>();
        services.AddSingleton<RemoteWorkspacePreparer>();
        services.AddSingleton<IAgentTaskLaunchSink>(sink);
        services.AddScoped<AgentTaskService>();
        services.AddScoped<AgentTaskDispatcher>();
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), sink);
    }

    private sealed class RecordingSink : IAgentTaskLaunchSink
    {
        public List<string> Inputs { get; } = [];
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) =>
            Inputs.Add(spec.RequiredPlatform ?? "");
    }

    private sealed class FlippingDirectory : ISessionRunnerDirectory
    {
        private int _calls;
        public ISessionRunnerClient Local { get; } = new PhoneHomeTestHost.RecordingLocalClient();
        public IReadOnlyList<string> KnownRunnerIds => ["desktop"];
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public Task<RunnerDescriptor?> DescribeAsync(string? runnerId, CancellationToken ct)
        {
            var platform = Interlocked.Increment(ref _calls) == 1 ? "linux" : "windows";
            return Task.FromResult<RunnerDescriptor?>(new RunnerDescriptor(
                "desktop", "Desktop", platform, DateTimeOffset.UtcNow, true, true, false, null,
                new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                    Features: [RunnerPlatformWire.Feature], Platform: platform)));
        }
        public ISessionRunnerClient Resolve(string? runnerId) => Local;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Missing.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Unavailable("unused"));
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c710-dispatch").FullName;
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class NoGit : ILandingGit
    {
        public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
            Task.FromResult(new LandingGitResult(0, new string('1', 40), ""));
        public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) => Task.FromResult(path);
        public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) => Task.FromResult(repository);
        public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => Task.FromResult(false);
        public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => Task.FromResult<IReadOnlyList<LandingRegistration>>([]);
        public Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination, string sourceSha, string observationRef, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef, string observationPrefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
        public Task<LandingIndexLockObservation> InspectIndexLockAsync(string checkout, CancellationToken ct) => throw new NotSupportedException();
    }
}
