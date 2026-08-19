using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0025: Channel/UI bodies over the inbox single-write envelope spill to
/// <c>{cwd}/.antiphon/inbox/{id}.md</c> and the POINTER is what confirmation, truncation and
/// channel-reply matching see. Tests without a <c>PtyDeliveryProfile</c> get inbox ceilings
/// (1 024 B) — a ~2 000-byte body is over that and under modern 86 400 B, so a mistakenly-modern
/// profile would still fail the assertion.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SessionMessageQueueSpillTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static async Task<BridgeQueueHarness> ObservableHarnessAsync(
        ChannelBridgeSettings? bridge = null)
    {
        var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = bridge ?? new ChannelBridgeSettings { Enabled = true, DebounceWindowMs = 0 },
        });
        await h.InsertTurnAsync("an earlier prompt", "an earlier answer");
        return h;
    }

    private static string OversizedUiBody() =>
        "UI paste head — identity frame. " + new string('x', 2_000);

    private static string OversizedChannelBody(string envelope) =>
        envelope + " " + new string('y', 2_000);

    private static string WorkspaceOf(BridgeQueueHarness h) =>
        Path.Combine(h.TempRoot, "workspace");

    [Test]
    public async Task A_large_ui_whenidle_message_spills_and_the_row_stores_the_pointer()
    {
        await using var h = await ObservableHarnessAsync();
        var body = OversizedUiBody();
        Encoding.UTF8.GetByteCount(body).ShouldBeGreaterThan(1_024);

        var dto = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Messages.ShouldBeEmpty("idle session: delivered immediately");
        var typed = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        typed.ShouldContain(TypedBodySpill.PointerHeadline);
        typed.ShouldNotBe(body);
        PromptSubmissionMatch.IsConfirmedBy(typed, typed).ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn(typed, typed).ShouldBeTrue(
            "CARD-0024 must not fire Truncated on a successful spill — confirm against the pointer");

        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.Body.ShouldBe(typed, "the stored Body is the match key and must be what was typed");
        var spill = TypedBodySpill.InboxAbsolutePath(WorkspaceOf(h), row.Id.ToString("D"));
        File.Exists(spill).ShouldBeTrue();
        File.ReadAllText(spill).ShouldBe(body);

        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.OversizedTerminalDelivery))
            .ShouldBeFalse("a successful spill is not oversize");
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery))
            .ShouldBeFalse("the pointer completed against itself");
    }

    [Test]
    public async Task A_small_ui_body_is_typed_whole_with_no_spill_file()
    {
        await using var h = await ObservableHarnessAsync();
        const string body = "short UI note";

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe([body]);
        Directory.Exists(Path.Combine(WorkspaceOf(h), ".antiphon", "inbox")).ShouldBeFalse();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Body.ShouldBe(body);
        (await db.AgentIncidents.AnyAsync(i => i.AgentId == h.AgentId)).ShouldBeFalse();
    }

    [Test]
    public async Task A_large_channel_message_spills_and_the_pointer_still_settles_the_reply()
    {
        await using var h = await ObservableHarnessAsync();
        var chatId = await h.BindChannelAsync();
        var envelope = "[Telegram \"Family\" — Mike 10:00]";
        var body = OversizedChannelBody(envelope);
        const string reply = "here is the guest list: Anna, Marek";
        h.Adapter.OnSubmitted = async submitted =>
        {
            await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, submitted);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, reply);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        };

        await h.Queue.EnqueueAsync(
            h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Channel, conversationKey: $"telegram:{chatId}");

        var typed = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        typed.ShouldContain(TypedBodySpill.PointerHeadline);
        typed.ShouldStartWith(envelope);
        var probe = typed.Length <= 120 ? typed : typed[..120];
        typed.Contains(probe, StringComparison.Ordinal).ShouldBeTrue(
            "PromptsMatch: the stored/typed pointer's head is contained in the turn");

        await using (var db = CreateContext())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
            row.Body.ShouldBe(typed);
            row.Origin.ShouldBe(QueuedMessageOrigin.Channel);
            row.ConversationKey.ShouldBe($"telegram:{chatId}");
        }

        await h.Dispatcher.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var sent = h.Messaging.SentReplies.ShouldHaveSingleItem();
        sent.ConversationId.ShouldBe(chatId);
        sent.Text.ShouldBe(reply);
        await using var settled = CreateContext();
        (await settled.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .ChannelReplySettledAt.ShouldNotBeNull();
        (await settled.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery))
            .ShouldBeFalse();
    }

    [Test]
    public async Task Two_channel_rows_batched_over_the_ceiling_share_one_file_and_one_pointer()
    {
        await using var h = await ObservableHarnessAsync(new ChannelBridgeSettings
        {
            Enabled = true,
            BatchingEnabled = true,
            DebounceWindowMs = 0,
        });
        await h.MarkWorkingAsync();
        const string conv = "telegram:-100777";
        var a = OversizedChannelBody("[Telegram \"Family\" — Mike 10:00]");
        var b = OversizedChannelBody("[Telegram \"Family\" — Mike 10:01]");

        await h.Queue.EnqueueAsync(h.SessionId, a, MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Channel, conversationKey: conv);
        await h.Queue.EnqueueAsync(h.SessionId, b, MessageSendMode.WhenIdle, CancellationToken.None,
            origin: QueuedMessageOrigin.Channel, conversationKey: conv);

        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var typed = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        typed.ShouldContain(TypedBodySpill.PointerHeadline);
        typed.ShouldContain("[Telegram \"Family\" — Mike 10:01]",
            customMessage: "batch pointer is prefixed with the newest row's envelope");

        await using var db = CreateContext();
        var rows = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == h.SessionId)
            .OrderBy(m => m.Sequence)
            .ToListAsync();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(m => m.Status == QueuedMessageStatus.Sent);
        rows[0].Body.ShouldBe(typed);
        rows[1].Body.ShouldBe(typed, "every row in the run is rewritten to the same pointer");

        var spill = TypedBodySpill.InboxAbsolutePath(WorkspaceOf(h), rows[0].Id.ToString("D"));
        File.Exists(spill).ShouldBeTrue();
        var spilled = File.ReadAllText(spill);
        spilled.ShouldContain(a);
        spilled.ShouldContain(b);
        spilled.ShouldContain(ChannelPromptFormat.BatchCurrentMarker);
    }

    [Test]
    public async Task File_write_failure_types_the_original_and_raises_oversize()
    {
        await using var h = await ObservableHarnessAsync();
        var antiphon = Path.Combine(WorkspaceOf(h), ".antiphon");
        File.WriteAllText(antiphon, "blocker");
        var body = OversizedUiBody();

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe([body], "write failure falls back to typing the original");
        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.Body.ShouldBe(body);
        (await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.OversizedTerminalDelivery))
            .ShouldNotBeNull();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery))
            .ShouldBeFalse();
    }

    [Test]
    public async Task Now_mode_oversize_types_the_pointer_and_persists_no_queue_row()
    {
        await using var h = await ObservableHarnessAsync();
        var body = OversizedUiBody();

        var dto = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.Now, CancellationToken.None);

        dto.Messages.ShouldBeEmpty();
        var typed = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        typed.ShouldContain(TypedBodySpill.PointerHeadline);
        typed.ShouldNotBe(body);

        var inbox = Path.Combine(WorkspaceOf(h), ".antiphon", "inbox");
        Directory.Exists(inbox).ShouldBeTrue();
        var files = Directory.GetFiles(inbox, "now-*.md");
        files.ShouldHaveSingleItem();
        File.ReadAllText(files[0]).ShouldBe(body);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId))
            .ShouldBe(0, "Now-mode persists no row");
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.OversizedTerminalDelivery))
            .ShouldBeFalse();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery))
            .ShouldBeFalse();
    }

    [Test]
    public async Task A_small_now_mode_body_is_typed_whole_with_no_spill_file()
    {
        await using var h = await ObservableHarnessAsync();
        const string body = "send this now";

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.Now, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe([body]);
        Directory.Exists(Path.Combine(WorkspaceOf(h), ".antiphon", "inbox")).ShouldBeFalse();
    }
}
