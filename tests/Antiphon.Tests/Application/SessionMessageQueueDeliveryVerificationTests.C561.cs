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

public partial class SessionMessageQueueDeliveryVerificationTests
{
    [Test]
    [Arguments(10, false)]
    [Arguments(30, false)]
    [Arguments(10, true)]
    public async Task C561_a_blind_matcher_verdict_withholds_the_kill_and_refunds_the_attempt(
        int sentAfterMarkSeconds, bool channelBound)
    {
        var clock = new AgentSupervisionTests.MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var h = await BlindHarnessAsync(clock);
        var t0 = clock.GetUtcNow().UtcDateTime;
        await h.Runtime.ObserveTranscriptAsync(PoisonToolCall(h.SessionId, 1), default);
        await h.InsertTurnAsync("earlier prompt", "earlier answer");
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        if (channelBound)
            await h.BindChannelAsync();
        var id = await h.SeedPendingMessageAsync(
            "the brief that must persist", deliveryAttempts: 1, baselineSequence: floor,
            status: QueuedMessageStatus.Sent, lastDeliveryStartedAt: t0.AddSeconds(sentAfterMarkSeconds));
        await h.Queue.HandleDeliveryFailureAsync(
            h.SessionId, [id], DeliveryVerdict.NoTranscriptRecord, default,
            capturedGeneration: await StartedAtAsync(h));

        h.Adapter.Killed.ShouldBeFalse();
        h.Adapter.KillGenerationCalls.ShouldBeEmpty();
        h.Adapter.Inputs.ShouldBeEmpty();
        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.SentAt.ShouldBeNull();
        row.DeliveryAttempts.ShouldBe(0);
        row.CanceledAt.ShouldBeNull();
        row.DeliveryVerdict.ShouldBe(DeliveryVerdict.NoTranscriptRecord);
        var incident = (await db.AgentIncidents.Where(i =>
                i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed)
            .ToListAsync()).ShouldHaveSingleItem();
        incident.Severity.ShouldBe(channelBound ? AlertSeverity.Error : AlertSeverity.Warning);
        incident.Message.ShouldContain("matcher was blind");
        incident.Message.ShouldContain("22001");
        incident.Message.ShouldContain("1 time(s)");
        incident.Message.ShouldContain("NOT restarted");
        incident.Message.ShouldNotContain("Restarting the session");
    }

    [Test]
    public async Task C561_a_persist_failure_older_than_the_attempt_does_not_excuse_the_verdict()
    {
        var clock = new AgentSupervisionTests.MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var h = await BlindHarnessAsync(clock);
        var t0 = clock.GetUtcNow().UtcDateTime;
        await h.Runtime.ObserveTranscriptAsync(PoisonToolCall(h.SessionId, 1), default);
        await h.InsertTurnAsync("earlier prompt", "earlier answer");
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        clock.Advance(TimeSpan.FromSeconds(31));
        var id = await h.SeedPendingMessageAsync(
            "the brief that must persist", deliveryAttempts: 1, baselineSequence: floor,
            status: QueuedMessageStatus.Sent, lastDeliveryStartedAt: t0.AddSeconds(31));
        await h.Queue.HandleDeliveryFailureAsync(
            h.SessionId, [id], DeliveryVerdict.NoTranscriptRecord, default,
            capturedGeneration: await StartedAtAsync(h));

        h.Adapter.Killed.ShouldBeTrue();
        h.Adapter.KillGenerationCalls.ShouldHaveSingleItem();
        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.DeliveryAttempts.ShouldBe(1);
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        var incident = await db.AgentIncidents.SingleAsync(i =>
            i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
        incident.Severity.ShouldBe(AlertSeverity.Error);
        incident.Message.ShouldContain("Restarting the session");
        incident.Message.ShouldNotContain("matcher was blind");
    }

    [Test]
    public async Task C561_a_no_composer_evidence_verdict_with_a_mark_keeps_the_card_0103_rules()
    {
        var clock = new AgentSupervisionTests.MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var h = await BlindHarnessAsync(clock);
        var t0 = clock.GetUtcNow().UtcDateTime;
        await h.Runtime.ObserveTranscriptAsync(PoisonToolCall(h.SessionId, 1), default);
        await h.InsertTurnAsync("earlier prompt", "earlier answer");
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        var id = await h.SeedPendingMessageAsync(
            "the brief that must persist", deliveryAttempts: 1, baselineSequence: floor,
            status: QueuedMessageStatus.Sent, lastDeliveryStartedAt: t0.AddSeconds(10));
        await h.Queue.HandleDeliveryFailureAsync(
            h.SessionId, [id], DeliveryVerdict.NoComposerEvidence, default,
            capturedGeneration: await StartedAtAsync(h));

        h.Adapter.Killed.ShouldBeTrue();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.Id == id)).DeliveryAttempts.ShouldBe(1);
        var incident = await db.AgentIncidents.SingleAsync(i =>
            i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
        incident.Severity.ShouldBe(AlertSeverity.Error);
        incident.Message.ShouldNotContain("matcher was blind");
    }

