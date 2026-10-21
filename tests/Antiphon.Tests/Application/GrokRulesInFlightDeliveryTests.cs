using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration"), NotInParallel]
public sealed class GrokRulesInFlightDeliveryTests
{
    [Test, Timeout(60_000)]
    public async Task An_already_started_delivery_finishes_without_refresh_interleaving(CancellationToken ct)
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConfigureServices = services => {
            services.AddSingleton(Options.Create(new GrokRulesSettings())); services.AddSingleton<GrokRulesRefreshService>();
        }});
        await using var db = BridgeQueueHarness.CreateContext();
        var session = await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId, ct);
        var receipt = new GrokRulesReceipt($"C:\\remote\\instructions\\grok\\{h.SessionId:N}\\rules.md", new string('a',64), 17, 1, Guid.NewGuid());
        session.AgentKind = AgentKind.Grok; session.GrokRulesGeneration = receipt.Generation;
        session.GrokRulesExpectedSha256 = receipt.Sha256; session.GrokRulesExpectedByteCount = receipt.ByteCount;
        session.GrokRulesReceiptJson = JsonSerializer.Serialize(receipt); session.GrokRulesState = GrokRulesState.Ready; session.GrokRulesReadyAt = DateTime.UtcNow;
        var launch = Guid.NewGuid();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = launch, AgentSessionId = h.SessionId, Sequence = 1,
            Body = GrokRulesRefreshService.Prompt(launch,receipt), CreatedAt = DateTime.UtcNow, Origin = QueuedMessageOrigin.System,
            RulesRefreshKey = "launch:" + receipt.Generation.ToString("N"), RulesReceiptJson = session.GrokRulesReceiptJson,
            RulesChainId = launch, RulesAcknowledgedAt = DateTime.UtcNow, Status = QueuedMessageStatus.Sent });
        await db.SaveChangesAsync(ct);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = h.Adapter.OnSubmitted!;
        h.Adapter.OnSubmitted = async body => {
            await original(body);
            if (body == "work already being submitted") { entered.SetResult(); await release.Task.WaitAsync(ct); }
        };
        var delivery = h.Queue.EnqueueAsync(h.SessionId, "work already being submitted", MessageSendMode.WhenIdle, ct);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10),ct);
            db.TranscriptEntries.Add(new TranscriptEntry { Id = Guid.NewGuid(), AgentSessionId = h.SessionId, Sequence = 100,
                Kind = TranscriptKinds.CompactBoundary, Uuid = "owned-native-boundary", Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            await h.Provider.GetRequiredService<GrokRulesRefreshService>().ReconcileAsync(h.SessionId, ct);
            h.Adapter.SubmittedBodies.ShouldHaveSingleItem().ShouldBe("work already being submitted");
            var refusal = await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueAsync(h.SessionId, "concurrent urgent work", MessageSendMode.Now, ct));
            refusal.Code.ShouldBe("grok_rules_initialization_pending");
            h.Adapter.SubmittedBodies.Count.ShouldBe(1, "the refresh and concurrent Now must not interleave with an in-flight submit");
        }
        finally { release.TrySetResult(); }
        await delivery.WaitAsync(TimeSpan.FromSeconds(10), ct);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId,ct);
        h.Adapter.SubmittedBodies.Count.ShouldBe(2);
        h.Adapter.SubmittedBodies[1].ShouldStartWith("[antiphon-grok-rules:");
    }
}
