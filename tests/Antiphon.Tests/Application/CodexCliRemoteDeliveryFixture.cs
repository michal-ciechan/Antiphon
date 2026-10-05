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
internal static partial class CodexCliRemoteDeliveryFixture
{
    public static Task RunAsync(string body, string version, bool busy) =>
        RunAsync(body, new RunnerCodexCliVersionDto(version, DateTimeOffset.UtcNow, null, new string('a', 64)), busy);

    public static async Task RunAsync(string body, RunnerCodexCliVersionDto? sample, bool busy, AgentModelLevel level = AgentModelLevel.High, string? negative = null)
    {
        var vector = $"remote/{sample?.CodexCliVersion}/{sample?.CodexCliVersionError}/busy={busy}";
        await using var world = await World.CreateAsync(sample, busy);
        {
            var schema = world.Schema;
            var git = world.Git;
            var root = world.Root;
            var runtime = world.Recipient;
            var host = world.Host;
            var peer = world.Peer;
            var h = world.Harness;
            var freeze = world.Freeze;
            var launches = world.Launches;
            AgentTaskCreatedDto created;
            using (var scope = h.Provider.CreateScope())
                created = await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CreateAsync(
                    new(body, Title: body, Role: AgentTaskRole.Docs, AgentKind: AgentKind.Codex,
                        ModelLevel: level, Workspace: WorkspaceMode.Worktree, RunnerId: host.AllowedRunnerId),
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
            var queued = (await db.SessionQueuedMessages.AsNoTracking().SingleOrDefaultAsync(q => q.ExecutionTaskId == task.Id))
                .ShouldNotBeNull("C959-pc-186 durable handoff exists");
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
            if (negative == "write-failure")
                System.IO.Directory.CreateDirectory(spillPath); // Owned directory where the file must be written.
            Guid? observedRecipient = null;
            bool? observedFileExists = null;
            byte[]? observedFileBytes = null;
            runtime.BeforeBody = async (id, input) =>
            {
                if (!input.Contains(DelegationReportFormatter.TaskMarker(task.Id), StringComparison.Ordinal)) return;
                if (observedRecipient is not null) return;
                observedRecipient = id;
                observedFileExists = File.Exists(spillPath);
                if (observedFileExists.Value) observedFileBytes = await File.ReadAllBytesAsync(spillPath);
            };
            var withheld = new List<(Guid Session, string ActualSubmitted)>();
            if (negative is not null and not "write-failure")
                runtime.RecordPrompt = async (id, submitted) =>
                {
                    withheld.Add((id, submitted));
                    if (negative == "clipped") runtime.Append(id, TranscriptKinds.UserPrompt, submitted[..200]);
                    if (negative is "other-session" or "baseline")
                    {
                        await using var adversarial = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
                        var attempted = await adversarial.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == queued.Id);
                        var floor = attempted.LastDeliveryBaselineSequence.ShouldNotBeNull("C959-negative-original-floor");
                        if (negative == "other-session")
                            adversarial.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = h.SessionId,
                                Sequence = floor + 100, Kind = TranscriptKinds.UserPrompt, Text = submitted,
                                Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
                        else
                        {
                            var atFloor = await adversarial.TranscriptEntries.SingleAsync(e => e.AgentSessionId == id && e.Sequence == floor);
                            atFloor.Kind = TranscriptKinds.UserPrompt;
                            atFloor.Text = submitted;
                        }
                        await adversarial.SaveChangesAsync();
                    }
                };
            launches.Release(h.Provider.GetRequiredService<AgentSessionLaunchQueue>());
            await h.Provider.GetRequiredService<AgentSessionLaunchQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            var terminal = runtime.Terminals[task.AgentSessionId!.Value];
            if (busy)
            {
                terminal.SubmittedBodies.ShouldBeEmpty("C959-pc-188 remote " + vector);
                (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == queued.Id)).Status.ShouldBe(QueuedMessageStatus.Pending);
                runtime.Append(task.AgentSessionId.Value, TranscriptKinds.TurnEnd, stopReason: "end_turn");
                await h.Runtime.CatchUpTranscriptAsync(task.AgentSessionId.Value, CancellationToken.None);
                await h.Queue.FlushSessionAsync(task.AgentSessionId.Value, CancellationToken.None);
            }
            if (negative == "write-failure")
            {
                terminal.SubmittedBodies.ShouldBeEmpty("C959-pc-217 failed writer allows zero pointer submission");
                observedRecipient.ShouldBeNull("C959-pc-217 failed writer allows zero pointer input");
                var retained = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == queued.Id);
                retained.RemoteSpillBody.ShouldBe(expectedFull, "C959-write-failure-durable-E");
                retained.RemoteSpillRelativePath.ShouldBe(owned, "C959-write-failure-original-owned-path");
                (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == task.AgentSessionId
                    && e.Kind == TranscriptKinds.UserPrompt && e.Text == expectedWire))
                    .ShouldBe(0, "C959-write-failure-zero-receipt");
                System.IO.Directory.Delete(spillPath); // Remove only the empty, task-owned fault directory.
                await h.Queue.FlushSessionAsync(task.AgentSessionId!.Value, CancellationToken.None);
            }
            if (negative is not null and not "write-failure")
            {
                var label = negative switch { "clipped" => "C959-pc-222", "baseline" => "C959-pc-223", "other-session" => "C959-pc-224", _ => "C959-observable-ack-only" };
                var retained = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == queued.Id);
                retained.RemoteSpillBody.ShouldBe(expectedFull, label + " retain durable E without a complete recipient receipt");
                retained.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.LateConfirmed, label);
                var baseline = retained.LastDeliveryBaselineSequence.ShouldNotBeNull("C959-negative-observable-baseline");
                (await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == task.AgentSessionId
                    && e.Kind == TranscriptKinds.UserPrompt && e.Sequence > baseline && e.Text == expectedWire))
                    .ShouldBe(0, label + " no qualifying receipt");
                withheld.ShouldHaveSingleItem(label + " exactly one actual input");
                // Delayed ingestion publishes what the recipient actually submitted, never the oracle.
                runtime.RecordPrompt = null;
                foreach (var received in withheld)
                    runtime.Append(received.Session, TranscriptKinds.UserPrompt, received.ActualSubmitted);
                runtime.Append(task.AgentSessionId!.Value, TranscriptKinds.TurnEnd, stopReason: "end_turn");
                await h.Runtime.CatchUpTranscriptAsync(task.AgentSessionId.Value, CancellationToken.None);
                await h.Queue.FlushSessionAsync(task.AgentSessionId.Value, CancellationToken.None);
                terminal.SubmittedBodies.Count.ShouldBe(1, label + " late-confirm without another submit");
            }
            observedRecipient.ShouldBe(task.AgentSessionId, "C959-v21-remote-recipient-identity " + vector);
            observedFileExists.ShouldBe(true, "C959-pc-217 " + vector);
            observedFileBytes.ShouldBe(Encoding.UTF8.GetBytes(expectedFull), "C959-pc-211 " + vector);
            terminal.SubmittedBodies.Single().ShouldBe(expectedWire, "C959-v21-remote-W " + vector);
            expectedWire.ShouldNotContain("\n", customMessage: "C959-pc-212 remote " + vector);
            expectedWire.ShouldNotContain("\r");
            runtime.ProbeRequests.ShouldBeEmpty("C959-v21-zero-version-operations " + vector);
            var args = terminal.StartedArgs.ToList();
            args.Count(a => a == "--model").ShouldBe(1, "C959-pc-184 remote " + vector);
            args[args.IndexOf("--model") + 1].ShouldBe(level switch {
                AgentModelLevel.Frontier => "gpt-6-astra", AgentModelLevel.Low => "gpt-5.6-luna", _ => "gpt-6.1-sol",
            }, "C959-remote-selected-model");
            await h.Runtime.CatchUpTranscriptAsync(task.AgentSessionId.Value, CancellationToken.None);
            peer.RequestCount(PhoneHomeOperation.Transcript).ShouldBeGreaterThan(0,
                "C959-v21-actual-remote-transcript-pull " + vector);
            queued = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == queued.Id);
            var originalBaseline = queued.LastDeliveryBaselineSequence ?? 0;
            var receipts = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == task.AgentSessionId
                && e.Kind == TranscriptKinds.UserPrompt && e.Text == expectedWire && e.Sequence > originalBaseline).ToListAsync();
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

    }

    internal sealed class HeldLaunches : IAgentTaskLaunchSink
    {
        private readonly List<(Guid Session, Guid Agent, DateTime Generation, AgentLaunchSpec Spec)> _items = [];
        public void Enqueue(Guid sessionId, Guid agentId, DateTime acceptedGeneration, AgentLaunchSpec spec) => _items.Add((sessionId, agentId, acceptedGeneration, spec));
        public void Discard() => _items.Clear();
        public void Release(AgentSessionLaunchQueue queue)
        {
            _items.ShouldHaveSingleItem("C959-v21-one-real-launch");
            foreach (var item in _items) queue.EnqueueInteractiveSession(item.Session, item.Agent, item.Generation, item.Spec, remoteControlName: null, notes: null);
            _items.Clear();
        }
    }

    internal sealed class Freeze(string connection) : LandDeliveryBoundary
    {
        public DelegationSettings Settings { get; set; } = new();
        public Dictionary<Guid, AgentTask> Tasks { get; } = [];
        public Dictionary<Guid, string> Full { get; } = [];
        public override async Task ReachedAsync(string boundary, Guid taskId, Guid identity, CancellationToken ct)
        {
            if (boundary != "dispatch-warning-claim-committed") return;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connection));
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId, ct);
            Tasks[taskId] = task;
            Full[taskId] = DelegationReportFormatter.BuildBrief(task, Settings,
                Settings.CeilingsFor(PtyBackend.InboxConhost, "runner").ForAgentKind(task.AgentKind).ReplyInlineMaxChars, refocus: false);
        }
    }

    private sealed class RemoteFactory(ISessionRunnerDirectory directory, string runnerId, AgentRegistrySettings registry) : IAgentProtocolAdapterFactory, IAsyncDisposable
    {
        private readonly List<RunnerCodexAdapter> _created = [];
        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            var adapter = new RunnerCodexAdapter(new RunnerScopedSessionRunnerClient(directory, runnerId), Options.Create(registry));
            _created.Add(adapter);
            return adapter;
        }
        public async ValueTask DisposeAsync()
        {
            foreach (var adapter in _created)
            {
                await adapter.DisposeAsync();
                await adapter.Exited.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private sealed class BusyBus(MockEventBus inner, Recipient recipient, Func<AgentSessionRuntime> runtime) : IEventBus
    {
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => inner.PublishToAllAsync(name, payload, ct);
        public async Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default)
        {
            if (name == "SessionStarted")
            {
                var id = (Guid)payload.GetType().GetProperty("sessionId")!.GetValue(payload)!;
                recipient.Append(id, TranscriptKinds.TurnEnd, stopReason: "end_turn");
                recipient.Append(id, TranscriptKinds.AssistantText, "C959 remote activity after TurnEnd");
                await runtime().CatchUpTranscriptAsync(id, ct);
            }
            await inner.PublishToGroupAsync(group, name, payload, ct);
        }
    }

    internal sealed class Recipient(RunnerCodexCliVersionDto? sample) : IPhoneHomeRuntimeSurface, IAsyncDisposable
    {
        public AgentKind Kind { get; init; } = AgentKind.Codex;
        public bool Ready { get; set; } = true;
        public bool Ack { get; set; } = true;
        public bool HoldTurnEnd { get; set; }
        public int SnapshotReads { get; private set; }
        private readonly Dictionary<Guid, GrokRulesReceipt> _rules = [];
        private readonly List<(Guid Session, string Text)> _heldAcks = [];
        public void ReleaseAcks()
        {
            Ack = true;
            foreach (var (id, text) in _heldAcks) { Append(id, TranscriptKinds.AssistantText, text); Append(id, TranscriptKinds.TurnEnd, stopReason: "end_turn"); }
            _heldAcks.Clear();
        }
        public Dictionary<Guid, FakeAgentProtocolAdapter> Terminals { get; } = [];
        private readonly Dictionary<Guid, List<RunnerTranscriptEvent>> _transcripts = [];
        public List<RunnerCodexCliProbeRequest> ProbeRequests { get; } = [];
        public Func<Guid, string, Task>? BeforeBody { get; set; }
        public Func<Guid, string, Task>? RecordPrompt { get; set; }
        public void Append(Guid id, string kind, string? text = null, string? stopReason = null, DateTimeOffset? timestamp = null, bool nullTimestamp = false, long? sequence = null)
        {
            var entries = _transcripts[id];
            entries.Add(new(id, sequence ?? (entries.LastOrDefault()?.Sequence ?? 0) + 1, kind, Guid.NewGuid().ToString("N"), null, nullTimestamp ? null : timestamp ?? DateTimeOffset.UtcNow,
                kind == TranscriptKinds.UserPrompt ? "user" : null, text, null, null, null, null, stopReason));
        }
        public RunnerCapabilitiesDto Capabilities() => new("InboxConhost", "inbox", "test", false,
            Version: "d40c1670", Platform: "linux", Features: [RunnerCapabilityFeatures.SessionGenerationV1, RunnerCapabilityFeatures.WorkspaceRepositoryV1, GrokRulesTransport.Capability],
            CodexCliVersion: sample?.CodexCliVersion, CodexCliVersionCheckedAtUtc: sample?.CodexCliVersionCheckedAtUtc,
            CodexCliVersionError: sample?.CodexCliVersionError, CodexCliLauncherFingerprint: sample?.CodexCliLauncherFingerprint);
        public Task<RunnerCodexCliVersionDto?> GetCodexCliVersionAsync(RunnerCodexCliProbeRequest request, CancellationToken ct)
        {
            ProbeRequests.Add(request);
            return Task.FromResult(sample);
        }
        public string Health() => "Healthy";
        public IReadOnlyList<RunnerSessionDto> List() => Terminals.Keys.Select(Session).ToList();
        private RunnerSessionDto Session(Guid id) => new(id, 1234, Terminals[id].StartedAcceptedGeneration!.Value,
            Terminals[id].Killed ? "Exited" : "Running", Terminals[id].Killed ? 0 : null, "Unknown", _transcripts[id].Count,
            GrokRulesReceipt: _rules.GetValueOrDefault(id), AcceptedStartedAt: Terminals[id].StartedAcceptedGeneration);
        public Task<RunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Terminals.ContainsKey(id) ? Session(id) : new RunnerSessionDto(id, null, DateTime.UtcNow, "Exited", 0, "Unknown", 0));
        public async Task<RunnerSessionDto> StartAsync(RunnerLaunchRequest request, CancellationToken ct)
        {
            var terminal = new FakeAgentProtocolAdapter();
            Terminals.Add(request.SessionId, terminal);
            _transcripts.Add(request.SessionId, []);
            if (request.GrokRulesPayload is { } payload)
            {
                var bytes = Encoding.UTF8.GetBytes(payload.Content);
                var path = Path.Combine(request.Cwd, "instructions", "grok", request.SessionId.ToString("N"), "rules.md");
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes, ct);
                _rules[request.SessionId] = new(path, GrokRulesTransport.Hash(bytes), bytes.Length, 1, payload.Generation);
            }
            terminal.OnSubmitted = async text =>
            {
                if (text.StartsWith("[antiphon-grok-rules:", StringComparison.Ordinal))
                {
                    Append(request.SessionId, TranscriptKinds.UserPrompt, text);
                    var receipt = _rules[request.SessionId];
                    var ack = $"ANTIPHON_RULES_ACK id={text[21..text.IndexOf(']')]} generation={receipt.Generation:N} sha256={receipt.Sha256}";
                    if (!Ack) { _heldAcks.Add((request.SessionId, ack)); return; }
                    Append(request.SessionId, TranscriptKinds.AssistantText, ack);
                }
                else if (RecordPrompt is { } record) await record(request.SessionId, text);
                else Append(request.SessionId, TranscriptKinds.UserPrompt, text);
                if (!HoldTurnEnd) Append(request.SessionId, TranscriptKinds.TurnEnd, stopReason: "end_turn");
            };
            await terminal.StartAsync(new AgentLaunchSpec(Kind.ToString(), Kind, request.Exe, request.Args, request.Env, request.Cwd,
                request.Cols, request.Rows,
                SessionId: request.SessionId, AcceptedStartedAt: request.AcceptedStartedAt), ct);
            return Session(request.SessionId);
        }
        public RunnerBufferDto GetBuffer(Guid id) => new(id, Terminals[id].SnapshotRawOutput(), _transcripts[id].Count);
        public RunnerSnapshotDto GetSnapshot(Guid id)
        {
            var terminal = Terminals[id];
            SnapshotReads++;
            string startup = Kind switch
            {
                AgentKind.Grok => Ready ? GrokStartupFixture.ReadyScreen() : GrokStartingScreen(),
                AgentKind.ClaudeCode => "Claude Code\n> \n  ⏵⏵ bypass permissions on (shift+tab to cycle)",
                _ => CodexStartupFixtures.P3,
            };
            var screen = terminal.Inputs.Count == 0 ? startup : terminal.SnapshotRenderedScreen();
            return new(id, terminal.SnapshotRawOutput(), screen,
                _transcripts[id].Count + terminal.Inputs.Count, terminal.StartedAcceptedGeneration!.Value, terminal.StartedAcceptedGeneration);
        }
        private static string GrokStartingScreen()
        {
            using var doc = GrokStartupFixture.Read();
            return GrokStartupFixture.Screen(GrokStartupFixture.Capture(doc, "startup-"), 15);
        }
        public RunnerTranscriptDto GetTranscript(Guid id) => new(id, _transcripts[id].ToArray(), _transcripts[id].LastOrDefault()?.Sequence ?? 0);
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
            if (!Terminals.ContainsKey(id)) return new(id, true, KillGenerationOutcomes.Killed, DateTime.UtcNow);
            var killed = await Terminals[id].KillGenerationAsync(expected, TimeSpan.FromSeconds(1), ct);
            return new(id, killed, killed ? KillGenerationOutcomes.Killed : KillGenerationOutcomes.Mismatch, DateTime.UtcNow);
        }
        public int OwnedSessionCount => Terminals.Values.Count(t => !t.Killed);
        public async Task StopAsync() { foreach (var terminal in Terminals.Values) await terminal.KillAsync(TimeSpan.FromSeconds(1), CancellationToken.None); }
        public async ValueTask DisposeAsync() => await StopAsync();
    }
}
