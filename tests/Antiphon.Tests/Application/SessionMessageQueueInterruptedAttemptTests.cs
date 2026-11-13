using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
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

    // CARD-1892 D-1: the durable shape left by an uncertain launch Input. Scrollback
    // is not a composer reading. Each later flush must independently forbid another write.
    [Test]
    [Arguments(false, false, true)]
    [Arguments(false, false, false)]
    [Arguments(false, true, true)]
    [Arguments(false, true, false)]
    [Arguments(true, false, true)]
    [Arguments(true, false, false)]
    [Arguments(true, true, true)]
    [Arguments(true, true, false)]
    public async Task C1892_Composerless_unknown_attempt_waits_without_Enter_or_retyping(
        bool rulesRow, bool aged, bool headVisible)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConnectionString = schema.ConnectionString });
        var started = h.Now - (aged ? InterruptedAge + TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(1));
        var id = await h.SeedPendingMessageAsync(Body, deliveryAttempts: 1,
            status: QueuedMessageStatus.Sent, lastDeliveryStartedAt: started);
        await using (var setup = h.CreateObserverContext())
        {
            await setup.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.AgentKind, AgentKind.Grok)
                .SetProperty(s => s.GrokRulesState, rulesRow ? GrokRulesState.Pending : GrokRulesState.Ready));
            if (rulesRow)
                await setup.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u => u
                    .SetProperty(m => m.RulesRefreshKey, "c1892-rules-unknown"));
            (await setup.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId)).ShouldBe(0);
        }
        h.Adapter.RenderedScreenOverride = (headVisible ? Body + "\n" : "") + "Waiting for response";
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;

        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        await AssertUnknownAsync();
        await using (var deadline = h.CreateObserverContext())
            (await deadline.AgentIncidents.CountAsync(i => i.SessionId == h.SessionId
                && i.FailureReason == "input-delivery-unknown")).ShouldBe(aged ? 1 : 0);

        // The same live TUI later scrolls the head away, beyond even the discovery window.
        await using (var age = h.CreateObserverContext())
            await age.SessionQueuedMessages.Where(m => m.Id == id).ExecuteUpdateAsync(u => u
                .SetProperty(m => m.LastDeliveryStartedAt, h.Now - TimeSpan.FromMinutes(90)));
        h.Adapter.RenderedScreenOverride = "Waiting for response";
        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        await AssertUnknownAsync();
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        await AssertUnknownAsync();
        await using (var attention = h.CreateObserverContext())
        {
            var incidents = await attention.AgentIncidents.Where(i => i.SessionId == h.SessionId
                && i.FailureReason == "input-delivery-unknown").ToListAsync();
            incidents.Count.ShouldBe(1, "one durable attention note per unresolved attempt, including rules rows");
            incidents[0].Severity.ShouldBe(AlertSeverity.Warning);
            incidents[0].Message.ShouldContain("orchestrator decision");
            incidents[0].Message.ShouldNotContain(Body);
        }

        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, Body, timestamp: h.Now);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);
        await using var confirmed = h.CreateObserverContext();
        var row = await confirmed.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        row.DeliveryAttempts.ShouldBe(1);
        h.Adapter.Inputs.ShouldBeEmpty();

        async Task AssertUnknownAsync()
        {
            h.Adapter.Inputs.ShouldBeEmpty("later flush must send neither another body nor an Enter");
            h.Adapter.SubmittedBodies.ShouldBeEmpty();
            await using var db = h.CreateObserverContext();
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == id);
            row.Status.ShouldBe(QueuedMessageStatus.Sent);
            row.DeliveryVerdict.ShouldBeNull();
            row.DeliveryAttempts.ShouldBe(1);
            (await db.TranscriptEntries.CountAsync(t => t.AgentSessionId == h.SessionId)).ShouldBe(0);
            (await db.AgentSessions.SingleAsync(s => s.Id == h.SessionId)).Status.ShouldBe(SessionStatus.Running);
        }
    }

    [Test]
    public async Task Delivered_outcome_stamps_the_verdict()
    {
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync();

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
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync();
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
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync();
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
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync();
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
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync();
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
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync();
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
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync();
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
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync();
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
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync();
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
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync();
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
        await using var h = await ClaudeDialogFixture.CreateBridgeAsync(
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

    [Test]
    public async Task C514_Interrupted_maintenance_never_retypes_as_work()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        Guid id;
        await using (var db = h.CreateDb())
        {
            id = Guid.NewGuid();
            db.SessionQueuedMessages.Add(new Antiphon.Server.Domain.Entities.SessionQueuedMessage
            {
                Id = id,
                AgentSessionId = h.SessionId,
                Body = "/remote-control",
                Status = QueuedMessageStatus.Sent,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Supervision,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                SentAt = DateTime.UtcNow.AddMinutes(-10),
                DeliveryAttempts = 1,
                MaintenanceKind = RemoteControlMaintenanceKind.AutomaticArm,
                MaintenanceAcceptedStartedAt = h.Generation,
                MaintenanceResult = RemoteControlArmResult.ArmUnconfirmed,
            });
            await db.SaveChangesAsync();
        }

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        h.Adapter.Inputs.ShouldBeEmpty();
        h.Adapter.ConditionalInputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Interrupted_composer_Enter_waits_behind_modal()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.Adapter.RemoteControlMenuOpen = true;
        await h.Recovery.DetectAsync(
            h.SessionId, h.Generation,
            await h.Recovery.ObserveAsync(h.SessionId, h.Generation, CancellationToken.None),
            null, CancellationToken.None);
        h.Adapter.PrimeComposer("primed body waiting for enter");
        await h.Inner.SeedPendingMessageAsync(
            "primed body waiting for enter",
            deliveryAttempts: 1,
            deliveryVerdict: DeliveryVerdict.NoSubmitOutput,
            baselineSequence: 0);
        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task C514_Clearance_late_confirmation_precedes_redelivery()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        const string body = "matching complete prompt body c514";
        var id = await h.Inner.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: 0);
        await h.Inner.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);
        await h.Inner.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        h.Adapter.RemoteControlMenuOpen = true;
        var episode = await h.Recovery.DetectAsync(
            h.SessionId, h.Generation,
            await h.Recovery.ObserveAsync(h.SessionId, h.Generation, CancellationToken.None),
            null, CancellationToken.None);
        h.Adapter.RemoteControlMenuOpen = false;
        var sem = h.Queue.GetLock(h.SessionId);
        await sem.WaitAsync(CancellationToken.None);
        try
        {
            await h.Recovery.TryDismissIdleUnderLockAsync(h.SessionId, episode!.Id, CancellationToken.None);
        }
        finally
        {
            sem.Release();
        }

        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.Inputs.ShouldBeEmpty();
        await using var db = h.CreateDb();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
    }

    [Test]
    public async Task C514_Only_matching_complete_prompt_confirms_each_message()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        const string body = "alpha-prefix-THEN-tail-AAAA";
        var id = await h.Inner.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: 0);
        await h.Inner.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "alpha-prefix-THEN-tail-BBBB", timestamp: DateTime.UtcNow);
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        await using (var db = h.CreateDb())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            row.DeliveryVerdict.ShouldNotBe(DeliveryVerdict.Delivered);
        }

        await h.Inner.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);
        await h.Inner.MarkIdleAsync();
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        await using var verify = h.CreateDb();
        (await verify.SessionQueuedMessages.SingleAsync(m => m.Id == id))
            .DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
    }
}
