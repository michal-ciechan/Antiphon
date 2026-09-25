using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskDeliveryWatchdogTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C714_watchdog_catchup_rechecks_before_failure(bool existingIncident)
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11, kind: AgentKind.Grok);
        var sessionId = task.AgentSessionId!.Value;
        if (existingIncident)
            await SeedUncorrelatedIncidentAsync(sessionId, minutesAgo: 5);
        harness.CatchUpOverride = async (sid, _) =>
        {
            if (sid != sessionId)
                return;
            await Card0714Transcript.SeedReplayAsync(
                sessionId, task.Id, task.DispatchedAt!.Value, createdAt: DateTime.UtcNow);
        };

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(Card0714Transcript.StoredReport(task.Id));
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        settled.NextStage.ShouldBe(PipelineHandoffKind.Review);
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Failed)).ShouldBeFalse();
        (await verify.AgentTaskLandNotifications.AnyAsync(
            n => n.TaskId == task.Id && n.Kind == LandNotificationKind.DeliveryFailure)).ShouldBeFalse();
        stopper.Killed.ShouldNotContain(sessionId);
    }

    [Test]
    public async Task C714_sweep_recovers_marked_report_under_housekeeping()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5, kind: AgentKind.Grok);
        await Card0714Transcript.SeedReplayAsync(
            task.AgentSessionId!.Value, task.Id, task.DispatchedAt!.Value, createdAt: DateTime.UtcNow);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(Card0714Transcript.StoredReport(task.Id));
        settled.Result.ShouldNotContain(Card0714Transcript.Acknowledgement);
    }

    [Test]
    public async Task C714_watchdog_deferred_report_does_not_fail()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11, kind: AgentKind.Grok);
        var sessionId = task.AgentSessionId!.Value;
        var now = DateTime.UtcNow;
        await using (var db = CreateContext())
        {
            db.TranscriptEntries.AddRange(
                Row(sessionId, 1, TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.", now, now),
                Row(sessionId, 2, TranscriptKinds.AssistantText, "I'll start by reading the spec.", now, now, "msg_narration"),
                Row(sessionId, 3, TranscriptKinds.TurnEnd, null, now, now, "msg_final"));
            await db.SaveChangesAsync();
        }

        await SeedUncorrelatedIncidentAsync(sessionId, minutesAgo: 5);
        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Failed)).ShouldBeFalse();
        stopper.Killed.ShouldNotContain(sessionId);
    }

    [Test]
    public async Task C714_watchdog_true_uncorrelated_still_fails()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11, kind: AgentKind.Grok);
        var sessionId = task.AgentSessionId!.Value;
        var at = DateTime.UtcNow.AddMinutes(-1);
        await using (var db = CreateContext())
        {
            db.TranscriptEntries.AddRange(
                Row(sessionId, 1, TranscriptKinds.UserPrompt, "a human typed here", at, at),
                Row(sessionId, 2, TranscriptKinds.AssistantText, "A finished answer with no task marker.", at, at, "msg_human"),
                Row(sessionId, 3, TranscriptKinds.TurnEnd, null, at, at, "msg_human"));
            await db.SaveChangesAsync();
        }

        await SeedUncorrelatedIncidentAsync(sessionId, minutesAgo: 5);
        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("could not be attributed");
        stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task C714_housekeeping_only_still_never_started()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11, kind: AgentKind.Grok);
        var sessionId = task.AgentSessionId!.Value;
        await Card0714Transcript.SeedReplayAsync(
            sessionId, task.Id, task.DispatchedAt!.Value,
            includeReportText: false, includeReportBoundary: false, includeBrief: false);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("no turn prompt");
        failed.FailureReason.ShouldNotContain("could not be attributed");
        stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task C714_ack_only_with_stale_incident_is_not_uncorrelated()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11, kind: AgentKind.Grok);
        var sessionId = task.AgentSessionId!.Value;
        var at = task.DispatchedAt!.Value.AddMinutes(1);
        await using (var db = CreateContext())
        {
            db.TranscriptEntries.AddRange(
                Row(sessionId, 1, TranscriptKinds.UserPrompt, Card0714Transcript.Reminder, at, at),
                Row(sessionId, 2, TranscriptKinds.AssistantText, Card0714Transcript.Acknowledgement, at, at, Card0714Transcript.AcknowledgementApiCallId),
                Row(sessionId, 3, TranscriptKinds.TurnEnd, null, at, at, Card0714Transcript.AcknowledgementApiCallId));
            await db.SaveChangesAsync();
        }

        await SeedUncorrelatedIncidentAsync(sessionId, minutesAgo: 5);
        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldNotContain("could not be attributed");
        failed.FailureReason.ShouldContain("no turn prompt");
        stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task C714_sweep_final_grace_uses_selected_boundary()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5, kind: AgentKind.Grok);
        var sessionId = task.AgentSessionId!.Value;
        var expired = DateTime.UtcNow.AddMinutes(-3);
        var fresh = DateTime.UtcNow;
        await using (var db = CreateContext())
        {
            db.TranscriptEntries.AddRange(
                Row(sessionId, 1, TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.", expired, expired),
                Row(sessionId, 2, TranscriptKinds.AssistantText, "I'll start by reading the spec.", expired, expired, "msg_narration"),
                Row(sessionId, 3, TranscriptKinds.TurnEnd, null, expired, expired, "msg_final"),
                Row(sessionId, 4, TranscriptKinds.UserPrompt, Card0714Transcript.Reminder, fresh, fresh),
                Row(sessionId, 5, TranscriptKinds.AssistantText, Card0714Transcript.Acknowledgement, fresh, fresh, Card0714Transcript.AcknowledgementApiCallId),
                Row(sessionId, 6, TranscriptKinds.TurnEnd, null, fresh, fresh, Card0714Transcript.AcknowledgementApiCallId));
            await db.SaveChangesAsync();
        }

        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        var stored = await ReadTaskAsync(task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
        stored.ReportNudgedAt.ShouldNotBeNull();
        stored.ReportNudgedSequence.ShouldBe(3);
    }

    [Test]
    public async Task C714_watchdog_does_not_overwrite_live_settlement()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11, kind: AgentKind.Grok);
        var sessionId = task.AgentSessionId!.Value;
        await Card0714Transcript.SeedReplayAsync(
            sessionId, task.Id, task.DispatchedAt!.Value, createdAt: DateTime.UtcNow);
        var replies = harness.Replies.ShouldNotBeNull();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.DelayBeforeArm2RecheckAsync = async (sid, ct) =>
        {
            if (sid != sessionId)
                return;
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
        };

        var sweep = harness.FailNeverStartedAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await replies.OnTurnEndAsync(sessionId, CancellationToken.None);
        release.TrySetResult();
        await sweep.WaitAsync(TimeSpan.FromSeconds(60));

        await using (var verify = CreateContext())
        {
            var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
            settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
            (await verify.AgentTaskEvents.CountAsync(
                e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Completed)).ShouldBe(1);
            (await verify.AgentTaskEvents.AnyAsync(
                e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Failed)).ShouldBeFalse();
            (await verify.AgentTaskLandNotifications.AnyAsync(
                n => n.TaskId == task.Id && n.Kind == LandNotificationKind.DeliveryFailure)).ShouldBeFalse();
        }

        stopper.Killed.ShouldNotContain(sessionId);

        harness.DelayBeforeArm2RecheckAsync = null;
        var second = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11, kind: AgentKind.Grok);
        var secondSession = second.AgentSessionId!.Value;
        var at = DateTime.UtcNow.AddMinutes(-1);
        await using (var db = CreateContext())
        {
            db.TranscriptEntries.AddRange(
                Row(secondSession, 1, TranscriptKinds.UserPrompt, "unmarked human turn", at, at),
                Row(secondSession, 2, TranscriptKinds.AssistantText, "A report that is not this task.", at, at, "msg_other"),
                Row(secondSession, 3, TranscriptKinds.TurnEnd, null, at, at, "msg_other"));
            await db.SaveChangesAsync();
        }

        await SeedUncorrelatedIncidentAsync(secondSession, minutesAgo: 5);
        var enteredWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.DelayBeforeArm2ConditionalWriteAsync = async (sid, ct) =>
        {
            if (sid != secondSession)
                return;
            enteredWrite.TrySetResult();
            await releaseWrite.Task.WaitAsync(ct);
        };
        var replacement = Guid.NewGuid();
        var secondSweep = harness.FailNeverStartedAsync(CancellationToken.None);
        await enteredWrite.Task.WaitAsync(TimeSpan.FromSeconds(60));
        await using (var other = CreateContext())
        {
            var row = await other.AgentTasks.SingleAsync(t => t.Id == second.Id);
            row.Status = AgentTaskStatus.Queued;
            row.AgentSessionId = null;
            row.ConcurrencyToken = replacement;
            await other.SaveChangesAsync();
        }

        releaseWrite.TrySetResult();
        await secondSweep.WaitAsync(TimeSpan.FromSeconds(60));

        await using var after = CreateContext();
        var intact = await after.AgentTasks.SingleAsync(t => t.Id == second.Id);
        intact.Status.ShouldBe(AgentTaskStatus.Queued);
        intact.ConcurrencyToken.ShouldBe(replacement);
        intact.AgentSessionId.ShouldBeNull();
        (await after.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == second.Id && e.Type == AgentTaskEventType.Failed)).ShouldBeFalse();
        stopper.Killed.ShouldNotContain(secondSession);
    }

    private static TranscriptEntry Row(
        Guid sessionId, long sequence, string kind, string? text, DateTime timestamp, DateTime createdAt, string? apiCallId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = kind,
            Uuid = $"c714wd{sequence:000}{Guid.NewGuid():N}",
            Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
            Text = text,
            ApiCallId = apiCallId,
            StopReason = kind == TranscriptKinds.TurnEnd ? TranscriptKinds.StopReasons.EndTurn : null,
            Timestamp = timestamp,
            CreatedAt = createdAt,
        };
}
