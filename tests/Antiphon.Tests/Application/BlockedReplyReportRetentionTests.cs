using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1472. A Reply to a task that is Blocked only by a runner sync refusal reopens it as a
/// new model turn. The retained substantive report must survive that turn: a token-only
/// completion never replaces <c>Result</c>, the handoff or the deliverable, the reopened wire
/// restates the complete-report contract, and a complete replacement report still settles once.
/// The fixture is the measured shape: a runner Code task whose complete marked report was
/// settled through the real reply service while the desktop repository lease was busy.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BlockedReplyReportRetentionTests
{
    private const string RetainedBody =
        "Implemented CARD-1472 in the runner mirror and pushed; checkpoint rows green; details in the plan.";

    [Test]
    [Timeout(240_000)]
    public async Task Bare_done_after_blocked_reply_preserves_retained_report()
    {
        await using var world = await BlockedBySyncAsync();
        var retained = world.Task;

        await ReplyAsync(world, "Accepted. The report is final. Stop now and do not take another turn.");
        world.Task.Status.ShouldBe(AgentTaskStatus.Working, "a Reply reopens the sync-blocked task");
        world.Task.RepliedAtSequence.ShouldNotBeNull();
        var wire = await ReplyWireAsync(world);

        // The lease is free now, so a complete report would settle. The child sends only the token.
        await world.SettleAsync(
            DelegationReportFormatter.ReportToken(world.TaskId, "done"), prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldNotBe(AgentTaskStatus.Succeeded, Why(after) + " a bare token is not a new successful report");
        after.Result.ShouldBe(retained.Result, "the retained report body stands");
        after.NextStage.ShouldBe(retained.NextStage, "the retained handoff kind stands");
        after.NextHandoff.ShouldBe(retained.NextHandoff, "the retained handoff sentence stands");
        after.DeliverablePath.ShouldBe(retained.DeliverablePath, "the retained artifact pointer stands");
        after.DeliverableRef.ShouldBe(retained.DeliverableRef);
        after.ResultFilePath.ShouldBe(retained.ResultFilePath);
        after.Status.ShouldBe(AgentTaskStatus.Working, Why(after) + " the task keeps awaiting the complete report");
        after.ReportNudgedAt.ShouldNotBeNull("the one bounded ask for the complete report is recorded");

        var events = await world.EventsAsync();
        events.Count(e => e.Type == AgentTaskEventType.Completed).ShouldBe(0, "no Succeeded publication");
        events.ShouldContain(
            e => e.Type == AgentTaskEventType.Warning && e.Detail.Contains("retained report", StringComparison.Ordinal),
            "the later turn is recorded as adding nothing");
    }

    [Test]
    [Timeout(240_000)]
    public async Task Closing_lines_only_after_blocked_reply_preserve_retained_report()
    {
        await using var world = await BlockedBySyncAsync();
        var retained = world.Task;
        await ReplyAsync(world, "Accepted. Stop.");
        var wire = await ReplyWireAsync(world);
        var pushedSha = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");
        var closingLines = DelegationReportFormatter.FindingToken(world.TaskId, "clean")
            + $"\n[antiphon-progress:{world.TaskId:D} commit={pushedSha}]\n"
            + DelegationReportFormatter.ReportToken(world.TaskId, "done");

        await world.SettleAsync(closingLines, prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Working, Why(after) + " closing markers add no report body");
        after.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        AssertRetained(after, retained);
        var ask = after.ReportNudgeMessageId.ShouldNotBeNull("one complete-report ask is recorded");
        await using (var db = world.CreateContext())
        {
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == ask);
            row.Status.ShouldBe(QueuedMessageStatus.Pending);
            row.Body.ShouldContain("only the closing report line", Case.Sensitive);
        }
        var events = await world.EventsAsync();
        events.Count(IsRetainedReportAsk).ShouldBe(1, "exactly one token-only ask");
        events.Count(e => e.Type == AgentTaskEventType.Completed).ShouldBe(0, "no Succeeded publication");
    }

    [Test]
    [Timeout(240_000)]
    public async Task Closing_lines_only_rejects_own_marker_between_prose_lines()
    {
        await using var world = await BlockedBySyncAsync();
        var replacement = "Replacement report: implemented and pushed.\n"
            + DelegationReportFormatter.FindingToken(world.TaskId, "clean")
            + "\nThe new report still contains substantive work after the marker.";

        await AssertReplacementSettlesOnceAsync(world, replacement);
    }

    [Test]
    [Timeout(240_000)]
    public async Task Closing_lines_only_rejects_first_line_marker_followed_by_prose()
    {
        await using var world = await BlockedBySyncAsync();
        var pushedSha = await world.Git.RunAsync(world.Git.Runner, "rev-parse", "HEAD");
        var replacement = $"[antiphon-progress:{world.TaskId:D} commit={pushedSha}]\n"
            + "Replacement report: implemented and pushed; the first marker must not hide this work.";

        await AssertReplacementSettlesOnceAsync(world, replacement);
    }

    [Test]
    [Timeout(240_000)]
    public async Task Closing_lines_only_rejects_foreign_marker_without_prose()
    {
        await using var world = await BlockedBySyncAsync();
        var replacement = DelegationReportFormatter.FindingToken(Guid.NewGuid(), "clean");

        await AssertReplacementSettlesOnceAsync(world, replacement);
    }

    private static async Task AssertReplacementSettlesOnceAsync(RunnerSettlementWorld world, string replacement)
    {
        var retained = world.Task;
        await ReplyAsync(world, "Accepted. Resend the complete report.");
        var wire = await ReplyWireAsync(world);
        var eventsBefore = await world.EventsAsync();

        // Preserve the exact body shape: adding a handoff would give the foreign-marker case prose.
        await world.SettleAsync(replacement + "\n" + DelegationReportFormatter.ReportToken(world.TaskId, "done"),
            prompt: wire.Body, closingVerdict: false);

        var settled = world.Task;
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(settled) + " this body is not closing-only");
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        settled.Result.ShouldBe(replacement, "the entire new body replaces the retained report");
        settled.Result.ShouldNotBe(retained.Result);
        settled.NextStage.ShouldBeNull("the replacement has no handoff; the retained sync handoff is gone");
        settled.NextHandoff.ShouldBeNull();
        settled.ReportNudgeMessageId.ShouldBeNull("normal settlement needs no complete-report ask");
        var events = await world.EventsAsync();
        events.Count(IsRetainedReportAsk).ShouldBe(eventsBefore.Count(IsRetainedReportAsk));
        // Merge-back bookkeeping can emit a separate Completed event; count the report settlement itself.
        events.Count(IsReportSettlement).ShouldBe(
            eventsBefore.Count(IsReportSettlement) + 1, "exactly one report settlement is published");

        var token = settled.ConcurrencyToken;
        await world.Services.GetRequiredService<AgentTaskReplyService>()
            .OnTurnEndAsync(world.SessionId, CancellationToken.None);
        await world.ReloadAsync();
        world.Task.ConcurrencyToken.ShouldBe(token, "observing the same boundary again changes nothing");
        (await world.EventsAsync()).Count.ShouldBe(events.Count, "no duplicate completion or ask");
    }

    [Test]
    [Timeout(240_000)]
    public async Task Bare_blocked_after_blocked_reply_blocks_with_retained_report()
    {
        await using var world = await BlockedBySyncAsync();
        var retained = world.Task;
        await ReplyAsync(world, "Accepted. Stop.");
        var wire = await ReplyWireAsync(world);

        await world.SettleAsync(
            DelegationReportFormatter.ReportToken(world.TaskId, "blocked"), prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Blocked, Why(after));
        after.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        AssertRetained(after, retained);
        (await world.EventsAsync()).Count(e => e.Type == AgentTaskEventType.Blocked).ShouldBe(2,
            "the sync block and the bare blocked verdict");
    }

    [Test]
    [Timeout(240_000)]
    public async Task Bare_failed_after_blocked_reply_fails_with_retained_report()
    {
        await using var world = await BlockedBySyncAsync();
        var retained = world.Task;
        await ReplyAsync(world, "Accepted. Stop.");
        var wire = await ReplyWireAsync(world);

        await world.SettleAsync(
            DelegationReportFormatter.ReportToken(world.TaskId, "failed"), prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Failed, Why(after));
        after.FailureReason.ShouldBe("Delegate reported failed.");
        after.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        AssertRetained(after, retained);
    }

    [Test]
    [Timeout(240_000)]
    public async Task Blocked_reply_wire_requires_a_complete_report_and_closing_token()
    {
        await using var world = await BlockedBySyncAsync();
        const string message = "Accepted. Stopping here.";

        await ReplyAsync(world, message);

        var wire = await ReplyWireAsync(world);
        wire.Status.ShouldBe(QueuedMessageStatus.Pending);
        var body = wire.Body;
        body.ShouldStartWith(DelegationReportFormatter.TaskMarker(world.TaskId), Case.Sensitive);
        body.ShouldContain(message, Case.Sensitive, "the caller's text still rides the wire");
        var done = DelegationReportFormatter.ReportToken(world.TaskId, "done");
        var blocked = DelegationReportFormatter.ReportToken(world.TaskId, "blocked");
        body.ShouldContain(done, Case.Sensitive, "the exact task-specific done line is restated");
        body.ShouldContain(blocked, Case.Sensitive, "the exact task-specific blocked line is restated");
        body.ShouldContain("complete", Case.Insensitive, "the wire asks for the complete report, not a token");
        body.ShouldContain("report", Case.Insensitive);
        body.IndexOf(done, StringComparison.Ordinal).ShouldBeGreaterThan(
            body.IndexOf(message, StringComparison.Ordinal), "the contract follows the caller's text");

        var replied = (await world.EventsAsync()).Single(e => e.Type == AgentTaskEventType.Replied);
        replied.InputBody.ShouldBe(body, "the Replied event stores the typed wire");
    }

    [Test]
    [Timeout(240_000)]
    public async Task Complete_replacement_report_settles_once()
    {
        await using var world = await BlockedBySyncAsync();
        await ReplyAsync(world, "Accepted. Resend the complete report.");
        var wire = await ReplyWireAsync(world);
        const string replacement = "Replacement report: implemented and pushed; nothing changed since the first report.";

        await world.SettleAsync(RunnerSettlementWorld.Report(replacement), prompt: wire.Body);

        var settled = world.Task;
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(settled));
        settled.Result.ShouldNotBeNull().ShouldStartWith(replacement, Case.Sensitive);
        settled.NextStage.ShouldBe(PipelineHandoffKind.Review);
        var token = settled.ConcurrencyToken;
        var events = await world.EventsAsync();
        events.Count(e => e.Type == AgentTaskEventType.Completed).ShouldBeGreaterThanOrEqualTo(1);

        // The same boundary observed again settles nothing more.
        await world.Services.GetRequiredService<AgentTaskReplyService>()
            .OnTurnEndAsync(world.SessionId, CancellationToken.None);
        await world.ReloadAsync();
        world.Task.ConcurrencyToken.ShouldBe(token, "a second observation changes nothing");
        (await world.EventsAsync()).Count.ShouldBe(events.Count, "a second observation adds no event");
    }

    [Test]
    [Timeout(240_000)]
    public async Task Wrong_task_token_cannot_settle_the_reopened_task()
    {
        await using var world = await BlockedBySyncAsync();
        var retained = world.Task;
        await ReplyAsync(world, "Accepted. Stop.");
        var wire = await ReplyWireAsync(world);
        var foreign = DelegationReportFormatter.ReportToken(Guid.NewGuid(), "done");

        await world.SettleAsync("Acknowledged.\n" + foreign, prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Working, Why(after) + " another task's token is not this task's verdict");
        after.Result.ShouldBe(retained.Result);
        after.NextStage.ShouldBe(retained.NextStage);
        after.NextHandoff.ShouldBe(retained.NextHandoff);
        after.ReportNudgedAt.ShouldNotBeNull("the unmarked answer gets the one closing-line ask");
        (await world.EventsAsync()).Count(e => e.Type == AgentTaskEventType.Completed).ShouldBe(0);
    }

    [Test]
    [Timeout(240_000)]
    public async Task Genuine_marked_blocked_reply_remains_blocked()
    {
        await using var world = await BlockedBySyncAsync();
        await ReplyAsync(world, "Accepted. Continue.");
        var wire = await ReplyWireAsync(world);
        const string question = "Which base should the follow-up branch use: master or the rc line?";

        await world.SettleAsync(
            question + "\n" + DelegationReportFormatter.ReportToken(world.TaskId, "blocked"),
            prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Blocked, Why(after));
        after.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        after.Result.ShouldBe(question, "a complete blocked report replaces the retained body");
        (await world.EventsAsync()).Count(e => e.Type == AgentTaskEventType.Blocked).ShouldBe(2,
            "the sync block and the genuine blocked verdict");
    }

    /// <summary>
    /// CARD-1486 S1 (G-4). CARD-1474 retired the never-typed ask when the report settled, so its
    /// row is Canceled with no <c>SentAt</c> while the task still names it. A Reply re-arms the
    /// one ask: a bare <c>done</c> on the reopened turn is asked again instead of waiting forever.
    /// </summary>
    [Test]
    [Timeout(240_000)]
    public async Task Reply_after_a_retired_nudge_rearms_the_one_ask()
    {
        await using var world = await BlockedBySyncAsync();
        var retiredAsk = await SeedRetiredNudgeAsync(world);
        var retained = world.Task;
        var warningsBefore = (await world.EventsAsync()).Count(IsRetainedReportAsk);

        await ReplyAsync(world, "Accepted. The report is final. Stop now and do not take another turn.");
        var wire = await ReplyWireAsync(world);
        await world.SettleAsync(
            DelegationReportFormatter.ReportToken(world.TaskId, "done"), prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Working, Why(after) + " the reopened turn is asked, not settled");
        var newAsk = after.ReportNudgeMessageId.ShouldNotBeNull("the reopened turn records its own ask");
        newAsk.ShouldNotBe(retiredAsk, "the retired, never-typed ask cannot be the reopened turn's ask");
        (await world.EventsAsync()).Count(IsRetainedReportAsk).ShouldBe(warningsBefore + 1,
            "one new token-only ask is recorded");
        await using (var db = world.CreateContext())
        {
            var row = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == newAsk);
            row.Status.ShouldBe(QueuedMessageStatus.Pending, "the new ask is queued for the live session");
            row.Body.ShouldContain("only the closing report line", Case.Sensitive);
        }
        AssertRetained(after, retained);
    }

    [Test]
    [Timeout(240_000)]
    public async Task Second_bare_done_after_a_typed_ask_blocks_with_retained_report()
    {
        await using var world = await BlockedBySyncAsync();
        var retained = world.Task;
        await ReplyAsync(world, "Accepted. Stop.");
        var wire = await ReplyWireAsync(world);
        var done = DelegationReportFormatter.ReportToken(world.TaskId, "done");

        await world.SettleAsync(done, prompt: wire.Body, closingVerdict: false);
        world.Task.Status.ShouldBe(AgentTaskStatus.Working, "fixture: the first bare done is asked once");
        var ask = world.Task.ReportNudgeMessageId.ShouldNotBeNull("fixture: the ask is recorded");
        string askBody;
        await using (var db = world.CreateContext())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == ask);
            row.Status = QueuedMessageStatus.Sent;
            row.SentAt = DateTime.UtcNow.AddSeconds(-1);
            askBody = row.Body;
            await db.SaveChangesAsync();
        }
        var asksBefore = (await world.EventsAsync()).Count(IsRetainedReportAsk);

        await world.SettleAsync(done, prompt: askBody, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Blocked, Why(after) + " a second bare done after a typed ask Blocks");
        after.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        after.ReportNudgeMessageId.ShouldBe(ask, "no second ask is recorded");
        AssertRetained(after, retained);
        var events = await world.EventsAsync();
        events.Count(IsRetainedReportAsk).ShouldBe(asksBefore, "no second ask goes out");
        events.Count(e => e.Type == AgentTaskEventType.Completed).ShouldBe(0, "no Succeeded publication");
    }

    /// <summary>
    /// CARD-1486 S1 guards (Review dfd45dcb). A recorded ask that can still be typed (Pending) or
    /// was typed (Sent) keeps the one-ask bound across a Reply: the task keeps naming it, and a bare
    /// <c>done</c> on the reopened turn sends no second ask.
    /// </summary>
    [Test]
    [Timeout(240_000)]
    public async Task Reply_with_a_pending_ask_keeps_the_recorded_ask()
    {
        await using var world = await BlockedBySyncAsync();
        var pendingAsk = await SeedRecordedNudgeAsync(world, QueuedMessageStatus.Pending, sentAt: null);
        var asksBefore = (await world.EventsAsync()).Count(IsRetainedReportAsk);

        await ReplyAsync(world, "Accepted. Stop.");
        world.Task.ReportNudgeMessageId.ShouldBe(pendingAsk, "a Pending ask can still be typed, so the Reply keeps it");
        world.Task.ReportNudgedAt.ShouldNotBeNull();
        var wire = await ReplyWireAsync(world);
        await world.SettleAsync(
            DelegationReportFormatter.ReportToken(world.TaskId, "done"), prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.ReportNudgeMessageId.ShouldBe(pendingAsk, Why(after) + " no second ask is recorded");
        (await world.EventsAsync()).Count(IsRetainedReportAsk).ShouldBe(asksBefore, "no second ask goes out");
    }

    [Test]
    [Timeout(240_000)]
    public async Task Reply_with_a_sent_ask_keeps_the_recorded_ask()
    {
        await using var world = await BlockedBySyncAsync();
        var sentAsk = await SeedRecordedNudgeAsync(
            world, QueuedMessageStatus.Sent, sentAt: DateTime.UtcNow.AddMinutes(-1));
        var asksBefore = (await world.EventsAsync()).Count(IsRetainedReportAsk);

        await ReplyAsync(world, "Accepted. Stop.");
        world.Task.ReportNudgeMessageId.ShouldBe(sentAsk, "a typed ask is the task's one ask, so the Reply keeps it");
        world.Task.ReportNudgedAt.ShouldNotBeNull();
        var wire = await ReplyWireAsync(world);
        await world.SettleAsync(
            DelegationReportFormatter.ReportToken(world.TaskId, "done"), prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.ReportNudgeMessageId.ShouldBe(sentAsk, Why(after) + " no second ask is recorded");
        (await world.EventsAsync()).Count(IsRetainedReportAsk).ShouldBe(asksBefore, "no second ask goes out");
    }

    /// <summary>
    /// CARD-1486 S2 (D-3). The reopened turn answers with an unmarked acknowledgement, is asked
    /// once, and answers the typed ask unmarked again. The retained marked report stands: the task
    /// Blocks with every retained field, and the settlement record names the unmarked turn.
    /// </summary>
    [Test]
    [Timeout(240_000)]
    public async Task Unmarked_ack_after_the_ask_blocks_with_retained_report()
    {
        await using var world = await BlockedBySyncAsync();
        var retained = world.Task;
        retained.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked, "fixture: the retained report was marked");
        await ReplyAsync(world, "Accepted. The report is final. Stop now and do not take another turn.");
        var wire = await ReplyWireAsync(world);

        await world.SettleAsync("Acknowledged. Stopping.", prompt: wire.Body, closingVerdict: false);
        world.Task.Status.ShouldBe(AgentTaskStatus.Working, "fixture: the unmarked acknowledgement is asked once");
        var ask = world.Task.ReportNudgeMessageId.ShouldNotBeNull("fixture: the ask is recorded");
        string askBody;
        await using (var db = world.CreateContext())
        {
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == ask);
            row.Status = QueuedMessageStatus.Sent;
            row.SentAt = DateTime.UtcNow.AddSeconds(-1);
            askBody = row.Body;
            await db.SaveChangesAsync();
        }

        await world.SettleAsync("Acknowledged, nothing changed.", prompt: askBody, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Blocked, Why(after) + " an unmarked acknowledgement is not a new report");
        after.ReportEvidence.ShouldBe(retained.ReportEvidence, "the retained evidence class stands");
        AssertRetained(after, retained);
        after.ReportNudgeMessageId.ShouldBe(ask, "no second ask is recorded");
        long boundary;
        await using (var db = world.CreateContext())
        {
            boundary = await db.TranscriptEntries.AsNoTracking()
                .Where(e => e.AgentSessionId == world.SessionId)
                .MaxAsync(e => e.Sequence);
        }
        var events = await world.EventsAsync();
        events.Count(e => e.Type == AgentTaskEventType.Completed).ShouldBe(0, "no Succeeded publication");
        var blocked = events.Where(e => e.Type == AgentTaskEventType.Blocked).OrderBy(e => e.At).Last();
        blocked.Detail.ShouldContain($"#{boundary}", Case.Sensitive, "the record names the unmarked turn");
        blocked.Detail.ShouldContain("unmarked", Case.Sensitive);
        blocked.Detail.ShouldContain("retained report", Case.Sensitive, "the record says the retained report stands");
    }

    /// <summary>
    /// CARD-1486 S3 (D-4). The reopened turn sends only the closing token, the one ask is recorded
    /// and never typed, and the session stays idle past <c>UnmarkedWaitingMinutes</c>. The waiting
    /// deadline Blocks it: the original marked report in the transcript predates the Reply and the
    /// bare token adds nothing, so neither stops the sweep, and the Block keeps the retained report.
    /// </summary>
    [Test]
    [Timeout(240_000)]
    public async Task Untyped_token_only_ask_blocks_after_waiting_minutes_with_retained_report()
    {
        await using var world = await BlockedBySyncAsync();
        await AssertWaitingDeadlineKeepsRetainedReportAsync(
            world, DelegationReportFormatter.ReportToken(world.TaskId, "done"));
    }

    /// <summary>CARD-1486 S3 (D-4): the same deadline for an unmarked acknowledgement after the Reply.</summary>
    [Test]
    [Timeout(240_000)]
    public async Task Untyped_unmarked_ask_after_reply_blocks_with_retained_report()
    {
        await using var world = await BlockedBySyncAsync();
        await AssertWaitingDeadlineKeepsRetainedReportAsync(world, "Acknowledged. Stopping.");
    }

    private static async Task AssertWaitingDeadlineKeepsRetainedReportAsync(
        RunnerSettlementWorld world, string reopenedTurn)
    {
        var retained = world.Task;
        retained.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked, "fixture: the retained report was marked");
        await ReplyAsync(world, "Accepted. The report is final. Stop now and do not take another turn.");
        var wire = await ReplyWireAsync(world);

        await world.SettleAsync(reopenedTurn, prompt: wire.Body, closingVerdict: false);
        world.Task.Status.ShouldBe(AgentTaskStatus.Working, "fixture: the reopened turn is asked once");
        var ask = world.Task.ReportNudgeMessageId.ShouldNotBeNull("fixture: the ask is recorded");
        int notesBefore;
        await using (var db = world.CreateContext())
        {
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == ask))
                .SentAt.ShouldBeNull("fixture: the ask is never typed");
            var row = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            row.ReportNudgedAt = DateTime.UtcNow.AddMinutes(-6);
            await db.SaveChangesAsync();
            notesBefore = await CallerNotesAsync(db, world);
        }

        await world.Services.GetRequiredService<AgentTaskReplyService>()
            .BlockUnmarkedWaitingAsync(world.SessionId, CancellationToken.None);
        await world.ReloadAsync();

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Blocked, Why(after) + " the waiting deadline reaches the reopened task");
        after.ReportEvidence.ShouldBe(retained.ReportEvidence, "the retained evidence class stands");
        AssertRetained(after, retained);
        after.AgentSessionId.ShouldBe(world.SessionId, "the Block keeps the session");
        await using (var db = world.CreateContext())
        {
            (await db.SessionQueuedMessages.SingleAsync(m => m.Id == ask))
                .Status.ShouldBe(QueuedMessageStatus.Canceled, "the never-typed ask is canceled at the Block");
            // The Block delivers the retained report, which the caller already holds: the queue drops
            // a same-task, same-digest Delegation note (CARD-0320). The waiting turn would add a row.
            (await CallerNotesAsync(db, world)).ShouldBe(
                notesBefore, "the caller note is the retained report, never the waiting turn");
            (await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.AgentSessionId == world.CallerSessionId && m.SourceTaskId == world.TaskId)
                    .Select(m => m.Body).ToListAsync())
                .ShouldContain(b => b.Contains(RetainedBody), "the caller note carries the retained report");
        }
        (await world.EventsAsync()).Count(e => e.Type == AgentTaskEventType.Completed)
            .ShouldBe(0, "no Succeeded publication");
    }

    private static Task<int> CallerNotesAsync(AppDbContext db, RunnerSettlementWorld world) =>
        db.SessionQueuedMessages.AsNoTracking()
            .CountAsync(m => m.AgentSessionId == world.CallerSessionId && m.SourceTaskId == world.TaskId);

    private static bool IsRetainedReportAsk(AgentTaskEvent e) =>
        e.Type == AgentTaskEventType.Warning && e.Detail.Contains("asked once for the complete report", StringComparison.Ordinal);

    private static bool IsReportSettlement(AgentTaskEvent e) =>
        e.Type == AgentTaskEventType.Completed && e.Detail.StartsWith("Delegate reported ", StringComparison.Ordinal);

    private static void AssertRetained(AgentTask after, AgentTask retained)
    {
        after.Result.ShouldBe(retained.Result, "the retained report body stands");
        after.NextStage.ShouldBe(retained.NextStage, "the retained handoff kind stands");
        after.NextHandoff.ShouldBe(retained.NextHandoff, "the retained handoff sentence stands");
        after.DeliverablePath.ShouldBe(retained.DeliverablePath, "the retained artifact pointer stands");
        after.DeliverableRef.ShouldBe(retained.DeliverableRef);
        after.ResultFilePath.ShouldBe(retained.ResultFilePath);
    }

    /// <summary>
    /// The CARD-1474 state after the sync block settled the report: the closing-line ask is
    /// recorded on the task, and its row was canceled before it was ever typed.
    /// </summary>
    private static Task<Guid> SeedRetiredNudgeAsync(RunnerSettlementWorld world) =>
        SeedRecordedNudgeAsync(world, QueuedMessageStatus.Canceled, sentAt: null);

    /// <summary>A closing-line ask recorded on the blocked task, its row in the given state.</summary>
    private static async Task<Guid> SeedRecordedNudgeAsync(
        RunnerSettlementWorld world, QueuedMessageStatus status, DateTime? sentAt)
    {
        var id = Guid.NewGuid();
        await using (var db = world.CreateContext())
        {
            var boundary = await db.TranscriptEntries.AsNoTracking()
                .Where(e => e.AgentSessionId == world.SessionId)
                .MaxAsync(e => e.Sequence);
            var at = DateTime.UtcNow.AddMinutes(-2);
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = id, AgentSessionId = world.SessionId, Origin = QueuedMessageOrigin.Delegation,
                Body = DelegationReportFormatter.TaskMarker(world.TaskId) + " Your turn ended without the closing report line.",
                Status = status, SentAt = sentAt, CreatedAt = at,
                CanceledAt = status == QueuedMessageStatus.Canceled ? at.AddMinutes(1) : null,
            });
            var row = await db.AgentTasks.SingleAsync(t => t.Id == world.TaskId);
            row.ReportNudgedAt = at;
            row.ReportNudgedSequence = boundary;
            row.ReportNudgeMessageId = id;
            await db.SaveChangesAsync();
        }
        await world.ReloadAsync();
        world.Task.Status.ShouldBe(AgentTaskStatus.Blocked, "fixture");
        return id;
    }

    /// <summary>
    /// A runner Code task whose complete marked report was settled while the desktop repository
    /// lease was busy: Blocked on <c>runner_sync_lease_busy</c> with the report retained.
    /// </summary>
    private static async Task<RunnerSettlementWorld> BlockedBySyncAsync()
    {
        var world = await RunnerSettlementWorld.CreateAsync(controlledSyncClock: true);
        try
        {
            await world.Git.RunnerPushAsync("work.txt", "runner work");
            await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
            {
                held.ShouldNotBeNull("fixture: hold the desktop common-directory lease");
                var settle = world.SettleAsync(RunnerSettlementWorld.Report(RetainedBody));
                (await Task.WhenAny(world.LeaseBusy.First, settle)).ShouldBe(world.LeaseBusy.First,
                    "fixture: the settlement must wait on the busy lease");
                world.SyncClock!.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
                await settle;
            }

            var blocked = world.Task;
            blocked.Status.ShouldBe(AgentTaskStatus.Blocked, "fixture: " + Why(blocked));
            world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy, "fixture");
            blocked.Result.ShouldNotBeNull().ShouldStartWith(RetainedBody, Case.Sensitive, "fixture: the report is retained");
            blocked.NextStage.ShouldBe(PipelineHandoffKind.Decide, "fixture: the sync block owns the handoff");
            blocked.NextHandoff.ShouldNotBeNullOrWhiteSpace("fixture");
            return world;
        }
        catch
        {
            await world.DisposeAsync();
            throw;
        }
    }

    private static async Task ReplyAsync(RunnerSettlementWorld world, string message)
    {
        await world.Services.GetRequiredService<AgentTaskReplyService>()
            .AnswerAsync(world.TaskId, message, AnswerOrigin.Cli, round: null, CancellationToken.None);
        await world.ReloadAsync();
    }

    /// <summary>The wire the Reply actually queued for the delegate session.</summary>
    private static async Task<SessionQueuedMessage> ReplyWireAsync(RunnerSettlementWorld world)
    {
        await using var db = world.CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == world.SessionId && m.Origin == QueuedMessageOrigin.Delegation)
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync();
    }

    private static string Why(AgentTask task) =>
        $"status={task.Status} evidence={task.ReportEvidence} reason={task.FailureReason} next={task.NextStage} "
        + $"result-length={task.Result?.Length ?? 0} sync={task.CompletionProgressEvidenceJson}";
}
