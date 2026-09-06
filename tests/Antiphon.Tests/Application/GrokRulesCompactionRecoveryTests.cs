using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class GrokRulesCompactionRecoveryTests
{
    // Genuine installed Grok 1.0.13 automatic-compaction capture. Fixture provenance
    // explicitly distinguishes this local-stub trigger calibration from live endurance.
    internal static readonly string NativeBoundary = CaptureRow("auto_compact_completed");
    private static readonly string CapturedTool = CaptureRow("tool_call");
    private static readonly string CapturedEnd = CaptureRow("turn_completed");

    private static string CaptureRow(string kind) => File.ReadLines(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "grok-1.0.13-auto-compaction.jsonl")).First(line =>
        {
            using var row = JsonDocument.Parse(line);
            return row.RootElement.GetProperty("params").GetProperty("update").GetProperty("sessionUpdate").GetString() == kind;
        });

    [Test]
    [Arguments("live", "standing")]
    [Arguments("live", "channel")]
    [Arguments("live", "retired-pool")]
    [Arguments("sync", "standing")]
    [Arguments("sync", "channel")]
    [Arguments("sync", "retired-pool")]
    [Arguments("startup", "standing")]
    [Arguments("startup", "channel")]
    [Arguments("startup", "retired-pool")]
    [Arguments("blocked-herdr", "standing")]
    [Arguments("in-flight", "standing")]
    public async Task Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(string lane, string population)
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConfigureServices = services => {
            services.AddSingleton(Options.Create(new GrokRulesSettings()));
            services.AddSingleton<GrokRulesRefreshService>();
            services.AddSingleton<CompactionRecoveryService>();
            services.AddSingleton(sp => new PtyDeliveryProfile(
                sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<PtyDeliveryProfile>.Instance,
                sp.GetRequiredService<IOptions<DelegationSettings>>(), TimeProvider.System, backendOverride: "inbox"));
            services.AddSingleton<SessionDeliveryProfile>();
        }});
        h.Runner.Capabilities = new("InboxConhost", "inbox", "test", false,
            SessionBackends: [SessionBackends.PtyHost, SessionBackends.Herdr]);
        var rules = h.Provider.GetRequiredService<GrokRulesRefreshService>();
        var payload = new GrokRulesPayload("attachment-only standing rules\n", 1, Guid.NewGuid());
        var bytes = Encoding.UTF8.GetBytes(payload.Content);
        var receipt = new GrokRulesReceipt($"C:\\remote\\instructions\\grok\\{h.SessionId:N}\\rules.md",
            GrokRulesTransport.Hash(bytes), bytes.Length, 1, payload.Generation);
        await using var db = BridgeQueueHarness.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId);
        session.AgentKind = AgentKind.Grok;
        session.GrokRulesGeneration = receipt.Generation;
        session.GrokRulesExpectedSha256 = receipt.Sha256;
        session.GrokRulesExpectedByteCount = receipt.ByteCount;
        session.GrokRulesReceiptJson = JsonSerializer.Serialize(receipt);
        session.GrokRulesState = GrokRulesState.Pending;
        await db.SaveChangesAsync();
        // Historical initialization is seed data: mutations of current recovery must reach
        // the boundary assertion, not fail during fixture construction.
        var launchId = Guid.NewGuid();
        db.SessionQueuedMessages.Add(new() {
            Id = launchId, AgentSessionId = h.SessionId, Sequence = 1,
            Origin = QueuedMessageOrigin.System, CreatedAt = DateTime.UtcNow,
            Body = GrokRulesRefreshService.Prompt(launchId, receipt),
            RulesRefreshKey = $"launch:{receipt.Generation:N}",
            RulesReceiptJson = session.GrokRulesReceiptJson, RulesChainId = launchId,
            RulesAcknowledgedAt = DateTime.UtcNow, Status = QueuedMessageStatus.Sent });
        session.GrokRulesReadyAt = DateTime.UtcNow;
        session.GrokRulesState = GrokRulesState.Ready;
        if (lane == "blocked-herdr")
        {
            session.SessionBackend = SessionBackend.Herdr;
            h.Runtime.SetTestAgentStatus(h.SessionId, "blocked");
        }
        await db.SaveChangesAsync();
        if (population == "channel") await h.BindChannelAsync();
        if (population == "retired-pool")
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteDeleteAsync();
        Guid? inFlight = lane == "in-flight"
            ? await h.SeedPendingMessageAsync("ordinary work already submitted before the boundary",
                deliveryAttempts: 1, baselineSequence: 0, status: QueuedMessageStatus.Sent)
            : null;
        var ordinary = await h.SeedPendingMessageAsync("older ordinary work must follow the reread");
        await h.MarkWorkingAsync();

        var path = Path.Combine(h.TempRoot, "captured-updates.jsonl");
        await File.WriteAllTextAsync(path, CapturedTool + "\n" + NativeBoundary + "\n");
        await using var tailer = new GrokTranscriptTailer(h.SessionId, path, new SessionRunnerEventHub(), NullLogger.Instance,
            pollInterval: TimeSpan.FromMilliseconds(20));
        tailer.Start();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (tailer.Snapshot().Entries.Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(20);
        tailer.Snapshot().Entries.Count.ShouldBe(2);
        var tool = JsonSerializer.Deserialize<SessionRunnerTranscriptEvent>(JsonSerializer.Serialize(tailer.Snapshot().Entries[0]))! with { Sequence = 9 };
        tool.Kind.ShouldBe(TranscriptKinds.ToolCall);
        await h.Runtime.ObserveTranscriptAsync(tool, CancellationToken.None);
        var captured = tailer.Snapshot().Entries[1];
        captured.InputTokens.ShouldBe(15);
        var entry = JsonSerializer.Deserialize<SessionRunnerTranscriptEvent>(JsonSerializer.Serialize(captured))! with { Sequence = 10 };
        h.Runner.SetTranscript(new(h.SessionId, [entry], 10));
        if (lane is "live" or "blocked-herdr" or "in-flight") await h.Runtime.ObserveTranscriptAsync(entry, CancellationToken.None);
        else if (lane == "sync") await h.Runtime.SyncTranscriptAsync(h.SessionId, CancellationToken.None);
        else
        {
            // Crash after transcript commit, before the trigger transaction.
            db.TranscriptEntries.Add(new() { Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = entry.Sequence,
                Kind = entry.Kind, Uuid = entry.Uuid, Text = entry.Text, InputTokens = entry.InputTokens,
                CreatedAt = DateTime.UtcNow, Timestamp = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await rules.RecoverActiveAsync(CancellationToken.None);
        }
        h.Adapter.SubmittedBodies.ShouldBeEmpty("native auto compaction during a tool turn must not flush older work");
        captured.Kind.ShouldBe(TranscriptKinds.CompactBoundary);
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null))
            .ShouldBe(1, $"{lane} must create the trigger before another recovery lane can mask its absence");
        await h.Runtime.SyncTranscriptAsync(h.SessionId, CancellationToken.None);
        h.Runner.SetTranscript(new(h.SessionId, [entry with { Sequence = 100 }], 100));
        await h.Runtime.SyncTranscriptAsync(h.SessionId, CancellationToken.None);
        var boundary = await db.TranscriptEntries.SingleAsync(e => e.AgentSessionId == h.SessionId && e.Kind == TranscriptKinds.CompactBoundary);
        var refresh = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId && m.RulesBoundarySequence != null);
        refresh.RulesRefreshKey.ShouldBe($"compact:{boundary.Id:N}");
        refresh.DeliveryAttempts.ShouldBe(0);
        refresh.RulesDeadlineAt.ShouldBeNull("a mid-turn boundary must not start the idle delivery deadline");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == h.SessionId)).GrokRulesState.ShouldBe(GrokRulesState.Pending);
        await File.AppendAllTextAsync(path, CapturedEnd + "\n");
        var endDeadline = DateTime.UtcNow.AddSeconds(10);
        while (tailer.Snapshot().Entries.Count < 3 && DateTime.UtcNow < endDeadline) await Task.Delay(20);
        var turnEnd = JsonSerializer.Deserialize<SessionRunnerTranscriptEvent>(JsonSerializer.Serialize(tailer.Snapshot().Entries.Last()))! with { Sequence = 101 };
        turnEnd.Kind.ShouldBe(TranscriptKinds.TurnEnd);
        await h.Runtime.ObserveTranscriptAsync(turnEnd, CancellationToken.None);
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        if (lane == "blocked-herdr")
        {
            h.Adapter.SubmittedBodies.ShouldBeEmpty("a blocking Herdr UI must not charge a refresh attempt or type work");
            (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == refresh.Id)).DeliveryAttempts.ShouldBe(0);
            h.Runtime.SetTestAgentStatus(h.SessionId, "idle");
            await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        }
        if (inFlight is Guid priorId)
        {
            h.Adapter.SubmittedBodies.ShouldBeEmpty("an already-started ordinary delivery must finish before a refresh can type");
            (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == priorId)).DeliveryAttempts.ShouldBe(1);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt,
                "ordinary work already submitted before the boundary", timestamp: DateTime.UtcNow);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
            await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
            (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == priorId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        }
        h.Adapter.SubmittedBodies.ShouldHaveSingleItem().ShouldStartWith(GrokRulesRefreshService.Header(refresh.Id));
        (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == ordinary)).Status.ShouldBe(QueuedMessageStatus.Pending);

    }
}
