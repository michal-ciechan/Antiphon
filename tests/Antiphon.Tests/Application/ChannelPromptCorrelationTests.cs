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
        var rows = File.ReadAllLines(path).Select(line => line
            .Replace("\"__PROMPT__\"", JsonSerializer.Serialize(prompt), StringComparison.Ordinal)
            .Replace("\"__ANSWER__\"", JsonSerializer.Serialize(answer), StringComparison.Ordinal)
            .Replace("__SESSION__", session.ToString("D"), StringComparison.Ordinal)
            .Replace("__EVENT__", Guid.NewGuid().ToString("N"), StringComparison.Ordinal)
            .Replace("\"timestamp\":0", $"\"timestamp\":{timestamp.ToUnixTimeSeconds()}", StringComparison.Ordinal)
            .Replace("\"agentTimestampMs\":0", $"\"agentTimestampMs\":{timestamp.ToUnixTimeMilliseconds()}", StringComparison.Ordinal));
        return rows.SelectMany(normalizer.Normalize).Concat(normalizer.FlushPending()).ToList();
    }

    private static async Task<long> ReplayAsync(BridgeQueueHarness h, string prompt,
        string answer = "Deployment verified.", DateTimeOffset? timestamp = null)
    {
        var baseline = await h.CurrentTranscriptMaxSequenceAsync();
        var parts = GrokParts(h.SessionId, prompt, answer, timestamp ?? DateTimeOffset.UtcNow);
        var entries = parts.Select((p, index) => new SessionRunnerTranscriptEvent(
            h.SessionId, baseline + index + 1, p.Kind, p.Uuid, p.ParentUuid, p.Timestamp,
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
        var id = await EnqueueAsync(h, chat, "please deploy\nthe latest build and verify");
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
        incident.Message.ShouldContain("21"); // Literal answer's character count.
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
}
