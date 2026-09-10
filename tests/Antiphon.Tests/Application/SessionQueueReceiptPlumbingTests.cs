using System.Runtime.InteropServices;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("Headed")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class SessionQueueReceiptPlumbingTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static string FakeClaudeExe => Path.Combine(AppContext.BaseDirectory, "fakeclaude", "fakeclaude.exe");

    [Test]
    [Arguments("insert-fails")]
    [Arguments("pending-before-flush")]
    [Arguments("attempt-before-write")]
    [Arguments("body-before-enter")]
    [Arguments("recipient-before-ingestion")]
    [Arguments("receipt-before-verdict")]
    public async Task C475_QueueCommitAndTransportRecovery(string cut)
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe)) throw new SkipTestException("fakeclaude missing");
        await using var world = await PtyWorld.StartAsync();
        const string body = "CARD-0475 complete recipient body for ";
        var text = body + cut;
        if (cut == "insert-fails")
        {
            world.Fault.FailNextInsert = true;
            await Should.ThrowAsync<InvalidOperationException>(() =>
                world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None));
            (await world.RowsAsync()).ShouldBeEmpty();
            world.Forward.Writes.ShouldBeEmpty();
            SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath).ShouldBeEmpty();
            (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(world.SessionId, 0)).ShouldBeEmpty();
            world.Fault.FailNextInsert = false;
            world.RecreateQueue();
            await world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None);
        }
        else if (cut == "pending-before-flush")
        {
            await world.AddActivityAsync(TranscriptKinds.AssistantText);
            await world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None);
            var pending = (await world.RowsAsync()).ShouldHaveSingleItem();
            pending.Status.ShouldBe(QueuedMessageStatus.Pending);
            pending.DeliveryAttempts.ShouldBe(0);
            world.Forward.Writes.ShouldBeEmpty();
            world.RecreateQueue();
            await world.AddActivityAsync(TranscriptKinds.TurnEnd);
            await world.Queue.FlushSessionAsync(world.SessionId, CancellationToken.None);
            (await world.RowsAsync()).ShouldHaveSingleItem().Id.ShouldBe(pending.Id);
        }
        else if (cut == "attempt-before-write")
        {
            world.Forward.BlockWrites = true;
            var attempt = world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None);
            try
            {
                await world.Forward.WriteReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                var claimed = (await world.RowsAsync()).ShouldHaveSingleItem();
                claimed.Status.ShouldBe(QueuedMessageStatus.Sent);
                claimed.DeliveryAttempts.ShouldBe(1);
                claimed.LastDeliveryBaselineSequence.ShouldNotBeNull();
                world.Forward.Writes.ShouldBeEmpty();
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(world.SessionId, 0)).ShouldBeEmpty();
            }
            finally
            {
                world.Forward.BlockWrites = false;
                world.Forward.ReleaseWrites();
                await attempt;
            }
        }
        else
        {
            if (cut == "recipient-before-ingestion") await world.StopPumpAsync();
            if (cut == "receipt-before-verdict") world.Fault.FailVerdict = true;
            else
            {
                // A process cut leaves the already committed attempt intact. Refuse the
                // graceful exception handler's revert as well as the selected transport cut.
                world.Fault.FailRevert = true;
                world.Forward.CutAtEnter = cut == "body-before-enter" ? "before" : "after";
            }
            await Should.ThrowAsync<InvalidOperationException>(() =>
                world.Queue.EnqueueAsync(world.SessionId, text, MessageSendMode.WhenIdle, CancellationToken.None));
            var interrupted = (await world.RowsAsync()).ShouldHaveSingleItem();
            interrupted.Status.ShouldBe(QueuedMessageStatus.Sent);
            interrupted.DeliveryVerdict.ShouldBeNull();
            interrupted.DeliveryAttempts.ShouldBe(1);
            interrupted.LastDeliveryBaselineSequence.ShouldNotBeNull();
            if (cut == "body-before-enter")
            {
                (await world.Client.GetSnapshotAsync(world.SessionId, CancellationToken.None)).RawOutput.ShouldContain(text);
                SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath).ShouldBeEmpty();
            }
            else
            {
                await WaitUntilAsync(() => Task.FromResult(SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath).Contains(text)));
                if (cut == "recipient-before-ingestion")
                {
                    (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(world.SessionId, 0)).ShouldBeEmpty();
                    world.StartPump();
                }
                await world.WaitForReceiptAsync(text);
            }
            var writes = world.Forward.Writes.Count;
            world.Fault.FailVerdict = false;
            world.Fault.FailRevert = false;
            world.Forward.CutAtEnter = null;
            world.Clock.Offset = TimeSpan.FromMinutes(2);
            world.RecreateQueue();
            await world.Queue.FlushSessionAsync(world.SessionId, CancellationToken.None);
            var recovered = (await world.RowsAsync()).ShouldHaveSingleItem();
            recovered.Id.ShouldBe(interrupted.Id);
            recovered.DeliveryAttempts.ShouldBe(1);
            recovered.LastDeliveryBaselineSequence.ShouldBe(interrupted.LastDeliveryBaselineSequence);
            recovered.DeliveryVerdict.ShouldBe(cut == "body-before-enter" ? DeliveryVerdict.Delivered : DeliveryVerdict.LateConfirmed);
            world.Forward.Writes.Skip(writes).ShouldBe(cut == "body-before-enter" ? new[] { "\r" } : Array.Empty<string>());
        }
        await world.WaitForReceiptAsync(text);
        SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath).ShouldBe([text]);
        (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(world.SessionId, 0)).ShouldBe([text]);
        var delivered = (await world.RowsAsync()).ShouldHaveSingleItem();
        delivered.Status.ShouldBe(QueuedMessageStatus.Sent);
        delivered.DeliveryVerdict.ShouldNotBeNull();
        Console.WriteLine("C475_RECEIPT:" + System.Text.Json.JsonSerializer.Serialize(new
        {
            cut, world.SessionId, delivered.Id, delivered.DeliveryAttempts, delivered.LastDeliveryBaselineSequence,
            delivered.DeliveryVerdict, body = text, inputs = world.Forward.Payloads,
            file = SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath),
            destination = await SessionQueueTranscriptPump.DestinationUserPromptsAsync(world.SessionId, 0),
        }));
    }

    [Test]
    public async Task C475_AlreadyIdleWhenIdleHasRecipientReceipt()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe)) throw new SkipTestException("fakeclaude missing");
        await using var world = await PtyWorld.StartAsync();
        var dto = await world.Queue.EnqueueAsync(world.SessionId, "already idle", MessageSendMode.WhenIdle, CancellationToken.None);
        dto.Messages.ShouldBeEmpty();
        SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath).ShouldBe(["already idle"]);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == world.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    [Test]
    [Arguments("partial-line")]
    [Arguments("read-fails")]
    [Arguments("save-fails")]
    [Arguments("restart-after-commit")]
    [Arguments("seeded-sequence")]
    public async Task C475_PumpPersistsCompleteLinesOnce(string cut)
    {
        var sessionId = Guid.NewGuid();
        var other = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"c475-pump-{Guid.NewGuid():N}.jsonl");
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "x", AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running, Cwd = ".", Cols = 80, Rows = 24, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            db.AgentSessions.Add(new AgentSession { Id = other, DefinitionName = "y", AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running, Cwd = ".", Cols = 80, Rows = 24, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            if (cut == "seeded-sequence")
                db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 9, Kind = "TurnEnd", CreatedAt = now });
            await db.SaveChangesAsync();
        }
        using var cts = new CancellationTokenSource();
        var uuid = Guid.NewGuid().ToString("N");
        var line = "{\"type\":\"user\",\"uuid\":\"" + uuid + "\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"pump body\"}]}}\n";
        Task pumping = Task.CompletedTask;
        Task pumping2 = Task.CompletedTask;
        using var cts2 = new CancellationTokenSource();
        try
        {
            if (cut == "partial-line")
            {
                await File.WriteAllTextAsync(path, line.TrimEnd('\n'));
                pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token);
                await Task.Delay(250);
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).ShouldBeEmpty();
                await File.AppendAllTextAsync(path, "\n");
                await WaitUntilAsync(async () => (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).Count == 1);
                cts.Cancel();
                await pumping;
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).ShouldBe(["pump body"]);
            }
            else if (cut == "read-fails")
            {
                var reads = 0;
                pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token, beforeRead: () =>
                {
                    reads++;
                    if (reads == 1) throw new IOException("injected read");
                    return Task.CompletedTask;
                });
                await File.WriteAllTextAsync(path, line);
                await WaitUntilAsync(async () => (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).Count == 1);
                cts.Cancel();
                await pumping;
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).ShouldBe(["pump body"]);
            }
            else if (cut == "save-fails")
            {
                var saves = 0;
                pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token, beforeSave: () =>
                {
                    saves++;
                    if (saves == 1) throw new IOException("injected save");
                    return Task.CompletedTask;
                });
                await File.WriteAllTextAsync(path, line);
                await WaitUntilAsync(async () => (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).Count == 1);
                cts.Cancel();
                await pumping;
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).ShouldBe(["pump body"]);
            }
            else if (cut == "restart-after-commit")
            {
                await File.WriteAllTextAsync(path, line);
                pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token);
                await WaitUntilAsync(async () => (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).Count == 1);
                cts.Cancel();
                await pumping;
                pumping2 = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts2.Token);
                await Task.Delay(300);
                cts2.Cancel();
                await pumping2;
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).Count.ShouldBe(1);
            }
            else
            {
                await File.WriteAllTextAsync(path, line);
                pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token);
                await WaitUntilAsync(async () => (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).Count == 1);
                cts.Cancel();
                await pumping;
                await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
                var seq = await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt)
                    .Select(t => t.Sequence).ToListAsync();
                seq.ShouldAllBe(s => s > 9);
                (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == other)).ShouldBe(0);
            }
        }
        finally
        {
            cts.Cancel();
            cts2.Cancel();
            await Task.WhenAll(pumping, pumping2);
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId || t.AgentSessionId == other).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == sessionId || s.Id == other).ExecuteDeleteAsync();
            try { File.Delete(path); } catch { }
        }
    }

    [Test]
    public async Task C475_PumpIsJoinedBeforeFixtureDisposal()
    {
        var sessionId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"c475-join-{Guid.NewGuid():N}.jsonl");
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            var now = DateTime.UtcNow;
            db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "x", AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running, Cwd = ".", Cols = 80, Rows = 24, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            await db.SaveChangesAsync();
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token,
            beforeRead: () => { entered.TrySetResult(); return Task.CompletedTask; }, hold: hold.Task);
        Task cleanup = Task.CompletedTask;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cleanup = CleanupAsync();
            cleanup.IsCompleted.ShouldBeFalse();
            await using var check = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            (await check.AgentSessions.AnyAsync(s => s.Id == sessionId)).ShouldBeTrue();
        }
        finally
        {
            hold.TrySetResult();
            cts.Cancel();
            await pumping;
            await cleanup;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            File.Delete(path);
        }

        async Task CleanupAsync()
        {
            cts.Cancel();
            await pumping;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task C475_ProactiveAndReactiveRecoveryShareOneEscBudget()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            ConfigureDeliveryVerification = v => v.OverlayRecoveryEnabled = true,
        });
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
            session.AgentKind = AgentKind.Grok;
            await db.SaveChangesAsync();
        }
        h.Adapter.OverlayOpen = true;
        h.Adapter.EchoTypedInputToScreen = false;
        await h.Queue.EnqueueAsync(h.SessionId, "one esc", MessageSendMode.WhenIdle, CancellationToken.None);
        h.Adapter.Inputs.Count(i => i == "\u001b").ShouldBe(1);
        h.Adapter.Inputs.ShouldNotContain("\r");
        (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(h.SessionId, 0)).ShouldBeEmpty();
    }

    [Test]
    public async Task C475_MultilineWritesKeepPasteMarkers()
    {
        if (!IsWindows) throw new SkipTestException("ConPTY only on Windows");
        if (!File.Exists(FakeClaudeExe)) throw new SkipTestException("fakeclaude missing");
        await using var world = await PtyWorld.StartAsync();
        const string body = "line one\nline two";
        await world.Queue.EnqueueAsync(world.SessionId, body, MessageSendMode.Now, CancellationToken.None);
        world.Forward.Payloads.ShouldContain(p => p.Contains("\u001b[200~") && p.Contains("line one\nline two") && p.Contains("\u001b[201~"));
        world.Forward.Payloads.ShouldContain("\r");
        SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath).ShouldBe([body.Replace("\r\n", "\n")]);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!await condition()) await Task.Delay(50, deadline.Token);
    }

    private sealed class OffsetClock : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }

    private sealed class PtyWorld : IAsyncDisposable
    {
        public required DirectSessionRunnerClient Client { get; init; }
        public required Guid SessionId { get; init; }
        public required string TranscriptPath { get; init; }
        public required string Cwd { get; init; }
        public required SessionMessageQueueService Queue { get; set; }
        public required ForwardingClient Forward { get; init; }
        public required InsertFault Fault { get; init; }
        public required OffsetClock Clock { get; init; }
        private CancellationTokenSource _pump = new();
        private Task _pumping = Task.CompletedTask;
        private ServiceProvider _provider = null!;

        public static async Task<PtyWorld> StartAsync()
        {
            var sessionId = Guid.NewGuid();
            var cwd = Path.Combine(Path.GetTempPath(), $"c475-plumb-{sessionId:N}");
            Directory.CreateDirectory(cwd);
            var transcript = Path.Combine(Path.GetTempPath(), $"c475-plumb-{sessionId:N}.jsonl");
            var client = new DirectSessionRunnerClient(Path.Combine(Path.GetTempPath(), $"c475-plumb-log-{sessionId:N}"), ptyBackend: "inbox");
            var inner = client;
            var forward = new ForwardingClient(inner);
            var fault = new InsertFault();
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o =>
            {
                o.UseNpgsql(TestDbFixture.ConnectionString, n => { n.MigrationsAssembly("Antiphon.Server"); n.SetPostgresVersion(16, 0); });
                o.AddInterceptors(fault);
            });
            var bus = new MockEventBus();
            services.AddSingleton<IEventBus>(bus);
            var clock = new OffsetClock();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings()));
            services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { Enabled = true, PollIntervalMs = 50, EvidenceTimeoutSeconds = 8 },
            }));
            services.AddSingleton<ISessionRunnerClient>(forward);
            services.AddSingleton<AgentSessionRuntime>();
            services.AddTransient<SessionMessageQueueService>();
            services.AddLogging();
            var provider = services.BuildServiceProvider();
            await inner.StartAsync(sessionId, new AgentLaunchSpec("fakeclaude", AgentKind.ClaudeCode, FakeClaudeExe, [],
                new Dictionary<string, string> { ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = transcript }, cwd, 120, 30), CancellationToken.None);
            await using (var scope = provider.CreateAsyncScope())
            {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "fakeclaude", AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running, Cwd = cwd, Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now });
                db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 1, Kind = Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd, StopReason = "end_turn", CreatedAt = now });
                await db.SaveChangesAsync();
            }
            var world = new PtyWorld
            {
                Client = inner, SessionId = sessionId, TranscriptPath = transcript, Cwd = cwd,
                Queue = provider.GetRequiredService<SessionMessageQueueService>(), Forward = forward, Fault = fault, Clock = clock,
            };
            world._provider = provider;
            await WaitUntilAsync(async () => (await inner.GetSnapshotAsync(sessionId, CancellationToken.None)).RawOutput.Contains("Fake Claude ready"));
            world.StartPump();
            return world;
        }

        public void RecreateQueue() => Queue = _provider.GetRequiredService<SessionMessageQueueService>();

        public void StartPump()
        {
            _pump.Dispose();
            _pump = new CancellationTokenSource();
            _pumping = SessionQueueTranscriptPump.RunAsync(TranscriptPath, SessionId, _pump.Token);
        }

        public async Task StopPumpAsync()
        {
            _pump.Cancel();
            await _pumping;
        }

        public async Task<List<SessionQueuedMessage>> RowsAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            return await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == SessionId).ToListAsync();
        }

        public async Task AddActivityAsync(string kind)
        {
            await StopPumpAsync();
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var seq = await SessionQueueTranscriptPump.MaxSequenceAsync(SessionId);
            db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = SessionId,
                Sequence = seq + 1, Kind = kind, Text = kind == TranscriptKinds.AssistantText ? "working" : null,
                StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            StartPump();
        }

        public Task WaitForReceiptAsync(string text) => WaitUntilAsync(async () =>
        {
            if (_pumping.IsFaulted) await _pumping;
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            return await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text == text)
                && await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == SessionId && t.Sequence > 1 && t.Kind == TranscriptKinds.TurnEnd);
        });

        public async ValueTask DisposeAsync()
        {
            await StopPumpAsync();
            _pump.Dispose();
            try { await Client.KillAsync(SessionId, CancellationToken.None); } catch { }
            await Client.DisposeAsync();
            await _provider.DisposeAsync();
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                await db.SessionQueuedMessages.Where(m => m.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
            }
            try { Directory.Delete(Cwd, true); } catch { }
            try { File.Delete(TranscriptPath); } catch { }
        }
    }

    private sealed class ForwardingClient(ISessionRunnerClient inner) : ISessionRunnerClient
    {
        public List<string> Writes { get; } = [];
        public List<string> Payloads { get; } = [];
        public bool BlockWrites { get; set; }
        public string? CutAtEnter { get; set; }
        public TaskCompletionSource WriteReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _writeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseWrites() { _writeGate.TrySetResult(); _writeGate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) => inner.StartAsync(id, spec, ct);
        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) => inner.GetCapabilitiesAsync(ct);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => inner.GetAsync(id, ct);
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct) => inner.GetBufferAsync(id, ct);
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct) => inner.GetSnapshotAsync(id, ct);
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) => inner.GetTranscriptAsync(id, ct);
        public async Task SendInputAsync(Guid id, string input, CancellationToken ct)
        {
            if (BlockWrites)
            {
                WriteReached.TrySetResult();
                await _writeGate.Task.WaitAsync(ct);
            }
            if (CutAtEnter == "before" && input == "\r") throw new InvalidOperationException("injected cut before Enter");
            Writes.Add(input);
            Payloads.Add(input);
            await inner.SendInputAsync(id, input, ct);
            if (CutAtEnter == "after" && input == "\r") throw new InvalidOperationException("injected cut after Enter");
        }
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => inner.ClearLiveBufferAsync(id, ct);
        public Task ResizeAsync(Guid id, int c, int r, CancellationToken ct) => inner.ResizeAsync(id, c, r, ct);
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) => inner.KillAsync(id, ct);
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => inner.StreamEventsAsync(ct);
    }

    private sealed class InsertFault : SaveChangesInterceptor
    {
        public bool FailNextInsert { get; set; }
        public bool FailVerdict { get; set; }
        public bool FailRevert { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (FailRevert && data.Context!.ChangeTracker.Entries<SessionQueuedMessage>()
                .Any(e => e.State == EntityState.Modified && e.Entity.Status == QueuedMessageStatus.Pending))
                throw new InvalidOperationException("injected process cut prevents revert");
            if (FailNextInsert && data.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("injected insert failure");
            if (FailVerdict && data.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.DeliveryVerdict is not null))
                throw new InvalidOperationException("injected verdict failure");
            return ValueTask.FromResult(result);
        }
    }
}
