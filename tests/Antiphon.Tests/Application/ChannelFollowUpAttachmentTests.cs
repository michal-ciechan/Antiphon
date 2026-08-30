using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0250: a follow-up attachment after channel-reply settlement must still reach the human.
///
/// Live miss 2026-08-30. A Slack user asked for a PDF; the PDF was written; the delegate's report
/// carried a correct <c>[[attach:]]</c>; the orchestrator re-emitted it on the <c>[task done]</c>
/// turn; Slack never received the file. The ack turn had already settled the only correlation, so
/// <c>OnTurnEndAsync</c> hit <c>open.Count == 0</c> and returned with no log line at all.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class ChannelFollowUpAttachmentTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static Task<BridgeQueueHarness> CreateHarnessAsync(
        Action<IServiceCollection>? configure = null) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
            ConfigureServices = configure,
        });

    /// <summary>
    /// A fresh process: same database, same producer, EMPTY in-memory state.
    /// </summary>
    private static ChannelReplyDispatcher Restarted(BridgeQueueHarness h) => new(
        h.Provider.GetRequiredService<IServiceScopeFactory>(),
        h.Messaging,
        h.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
        h.Provider.GetRequiredService<TimeProvider>(),
        NullLogger<ChannelReplyDispatcher>.Instance);

    private static async Task<SessionQueuedMessage> RowAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    // THE incident shape, red-first. Ack turn settles the only correlation; a later [task done]
    // turn re-emits [[attach:]]. Current code returns silently on open.Count == 0 and the file
    // never leaves. After the fix: a second reply to the same conversation carries the PDF.
    [Test]
    public async Task A_later_task_done_turn_with_attach_is_delivered_to_the_settled_conversation()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 10:00] send me the PDF";
        var pdfBytes = "%PDF-1.4 card-0250 invoice"u8.ToArray();
        var pdf = WriteFile(h, "invoice.pdf", pdfBytes);

        var channelRowId = await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "On it — a delegate is producing the PDF.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1, "the ack turn is the main-path reply");
        (await RowAsync(channelRowId)).ChannelReplySettledAt.ShouldNotBeNull();

        var note = "[task ab12cd34 done] PDF written.\n[[attach: " + pdf + "]]";
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, $"Here it is.\n[[attach: {pdf}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2, "the [task done] turn must follow-up the same conversation");
        var followUp = h.Messaging.SentReplies[1];
        followUp.Channel.ShouldBe("telegram");
        followUp.ConversationId.ShouldBe(chatId);
        followUp.ReplyHandle.ShouldBe(chatId);
        followUp.Text.ShouldBe("Here it is.");
        var attachment = followUp.Attachments.ShouldHaveSingleItem();
        attachment.Name.ShouldBe("invoice.pdf");
        attachment.Mime.ShouldBe("application/pdf");
        attachment.Content.ShouldBe(pdfBytes);
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldNotBeNull(
            "the Delegation row's ChannelReplySettledAt is the claim-before-produce marker");
        (await RowAsync(channelRowId)).ChannelReplySettledAt.ShouldNotBeNull(
            "the follow-up must not touch the already-settled Channel-origin row");
    }

    [Test]
    public async Task Re_running_the_same_turn_end_and_a_restart_do_not_double_send()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 10:05] the file please";
        var pdf = WriteFile(h, "report.pdf", "%PDF-1.4 once"u8.ToArray());

        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Working.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[task cd34ef56 done] attached";
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, $"[[attach: {pdf}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Count.ShouldBe(2);

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await Restarted(h).OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2, "claim-before-produce makes re-triggers a no-op");
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task A_produce_failure_un_claims_so_the_next_trigger_sends_once()
    {
        await using var h = await CreateHarnessAsync(services =>
        {
            services.AddSingleton(sp =>
                new ToggleFailProducer(sp.GetRequiredService<FakeAntiphonMessagingClient>()));
            services.AddSingleton<IAntiphonMessagingProducer>(sp =>
                sp.GetRequiredService<ToggleFailProducer>());
        });
        var fail = h.Provider.GetRequiredService<ToggleFailProducer>();

        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 10:10] PDF?";
        var pdf = WriteFile(h, "again.pdf", "%PDF-1.4 retry"u8.ToArray());

        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Soon.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        h.Messaging.SentReplies.Count.ShouldBe(1);

        var note = "[task 11223344 done] file ready";
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, $"[[attach: {pdf}]]");

        fail!.FailRemaining = 1;
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1, "the failed produce must not leave a reply recorded");
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldBeNull(
            "un-claim on produce failure, or the attachment is lost forever");

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2);
        h.Messaging.SentReplies[1].Attachments.ShouldHaveSingleItem().Name.ShouldBe("again.pdf");
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldNotBeNull();
    }

    [Test]
    public async Task An_operator_typed_turn_never_sends_and_raises_a_warning()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 10:15] still waiting";
        var pdf = WriteFile(h, "stray.pdf", "%PDF-1.4 stray"u8.ToArray());

        var channelRowId = await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(
            "run the tests please",
            $"All green.\n[[attach: {pdf}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty("an operator-typed turn must never follow-up a chat");
        (await RowAsync(channelRowId)).ChannelReplySettledAt.ShouldBeNull(
            "the owed Channel correlation is untouched");

        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelAttachmentsDropped)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.SessionId.ShouldBe(h.SessionId);
        incident.FailureReason.ShouldNotBeNull();
        incident.FailureReason.ShouldStartWith("UnmatchedHuman:");

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await using var db2 = CreateContext();
        (await db2.AgentIncidents.CountAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelAttachmentsDropped))
            .ShouldBe(1, "deduped per (session, owning-prompt sequence)");
    }

    [Test]
    public async Task A_plain_delegate_session_never_raises_the_dropped_incident()
    {
        await using var h = await CreateHarnessAsync();
        var pdf = WriteFile(h, "delegate.pdf", "%PDF-1.4 caller-only"u8.ToArray());
        var note = "[task deadbeef done] for the caller, not a chat";
        await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, $"[[attach: {pdf}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelAttachmentsDropped))
            .ShouldBeFalse("a delegate's own marker is input for its caller, not a send");
    }

    [Test]
    public async Task A_machine_turn_with_an_unsplittable_conversation_key_raises_critical()
    {
        await using var h = await CreateHarnessAsync();
        await h.BindChannelAsync();
        var pdf = WriteFile(h, "lost.pdf", "%PDF-1.4 unroutable"u8.ToArray());

        await h.SeedChannelCorrelationAsync(
            "[Telegram \"Family\" — Mike 10:20] anything",
            "not-a-key");
        var note = "[task cafe1234 done] file";
        await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, $"[[attach: {pdf}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.ShouldBeEmpty();
        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelAttachmentsDropped)
                .ToListAsync())
            .ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        incident.FailureReason.ShouldNotBeNull();
        incident.FailureReason.ShouldStartWith("Unroutable:");
    }

    [Test]
    [Arguments(QueuedMessageOrigin.Delegation)]
    [Arguments(QueuedMessageOrigin.Check)]
    [Arguments(QueuedMessageOrigin.System)]
    public async Task Check_and_system_injections_are_in_the_attach_gate(QueuedMessageOrigin origin)
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = $"[Telegram \"Family\" — Mike 10:25] {origin} file";
        var pdf = WriteFile(h, $"{origin}.pdf", "%PDF-1.4 origin"u8.ToArray());

        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = origin == QueuedMessageOrigin.Check
            ? "[check] the PDF is ready now"
            : origin == QueuedMessageOrigin.System
                ? "[System note from Antiphon: file ready]"
                : "[task 99aa88bb done] file ready";
        await SeedMachineInjectionAsync(h, note, origin);
        await h.InsertTurnAsync(note, $"[[attach: {pdf}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2, $"{origin} is a machine injection; the file must follow-up");
        h.Messaging.SentReplies[1].Attachments.ShouldHaveSingleItem().Name.ShouldBe($"{origin}.pdf");
    }

    [Test]
    public async Task NO_REPLY_plus_marker_sends_the_file_with_empty_text()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 10:30] quiet file";
        var pdf = WriteFile(h, "quiet.pdf", "%PDF-1.4 quiet"u8.ToArray());

        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[task 00ff00ff done] silent attach";
        await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, $"NO_REPLY\n[[attach: {pdf}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2);
        var followUp = h.Messaging.SentReplies[1];
        followUp.Text.ShouldBeNull("NO_REPLY remaining text still sends — the file IS the follow-up");
        followUp.Attachments.ShouldHaveSingleItem().Name.ShouldBe("quiet.pdf");
    }

    [Test]
    public async Task An_api_error_stub_in_the_window_sends_nothing_and_does_not_claim()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 10:35] file?";
        var pdf = WriteFile(h, "stub.pdf", "%PDF-1.4 stub"u8.ToArray());

        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[task stub0001 done] died";
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, note);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, $"[[attach: {pdf}]]");
        await h.InsertApiErrorStubAsync();
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1, "CARD-0071: an API-error stub withholds the whole turn");
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldBeNull(
            "a first trigger that cannot send must not claim — a resumed turn still can");
        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
                i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ChannelAttachmentsDropped))
            .ShouldBeFalse();
    }

    [Test]
    public async Task An_html_attachment_carries_text_html_and_is_not_called_document_delivery()
    {
        // Slack renders HTML as a text snippet regardless of MIME (CARD-0250). This test pins the
        // MIME map only — it is a correctness fix, never "the file was delivered as a document".
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 10:40] the page";
        var html = WriteFile(h, "page.html", "<html><body>hi</body></html>"u8.ToArray());

        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[task html0001 done] page";
        await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTurnAsync(note, $"[[attach: {html}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2);
        var attachment = h.Messaging.SentReplies[1].Attachments.ShouldHaveSingleItem();
        attachment.Mime.ShouldBe("text/html");
        attachment.Name.ShouldBe("page.html");
        attachment.Kind.ShouldBe(Antiphon.Messaging.AttachmentKind.File);
    }

    [Test]
    public async Task A_first_trigger_without_markers_does_not_claim_so_a_later_batch_still_sends()
    {
        await using var h = await CreateHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var prompt = "[Telegram \"Family\" — Mike 10:45] wait for the file";
        var pdf = WriteFile(h, "late.pdf", "%PDF-1.4 late"u8.ToArray());

        await h.SeedChannelCorrelationAsync(prompt, $"telegram:{chatId}");
        await h.InsertTurnAsync(prompt, "Ack.");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var note = "[task late0001 done] coming";
        var injectionId = await SeedMachineInjectionAsync(h, note, QueuedMessageOrigin.Delegation);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, note);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "Working on the file.");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(1);
        (await RowAsync(injectionId)).ChannelReplySettledAt.ShouldBeNull(
            "text without markers must not claim, or a later [[attach:]] in the same turn is lost");

        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.AssistantText, $"[[attach: {pdf}]]");
        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Messaging.SentReplies.Count.ShouldBe(2);
        h.Messaging.SentReplies[1].Attachments.ShouldHaveSingleItem().Name.ShouldBe("late.pdf");
    }

    private static string WriteFile(BridgeQueueHarness h, string name, byte[] bytes)
    {
        var path = Path.Combine(h.TempRoot, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static async Task<Guid> SeedMachineInjectionAsync(
        BridgeQueueHarness h, string body, QueuedMessageOrigin origin)
    {
        await using var db = CreateContext();
        var seq = ((await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == h.SessionId)
            .MaxAsync(m => (long?)m.Sequence)) ?? 0) + 1;
        var id = Guid.NewGuid();
        var sent = DateTime.UtcNow;
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = id,
            AgentSessionId = h.SessionId,
            Body = body,
            Status = QueuedMessageStatus.Sent,
            Sequence = seq,
            Origin = origin,
            CreatedAt = sent,
            SentAt = sent,
            DeliveryAttempts = 1,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private sealed class ToggleFailProducer : IAntiphonMessagingProducer
    {
        private readonly IAntiphonMessagingProducer _inner;

        public int FailRemaining;

        public ToggleFailProducer(IAntiphonMessagingProducer inner) => _inner = inner;

        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            if (FailRemaining > 0)
            {
                FailRemaining--;
                throw new InvalidOperationException("broker down (CARD-0250 produce-failure test)");
            }

            return _inner.SendAsync(reply, cancellationToken);
        }
    }
}
