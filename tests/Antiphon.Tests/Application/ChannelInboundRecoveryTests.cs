using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Dtos;
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
        NullLogger<ChannelBridgeService>.Instance,
        h.Provider.GetRequiredService<ChannelInboundWakeSignal>());

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

    private static void InstallTranscriptReceipt(BridgeQueueHarness h)
    {
        h.Adapter.OnSubmitted = async submitted =>
        {
            await using var db = Db(h.ConnectionString);
            var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId)
                .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
            var record = new SessionRunnerTranscriptEvent(h.SessionId, sequence, TranscriptKinds.UserPrompt,
                Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow, "user", submitted,
                null, null, null, null, null);
            h.Runner.SetTranscript(new SessionRunnerTranscriptDto(h.SessionId, [record], sequence));
            await h.Runtime.SyncTranscriptAsync(h.SessionId, Ct);
        };
    }

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

        await using var faultSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var incidentFault = new IncidentCommitFault();
        await using var faultHarness = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = faultSchema.ConnectionString, ClockSpeed = 20,
            Bridge = Settings(), AlwaysOn = false, PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(incidentFault),
        });
        var faultChat = await faultHarness.BindChannelAsync();
        await using (var setup = Db(faultSchema.ConnectionString))
            await setup.AgentSessions.Where(s => s.Id == faultHarness.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
        var faultMessage = Message(faultChat, $"incident-fault-{Guid.NewGuid():N}", "incident failure must be retryable");
        incidentFault.Arm();
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await Bridge(faultHarness).HandleInboundAsync(faultMessage, Ct));
        await using (var cut = Db(faultSchema.ConnectionString))
        {
            var retained = await cut.ChannelInbounds.AsNoTracking()
                .SingleAsync(i => i.NativeMessageId == faultMessage.ChannelMessageId);
            retained.EnvelopeJson.ShouldContain(faultMessage.Text!);
            retained.WakeTimeoutIncidentAt.ShouldBeNull();
            (await cut.AgentIncidents.CountAsync(i => i.AgentId == faultHarness.AgentId
                && i.FailureReason == "ChannelWakeTimeout")).ShouldBe(0);
        }
        await Bridge(faultHarness).DrainPendingAsync(Ct);
        await using var repaired = Db(faultSchema.ConnectionString);
        (await repaired.AgentIncidents.CountAsync(i => i.AgentId == faultHarness.AgentId
            && i.FailureReason == "ChannelWakeTimeout")).ShouldBe(1);
        (await repaired.ChannelInbounds.Where(i => i.NativeMessageId == faultMessage.ChannelMessageId)
            .Select(i => i.WakeTimeoutIncidentAt).SingleAsync()).ShouldNotBeNull();
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
        Guid replacementAgentId; Guid replacementSessionId;
        await using (var replacement = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(), AlwaysOn = false,
            PreserveDatabaseOnDispose = true,
        }))
        {
            replacementAgentId = replacement.AgentId;
            replacementSessionId = replacement.SessionId;
        }
        await using (var rebound = Db(schema.ConnectionString))
            await rebound.ChatChannels.Where(c => c.ExternalId == chat)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.AgentId, replacementAgentId));
        await using var recovered = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, AttachSessionId = session, AttachAgentId = agent,
            Bridge = Settings(), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        InstallTranscriptReceipt(recovered);
        var recoveredBridge = Bridge(recovered);
        await recoveredBridge.StartAsync(Ct);
        await WaitForAsync(async () =>
        {
            await using var pending = Db(schema.ConnectionString);
            return await pending.ChannelInbounds.AnyAsync(i => i.NativeMessageId == native && i.QueueMessageId != null);
        });
        await recoveredBridge.StopAsync(Ct);
        await using var verify = Db(schema.ConnectionString);
        var inbound = await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == native);
        var owner = await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.SourceChannelInboundId == inbound.Id);
        owner.Body.ShouldContain("durable across restart");
        owner.Body.ShouldContain("[antiphon-channel:");
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == session &&
            t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.Contains(owner.Body))).ShouldBe(1);
        (await verify.SessionQueuedMessages.CountAsync(q => q.AgentSessionId == replacementSessionId
            && q.SourceChannelInboundId != null)).ShouldBe(0);

        await verify.ChatChannels.Where(c => c.ExternalId == chat)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.AgentId, agent));
        await recovered.MarkWorkingAsync();
        var busyNative = $"busy-{Guid.NewGuid():N}";
        await Bridge(recovered).HandleInboundAsync(Message(chat, busyNative, "wait until turn end"), Ct);
        var busyInbound = await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == busyNative);
        var busyOwner = await verify.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(q => q.SourceChannelInboundId == busyInbound.Id);
        busyOwner.Status.ShouldBe(QueuedMessageStatus.Pending);
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(1);
        await recovered.Queue.OnTurnEndAsync(session, Ct);
        await WaitForAsync(async () =>
        {
            await using var received = Db(schema.ConnectionString);
            return await received.TranscriptEntries.AnyAsync(t => t.AgentSessionId == session
                && t.Kind == TranscriptKinds.UserPrompt && t.Text != null && t.Text.Contains(busyOwner.Body));
        });
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(2);

        await RecoveryTriggerAsync(signal: true);
        await RecoveryTriggerAsync(signal: false);
    }

    private static async Task RecoveryTriggerAsync(bool signal)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, ClockSpeed = signal ? null : 5,
            Bridge = Settings(timeout: 1), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var chat = await h.BindChannelAsync();
        InstallTranscriptReceipt(h);
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
        var native = $"trigger-{signal}-{Guid.NewGuid():N}";
        await Bridge(h).HandleInboundAsync(Message(chat, native, "recovered by worker trigger"), Ct);
        var worker = Bridge(h);
        await worker.StartAsync(Ct);
        await WaitForAsync(() => Task.FromResult(worker.CompletedDrainIterations > 0));
        await using (var db = Db(schema.ConnectionString))
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        if (signal)
            h.Provider.GetRequiredService<ChannelInboundWakeSignal>().Signal(h.AgentId);
        await WaitForAsync(async () =>
        {
            await using var db = Db(schema.ConnectionString);
            return await db.ChannelInbounds.AnyAsync(i => i.NativeMessageId == native && i.QueueMessageId != null)
                && await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == h.SessionId
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text != null
                    && t.Text.Contains("recovered by worker trigger"));
        });
        await worker.StopAsync(Ct);
    }

    [Test]
    public async Task C593_AcceptanceAckAndQueueCommitCuts_AreIdempotent()
    {
        await using (var ackSchema = await TestDbFixture.CreateIsolatedSchemaAsync())
        {
            var gate = new AcceptanceGate();
            await using var hostedHarness = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = ackSchema.ConnectionString, Bridge = Settings(timeout: 5),
                ClockSpeed = 20, AlwaysOn = false, PreserveDatabaseOnDispose = true,
                ConfigureDbContext = options => options.AddInterceptors(gate),
            });
            var hostedChat = await hostedHarness.BindChannelAsync();
            await using (var setup = Db(ackSchema.ConnectionString))
                await setup.AgentSessions.Where(s => s.Id == hostedHarness.SessionId)
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
            var acceptedMessage = Message(hostedChat, "hosted-" + Guid.NewGuid().ToString("N"),
                "full envelope before Kafka acknowledgement");
            gate.PauseNext();
            var hosted = Bridge(hostedHarness);
            await hosted.StartAsync(Ct);
            hostedHarness.Messaging.InjectInbound(acceptedMessage);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
            hostedHarness.Messaging.AcknowledgedCount.ShouldBe(0);
            await using (var beforeCommit = Db(ackSchema.ConnectionString))
            {
                (await beforeCommit.ChannelInbounds.AnyAsync(i => i.NativeMessageId == acceptedMessage.ChannelMessageId)).ShouldBeFalse();
                (await beforeCommit.ChatChannels.Where(c => c.ExternalId == hostedChat)
                    .Select(c => c.MessageCount).SingleAsync()).ShouldBe(0);
            }
            gate.Release.TrySetResult(true);
            await WaitForAsync(() => Task.FromResult(hostedHarness.Messaging.AcknowledgedCount == 1));
            await using (var afterCommit = Db(ackSchema.ConnectionString))
                (await afterCommit.ChannelInbounds.Where(i => i.NativeMessageId == acceptedMessage.ChannelMessageId)
                    .Select(i => i.EnvelopeJson).SingleAsync()).ShouldContain(acceptedMessage.Text!);
            hostedHarness.Adapter.SentInput.ShouldBeEmpty(); // slow wake has not completed
            await hosted.StopAsync(Ct);

            gate.FailNext();
            var failedMessage = Message(hostedChat, "failed-" + Guid.NewGuid().ToString("N"),
                "replay after acceptance failure");
            var failedHost = Bridge(hostedHarness);
            await failedHost.StartAsync(Ct);
            hostedHarness.Messaging.InjectInbound(failedMessage);
            await gate.Failed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            hostedHarness.Messaging.AcknowledgedCount.ShouldBe(1);
            await using (var failedRead = Db(ackSchema.ConnectionString))
                (await failedRead.ChannelInbounds.AnyAsync(i => i.NativeMessageId == failedMessage.ChannelMessageId)).ShouldBeFalse();
            await failedHost.StopAsync(Ct);
            var replayHost = Bridge(hostedHarness);
            await replayHost.StartAsync(Ct);
            await WaitForAsync(() => Task.FromResult(hostedHarness.Messaging.AcknowledgedCount == 2));
            await replayHost.StopAsync(Ct);
        }

        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var mappingFault = new QueueMappingFault();
        await using var h = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = schema.ConnectionString, Bridge = Settings(),
            AlwaysOn = false, PreserveDatabaseOnDispose = true,
            ConfigureDbContext = options => options.AddInterceptors(mappingFault),
        });
        var chat = await h.BindChannelAsync();
        var faulted = Message(chat, "fault-" + Guid.NewGuid().ToString("N"), "must survive mapping fault") with
        {
            Attachments = [new Attachment
            {
                Kind = AttachmentKind.File, ChannelRef = "stable-file", Name = "retry-proof.bin",
                Content = [31, 32, 33, 34],
            }],
        };
        mappingFault.Arm();
        await Bridge(h).HandleInboundAsync(faulted, Ct);
        await using (var cut = Db(schema.ConnectionString))
        {
            var pending = await cut.ChannelInbounds.AsNoTracking().SingleAsync(i => i.NativeMessageId == faulted.ChannelMessageId);
            pending.QueueMessageId.ShouldBeNull();
            (await cut.SessionQueuedMessages.CountAsync(q => q.SourceChannelInboundId == pending.Id)).ShouldBe(0,
                "queue owner and every journal member must roll back together");
        }
        var inbox = Path.Combine(h.TempRoot, "workspace", ".antiphon", "inbox");
        var firstSavedPath = Directory.GetFiles(inbox).Single();
        File.ReadAllBytes(firstSavedPath).ShouldBe(faulted.Attachments.Single().Content);
        await Bridge(h).DrainPendingAsync(Ct);
        Directory.GetFiles(inbox).ShouldBe([firstSavedPath]);
        await using (var recoveredOwner = Db(schema.ConnectionString))
            (await recoveredOwner.SessionQueuedMessages.Where(q => q.SourceChannelInboundId != null
                && q.Body.Contains("must survive mapping fault")).Select(q => q.Body).SingleAsync())
                .ShouldContain(firstSavedPath);
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
        (await db.ChatChannels.Where(c => c.ExternalId == chat).Select(c => c.MessageCount).SingleAsync()).ShouldBe(3);
        var inbounds = await db.ChannelInbounds.Where(i => i.ConversationId == chat).ToListAsync();
        inbounds.Count.ShouldBe(3);
        var owners = await db.SessionQueuedMessages.Where(q => q.AgentSessionId == h.SessionId &&
            q.SourceChannelInboundId != null).ToListAsync();
        owners.Count.ShouldBe(3);
        owners.Select(o => o.SourceChannelInboundId!.Value).Distinct().Count().ShouldBe(3);

        // A catalog row from before the inbound journal can already name this message.
        // That one-slot hint is not an acceptance or delivery receipt.
        var legacyChat = await h.BindChannelAsync($"legacy-{Guid.NewGuid():N}");
        var legacyMessage = Message(legacyChat, $"legacy-native-{Guid.NewGuid():N}",
            "complete legacy replay body");
        await db.ChatChannels.Where(c => c.ExternalId == legacyChat)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.LastChannelMessageId,
                legacyMessage.ChannelMessageId));
        await Bridge(h).HandleInboundAsync(legacyMessage, Ct);
        var legacyInbound = await db.ChannelInbounds.AsNoTracking()
            .SingleAsync(i => i.NativeMessageId == legacyMessage.ChannelMessageId);
        legacyInbound.EnvelopeJson.ShouldContain(legacyMessage.Text!);
        legacyInbound.QueueMessageId.ShouldNotBeNull();
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
            // Force the tie that a frozen clock or broker replay can produce. The old
            // (AcceptedAt, random Id) order now places the second native message first.
            var tiedAt = DateTime.UtcNow;
            await verify.ChannelInbounds.Where(i => i.NativeMessageId == firstId)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.AcceptedAt, tiedAt)
                    .SetProperty(i => i.Id, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));
            await verify.ChannelInbounds.Where(i => i.NativeMessageId == secondId)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.AcceptedAt, tiedAt)
                    .SetProperty(i => i.Id, Guid.Parse("00000000-0000-0000-0000-000000000001")));
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
            return await db.ChannelInbounds.CountAsync(i => i.ConversationId == chat && i.QueueMessageId != null) == 2
                && recovered.Adapter.SubmittedBodies.Count == 1;
        });
        await using var db = Db(schema.ConnectionString);
        var members = await db.ChannelInbounds.Where(i => i.ConversationId == chat).OrderBy(i => i.AcceptedAt).ToListAsync();
        members.Select(i => i.NativeMessageId).Order().ShouldBe(new[] { firstId, secondId }.Order());
        members.Select(i => i.QueueMessageId).Distinct().Count().ShouldBe(1);
        var owner = await db.SessionQueuedMessages.SingleAsync(q => q.Id == members[0].QueueMessageId);
        owner.Body.IndexOf("line one tail", StringComparison.Ordinal).ShouldBeLessThan(owner.Body.IndexOf("line two tail", StringComparison.Ordinal));
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(1);

        // Delimiter-bearing native IDs must still form distinct (conversation, sender) lanes.
        // These two tuples collide under a colon-concatenated string key.
        var leftChat = await recovered.BindChannelAsync($"lane-{Guid.NewGuid():N}:a:b");
        var rightChat = leftChat[..^2];
        await recovered.BindChannelAsync(rightChat);
        var leftNative = $"left-{Guid.NewGuid():N}";
        var rightNative = $"right-{Guid.NewGuid():N}";
        var laneBridge = Bridge(recovered);
        await laneBridge.HandleInboundAsync(Message(leftChat, leftNative, "left lane tail", "c"), Ct);
        await laneBridge.HandleInboundAsync(Message(rightChat, rightNative, "right lane tail", "b:c"), Ct);
        await WaitForAsync(async () =>
        {
            await using var pending = Db(schema.ConnectionString);
            return await pending.ChannelInbounds.CountAsync(i =>
                (i.NativeMessageId == leftNative || i.NativeMessageId == rightNative)
                && i.QueueMessageId != null) == 2;
        });
        var laneMembers = await db.ChannelInbounds.AsNoTracking()
            .Where(i => i.NativeMessageId == leftNative || i.NativeMessageId == rightNative)
            .Select(i => i.QueueMessageId).ToListAsync();
        laneMembers.Distinct().Count().ShouldBe(2);
        await recovered.Queue.OnTurnEndAsync(session, Ct);
        await recovered.Queue.OnTurnEndAsync(session, Ct);
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(3);
        recovered.Adapter.SubmittedBodies.Count(b => b.Contains("left lane tail") && b.Contains("right lane tail")).ShouldBe(0);
        await laneBridge.HandleInboundAsync(Message(chat, secondId, "line two tail"), Ct);
        (await db.SessionQueuedMessages.CountAsync(q => q.SourceChannelInboundId != null
            && (q.AgentSessionId == session))).ShouldBe(3);
        recovered.Adapter.SubmittedBodies.Count.ShouldBe(3);

        // A held first page must not starve the next accepted message forever.
        await using var pageSchema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var held = await BridgeQueueHarness.CreateAsync(new()
        {
            ConnectionString = pageSchema.ConnectionString, ClockSpeed = 100,
            Bridge = Settings(timeout: 1), AlwaysOn = false, PreserveDatabaseOnDispose = true,
        });
        var heldChat = await held.BindChannelAsync();
        var nativeIds = Enumerable.Range(0, 65).Select(i => $"page-{i:D2}-{Guid.NewGuid():N}").ToArray();
        await using (var seed = Db(pageSchema.ConnectionString))
        {
            await seed.AgentSessions.Where(s => s.Id == held.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
            var channelId = await seed.ChatChannels.Where(c => c.ExternalId == heldChat).Select(c => c.Id).SingleAsync();
            foreach (var nativeId in nativeIds)
                seed.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = Guid.NewGuid(), Provider = "telegram", ConversationId = heldChat,
                    NativeMessageId = nativeId, AgentId = held.AgentId, ChatChannelId = channelId,
                    EnvelopeJson = JsonSerializer.Serialize(Message(heldChat, nativeId, nativeId), Antiphon.Messaging.MessagingJson.Options),
                    AcceptedAt = DateTime.UtcNow,
                });
            await seed.SaveChangesAsync();
        }
        var pageBridge = Bridge(held);
        string lastNativeId;
        await using (var pageOrder = Db(pageSchema.ConnectionString))
            lastNativeId = await pageOrder.ChannelInbounds.OrderByDescending(i => i.AcceptanceSequence)
                .Select(i => i.NativeMessageId).FirstAsync();
        await pageBridge.DrainPendingAsync(Ct);
        await pageBridge.DrainPendingAsync(Ct);
        await using var pageVerify = Db(pageSchema.ConnectionString);
        (await pageVerify.ChannelInbounds.Where(i => i.NativeMessageId == lastNativeId)
            .Select(i => i.WakeTimeoutIncidentAt).SingleAsync()).ShouldNotBeNull();
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
        var verifiedState = await verify.AgentSupervisionStates.AsNoTracking().SingleAsync(s => s.AgentId == h.AgentId);
        verifiedState.Suspended.ShouldBeTrue();
        verifiedState.LivenessLatchedAt.ShouldNotBeNull();
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

        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, body,
            timestamp: h.Now);
        await h.Queue.FlushSessionAsync(h.SessionId, Ct);
        await using (var assistantOnly = Db(schema.ConnectionString))
            (await assistantOnly.SessionQueuedMessages.AsNoTracking()
                .Where(q => q.Id == id).Select(q => q.Status).SingleAsync())
                .ShouldBe(QueuedMessageStatus.Pending);

        // A queued prompt is not a recipient UserPrompt. Even an exact body past the
        // attempt floor must leave this Channel obligation pending.
        await h.InsertTranscriptEntryAsync(TranscriptKinds.QueuedUserPrompt, body,
            timestamp: h.Now);
        await h.Queue.FlushSessionAsync(h.SessionId, Ct);
        await using var noRecipientReceipt = Db(schema.ConnectionString);
        var afterQueuedOnly = await noRecipientReceipt.SessionQueuedMessages.AsNoTracking()
            .SingleAsync(q => q.Id == id);
        afterQueuedOnly.Status.ShouldBe(QueuedMessageStatus.Pending);
        afterQueuedOnly.DeliveryAttempts.ShouldBe(3);
        h.Adapter.SentInput.ShouldBeEmpty();
    }

    private sealed class QueueMappingFault : SaveChangesInterceptor
    {
        private int _armed;
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1 && eventData.Context?.ChangeTracker.Entries<ChannelInbound>()
                .Any(e => e.State == EntityState.Modified && e.Entity.QueueMessageId != null) == true
                && Interlocked.Exchange(ref _armed, 0) == 1)
                throw new InvalidOperationException("synthetic journal mapping failure after queue preparation");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class AcceptanceGate : SaveChangesInterceptor
    {
        private int _mode;
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void PauseNext() => Interlocked.Exchange(ref _mode, 1);
        public void FailNext() => Interlocked.Exchange(ref _mode, 2);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<ChannelInbound>()
                .Any(e => e.State == EntityState.Added) == true)
            {
                switch (Interlocked.Exchange(ref _mode, 0))
                {
                    case 1:
                        Entered.TrySetResult(true);
                        await Release.Task.WaitAsync(cancellationToken);
                        break;
                    case 2:
                        Failed.TrySetResult(true);
                        throw new InvalidOperationException("synthetic acceptance transaction failure");
                }
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class IncidentCommitFault : SaveChangesInterceptor
    {
        private int _armed;
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AgentIncident>()
                .Any(e => e.State == EntityState.Added && e.Entity.FailureReason == "ChannelWakeTimeout") == true
                && Interlocked.Exchange(ref _armed, 0) == 1)
                throw new InvalidOperationException("synthetic incident commit failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
