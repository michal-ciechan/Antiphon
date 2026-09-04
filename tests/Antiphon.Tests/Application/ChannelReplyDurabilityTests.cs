using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private static SessionRunnerTranscriptDto LateConfirmBatch(
        Guid sessionId, string prompt, string response, bool isApiError = false) =>
        new(sessionId,
        [
            new SessionRunnerTranscriptEvent(
                sessionId, 1, TranscriptKinds.UserPrompt, "late-confirm-prompt", null, DateTimeOffset.UtcNow,
                "user", prompt, null, null, null, null, null),
            new SessionRunnerTranscriptEvent(
                sessionId, 2, TranscriptKinds.AssistantText, "late-confirm-answer", null, DateTimeOffset.UtcNow,
                "assistant", response, null, null, null, null, null, IsApiError: isApiError),
            new SessionRunnerTranscriptEvent(
                sessionId, 3, TranscriptKinds.TurnEnd, "late-confirm-end", null, DateTimeOffset.UtcNow,
                "assistant", null, null, null, null, null, TranscriptKinds.StopReasons.EndTurn,
                IsApiError: isApiError),
        ], 3);

    private sealed class FailingProducer : IAntiphonMessagingProducer
    {
        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("CARD-0313 deterministic producer failure"));
    }

    private sealed class ListLogger<T>(List<string> sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (sink)
                sink.Add(formatter(state, exception));
        }
    }

    [Test]
    public async Task A_runtime_batch_redispatches_a_late_confirmed_channel_reply_once()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        const string prompt = "[Telegram \"Family\" — Mike 10:10] Please send the real answer";
        const string answer = "The runtime batch reached the actual answer exactly once.";
        var baseline = await h.CurrentTranscriptMaxSequenceAsync();
        var messageId = await h.SeedPendingMessageAsync(
            prompt,
            deliveryAttempts: 1,
            baselineSequence: baseline,
            origin: QueuedMessageOrigin.Channel,
            conversationKey: $"telegram:{chatId}");
        h.Runner.SetTranscript(LateConfirmBatch(h.SessionId, prompt, answer));

        await h.Runtime.SyncTranscriptAsync(h.SessionId, CancellationToken.None);

        var reply = h.Messaging.SentReplies.ShouldHaveSingleItem();
        reply.ConversationId.ShouldBe(chatId);
        reply.Text.ShouldBe(answer);
        var row = await RowAsync(messageId);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        row.ChannelReplySettledAt.ShouldNotBeNull();
        await using var db = CreateContext();
        (await db.AgentIncidents.CountAsync(i =>
            i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost)).ShouldBe(0);
    }

    [Test]
    public async Task A_failed_late_confirm_recovery_warns_with_the_correlation_and_leaves_it_owed()
    {
        var logs = new List<string>();
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
            ConfigureServices = services =>
            {
                services.AddSingleton<IAntiphonMessagingProducer>(new FailingProducer());
                services.AddSingleton<ILogger<AgentSessionRuntime>>(
                    new ListLogger<AgentSessionRuntime>(logs));
            },
        });
        var chatId = await h.BindChannelAsync();
        var conversationKey = $"telegram:{chatId}";
        const string prompt = "[Telegram \"Family\" — Mike 10:11] Force the recovery producer failure";
        const string answer = "This answer must remain owed when publication fails.";
        var messageId = await h.SeedPendingMessageAsync(
            prompt,
            deliveryAttempts: 1,
            baselineSequence: await h.CurrentTranscriptMaxSequenceAsync(),
            origin: QueuedMessageOrigin.Channel,
            conversationKey: conversationKey);
        h.Runner.SetTranscript(LateConfirmBatch(h.SessionId, prompt, answer));

        await h.Runtime.SyncTranscriptAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        var row = await RowAsync(messageId);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        row.ChannelReplySettledAt.ShouldBeNull();
        logs.ShouldContain(log => log.Contains("Late-confirmed channel reply was not published after recovery dispatch", StringComparison.Ordinal)
            && log.Contains(h.SessionId.ToString(), StringComparison.Ordinal)
            && log.Contains(messageId.ToString(), StringComparison.Ordinal)
            && log.Contains(conversationKey, StringComparison.Ordinal)
            && log.Contains(ChannelReplyDispatchOutcome.PublicationFailed.ToString(), StringComparison.Ordinal));
    }

    [Test]
    public async Task A_late_confirmed_no_reply_turn_does_not_warn_as_an_unpublished_reply()
    {
        var logs = new List<string>();
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
            ConfigureServices = services => services.AddSingleton<ILogger<AgentSessionRuntime>>(
                new ListLogger<AgentSessionRuntime>(logs)),
        });
        var chatId = await h.BindChannelAsync();
        const string prompt = "[Telegram \"Family\" — Mike 10:12] Do not answer this turn";
        var messageId = await h.SeedPendingMessageAsync(
            prompt,
            deliveryAttempts: 1,
            baselineSequence: await h.CurrentTranscriptMaxSequenceAsync(),
            origin: QueuedMessageOrigin.Channel,
            conversationKey: $"telegram:{chatId}");
        h.Runner.SetTranscript(LateConfirmBatch(h.SessionId, prompt, "NO_REPLY"));

        await h.Runtime.SyncTranscriptAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull();
        logs.ShouldNotContain(log => log.Contains(
            "Late-confirmed channel reply was not published after recovery dispatch", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_late_confirmed_api_withheld_turn_does_not_warn_as_an_unpublished_reply()
    {
        var logs = new List<string>();
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
            ConfigureServices = services => services.AddSingleton<ILogger<AgentSessionRuntime>>(
                new ListLogger<AgentSessionRuntime>(logs)),
        });
        var chatId = await h.BindChannelAsync();
        const string prompt = "[Telegram \"Family\" — Mike 10:13] Simulate an API-withheld turn";
        var messageId = await h.SeedPendingMessageAsync(
            prompt,
            deliveryAttempts: 1,
            baselineSequence: await h.CurrentTranscriptMaxSequenceAsync(),
            origin: QueuedMessageOrigin.Channel,
            conversationKey: $"telegram:{chatId}");
        h.Runner.SetTranscript(LateConfirmBatch(
            h.SessionId, prompt, "API Error: 529 Overloaded", isApiError: true));

        await h.Runtime.SyncTranscriptAsync(h.SessionId, CancellationToken.None);

        (await RowAsync(messageId)).DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        logs.ShouldNotContain(log => log.Contains(
            "Late-confirmed channel reply was not published after recovery dispatch", StringComparison.Ordinal));
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

        var notice = h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).ShouldHaveSingleItem();
        notice.Text.ShouldNotBeNull();
        notice.Text.ShouldStartWith(ChannelReplyDispatcher.LostReplyNoticePrefix);
        notice.Text.ShouldContain("no turn matching the message completed");
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull(
            "abandoning is terminal, or the incident would re-raise on every sweep");

        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Critical, "a human is waiting on this reply");
        incident.SessionId.ShouldBe(h.SessionId);
        incident.FailureReason.ShouldBe("StaleTtl");
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

    // CARD-0071 (S2 of the usage-limit spec). A turn killed by the API writes its error string as
    // ordinary AssistantText, so before this guard "API Error: 529 Overloaded" was a publishable
    // reply — and because dispatch settles BEFORE producing, publishing it would also consume the
    // correlation and cancel the genuine answer forever. The stub turn must publish NOTHING and
    // leave the correlation owed for a resumed turn (or, failing that, the TTL sweep).
    [Test]
    public async Task A_turn_killed_by_an_api_error_publishes_nothing_and_stays_owed()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 09:30] what time is dinner";

        var messageId = await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertApiErrorStubAsync();

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty("an API error string must never reach a chat");
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldBeNull(
            "the correlation stays OWED — settling it would cancel the resumed turn's genuine answer");
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(1);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost))
            .ShouldBeFalse("withholding is not loss — the TTL sweep owns the give-up, and it has not expired");
    }

    // The spec's decision is to withhold the TURN, not to strip the stub line: a multi-call turn can
    // produce real text before a later API call dies, and publishing the fragment would settle the
    // correlation against half an answer.
    [Test]
    public async Task A_mixed_turn_with_real_text_beside_the_stub_is_withheld_whole()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 09:35] book the restaurant";

        var messageId = await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "Looking at their booking page now.");
        await h.InsertApiErrorStubAsync(
            errorText: "API Error: 529 Overloaded", apiErrorClass: "server_error", apiErrorStatus: 529);

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty(
            "half an answer would settle the correlation against an interim fragment");
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldBeNull();
    }

    // The follow-up path gathers trailing text for an already-answered turn; a stub landing there
    // must withhold the follow-up the same way (the spec names both gather sites).
    [Test]
    public async Task A_stub_in_the_trailing_window_withholds_the_follow_up()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 09:40] and the flowers?";

        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ordered — peonies, pickup Friday.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Count.ShouldBe(1, "the real answer goes out normally");

        // The turn keeps writing (stop marker mid-stream) and then dies on the API: real trailing
        // text AND the stub, with no new prompt in between.
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "One more thing about the vases —");
        await h.InsertApiErrorStubAsync(
            errorText: "API Error: 529 Overloaded", apiErrorClass: "server_error", apiErrorStatus: 529);
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1,
            "the follow-up window contains a stub, so the whole follow-up is withheld");
    }

    // Withholding leans on the TTL sweep as its give-up path, so prove the two compose: a stub-killed
    // turn that nothing ever resumes still ends in the Critical incident, never in silence.
    [Test]
    public async Task A_withheld_correlation_that_ages_out_still_raises_the_critical_incident()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var conversationKey = $"telegram:{chatId}";
        var prompt = "[Telegram \"Family\" — Ola 07:10] did you pay the deposit?";

        var messageId = await h.SeedChannelCorrelationAsync(
            prompt, conversationKey, sentAtUtc: DateTime.UtcNow.AddHours(-2));
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertApiErrorStubAsync();

        var abandoned = await h.Dispatcher.SweepStaleCorrelationsAsync(CancellationToken.None);
        abandoned.ShouldBeGreaterThanOrEqualTo(1, "a shared database means other rows may ride along");

        h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).ShouldHaveSingleItem()
            .Text.ShouldNotBeNull()
            .ShouldStartWith(ChannelReplyDispatcher.LostReplyNoticePrefix);
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull("abandoning is terminal");

        await using var db = CreateContext();
        (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost)
                .ToListAsync())
            .ShouldHaveSingleItem()
            .Severity.ShouldBe(AlertSeverity.Critical);
    }

    // CARD-0233 S3: three silences that used to share the StaleTtl lie "no turn matching the
    // message completed". The TTL sweep now inspects the transcript first.

    [Test]
    public async Task Ttl_with_no_matching_prompt_is_stale_ttl()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"AZ Care\" — Mike 21:03] Give me message to Phil";

        var messageId = await h.SeedChannelCorrelationAsync(
            prompt, $"telegram:{chatId}", sentAtUtc: DateTime.UtcNow.AddHours(-2));

        await h.Dispatcher.SweepStaleCorrelationsAsync(CancellationToken.None);

        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull();
        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.FailureReason.ShouldBe("StaleTtl");
        incident.Message.ShouldContain("no turn matching the message completed");
        h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).ShouldHaveSingleItem();
    }

    [Test]
    public async Task Ttl_with_a_matching_prompt_but_no_turn_end_is_turn_incomplete()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"AZ Care\" — Mike 21:03] Give me message to Phil";

        var messageId = await h.SeedChannelCorrelationAsync(
            prompt, $"telegram:{chatId}", sentAtUtc: DateTime.UtcNow.AddHours(-2));
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);

        await h.Dispatcher.SweepStaleCorrelationsAsync(CancellationToken.None);

        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull();
        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.FailureReason.ShouldBe("TurnIncomplete");
        incident.Message.ShouldContain("a matching prompt was recorded but no turn completed");
        incident.Severity.ShouldBe(AlertSeverity.Critical);
    }

    [Test]
    public async Task Ttl_with_a_completed_unmatched_turn_is_turn_unmatched()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"AZ Care\" — Mike Ciechan 21:03] Give me message to Phil asking for quote for all 3 certificates";
        const string hiPhil = "Hi Phil,\n\nCould you please quote for EPC, CP12 and EICR.\n\nThanks, Ola Zawojska";

        var messageId = await h.SeedChannelCorrelationAsync(
            prompt, $"telegram:{chatId}", sentAtUtc: DateTime.UtcNow.AddHours(-2));
        // Previous settled turn, then the incident shape: UserPrompt, in-turn bootstrap,
        // AssistantText, TurnEnd — dispatch is NEVER called (forced miss).
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        var promptSeq = await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.QueuedUserPrompt, ChannelPreamble.BootstrapBody);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, hiPhil);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        await h.Dispatcher.SweepStaleCorrelationsAsync(CancellationToken.None);

        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull();
        var notice = h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).ShouldHaveSingleItem();
        notice.Text.ShouldNotBeNull();
        notice.Text.ShouldStartWith(ChannelReplyDispatcher.LostReplyNoticePrefix);
        notice.Text.ShouldNotContain(hiPhil);

        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.FailureReason.ShouldBe("TurnUnmatched");
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        incident.Message.ShouldContain($"prompt seq {promptSeq}");
        incident.Message.ShouldContain("Give me message to Phil");
    }

    [Test]
    public async Task A_terminal_grok_402_sends_one_notice_settles_and_skips_ttl()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var conversationKey = $"telegram:{chatId}";
        var prompt = "[Telegram \"Family\" — Mike 10:00] what is the market doing";

        await using (var stamp = CreateContext())
        {
            await stamp.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.AgentKind, AgentKind.Grok)
                    .SetProperty(s => s.EffectiveModelId, "grok-4.6"));
        }

        var messageId = await h.SeedChannelCorrelationAsync(prompt, conversationKey);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd,
            "Grok Build usage balance exhausted",
            stopReason: TranscriptKinds.StopReasons.Error,
            isApiError: true,
            apiErrorClass: "payment_required",
            apiErrorStatus: 402);

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var notice = h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).ShouldHaveSingleItem();
        notice.Text.ShouldNotBeNull();
        notice.Text.ShouldContain("can't answer right now");
        notice.Text.ShouldContain("Grok");
        notice.Text.ShouldContain("HTTP 402");
        notice.Text.ShouldContain("Your message is kept");
        notice.Text.ShouldNotContain("Bearer");
        notice.ReplyHandle.ShouldBe(chatId);
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull();
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(0);

        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.FailureReason.ShouldBe("ProviderCapacity");
        incident.Severity.ShouldBe(AlertSeverity.Critical);

        var hold = await db.ModelAvailabilityHolds.SingleAsync(
            x => x.SourceSessionId == h.SessionId && x.ClearedAt == null);
        hold.Kind.ShouldBe(AgentKind.Grok);
        hold.ModelAlias.ShouldBe("grok-4.6");
        hold.DisabledUntil.ShouldNotBeNull();
        hold.DisabledUntil!.Value.ShouldBe(DateTime.UtcNow.AddHours(6), TimeSpan.FromSeconds(15));

        var beforeSweep = h.Messaging.SentReplies.Count;
        var abandoned = await h.Dispatcher.SweepStaleCorrelationsAsync(CancellationToken.None);
        _ = abandoned;
        h.Messaging.SentReplies.Skip(beforeSweep)
            .Where(r => r.ConversationId == chatId && r.Text != null
                && r.Text.StartsWith(ChannelReplyDispatcher.LostReplyNoticePrefix, StringComparison.Ordinal))
            .ShouldBeEmpty("TTL must not send the contradictory 'no turn completed' notice");

        await db.ModelAvailabilityHolds.Where(x => x.Id == hold.Id).ExecuteDeleteAsync();
    }

    [Test]
    public async Task A_capacity_notice_scrubs_bearer_tokens_and_urls()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 10:05] ping";

        await using (var stamp = CreateContext())
        {
            await stamp.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.AgentKind, AgentKind.Grok)
                    .SetProperty(s => s.EffectiveModelId, "grok-4.6"));
        }

        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, prompt);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd,
            "API error (status 402 Payment Required): usage balance exhausted Bearer xai-secretvalue https://api.x.ai/v1/fail",
            stopReason: TranscriptKinds.StopReasons.Error,
            isApiError: true,
            apiErrorClass: "payment_required",
            apiErrorStatus: 402);

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var notice = h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).ShouldHaveSingleItem();
        notice.Text.ShouldNotBeNull();
        notice.Text.ShouldNotContain("xai-secretvalue");
        notice.Text.ShouldNotContain("https://api.x.ai");
        notice.Text.ShouldNotContain("Bearer xai");

        await using var db = CreateContext();
        await db.ModelAvailabilityHolds
            .Where(x => x.SourceSessionId == h.SessionId)
            .ExecuteDeleteAsync();
    }

    private const string TransportPrompt = "[Telegram \"Family\" — Mike 11:00] ping the grok proxy";
    private const string TransportDetail =
        "error sending request for url (http://localhost:10746/v1/chat/completions)";
    private const string TransportText = TransportDetail + " [after 15 retries]";

    [Test]
    public async Task A_grok_transport_death_sends_the_error_now_settles_and_skips_ttl()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var conversationKey = $"telegram:{chatId}";
        await StampGrokAsync(h.SessionId);

        var messageId = await h.SeedChannelCorrelationAsync(TransportPrompt, conversationKey);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, TransportPrompt);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd,
            TransportText,
            stopReason: TranscriptKinds.StopReasons.Error,
            isApiError: true,
            apiErrorClass: TranscriptKinds.ApiErrorClasses.Transport,
            apiErrorStatus: null);

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        await AssertTransportNoticeAsync(h, chatId, messageId);
    }

    [Test]
    public async Task The_measured_transport_fixture_dispatches_the_error_through_the_runtime()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var conversationKey = $"telegram:{chatId}";
        await StampGrokAsync(h.SessionId);

        var messageId = await h.SeedChannelCorrelationAsync(TransportPrompt, conversationKey);
        h.Runner.SetTranscript(TransportFixtureBatch(h.SessionId));

        await h.Runtime.SyncTranscriptAsync(h.SessionId, CancellationToken.None);

        await AssertTransportNoticeAsync(h, chatId, messageId);
    }

    [Test]
    public async Task A_second_transport_death_on_the_same_session_notifies_again()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var conversationKey = $"telegram:{chatId}";
        await StampGrokAsync(h.SessionId);

        var firstId = await h.SeedChannelCorrelationAsync(TransportPrompt, conversationKey);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, TransportPrompt);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd,
            TransportText,
            stopReason: TranscriptKinds.StopReasons.Error,
            isApiError: true,
            apiErrorClass: TranscriptKinds.ApiErrorClasses.Transport);

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).Count().ShouldBe(1);
        (await RowAsync(firstId)).ChannelReplySettledAt.ShouldNotBeNull();

        const string secondPrompt = "[Telegram \"Family\" — Mike 11:05] try the grok proxy again";
        var secondId = await h.SeedChannelCorrelationAsync(secondPrompt, conversationKey);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, secondPrompt);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd,
            TransportText,
            stopReason: TranscriptKinds.StopReasons.Error,
            isApiError: true,
            apiErrorClass: TranscriptKinds.ApiErrorClasses.Transport);

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).Count().ShouldBe(2,
            "ApiErrorTurnDied dedup must not swallow the second channel notice");
        foreach (var notice in h.Messaging.SentReplies.Where(r => r.ConversationId == chatId))
        {
            notice.Text.ShouldNotBeNull();
            notice.Text.ShouldContain("couldn't reach");
        }

        (await RowAsync(secondId)).ChannelReplySettledAt.ShouldNotBeNull();

        await using var db = CreateContext();
        (await db.AgentIncidents.CountAsync(i =>
            i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost
            && i.FailureReason == "ProviderTransport")).ShouldBe(2);
        (await db.AgentIncidents.CountAsync(i =>
            i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ApiErrorTurnDied)).ShouldBe(1);
    }

    [Test]
    public async Task A_grok_transient_5xx_death_stays_owed_and_sends_nothing_at_withhold()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var conversationKey = $"telegram:{chatId}";
        await StampGrokAsync(h.SessionId);

        var messageId = await h.SeedChannelCorrelationAsync(TransportPrompt, conversationKey);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, TransportPrompt);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd,
            "The model is currently at capacity",
            stopReason: TranscriptKinds.StopReasons.Error,
            isApiError: true,
            apiErrorClass: "server_error",
            apiErrorStatus: 500);

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).ShouldBeEmpty();
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldBeNull();
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(1);

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(i =>
            i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost))
            .ShouldBeFalse();
        var recovery = await db.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
        recovery.Classification.ShouldBe(ApiErrorClassification.Transient);
        recovery.ResolvedAt.ShouldBeNull();
        recovery.NextAttemptAt.ShouldNotBeNull();
    }

    private static async Task StampGrokAsync(Guid sessionId)
    {
        await using var stamp = CreateContext();
        await stamp.AgentSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.AgentKind, AgentKind.Grok)
                .SetProperty(s => s.EffectiveModelId, "grok-4.6"));
    }

    private static async Task AssertTransportNoticeAsync(
        BridgeQueueHarness h, string chatId, Guid messageId)
    {
        var notice = h.Messaging.SentReplies.Where(r => r.ConversationId == chatId).ShouldHaveSingleItem();
        notice.Text.ShouldNotBeNull();
        notice.Text.ShouldContain("couldn't reach");
        notice.Text.ShouldContain("Grok");
        notice.Text.ShouldContain("after 15 attempts");
        notice.Text.ShouldNotContain("http://");
        notice.ReplyHandle.ShouldBe(chatId);
        (await RowAsync(messageId)).ChannelReplySettledAt.ShouldNotBeNull();
        (await h.Dispatcher.PendingCountAsync(h.SessionId)).ShouldBe(0);

        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelReplyLost)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.FailureReason.ShouldBe("ProviderTransport");
        incident.Severity.ShouldBe(AlertSeverity.Critical);

        var recovery = await db.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == h.SessionId);
        recovery.Classification.ShouldBe(ApiErrorClassification.Transient);
        recovery.NextAttemptAt.ShouldNotBeNull();

        var beforeSweep = h.Messaging.SentReplies.Count;
        _ = await h.Dispatcher.SweepStaleCorrelationsAsync(CancellationToken.None);
        h.Messaging.SentReplies.Skip(beforeSweep)
            .Where(r => r.ConversationId == chatId && r.Text != null
                && r.Text.StartsWith(ChannelReplyDispatcher.LostReplyNoticePrefix, StringComparison.Ordinal))
            .ShouldBeEmpty("TTL must not send a follow-up notice after a transport death");
    }

    private static SessionRunnerTranscriptDto TransportFixtureBatch(Guid sessionId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "grok-transport-error.jsonl");
        File.Exists(path).ShouldBeTrue($"fixture missing at {path}");
        var n = new GrokTranscriptNormalizer();
        var events = new List<SessionRunnerTranscriptEvent>();
        long seq = 0;
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;
            foreach (var p in n.Normalize(line))
            {
                events.Add(new SessionRunnerTranscriptEvent(
                    sessionId, ++seq, p.Kind, p.Uuid, p.ParentUuid, p.Timestamp,
                    p.Role, p.Text, p.ToolName, p.ToolInput, p.ToolUseId, p.ToolIsError, p.StopReason,
                    p.ApiCallId, p.InputTokens, p.OutputTokens, p.CacheReadTokens, p.CacheCreationTokens,
                    p.IsApiError, p.ApiErrorClass, p.ApiErrorStatus, p.Model, p.ModelCalls));
            }
        }

        return new(sessionId, events, seq);
    }
}