    [Test]
    public async Task C561_a_mixed_batch_takes_the_destructive_default()
    {
        var clock = new AgentSupervisionTests.MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var h = await BlindHarnessAsync(clock);
        var t0 = clock.GetUtcNow().UtcDateTime;
        await h.Runtime.ObserveTranscriptAsync(PoisonToolCall(h.SessionId, 1), default);
        await h.InsertTurnAsync("earlier prompt", "earlier answer");
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        var a = await h.SeedPendingMessageAsync(
            "first", deliveryAttempts: 1, baselineSequence: floor,
            status: QueuedMessageStatus.Sent, lastDeliveryStartedAt: t0.AddSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(45));
        var b = await h.SeedPendingMessageAsync(
            "second", deliveryAttempts: 1, baselineSequence: floor,
            status: QueuedMessageStatus.Sent, lastDeliveryStartedAt: t0.AddSeconds(45));
        await h.Queue.HandleDeliveryFailureAsync(
            h.SessionId, [a, b], DeliveryVerdict.NoTranscriptRecord, default,
            capturedGeneration: await StartedAtAsync(h));

        h.Adapter.Killed.ShouldBeTrue();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.FindAsync(a))!.DeliveryAttempts.ShouldBe(1);
        (await db.SessionQueuedMessages.FindAsync(b))!.DeliveryAttempts.ShouldBe(1);
        (await db.AgentIncidents.SingleAsync(i =>
            i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .Message.ShouldNotContain("matcher was blind");
    }

    [Test]
    [Arguments("recovered")]
    [Arguments("busy")]
    [Arguments("still-failing")]
    public async Task C561_after_a_refund_the_stranded_sweep_delivers_once_the_store_recovers(string shape)
    {
        var clock = new AgentSupervisionTests.MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var h = await BlindHarnessAsync(clock);
        var t0 = clock.GetUtcNow().UtcDateTime;
        await h.Runtime.ObserveTranscriptAsync(PoisonToolCall(h.SessionId, 1), default);
        await h.InsertTurnAsync("earlier prompt", "earlier answer");
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        var id = await h.SeedPendingMessageAsync(
            "the brief that must persist", deliveryAttempts: 1, baselineSequence: floor,
            status: QueuedMessageStatus.Sent, lastDeliveryStartedAt: t0.AddSeconds(10));
        await h.Queue.HandleDeliveryFailureAsync(
            h.SessionId, [id], DeliveryVerdict.NoTranscriptRecord, default,
            capturedGeneration: await StartedAtAsync(h));
        var preSweepMax = await h.CurrentTranscriptMaxSequenceAsync();

        if (shape == "busy")
        {
            await h.MarkWorkingAsync();
            await h.Queue.FlushStrandedQueuesAsync(default);
            h.Adapter.Inputs.ShouldBeEmpty();
            await using var busyDb = CreateContext();
            var busy = await busyDb.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            busy.Status.ShouldBe(QueuedMessageStatus.Pending);
            busy.DeliveryAttempts.ShouldBe(0);
            return;
        }

        if (shape == "still-failing")
        {
            h.Adapter.OnSubmitted = body => h.Runtime.ObserveTranscriptAsync(StubbedUserPrompt(h.SessionId, body), default);
            await h.Queue.FlushStrandedQueuesAsync(default);
            h.Adapter.Inputs.Count(i => i == "the brief that must persist").ShouldBe(1);
            await using var failDb = CreateContext();
            var failed = await failDb.SessionQueuedMessages.SingleAsync(m => m.Id == id);
            failed.Status.ShouldBe(QueuedMessageStatus.Pending);
            failed.DeliveryAttempts.ShouldBe(0);
            h.Adapter.Killed.ShouldBeFalse();
            (await failDb.AgentIncidents.CountAsync(i =>
                i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed)).ShouldBe(1);
            (await failDb.TranscriptEntries.SingleAsync(t =>
                    t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Sequence > preSweepMax))
                .Text!.StartsWith("[transcript text not persistable").ShouldBeTrue();
            return;
        }

        await h.Queue.FlushStrandedQueuesAsync(default);
        h.Adapter.Inputs.Count(i => i == "the brief that must persist").ShouldBe(1);
        await using var db = CreateContext();
        var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        row.Status.ShouldBe(QueuedMessageStatus.Sent);
        row.DeliveryAttempts.ShouldBe(1);
        (await db.TranscriptEntries.SingleAsync(t =>
                t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Sequence > preSweepMax))
            .Text.ShouldBe("the brief that must persist");
        h.Adapter.Killed.ShouldBeFalse();
        (await db.AgentIncidents.CountAsync(i =>
            i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed)).ShouldBe(1);
    }

    private static Task<BridgeQueueHarness> BlindHarnessAsync(TimeProvider clock) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            TimeProvider = clock,
        });

    private static SessionRunnerTranscriptEvent PoisonToolCall(Guid sessionId, long seq) =>
        new(sessionId, seq, TranscriptKinds.ToolCall, "poison", null, DateTimeOffset.UtcNow,
            "assistant", null, new string('x', 201), "SECRET-INPUT-MARKER", null, null, null);

    private static SessionRunnerTranscriptEvent StubbedUserPrompt(Guid sessionId, string body) =>
        new(sessionId, 99, TranscriptKinds.UserPrompt, new string('u', 65), null, DateTimeOffset.UtcNow,
            "user", body, null, null, null, null, null);

    private static async Task<DateTime> StartedAtAsync(BridgeQueueHarness h)
    {
        await using var db = CreateContext();
        return SessionGeneration.Normalize((await db.AgentSessions.FindAsync(h.SessionId))!.StartedAt);
    }
}
