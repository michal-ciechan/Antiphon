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
        switch (cut)
        {
            case "insert-fails":
            {
                world.Fault.FailNextInsert = true;
                await Should.ThrowAsync<Exception>(() =>
                    world.Queue.EnqueueAsync(world.SessionId, "never inserted", MessageSendMode.WhenIdle, CancellationToken.None));
                await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
                (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == world.SessionId)).ShouldBe(0);
                world.Forward.Writes.ShouldBeEmpty();
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(world.SessionId, 0)).ShouldBeEmpty();
                world.Fault.FailNextInsert = false;
                await world.Queue.EnqueueAsync(world.SessionId, "after insert", MessageSendMode.WhenIdle, CancellationToken.None);
                SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath).ShouldContain("after insert");
                break;
            }
            case "pending-before-flush":
            {
                await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
                    db.TranscriptEntries.Add(new TranscriptEntry
                    {
                        Id = Guid.NewGuid(), AgentSessionId = world.SessionId, Sequence = 1,
                        Kind = Antiphon.SessionRunner.Contracts.TranscriptKinds.AssistantText,
                        Text = "working", CreatedAt = DateTime.UtcNow,
                    });
                await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
                {
                    db.TranscriptEntries.Add(new TranscriptEntry
                    {
                        Id = Guid.NewGuid(), AgentSessionId = world.SessionId, Sequence = 1,
                        Kind = Antiphon.SessionRunner.Contracts.TranscriptKinds.AssistantText,
                        Text = "working", CreatedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync();
                }
                await world.Queue.EnqueueAsync(world.SessionId, "held while busy", MessageSendMode.WhenIdle, CancellationToken.None);
                await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
                {
                    var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == world.SessionId);
                    row.Status.ShouldBe(QueuedMessageStatus.Pending);
                }
                world.Forward.Writes.ShouldBeEmpty();
                await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
                {
                    db.TranscriptEntries.Add(new TranscriptEntry
                    {
                        Id = Guid.NewGuid(), AgentSessionId = world.SessionId, Sequence = 2,
                        Kind = Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd,
                        StopReason = "end_turn", CreatedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync();
                }
                await world.Queue.OnTurnEndAsync(world.SessionId, CancellationToken.None);
                SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath).ShouldContain("held while busy");
                break;
            }
            case "attempt-before-write":
                world.Forward.BlockWrites = true;
                var attempt = world.Queue.EnqueueAsync(world.SessionId, "blocked write", MessageSendMode.WhenIdle, CancellationToken.None);
                await Task.Delay(300);
                world.Forward.BlockWrites = false;
                world.Forward.ReleaseWrites();
                await attempt;
                await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
                {
                    var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == world.SessionId);
                    row.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(1);
                    row.LastDeliveryBaselineSequence.ShouldNotBeNull();
                }
                break;
            case "body-before-enter":
                world.Forward.WithholdEnter = true;
                await Should.ThrowAsync<Exception>(() =>
                    world.Queue.EnqueueAsync(world.SessionId, "body stays", MessageSendMode.Now, CancellationToken.None));
                (await world.Client.GetSnapshotAsync(world.SessionId, CancellationToken.None)).RawOutput
                    .ShouldContain("body stays");
                world.Forward.WithholdEnter = false;
                await world.Queue.EnqueueAsync(world.SessionId, "body stays", MessageSendMode.Now, CancellationToken.None);
                SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath).Count(p => p == "body stays").ShouldBe(1);
                break;
            case "recipient-before-ingestion":
                world.PumpHold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await world.Queue.EnqueueAsync(world.SessionId, "ingest later", MessageSendMode.Now, CancellationToken.None);
                world.PumpHold.SetResult();
                await Task.Delay(400);
                SessionQueueTranscriptPump.FileUserPrompts(world.TranscriptPath).ShouldContain("ingest later");
                break;
            case "receipt-before-verdict":
                world.Fault.FailVerdict = true;
                try
                {
                    await world.Queue.EnqueueAsync(world.SessionId, "late verdict", MessageSendMode.WhenIdle, CancellationToken.None);
                }
                catch { /* injected */ }
                world.Fault.FailVerdict = false;
                await world.Queue.EnqueueAsync(world.SessionId, "late verdict", MessageSendMode.WhenIdle, CancellationToken.None);
                await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
                {
                    var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == world.SessionId);
                    row.Status.ShouldBe(QueuedMessageStatus.Sent);
                }
                break;
        }
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
        try
        {
            if (cut == "partial-line")
            {
                await File.WriteAllTextAsync(path, line.TrimEnd('\n'));
                var pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token);
                await Task.Delay(250);
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).ShouldBeEmpty();
                await File.AppendAllTextAsync(path, "\n");
                await Task.Delay(300);
                cts.Cancel();
                await pumping;
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).ShouldBe(["pump body"]);
            }
            else if (cut == "read-fails")
            {
                var reads = 0;
                var pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token, beforeRead: () =>
                {
                    reads++;
                    if (reads == 1) throw new IOException("injected read");
                    return Task.CompletedTask;
                });
                await File.WriteAllTextAsync(path, line);
                await Task.Delay(400);
                cts.Cancel();
                await pumping;
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).ShouldBe(["pump body"]);
            }
            else if (cut == "save-fails")
            {
                var saves = 0;
                var pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token, beforeSave: () =>
                {
                    saves++;
                    if (saves == 1) throw new InvalidOperationException("injected save");
                    return Task.CompletedTask;
                });
                await File.WriteAllTextAsync(path, line);
                await Task.Delay(500);
                cts.Cancel();
                await pumping;
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).ShouldBe(["pump body"]);
            }
            else if (cut == "restart-after-commit")
            {
                await File.WriteAllTextAsync(path, line);
                var pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token);
                await Task.Delay(300);
                cts.Cancel();
                await pumping;
                using var cts2 = new CancellationTokenSource();
                var pumping2 = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts2.Token);
                await Task.Delay(300);
                cts2.Cancel();
                await pumping2;
                (await SessionQueueTranscriptPump.DestinationUserPromptsAsync(sessionId, 0)).Count.ShouldBe(1);
            }
            else
            {
                await File.WriteAllTextAsync(path, line);
                var pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token);
                await Task.Delay(300);
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
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var pumping = SessionQueueTranscriptPump.RunAsync(path, sessionId, cts.Token, hold: hold.Task);
        var cleanup = Task.Run(async () =>
        {
            cts.Cancel();
            await pumping;
        });
        await Task.Delay(150);
        cleanup.IsCompleted.ShouldBeFalse();
        hold.SetResult();
        await cleanup;
        cleanup.IsCompleted.ShouldBeTrue();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
        {
            await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
        }
        try { File.Delete(path); } catch { }
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
        SessionQueueTranscriptPump.FileUserPrompts("missing").ShouldBeEmpty();
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

    private sealed class PtyWorld : IAsyncDisposable
    {
        public required DirectSessionRunnerClient Client { get; init; }
        public required Guid SessionId { get; init; }
        public required string TranscriptPath { get; init; }
        public required string Cwd { get; init; }
        public required SessionMessageQueueService Queue { get; init; }
        public required ForwardingClient Forward { get; init; }
        public required InsertFault Fault { get; init; }
        public TaskCompletionSource? PumpHold { get; set; }
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
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IOptions<AgentSessionSettings>>(Options.Create(new AgentSessionSettings()));
            services.AddSingleton<IOptions<SupervisionSettings>>(Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { Enabled = true, PollIntervalMs = 50, EvidenceTimeoutSeconds = 8 },
            }));
            services.AddSingleton<ISessionRunnerClient>(forward);
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddLogging();
            var provider = services.BuildServiceProvider();
            await inner.StartAsync(sessionId, new AgentLaunchSpec("fakeclaude", AgentKind.ClaudeCode, FakeClaudeExe, [],
                new Dictionary<string, string> { ["ANTIPHON_FAKE_TRANSCRIPT_PATH"] = transcript }, cwd, 120, 30), CancellationToken.None);
            await using (var db = provider.GetRequiredService<AppDbContext>())
            {
                var now = DateTime.UtcNow;
                db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "fakeclaude", AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running, Cwd = cwd, Cols = 120, Rows = 30, CreatedAt = now, StartedAt = now, LastSeenAt = now });
                db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 1, Kind = Antiphon.SessionRunner.Contracts.TranscriptKinds.TurnEnd, StopReason = "end_turn", CreatedAt = now });
                await db.SaveChangesAsync();
            }
            var world = new PtyWorld
            {
                Client = inner, SessionId = sessionId, TranscriptPath = transcript, Cwd = cwd,
                Queue = provider.GetRequiredService<SessionMessageQueueService>(), Forward = forward, Fault = fault,
            };
            world._provider = provider;
            world._pumping = SessionQueueTranscriptPump.RunAsync(transcript, sessionId, world._pump.Token,
                hold: world.PumpHold?.Task);
            return world;
        }

        public async ValueTask DisposeAsync()
        {
            _pump.Cancel();
            try { await _pumping; } catch { }
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
        public bool WithholdEnter { get; set; }
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
            if (BlockWrites) await _writeGate.Task;
            if (WithholdEnter && input == "\r") return;
            Writes.Add(input);
            Payloads.Add(input);
            await inner.SendInputAsync(id, input, ct);
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
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (FailNextInsert && data.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("injected insert failure");
            if (FailVerdict && data.Context!.ChangeTracker.Entries<SessionQueuedMessage>().Any(e => e.Entity.DeliveryVerdict is not null))
                throw new InvalidOperationException("injected verdict failure");
            return ValueTask.FromResult(result);
        }
    }
}
