using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.Application;

internal static partial class CodexCliRemoteDeliveryFixture
{
    // The world owns the database and recipient. Graph recreation owns no terminal lifetime.
    internal sealed class World : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required ScratchGitRepo Git { get; init; }
        public required string Root { get; init; }
        public required Recipient Recipient { get; init; }
        public required Freeze Freeze { get; init; }
        public bool Remote { get; init; } = true;
        public bool Busy { get; set; }
        public AgentKind Kind { get; init; } = AgentKind.Codex;
        public RecoveryClock Clock { get; } = new();
        public QueueFault Fault { get; } = new();
        public PhoneHomeTestHost Host { get; private set; } = null!;
        public PhoneHomeScriptedPeer Peer { get; private set; } = null!;
        public BridgeQueueHarness Harness { get; private set; } = null!;
        public HeldLaunches Launches { get; private set; } = null!;
        private Guid _store = Guid.NewGuid();
        private Guid _boot = Guid.NewGuid();
        private Guid? _helperSession, _helperAgent;
        private readonly List<string> _roots = [];
        private bool _graph;
        public AppDbContext Context() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

        public static async Task<World> CreateAsync(RunnerCodexCliVersionDto? sample = null, bool busy = false,
            AgentKind kind = AgentKind.Codex, bool remote = true)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var git = new ScratchGitRepo("c1029-world");
            var root = System.IO.Directory.CreateTempSubdirectory("c1029-recipient-").FullName;
            var world = new World { Schema = schema, Git = git, Root = root, Busy = busy, Kind = kind,
                Remote = remote, Recipient = new(sample) { Kind = kind }, Freeze = new(schema.ConnectionString) };
            try
            {
                await git.CommitFileAsync("seed.txt", "C1029 retained recipient\n");
                await git.AddBareOriginAsync();
                await world.OpenGraphAsync();
                return world;
            }
            catch { await world.DisposeAsync(); throw; }
        }

        private async Task OpenGraphAsync()
        {
            var dispatcher = new PhoneHomeCommandDispatcher(Recipient, new PhoneHomeSettings
            {
                AllowedCwd = Root, RunnerRepository = Path.Combine(Root, "repo"),
                RunnerCloneSource = (await Git.GitReadAsync("remote", "get-url", "origin")).Trim(),
                CapacityStatePath = Path.Combine(Root, "capacity"),
                LaunchGenerationsPath = Path.Combine(Root, "generations"),
            }, new SignedInProbe());
            Host = await PhoneHomeTestHost.StartAsync(connectionString: Schema.ConnectionString,
                configureRunnerSettings: settings =>
                {
                    settings.AllowDelegatedTasks = true;
                    settings.HostWorkspaceRoot = Git.Path;
                    settings.RunnerWorkspace = Root;
                    settings.RunnerRepository = Path.Combine(Root, "repo");
                    settings.CallbackOrigin = "https://antiphon.test";
                }, configureServices: services =>
                {
                    services.AddSingleton<RemoteSpillCourier>();
                    services.AddSingleton(sp => new PhoneHomeRunnerDirectory(
                        sp.GetRequiredService<ISessionRunnerClient>(), sp.GetRequiredService<IOptions<PhoneHomeRunnerSettings>>(),
                        sp.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, sp.GetRequiredService<RemoteSpillCourier>()));
                });
            // Rules preflight reads the local capabilities surface; the selected adapter
            // independently checks the remote capabilities before its framed launch.
            Host.Local.Capabilities = Recipient.Capabilities();
            Peer = await Host.ConnectPeerAsync(storeId: _store, bootId: _boot, capabilities: Recipient.Capabilities());
            Peer.Reply = request => dispatcher.DispatchAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            Host.Directory.MarkRecovered(await Host.WaitLiveAsync());
            var registry = CodexCliObservationTests.Registry();
            registry.Definitions["grok"] = new() { Kind = "Grok", Exe = "grok",
                Env = new() { ["GROK_HOME"] = Path.Combine(Root, "grok-home"), ["GROK_AUTH_PATH"] = Path.Combine(Root, "grok-home", "auth.json") } };
            System.IO.Directory.CreateDirectory(Path.Combine(Root, "grok-home"));
            await File.WriteAllTextAsync(Path.Combine(Root, "grok-home", "auth.json"), "{\"test\":{\"key\":\"synthetic-owned-fixture\"}}");
            registry.GrokStartupCaptureDirectory = Root;
            // Keep production readiness/verification deadlines; the scripted screen is immediately stable.
            Launches = new();
            var local = new LocalRecipientClient(Recipient);
            Harness = await BridgeQueueHarness.CreateAsync(new()
            {
                AlwaysOn = false, ConnectionString = Schema.ConnectionString, TimeProvider = Clock,
                PreserveDatabaseOnDispose = true, AttachSessionId = _helperSession, AttachAgentId = _helperAgent,
                ConfigureDbContext = o => o.AddInterceptors(Fault),
                Delegation = new() { DefaultWorkerWorkspace = WorkspaceMode.Worktree, MaxConcurrentTasks = 20, RolePolicy = new() },
                ConfigureServices = services =>
                {
                    services.AddSingleton<ISessionRunnerDirectory>(Remote ? Host.Directory : new LocalDirectory(local));
                    services.AddSingleton<ISessionRunnerClient>(Remote ? new RoutingSessionRunnerClient(Host.Directory) : local);
                    services.AddSingleton(Host.App.Services.GetRequiredService<IOptions<PhoneHomeRunnerSettings>>());
                    services.AddSingleton<PhoneHomeLaunchPolicy>();
                    services.AddSingleton<IOptions<AgentRegistrySettings>>(Options.Create(registry));
                    services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(registry));
                    services.AddSingleton<IAgentProtocolAdapterFactory>(sp => new KindFactory(
                        Remote ? new RunnerScopedSessionRunnerClient(Host.Directory, Host.AllowedRunnerId) : local,
                        registry, sp.GetRequiredService<IOptions<SupervisionSettings>>(), Clock));
                    services.AddSingleton<LandDeliveryBoundary>(Freeze);
                    services.AddSingleton<IAgentTaskLaunchSink>(Launches);
                    services.AddSingleton<RemoteSpillCourier>();
                    services.AddSingleton(Options.Create(new GrokRulesSettings()));
                    services.AddSingleton<GrokRulesRefreshService>();
                    services.AddScoped<RemoteWorkspaceService>();
                    services.AddSingleton<RemoteWorkspacePreparer>();
                    services.RemoveAll<IWorktreeManager>();
                    services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Git.WorktreeRoot });
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    services.AddScoped<AgentTaskService>();
                    services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>());
                    services.AddScoped<AgentTaskDispatcher>();
                    if (Busy) services.AddSingleton<IEventBus>(sp => new BusyBus(sp.GetRequiredService<MockEventBus>(), Recipient,
                        () => sp.GetRequiredService<AgentSessionRuntime>()));
                },
            });
            Freeze.Settings = Harness.Delegation;
            _helperSession = Harness.SessionId; _helperAgent = Harness.AgentId;
            _roots.Add(Harness.TempRoot);
            _graph = true;
        }

        public async Task RecreateAsync(Guid sessionId)
        {
            await CloseGraphAsync();
            Recipient.Terminals[sessionId].Killed.ShouldBeFalse("C1029 retained recipient survives server disposal");
            await OpenGraphAsync();
            var adapter = Harness.Provider.GetRequiredService<IAgentProtocolAdapterFactory>().Create(Kind);
            await ((IAttachableProtocolAdapter)adapter).AttachAsync(sessionId, CancellationToken.None);
            Harness.Runtime.Register(sessionId, adapter);
            await Harness.Runtime.CatchUpTranscriptAsync(sessionId, CancellationToken.None);
        }

        public async Task<Guid> CreateTaskAsync(string body)
        {
            using var scope = Harness.Provider.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                new(body, Title: "C1029 delivery", Role: AgentTaskRole.Docs, AgentKind: Kind,
                    ModelLevel: AgentModelLevel.High, Workspace: WorkspaceMode.Worktree, RunnerId: Remote ? Host.AllowedRunnerId : "local"),
                new(null, null, Git.Path), CancellationToken.None)).Id;
        }
        public async Task DispatchAsync()
        {
            async Task Tick()
            {
                using var scope = Harness.Provider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            }
            await Tick();
            if (Remote) { await Harness.Provider.GetRequiredService<RemoteWorkspacePreparer>().WhenIdleAsync(); await Tick(); }
            await using var db = Context();
            foreach (var task in await db.AgentTasks.AsNoTracking().ToListAsync())
            {
                Console.WriteLine($"C1029 DISPATCH task={task.Id} attempt={task.Attempt} status={task.Status} session={task.AgentSessionId}");
                if (task.Status != AgentTaskStatus.Dispatched)
                    foreach (var e in await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == task.Id).OrderBy(e => e.At).ToListAsync())
                        Console.WriteLine($"C1029 EVENT task={task.Id} type={e.Type} detail={e.Detail}");
            }
        }
        public void Launch() => Launches.Release(Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>());
        public Task JoinLaunchAsync() => Harness.Provider.GetRequiredService<AgentSessionLaunchQueue>()
            .WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        public async Task EligibleAsync(Guid session)
        {
            Recipient.Append(session, TranscriptKinds.TurnEnd, stopReason: "end_turn");
            await Harness.Runtime.CatchUpTranscriptAsync(session, CancellationToken.None);
        }
        public Task FlushAsync(Guid session) => Harness.Queue.FlushSessionAsync(session, CancellationToken.None);
        private async Task CloseGraphAsync()
        {
            if (!_graph)
            {
                if (Peer is not null) await Peer.DisposeAsync();
                if (Host is not null) await Host.DisposeAsync();
                Peer = null!; Host = null!;
                return;
            }
            await JoinLaunchAsync();
            await Harness.Provider.GetRequiredService<RemoteWorkspacePreparer>().WhenIdleAsync();
            await Harness.DisposeAsync();
            await Peer.DisposeAsync();
            await Host.DisposeAsync();
            Peer = null!; Host = null!;
            _graph = false;
        }
        public async ValueTask DisposeAsync()
        {
            await CloseGraphAsync();
            await Recipient.DisposeAsync();
            await Schema.DisposeAsync();
            Git.Dispose();
            foreach (var root in _roots.Append(Root))
                if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true);
        }
    }

    internal sealed class RecoveryClock : TimeProvider
    {
        private TimeSpan _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
        public void Advance(TimeSpan time) => _offset += time;
    }

    // Fault only the identified task's production queue insert or verdict save. No fake queue.
    internal sealed class QueueFault : SaveChangesInterceptor
    {
        public Guid? TaskId { get; set; }
        public Guid? QueueId { get; set; }
        public bool AfterSave { get; set; }
        public bool Verdict { get; set; }
        public bool KeepFailing { get; set; }
        public bool RejectRetry { get; set; }
        public int Hits { get; private set; }
        private bool _saving;
        private bool Matches(DbContext? db) => db is not null && db.ChangeTracker.Entries<SessionQueuedMessage>().Any(e =>
            RejectRetry ? e.Entity.Id == QueueId && e.State == EntityState.Modified
                && e.Property(x => x.DeliveryAttempts).CurrentValue > e.Property(x => x.DeliveryAttempts).OriginalValue
                : Verdict ? e.Entity.Id == QueueId && e.State == EntityState.Modified && e.Entity.DeliveryVerdict != null
            : e.State == EntityState.Added && (e.Entity.ExecutionTaskId == TaskId || e.Entity.SourceTaskId == TaskId) && TaskId != null);
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            _saving = Matches(data.Context);
            if (_saving && !AfterSave) Fail();
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (_saving && AfterSave) Fail();
            return ValueTask.FromResult(result);
        }
        private void Fail() { Hits++; if (!KeepFailing) TaskId = QueueId = null; _saving = false; throw new InvalidOperationException("C1029 owned queue save fault"); }
    }

    private sealed class KindFactory(ISessionRunnerClient client, AgentRegistrySettings registry,
        IOptions<SupervisionSettings> verification, TimeProvider clock) : IAgentProtocolAdapterFactory, IAsyncDisposable
    {
        private readonly List<IAgentProtocolAdapter> _created = [];
        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            IAgentProtocolAdapter adapter = kind switch
            {
                AgentKind.Grok => new RunnerGrokAdapter(client, Options.Create(registry), verification, time: clock),
                AgentKind.ClaudeCode => new RunnerClaudeAdapter(client, Options.Create(registry), verification, time: clock),
                _ => new RunnerCodexAdapter(client, Options.Create(registry)),
            };
            _created.Add(adapter); return adapter;
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var adapter in _created) { await adapter.DisposeAsync(); await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(5)); }
        }
    }
    private sealed class SignedInProbe : IProviderAuthProbe
    {
        public Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct) =>
            Task.FromResult(new RunnerProviderAuthDto(provider, true, "fixture", null, DateTimeOffset.UtcNow, null));
    }

    private sealed class LocalDirectory(LocalRecipientClient client) : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => client;
        public IReadOnlyList<string> KnownRunnerIds => [];
        public ISessionRunnerClient Resolve(string? id) => client;
        public Guid? GetLiveStoreId(string? id) => null;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct) => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
    }
    private sealed class LocalRecipientClient(Recipient recipient) : ISessionRunnerClient
    {
        private static SessionRunnerSessionDto Map(RunnerSessionDto s) => new(s.SessionId,s.Pid,s.StartedAt,s.Status,s.ExitCode,
            AgentExitReason.Unknown,s.LastSequence,TranscriptBound:s.TranscriptBound,GrokRulesReceipt:s.GrokRulesReceipt,AcceptedStartedAt:s.AcceptedStartedAt);
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => Task.FromResult<RunnerCapabilitiesDto?>(recipient.Capabilities());
        public Task<RunnerProviderAuthDto?> GetProviderAuthAsync(string provider, CancellationToken ct) => Task.FromResult<RunnerProviderAuthDto?>(new(provider,true,"fixture",null,DateTimeOffset.UtcNow,null));
        public Task<RunnerCodexCliVersionDto?> GetCodexCliVersionAsync(RunnerCodexCliProbeRequest request,CancellationToken ct) => recipient.GetCodexCliVersionAsync(request,ct);
        public async Task<SessionRunnerSessionDto> StartAsync(Guid id,AgentLaunchSpec s,CancellationToken ct) => Map(await recipient.StartAsync(new(id,s.Exe,s.Args,s.Env,s.Cwd,s.Cols,s.Rows,GrokRulesPayload:s.GrokRulesPayload,AcceptedStartedAt:s.AcceptedStartedAt),ct));
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(recipient.List().Select(Map).ToArray());
        public async Task<SessionRunnerSessionDto> GetAsync(Guid id,CancellationToken ct) => Map(await recipient.GetAsync(id,ct));
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id,CancellationToken ct) { var s=recipient.GetBuffer(id);return Task.FromResult(new SessionRunnerBufferDto(id,s.Buffer,s.LastSequence)); }
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id,CancellationToken ct) { var s=recipient.GetSnapshot(id);return Task.FromResult(new SessionRunnerSnapshotDto(id,s.RawOutput,s.RenderedScreen,s.LastSequence,s.StartedAt,s.AcceptedStartedAt)); }
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id,CancellationToken ct)
        {
            var s=recipient.GetTranscript(id);
            return Task.FromResult(new SessionRunnerTranscriptDto(id,s.Entries.Select(e => new SessionRunnerTranscriptEvent(id,e.Sequence,e.Kind,e.Uuid,e.ParentUuid,e.Timestamp,e.Role,e.Text,e.ToolName,e.ToolInput,e.ToolUseId,e.ToolIsError,e.StopReason)).ToArray(),s.LastSequence));
        }
        public Task SendInputAsync(Guid id,string input,CancellationToken ct) => recipient.SendInputAsync(id,input,ct);
        public Task ClearLiveBufferAsync(Guid id,CancellationToken ct) => recipient.ClearLiveBufferAsync(id,ct);
        public Task ResizeAsync(Guid id,int cols,int rows,CancellationToken ct) => recipient.ResizeAsync(id,cols,rows,ct);
        public async Task<SessionRunnerSessionDto> KillAsync(Guid id,CancellationToken ct) { if(recipient.Terminals.TryGetValue(id,out var t)) await t.KillAsync(TimeSpan.FromSeconds(1),ct); return await GetAsync(id,ct); }
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid id,DateTime expected,CancellationToken ct) => recipient.KillGenerationAsync(id,expected,ct);
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { await Task.CompletedTask; yield break; }
    }
}
