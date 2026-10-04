using System.Text;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Collections.Concurrent;
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
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.Application;

internal static partial class CodexCliRemoteDeliveryFixture
{
    // The world owns the recipient, origin and database. Every graph owns its own transport,
    // adapters and courier; no surviving positive receipt comes from a server cache.
    internal sealed class World : IAsyncDisposable
    {
        public required IsolatedTestSchema Schema { get; init; }
        public required ScratchGitRepo Git { get; init; }
        public required string Root { get; init; }
        public required bool Remote { get; init; }
        public Recipient Recipient { get; } = new(new("0.160.0", DateTimeOffset.UtcNow, null, new string('a', 64)));
        public Clock Clock { get; } = new();
        public SaveFault Fault { get; } = new();
        public Freeze Freeze { get; private set; } = null!;
        public BridgeQueueHarness H { get; private set; } = null!;
        public PhoneHomeTestHost? Host { get; private set; }
        public PhoneHomeScriptedPeer? Peer { get; private set; }
        private AdapterFactory _factory = null!;
        private HeldLaunches _launches = null!;
        private readonly List<string> _graphRoots = [];
        private readonly Guid _store = Guid.NewGuid();
        private readonly Guid _boot = Guid.NewGuid();
        public ConcurrentDictionary<string, int> ProbeCalls { get; } = new();
        private int _oldProbeFrames;
        public Guid TaskId { get; private set; }
        public Guid SessionId { get; private set; }
        public string RunnerId => Remote ? Host!.AllowedRunnerId : "local";
        public AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions(Schema.ConnectionString));

