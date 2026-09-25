using System.Text.Json;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[NotInParallel]
public class ChannelPromptCorrelationTests
{
    private const string Envelope = "[Telegram \"Family\" — Tester 10:10] ";
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static Task<BridgeQueueHarness> HarnessAsync() => BridgeQueueHarness.CreateAsync(new()
    {
        AlwaysOn = false,
        Bridge = new() { Enabled = true, DebounceWindowMs = 0 },
        // Ingest via the runtime but let each scenario choose when publication/sweep happens.
        ConfigureServices = services => services.RemoveAll<ChannelReplyDispatcher>(),
    });

    private static ChannelReplyDispatcher Dispatcher(BridgeQueueHarness h,
        IAntiphonMessagingProducer? producer = null) => new(
        h.Provider.GetRequiredService<IServiceScopeFactory>(), producer ?? h.Messaging,
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(), TimeProvider.System,
        NullLogger<ChannelReplyDispatcher>.Instance);

    private static AppDbContext Db(BridgeQueueHarness h) =>
        new(TestDbFixture.CreateDbContextOptions(h.ConnectionString));

    private static async Task<SessionQueuedMessage> RowAsync(BridgeQueueHarness h, Guid id)
    {
        await using var db = Db(h);
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private static async Task<Guid> EnqueueAsync(BridgeQueueHarness h, string chat, string text,
        bool deliver = true)
    {
        Guid id = default;
        await h.Queue.EnqueueAsync(h.SessionId, Envelope + text, MessageSendMode.WhenIdle, Ct,
            origin: QueuedMessageOrigin.Channel, conversationKey: $"telegram:{chat}",
            onCreated: value => id = value, deliverIfIdle: deliver);
        return id;
    }

    internal static IReadOnlyList<TranscriptPart> GrokParts(Guid session, string prompt,
        string answer, DateTimeOffset timestamp)
    {
        var normalizer = new GrokTranscriptNormalizer();
        var path = Path.Combine(AppContext.BaseDirectory, "Agents", "Fixtures", "card0584", "grok-turn.jsonl");
        var eventId = Guid.NewGuid().ToString("N");
        var rows = File.ReadAllLines(path).Select(line => line
            .Replace("\"__PROMPT__\"", JsonSerializer.Serialize(prompt), StringComparison.Ordinal)
            .Replace("\"__ANSWER__\"", JsonSerializer.Serialize(answer), StringComparison.Ordinal)
            .Replace("__SESSION__", session.ToString("D"), StringComparison.Ordinal)
            .Replace("__EVENT__", eventId, StringComparison.Ordinal)
            .Replace("\"timestamp\":0", $"\"timestamp\":{timestamp.ToUnixTimeSeconds()}", StringComparison.Ordinal)
            .Replace("\"agentTimestampMs\":0", $"\"agentTimestampMs\":{timestamp.ToUnixTimeMilliseconds()}", StringComparison.Ordinal));
        return rows.SelectMany(normalizer.Normalize).Concat(normalizer.FlushPending()).ToList();
    }

    private static async Task<long> ReplayAsync(BridgeQueueHarness h, string prompt,
        string answer = "Deployment verified.", DateTimeOffset? timestamp = null, bool nativeTimestamp = true)
    {
        var baseline = await h.CurrentTranscriptMaxSequenceAsync();
        var parts = GrokParts(h.SessionId, prompt, answer, timestamp ?? DateTimeOffset.UtcNow);
        var entries = parts.Select((p, index) => new SessionRunnerTranscriptEvent(
            h.SessionId, baseline + index + 1, p.Kind, p.Uuid, p.ParentUuid, nativeTimestamp ? p.Timestamp : null,
            p.Role, p.Text, p.ToolName, p.ToolInput, p.ToolUseId, p.ToolIsError, p.StopReason)).ToList();
        h.Runner.SetTranscript(new(h.SessionId, entries, entries[^1].Sequence));
        await h.Runtime.SyncTranscriptAsync(h.SessionId, Ct);
        return baseline + 1;
    }

    private static async Task AgeAsync(BridgeQueueHarness h, Guid id)
    {
        await using var db = Db(h);
        await db.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u =>
            u.SetProperty(m => m.SentAt, DateTime.UtcNow.AddHours(-2)));
    }

