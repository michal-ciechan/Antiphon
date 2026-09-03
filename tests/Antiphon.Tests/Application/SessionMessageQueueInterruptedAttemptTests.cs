using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0340 S3 / CARD-0342: a persisted delivery verdict plus the stranded-sweep recovery
/// (late-confirm, Enter-only if the body is still on screen, else revert) for an interrupted
/// Sent attempt and a Pending NoSubmitOutput.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SessionMessageQueueInterruptedAttemptTests
{
    private const string Body = "interrupted delivery body that is long enough to match";

    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static TimeSpan InterruptedAge =>
        TimeSpan.FromSeconds(3 + 3 + 30); // harness confirm + grace + unobservable tolerance

    [Test]
    public async Task Delivered_outcome_stamps_the_verdict()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();

        await h.Queue.EnqueueAsync(h.SessionId, Body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        message.DeliveryVerdictAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Fresh_typed_attempt_clears_a_stale_NoSubmitOutput()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        await h.SeedPendingMessageAsync(
            Body,
            deliveryAttempts: 1,
            deliveryVerdict: DeliveryVerdict.NoSubmitOutput,
            lastDeliveryStartedAt: DateTime.UtcNow - TimeSpan.FromMinutes(2));

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        message.DeliveryAttempts.ShouldBe(2);
        h.Adapter.Inputs.ShouldBe([Body, "\r"]);
    }

    [Test]
    public async Task Verdict_less_Sent_with_matching_UserPrompt_is_late_confirmed()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        var started = DateTime.UtcNow - InterruptedAge - TimeSpan.FromSeconds(5);
        await h.SeedPendingMessageAsync(
            Body,
            deliveryAttempts: 1,
            status: QueuedMessageStatus.Sent,
            lastDeliveryStartedAt: started);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, Body, timestamp: DateTime.UtcNow);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        message.DeliveryAttempts.ShouldBe(1);
        h.Adapter.Inputs.ShouldBeEmpty("late-confirm never writes");
    }

    [Test]
    public async Task Verdict_less_Sent_with_head_on_screen_sends_Enter_only()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        var started = DateTime.UtcNow - InterruptedAge - TimeSpan.FromSeconds(5);
        await h.SeedPendingMessageAsync(
            Body,
            deliveryAttempts: 1,
            status: QueuedMessageStatus.Sent,
            lastDeliveryStartedAt: started);
        h.Adapter.PrimeComposer(Body);

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["\r"], "CARD-0055: never re-type a body that is on screen");
        h.Adapter.SubmittedBodies.ShouldBe([Body]);
        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        message.DeliveryAttempts.ShouldBe(1, "Enter-only finishes the original attempt");
    }

    [Test]
    public async Task Verdict_less_Sent_with_nothing_on_screen_is_reverted_then_retyped()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        var started = DateTime.UtcNow - InterruptedAge - TimeSpan.FromSeconds(5);
        await h.SeedPendingMessageAsync(
            Body,
            deliveryAttempts: 1,
            status: QueuedMessageStatus.Sent,
            lastDeliveryStartedAt: started);

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        h.Adapter.Inputs.ShouldBe([Body, "\r"], "absent body is the one case that may re-type");
        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryAttempts.ShouldBe(2, "attempts were kept on revert, then the retype charged one");
        message.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
    }

    [Test]
    public async Task Verdict_less_Sent_outside_the_window_is_untouched()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        await h.SeedPendingMessageAsync(
            Body,
            deliveryAttempts: 1,
            status: QueuedMessageStatus.Sent,
            lastDeliveryStartedAt: DateTime.UtcNow - TimeSpan.FromMinutes(90));

        (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None))
            .ShouldBeGreaterThanOrEqualTo(0);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryVerdict.ShouldBeNull();
        message.DeliveryAttempts.ShouldBe(1);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Verdict_less_Sent_on_a_working_session_is_untouched()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        var started = DateTime.UtcNow - InterruptedAge - TimeSpan.FromSeconds(5);
        await h.SeedPendingMessageAsync(
            Body,
            deliveryAttempts: 1,
            status: QueuedMessageStatus.Sent,
            lastDeliveryStartedAt: started);
        await h.MarkWorkingAsync();

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryVerdict.ShouldBeNull();
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Pending_NoSubmitOutput_with_head_on_screen_sends_Enter_only()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        await h.SeedPendingMessageAsync(
            Body,
            deliveryAttempts: 1,
            origin: QueuedMessageOrigin.Delegation,
            deliveryVerdict: DeliveryVerdict.NoSubmitOutput,
            lastDeliveryStartedAt: DateTime.UtcNow - TimeSpan.FromSeconds(5));
        h.Adapter.PrimeComposer(Body);

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["\r"]);
        h.Adapter.SubmittedBodies.ShouldBe([Body]);
        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryVerdict.ShouldBe(DeliveryVerdict.Delivered);
        message.DeliveryAttempts.ShouldBe(1);
    }

    [Test]
    public async Task Pending_NoSubmitOutput_late_UserPrompt_wins_before_input()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        await h.SeedPendingMessageAsync(
            Body,
            deliveryAttempts: 1,
            origin: QueuedMessageOrigin.Delegation,
            deliveryVerdict: DeliveryVerdict.NoSubmitOutput,
            lastDeliveryStartedAt: DateTime.UtcNow - TimeSpan.FromSeconds(5));
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, Body, timestamp: DateTime.UtcNow);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        h.Adapter.PrimeComposer(Body);

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty("late transcript wins before Enter");
        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
    }

    [Test]
    public async Task Snapshot_unavailable_does_not_retype_or_revert()
    {
        await using var h = await BridgeQueueHarness.CreateAsync();
        var started = DateTime.UtcNow - InterruptedAge - TimeSpan.FromSeconds(5);
        await h.SeedPendingMessageAsync(
            Body,
            deliveryAttempts: 1,
            status: QueuedMessageStatus.Sent,
            lastDeliveryStartedAt: started);
        h.Adapter.ThrowOnRenderedSnapshot = true;

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty();
        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.DeliveryVerdict.ShouldBeNull();
    }

    [Test]
    public async Task Multi_row_batch_is_recovered_as_one_composed_body()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(
            new BridgeQueueHarness.HarnessOptions
            {
                Bridge = new Antiphon.Server.Application.Settings.ChannelBridgeSettings
                {
                    Enabled = true,
                    BatchingEnabled = true,
                },
            });

        var started = DateTime.UtcNow - InterruptedAge - TimeSpan.FromSeconds(5);
        const string first = "batch first body that is long enough";
        const string second = "batch second body that is long enough";
        await h.SeedPendingMessageAsync(
            first,
            deliveryAttempts: 1,
            origin: QueuedMessageOrigin.Delegation,
            status: QueuedMessageStatus.Sent,
            lastDeliveryStartedAt: started,
            conversationKey: "task:shared");
        await h.SeedPendingMessageAsync(
            second,
            deliveryAttempts: 1,
            origin: QueuedMessageOrigin.Delegation,
            status: QueuedMessageStatus.Sent,
            lastDeliveryStartedAt: started,
            conversationKey: "task:shared");
        var composed = ChannelPromptFormat.FormatBatch([first], second);
        h.Adapter.PrimeComposer(composed);

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["\r"], "the whole batch is one Enter, never a retype of one row");
        h.Adapter.SubmittedBodies.ShouldBe([composed]);
        await using var db = CreateContext();
        var rows = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == h.SessionId)
            .OrderBy(m => m.Sequence)
            .ToListAsync();
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(m => m.Status == QueuedMessageStatus.Sent);
        rows.ShouldAllBe(m => m.DeliveryVerdict == DeliveryVerdict.Delivered);
        rows.ShouldAllBe(m => m.DeliveryAttempts == 1);
    }
}