        public static async Task<World> CreateAsync(bool remote, bool busy = false)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var git = new ScratchGitRepo("c1029-world");
            await git.CommitFileAsync("seed.txt", "C1029 isolated producer\n");
            await git.AddBareOriginAsync();
            var world = new World { Schema = schema, Git = git, Remote = remote,
                Root = System.IO.Directory.CreateTempSubdirectory("c1029-recipient-").FullName };
            world.Freeze = new(schema.ConnectionString);
            world.Recipient.Time = world.Clock;
            try { await world.OpenGraphAsync(busy); return world; }
            catch { await world.DisposeAsync(); throw; }
        }

        private async Task OpenGraphAsync(bool busy)
        {
            var local = new SurfaceClient(Recipient);
            ISessionRunnerDirectory directory = new SingleRunnerDirectory(local);
            if (Remote)
            {
                var dispatcher = new PhoneHomeCommandDispatcher(Recipient, new PhoneHomeSettings
                {
                    AllowedCwd = Root, RunnerRepository = Path.Combine(Root, "repo"),
                    RunnerCloneSource = (await Git.GitReadAsync("remote", "get-url", "origin")).Trim(),
                    CapacityStatePath = Path.Combine(Root, "capacity"), LaunchGenerationsPath = Path.Combine(Root, "generations"),
                }, new SignedIn());
                Host = await PhoneHomeTestHost.StartAsync(clock: Clock, connectionString: Schema.ConnectionString,
                    configureRunnerSettings: s =>
                    {
                        services.AddSingleton<TimeProvider>(Clock);
                        s.AllowDelegatedTasks = true; s.HostWorkspaceRoot = Git.Path; s.RunnerWorkspace = Root;
                        s.RunnerRepository = Path.Combine(Root, "repo"); s.CallbackOrigin = "https://antiphon.test";
                    }, configureServices: services =>
                    {
                        services.AddSingleton<RemoteSpillCourier>();
                        services.AddSingleton(sp => new PhoneHomeRunnerDirectory(
                            sp.GetRequiredService<ISessionRunnerClient>(), sp.GetRequiredService<IOptions<PhoneHomeRunnerSettings>>(),
                            sp.GetRequiredService<IServiceScopeFactory>(), Clock, sp.GetRequiredService<RemoteSpillCourier>()));
                    });
                Peer = await Host.ConnectPeerAsync(capabilities: Recipient.Capabilities(), storeId: _store, bootId: _boot);
                Peer.Reply = r => dispatcher.DispatchAsync(r, CancellationToken.None).GetAwaiter().GetResult();
                Host.Directory.MarkRecovered(await Host.WaitLiveAsync());
                directory = Host.Directory;
            }
            var counted = DispatchProxy.Create<ISessionRunnerDirectory, DirectoryCounter>();
            ((DirectoryCounter)counted).Inner = directory;
            ((DirectoryCounter)counted).Owner = this;
            directory = counted;
            ISessionRunnerClient selected = Remote ? new RunnerScopedSessionRunnerClient(directory, RunnerId) : directory.Local;
            var registry = CodexCliObservationTests.Registry();
            var authHome = Path.Combine(Root, "grok-home");
            System.IO.Directory.CreateDirectory(authHome);
            await File.WriteAllTextAsync(Path.Combine(authHome, "auth.json"), "{\"default\":{\"key\":\"c1029-synthetic-key\",\"refresh_token\":\"c1029-synthetic-refresh\"}}");
            registry.Definitions["grok"] = new() { Kind = "Grok", Exe = "grok", Env = new() { ["GROK_HOME"] = authHome } };
            registry.GrokReadyMinTotalWaitMs = 0;
            registry.GrokStartupCaptureDirectory = Root;
            _launches = new();
            H = await BridgeQueueHarness.CreateAsync(new()
            {
                AlwaysOn = false, ConnectionString = Schema.ConnectionString, PreserveDatabaseOnDispose = true, TimeProvider = Clock,
                ConfigureDbContext = o => o.AddInterceptors(Fault),
                Delegation = new() { DefaultWorkerWorkspace = WorkspaceMode.Worktree, MaxConcurrentTasks = 20, RolePolicy = new() },
                ConfigureServices = services =>
                {
                    services.AddSingleton<ISessionRunnerDirectory>(directory);
                    services.AddSingleton<ISessionRunnerClient>(selected);
                    if (Remote) services.AddSingleton(Host!.App.Services.GetRequiredService<IOptions<PhoneHomeRunnerSettings>>());
                    services.AddSingleton<PhoneHomeLaunchPolicy>();
                    services.AddSingleton<IOptions<AgentRegistrySettings>>(Options.Create(registry));
                    services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(registry));
                    services.AddSingleton<IAgentProtocolAdapterFactory>(sp => _factory = new(
                        selected, registry,
                        sp.GetRequiredService<IOptions<SupervisionSettings>>()));
                    services.AddSingleton<LandDeliveryBoundary>(Freeze);
                    services.AddSingleton<IAgentTaskLaunchSink>(_launches);
                    services.AddSingleton<RemoteSpillCourier>();
                    services.AddScoped<RemoteWorkspaceService>(); services.AddSingleton<RemoteWorkspacePreparer>();
                    services.RemoveAll<IWorktreeManager>();
                    services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = Git.WorktreeRoot });
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    services.AddScoped<AgentTaskService>();
                    services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>());
                    services.AddScoped<AgentTaskDispatcher>();
                    services.AddSingleton(Options.Create(new GrokRulesSettings())); services.AddSingleton<GrokRulesRefreshService>();
                    if (busy) services.AddSingleton<IEventBus>(sp => new BusyBus(sp.GetRequiredService<MockEventBus>(), Recipient,
                        () => sp.GetRequiredService<AgentSessionRuntime>()));
                },
            });
            _graphRoots.Add(H.TempRoot);
            Freeze.Settings = H.Delegation;
        }

        public async Task ProduceAsync(AgentKind kind = AgentKind.Codex, bool longBody = false)
        {
            var body = "C959 delivery α\nsecond line\nEND-C959" + (longBody ? new string('x', 20000) : "");
            using var scope = H.Provider.CreateScope();
            TaskId = (await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                new(body, Title: "C1029 controlled delivery", Role: AgentTaskRole.Docs, AgentKind: kind, ModelLevel: AgentModelLevel.High,
                    Workspace: WorkspaceMode.Worktree, RunnerId: RunnerId), new(null, null, Git.Path), CancellationToken.None)).Id;
            Fault.TaskId = TaskId;
            AssertNoProbes("Create");
            await DispatchAsync();
        }

        public async Task DispatchAsync()
        {
            await RefreshHeartbeatAsync();
            async Task Tick()
            {
                using var scope = H.Provider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            }
            await Tick();
            if (Remote) { await H.Provider.GetRequiredService<RemoteWorkspacePreparer>().WhenIdleAsync(); await Tick(); }
            await using var db = Db();
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == TaskId);
            var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == TaskId)
                .OrderBy(e => e.At).Select(e => e.Detail).ToListAsync();
            AssertNoProbes("dispatch");
            SessionId = task.AgentSessionId.ShouldNotBeNull($"C1029 dispatch status={task.Status}: {task.FailureReason}; {string.Join("; ", events)}");
        }
        public async Task RefreshHeartbeatAsync()
        {
            if (!Remote) return;
            var sent = Clock.GetUtcNow();
            await Peer!.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Heartbeat, Peer.Epoch, Guid.NewGuid()));
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while ((Host!.Directory.SnapshotLive()?.LastHeartbeatUtc ?? DateTimeOffset.MinValue) < sent)
            {
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("C1029 framed heartbeat was not received");
                await Task.Delay(20);
            }
            await Host!.Directory.Resolve(RunnerId).ListAsync(CancellationToken.None);
        }
        public void DiscardUnstartedLaunch() => _launches.Discard();

        public async Task StartAsync()
        {
            _launches.Release(H.Provider.GetRequiredService<AgentSessionLaunchQueue>());
            await H.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            await AssertStartedAsync();
        }
        public async Task AssertStartedAsync()
        {
            AssertNoProbes("launch");
            await using var db = Db();
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId);
            var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == TaskId)
                .OrderBy(e => e.At).Select(e => e.Detail).ToListAsync();
            session.Status.ShouldBe(SessionStatus.Running,
                $"C1029 recipient launch: {session.FailureReason}; {session.GrokRulesFailure}; {string.Join("; ", events)}");
            Recipient.Terminals.ContainsKey(SessionId).ShouldBeTrue("C1029 launch reached recipient");
        }
        public async Task<SessionQueuedMessage> RowAsync()
        {
            await using var db = Db();
            return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.ExecutionTaskId == TaskId ||
                (q.SourceTaskId == TaskId && q.Origin == QueuedMessageOrigin.Delegation));
        }
        public async Task ReadyToFlushAsync()
        {
            Recipient.Append(SessionId, TranscriptKinds.TurnEnd, stopReason: "end_turn");
            await H.Runtime.CatchUpTranscriptAsync(SessionId, CancellationToken.None);
        }
        public async Task FlushAsync()
        {
            try { await H.Queue.FlushSessionAsync(SessionId, CancellationToken.None); }
            finally { AssertNoProbes("queue flush"); }
        }
        public void AssertNoProbes(string at)
        {
            foreach (var count in ProbeCalls) count.Value.ShouldBe(0, $"C1029 typed observation at {at}, {count.Key}");
            Recipient.ProbeRequests.ShouldBeEmpty("C1029 recipient typed observation at " + at);
            (_oldProbeFrames + (Peer?.RequestCount(PhoneHomeOperation.CodexCliVersion) ?? 0)).ShouldBe(0,
                "C1029 typed observation frames at " + at);
        }

        public string Full => Freeze.Full[TaskId];
        public PtyDeliveryCeilings Limits => H.Delegation.CeilingsFor(PtyBackend.InboxConhost, "runner").ForAgentKind(Freeze.Tasks[TaskId].AgentKind);
        public string Wire(SessionQueuedMessage row)
        {
            var task = Freeze.Tasks[TaskId];
            var limits = H.Delegation.CeilingsFor(PtyBackend.InboxConhost, "runner").ForAgentKind(task.AgentKind);
            if (Encoding.UTF8.GetByteCount(Full) <= limits.BriefInlineMaxBytes)
                return Full.TrimEnd();
            return DelegationReportFormatter.BuildBriefPointer(task, H.Delegation,
                Remote ? TypedBodySpill.InboxRelativePath(row.Id.ToString("D")) : LocalSpillPath,
                Full.Length, task.AgentKind, maxWireBytes: Remote ? limits.SingleWriteMaxBytes : null).TrimEnd();
        }
        public string LocalSpillPath => Path.Combine(Freeze.Tasks[TaskId].WorkingDirectory, ".antiphon", $"task-{DelegationReportFormatter.Short(TaskId)}-brief.md");
        public string SpillPath(SessionQueuedMessage row) => Remote
            ? Path.Combine(Freeze.Tasks[TaskId].RemoteWorktreePath!, TypedBodySpill.InboxRelativePath(row.Id.ToString("D"))) : LocalSpillPath;
        public FakeAgentProtocolAdapter Terminal => Recipient.Terminals[SessionId];

        public async Task AssertReceiptAsync(string label, bool spilled = true)
        {
            AssertNoProbes(label);
            var row = await RowAsync();
            var wire = Wire(row);
            Encoding.UTF8.GetByteCount(wire).ShouldBeLessThanOrEqualTo(Limits.SingleWriteMaxBytes, label + " single-write ceiling");
            Full.ShouldContain("C959 delivery α\nsecond line\nEND-C959", customMessage: label);
            Full.ShouldNotContain("\r");
            Terminal.SubmittedBodies.Count(b => b == wire).ShouldBe(1, label + " actual submissions");
            await H.Runtime.CatchUpTranscriptAsync(SessionId, CancellationToken.None);
            await using var db = Db();
            var from = row.LastDeliveryStartedAt?.AddSeconds(-30);
            var prompts = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == SessionId &&
                e.Kind == TranscriptKinds.UserPrompt && e.Text == wire &&
                (row.LastDeliveryBaselineSequence != null ? e.Sequence > row.LastDeliveryBaselineSequence : e.Timestamp != null && e.Timestamp >= from)).ToListAsync();
            prompts.Count.ShouldBe(1, label + " selected complete UserPrompt");
            row = await RowAsync();
            row.Status.ShouldBe(QueuedMessageStatus.Sent, label);
            row.RemoteSpillBody.ShouldBeNull(label + " release durable E only after receipt");
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == SessionId);
            row.LastDeliveryGeneration.ShouldBe(SessionGeneration.Normalize(session.StartedAt), label);
            if (spilled)
            {
                File.Exists(SpillPath(row)).ShouldBeTrue(label + " file exists");
                (await File.ReadAllBytesAsync(SpillPath(row))).ShouldBe(Encoding.UTF8.GetBytes(Full), label + " independent E bytes");
            }
            if (Remote) Peer!.RequestCount(PhoneHomeOperation.Transcript).ShouldBeGreaterThan(0, label);
        }

        public async Task RecreateAsync()
        {
            var id = SessionId;
            var kind = Freeze.Tasks[TaskId].AgentKind;
            await CloseGraphAsync();
            await OpenGraphAsync(false);
            var adapter = H.Provider.GetRequiredService<IAgentProtocolAdapterFactory>().Create(kind);
            await ((IAttachableProtocolAdapter)adapter).AttachAsync(id, CancellationToken.None);
            H.Runtime.Register(id, adapter);
            await H.Runtime.CatchUpTranscriptAsync(id, CancellationToken.None);
            AssertNoProbes("recreated graph");
            Recipient.Terminals[id].Killed.ShouldBeFalse("C1029 retained recipient survived graph disposal");
        }
        private async Task CloseGraphAsync()
        {
            if (H is not null)
            {
                await H.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                await H.DisposeAsync(); H = null!;
            }
            if (Peer is not null) { _oldProbeFrames += Peer.RequestCount(PhoneHomeOperation.CodexCliVersion); await Peer.DisposeAsync(); Peer = null; }
            if (Host is not null) { await Host.DisposeAsync(); Host = null; }
        }
        public async ValueTask DisposeAsync()
        {
            await CloseGraphAsync();
            await Recipient.DisposeAsync();
            Git.Dispose(); await Schema.DisposeAsync();
            foreach (var root in _graphRoots) if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
            if (System.IO.Directory.Exists(Root)) System.IO.Directory.Delete(Root, true);
        }
    }

    internal sealed class Clock : TimeProvider
    {
        private TimeSpan _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
        public void Advance(TimeSpan delta) => _offset += delta;
    }
    internal sealed class InjectedFault(string point) : Exception(point);
    internal sealed class SaveFault : SaveChangesInterceptor
    {
        public Guid TaskId { get; set; }
        public string? Point { get; set; }
        public Guid? QueueId { get; private set; }
        private bool _afterInsert;
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        {
            e.Context!.ChangeTracker.DetectChanges();
            foreach (var entry in e.Context!.ChangeTracker.Entries<SessionQueuedMessage>())
            {
                if (entry.Entity.ExecutionTaskId != TaskId && entry.Entity.SourceTaskId != TaskId) continue;
                if (entry.State == EntityState.Added && Point is "before-insert" or "after-insert")
                {
                    QueueId = entry.Entity.Id;
                    if (Point == "before-insert") Throw();
                    _afterInsert = true;
                }
                if (entry.State == EntityState.Modified
                    && entry.Entity.DeliveryVerdict is (DeliveryVerdict.Delivered or DeliveryVerdict.LateConfirmed)
                    && Point == "after-input") Throw();
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
        {
            if (_afterInsert) { _afterInsert = false; Throw(); }
            return ValueTask.FromResult(result);
        }
        private void Throw() { var point = Point!; Point = null; Fired = true; throw new InjectedFault(point); }
    }

    // Forward every interface member, including additive defaults, while counting typed
    // calls before transport/refusal. Counts belong to the world and survive graph loss.
    internal class ClientCounter : DispatchProxy
    {
        public ClientCounter() { }
        public ISessionRunnerClient Inner = null!;
        public World Owner = null!;
        public string Lane = "";
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(ISessionRunnerClient.GetCodexCliVersionAsync))
                Owner.ProbeCalls.AddOrUpdate(Lane, 1, (_, n) => n + 1);
            return Forward(method, Inner, args);
        }
    }
    internal class DirectoryCounter : DispatchProxy
    {
        public DirectoryCounter() { }
        public ISessionRunnerDirectory Inner = null!;
        public World Owner = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = Forward(method!, Inner, args);
            if (result is not ISessionRunnerClient client) return result;
            var proxy = DispatchProxy.Create<ISessionRunnerClient, ClientCounter>();
            var counter = (ClientCounter)proxy;
            counter.Inner = client; counter.Owner = Owner;
            counter.Lane = method!.Name == "get_Local" ? "local" : "selected:" + (args?[0] as string ?? "local");
            return proxy;
        }
    }
    private static object? Forward(MethodInfo method, object inner, object?[]? args)
    {
        try { return method.Invoke(inner, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private sealed class SignedIn : IProviderAuthProbe
    {
        public Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct) =>
            Task.FromResult(new RunnerProviderAuthDto(provider, true, "fixture", null, DateTimeOffset.UtcNow, null));
    }
    private sealed class AdapterFactory(ISessionRunnerClient client, AgentRegistrySettings registry, IOptions<SupervisionSettings> supervision)
        : IAgentProtocolAdapterFactory, IAsyncDisposable
    {
        private readonly List<IAgentProtocolAdapter> _adapters = [];
        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            IAgentProtocolAdapter a = kind switch
            {
                AgentKind.ClaudeCode => new RunnerClaudeAdapter(client, Options.Create(registry), supervision),
                AgentKind.Grok => new RunnerGrokAdapter(client, Options.Create(registry), supervision),
                _ => new RunnerCodexAdapter(client, Options.Create(registry)),
            };
            _adapters.Add(a); return a;
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var adapter in _adapters) { await adapter.DisposeAsync(); await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(5)); }
        }
    }

    // Local runner I/O speaks the same retained scripted terminal, without a phone-home peer.
    private sealed class SurfaceClient(Recipient surface) : ISessionRunnerClient
    {
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => Task.FromResult<RunnerCapabilitiesDto?>(surface.Capabilities());
        public Task<RunnerProviderAuthDto?> GetProviderAuthAsync(string provider, CancellationToken ct) => Task.FromResult<RunnerProviderAuthDto?>(new(provider, true, "fixture", null, DateTimeOffset.UtcNow, null));
        public async Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec s, CancellationToken ct)
        {
            var request = new RunnerLaunchRequest(id, s.Exe, s.Args, s.Env, s.Cwd, s.Cols, s.Rows,
                TranscriptFormat: s.Kind == AgentKind.Grok ? TranscriptFormats.Grok : s.Kind == AgentKind.ClaudeCode ? TranscriptFormats.Claude : TranscriptFormats.Codex,
                GrokRulesPayload: s.GrokRulesPayload, AcceptedStartedAt: s.AcceptedStartedAt);
            if (s.GrokRulesPayload is { } rules)
            {
                var path = Path.Combine(s.Cwd, ".antiphon", "instructions", "grok", id.ToString("N"), "rules.md");
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var bytes = GrokRulesTransport.Encode(rules, true, new GrokRulesSettings().MaxFileBytes);
                await File.WriteAllBytesAsync(path, bytes, ct);
                request = request with { InstalledGrokRulesReceipt = new(path, GrokRulesTransport.Hash(bytes), bytes.Length, 1, rules.Generation) };
            }
            await surface.StartAsync(request, ct); return await GetAsync(id, ct);
        }
        public async Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) { var list = new List<SessionRunnerSessionDto>(); foreach (var s in surface.List()) list.Add(await GetAsync(s.SessionId, ct)); return list; }
        public async Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct)
        {
            if (!surface.Terminals.ContainsKey(id)) return new(id, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.Unknown, 0);
            var s = await surface.GetAsync(id, ct);
            return new(id, s.Pid, s.StartedAt, s.Status, s.ExitCode, AgentExitReason.Unknown, s.LastSequence, GrokRulesReceipt: s.GrokRulesReceipt, AcceptedStartedAt: s.AcceptedStartedAt);
        }
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) { var s = surface.GetBuffer(id); return Task.FromResult(new SessionRunnerBufferDto(id, s.Buffer, s.LastSequence)); }
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) { var s = surface.GetSnapshot(id); return Task.FromResult(new SessionRunnerSnapshotDto(id, s.RawOutput, s.RenderedScreen, s.LastSequence, s.StartedAt, s.AcceptedStartedAt)); }
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct)
        {
            var s = surface.GetTranscript(id);
            return Task.FromResult(new SessionRunnerTranscriptDto(id, s.Entries.Select(e => new SessionRunnerTranscriptEvent(e.SessionId, e.Sequence, e.Kind,
                e.Uuid, e.ParentUuid, e.Timestamp, e.Role, e.Text, e.ToolName, e.ToolInput, e.ToolUseId, e.ToolIsError, e.StopReason)).ToArray(), s.LastSequence));
        }
        public Task SendInputAsync(Guid id, string input, CancellationToken ct) => surface.SendInputAsync(id, input, ct);
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => surface.ClearLiveBufferAsync(id, ct);
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => surface.ResizeAsync(id, cols, rows, ct);
        public async Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) { if (surface.Terminals.TryGetValue(id, out var terminal)) await terminal.KillAsync(TimeSpan.FromSeconds(1), ct); return await GetAsync(id, ct); }
        public Task<RunnerKillGenerationResult> KillGenerationAsync(Guid id, DateTime expected, CancellationToken ct) => surface.Terminals.ContainsKey(id)
            ? surface.KillGenerationAsync(id, expected, ct) : Task.FromResult(new RunnerKillGenerationResult(id, false, KillGenerationOutcomes.Missing, null));
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { await Task.CompletedTask; yield break; }
    }
}
