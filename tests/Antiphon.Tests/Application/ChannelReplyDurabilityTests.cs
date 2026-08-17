using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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

/// <summary>
/// CARD-0067: the reply route OUT of an agent is as durable as the message route IN, and a reply
/// that is lost is never lost silently.
///
/// <para>Live miss 2026-08-17. The Family agent was asked for a wedding guest list; the server was
/// hard-restarted at 09:05:01Z mid-conversation; the agent then wrote the list twice (1 849 and
/// 2 487 characters, ~42 then ~64 people) and neither reply was ever published. The messages coming
/// IN survived the restart because they are rows in Postgres. The routes back OUT did not, because
/// they lived in a <c>ConcurrentDictionary</c> in <c>ChannelReplyDispatcher</c> — and
/// <c>OnTurnEndAsync</c> returned SILENTLY on the resulting miss, so 47 minutes of silence produced
/// no log line, no incident and no user-visible signal anywhere.</para>
///
/// <para>"Restart" in these tests means what it means in production: a brand-new dispatcher instance
/// with an empty in-memory map, pointed at the same database.</para>
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ChannelReplyDurabilityTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static Task<BridgeQueueHarness> CreateHarnessAsync() =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
        });

    /// <summary>
    /// A fresh process: same database, same producer, EMPTY in-memory state. Everything the old
    /// design kept between "routed" and "turn ended" is gone here by construction.
    /// </summary>
    private static ChannelReplyDispatcher Restarted(BridgeQueueHarness h) => new(
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Messaging,
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
        h.Provider.GetRequiredService<TimeProvider>(),
        NullLogger<ChannelReplyDispatcher>.Instance);

    /// <summary>The one queued row this assertion is about — never a query over the shared table.</summary>
    private static async Task<Antiphon.Server.Domain.Entities.SessionQueuedMessage> RowAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    // THE regression test. Nothing in this test's setup survives in process memory: the correlation
    // is only ever a row, and the dispatcher that answers the turn has never seen the message routed.
    [Test]
    public async Task A_correlation_survives_a_process_restart_and_the_reply_is_published()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 08:59] Potencjalna lista gosci";
        var guestList = "Ola's guests: Anna, Marek, Kasia\n\nMichal's guests: Piotr, Zofia, Jan";

        var messageId = await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, guestList);

        // Restart. The old design lost the reply route here and answered into nothing, twice.
        var afterRestart = Restarted(h);
        await afterRestart.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.Channel.ShouldBe("telegram");
        reply.ConversationId.ShouldBe(chatId, "the target came from the persisted ConversationKey");
        reply.ReplyHandle.ShouldBe(chatId, "the addressing handle came from the channel catalog");
        reply.Text.ShouldBe(guestList);
        (await afterRestart.PendingCountAsync(h.SessionId)).ShouldBe(0);
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull(
            "the row is the correlation, so the row is what records that it was answered");
    }

    // The other half of durability, and the reason the settle marker is a column rather than nothing:
    // dispatch is re-triggered for the same turn all the time (a late AssistantText, the closing
    // TurnEnd, a reconnect's backfilled boundary). With a durable target and no durable consume
    // marker, every restart would re-answer the last turn — a duplicate into a real family chat.
    [Test]
    public async Task A_restarted_process_does_not_answer_an_already_answered_turn_twice()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 09:00] Make 2 columns";

        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ola | Michal");

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Count.ShouldBe(1);

        // Same turn, same transcript, brand-new process. Must be a no-op.
        await Restarted(h).OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await Restarted(h).OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1, "a restart must never re-send a reply to a human");
    }

    // The silence itself is the bug. A correlation that ages out unanswered used to be a Warning log
    // line about a channel id and nothing else; nothing recorded that a person had asked a question
    // and been ignored.
    [Test]
    public async Task An_unanswered_correlation_past_its_ttl_raises_a_critical_incident()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var conversationKey = $"telegram:{chatId}";

        // Delivered to the agent two hours ago; the default TTL is 30 minutes. Backdated rather than
        // driven by a fake clock: the harness's TimeProvider also paces the queue's delivery writes.
        var messageId = await h.SeedChannelCorrelationAsync(
            "[Telegram \"Family\" — Ola 07:00] did you book the dentist?",
            conversationKey,
            sentAtUtc: DateTime.UtcNow.AddHours(-2));

        // The global sweep, because a session that answers into a void may never end another turn.
        var abandoned = await h.Dispatcher.SweepStaleCorrelationsAsync(CancellationToken.None);
        abandoned.ShouldBeGreaterThanOrEqualTo(1, "a shared database means other rows may ride along");

        h.Messaging.SentReplies.ShouldBeEmpty("nothing to send — the agent never answered");
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull(
            "abandoning is terminal, or the incident would re-raise on every sweep");

        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Critical, "a human is waiting on this reply");
        incident.SessionId.ShouldBe(h.SessionId);
        incident.Message.ShouldContain(conversationKey);
        (await db.Alerts.AnyAsync(a => a.AgentId == h.AgentId))
            .ShouldBeTrue("the incident must reach the alert pipeline, not just the incident table");
    }

    // A second sweep must stay quiet: the loss is reported once, not once a minute forever.
    [Test]
    public async Task A_second_sweep_does_not_re_raise_the_same_loss()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();

        await h.SeedChannelCorrelationAsync(
            "[Telegram \"Family\" — Ola 07:05] and the car?",
            $"telegram:{chatId}",
            sentAtUtc: DateTime.UtcNow.AddHours(-2));

        await h.Dispatcher.SweepStaleCorrelationsAsync(CancellationToken.None);
        await h.Dispatcher.SweepStaleCorrelationsAsync(CancellationToken.None);

        await using var db = CreateContext();
        (await db.AgentIncidents.CountAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost))
            .ShouldBe(1);
    }

    // The turn WAS answered and there is nowhere to send it. That is a lost reply, not a skip, so it
    // settles with the incident instead of being retried into the void on every future turn.
    [Test]
    public async Task An_answered_turn_with_an_unroutable_conversation_key_raises_the_incident()
    {
        await using var h = await CreateHarnessAsync();
        await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 09:10] where are we eating";

        var messageId = await h.SeedChannelCorrelationAsync(prompt, "telegram-with-no-conversation");
        await h.InsertTurnAsync(prompt, "Booked for 19:00.");

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull();

        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        incident.FailureReason.ShouldBe("Unroutable");
    }

    // A correlation still inside its TTL that no turn has matched stays OWED — the turn-end miss must
    // not settle it, or a message the agent has not got round to answering would be written off.
    [Test]
    public async Task A_turn_that_matches_nothing_leaves_the_correlation_owed()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();

        var messageId = await h.SeedChannelCorrelationAsync(
            "[Telegram \"Family\" — Mike 09:20] remind me about the passports", $"telegram:{chatId}");

        // The operator types straight into the terminal while the chat message is still in flight.
        await h.InsertTurnAsync("run the tests please", "All green.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldBeNull();
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(1);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost))
            .ShouldBeFalse("a turn the bridge did not start is normal, not an incident");
    }
}