    [Test]
    public async Task C584_Red_JoinedGrokReply()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        var settings = h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>();
        var debouncer = new ChannelInboundDebouncer(settings, TimeProvider.System,
            NullLogger<ChannelInboundDebouncer>.Instance);
        var bridge = new ChannelBridgeService(h.Messaging, h.Queue, debouncer, h.EventBus,
            h.Provider.GetRequiredService<IServiceScopeFactory>(), settings, TimeProvider.System,
            NullLogger<ChannelBridgeService>.Instance);
        await bridge.HandleInboundAsync(new Antiphon.Messaging.ChannelMessage
        {
            Id = Guid.NewGuid().ToString("N"), Channel = "telegram", ChannelMessageId = "c584-inbound",
            Conversation = new() { Id = chat, Kind = Antiphon.Messaging.ConversationKind.Direct },
            Author = new() { Id = "test-operator", DisplayName = "Tester" },
            Timestamp = DateTimeOffset.UtcNow, Text = "please deploy\nthe latest build and verify", ReplyHandle = chat,
            Raw = JsonDocument.Parse("{}").RootElement.Clone(),
        }, Ct);
        await using var ingressDb = Db(h);
        var id = await ingressDb.SessionQueuedMessages.Where(m => m.AgentSessionId == h.SessionId
            && m.Origin == QueuedMessageOrigin.Channel).Select(m => m.Id).SingleAsync();
        var row = await RowAsync(h, id);
        var joined = row.Body.Replace("\n", "", StringComparison.Ordinal);
        joined.ShouldContain("deploythe");
        await ReplayAsync(h, joined);
        var dispatcher = Dispatcher(h);
        await dispatcher.OnTurnEndAsync(h.SessionId, Ct);
        h.Messaging.SentReplies.ShouldHaveSingleItem().ConversationId.ShouldBe(chat);
        (await RowAsync(h, id)).ChannelReplySettledAt.ShouldNotBeNull();
        await AgeAsync(h, id);
        await dispatcher.SweepStaleCorrelationsAsync(Ct);
        h.Messaging.SentReplies.Count.ShouldBe(1);
    }

    [Test]
    public async Task C584_Red_JoinedGrokTtl()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        var id = await EnqueueAsync(h, chat, "please deploy\nthe latest build and verify");
        // Remove the fake's initial receipt so only the joined ACP receipt is available.
        await using var db = Db(h);
        await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId).ExecuteDeleteAsync();
        var seq = await ReplayAsync(h, (await RowAsync(h, id)).Body.Replace("\n", ""));
        await AgeAsync(h, id);
        await Dispatcher(h).SweepStaleCorrelationsAsync(Ct);
        var incident = await db.AgentIncidents.SingleAsync(i => i.AgentId == h.AgentId
            && i.Kind == AgentIncidentKind.ChannelReplyLost);
        incident.FailureReason.ShouldBe("TurnUnmatched");
        incident.Message.ShouldContain($"prompt seq {seq}");
        incident.Message.ShouldContain("20 chars"); // Literal answer's character count.
    }

    [Test]
    public async Task C584_Red_CommonHeadDifferentTail()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        var head = Envelope + new string('x', 160);
        var blue = await h.SeedChannelCorrelationAsync(head + " deploy blue", $"telegram:{chat}");
        var green = await h.SeedChannelCorrelationAsync(head + " deploy green", $"telegram:{chat}");
        await ReplayAsync(h, head + " deploy blue");
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        (await RowAsync(h, green)).ChannelReplySettledAt.ShouldBeNull();
        (await RowAsync(h, blue)).ChannelReplySettledAt.ShouldNotBeNull();
        h.Messaging.SentReplies.ShouldHaveSingleItem();
    }

    [Test]
    public async Task C584_Red_PreAttemptPrompt()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        const string body = Envelope + "please deploy the latest build and verify";
        await ReplayAsync(h, body, "An old answer.");
        var id = await h.SeedPendingMessageAsync(body, deliveryAttempts: 1,
            baselineSequence: await h.CurrentTranscriptMaxSequenceAsync(),
            origin: QueuedMessageOrigin.Channel, status: QueuedMessageStatus.Sent,
            lastDeliveryStartedAt: DateTime.UtcNow, conversationKey: $"telegram:{chat}");
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        (await RowAsync(h, id)).ChannelReplySettledAt.ShouldBeNull();
        h.Messaging.SentReplies.ShouldBeEmpty();
    }

    [Test]
    public async Task C584_WhitespaceCollision()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        var a = await EnqueueAsync(h, chat, "instruction: ab cd");
        var b = await EnqueueAsync(h, chat, "instruction: a bcd");
        var first = await RowAsync(h, a);
        var second = await RowAsync(h, b);
        ChannelPromptCorrelation.OpeningMarker(first.Body).ShouldNotBe(ChannelPromptCorrelation.OpeningMarker(second.Body));
        var flattened = first.Body.Replace(" ", "");
        var marker = ChannelPromptCorrelation.OpeningMarker(first.Body)!;
        foreach (var wrong in new[] { flattened.Replace(marker, ""), flattened.Replace(marker, $"[antiphon-channel:{Guid.NewGuid():N}]") })
        {
            await ReplayAsync(h, wrong);
            await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
            h.Messaging.SentReplies.ShouldBeEmpty();
        }
        await ReplayAsync(h, flattened);
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        h.Messaging.SentReplies.ShouldHaveSingleItem();
        (await RowAsync(h, a)).ChannelReplySettledAt.ShouldNotBeNull();
        (await RowAsync(h, b)).ChannelReplySettledAt.ShouldBeNull();
    }

    [Test]
    public async Task C584_AttemptFloorsAndLateConfirm()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        var id = await EnqueueAsync(h, chat, "please deploy\nthe current application", deliver: false);
        await using var db = Db(h);
        var started = DateTime.UtcNow.AddMinutes(-1);
        // A parked, unobservable attempt remains eligible for evidence, never automatic retyping.
        await db.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(m => m.DeliveryAttempts, 3).SetProperty(m => m.LastDeliveryStartedAt, started)
            .SetProperty(m => m.LastDeliveryGeneration, started.AddMinutes(-1)));
        var body = (await RowAsync(h, id)).Body.Replace("\n", "");
        await ReplayAsync(h, body, timestamp: DateTimeOffset.UtcNow.AddMinutes(-5));
        (await RowAsync(h, id)).Status.ShouldBe(QueuedMessageStatus.Pending);
        await ReplayAsync(h, body, nativeTimestamp: false);
        (await RowAsync(h, id)).Status.ShouldBe(QueuedMessageStatus.Pending);
        h.Adapter.Inputs.ShouldBeEmpty();
        // A standing resume changes current generation, not the accepted generation of this receipt.
        await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u =>
            u.SetProperty(s => s.StartedAt, DateTime.UtcNow));
        var promptTime = DateTimeOffset.UtcNow.AddSeconds(-10);
        await ReplayAsync(h, body, timestamp: promptTime);
        var confirmed = await RowAsync(h, id);
        confirmed.Status.ShouldBe(QueuedMessageStatus.Sent);
        confirmed.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        confirmed.SentAt!.Value.ShouldBeGreaterThan(promptTime.UtcDateTime);
        h.Adapter.Inputs.ShouldBeEmpty();
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        h.Messaging.SentReplies.ShouldHaveSingleItem();
    }

    [Test]
    public async Task C584_InlineBatch()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        var a = await EnqueueAsync(h, chat, "Please discuss [task deadbeef done]", deliver: false);
        var b = await EnqueueAsync(h, chat, "and [check deadbeef #1] please deploy\nthe build", deliver: false);
        await h.Queue.OnTurnEndAsync(h.SessionId, Ct);
        var typed = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        typed.ShouldContain($"[antiphon-channel:{a:N}]");
        typed.ShouldContain($"[antiphon-channel:{b:N}]");
        // Owed elsewhere, with a genuine earlier attempt; it must not settle with this batch.
        var other = await h.SeedPendingMessageAsync(Envelope + "unrelated third message", deliveryAttempts: 1,
            baselineSequence: 0, origin: QueuedMessageOrigin.Channel, status: QueuedMessageStatus.Sent,
            deliveryVerdict: DeliveryVerdict.Delivered, conversationKey: $"telegram:{chat}");
        var machine = await SeedMachineAsync(h, "[task deadbeef done]");
        await ReplayAsync(h, typed.Replace("\n", ""));
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        h.Messaging.SentReplies.ShouldHaveSingleItem();
        (await RowAsync(h, a)).ChannelReplySettledAt.ShouldNotBeNull();
        (await RowAsync(h, b)).ChannelReplySettledAt.ShouldNotBeNull();
        (await RowAsync(h, other)).ChannelReplySettledAt.ShouldBeNull();
        (await RowAsync(h, machine)).ChannelReplySettledAt.ShouldBeNull("channel input owns the quoted header");
    }

    [Test]
    public async Task C584_SpilledBatchAndRetry()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        await h.InsertTurnAsync("observable prior prompt", "prior answer");
        var a = await EnqueueAsync(h, chat, "first\n" + new string('a', 1100), deliver: false);
        var b = await EnqueueAsync(h, chat, "last\n" + new string('b', 1100), deliver: false);
        var originalA = (await RowAsync(h, a)).Body;
        var originalB = (await RowAsync(h, b)).Body;
        h.Adapter.OnSubmitted = _ => Task.CompletedTask; // submitted, receipt temporarily unavailable
        await h.Queue.OnTurnEndAsync(h.SessionId, Ct);
        var pointer = (await RowAsync(h, a)).Body;
        pointer.ShouldStartWith($"[antiphon-channel:{a:N}] {Envelope.TrimEnd()}");
        pointer.ShouldContain(TypedBodySpill.PointerHeadline);
        (await RowAsync(h, b)).Body.ShouldBe(pointer);
        (await RowAsync(h, a)).Status.ShouldBe(QueuedMessageStatus.Pending);
        var folder = Path.Combine(h.TempRoot, "workspace", ".antiphon", "inbox");
        var path = Directory.GetFiles(folder).ShouldHaveSingleItem();
        var content = File.ReadAllText(path);
        content.ShouldBe(ChannelPromptFormat.FormatBatch([originalA], originalB));
        System.Text.Encoding.UTF8.GetByteCount(pointer).ShouldBeLessThanOrEqualTo(1024);

        await using var restarted = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, AttachSessionId = h.SessionId, AttachAgentId = h.AgentId,
            ConnectionString = h.ConnectionString,
            Bridge = new() { Enabled = true, DebounceWindowMs = 0 },
            ConfigureServices = services => services.RemoveAll<ChannelReplyDispatcher>(),
        });
        await restarted.Queue.OnTurnEndAsync(h.SessionId, Ct);
        restarted.Adapter.SubmittedBodies.ShouldBe([pointer]);
        (await RowAsync(h, a)).Body.ShouldBe(pointer);
        (await RowAsync(h, b)).Body.ShouldBe(pointer);
        File.ReadAllText(path).ShouldBe(content);
        Directory.GetFiles(folder).Length.ShouldBe(1);
        await ReplayAsync(restarted, pointer.ReplaceLineEndings("\n").Replace("\n", ""));
        await Dispatcher(restarted).OnTurnEndAsync(h.SessionId, Ct);
        restarted.Messaging.SentReplies.ShouldHaveSingleItem().ConversationId.ShouldBe(chat);
        (await RowAsync(h, a)).ChannelReplySettledAt.ShouldNotBeNull();
        (await RowAsync(h, b)).ChannelReplySettledAt.ShouldNotBeNull();
        var courier = new RemoteSpillCourier();
        await using var remote = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false,
            Bridge = new() { Enabled = true, DebounceWindowMs = 0 },
            ConfigureServices = services =>
            {
                services.RemoveAll<ChannelReplyDispatcher>();
                services.AddSingleton(courier);
            },
        });
        courier.UseScopeFactory(remote.Provider.GetRequiredService<IServiceScopeFactory>());
        var remoteChat = await remote.BindChannelAsync();
        await using var remoteDb = Db(remote);
        var runnerCwd = Path.Combine(remote.TempRoot, "runner-workspace");
        await remoteDb.AgentSessions.Where(s => s.Id == remote.SessionId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.RunnerId, "fixture-runner")
            .SetProperty(s => s.RunnerStoreId, Guid.NewGuid()).SetProperty(s => s.RunnerCwd, runnerCwd));
        var source = Envelope + "remote\n" + new string('r', 2000);
        var stagedPointer = await remote.Queue.SpillQueueBodyAsync(remote.SessionId, source,
            "producer-stage", Envelope.TrimEnd(), remoteDb, Ct);
        courier.IsStaged(remote.SessionId).ShouldBeTrue();
        Guid remoteId = default;
        await remote.Queue.EnqueueAsync(remote.SessionId, stagedPointer, MessageSendMode.WhenIdle, Ct,
            origin: QueuedMessageOrigin.Channel, conversationKey: $"telegram:{remoteChat}",
            onCreated: id => remoteId = id, deliverIfIdle: false);
        var owned = await RowAsync(remote, remoteId);
        owned.Body.ShouldStartWith($"[antiphon-channel:{remoteId:N}] {Envelope.TrimEnd()}");
        owned.Body.ShouldContain(TypedBodySpill.InboxRelativePath(remoteId.ToString("D")));
        owned.RemoteSpillBody.ShouldBe(source);
        courier.IsStaged(remote.SessionId).ShouldBeFalse("binding acknowledged the exact staged bytes");
        await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ValidationException>(() =>
            remote.Queue.SpillQueueBodyAsync(remote.SessionId, owned.Body + new string('x', 1024),
                remoteId.ToString("D"), Envelope.TrimEnd(), remoteDb, Ct));
        (await RowAsync(remote, remoteId)).RemoteSpillBody.ShouldBe(source);
        var receivedBytes = "";
        remote.Adapter.OnSubmitted = async submitted =>
        {
            var delivery = await courier.FindDurableAsync(remote.SessionId, submitted, Ct);
            delivery.ShouldNotBeNull();
            receivedBytes = delivery.Spill.Body;
            delivery.Spill.MessageId.ShouldBe(remoteId);
            await remote.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow);
            await remote.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        };
        await remote.Queue.SendNowAsync(remote.SessionId, remoteId, Ct);
        receivedBytes.ShouldBe(source);
        remote.Adapter.SubmittedBodies.ShouldBe([owned.Body]);
        (await RowAsync(remote, remoteId)).Body.ShouldBe(owned.Body);
        (await RowAsync(remote, remoteId)).RemoteSpillBody.ShouldBeNull("complete receipt releases held bytes");
        Directory.Exists(Path.Combine(remote.TempRoot, "workspace", ".antiphon", "inbox")).ShouldBeFalse();
        await ReplayAsync(remote, owned.Body.ReplaceLineEndings("\n").Replace("\n", ""));
        await Dispatcher(remote).OnTurnEndAsync(remote.SessionId, Ct);
        remote.Messaging.SentReplies.ShouldHaveSingleItem().ConversationId.ShouldBe(remoteChat);

    }

    [Test]
    public async Task C584_RestartAndProducerFailure()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        var id = await EnqueueAsync(h, chat, "please deploy\nthe latest build and verify");
        var body = (await RowAsync(h, id)).Body;
        await ReplayAsync(h, body.Replace("\n", ""), "Done. " + ChannelPromptCorrelation.OpeningMarker(body));
        await Dispatcher(h, new FailingProducer()).OnTurnEndAsync(h.SessionId, Ct);
        (await RowAsync(h, id)).ChannelReplySettledAt.ShouldBeNull();
        h.Messaging.SentReplies.ShouldBeEmpty();
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        h.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("Done.");
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        h.Messaging.SentReplies.Count.ShouldBe(1);
    }

    [Test]
    public async Task C584_LegacyCompatibility()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        const string legacy = Envelope + "please deploy\nthe old application";
        var old = await h.SeedChannelCorrelationAsync(legacy, $"telegram:{chat}");
        // Exercise the actual old-row SentAt fallback, without inventing baseline/generation.
        await using var db = Db(h);
        await db.SessionQueuedMessages.Where(m => m.Id == old).ExecuteUpdateAsync(u => u
            .SetProperty(m => m.LastDeliveryStartedAt, (DateTime?)null)
            .SetProperty(m => m.LastDeliveryBaselineSequence, (long?)null));
        await ReplayAsync(h, legacy);
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        (await RowAsync(h, old)).ChannelReplySettledAt.ShouldNotBeNull();
        var pending = await h.SeedPendingMessageAsync(Envelope + "fresh legacy row", origin: QueuedMessageOrigin.Channel,
            conversationKey: $"telegram:{chat}");
        await h.Queue.SendNowAsync(h.SessionId, pending, Ct);
        (await RowAsync(h, pending)).Body.ShouldStartWith($"[antiphon-channel:{pending:N}]");
        var joined = await h.SeedChannelCorrelationAsync(legacy, $"telegram:{chat}");
        await ReplayAsync(h, legacy.Replace("\n", ""));
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        (await RowAsync(h, joined)).ChannelReplySettledAt.ShouldBeNull();
        (await RowAsync(h, joined)).Body.ShouldBe(legacy);
        const string ambiguous = Envelope + "two indistinguishable historical requests";
        var first = await h.SeedChannelCorrelationAsync(ambiguous, $"telegram:{chat}");
        var second = await h.SeedChannelCorrelationAsync(ambiguous, $"telegram:{chat}");
        await ReplayAsync(h, ambiguous);
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        (await RowAsync(h, first)).ChannelReplySettledAt.ShouldBeNull();
        (await RowAsync(h, second)).ChannelReplySettledAt.ShouldBeNull();
        h.Messaging.SentReplies.Count.ShouldBe(1);
        var batchStart = DateTime.UtcNow;
        var batchFloor = await h.CurrentTranscriptMaxSequenceAsync();
        const string legacyA = Envelope + "legacy delivered batch member A";
        const string legacyB = Envelope + "legacy delivered batch member B";
        var batchA = await h.SeedPendingMessageAsync(legacyA, deliveryAttempts: 1,
            baselineSequence: batchFloor, lastDeliveryStartedAt: batchStart,
            origin: QueuedMessageOrigin.Channel, status: QueuedMessageStatus.Sent, conversationKey: $"telegram:{chat}");
        var batchB = await h.SeedPendingMessageAsync(legacyB, deliveryAttempts: 1,
            baselineSequence: batchFloor, lastDeliveryStartedAt: batchStart,
            origin: QueuedMessageOrigin.Channel, status: QueuedMessageStatus.Sent, conversationKey: $"telegram:{chat}");
        await ReplayAsync(h, ChannelPromptFormat.FormatBatch([legacyA], legacyB));
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        (await RowAsync(h, batchA)).ChannelReplySettledAt.ShouldNotBeNull();
        (await RowAsync(h, batchB)).ChannelReplySettledAt.ShouldNotBeNull();
        h.Messaging.SentReplies.Count.ShouldBe(2);
        await h.Queue.EnqueueDeliveringNowAsync(h.SessionId, "tracked Channel input", Ct, QueuedMessageOrigin.Channel);
        var tracked = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == h.SessionId)
            .OrderByDescending(m => m.Sequence).FirstAsync();
        tracked.Body.ShouldBe($"[antiphon-channel:{tracked.Id:N}] tracked Channel input");
    }

    [Test]
    public async Task C584_TtlOwnTurnOnly()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        var id = await EnqueueAsync(h, chat, "this turn has no completion boundary");
        await using var db = Db(h);
        await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId).ExecuteDeleteAsync();
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, (await RowAsync(h, id)).Body, timestamp: DateTime.UtcNow);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "unfinished fragment");
        await ReplayAsync(h, "An unrelated operator prompt.", "An unrelated completed answer.");
        await AgeAsync(h, id);
        await Dispatcher(h).SweepStaleCorrelationsAsync(Ct);
        var incident = await db.AgentIncidents.SingleAsync(i => i.AgentId == h.AgentId
            && i.Kind == AgentIncidentKind.ChannelReplyLost);
        incident.FailureReason.ShouldBe("TurnIncomplete");
    }

    [Test]
    public async Task C584_ClippedAndMachineTurns()
    {
        await using var h = await HarnessAsync();
        var chat = await h.BindChannelAsync();
        var id = await EnqueueAsync(h, chat, new string('h', 210) + " REQUIRED-MIDDLE " + new string('t', 100));
        var body = (await RowAsync(h, id)).Body;
        foreach (var clipped in new[] { body[..210], body.Replace(" REQUIRED-MIDDLE ", "") })
        {
            await ReplayAsync(h, clipped);
            await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
            (await RowAsync(h, id)).ChannelReplySettledAt.ShouldBeNull();
            h.Messaging.SentReplies.ShouldBeEmpty();
        }
        var machine = await SeedMachineAsync(h, "[task deadbeef done] genuine later machine note");
        var old = await h.SeedChannelCorrelationAsync("[task deadbeef done] genuine later machine note", $"telegram:{chat}");
        await using var db = Db(h);
        // Same historical text, but an ineligible delivery window: no suppression by substring.
        await db.SessionQueuedMessages.Where(m => m.Id == old).ExecuteUpdateAsync(u => u
            .SetProperty(m => m.ChannelReplySettledAt, DateTime.UtcNow)
            .SetProperty(m => m.LastDeliveryBaselineSequence, long.MaxValue));
        await ReplayAsync(h, "[task deadbeef done] genuine later machine note", "Machine follow-up.");
        await Dispatcher(h).OnTurnEndAsync(h.SessionId, Ct);
        h.Messaging.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("Machine follow-up.");
        (await RowAsync(h, machine)).ChannelReplySettledAt.ShouldNotBeNull();
        (await RowAsync(h, id)).ChannelReplySettledAt.ShouldBeNull();
    }

    private static Task<Guid> SeedMachineAsync(BridgeQueueHarness h, string body) =>
        h.SeedPendingMessageAsync(body, deliveryAttempts: 1, origin: QueuedMessageOrigin.Delegation,
            status: QueuedMessageStatus.Sent, deliveryVerdict: DeliveryVerdict.Delivered,
            conversationKey: "task:deadbeef");

    private sealed class FailingProducer : IAntiphonMessagingProducer
    {
        public Task SendAsync(Antiphon.Messaging.ChannelReply reply, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("C584 producer failure"));
    }
}
