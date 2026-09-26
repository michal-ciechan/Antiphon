using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel("MessageQueue")]
public sealed class ChannelInboundRecoveryTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static ChannelBridgeService Bridge(BridgeQueueHarness h) => new(
        h.Messaging, h.Queue, h.Provider.GetRequiredService<ChannelInboundDebouncer>(), h.EventBus,
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(), h.Clock,
        NullLogger<ChannelBridgeService>.Instance);

    private static AppDbContext Db(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static ChannelMessage Message(string chat, string native, string body, string sender = "alice") => new()
    {
        Id = Guid.NewGuid().ToString("N"), Channel = "telegram", ChannelMessageId = native,
        Conversation = new Conversation { Id = chat, Kind = ConversationKind.Group, Title = "Family" },
        Author = new Participant { Id = sender, DisplayName = sender },
        Timestamp = DateTimeOffset.UtcNow, Text = body, ReplyHandle = chat,
        Raw = JsonDocument.Parse("{\"from\":\"native\"}").RootElement.Clone(),
    };

    private static ChannelBridgeSettings Settings(int debounce = 0, int timeout = 1) => new()
    {
        Enabled = true, DebounceWindowMs = debounce, DebounceMaxMs = Math.Max(2000, debounce * 2),
        AgentStartTimeoutSeconds = timeout, AgentReadyDelaySeconds = 0,
    };

    private static async Task WaitForAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(25);
        }
        (await predicate()).ShouldBeTrue("durable handoff did not complete before the diagnostic deadline");
    }

    [Test]
    public async Task C593_WakeTimeout_PreservesEnvelopeAndCriticalIncident()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, ClockSpeed = 20,
            Bridge = Settings(), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
        var original = Message(chat, "timeout-" + Guid.NewGuid().ToString("N"), new string('x', 220) + "\nUNIQUE TAIL") with
        {
            Attachments = [new Attachment { Kind = AttachmentKind.File, ChannelRef = "file-1", Name = "proof.bin", Content = [1, 2, 3, 4] }],
            Mentions = [new Mention { Id = "target", IsMe = true }],
            ReplyTo = new ReplyReference { ChannelMessageId = "prior", Excerpt = "context" },
        };
        await Bridge(h).HandleInboundAsync(original, Ct);
        await using var verify = Db(schema.ConnectionString);
        var inbound = await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == original.ChannelMessageId);
        inbound.AgentId.ShouldBe(h.AgentId);
        inbound.ChatChannelId.ShouldNotBeNull();
        var restored = JsonSerializer.Deserialize<ChannelMessage>(inbound.EnvelopeJson!, Antiphon.Messaging.MessagingJson.Options)!;
        restored.Text.ShouldBe(original.Text);
        restored.Attachments.Single().Content.ShouldBe(original.Attachments.Single().Content);
        restored.ReplyTo!.ChannelMessageId.ShouldBe("prior");
        restored.Mentions.Single().Id.ShouldBe("target");
        restored.Raw.GetProperty("from").GetString().ShouldBe("native");
        (await verify.SessionQueuedMessages.CountAsync(q => q.AgentSessionId == h.SessionId)).ShouldBe(0);
        h.Adapter.SentInput.ShouldBeEmpty();
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId &&
            i.Kind == AgentIncidentKind.ChannelReplyLost && i.Severity == AlertSeverity.Critical &&
            i.FailureReason == "ChannelWakeTimeout" && i.Message.Contains("pending") &&
            i.Message.Contains("has not reached"))).ShouldBe(1);
        await Bridge(h).DrainPendingAsync(Ct);
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId && i.FailureReason == "ChannelWakeTimeout")).ShouldBe(1);
    }

    [Test]
    public async Task C593_Restart_DrainsToEligibleAndBusyRecipients()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        Guid agent; Guid session; string chat; string native;
        await using (var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, ClockSpeed = 20,
            Bridge = Settings(), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        }))
        {
            agent = h.AgentId; session = h.SessionId; chat = await h.BindChannelAsync();
            await using (var db = Db(schema.ConnectionString))
                await db.AgentSessions.Where(s => s.Id == session)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
            native = "restart-" + Guid.NewGuid().ToString("N");
            await Bridge(h).HandleInboundAsync(Message(chat, native, "durable across restart"), Ct);
        }
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == session)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        await using var recovered = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, AttachSessionId = session, AttachAgentId = agent,
            Bridge = Settings(), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        recovered.Adapter.OnSubmitted = async submitted =>
        {
            var record = new SessionRunnerTranscriptEvent(session, 1, TranscriptKinds.UserPrompt,
                Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow, "user", submitted,
                null, null, null, null, null);
            recovered.Runner.SetTranscript(new SessionRunnerTranscriptDto(session, [record], 1));
            await recovered.Runtime.SyncTranscriptAsync(session, Ct);
        };
        await Bridge(recovered).DrainPendingAsync(Ct);
        await using var verify = Db(schema.ConnectionString);
        var inbound = await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == native);
        var owner = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
        owner.Body.ShouldContain("durable across restart");
        owner.Body.ShouldContain("[antiphon-channel:");
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == session &&
            t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.Contains(owner.Body))).ShouldBe(1);
    }

    [Test]
    public async Task C593_AcceptanceAckAndQueueCommitCuts_AreIdempotent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        var first = Message(chat, "first-" + Guid.NewGuid().ToString("N"), "first complete body");
        var second = Message(chat, "second-" + Guid.NewGuid().ToString("N"), "second complete body");
        h.Messaging.InjectInbound(first);
        await using (var delivery = h.Messaging.ConsumeDeliveriesAsync().GetAsyncEnumerator())
        {
            (await delivery.MoveNextAsync()).ShouldBeTrue();
            h.Messaging.AcknowledgedCount.ShouldBe(0);
            await Bridge(h).HandleInboundAsync(delivery.Current.Message!, Ct);
            await using (var verify = Db(schema.ConnectionString))
                (await verify.ChannelInbounds.AnyAsync(i => i.NativeMessageId == first.ChannelMessageId &&
                    i.EnvelopeJson != null && i.EnvelopeJson.Contains("first complete body"))).ShouldBeTrue();
            await delivery.Current.AcknowledgeAsync("accepted");
        }
        h.Messaging.AcknowledgedCount.ShouldBe(1);
        await Bridge(h).HandleInboundAsync(second, Ct);
        await Bridge(h).HandleInboundAsync(first with { Id = Guid.NewGuid().ToString("N") }, Ct);
        await using var db = Db(schema.ConnectionString);
        (await db.ChatChannels.Where(c => c.ExternalId == chat).Select(c => c.MessageCount).SingleAsync()).ShouldBe(2);
        var inbounds = await db.ChannelInbounds.Where(i => i.ConversationId == chat).ToListAsync();
        inbounds.Count.ShouldBe(2);
        var owners = await db.SessionQueuedMessages.Where(q => q.AgentSessionId == h.SessionId &&
            q.SourceChannelInboundId != null).ToListAsync();
        owners.Count.ShouldBe(2);
        owners.Select(o => o.SourceChannelInboundId!.Value).Distinct().Count().ShouldBe(2);
    }

    [Test]
    public async Task C593_DebounceCrash_RetainsEveryMemberInOrder()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        Guid agent; Guid session; string chat; string firstId; string secondId;
        await using (var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(debounce: 10000),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
        }))
        {
            agent = h.AgentId; session = h.SessionId; chat = await h.BindChannelAsync();
            firstId = "batch-1-" + Guid.NewGuid().ToString("N");
            secondId = "batch-2-" + Guid.NewGuid().ToString("N");
            await Bridge(h).HandleInboundAsync(Message(chat, firstId, "line one tail"), Ct);
            await Bridge(h).HandleInboundAsync(Message(chat, secondId, "line two tail"), Ct);
            await using var verify = Db(schema.ConnectionString);
            (await verify.ChannelInbounds.CountAsync(i => i.ConversationId == chat && i.QueueMessageId == null)).ShouldBe(2);
        }
        await using var recovered = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, AttachSessionId = session, AttachAgentId = agent,
            Bridge = Settings(debounce: 150), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        await Bridge(recovered).DrainPendingAsync(Ct);
        await WaitForAsync(async () =>
        {
            await using var db = Db(schema.ConnectionString);
            return await db.ChannelInbounds.CountAsync(i => i.ConversationId == chat && i.QueueMessageId != null) == 2;
        });
        await using var db = Db(schema.ConnectionString);
        var members = await db.ChannelInbounds.Where(i => i.ConversationId == chat).OrderBy(i => i.AcceptedAt).ToListAsync();
        members.Select(i => i.NativeMessageId).ToArray().ShouldBe(new[] { firstId, secondId });
        members.Select(i => i.QueueMessageId).Distinct().Count().ShouldBe(1);
        var owner = await db.SessionQueuedMessages.SingleAsync(q => q.Id == members[0].QueueMessageId);
        owner.Body.IndexOf("line one tail", StringComparison.Ordinal).ShouldBeLessThan(owner.Body.IndexOf("line two tail", StringComparison.Ordinal));
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(1);
    }

    [Test]
    public async Task C593_HoldsAndConflicts_ParkWithoutRerouting()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), AlwaysOn = true,
            PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        await using (var db = Db(schema.ConnectionString))
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            var state = await db.AgentSupervisionStates.SingleOrDefaultAsync(s => s.AgentId == h.AgentId);
            if (state is null) { state = new AgentSupervisionState { AgentId = h.AgentId }; db.AgentSupervisionStates.Add(state); }
            state.HerdrFailureHeldAt = DateTime.UtcNow.AddMinutes(-1);
            state.HerdrConsecutiveFailures = 3;
            await db.SaveChangesAsync();
        }
        await Bridge(h).HandleInboundAsync(Message(chat, Guid.NewGuid().ToString("N"), "held first full body"), Ct);
        await Bridge(h).HandleInboundAsync(Message(chat, Guid.NewGuid().ToString("N"), "held second full body"), Ct);
        await using var verify = Db(schema.ConnectionString);
        (await verify.ChannelInbounds.CountAsync(i => i.AgentId == h.AgentId && i.EnvelopeJson != null)).ShouldBe(2);
        (await verify.AgentIncidents.CountAsync(i => i.AgentId == h.AgentId &&
            i.Kind == AgentIncidentKind.ChannelReplyLost && i.FailureReason == "HerdrSupervisionHeld" &&
            i.Severity == AlertSeverity.Critical)).ShouldBe(1);
        h.Adapter.SentInput.ShouldBeEmpty();
        h.Messaging.SentReplies.ShouldBeEmpty();
    }

    [Test]
    public async Task C593_ChannelWake_IsAutomaticWithQuotaOverride()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        await using (var db = Db(schema.ConnectionString))
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            var state = await db.AgentSupervisionStates.SingleOrDefaultAsync(s => s.AgentId == h.AgentId);
            if (state is null) { state = new AgentSupervisionState { AgentId = h.AgentId }; db.AgentSupervisionStates.Add(state); }
            state.Suspended = true;
            state.LivenessLatchedAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        await Bridge(h).HandleInboundAsync(Message(chat, Guid.NewGuid().ToString("N"), "preserve operator hold"), Ct);
        await using var verify = Db(schema.ConnectionString);
        var state = await verify.AgentSupervisionStates.AsNoTracking().SingleAsync(s => s.AgentId == h.AgentId);
        state.Suspended.ShouldBeTrue();
        state.LivenessLatchedAt.ShouldNotBeNull();
        (await verify.ChannelInbounds.CountAsync(i => i.AgentId == h.AgentId && i.QueueMessageId == null)).ShouldBe(1);
        h.Adapter.SentInput.ShouldBeEmpty();
    }

    [Test]
    public async Task C593_QueuedLaunch_WaitsOnceAndRecognizesTerminalFailure()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, ClockSpeed = 20,
            Bridge = Settings(timeout: 5), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
        var inbound = Message(chat, Guid.NewGuid().ToString("N"), "wait for Running");
        var handling = Bridge(h).HandleInboundAsync(inbound, Ct);
        await Task.Delay(30);
        h.Adapter.SentInput.ShouldBeEmpty();
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        await handling;
        h.Adapter.SubmittedBodies.Count.ShouldBe(1);
        await using var verify = Db(schema.ConnectionString);
        (await verify.ChannelInbounds.SingleAsync(i => i.NativeMessageId == inbound.ChannelMessageId)).QueueMessageId.ShouldNotBeNull();
    }

    [Test]
    public async Task C593_TransferAndUncertainWrite_RequireCompleteRecipientReceipt()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        var body = "complete marked body " + new string('z', 240) + " DISTINCT TAIL";
        var id = await h.SeedPendingMessageAsync(body, origin: QueuedMessageOrigin.Channel,
            conversationKey: $"telegram:{chat}", deliveryAttempts: 3);
        await h.Queue.FlushSessionAsync(h.SessionId, Ct);
        await using var verify = Db(schema.ConnectionString);
        var row = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == id);
        row.Body.ShouldBe(body);
        row.DeliveryAttempts.ShouldBe(3);
        h.Adapter.SentInput.ShouldBeEmpty();
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId &&
            t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.Contains("DISTINCT TAIL"))).ShouldBe(0);
    }
}
