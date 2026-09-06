using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
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
    // Verbatim CARD-0157 capture, Grok 1.0.5, also pinned in GrokTranscriptTailerTests.
    // Parser/replay evidence only: this is not a CARD-0395 live endurance run.
    internal const string NativeBoundary = """{"timestamp":1787167460,"method":"_x.ai/session/update","params":{"sessionId":"1636e434-b4bc-4743-ae39-9381bd83a2cc","update":{"sessionUpdate":"auto_compact_completed","tokens_before":106112,"tokens_after":34833,"summary_preview":null},"_meta":{"eventId":"1636e434-b4bc-4743-ae39-9381bd83a2cc-1550","agentTimestampMs":1787167460583}}}""";

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
    public async Task Captured_native_boundary_reaches_one_durable_refresh_without_idle_input(string lane, string population)
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConfigureServices = services => {
            services.AddSingleton(Options.Create(new GrokRulesSettings()));
            services.AddSingleton<GrokRulesRefreshService>();
            services.AddSingleton<CompactionRecoveryService>();
        }});
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
        await db.SaveChangesAsync();
        if (population == "channel") await h.BindChannelAsync();
        if (population == "retired-pool")
            await db.Agents.Where(a => a.Id == h.AgentId).ExecuteDeleteAsync();
        await h.MarkWorkingAsync();

        var path = Path.Combine(h.TempRoot, "captured-updates.jsonl");
        await File.WriteAllTextAsync(path, NativeBoundary + "\n");
        await using var tailer = new GrokTranscriptTailer(h.SessionId, path, new SessionRunnerEventHub(), NullLogger.Instance,
            pollInterval: TimeSpan.FromMilliseconds(20));
        tailer.Start();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (tailer.Snapshot().Entries.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
        var captured = tailer.Snapshot().Entries.ShouldHaveSingleItem();
        captured.Kind.ShouldBe(TranscriptKinds.CompactBoundary);
        captured.InputTokens.ShouldBe(34833);
        var entry = JsonSerializer.Deserialize<SessionRunnerTranscriptEvent>(JsonSerializer.Serialize(captured))! with { Sequence = 10 };
        h.Runner.SetTranscript(new(h.SessionId, [entry], 10));
        if (lane == "live") await h.Runtime.ObserveTranscriptAsync(entry, CancellationToken.None);
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
    }
}
