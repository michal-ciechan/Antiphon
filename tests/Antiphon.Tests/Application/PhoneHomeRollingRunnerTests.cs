using System.Collections.Concurrent;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0727 V-2. Two runner ids on the real dispatcher graph. Default placement admits Claude
/// Code (and Grok and Codex workers); Raw is excluded as kind_not_supported, so the new task is
/// Claude Code. The phone-home host keeps a fake clock so its lease does not expire during the
/// queue's wall-clock confirm; the harness stays on the system clock so that confirm can finish.
/// </summary>
[Category("Integration")]
public sealed partial class PhoneHomeRollingRunnerTests
{
    [Test]
    [Timeout(120_000)]
    public async Task Input_to_a_session_on_server2_reaches_server2_while_a_new_launch_goes_to_server2_temp()
    {
        await using var world = await RollingWorld.StartAsync();
        const string message = "stay-on-server2";

        var created = await world.CreateDefaultTaskAsync();
        var saved = await world.ReadTaskAsync(created.Id);
        saved.Task.RunnerId.ShouldBe(RollingRunnerSettings.Server2Temp);
        saved.Created.ShouldContain("selected=server2-temp");

        await world.StampDesktopWorktreeAsync(created.Id);
        for (var tick = 1; tick <= 5 && world.PeerB.Launches.Count == 0; tick++)
        {
            await world.TickAsync();
            await world.WaitForLaunchesAsync(TimeSpan.FromSeconds(3));
        }

        world.PeerB.Launches.Count.ShouldBe(1, await world.DiagnoseAsync(created.Id));
        world.PeerA.Launches.Count.ShouldBe(0);
        var launched = await world.ReadTaskAsync(created.Id);
        var sessionId = launched.Task.AgentSessionId.ShouldNotBeNull();
        await using (var db = world.NewDb())
        {
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
            session.RunnerId.ShouldBe(RollingRunnerSettings.Server2Temp);
            session.RunnerStoreId.ShouldBe(world.StoreB);
        }

        await world.Queue.EnqueueAsync(world.SessionA, message, MessageSendMode.WhenIdle, CancellationToken.None);
        world.PeerA.Inputs.ShouldContain(frame => InputText(frame) == message);
        world.PeerB.Inputs.ShouldNotContain(frame => InputText(frame) == message);
        await using (var db = world.NewDb())
        {
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.AgentSessionId == world.SessionA);
            row.Status.ShouldBe(QueuedMessageStatus.Sent);
            row.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        }

