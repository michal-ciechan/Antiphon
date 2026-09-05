using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
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
/// CARD-0047 slice 3 — the sweep that claims a due check, the worker that runs it, and the note
/// that reaches the caller.
///
/// Three properties here are not conveniences, they are the reasons this feature is safe to run
/// against work in flight, and each is TESTED rather than intended:
///
/// <list type="number">
/// <item><b>A check cannot settle anything.</b> The delegate's own settlement reads the DELEGATE's
/// transcript and nothing here writes there; the caller's settlement is gated on the caller's task
/// marker, and a check note is an unmarked prompt. <c>a_parent_reacting_to_a_check_note_does_not_settle_its_own_task</c>
/// drives the whole shape.</item>
/// <item><b>Read-only.</b> A full check cycle leaves the delegate's session with no new queued
/// messages, no new transcript entries, no status change, and no runner writes at all.</item>
/// <item><b>Re-arm before run.</b> The schedule is advanced and committed BEFORE the id is handed
/// off, so a check that throws costs one skipped check instead of a task that is due forever and
/// re-claimed every 5 s.</item>
/// </list>
///
/// The sweep is global over the shared fixture database, so this class takes <c>[NotInParallel]</c>
/// with NO group key (CLAUDE.md) and every assertion is scoped to rows it created.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AgentTaskCheckSweepTests
{
    // ---- claiming ----------------------------------------------------------------------------

    [Test]
    public async Task a_due_task_is_claimed_and_a_task_due_later_is_not()
    {
        var harness = new Harness();
        var due = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
        var notYet = await harness.SeedDelegateAsync(nextCheckInMinutes: 30);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var claimed = harness.DrainQueue();
        claimed.ShouldContain(due.Task.Id);
        claimed.ShouldNotContain(notYet.Task.Id);
        (await harness.ReloadAsync(notYet.Task.Id)).CheckCount.ShouldBe(0);
    }

    [Test]
    public async Task a_task_that_has_settled_is_never_claimed()
    {
        // The sweep filter is the whole mechanism: settlement needs no check bookkeeping.
        var harness = new Harness();
        var settled = await harness.SeedDelegateAsync(nextCheckInMinutes: -1, status: AgentTaskStatus.Succeeded);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        harness.DrainQueue().ShouldNotContain(settled.Task.Id);
    }

    [Test]
    public async Task a_failed_row_with_a_due_check_is_never_claimed_as_a_check()
    {
        // CARD-0231: a never-dispatched failure reuses NextCheckAt, so Failed + due is now a legal
        // state. The real check sweep must keep filtering to Dispatched/Working — claiming a Failed
        // row would waste a ramp step and log a check that cannot run.
        var harness = new Harness();
        var failed = await harness.SeedDelegateAsync(
            nextCheckInMinutes: -1, status: AgentTaskStatus.Failed);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        harness.DrainQueue().ShouldNotContain(failed.Task.Id);
        var after = await harness.ReloadAsync(failed.Task.Id);
        after.CheckCount.ShouldBe(0);
        after.NextCheckAt.ShouldNotBeNull();
    }

    [Test]
    public async Task the_feature_switch_stops_the_sweep_entirely()
    {
        var harness = new Harness(s => s.CheckEnabled = false);
        var due = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);

        (await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None)).ShouldBe(0);

        harness.DrainQueue().ShouldNotContain(due.Task.Id);
        (await harness.ReloadAsync(due.Task.Id)).NextCheckAt.ShouldNotBeNull();
    }

    // ---- backoff, cap, dead parent -------------------------------------------------------------

    [Test]
    public async Task the_next_check_backs_off_to_the_fixed_fibonacci_base()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(
            nextCheckInMinutes: -1, expectedMinutes: 20, checkCount: 0);

        var before = DateTime.UtcNow;
        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var after = await harness.ReloadAsync(seed.Task.Id);
        after.CheckCount.ShouldBe(1);
        after.NextCheckAt.ShouldNotBeNull();
        // CARD-0061: interval(1) is the fixed 5-minute base, regardless of the declared duration.
        after.NextCheckAt!.Value.ShouldBe(before.AddMinutes(5), TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task the_gap_widens_along_the_fibonacci_ramp_on_the_next_claim()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(
            nextCheckInMinutes: -1, expectedMinutes: 20, checkCount: 1);

        var before = DateTime.UtcNow;
        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        var after = await harness.ReloadAsync(seed.Task.Id);
        after.CheckCount.ShouldBe(2);
        // CARD-0061: interval(2) is twice the base — 10 minutes, not the old doubled 20.
        after.NextCheckAt!.Value.ShouldBe(before.AddMinutes(10), TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task the_last_check_of_the_budget_still_runs_and_disarms_the_schedule()
    {
        // The cap exists so a forgotten immortal task doesn't check forever — but the check that
        // spends the budget must still be DELIVERED, saying so, rather than vanishing.
        var harness = new Harness(s => s.CheckMaxCount = 3);
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1, checkCount: 2);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        harness.DrainQueue().ShouldContain(seed.Task.Id, "the final check runs");
        var after = await harness.ReloadAsync(seed.Task.Id);
        after.CheckCount.ShouldBe(3);
        after.NextCheckAt.ShouldBeNull("and nothing is scheduled after it");

        await harness.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None);
        var final = (await harness.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();
        final.ShouldContain("final check");
        final.ShouldContain(
            "#3]",
            customMessage: "the third check of a three-check budget is #3 — the header must agree with the budget");
    }

    /// <summary>
    /// The number the caller reads has to be the number of the check. It was not: the sweep counts a
    /// check when it CLAIMS it (<c>CheckCount</c> is half the conditional UPDATE that makes the claim
    /// atomic), and the probe then added one on top — so the very first check of a task announced
    /// itself as <c>#2</c>, and the tenth and last of the default budget as <c>#11</c>. Seen live on
    /// 2026-08-16: "Check #2 on task 8ae80695 delivered" for that task's first ever check.
    /// </summary>
    [Test]
    public async Task the_first_check_of_a_task_is_numbered_one()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1, checkCount: 0);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        await harness.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None);

        (await harness.ReloadAsync(seed.Task.Id)).CheckCount.ShouldBe(1, "one check has been spent");
        (await harness.NotesToCallerAsync(seed.CallerSessionId))
            .ShouldHaveSingleItem()
            .ShouldStartWith(
                $"[check {DelegationReportFormatter.Short(seed.Task.Id)} #1]",
                customMessage: "the claim already counted this check — adding one again numbers it #2");
    }

    [Test]
    public async Task a_task_at_its_cap_is_never_claimed_again()
    {
        var harness = new Harness(s => s.CheckMaxCount = 3);
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1, checkCount: 2);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        harness.DrainQueue();
        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        harness.DrainQueue().ShouldNotContain(seed.Task.Id);
        (await harness.ReloadAsync(seed.Task.Id)).CheckCount.ShouldBe(3, "the cap is a cap");
    }

    [Test]
    public async Task an_exited_caller_session_stops_the_checks()
    {
        // Nobody is listening. Continuing to gather facts for a session that has ended is pure cost.
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(
            nextCheckInMinutes: -1, callerSessionStatus: SessionStatus.Stopped);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        harness.DrainQueue().ShouldNotContain(seed.Task.Id);
        var after = await harness.ReloadAsync(seed.Task.Id);
        after.NextCheckAt.ShouldBeNull("disarmed, not left due forever");
        after.CheckCount.ShouldBe(0, "no check was spent");
    }

    [Test]
    public async Task a_caller_session_that_no_longer_exists_stops_the_checks()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1, callerSessionStatus: null);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        harness.DrainQueue().ShouldNotContain(seed.Task.Id);
        (await harness.ReloadAsync(seed.Task.Id)).NextCheckAt.ShouldBeNull();
    }

    // ---- re-arm BEFORE run ---------------------------------------------------------------------

    [Test]
    public async Task the_schedule_is_already_advanced_before_the_check_is_handed_off()
    {
        // This ordering is the whole crash-safety story: whatever happens to the run — a throw, a
        // kill -9, a machine reboot — the row has already moved on.
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);

        // Read the row while the id is STILL sitting unread in the queue.
        var beforeAnyRun = await harness.ReloadAsync(seed.Task.Id);
        beforeAnyRun.CheckCount.ShouldBe(1);
        beforeAnyRun.NextCheckAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
        harness.DrainQueue().ShouldContain(seed.Task.Id);
    }

    [Test]
    public async Task a_throwing_check_does_not_put_the_task_back_on_the_clock()
    {
        // A check whose probe blows up must cost ONE check, not turn the task into a row that is
        // due forever and re-claimed on every 5 s tick.
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        var armed = await harness.ReloadAsync(seed.Task.Id);
        harness.DrainQueue().ShouldContain(seed.Task.Id);

        // ObjectDisposedException specifically: the throw has to come from the PROBE, not from a
        // task that failed to load, or this would pass for the wrong reason.
        await Should.ThrowAsync<ObjectDisposedException>(
            () => harness.BrokenChecks().RunCheckAsync(seed.Task.Id, CancellationToken.None));

        var after = await harness.ReloadAsync(seed.Task.Id);
        after.CheckCount.ShouldBe(armed.CheckCount, "the failed run must not re-spend or refund a check");
        after.NextCheckAt.ShouldBe(armed.NextCheckAt, "and must not move the clock either way");

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        harness.DrainQueue().ShouldNotContain(
            seed.Task.Id, "it is not due again — a failed check is skipped, never retried in a loop");
    }

    // ---- the tick's clocks are independent -----------------------------------------------------

    /// <summary>
    /// The check sweep is the LAST of five clocks <c>TickAsync</c> runs before it dispatches, and
    /// they used to be five bare awaits — so a throw in any earlier one aborted the tick before the
    /// check sweep and the dispatch loop ever ran. Because the conditions that make a whole-table
    /// sweep throw are persistent (one poisoned row, one unreachable service), that is not a skipped
    /// tick, it is check-ins that stop firing for good, with nothing in the log naming what stopped.
    ///
    /// <para>The trigger here is the event-bus publish at the tail of the delivery watchdog — the one
    /// call in that sweep with no guard of its own — poisoned for one specific task so the throw is
    /// deterministic and no other row's handling changes.</para>
    /// </summary>
    [Test]
    public async Task an_earlier_sweep_that_throws_does_not_take_the_check_sweep_down_with_it()
    {
        var bus = new PoisonEventBus();
        var harness = new Harness(
            s =>
            {
                // Only the sweeps are under test: hold every queued task on the concurrency gate so
                // the dispatch loop cannot launch anything out of the shared database.
                s.MaxConcurrentTasks = 0;
                // And quiet the three clocks whose reach is global, so this test's throw is the only
                // thing that moves a row it did not create.
                s.RolePolicy.Clear();
                s.FinalMessageGraceSeconds = 0;
                s.SubagentGraceMinutes = 0;
                s.PoolEnabled = true;
                s.PoolIdleRetireMinutes = 525_600;
                s.PoolMaxIdlePerDirectory = int.MaxValue;
            },
            bus);

        // A task the delivery watchdog will fail: dispatched long ago, no transcript at all.
        var neverStarted = await harness.SeedDelegateAsync(nextCheckInMinutes: 600, expectedMinutes: 30);
        bus.PoisonTaskId = neverStarted.Task.Id;

        // And a task whose check is due. Its transcript is what keeps the watchdog off it, so the
        // only thing standing between it and its check is the throw.
        var due = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
        await harness.SeedDelegateTranscriptAsync(due.DelegateSessionId, due.Task.Id);

        var result = await harness.Dispatcher.TickAsync(CancellationToken.None);

        result.SweepFailures.ShouldBeGreaterThanOrEqualTo(
            1, "the failure must be COUNTED and reported, not swallowed into a quiet tick");
        harness.DrainQueue().ShouldContain(
            due.Task.Id, "the check sweep runs even though an earlier clock threw");
        (await harness.ReloadAsync(due.Task.Id)).CheckCount.ShouldBe(1);
    }

    // ---- running a check -----------------------------------------------------------------------

    [Test]
    public async Task a_check_delivers_a_digest_note_to_the_caller()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1, expectedMinutes: 15);
        await harness.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        (await harness.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        var note = (await harness.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();
        note.ShouldStartWith("[check ");
        note.Split('\n')[0].ShouldContain("/15m");
        note.ShouldContain("TASK ");
        note.ShouldContain("SESSION ");
    }

    [Test]
    public async Task the_note_is_unmistakably_a_check_and_carries_no_task_marker()
    {
        // The envelope has to be readable at a glance: a caller that mistook a check for a report
        // would move on from a task that has not finished. And it must carry NO task marker of any
        // task — the transcript tail legitimately quotes the delegate's own marked brief.
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
        await harness.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        await harness.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None);

        var note = (await harness.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();
        note.ShouldStartWith("[check ");
        note.ShouldContain("captured ");
        note.ShouldNotStartWith("[task ");
        note.ShouldNotContain("[antiphon-task:", customMessage:
            "no marker of ANY task may ride a check note into someone else's session");
        note.ShouldNotContain(
            DelegationReportFormatter.TaskMarker(seed.Task.Id),
            customMessage: "including the delegate's own, which the transcript tail quotes verbatim");

        await using var verify = CreateContext();
        var stored = await verify.SessionQueuedMessages.SingleAsync(
            m => m.AgentSessionId == seed.CallerSessionId);
        stored.Origin.ShouldBe(QueuedMessageOrigin.Check, "a check is not a Delegation report");
        stored.ConversationKey.ShouldBe(AgentTaskCheckService.ConversationKey(seed.Task.Id));
        stored.SourceTaskId.ShouldBe(seed.Task.Id, "CARD-0397: new Check rows stamp SourceTaskId");
    }

    [Test]
    public async Task a_big_digest_is_fitted_to_the_pty_ceiling_instead_of_tripping_the_oversize_guard()
    {
        // A digest with a full transcript tail and twenty commits comfortably clears the inbox
        // conhost's 3 000-char ceiling. Typing it whole would raise an OversizedTerminalDelivery
        // incident on EVERY check — and the note would be silently clipped besides.
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
        await harness.SeedNoisyDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        await harness.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None);

        var note = (await harness.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();
        note.Length.ShouldBeLessThanOrEqualTo(new DelegationSettings().ReplyInlineMaxChars);
        note.ShouldStartWith("[check ", customMessage: "the header must survive the excerpt");
        note.ShouldContain("EXCERPT", customMessage: "and the caller must be told it is one");
    }

    [Test]
    public async Task a_task_that_settles_between_the_claim_and_the_run_delivers_nothing()
    {
        // The completion note is already on its way and says everything the check would.
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        harness.DrainQueue();
        await harness.SettleAsync(seed.Task.Id);

        (await harness.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.AlreadySettled);
        (await harness.NotesToCallerAsync(seed.CallerSessionId)).ShouldBeEmpty();
    }

    [Test]
    public async Task a_delivered_check_leaves_a_timeline_entry_and_nothing_else_on_the_task()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        await harness.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None);

        await using var verify = CreateContext();
        var events = await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == seed.Task.Id).ToListAsync();
        events.ShouldContain(e => e.Type == AgentTaskEventType.Check);
        var task = await verify.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id);
        task.Status.ShouldBe(AgentTaskStatus.Dispatched, "a check is an observation, not a transition");
        task.Result.ShouldBeNull();
        task.CompletedAt.ShouldBeNull();
    }

    // ---- (a) a check CANNOT settle anything ----------------------------------------------------

    /// <summary>
    /// §1.4, driven end to end. The caller here is itself a delegate with a live task of its own.
    /// It receives a check note about its subordinate and REACTS to it, ending a turn — the exact
    /// shape that would settle a task if the note were mistaken for the caller's own brief.
    ///
    /// <para>It cannot be, and the mechanism is structural: the note arrives as a UserPrompt WITHOUT
    /// the caller's task marker, and settlement's marker gate refuses the turn. This also pins the
    /// negative decision behind it — check notes are deliberately NOT classified as housekeeping
    /// prompts in the walk-back, because if they were, the walk-back would skip past this note to
    /// the caller's own marked brief and settle the caller's task on its reaction to a note about
    /// somebody else's work.</para>
    /// </summary>
    [Test]
    public async Task a_parent_reacting_to_a_check_note_does_not_settle_its_own_task()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
        // The caller is a mid-flight orchestrator: its own brief, marked, is sitting in its session.
        var parentTask = await harness.SeedCallerTaskAsync(seed.CallerSessionId);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        await harness.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None);
        var note = (await harness.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem();

        // The caller reads the note and answers it, ending a turn.
        await harness.SeedTurnAsync(
            seed.CallerSessionId, prompt: note, reply: "Noted — it is still working, I will wait.");
        await harness.Replies.OnTurnEndAsync(seed.CallerSessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var parent = await verify.AgentTasks.SingleAsync(t => t.Id == parentTask.Id);
        parent.Status.ShouldBe(
            AgentTaskStatus.Dispatched,
            "an unmarked check note cannot pass the marker gate — the caller's own task is untouched");
        parent.Result.ShouldBeNull();
        parent.CompletedAt.ShouldBeNull();

        var delegated = await verify.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id);
        delegated.Status.ShouldBe(
            AgentTaskStatus.Dispatched, "and the delegate's own task is settled by its OWN transcript");
        delegated.Result.ShouldBeNull();
    }

    /// <summary>
    /// The positive control for the test above. Same harness, same session, same turn shape — only
    /// the prompt differs, carrying the caller's OWN marker. It settles. Without this, "the parent
    /// was not settled" could just as easily mean the harness never reached settlement at all.
    /// </summary>
    [Test]
    public async Task the_same_caller_turn_DOES_settle_when_the_prompt_carries_its_own_marker()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
        var parentTask = await harness.SeedCallerTaskAsync(seed.CallerSessionId);

        await harness.SeedTurnAsync(
            seed.CallerSessionId,
            prompt: DelegationReportFormatter.TaskMarker(parentTask.Id) + "\n\nOwn the chunk.",
            reply: "Chunk owned: three delegates ran, all merged.");
        await harness.Replies.OnTurnEndAsync(seed.CallerSessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var parent = await verify.AgentTasks.SingleAsync(t => t.Id == parentTask.Id);
        parent.Status.ShouldBe(AgentTaskStatus.Succeeded, "the control has to actually fire");
        parent.Result.ShouldContain("three delegates ran");
        parent.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
    }

    /// <summary>
    /// CARD-0243: the two-step closing-line contract applies to a caller-owned task the same as
    /// any other live session. Without the token the turn is nudged once and stays Dispatched —
    /// the pin that keeps the sibling "DOES settle" test from being "fixed" back to an unmarked
    /// reply.
    /// </summary>
    [Test]
    public async Task the_same_caller_turn_without_the_closing_line_is_nudged_once_not_settled()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
        var parentTask = await harness.SeedCallerTaskAsync(seed.CallerSessionId);

        await harness.SeedTurnAsync(
            seed.CallerSessionId,
            prompt: DelegationReportFormatter.TaskMarker(parentTask.Id) + "\n\nOwn the chunk.",
            reply: "Chunk owned: three delegates ran, all merged.",
            closingVerdict: false);
        await harness.Replies.OnTurnEndAsync(seed.CallerSessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var parent = await verify.AgentTasks.SingleAsync(t => t.Id == parentTask.Id);
        parent.Status.ShouldBe(AgentTaskStatus.Dispatched);
        parent.ReportNudgedAt.ShouldNotBeNull();

        var queued = await verify.SessionQueuedMessages
            .Where(m => m.AgentSessionId == seed.CallerSessionId)
            .ToListAsync();
        var nudge = queued.ShouldHaveSingleItem();
        nudge.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
        nudge.Status.ShouldBe(QueuedMessageStatus.Pending);
        nudge.Body.ShouldContain(DelegationReportFormatter.TaskMarker(parentTask.Id));
        nudge.Body.ShouldContain("closing report line");

        (await harness.NotesToCallerAsync(seed.CallerSessionId)).ShouldBeEmpty(
            "the nudge must not be mistaken for a check note by this suite's own accounting");
    }

    // ---- (b) READ-ONLY -------------------------------------------------------------------------

    /// <summary>
    /// §1.6, end to end. A full check cycle — sweep, claim, probe, render, deliver — against a
    /// seeded delegate, asserting the delegate itself came out of it byte for byte the same: no
    /// queued message, no transcript entry, no status change, and not one call to the runner.
    /// </summary>
    [Test]
    public async Task a_full_check_cycle_touches_the_delegate_not_at_all()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
        await harness.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);

        await using (var snapshotDb = CreateContext())
        {
            var beforeMessages = await snapshotDb.SessionQueuedMessages
                .CountAsync(m => m.AgentSessionId == seed.DelegateSessionId);
            beforeMessages.ShouldBe(0, "the fixture starts clean");
        }
        var beforeEntries = await CountTranscriptAsync(seed.DelegateSessionId);

        await harness.Dispatcher.RunScheduledChecksAsync(CancellationToken.None);
        (await harness.Checks.RunCheckAsync(seed.Task.Id, CancellationToken.None))
            .ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);

        await using var verify = CreateContext();
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == seed.DelegateSessionId))
            .ShouldBe(0, "nothing may be typed at the delegate");
        (await CountTranscriptAsync(seed.DelegateSessionId))
            .ShouldBe(beforeEntries, "and nothing may be written into its transcript");
        (await verify.AgentSessions.SingleAsync(s => s.Id == seed.DelegateSessionId))
            .Status.ShouldBe(SessionStatus.Running, "and its session may not be stopped or restarted");
        harness.Runner.Writes.ShouldBeEmpty(
            "no input, resize, buffer clear or kill may reach the runner during a check");

        // The one thing that DID happen: the caller heard about it.
        (await harness.NotesToCallerAsync(seed.CallerSessionId)).Count.ShouldBe(1);

        // Control. The zero-queued-messages assertion above is the load-bearing detector — ANY
        // attempt to type at the delegate persists a Pending row whether or not a runner is live —
        // so prove it can actually see one before trusting that it saw none.
        await harness.EnqueueToAsync(seed.DelegateSessionId, "a message the check never sent");
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == seed.DelegateSessionId))
            .ShouldBe(1, "control: a write to the delegate's queue really is visible from here");
    }

    // ---- CARD-0074: interpreter window, queue window, lock, ceiling ----------------------------

    [Test]
    public async Task a_task_that_settles_during_interpretation_enqueues_nothing_and_records_the_supersession_on_the_timeline()
    {
        // The 3/7 case: settled after GatherAsync / during InterpretAsync, before EnqueueAsync.
        // A note still goes out — marked — because the caller was scheduled to receive one.
        var scratch = Directory.CreateTempSubdirectory("antiphon-c74-interp").FullName;
        try
        {
            var harness = new Harness(s =>
            {
                s.CheckInterpreterEnabled = true;
                s.CheckInterpreterAgentSlug = $"c74-{Guid.NewGuid():N}"[..20];
                s.CheckInterpreterWorkingDirectory = scratch;
                s.CompletionNoteGraceSeconds = 0;
            });
            var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
            await harness.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);

            var specialistId = await harness.EnsureSpecialistAsync();
            var checks = harness.CreateChecksWithInterpreter();

            var run = Task.Run(() => checks.RunCheckAsync(seed.Task.Id, CancellationToken.None));
            var interpretation = await harness.WaitForInterpretationAsync(specialistId);
            await harness.SettleAsync(seed.Task.Id);
            await harness.SeedCompletionNoteAsync(seed.CallerSessionId, seed.Task.RootTaskId);
            await harness.SettleInterpretationAsync(interpretation.Id);

            (await run).ShouldBe(AgentTaskCheckService.CheckOutcome.SupersededBeforeDelivery);
            (await harness.NotesToCallerAsync(seed.CallerSessionId)).ShouldBeEmpty();
            await using var verify = CreateContext();
            var timeline = (await verify.AgentTaskEvents
                .Where(e => e.AgentTaskId == seed.Task.Id && e.Type == AgentTaskEventType.Check)
                .Select(e => e.Detail).ToListAsync()).ShouldHaveSingleItem();
            timeline.ShouldContain(AgentTaskCheckService.SupersededMarker);
            timeline.ShouldContain("CAPTURED ");
            timeline.ShouldContain("TASK ", customMessage: "the complete digest remains evidence on the task timeline");
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
        }
    }

    [Test]
    public async Task a_pending_check_note_is_canceled_when_its_task_settles()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: 30);
        var captured = DateTime.UtcNow.AddMinutes(-2);
        var body =
            $"[check {DelegationReportFormatter.Short(seed.Task.Id)} #1] captured {DelegateCheckProbe.Stamp(captured)}\n\n"
            + $"CAPTURED {DelegateCheckProbe.Stamp(captured)} — a snapshot of that moment, not of now.\n\n"
            + "TASK deadbeef: still going";
        var noteId = await harness.SeedCheckNoteAsync(
            seed.CallerSessionId, seed.Task.Id, body, createdAt: captured.AddSeconds(5));

        await harness.SettleAsync(seed.Task.Id);
        await harness.SeedCompletionNoteAsync(seed.CallerSessionId, seed.Task.RootTaskId);

        (await harness.Dispatcher.ReconcileSupersededChecksAsync(CancellationToken.None))
            .ShouldBeGreaterThanOrEqualTo(1);
        (await harness.Dispatcher.ReconcileSupersededChecksAsync(CancellationToken.None))
            .ShouldBe(0, "a second pass cannot cancel again");

        await using var verify = CreateContext();
        var stored = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == noteId);
        stored.Status.ShouldBe(QueuedMessageStatus.Canceled);
        stored.CanceledAt.ShouldNotBeNull();
        stored.DeliveryAttempts.ShouldBe(0);
        var timeline = (await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == seed.Task.Id && e.Type == AgentTaskEventType.Check)
            .Select(e => e.Detail).ToListAsync()).ShouldHaveSingleItem();
        timeline.ShouldStartWith(AgentTaskCheckService.SupersededMarker);
        timeline.ShouldContain("TASK deadbeef");
    }

    [Test]
    public async Task a_check_note_already_typed_once_is_not_canceled_by_the_sweep()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: 30);
        var body = $"[check {DelegationReportFormatter.Short(seed.Task.Id)} #1] the original digest";
        var noteId = await harness.SeedCheckNoteAsync(
            seed.CallerSessionId, seed.Task.Id, body,
            deliveryAttempts: 1, createdAt: DateTime.UtcNow.AddMinutes(-2));
        await harness.SettleAsync(seed.Task.Id);

        (await harness.Dispatcher.ReconcileSupersededChecksAsync(CancellationToken.None)).ShouldBe(0);

        await using var verify = CreateContext();
        var stored = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == noteId);
        stored.Body.ShouldBe(body);
        stored.Body.ShouldNotContain(AgentTaskCheckService.SupersededMarker);
        stored.DeliveryAttempts.ShouldBe(1);
    }

    [Test]
    public async Task a_settled_check_without_a_completion_note_falls_back_to_the_bannered_delivery()
    {
        var scratch = Directory.CreateTempSubdirectory("antiphon-c132-fallback").FullName;
        try
        {
            var harness = new Harness(s =>
            {
                s.CheckInterpreterEnabled = true;
                s.CheckInterpreterAgentSlug = $"c132-{Guid.NewGuid():N}"[..20];
                s.CheckInterpreterWorkingDirectory = scratch;
                s.CompletionNoteGraceSeconds = 0;
            });
            var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
            await harness.SeedDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);
            var specialistId = await harness.EnsureSpecialistAsync();
            var checks = harness.CreateChecksWithInterpreter();

            var run = Task.Run(() => checks.RunCheckAsync(seed.Task.Id, CancellationToken.None));
            var interpretation = await harness.WaitForInterpretationAsync(specialistId);
            await harness.SettleAsync(seed.Task.Id);
            await harness.SettleInterpretationAsync(interpretation.Id);

            (await run).ShouldBe(AgentTaskCheckService.CheckOutcome.Delivered);
            (await harness.NotesToCallerAsync(seed.CallerSessionId)).ShouldHaveSingleItem()
                .ShouldStartWith(AgentTaskCheckService.SupersededMarker);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); }
            catch (IOException) { }
        }
    }

    [Test]
    public async Task the_flush_cancels_a_settled_check_already_typed_once_before_it_retypes()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: 30);
        var noteId = await harness.SeedCheckNoteAsync(
            seed.CallerSessionId, seed.Task.Id, "[check] stale", deliveryAttempts: 1,
            createdAt: DateTime.UtcNow.AddMinutes(-2));
        await harness.SettleAsync(seed.Task.Id);
        await harness.SeedCompletionNoteAsync(seed.CallerSessionId, seed.Task.RootTaskId);

        await harness.Messages.FlushIfIdleAsync(seed.CallerSessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == noteId);
        stored.Status.ShouldBe(QueuedMessageStatus.Canceled);
        stored.DeliveryAttempts.ShouldBe(1, "the last look cancels instead of re-typing");
        harness.Runner.Writes.ShouldNotBeEmpty("the next completion note is typed after the canceled check");
    }

    [Test]
    public async Task a_check_note_whose_task_is_still_working_is_not_canceled()
    {
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: 30);
        var body = $"[check {DelegationReportFormatter.Short(seed.Task.Id)} #1] still live";
        var noteId = await harness.SeedCheckNoteAsync(seed.CallerSessionId, seed.Task.Id, body);

        (await harness.Dispatcher.ReconcileSupersededChecksAsync(CancellationToken.None)).ShouldBe(0);

        await using var verify = CreateContext();
        var stored = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == noteId);
        stored.Body.ShouldBe(body);
        stored.Body.ShouldNotContain(AgentTaskCheckService.SupersededMarker);
    }

    [Test]
    public async Task cancel_pending_if_untyped_waits_on_the_same_lock_cancel_uses()
    {
        // Would go green on an unlocked separate-scope write: that path would mutate while this
        // test still holds the per-session lock. Holding GetLock is exactly what CancelAsync and
        // FlushAsync serialise on.
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: 30);
        var original = $"[check {DelegationReportFormatter.Short(seed.Task.Id)} #1] unlocked-write detector";
        var noteId = await harness.SeedCheckNoteAsync(seed.CallerSessionId, seed.Task.Id, original);
        await harness.SettleAsync(seed.Task.Id);

        var queue = harness.Messages;
        var sem = queue.GetLock(seed.CallerSessionId);
        await sem.WaitAsync();
        Task<bool> cancel;
        try
        {
            cancel = Task.Run(() => queue.CancelPendingIfUntypedAsync(
                seed.CallerSessionId, noteId, CancellationToken.None));

            var finished = await Task.WhenAny(cancel, Task.Delay(400));
            finished.ShouldNotBe(cancel, "CancelPendingIfUntypedAsync must wait on GetLock — an unlocked scope would have finished");

            await using var mid = CreateContext();
            var still = await mid.SessionQueuedMessages.SingleAsync(m => m.Id == noteId);
            still.Status.ShouldBe(QueuedMessageStatus.Pending, "no cancellation may land while the session lock is held");
        }
        finally
        {
            sem.Release();
        }

        (await cancel).ShouldBeTrue();
        await using var after = CreateContext();
        var canceled = await after.SessionQueuedMessages.SingleAsync(m => m.Id == noteId);
        canceled.Status.ShouldBe(QueuedMessageStatus.Canceled);
    }

    [Test]
    public async Task a_banner_on_a_ceiling_sized_note_keeps_the_banner_and_trims_the_tail()
    {
        var ceiling = new DelegationSettings().ReplyInlineMaxChars;
        var harness = new Harness();
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: -1);
        await harness.SeedNoisyDelegateTranscriptAsync(seed.DelegateSessionId, seed.Task.Id);

        await using var db = CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == seed.Task.Id);
        var facts = await new DelegateCheckProbe(
                db,
                new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
                TimeProvider.System,
                Options.Create(new SupervisionSettings()))
            .GatherAsync(task, CancellationToken.None);
        var digest = DelegateCheckProbe.RenderDigest(facts);
        digest.Length.ShouldBeGreaterThan(ceiling, "control: the unfitted digest really does exceed the ceiling");

        var settledAt = facts.At.AddMinutes(2);
        var banner = AgentTaskCheckService.SupersededBanner(AgentTaskStatus.Succeeded, settledAt, facts.At);
        var note = harness.Checks.BuildNote(task, facts, digest, supersededBanner: banner);

        note.Length.ShouldBeLessThanOrEqualTo(ceiling);
        note.ShouldStartWith(AgentTaskCheckService.SupersededMarker);
        note.ShouldContain("SETTLED at");
        note.ShouldContain("[check ");
        note.ShouldContain("EXCERPT", customMessage: "the digest tail is what yields, not the banner");
    }

    [Test]
    public async Task tick_runs_the_superseded_check_reconcile()
    {
        var harness = new Harness(s =>
        {
            s.MaxConcurrentTasks = 0;
            s.RolePolicy.Clear();
            s.FinalMessageGraceSeconds = 0;
            s.SubagentGraceMinutes = 0;
            s.PoolIdleRetireMinutes = 525_600;
            s.PoolMaxIdlePerDirectory = int.MaxValue;
            s.CheckEnabled = false;
        });
        var seed = await harness.SeedDelegateAsync(nextCheckInMinutes: 30);
        var body = $"[check {DelegationReportFormatter.Short(seed.Task.Id)} #1] live-looking digest";
        await harness.SeedCheckNoteAsync(
            seed.CallerSessionId, seed.Task.Id, body, createdAt: DateTime.UtcNow.AddMinutes(-2));
        await harness.SettleAsync(seed.Task.Id);

        await harness.Dispatcher.TickAsync(CancellationToken.None);

        await harness.SeedCompletionNoteAsync(seed.CallerSessionId, seed.Task.RootTaskId);
        await harness.Dispatcher.TickAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.SessionQueuedMessages.SingleAsync(m => m.Origin == QueuedMessageOrigin.Check && m.AgentSessionId == seed.CallerSessionId))
            .Status.ShouldBe(QueuedMessageStatus.Canceled);
    }

    [Test]
    public void conversation_key_round_trips()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        AgentTaskCheckService.TryParseCheckConversationKey(
            AgentTaskCheckService.ConversationKey(id), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(id);
        AgentTaskCheckService.TryParseCheckConversationKey("check:not-a-guid", out _).ShouldBeFalse();
        AgentTaskCheckService.TryParseCheckConversationKey("delegation:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", out _).ShouldBeFalse();
        AgentTaskCheckService.TryParseCheckConversationKey(null, out _).ShouldBeFalse();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        for (var i = 0; (i = text.IndexOf(token, i, StringComparison.Ordinal)) >= 0; i += token.Length)
            count++;
        return count;
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static async Task<int> CountTranscriptAsync(Guid sessionId)
    {
        await using var db = CreateContext();
        return await db.TranscriptEntries.CountAsync(e => e.AgentSessionId == sessionId);
    }

    private sealed record Seeded(AgentTask Task, Guid DelegateSessionId, Guid CallerSessionId);

    private sealed class Harness
    {
        private readonly ServiceProvider _provider;
        private readonly DelegationSettings _settings;

        public Harness(Action<DelegationSettings>? configure = null, IEventBus? eventBus = null)
        {
            _settings = new DelegationSettings { MaxConcurrentTasks = 512 };
            configure?.Invoke(_settings);
            var settings = _settings;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
            services.AddSingleton<IEventBus>(eventBus ?? new MockEventBus());
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(settings));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddOptions<AgentRegistrySettings>();
            services.AddSingleton<AgentRegistry>();
            services.AddSingleton<AgentSessionLaunchQueue>();
            // A RECORDING runner client, so "the check never wrote to the delegate" is a mechanical
            // assertion rather than a consequence of the fake doing nothing.
            services.AddSingleton<ISessionRunnerClient>(Runner);
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<IDelegateSessionStopper>(new RecordingSessionStopper());
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-check-sweep-wt"),
            });
            services.AddScoped<AgentTaskService>();
            services.AddSingleton<AgentTaskCheckQueue>();
            services.AddScoped<DelegateCheckProbe>();
            services.AddScoped<AgentTaskCheckService>();
            services.AddSingleton<AgentTaskReplyService>();
            services.AddScoped<AgentTaskDispatcher>();

            _provider = services.BuildServiceProvider();
            Dispatcher = _provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
            Checks = _provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskCheckService>();
            Replies = _provider.GetRequiredService<AgentTaskReplyService>();
            Queue = _provider.GetRequiredService<AgentTaskCheckQueue>();
        }

        public RecordingRunnerClient Runner { get; } = new();
        public AgentTaskDispatcher Dispatcher { get; }
        public AgentTaskCheckService Checks { get; }
        public AgentTaskReplyService Replies { get; }
        public AgentTaskCheckQueue Queue { get; }
        public SessionMessageQueueService Messages =>
            _provider.GetRequiredService<SessionMessageQueueService>();

        /// <summary>
        /// Same check service as <see cref="Checks"/>, but with the specialist wired in so
        /// <c>InterpretAsync</c> actually waits — the interpreter window CARD-0074 closes.
        /// </summary>
        public AgentTaskCheckService CreateChecksWithInterpreter()
        {
            var db = CreateContext();
            return new AgentTaskCheckService(
                db,
                new DelegateCheckProbe(
                    db, new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance),
                    TimeProvider.System, Options.Create(new SupervisionSettings())),
                Messages,
                Options.Create(_settings),
                _provider.GetRequiredService<IEventBus>(),
                TimeProvider.System,
                NullLogger<AgentTaskCheckService>.Instance,
                interpreter: new CheckInterpreterProvisioner(
                    db, Options.Create(_settings), TimeProvider.System,
                    NullLogger<CheckInterpreterProvisioner>.Instance));
        }

        public async Task<Guid> EnsureSpecialistAsync()
        {
            await using var db = CreateContext();
            var agent = await new CheckInterpreterProvisioner(
                    db, Options.Create(_settings), TimeProvider.System,
                    NullLogger<CheckInterpreterProvisioner>.Instance)
                .EnsureAsync(CancellationToken.None);
            agent.ShouldNotBeNull();
            return agent!.Id;
        }

        public async Task<AgentTask> WaitForInterpretationAsync(Guid specialistId)
        {
            var started = DateTime.UtcNow.AddSeconds(-5);
            for (var attempt = 0; attempt < 200; attempt++)
            {
                await using var db = CreateContext();
                var row = await db.AgentTasks.AsNoTracking()
                    .Where(t => t.AgentId == specialistId
                        && t.Role == AgentTaskRole.Check
                        && t.CreatedAt >= started
                        && t.Status == AgentTaskStatus.Queued)
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefaultAsync();
                if (row is not null)
                    return row;
                await Task.Delay(25);
            }

            throw new TimeoutException("The check never created an interpretation task.");
        }

        public async Task SettleInterpretationAsync(Guid id)
        {
            await using var db = CreateContext();
            var row = await db.AgentTasks.SingleAsync(t => t.Id == id);
            row.Status = AgentTaskStatus.Succeeded;
            row.Result = "DOING — still mid-turn when this reading was captured.";
            row.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task<Guid> SeedCheckNoteAsync(
            Guid callerSessionId,
            Guid taskId,
            string body,
            int deliveryAttempts = 0,
            DateTime? createdAt = null)
        {
            await using var db = CreateContext();
            var next = await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == callerSessionId)
                .MaxAsync(m => (long?)m.Sequence) ?? 0;
            var id = Guid.NewGuid();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = id,
                AgentSessionId = callerSessionId,
                Body = body,
                Status = QueuedMessageStatus.Pending,
                Sequence = next + 1,
                Origin = QueuedMessageOrigin.Check,
                ConversationKey = AgentTaskCheckService.ConversationKey(taskId),
                CreatedAt = createdAt ?? DateTime.UtcNow.AddSeconds(-30),
                DeliveryAttempts = deliveryAttempts,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<Guid> SeedCompletionNoteAsync(Guid callerSessionId, Guid rootTaskId)
        {
            await using var db = CreateContext();
            var next = await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == callerSessionId)
                .MaxAsync(m => (long?)m.Sequence) ?? 0;
            var id = Guid.NewGuid();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = id,
                AgentSessionId = callerSessionId,
                Body = "[task done] completion note",
                Status = QueuedMessageStatus.Pending,
                Sequence = next + 1,
                Origin = QueuedMessageOrigin.Delegation,
                ConversationKey = $"task:{rootTaskId:N}",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return id;
        }

        /// <summary>A check service whose probe is built over a DISPOSED context, so it throws.</summary>
        public AgentTaskCheckService BrokenChecks()
        {
            var dead = CreateContext();
            dead.Dispose();
            return new AgentTaskCheckService(
                CreateContext(),
                new DelegateCheckProbe(
                    dead, new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance), TimeProvider.System,
                    Options.Create(new SupervisionSettings())),
                _provider.GetRequiredService<SessionMessageQueueService>(),
                Options.Create(new DelegationSettings()),
                new MockEventBus(),
                TimeProvider.System,
                NullLogger<AgentTaskCheckService>.Instance);
        }

        public List<Guid> DrainQueue()
        {
            var ids = new List<Guid>();
            while (Queue.TryDequeue(out var id))
                ids.Add(id);
            return ids;
        }

        public async Task<AgentTask> ReloadAsync(Guid taskId)
        {
            await using var db = CreateContext();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        }

        public async Task<IReadOnlyList<string>> NotesToCallerAsync(Guid callerSessionId)
        {
            await using var db = CreateContext();
            return await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == callerSessionId && m.Origin == QueuedMessageOrigin.Check)
                .OrderBy(m => m.Sequence)
                .Select(m => m.Body)
                .ToListAsync();
        }

        /// <summary>Through the REAL queue service — the control for the read-only assertion.</summary>
        public Task EnqueueToAsync(Guid sessionId, string body) =>
            _provider.GetRequiredService<SessionMessageQueueService>()
                .EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        public async Task SettleAsync(Guid taskId)
        {
            await using var db = CreateContext();
            var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
            task.Status = AgentTaskStatus.Succeeded;
            task.Result = "Done.";
            task.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task<Seeded> SeedDelegateAsync(
            int nextCheckInMinutes,
            int expectedMinutes = 10,
            int checkCount = 0,
            AgentTaskStatus status = AgentTaskStatus.Dispatched,
            SessionStatus? callerSessionStatus = SessionStatus.Running)
        {
            var callerSessionId = Guid.NewGuid();
            var delegateSessionId = Guid.NewGuid();
            var id = Guid.NewGuid();
            var dispatched = DateTime.UtcNow.AddMinutes(-expectedMinutes - 1);

            await using var db = CreateContext();
            if (callerSessionStatus is SessionStatus callerStatus)
                db.AgentSessions.Add(NewSession(callerSessionId, callerStatus, dispatched));
            db.AgentSessions.Add(NewSession(delegateSessionId, SessionStatus.Running, dispatched));
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                ParentSessionId = callerSessionId,
                ReplyTo = AgentTaskReplyTo.Session,
                Title = "check sweep test delegate",
                Goal = "do the checked thing",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                AgentSessionId = delegateSessionId,
                Status = status,
                ExpectedDurationMinutes = expectedMinutes,
                CheckCount = checkCount,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
                CompletedAt = status == AgentTaskStatus.Succeeded ? DateTime.UtcNow : null,
                NextCheckAt = DateTime.UtcNow.AddMinutes(nextCheckInMinutes),
            });
            await db.SaveChangesAsync();

            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
            return new Seeded(task, delegateSessionId, callerSessionId);
        }

        /// <summary>The caller's OWN task — a mid-flight orchestrator, with its marked brief on file.</summary>
        public async Task<AgentTask> SeedCallerTaskAsync(Guid callerSessionId)
        {
            var id = Guid.NewGuid();
            var dispatched = DateTime.UtcNow.AddMinutes(-30);
            await using var db = CreateContext();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                ParentSessionId = Guid.NewGuid(),
                ReplyTo = AgentTaskReplyTo.Session,
                Title = "the orchestrator's own chunk",
                Goal = "own the chunk and run agents for it",
                Kind = AgentTaskKind.Orchestrator,
                Role = AgentTaskRole.Plan,
                ModelLevel = AgentModelLevel.Frontier,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                AgentSessionId = callerSessionId,
                Status = AgentTaskStatus.Dispatched,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = callerSessionId,
                Sequence = 1,
                Kind = TranscriptKinds.UserPrompt,
                Uuid = $"caller-{Guid.NewGuid():N}",
                Role = "user",
                Text = DelegationReportFormatter.TaskMarker(id) + "\n\nOwn the chunk.",
                Timestamp = dispatched.AddSeconds(1),
                CreatedAt = dispatched.AddSeconds(1),
            });
            await db.SaveChangesAsync();
            return await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        }

        /// <summary>The delegate's own brief, marker and all — what the transcript tail will quote.</summary>
        public async Task SeedDelegateTranscriptAsync(Guid sessionId, Guid taskId)
        {
            var at = DateTime.UtcNow.AddMinutes(-4);
            await using var db = CreateContext();
            db.TranscriptEntries.AddRange(
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 1,
                    Kind = TranscriptKinds.UserPrompt,
                    Uuid = $"delegate-{Guid.NewGuid():N}",
                    Role = "user",
                    Text = DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the checked thing.",
                    Timestamp = at,
                    CreatedAt = at,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 2,
                    Kind = TranscriptKinds.AssistantText,
                    Uuid = $"delegate-{Guid.NewGuid():N}",
                    Role = "assistant",
                    Text = "Reading the spec first.",
                    Timestamp = at.AddSeconds(5),
                    CreatedAt = at.AddSeconds(5),
                });
            await db.SaveChangesAsync();
        }

        /// <summary>A tail big enough that the digest exceeds the inbox conhost's typing ceiling.</summary>
        public async Task SeedNoisyDelegateTranscriptAsync(Guid sessionId, Guid taskId)
        {
            var at = DateTime.UtcNow.AddMinutes(-4);
            await using var db = CreateContext();
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 1,
                Kind = TranscriptKinds.UserPrompt,
                Uuid = $"noisy-{Guid.NewGuid():N}",
                Role = "user",
                Text = DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the checked thing.",
                Timestamp = at,
                CreatedAt = at,
            });
            for (var i = 0; i < 14; i++)
            {
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 2 + i,
                    Kind = TranscriptKinds.AssistantText,
                    Uuid = $"noisy-{Guid.NewGuid():N}",
                    Role = "assistant",
                    Text = $"step {i}: " + new string('n', 400),
                    Timestamp = at.AddSeconds(i + 1),
                    CreatedAt = at.AddSeconds(i + 1),
                });
            }

            // The worst — and most interesting — case: a delegate with a queue backlog AND
            // incidents. That is exactly the shape a check most needs to deliver intact.
            var agentName = $"noisy-{Guid.NewGuid():N}"[..16];
            var agent = new Agent
            {
                Id = Guid.NewGuid(),
                Name = agentName,
                Slug = agentName,
                WorkingDirectory = Path.GetTempPath(),
                Details = "Noisy delegate.",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.Medium,
                IsPoolDelegate = true,
                CreatedAt = at,
                UpdatedAt = at,
            };
            db.Agents.Add(agent);
            for (var i = 0; i < 5; i++)
            {
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Body = $"stranded message {i}: " + new string('m', 400),
                    Status = QueuedMessageStatus.Pending,
                    Sequence = i + 1,
                    Origin = QueuedMessageOrigin.Delegation,
                    CreatedAt = at,
                });
                db.AgentIncidents.Add(new AgentIncident
                {
                    Id = Guid.NewGuid(),
                    AgentId = agent.Id,
                    SessionId = sessionId,
                    Kind = AgentIncidentKind.DeliveryVerificationFailed,
                    Severity = AlertSeverity.Warning,
                    Message = $"incident {i}: " + new string('i', 400),
                    CreatedAt = at.AddSeconds(i),
                });
            }
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// A complete turn on a session: prompt, reply, end. CARD-0159: when the prompt carries a
        /// task marker, append the closing report token unless <paramref name="closingVerdict"/> is
        /// false (the nudge arm). Copied from
        /// <c>AgentTaskReplyIntegrationTests.ApplyClosingVerdict</c> — do not share across suites.
        /// </summary>
        public async Task SeedTurnAsync(
            Guid sessionId, string prompt, string reply, bool closingVerdict = true)
        {
            reply = ApplyClosingVerdict(prompt, reply, closingVerdict);
            await using var db = CreateContext();
            var seq = await db.TranscriptEntries
                .Where(e => e.AgentSessionId == sessionId)
                .MaxAsync(e => (long?)e.Sequence) ?? 0;
            var at = DateTime.UtcNow;
            var apiCall = $"msg_{Guid.NewGuid():N}";
            db.TranscriptEntries.AddRange(
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = ++seq,
                    Kind = TranscriptKinds.UserPrompt,
                    Uuid = $"turn-{Guid.NewGuid():N}",
                    Role = "user",
                    Text = prompt,
                    Timestamp = at,
                    CreatedAt = at,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = ++seq,
                    Kind = TranscriptKinds.AssistantText,
                    Uuid = $"turn-{Guid.NewGuid():N}",
                    Role = "assistant",
                    Text = reply,
                    ApiCallId = apiCall,
                    Timestamp = at.AddSeconds(1),
                    CreatedAt = at.AddSeconds(1),
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = ++seq,
                    Kind = TranscriptKinds.TurnEnd,
                    Uuid = $"turn-{Guid.NewGuid():N}",
                    Role = "assistant",
                    StopReason = "end_turn",
                    ApiCallId = apiCall,
                    Timestamp = at.AddSeconds(2),
                    CreatedAt = at.AddSeconds(2),
                });
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// CARD-0159: existing settlement tests predate the closing-line contract. Appending the
        /// matching <c>[antiphon-report:id done]</c> (stripped again at settle) keeps them on the
        /// marked path. Question reports and tests that pass <paramref name="closingVerdict"/> false
        /// are left untouched so they exercise the heuristic / nudge arms.
        /// </summary>
        private static string ApplyClosingVerdict(string prompt, string reply, bool closingVerdict)
        {
            if (!closingVerdict || string.IsNullOrEmpty(reply))
                return reply;
            if (AgentTaskReplyService.LooksLikeAQuestion(reply))
                return reply;
            if (reply.Contains("[antiphon-report:", StringComparison.Ordinal))
                return reply;
            var shortId = DelegationReportFormatter.TryReadTaskMarkerId(prompt);
            if (shortId is null)
                return reply;
            return reply.TrimEnd() + "\n" + DelegationReportFormatter.ReportToken(shortId, "done");
        }

        private static AgentSession NewSession(Guid id, SessionStatus status, DateTime at) => new()
        {
            Id = id,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = status,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = at,
            StartedAt = at,
            LastSeenAt = at,
        };
    }

    /// <summary>
    /// An event bus that fails for ONE task and behaves for every other. Scoped that tightly on
    /// purpose: the sweeps under test scan the whole shared database, and a bus that threw for
    /// everything would change how this test's tick handles other suites' rows.
    /// </summary>
    private sealed class PoisonEventBus : IEventBus
    {
        public Guid PoisonTaskId { get; set; }

        public Task PublishToGroupAsync(
            string group, string eventName, object payload, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task PublishToAllAsync(string eventName, object payload, CancellationToken ct = default)
        {
            var taskId = payload.GetType().GetProperty("taskId")?.GetValue(payload) as Guid?;
            if (PoisonTaskId != Guid.Empty && taskId == PoisonTaskId)
                throw new InvalidOperationException("the event bus is down");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Records every WRITE-shaped call. Reads throw: a check has no business asking the runner
    /// anything, so an accidental read would fail loudly rather than pass quietly.
    /// </summary>
    private sealed class RecordingRunnerClient : ISessionRunnerClient
    {
        public List<string> Writes { get; } = [];

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
        {
            Writes.Add($"start:{sessionId}");
            throw new NotSupportedException("a check never starts a session");
        }

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Writes.Add($"input:{sessionId}");
            return Task.CompletedTask;
        }

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct)
        {
            Writes.Add($"clear:{sessionId}");
            return Task.CompletedTask;
        }

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct)
        {
            Writes.Add($"resize:{sessionId}");
            return Task.CompletedTask;
        }

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            Writes.Add($"kill:{sessionId}");
            throw new NotSupportedException("a check never kills a session");
        }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException("a check does not read the runner");

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException("a check does not read the runner");

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException("a check does not read the runner");

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException("a check does not read the runner");

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
