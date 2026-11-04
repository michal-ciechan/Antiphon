using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1716: one held session queue lock must not stall the inbound wake worker for every agent.
/// A pass waits a bounded time for a session's lock, skips that session's remaining rows in order,
/// and retries them on the next pass; busy is never a failure, incident or alert.
/// </summary>
public sealed partial class ChannelInboundRecoveryTests
{
    // Generous for one bounded pass (1 s per busy session plus the healthy delivery); the base
    // code never finishes the pass while the lock is held.
    private static readonly TimeSpan C1716PassBound = TimeSpan.FromSeconds(8);

    [Test]
    [Arguments("enqueue")]
    [Arguments("sweep")]
    public async Task C1716_HeldQueueLock_OtherSessionQueuedWithinOnePass(string phase)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateHarnessAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), PreserveDatabaseOnDispose = true,
        });
        await InstallTranscriptReceipt(h);
        var chat = await h.BindChannelAsync();
        var heldNative = $"c1716-held-{Guid.NewGuid():N}";
        var heldInbound = await SeedC1716InboundAsync(h, h.AgentId, chat, heldNative, "held body");
        if (phase == "sweep")
        {
            // A queue row the pass's owned-row sweep flushes; the held lock meets that flush.
            await h.Queue.EnqueueAsync(h.SessionId, "held channel body", MessageSendMode.WhenIdle, Ct,
                origin: QueuedMessageOrigin.Channel, conversationKey: $"telegram:{chat}", deliverIfIdle: false,
                sourceChannelInboundId: heldInbound, channelMemberInboundIds: [heldInbound]);
        }
        var healthy = await SeedHealthyRecipientAsync(h, "healthy body");

        var queueLock = h.Queue.GetLock(h.SessionId);
        await queueLock.WaitAsync();
        Task? drain = null;
        try
        {
            drain = Bridge(h).DrainPendingAsync(Ct);
            await drain.WaitAsync(C1716PassBound);
            await AssertCompleteHealthyReceiptAsync(h, healthy);
            await using var db = Db(schema.ConnectionString);
            if (phase == "enqueue")
                (await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == heldInbound))
                    .QueueMessageId.ShouldBeNull("the held session's row waits unchanged for a later pass");
            else
                (await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.SourceChannelInboundId == heldInbound))
                    .Status.ShouldBe(QueuedMessageStatus.Pending);
            await AssertC1716NoFailureAsync(schema.ConnectionString, h.AgentId);
        }
        finally
        {
            queueLock.Release();
            if (drain is not null)
                try { await drain.WaitAsync(TimeSpan.FromSeconds(30)); } catch { /* the red run's diagnostic is above */ }
        }
    }

    [Test]
    public async Task C1716_BusySession_KeepsOrderWhenLockFreesMidPass_AndRetriesNextPass()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await CreateHarnessAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), PreserveDatabaseOnDispose = true,
        });
        await InstallTranscriptReceipt(h);
        var chat = await h.BindChannelAsync();
        // Acceptance order A1 < B < A2.
        var a1 = await SeedC1716InboundAsync(h, h.AgentId, chat, $"c1716-a1-{Guid.NewGuid():N}", "a1 body");
        var healthy = await SeedHealthyRecipientAsync(h, "b body");
        var a2 = await SeedC1716InboundAsync(h, h.AgentId, chat, $"c1716-a2-{Guid.NewGuid():N}", "a2 body");

        var queueLock = h.Queue.GetLock(h.SessionId);
        await queueLock.WaitAsync();
        var released = 0;
        // A's lock frees the moment A1 is reported busy, before the pass reaches A2.
        var logger = new C1716BusyHookLogger(() =>
        {
            if (Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
        });
        var bridge = Bridge(h, logger);
        Task? drain = null;
        try
        {
            drain = bridge.DrainPendingAsync(Ct);
            await drain.WaitAsync(C1716PassBound);
            Volatile.Read(ref released).ShouldBe(1, "A1 met the held lock and A's lock freed mid-pass");
            await AssertCompleteHealthyReceiptAsync(h, healthy);
            await using (var first = Db(schema.ConnectionString))
            {
                (await first.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == a1))
                    .QueueMessageId.ShouldBeNull("A1 met the held lock");
                (await first.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == a2))
                    .QueueMessageId.ShouldBeNull("A2 is not queued in the pass where A1 was busy, even after A's lock frees");
            }
            await AssertC1716NoFailureAsync(schema.ConnectionString, h.AgentId);

            // The next pass retries the skipped session, oldest row first.
            await bridge.DrainPendingAsync(Ct).WaitAsync(C1716PassBound);
            await using var second = Db(schema.ConnectionString);
            var owner1 = (await second.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == a1)).QueueMessageId;
            var owner2 = (await second.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == a2)).QueueMessageId;
            owner1.ShouldNotBeNull("the skipped session's oldest row is retried on the next pass");
            owner2.ShouldNotBeNull("the skipped session's later row is retried on the next pass");
            var sequence1 = await second.SessionQueuedMessages.Where(q => q.Id == owner1).Select(q => q.Sequence).SingleAsync();
            var sequence2 = await second.SessionQueuedMessages.Where(q => q.Id == owner2).Select(q => q.Sequence).SingleAsync();
            sequence1.ShouldBeLessThan(sequence2, "A1 keeps its place ahead of A2");
            await AssertC1716NoFailureAsync(schema.ConnectionString, h.AgentId);
        }
        finally
        {
            if (Interlocked.Exchange(ref released, 1) == 0) queueLock.Release();
            if (drain is not null)
                try { await drain.WaitAsync(TimeSpan.FromSeconds(30)); } catch { /* the red run's diagnostic is above */ }
        }
    }

    private static async Task<Guid> SeedC1716InboundAsync(BridgeQueueHarness h, Guid agentId, string chat,
        string native, string body)
    {
        await using var seed = Db(h.ConnectionString);
        var channelId = await seed.ChatChannels.Where(c => c.ExternalId == chat).Select(c => c.Id).SingleAsync();
        var inbound = new ChannelInbound
        {
            Id = Guid.NewGuid(), Provider = "telegram", ConversationId = chat,
            NativeMessageId = native, AgentId = agentId, ChatChannelId = channelId,
            EnvelopeJson = JsonSerializer.Serialize(Message(chat, native, body), MessagingJson.Options),
            AcceptedAt = h.Now,
        };
        seed.ChannelInbounds.Add(inbound);
        await seed.SaveChangesAsync();
        return inbound.Id;
    }

    /// <summary>Busy is not a failed attempt: no incident, no pending-message alert.</summary>
    private static async Task AssertC1716NoFailureAsync(string connectionString, Guid agentId)
    {
        await using var db = Db(connectionString);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == agentId)).ShouldBe(0, "busy is not a channel incident");
        (await db.Alerts.CountAsync(a => a.DedupKey.StartsWith("bridge:pending:"))).ShouldBe(0, "busy raises no bridge alert");
    }

    private static ChannelBridgeService Bridge(BridgeQueueHarness h, ILogger<ChannelBridgeService> logger) => new(
        h.Messaging, h.Queue, h.Provider.GetRequiredService<ChannelInboundDebouncer>(), h.EventBus,
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(), h.Clock, logger,
        h.Provider.GetRequiredService<ChannelInboundWakeSignal>());

    /// <summary>Runs <paramref name="onBusy"/> when the bridge reports a session's queue lock busy.</summary>
    private sealed class C1716BusyHookLogger(Action onBusy) : ILogger<ChannelBridgeService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains("queue lock is held", StringComparison.Ordinal))
                onBusy();
        }
    }
}