        var afterQueue = world.PeerA.Inputs.Count;
        var routing = new RoutingSessionRunnerClient(world.RunnerDirectory);
        await routing.SendInputAsync(world.SessionA, "x", CancellationToken.None);
        world.PeerA.Inputs.Count.ShouldBe(afterQueue + 1);
        InputText(world.PeerA.Inputs[^1]).ShouldBe("x");
        world.PeerB.Inputs.ShouldNotContain(frame => InputText(frame) == "x");
    }

    private static string? InputText(PhoneHomeFrame frame)
    {
        if (frame.Payload is not { } payload || payload.ValueKind != JsonValueKind.Object)
            return null;
        return payload.TryGetProperty("input", out var input) ? input.GetString() : null;
    }

    private static Guid SessionIdOf(PhoneHomeFrame frame)
    {
        if (frame.Payload is not { } payload || payload.ValueKind != JsonValueKind.Object)
            return Guid.Empty;
        return payload.TryGetProperty("sessionId", out var id) && id.TryGetGuid(out var sessionId)
            ? sessionId
            : Guid.Empty;
    }

    /// <summary>CARD-0727 MS-2. Real queue, real dispatcher, real adapter factory, two scripted peers.</summary>
    private sealed partial class RollingWorld : IAsyncDisposable
    {
        private readonly List<IServiceScope> _scopes = [];
        private readonly string _root;
        private readonly ConcurrentDictionary<Guid, string> _screens = new();
        private string? _stampedWorktree;

        public PhoneHomeTestHost Host { get; }
        public PhoneHomeScriptedPeer PeerA { get; }
        public PhoneHomeScriptedPeer PeerB { get; }
        public BridgeQueueHarness Harness { get; }
        public IsolatedTestSchema Schema { get; }
        public Guid SessionA { get; }
        public Guid StoreA { get; }
        public Guid StoreB { get; }
        public PhoneHomeRunnerDirectory RunnerDirectory => Host.Directory;
        public SessionMessageQueueService Queue => Harness.Queue;

        private RollingWorld(
            PhoneHomeTestHost host,
            PhoneHomeScriptedPeer peerA,
            PhoneHomeScriptedPeer peerB,
            BridgeQueueHarness harness,
            IsolatedTestSchema schema,
            Guid sessionA,
            Guid storeA,
            Guid storeB,
            string root)
        {
            Host = host;
            PeerA = peerA;
            PeerB = peerB;
            Harness = harness;
            Schema = schema;
            SessionA = sessionA;
            StoreA = storeA;
            StoreB = storeB;
            _root = root;
        }

        public static Task<RollingWorld> StartAsync() => StartAsync(null);

        public static async Task<RollingWorld> StartAsync(RollingOptions? options)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var secretA = "rolling-a-" + Guid.NewGuid().ToString("N");
            var secretB = "rolling-b-" + Guid.NewGuid().ToString("N");
            var configured = options?.Settings ?? RollingRunnerSettings.Pair(secretA, secretB);
            options?.ConfigureSettings?.Invoke(configured);
            if (options?.Settings is not null || options?.ConfigureSettings is not null)
            {
                secretA = configured.Runners[RollingRunnerSettings.Server2].SharedSecret;
                secretB = configured.Runners[RollingRunnerSettings.Server2Temp].SharedSecret;
            }
            // Two map entries make the unscoped probe read the legacy singleton. Keep that false
            // so create and dispatch do not ask the peer for provider auth.
            configured.ClaudeAuthProbeEnabled = false;
            var clock = options?.Clock
                ?? new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString, configured: configured);
            host.Capacity = options?.PeerACapacity ?? 4;
            var storeA = Guid.NewGuid();
            var storeB = Guid.NewGuid();
            var peerA = await host.ConnectPeerAsync(
                runnerId: RollingRunnerSettings.Server2, storeId: storeA, secret: secretA);
            if (options?.PeerBCapacity is int peerBCapacity)
                host.Capacity = peerBCapacity;
            var peerB = await host.ConnectPeerAsync(
                runnerId: RollingRunnerSettings.Server2Temp, storeId: storeB, secret: secretB);
            host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: RollingRunnerSettings.Server2));
            host.Directory.MarkRecovered(await host.WaitLiveAsync(runnerId: RollingRunnerSettings.Server2Temp));
            var directory = options?.Directory?.Invoke(host) ?? (ISessionRunnerDirectory)host.Directory;

            var root = RepoRoot();
            var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = false,
                ConnectionString = schema.ConnectionString,
                TimeProvider = options?.Clock,
                Delegation = new DelegationSettings
                {
                    MaxConcurrentTasks = 32,
                    AllowedRoots = [root],
                },
                ConfigureServices = services => Configure(services, host, configured, root, directory),
            });

            var sessionA = Guid.NewGuid();
            var started = DateTime.UtcNow;
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)))
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionA,
                    DefinitionName = "claude",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Running,
                    Cwd = root,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = started,
                    StartedAt = started,
                    LastSeenAt = started,
                    RunnerId = RollingRunnerSettings.Server2,
                    RunnerStoreId = storeA,
                    RunnerCwd = "/work",
                });
                await db.SaveChangesAsync();
            }

            var liveA = host.Directory.SnapshotLive(RollingRunnerSettings.Server2)!;
            liveA.NoteSessionLive(sessionA, started, liveA.BeginInventoryRead());

            var world = new RollingWorld(host, peerA, peerB, harness, schema, sessionA, storeA, storeB, root);
            world.Configured = configured;
            world.Script(peerA);
            world.Script(peerB);
            return world;
        }

        public async Task<AgentTaskCreatedDto> CreateDefaultTaskAsync()
        {
            await using var db = NewDb();
            var defaults = new RunnerDefaultSettingsService(
                db, Options.Create(Harness.Delegation), TimeProvider.System, Harness.EventBus, RunnerDirectory);
            await defaults.EnsureInitializedAsync(CancellationToken.None);
            var current = await defaults.GetAsync(CancellationToken.None);
            await defaults.PutAsync(new PutRunnerDefaultsRequest(
                current.Revision,
                RollingRunnerSettings.Server2Temp,
                [],
                "Prove the new image on server2-temp.",
                "Human"), null, CancellationToken.None);

            using var scope = Harness.Provider.CreateScope();
            _scopes.Add(scope);
            return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                new CreateAgentTaskRequest(
                    "rolling default lands on server2-temp",
                    Role: AgentTaskRole.Code,
                    AgentKind: AgentKind.ClaudeCode,
                    Workspace: WorkspaceMode.Worktree),
                new AgentTaskService.Caller(null, null, _root),
                CancellationToken.None);
        }

        public async Task StampDesktopWorktreeAsync(Guid taskId)
        {
            var path = Path.Combine(Path.GetTempPath(), "antiphon-c727-wt-" + taskId.ToString("N")[..8]);
            Directory.CreateDirectory(path);
            _stampedWorktree = path;
            await using var db = NewDb();
            await db.AgentTasks.Where(t => t.Id == taskId).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.WorktreePath, path)
                .SetProperty(t => t.WorktreeBranch, "feat/rolling-pin"));
        }

        public Task<AgentTaskDispatcher.TickResult> TickAsync(Action<AgentTaskDispatcher>? configure = null)
        {
            var scope = Harness.Provider.CreateScope();
            _scopes.Add(scope);
            var dispatcher = scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            configure?.Invoke(dispatcher);
            return dispatcher.TickAsync(CancellationToken.None);
        }

        public async Task WaitForLaunchesAsync(TimeSpan budget)
        {
            var deadline = DateTime.UtcNow + budget;
            while (PeerB.Launches.Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(20);
        }

        public async Task<DefaultRunnerKit.Saved> ReadTaskAsync(Guid taskId)
        {
            await using var db = NewDb();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == taskId).ToListAsync();
            var created = events.Single(e => e.Type == AgentTaskEventType.Created).Detail ?? "";
            var warnings = events.Where(e => e.Type == AgentTaskEventType.Warning).Select(e => e.Detail ?? "").ToList();
            return new DefaultRunnerKit.Saved(task, created, warnings);
        }

        public async Task<string> DiagnoseAsync(Guid taskId)
        {
            await using var db = NewDb();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            var events = await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == taskId)
                .OrderBy(e => e.At)
                .Select(e => e.Type + ": " + e.Detail)
                .ToListAsync();
            return $"status={task.Status} failure={task.FailureReason} remote={task.RemoteWorktreePath} events={string.Join(" | ", events)}";
        }

        public AppDbContext NewDb() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

        public async ValueTask DisposeAsync()
        {
            foreach (var scope in _scopes)
                scope.Dispose();
            await Harness.DisposeAsync();
            await PeerA.DisposeAsync();
            await PeerB.DisposeAsync();
            await Host.DisposeAsync();
            await Schema.DisposeAsync();
            if (_stampedWorktree is not null)
            {
                try { Directory.Delete(_stampedWorktree, recursive: true); } catch (IOException) { }
            }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
        }

        private void Script(PhoneHomeScriptedPeer peer)
        {
            var connection = Schema.ConnectionString;
            peer.Reply = frame =>
            {
                if (frame.Operation == PhoneHomeOperation.WorkspaceMirror)
                {
                    if (ReferenceEquals(peer, PeerB) && PeerBMirrorErrors > 0)
                    {
                        PeerBMirrorErrors--;
                        return new PhoneHomeFrame(
                            PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                            ErrorCode: PhoneHomeProblemTypes.Unavailable, ErrorDetail: "mirror failed");
                    }

                    var request = frame.Payload?.Deserialize<PhoneHomeWorkspaceMirrorRequest>(PhoneHomeFraming.Json);
                    var name = string.IsNullOrWhiteSpace(request?.Name) ? "mirror" : request!.Name;
                    return Result(frame, new PhoneHomeWorkspaceMirrorResponse("/work/worktrees/" + name));
                }

                if (frame.Operation == PhoneHomeOperation.WorkspaceRemove)
                {
                    if (ReferenceEquals(peer, PeerA) && PeerARemoveErrors > 0)
                    {
                        PeerARemoveErrors--;
                        return new PhoneHomeFrame(
                            PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                            ErrorCode: PhoneHomeProblemTypes.Unavailable, ErrorDetail: "remove failed");
                    }

                    return Result(frame, new PhoneHomeWorkspaceRemoveResponse(true, null));
                }

                if (frame.Operation == PhoneHomeOperation.Capabilities && ReferenceEquals(peer, PeerA))
                {
                    return Result(frame, new RunnerCapabilitiesDto(
                        "ModernConPty", "modern", "test", false,
                        Features: [RunnerCapabilityFeatures.VerificationCustodyV1],
                        VerificationCustodyBackend: VerificationCustodyBackends.LinuxCgroup,
                        RunnerStoreId: StoreA,
                        Platform: "linux"));
                }

                if (frame.Operation == PhoneHomeOperation.ReleaseSlot)
                {
                    var sessionId = SessionIdOf(frame);
                    return Result(frame, new RunnerSessionDto(
                        sessionId, 1, DateTime.UtcNow, "Exited", 0, "", 0, AcceptedStartedAt: DateTime.UtcNow));
                }

                if (frame.Operation == PhoneHomeOperation.Input)
                {
                    var sessionId = SessionIdOf(frame);
                    var text = InputText(frame);
                    if (sessionId != Guid.Empty && !string.IsNullOrEmpty(text))
                    {
                        _screens[sessionId] = text;
                        if (text != "\r")
                        {
                            BridgeQueueHarness.InsertEntryAsync(
                                sessionId, TranscriptKinds.UserPrompt, text, timestamp: DateTime.UtcNow,
                                connectionString: connection).GetAwaiter().GetResult();
                        }
                    }

                    return null;
                }

                if (frame.Operation == PhoneHomeOperation.Snapshot)
                {
                    var sessionId = SessionIdOf(frame);
                    _screens.TryGetValue(sessionId, out var screen);
                    screen ??= "";
                    return Result(frame, new RunnerSnapshotDto(sessionId, screen, screen, 1, DateTime.UtcNow));
                }

                return null;
            };
        }

        private static PhoneHomeFrame Result<T>(PhoneHomeFrame request, T payload) =>
            new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
                JsonSerializer.SerializeToElement(payload, PhoneHomeFraming.Json));

        private static void Configure(
            IServiceCollection services, PhoneHomeTestHost host, PhoneHomeRunnerSettings configured, string root,
            ISessionRunnerDirectory directory)
        {
            var registry = new AgentRegistrySettings
            {
                DefaultDefinition = "claude",
                GrokCredentialProbeEnabled = false,
                Definitions =
                {
                    ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" },
                },
            };
            services.RemoveAll<IOptionsMonitor<AgentRegistrySettings>>();
            services.RemoveAll<AgentRegistry>();
            services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(registry));
            services.AddSingleton<AgentRegistry>();
            services.RemoveAll<ISessionRunnerClient>();
            services.AddSingleton<ISessionRunnerClient>(new RoutingSessionRunnerClient(directory));
            services.RemoveAll<IAgentProtocolAdapterFactory>();
            services.AddSingleton<IAgentProtocolAdapterFactory>(sp => new AgentProtocolAdapterFactory(
                Options.Create(registry),
                sp.GetRequiredService<ISessionRunnerClient>(),
                directory: directory));
            services.AddSingleton(directory);
            services.AddScoped<SourceLandingAdmission>();
            services.AddSingleton(Options.Create(configured));
            services.AddSingleton<PhoneHomeLaunchPolicy>();
            services.AddSingleton<ILandingGit>(new PushGit(root));
            services.AddSingleton<IRepositoryMutationLease>(new OpenLease());
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(root, "trees"),
            });
            services.RemoveAll<ITaskProgressGit>();
            services.AddSingleton<RemoteWorkspaceService>();
            services.AddSingleton<RemoteWorkspacePreparer>();
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
            services.AddScoped<RunnerDefaultSettingsService>();
            services.AddScoped<AgentTaskService>();
            services.AddScoped<AgentTaskDispatcher>();
        }

        /// <summary>A lease that always grants, so the dispatch claim does not touch a real repository lock.</summary>
        private sealed class OpenLease : IRepositoryMutationLease
        {
            public Task<RepositoryLease?> TryAcquireAsync(string repository, CancellationToken ct) =>
                Task.FromResult<RepositoryLease?>(new TrivialLease(repository));

            public bool Owns(RepositoryLease lease, string commonDirectory) => lease is TrivialLease;

            private sealed class TrivialLease(string repository) : RepositoryLease
            {
                public override string CommonDirectory { get; } = Path.GetFullPath(repository);
                public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }

        private sealed class PushGit(string root) : ILandingGit
        {
            public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct) =>
                Task.FromResult(Path.GetFullPath(path));

            public Task<string> CommonDirectoryAsync(string repository, CancellationToken ct) =>
                Task.FromResult(Path.GetFullPath(root));

            public Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct) =>
                Task.FromResult(arguments[0] switch
                {
                    "rev-parse" => new LandingGitResult(0, new string('1', 40), ""),
                    "push" => new LandingGitResult(0, "", ""),
                    _ => throw new NotSupportedException(arguments[0]),
                });

            public Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments, Func<int, long, CancellationToken, Task> started, CancellationToken ct) => throw new NotSupportedException();
            public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) => throw new NotSupportedException();
            public Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
            public Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct) => throw new NotSupportedException();
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
}
