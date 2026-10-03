using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.Agents.Pty.Tests;
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
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Antiphon.Tests.Application;

// Only the terminal/provider is scripted. Producer, git mirror, launch queue, adapter,
// phone-home frames, runner spill writer, delivery queue and transcript pull are real.
internal static class CodexCliRemoteDeliveryFixture
{
    public static async Task RunAsync(string body, string version, bool busy)
    {
        var vector = $"remote/{version}/busy={busy}";
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var git = new ScratchGitRepo("c959-remote");
        await git.CommitFileAsync("seed.txt", "C959 real runner mirror\n");
        await git.AddBareOriginAsync();
        var root = System.IO.Directory.CreateTempSubdirectory("c959-recipient-").FullName;
        try
        {
            var runtime = new Recipient(version);
            var dispatcher = new PhoneHomeCommandDispatcher(runtime, new PhoneHomeSettings
            {
                AllowedCwd = root, RunnerRepository = Path.Combine(root, "repo"),
                RunnerCloneSource = (await git.GitReadAsync("remote", "get-url", "origin")).Trim(),
                CapacityStatePath = Path.Combine(root, "capacity"),
                LaunchGenerationsPath = Path.Combine(root, "generations"),
            });
            await using var host = await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString,
                configureRunnerSettings: settings =>
                {
                    settings.AllowDelegatedTasks = true;
                    settings.HostWorkspaceRoot = git.Path;
                    settings.RunnerWorkspace = root;
                    settings.RunnerRepository = Path.Combine(root, "repo");
                    settings.CallbackOrigin = "https://antiphon.test";
                    settings.CodexAuthProbeEnabled = false;
                },
                configureServices: services =>
                {
                    services.AddSingleton<RemoteSpillCourier>();
                    services.AddSingleton(sp => new PhoneHomeRunnerDirectory(
                        sp.GetRequiredService<ISessionRunnerClient>(),
                        sp.GetRequiredService<IOptions<PhoneHomeRunnerSettings>>(),
                        sp.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
                        sp.GetRequiredService<RemoteSpillCourier>()));
                });
            await using var peer = await host.ConnectPeerAsync(capabilities: runtime.Capabilities());
            peer.Reply = request => dispatcher.DispatchAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            host.Directory.MarkRecovered(await host.WaitLiveAsync());
            var registry = CodexCliAdmissionTests.Registry();
            var freeze = new Freeze(schema.ConnectionString);
            var launches = new HeldLaunches();
            await using var h = await BridgeQueueHarness.CreateAsync(new()
            {
                AlwaysOn = false, ConnectionString = schema.ConnectionString,
                Delegation = new() { DefaultWorkerWorkspace = WorkspaceMode.Worktree, MaxConcurrentTasks = 20, RolePolicy = new() },
                ConfigureServices = services =>
                {
                    services.AddSingleton<ISessionRunnerDirectory>(host.Directory);
                    services.AddSingleton<ISessionRunnerClient>(new RoutingSessionRunnerClient(host.Directory));
                    services.AddSingleton(host.App.Services.GetRequiredService<IOptions<PhoneHomeRunnerSettings>>());
                    services.AddSingleton<PhoneHomeLaunchPolicy>();
                    services.AddSingleton<IOptions<AgentRegistrySettings>>(Options.Create(registry));
                    services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(registry));
                    services.AddSingleton<IAgentProtocolAdapterFactory>(new RemoteFactory(host.Directory, host.AllowedRunnerId, registry));
                    services.AddSingleton<LandDeliveryBoundary>(freeze);
                    services.AddSingleton<IAgentTaskLaunchSink>(launches);
                    services.AddSingleton<RemoteSpillCourier>();
                    services.AddScoped<RemoteWorkspaceService>();
                    services.AddSingleton<RemoteWorkspacePreparer>();
                    services.RemoveAll<IWorktreeManager>();
                    services.AddDelegationWorktreeGraph(new GitSettings { WorktreeBasePath = git.WorktreeRoot });
                    services.AddSingleton<DelegationWorkspaceResolver>();
                    services.AddScoped<AgentTaskService>();
                    services.AddScoped<IDelegateSessionStopper>(sp => sp.GetRequiredService<AgentSessionService>());
                    services.AddScoped<AgentTaskDispatcher>();
                    if (busy) services.AddSingleton<IEventBus>(sp => new BusyBus(sp.GetRequiredService<MockEventBus>(), schema.ConnectionString));
                },
            });
            freeze.Settings = h.Delegation;
            AgentTaskCreatedDto created;
            using (var scope = h.Provider.CreateScope())
                created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                    new(body, Title: body, Role: AgentTaskRole.Docs, AgentKind: AgentKind.Codex,
                        ModelLevel: AgentModelLevel.High, Workspace: WorkspaceMode.Worktree, RunnerId: host.AllowedRunnerId),
                    new(null, null, git.Path), CancellationToken.None);
            async Task Tick()
            {
                using var scope = h.Provider.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AgentTaskDispatcher>().TickAsync(CancellationToken.None);
            }
            await Tick();
            await h.Provider.GetRequiredService<RemoteWorkspacePreparer>().WhenIdleAsync();
            await Tick();
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
            task.Status.ShouldBe(AgentTaskStatus.Dispatched, "C959-v21-remote-claim " + vector);
            task.RunnerId.ShouldBe(host.AllowedRunnerId, "C959-pc-185 " + vector);
            System.IO.Directory.Exists(task.RemoteWorktreePath).ShouldBeTrue("C959-v21-real-runner-worktree " + vector);
            var queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.ExecutionTaskId == task.Id);
            var expectedFull = freeze.Full[task.Id];
            expectedFull.ShouldContain(body, customMessage: "C959-v21-remote-literal " + vector);
            expectedFull.ShouldNotContain("\r");
            var owned = TypedBodySpill.InboxRelativePath(queued.Id.ToString("D"));
            queued.Body.ShouldContain(owned, customMessage: "C959-pc-213 " + vector);
            queued.RemoteSpillRelativePath.ShouldBe(owned);
            queued.RemoteSpillBody.ShouldBe(expectedFull, "C959-pc-216 " + vector);
            var expectedWire = DelegationReportFormatter.BuildBriefPointer(freeze.Tasks[task.Id], h.Delegation,
                owned, expectedFull.Length, AgentKind.Codex).TrimEnd();
            queued.Body.ShouldBe(expectedWire, "C959-pc-186 remote " + vector);
            var spillPath = Path.Combine(task.RemoteWorktreePath!, owned);
            runtime.BeforeBody = async (id, input) =>
            {
                if (!input.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal)) return;
                id.ShouldBe(task.AgentSessionId!.Value, "C959-v21-remote-recipient-identity " + vector);
                File.Exists(spillPath).ShouldBeTrue("C959-pc-217 " + vector);
                (await File.ReadAllBytesAsync(spillPath)).ShouldBe(Encoding.UTF8.GetBytes(expectedFull), "C959-pc-211 " + vector);
            };
            launches.Release(h.Provider.GetRequiredService<AgentSessionLaunchQueue>());
            await h.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            var terminal = runtime.Terminals[task.AgentSessionId!.Value];
            if (busy)
            {
                terminal.SubmittedBodies.ShouldBeEmpty("C959-pc-188 remote " + vector);
                (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == queued.Id)).Status.ShouldBe(QueuedMessageStatus.Pending);
                await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn", sessionId: task.AgentSessionId);
                await h.Queue.FlushSessionAsync(task.AgentSessionId.Value, CancellationToken.None);
            }
            terminal.SubmittedBodies.Single().ShouldBe(expectedWire, "C959-v21-remote-W " + vector);
            expectedWire.ShouldNotContain("\n", customMessage: "C959-pc-212 remote " + vector);
            expectedWire.ShouldNotContain("\r");
            runtime.ProbeRequests.ShouldNotBeEmpty("C959-v21-real-version-operation " + vector);
            var args = terminal.StartedArgs.ToList();
            args.Count(a => a == "--model").ShouldBe(1, "C959-pc-184 remote " + vector);
            args[args.IndexOf("--model") + 1].ShouldBe("gpt-6.1-sol");
            (await h.Runtime.CatchUpTranscriptAsync(task.AgentSessionId.Value, CancellationToken.None)).ShouldBeTrue();
            var receipts = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == task.AgentSessionId && e.Kind == TranscriptKinds.UserPrompt).ToListAsync();
            receipts.Count.ShouldBe(1, "C959-v21-remote-no-duplicate " + vector);
            receipts.Single().Text.ShouldBe(expectedWire, "C959-v21-remote-pulled-receipt " + vector);
            queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == queued.Id);
            var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == task.AgentSessionId);
            queued.LastDeliveryGeneration.ShouldBe(SessionGeneration.Normalize(session.StartedAt));
            receipts.Single().Sequence.ShouldBeGreaterThan(queued.LastDeliveryBaselineSequence ?? 0);
            queued.RemoteSpillBody.ShouldBeNull("C959-v21-complete-receipt-releases-spill " + vector);
            (await File.ReadAllBytesAsync(spillPath)).ShouldBe(Encoding.UTF8.GetBytes(expectedFull));
            await runtime.StopAsync();
        }
        finally { System.IO.Directory.Delete(root, recursive: true); }
    }

    private sealed class HeldLaunches : IAgentTaskLaunchSink
    {
        private readonly List<(Guid Session, Guid Agent, DateTime Generation, AgentLaunchSpec Spec)> _items = [];
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) => _items.Add((sessionId, agentId, acceptedGeneration, spec));
        public void Release(AgentSessionLaunchQueue queue)
        {
            _items.ShouldHaveSingleItem("C959-v21-one-real-launch");
            foreach (var item in _items) queue.EnqueueInteractiveSession(item.Session, item.Agent, item.Generation, item.Spec, null, null);
            _items.Clear();
        }
    }

    private sealed class Freeze(string connection) : LandDeliveryBoundary
    {
        public DelegationSettings Settings { get; set; } = new();
        public Dictionary<Guid, AgentTask> Tasks { get; } = [];
        public Dictionary<Guid, string> Full { get; } = [];
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "dispatch-warning-claim-committed") return;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
            Tasks.Add(taskId, task);
            Full.Add(taskId, DelegationReportFormatter.BuildBrief(task, Settings,
                Settings.CeilingsFor(PtyBackend.InboxConhost, "runner").ForAgentKind(task.AgentKind).ReplyInlineMaxChars, refocus: false));
        }
    }

    private sealed class RemoteFactory(ISessionRunnerDirectory directory, string runnerId, AgentRegistrySettings registry) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => new RunnerCodexAdapter(new RunnerScopedSessionRunnerClient(directory, runnerId), Options.Create(registry));
    }

    private sealed class BusyBus(MockEventBus inner, string connection) : IEventBus
    {
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => inner.PublishToAllAsync(name, payload, ct);
        public async Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default)
        {
            if (name == "SessionStarted")
            {
                var id = (Guid)payload.GetType().GetProperty("sessionId")!.GetValue(payload)!;
                await BridgeQueueHarness.InsertEntryAsync(id, TranscriptKinds.TurnEnd, stopReason: "end_turn", connectionString: connection);
                await BridgeQueueHarness.InsertEntryAsync(id, TranscriptKinds.AssistantText, "C959 remote activity after TurnEnd", connectionString: connection);
            }
            await inner.PublishToGroupAsync(group, name, payload, ct);
        }
    }

    private sealed class Recipient(string version) : IPhoneHomeRuntimeSurface
    {
        public Dictionary<Guid, FakeAgentProtocolAdapter> Terminals { get; } = [];
        private readonly Dictionary<Guid, List<RunnerTranscriptEvent>> _transcripts = [];
        public List<RunnerCodexCliProbeRequest> ProbeRequests { get; } = [];
        public Func<Guid, string, Task>? BeforeBody { get; set; }
        public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "test", false,
            Version: "d40c1670", Platform: "linux", Features: [RunnerCapabilityFeatures.SessionGenerationV1, RunnerCapabilityFeatures.WorkspaceRepositoryV1]);
        public Task<RunnerCodexCliVersionDto?> GetCodexCliVersionAsync(RunnerCodexCliProbeRequest request, CancellationToken ct)
        {
            ProbeRequests.Add(request);
            return Task.FromResult<RunnerCodexCliVersionDto?>(new(version, DateTimeOffset.UtcNow, null, new string('a',64)));
        }
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => Terminals.Keys.Select(Session).ToList();
        private RunnerSessionDto Session(Guid id) => new(id, 1234, Terminals[id].StartedAcceptedGeneration!.Value,
            Terminals[id].Killed ? "Exited" : "Running", Terminals[id].Killed ? 0 : null, "Unknown", _transcripts[id].Count,
            AcceptedStartedAt: Terminals[id].StartedAcceptedGeneration);
        public Task<RunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Session(id));
        public async Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            var terminal = new FakeAgentProtocolAdapter();
            Terminals.Add(request.SessionId, terminal);
            _transcripts.Add(request.SessionId, []);
            terminal.OnSubmitted = text =>
            {
                var entries = _transcripts[request.SessionId];
                entries.Add(new(request.SessionId, entries.Count + 1, TranscriptKinds.UserPrompt, null, null, DateTimeOffset.UtcNow,
                    "user", text, null, null, null, null, null));
                entries.Add(new(request.SessionId, entries.Count + 1, TranscriptKinds.TurnEnd, null, null, DateTimeOffset.UtcNow,
                    null, null, null, null, null, null, "end_turn"));
                return Task.CompletedTask;
            };
            await terminal.StartAsync(new AgentLaunchSpec("codex", AgentKind.Codex, request.Exe, request.Args, request.Env, request.Cwd,
                request.Cols, request.Rows,
                SessionId: request.SessionId, AcceptedStartedAt: request.AcceptedStartedAt), ct);
            return Session(request.SessionId);
        }
        public RunnerBufferDto GetBuffer(Guid id) => new(id, Terminals[id].SnapshotRawOutput(), _transcripts[id].Count);
        public RunnerSnapshotDto GetSnapshot(Guid id)
        {
            var terminal = Terminals[id];
            return new(id, terminal.SnapshotRawOutput(), terminal.Inputs.Count == 0 ? CodexStartupFixtures.P3 : terminal.SnapshotRenderedScreen(),
                _transcripts[id].Count, terminal.StartedAcceptedGeneration!.Value, terminal.StartedAcceptedGeneration);
        }
        public RunnerTranscriptDto GetTranscript(Guid id) => new(id, _transcripts[id].ToArray(), _transcripts[id].Count);
        public async Task SendInputAsync(Guid id, string input, CancellationToken ct)
        {
            if (BeforeBody is { } check) await check(id, input);
            await Terminals[id].SendInputAsync(input, ct);
        }
        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid id, RunnerConditionalInputRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => Terminals[id].ResizeAsync(cols, rows, ct);
        public async Task<RunnerKillGenerationResult> KillGenerationAsync(Guid id, DateTime expected, CancellationToken ct)
        {
            var killed = await Terminals[id].KillGenerationAsync(expected, TimeSpan.FromSeconds(1), ct);
            return new(id, killed, killed ? KillGenerationOutcomes.Killed : KillGenerationOutcomes.Mismatch, DateTime.UtcNow);
        }
        public int OwnedSessionCount => Terminals.Values.Count(t => !t.Killed);
        public async Task StopAsync() { foreach (var terminal in Terminals.Values) await terminal.KillAsync(TimeSpan.FromSeconds(1), CancellationToken.None); }
    }
}
