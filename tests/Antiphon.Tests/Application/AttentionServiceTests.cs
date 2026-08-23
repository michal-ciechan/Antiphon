using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// CARD-0035 slice 1 — the "what needs a human" projection. One test per predicate, plus the two
/// properties that decide whether the view is worth opening at all:
///
/// <list type="number">
/// <item>a task that is merely SLOW — past its estimate but with a session that is mid-turn — is
/// never listed. Genuinely slow is not stuck, and a diagnostic list that cries wolf buries the one
/// real row among nine false ones;</item>
/// <item>a session runner that cannot answer costs the caller the runner-derived condition and
/// nothing else — the DB-derived rows still come back, and the response says nobody asked.</item>
/// </list>
///
/// <para><b>Shared-database discipline.</b> Every test in this assembly runs against ONE Postgres,
/// and this projection is deliberately FLEET-GLOBAL: it returns other suites' rows by design. So no
/// assertion here may touch a count of <c>Items</c>, and none may assert the list is empty — every
/// one filters to ids the test itself created, and every test deletes what it seeded. Three separate
/// "flaky test" incidents in this repo were an unscoped assertion over this same database.</para>
/// </summary>
[Category("Integration")]
public class AttentionServiceTests
{
    // ---- 1. BlockedQuestion ---------------------------------------------------------------------

    [Test]
    public async Task a_blocked_task_is_listed_as_needing_an_answer_and_offers_reply()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Blocked, dispatchedMinutesAgo: 20, costUsd: 0.25m);
        await scenario.AddTaskEventAsync(task, AgentTaskEventType.Blocked, "Delegate asked a question.",
            minutesAgo: 5);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.BlockedQuestion);
        item.Severity.ShouldBe(AlertSeverity.Critical, "only a human answer moves a blocked delegate");
        item.Actions.ShouldContain(AttentionAction.Reply);
        item.SubtreeCostUsd.ShouldBe(0.25m, "spend belongs on the row, not buried in a report");
        item.SinceUtc.ShouldNotBeNull();
        item.SinceUtc!.Value.ShouldBeGreaterThan(
            DateTime.UtcNow.AddMinutes(-10),
            "SinceUtc is when it BECAME blocked, not when it was dispatched");
    }

    [Test]
    public async Task a_merge_conflict_dates_from_its_conflicted_event_not_from_dispatch()
    {
        // The one transition that does NOT write a Blocked event: a conflicted merge-back sets
        // Status = Blocked while writing a Conflicted event. Reading only Blocked events would date
        // every conflicted task to its dispatch and sort it to the wrong end of the list.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Blocked, dispatchedMinutesAgo: 180,
            failureReason: "Rebase onto master conflicted in 2 file(s).");
        await scenario.AddTaskEventAsync(task, AgentTaskEventType.Conflicted, "Conflicts: a.cs, b.cs",
            minutesAgo: 3);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.SinceUtc!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-10));
        item.Evidence.ShouldContain("conflicted in 2 file(s)");
    }

    // ---- 2. ParkedMessage -----------------------------------------------------------------------

    [Test]
    public async Task a_message_that_spent_its_delivery_attempts_is_listed_for_a_human()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync(persistentSession: session);
        var message = await scenario.AddQueuedMessageAsync(session, "the reply nobody received", attempts: 3);

        var item = (await ItemsForAsync(scenario)).Single(i => i.MessageId == message);

        item.Kind.ShouldBe(AttentionKind.ParkedMessage);
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.AgentId.ShouldBe(agent);
        item.Evidence.ShouldContain("the reply nobody received");
        item.Actions.ShouldBe([AttentionAction.SendNow, AttentionAction.CancelMessage]);
    }

    [Test]
    public async Task a_parked_message_on_a_channel_bound_agent_reads_critical()
    {
        // A parked channel reply is not a stalled delivery — it is a person waiting on a line that
        // has gone dead, which is the whole reason the severity is conditional.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync(persistentSession: session);
        await scenario.AddChannelAsync(agent);
        var message = await scenario.AddQueuedMessageAsync(session, "are you still there?", attempts: 3);

        var item = (await ItemsForAsync(scenario)).Single(i => i.MessageId == message);

        item.Severity.ShouldBe(AlertSeverity.Critical);
        item.Headline.ShouldContain("channel-bound");
    }

    [Test]
    public async Task a_pending_message_that_has_attempts_left_is_not_parked()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        await scenario.AddAgentAsync(persistentSession: session);
        var message = await scenario.AddQueuedMessageAsync(session, "still in the normal queue", attempts: 1);

        (await ItemsForAsync(scenario)).ShouldNotContain(i => i.MessageId == message);
    }

    // ---- 3. DeadSession -------------------------------------------------------------------------

    [Test]
    public async Task an_open_task_whose_session_is_dead_is_listed()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Failed, endedMinutesAgo: 4);
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 9);
        await scenario.AddTranscriptAsync(session, (TranscriptKinds.UserPrompt, "go", null));

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.DeadSession);
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.Headline.ShouldContain("Failed");
        item.Actions.ShouldContain(AttentionAction.Retry);
    }

    // ---- 4. NeverStarted ------------------------------------------------------------------------

    [Test]
    public async Task a_dispatched_task_whose_session_has_written_nothing_is_listed()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 6);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.NeverStarted);
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.Headline.ShouldContain("written nothing");
    }

    // ---- 5. BriefUndelivered (CARD-0117 S5) -----------------------------------------------------

    [Test]
    public async Task a_deferred_pending_brief_on_a_working_session_is_listed()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 12);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "keep going", null),
            (TranscriptKinds.ToolCall, null, "Bash"));
        await scenario.AddDelegationBriefAsync(session, task, QueuedMessageStatus.Pending);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.BriefUndelivered);
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.Headline.ShouldContain("Pending");
        item.Actions[0].ShouldBe(AttentionAction.OpenDrawer);
    }

    [Test]
    public async Task a_pending_brief_on_an_idle_session_is_not_brief_undelivered()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 12);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.TurnEnd, null, null));
        await scenario.AddDelegationBriefAsync(session, task, QueuedMessageStatus.Pending);

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.TaskId == task && i.Kind == AttentionKind.BriefUndelivered);
    }

    [Test]
    public async Task a_sent_brief_on_a_working_session_is_not_brief_undelivered()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 12);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "keep going", null),
            (TranscriptKinds.ToolCall, null, "Bash"));
        await scenario.AddDelegationBriefAsync(session, task, QueuedMessageStatus.Sent);

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.TaskId == task && i.Kind == AttentionKind.BriefUndelivered);
    }

    [Test]
    public async Task a_pending_brief_inside_the_delivery_window_is_not_brief_undelivered()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 5);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "keep going", null),
            (TranscriptKinds.ToolCall, null, "Bash"));
        await scenario.AddDelegationBriefAsync(session, task, QueuedMessageStatus.Pending);

        (await ItemsForAsync(scenario)).ShouldNotContain(i => i.TaskId == task);
    }

    [Test]
    public async Task a_dispatched_task_still_inside_the_start_grace_is_not_listed()
    {
        // A brand-new dispatch has legitimately written nothing yet. Flagging it would put a row on
        // the board for every launch.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 1);

        (await ItemsForAsync(scenario)).ShouldNotContain(i => i.TaskId == task);
    }

    // ---- 5. UncorrelatedReport ------------------------------------------------------------------

    [Test]
    public async Task a_report_that_could_not_be_correlated_lists_its_task_once()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 8);
        await scenario.AddTranscriptAsync(session, (TranscriptKinds.UserPrompt, "the brief", null));
        await scenario.AddIncidentAsync(
            agent, session, AgentIncidentKind.DelegateReportUncorrelated, AlertSeverity.Error,
            "A delegate reported with no task marker.", minutesAgo: 2);

        var mine = await ItemsForAsync(scenario);
        var item = mine.Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.UncorrelatedReport);
        item.Evidence.ShouldContain("no task marker");
        // The same incident must not also appear as a RecentCriticalIncident row: one fact, one row.
        mine.ShouldNotContain(i => i.Kind == AttentionKind.RecentCriticalIncident && i.AgentId == agent);
    }

    // ---- 6. PastExpectedIdle, and THE exclusion -------------------------------------------------

    [Test]
    public async Task a_task_far_past_its_estimate_and_idle_at_the_prompt_is_listed()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 200, expectedMinutes: 30);
        // Ends on a TurnEnd: the shared verdict reads idle.
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.AssistantText, "on it", null),
            (TranscriptKinds.TurnEnd, null, null));

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.PastExpectedIdle);
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.Headline.ShouldContain("30m estimate");
        item.Actions[0].ShouldBe(
            AttentionAction.OpenDrawer, "read the check digest first — the answer is often 'leave it'");
    }

    [Test]
    public async Task a_task_far_past_its_estimate_but_still_mid_turn_is_never_listed()
    {
        // THE non-membership rule. This task is four hours into a thirty-minute estimate — every
        // clock says late — and it is excluded, because its session is working. Genuinely slow is
        // not stuck, and the moment this view says otherwise it stops being worth opening.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        // Three hours, not the four this used to seed: CARD-0020's Overdue row starts at 80% of the
        // 240-minute ceiling, so a four-hour task is now listed — by that condition, on the deadline
        // it is about to breach, which is a different claim from the one this test makes.
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 180, expectedMinutes: 30);
        // Activity above the last turn end: the shared verdict reads working.
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.TurnEnd, null, null),
            (TranscriptKinds.UserPrompt, "keep going", null),
            (TranscriptKinds.ToolCall, null, "Bash"));

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.TaskId == task,
            "a working session with a fresh transcript is never listed, however far past its estimate");
    }

    // ---- 7. Overdue (CARD-0020 S2/S3) -----------------------------------------------------------

    [Test]
    public async Task a_mid_turn_task_closing_on_its_ceiling_is_listed_before_the_sweep_fails_it()
    {
        // The hole PastExpectedIdle declines by construction. Listed at 80% of the limit, the way
        // NeverStartedGrace previews the delivery watchdog, so a reply, a check or a cancel are all
        // still open to a human.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 200, expectedMinutes: 30);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.ToolCall, null, "Bash"));

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.Overdue);
        item.Severity.ShouldBe(
            AlertSeverity.Warning, "nothing is broken yet — the honest reading is 'look at this'");
        item.Headline.ShouldContain("Closing on the deadline");
        item.Headline.ShouldContain("ceiling for role Code", customMessage:
            "the row and the failure it becomes are the same sentence");
        item.Actions[0].ShouldBe(AttentionAction.OpenDrawer);
    }

    [Test]
    public async Task a_task_only_part_way_through_its_ceiling_is_not_listed_as_overdue()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 100, expectedMinutes: 30);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.ToolCall, null, "Bash"));

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.TaskId == task,
            "under the preview fraction there is nothing to say, and a view that cries wolf is not opened");
    }

    [Test]
    public async Task a_breached_deadline_says_the_sweep_is_about_to_fail_it()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 300, expectedMinutes: 30);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.ToolCall, null, "Bash"));

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.Overdue);
        item.Headline.ShouldContain("Past its deadline", customMessage:
            "a row that has already fired must not read the same as one that has not");
    }

    [Test]
    public async Task an_idle_task_keeps_the_more_explanatory_past_expected_row()
    {
        // First-match order: PastExpectedIdle owns the idle case and names the cause ("finished and
        // never reported"); Overdue would only name a clock. The two partition the open tasks.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 200, expectedMinutes: 30);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.TurnEnd, null, null));

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(
            AttentionKind.PastExpectedIdle,
            "the ceiling is at 83% here too — the idle row still wins, because it names the cause");
    }

    // ---- 8. ChecksSpent -------------------------------------------------------------------------

    [Test]
    public async Task a_task_whose_check_budget_ran_out_is_listed_as_unwatched()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 12, checkCount: 10, nextCheckAt: null);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.TurnEnd, null, null));
        await scenario.AddTaskEventAsync(
            task, AgentTaskEventType.Check,
            "TASK abcd1234: something\n  status=Dispatched\nGIT: commits=3 changed=1 untracked=0",
            minutesAgo: 6);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.ChecksSpent);
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.Headline.ShouldContain("10 check(s)");
        item.Evidence.ShouldContain(
            "commits=3",
            customMessage: "the tail of the latest check digest IS the v1 explanation column");
    }

    /// <summary>
    /// CARD-0035 slice 5. The digest tail is deterministic and always there, but it is six lines of
    /// <c>commits=3 changed=1</c> that a human still has to interpret — and the check interpreter has
    /// already done exactly that job at exactly this altitude. Once its reading is stored on the
    /// Check event it WINS, because a row that made the operator re-derive the diagnosis from raw
    /// counters is throwing away the best explanation the system produced.
    /// </summary>
    [Test]
    public async Task the_check_interpreters_reading_beats_the_digest_tail_when_one_was_stored()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 12, checkCount: 10, nextCheckAt: null);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.TurnEnd, null, null));
        await scenario.AddTaskEventAsync(
            task, AgentTaskEventType.Check,
            AgentTaskCheckService.ComposeEventDetail(
                "STALLED — three commits, then 40 minutes of nothing. It finished and never reported.",
                "interpreter: task abcd1234, $0.0031",
                "TASK abcd1234: something\n  status=Dispatched\nGIT: commits=3 changed=1 untracked=0"),
            minutesAgo: 6);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Evidence.ShouldContain("STALLED — three commits", customMessage:
            "the specialist's reading, verbatim");
        item.Evidence.ShouldContain("The last check read it as:", customMessage:
            "labelled as a judgement — counters presented as a reading would claim somebody looked");
        item.Evidence.ShouldNotContain("commits=3", customMessage:
            "and NOT the digest tail as well: two explanations of one fact is one too many");
    }

    [Test]
    public async Task a_task_that_is_still_being_checked_is_not_listed()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 12, checkCount: 3,
            nextCheckAt: DateTime.UtcNow.AddMinutes(10));
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.TurnEnd, null, null));

        (await ItemsForAsync(scenario)).ShouldNotContain(i => i.TaskId == task);
    }

    // ---- 9. RecentCriticalIncident --------------------------------------------------------------

    [Test]
    public async Task recent_error_incidents_collapse_to_one_row_per_agent_and_kind()
    {
        // Ungrouped this condition is the noisiest by an order of magnitude: 107 raw Error+ rows in
        // one live day, 80 of them one kind on one agent. Per (agent, kind) is what keeps it
        // readable WITHOUT merging two different problems into one line.
        await using var scenario = new Scenario();
        var agent = await scenario.AddAgentAsync();
        for (var i = 0; i < 5; i++)
            await scenario.AddIncidentAsync(
                agent, null, AgentIncidentKind.TranscriptBindFailed, AlertSeverity.Critical,
                $"Could not bind a transcript ({i}).", minutesAgo: 30 + i);
        await scenario.AddIncidentAsync(
            agent, null, AgentIncidentKind.DeliveryVerificationFailed, AlertSeverity.Error,
            "Delivery could not be verified.", minutesAgo: 10);
        // Info-level noise and anything older than the window stay out entirely.
        await scenario.AddIncidentAsync(
            agent, null, AgentIncidentKind.ContextCompacted, AlertSeverity.Info, "Compacted.", minutesAgo: 5);
        await scenario.AddIncidentAsync(
            agent, null, AgentIncidentKind.Crash, AlertSeverity.Error, "Ancient history.",
            minutesAgo: 60 * 30);

        var mine = (await ItemsForAsync(scenario))
            .Where(i => i.Kind == AttentionKind.RecentCriticalIncident && i.AgentId == agent)
            .ToList();

        mine.Count.ShouldBe(2, "five bind failures and one delivery failure are two conditions, not six");
        var bind = mine.Single(i => i.Headline.Contains(nameof(AgentIncidentKind.TranscriptBindFailed)));
        bind.Severity.ShouldBe(AlertSeverity.Critical);
        bind.Headline.ShouldContain("5 x");
        bind.Actions.ShouldBe([AttentionAction.OpenAgent]);
        mine.ShouldNotContain(i => i.Headline.Contains(nameof(AgentIncidentKind.Crash)));
        mine.ShouldNotContain(i => i.Headline.Contains(nameof(AgentIncidentKind.ContextCompacted)));
    }

    // ---- the collapsed context group -------------------------------------------------------------

    [Test]
    public async Task a_task_that_failed_today_is_carried_as_context()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Stopped, endedMinutesAgo: 120);
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Failed, dispatchedMinutesAgo: 150, completedMinutesAgo: 120,
            failureReason: "Delivered but never started.");

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.RecentFailure);
        item.Severity.ShouldBe(AlertSeverity.Warning, "failures are context, not an alarm");
        item.Evidence.ShouldContain("never started");
        item.Actions.ShouldContain(AttentionAction.Retry);
    }

    [Test]
    public async Task a_failure_older_than_the_recency_window_is_not_carried()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Stopped, endedMinutesAgo: 60 * 40);
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Failed, dispatchedMinutesAgo: 60 * 41, completedMinutesAgo: 60 * 40);

        (await ItemsForAsync(scenario)).ShouldNotContain(i => i.TaskId == task);
    }

    // ---- 8. SessionDisagreement -------------------------------------------------------------------

    [Test]
    public async Task a_session_the_database_wrote_off_but_the_runner_still_runs_is_listed()
    {
        // The CARD-0056 shape, seen from the outside: a launch path that failed after starting the
        // process left the row Failed while the agent kept running. That false Failed silently
        // disabled the caller's check-ins for four hours, and nothing surfaced it.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Failed, endedMinutesAgo: 50);
        var agent = await scenario.AddAgentAsync(persistentSession: session);

        var item = (await ItemsForAsync(scenario, Running(session, pid: 4242, hostPid: 77)))
            .Single(i => i.Kind == AttentionKind.SessionDisagreement && i.SessionId == session);

        item.Severity.ShouldBe(AlertSeverity.Error, "a live process proves the database row is wrong");
        item.AgentId.ShouldBe(agent);
        item.Headline.ShouldContain("Failed");
        item.Headline.ShouldContain("still running");
        item.Evidence.ShouldContain("4242");
        item.Actions[0].ShouldBe(
            AttentionAction.OpenAgent,
            "look before you kill — this exact row was once the operator's own live conversation");
        item.Actions.ShouldContain(AttentionAction.KillSession);
    }

    [Test]
    public async Task a_runner_session_with_no_database_row_is_listed_as_unclaimed()
    {
        await using var scenario = new Scenario();
        var orphan = scenario.ClaimSessionId();

        var item = (await ItemsForAsync(scenario, Running(orphan)))
            .Single(i => i.SessionId == orphan);

        item.Kind.ShouldBe(AttentionKind.SessionDisagreement);
        item.Severity.ShouldBe(
            AlertSeverity.Warning, "unclaimed is suspect, not broken — it is usually somebody's work");
        item.Title.ShouldContain("Unclaimed");
        item.Actions.ShouldBe([AttentionAction.KillSession]);
        item.Evidence.ShouldContain("Read it before killing it.");
    }

    [Test]
    public async Task a_disagreement_says_the_dead_session_rows_above_it_are_wrong()
    {
        // Both rows are true about their own subject, and read together they contradict: the task
        // pass says "dead, retry it" and the runner says the agent is alive. Retrying would start a
        // SECOND agent alongside the running one, so the disagreement row names the trap.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Failed, endedMinutesAgo: 5);
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 20);
        await scenario.AddTranscriptAsync(session, (TranscriptKinds.UserPrompt, "go", null));

        var mine = await ItemsForAsync(scenario, Running(session));

        mine.Single(i => i.TaskId == task).Kind.ShouldBe(AttentionKind.DeadSession);
        mine.Single(i => i.Kind == AttentionKind.SessionDisagreement)
            .Evidence.ShouldContain("those tasks are not dead");
    }

    [Test]
    public async Task a_session_both_sides_agree_is_running_is_not_a_disagreement()
    {
        // The healthy case, and by far the commonest: every live session in the fleet appears in the
        // runner's list. If agreement produced a row the view would be nothing BUT rows.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        await scenario.AddAgentAsync(persistentSession: session);

        (await ItemsForAsync(scenario, Running(session)))
            .ShouldNotContain(i => i.Kind == AttentionKind.SessionDisagreement);
    }

    [Test]
    public async Task a_runner_session_that_has_exited_is_not_a_disagreement()
    {
        // An Exited runner session next to a Stopped row is two systems AGREEING. Only "Running" can
        // contradict a settled row, and reconciliation already owns the live-row-vs-exited direction.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Stopped, endedMinutesAgo: 10);
        await scenario.AddAgentAsync(persistentSession: session);

        (await ItemsForAsync(scenario, Running(session) with { Status = "Exited", ExitCode = 0 }))
            .ShouldNotContain(i => i.Kind == AttentionKind.SessionDisagreement);
    }

    // ---- degradation ------------------------------------------------------------------------------

    [Test]
    public async Task a_runner_that_cannot_answer_omits_the_disagreement_rather_than_reporting_none()
    {
        // The distinction the flag exists for. This session WOULD be a disagreement, and the runner
        // being down must not turn that into a clean bill of health — the condition is absent, and
        // RunnerConsulted is how the client tells absent from empty.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Failed, endedMinutesAgo: 30);
        await scenario.AddAgentAsync(persistentSession: session);

        var result = await BuildService(new FakeRunnerClient
        {
            Sessions = [Running(session)],
            ListError = new HttpRequestException("connection refused"),
        }).GetAsync(CancellationToken.None);

        result.RunnerConsulted.ShouldBeFalse();
        result.Items.ShouldNotContain(i => i.Kind == AttentionKind.SessionDisagreement);
    }

    [Test]
    public async Task a_runner_that_cannot_answer_degrades_instead_of_throwing()
    {
        // A runner that is down must cost the caller the runner-derived condition and NOTHING else.
        // The DB-derived list is the part a human can still act on, and failing the whole projection
        // would hide it exactly when the fleet is least healthy.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 6);

        var result = await BuildService(
            new FakeRunnerClient { ListError = new HttpRequestException("connection refused") })
            .GetAsync(CancellationToken.None);

        result.RunnerConsulted.ShouldBeFalse("false means nobody asked, which is not the same claim as 'nothing disagrees'");
        result.Items.ShouldContain(i => i.TaskId == task, "the DB-derived rows are unaffected");
    }

    [Test]
    public async Task a_runner_that_answers_is_recorded_as_consulted()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 6);

        var result = await BuildService(new FakeRunnerClient()).GetAsync(CancellationToken.None);

        result.RunnerConsulted.ShouldBeTrue();
    }

    // ---- ordering ---------------------------------------------------------------------------------

    [Test]
    public async Task rows_are_ranked_by_severity_and_then_oldest_first()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var blockedRecently = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Blocked, dispatchedMinutesAgo: 10);
        await scenario.AddTaskEventAsync(blockedRecently, AgentTaskEventType.Blocked, "recent", minutesAgo: 2);
        var blockedLongAgo = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Blocked, dispatchedMinutesAgo: 400);
        await scenario.AddTaskEventAsync(blockedLongAgo, AgentTaskEventType.Blocked, "old", minutesAgo: 300);
        var deadSession = await scenario.AddSessionAsync(SessionStatus.Failed, endedMinutesAgo: 30);
        var broken = await scenario.AddTaskAsync(deadSession, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 35);

        var items = await ItemsForAsync(scenario);
        var order = items.Select(i => i.TaskId).ToList();

        order.IndexOf(blockedLongAgo).ShouldBeLessThan(
            order.IndexOf(blockedRecently), "oldest stuck first inside a severity band");
        order.IndexOf(blockedRecently).ShouldBeLessThan(
            order.IndexOf(broken), "Critical outranks Error whatever the ages");
    }

    // ---- harness ------------------------------------------------------------------------------------

    /// <summary>Only the rows this test created — the shared-database rule, mechanised.</summary>
    private static async Task<List<AttentionItemDto>> ItemsForAsync(
        Scenario scenario, params SessionRunnerSessionDto[] runnerSessions)
    {
        var result = await BuildService(new FakeRunnerClient { Sessions = runnerSessions })
            .GetAsync(CancellationToken.None);
        return result.Items.Where(scenario.Owns).ToList();
    }

    /// <summary>What the runner says when it is running a session — the only status that can differ.</summary>
    private static SessionRunnerSessionDto Running(Guid sessionId, int? pid = 1234, int? hostPid = null) =>
        new(sessionId, pid, DateTime.UtcNow.AddMinutes(-45), "Running", null, AgentExitReason.Unknown,
            LastSequence: 900, HostPid: hostPid);

    private static AttentionService BuildService(ISessionRunnerClient runner) =>
        new(CreateContext(), runner, Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()), TimeProvider.System,
            NullLogger<AttentionService>.Instance);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>
    /// Seeds rows, remembers their ids, and deletes exactly those on dispose. The remembering is what
    /// makes every assertion in this file id-scoped; the deleting is what stops a half-dispatched
    /// task of ours from turning up in someone else's global sweep.
    /// </summary>
    private sealed class Scenario : IAsyncDisposable
    {
        private readonly List<Guid> _tasks = [];
        private readonly List<Guid> _sessions = [];
        private readonly List<Guid> _agents = [];
        private readonly List<Guid> _messages = [];

        public bool Owns(AttentionItemDto item) =>
            (item.TaskId is { } t && _tasks.Contains(t))
            || (item.MessageId is { } m && _messages.Contains(m))
            || (item.TaskId is null && item.MessageId is null && item.AgentId is { } a && _agents.Contains(a))
            // Session-scoped rows (SessionDisagreement) may carry no task, message or agent at all —
            // an unclaimed runner session is by definition owned by nothing the database knows.
            || (item.TaskId is null && item.MessageId is null && item.SessionId is { } s && _sessions.Contains(s));

        /// <summary>
        /// A session id this test owns for filtering purposes but deliberately never inserts — the
        /// unclaimed arm exists precisely for ids with no <c>AgentSessions</c> row behind them.
        /// </summary>
        public Guid ClaimSessionId()
        {
            var id = Guid.NewGuid();
            _sessions.Add(id);
            return id;
        }

        public async Task<Guid> AddSessionAsync(
            SessionStatus status = SessionStatus.Running, int? endedMinutesAgo = null)
        {
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = id,
                DefinitionName = "attention-test",
                AgentKind = AgentKind.ClaudeCode,
                Status = status,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                StartedAt = DateTime.UtcNow.AddHours(-1),
                LastSeenAt = DateTime.UtcNow,
                EndedAt = endedMinutesAgo is { } ago ? DateTime.UtcNow.AddMinutes(-ago) : null,
                FailureReason = status == SessionStatus.Failed ? "the process vanished" : null,
            });
            await db.SaveChangesAsync();
            _sessions.Add(id);
            return id;
        }

        public async Task<Guid> AddAgentAsync(Guid? persistentSession = null)
        {
            var id = Guid.NewGuid();
            var name = $"attn-{id:N}"[..16];
            await using var db = CreateContext();
            db.Agents.Add(new Agent
            {
                Id = id,
                Name = name,
                Slug = name,
                WorkingDirectory = Path.GetTempPath(),
                Details = "Attention projection test agent.",
                Status = AgentStatus.Running,
                ModelLevel = AgentModelLevel.Medium,
                IsPoolDelegate = true,
                PersistentSessionId = persistentSession?.ToString("D"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            _agents.Add(id);
            return id;
        }

        public async Task AddChannelAsync(Guid agentId)
        {
            await using var db = CreateContext();
            db.ChatChannels.Add(new ChatChannel
            {
                Id = Guid.NewGuid(),
                Provider = "telegram",
                ExternalId = $"attn-{Guid.NewGuid():N}",
                Kind = ChatChannelKind.Direct,
                AgentId = agentId,
                Enabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task<Guid> AddTaskAsync(
            Guid sessionId,
            AgentTaskStatus status,
            int dispatchedMinutesAgo,
            int expectedMinutes = 10,
            int checkCount = 0,
            DateTime? nextCheckAt = null,
            int? completedMinutesAgo = null,
            string? failureReason = null,
            decimal costUsd = 0m)
        {
            var id = Guid.NewGuid();
            var dispatched = DateTime.UtcNow.AddMinutes(-dispatchedMinutesAgo);
            await using var db = CreateContext();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = $"attention test {id:N}"[..24],
                Goal = "the thing under test",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                AgentSessionId = sessionId,
                Status = status,
                ReplyTo = AgentTaskReplyTo.Session,
                ParentSessionId = Guid.NewGuid(),
                ExpectedDurationMinutes = expectedMinutes,
                CheckCount = checkCount,
                NextCheckAt = nextCheckAt,
                FailureReason = failureReason,
                CostUsd = costUsd,
                CreatedAt = dispatched,
                DispatchedAt = dispatched,
                CompletedAt = completedMinutesAgo is { } done ? DateTime.UtcNow.AddMinutes(-done) : null,
            });
            await db.SaveChangesAsync();
            _tasks.Add(id);
            return id;
        }

        public async Task AddTaskEventAsync(Guid taskId, AgentTaskEventType type, string detail, int minutesAgo)
        {
            await using var db = CreateContext();
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = taskId,
                Type = type,
                Detail = detail,
                At = DateTime.UtcNow.AddMinutes(-minutesAgo),
            });
            await db.SaveChangesAsync();
        }

        public async Task AddTranscriptAsync(
            Guid sessionId, params (string Kind, string? Text, string? Tool)[] entries)
        {
            var at = DateTime.UtcNow.AddMinutes(-5);
            var seq = 0L;
            await using var db = CreateContext();
            foreach (var (kind, text, tool) in entries)
            {
                seq++;
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = seq,
                    Kind = kind,
                    Uuid = $"attn-{Guid.NewGuid():N}",
                    Text = text,
                    ToolName = tool,
                    StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
                    Timestamp = at.AddSeconds(seq),
                    CreatedAt = at.AddSeconds(seq),
                });
            }
            await db.SaveChangesAsync();
        }

        public async Task AddDelegationBriefAsync(Guid sessionId, Guid taskId, QueuedMessageStatus status)
        {
            await using var db = CreateContext();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Body = DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the thing.",
                Status = status,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Delegation,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
        }

        public async Task<Guid> AddQueuedMessageAsync(Guid sessionId, string body, int attempts)
        {
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = id,
                AgentSessionId = sessionId,
                Body = body,
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Channel,
                DeliveryAttempts = attempts,
                LastDeliveryStartedAt = DateTime.UtcNow.AddMinutes(-4),
                CreatedAt = DateTime.UtcNow.AddMinutes(-15),
            });
            await db.SaveChangesAsync();
            _messages.Add(id);
            return id;
        }

        public async Task AddIncidentAsync(
            Guid agentId, Guid? sessionId, AgentIncidentKind kind, AlertSeverity severity,
            string message, int minutesAgo)
        {
            await using var db = CreateContext();
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                SessionId = sessionId,
                Kind = kind,
                Severity = severity,
                Message = message,
                CreatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
            });
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.TranscriptEntries.Where(e => _sessions.Contains(e.AgentSessionId)).ExecuteDeleteAsync();
            await db.SessionQueuedMessages.Where(m => _sessions.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
            await db.AgentTaskEvents.Where(e => _tasks.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            await db.AgentIncidents.Where(i => _agents.Contains(i.AgentId)).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => c.AgentId != null && _agents.Contains(c.AgentId!.Value))
                .ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => _sessions.Contains(s.Id)).ExecuteDeleteAsync();
            await db.Agents.Where(a => _agents.Contains(a.Id)).ExecuteDeleteAsync();
        }
    }

    private sealed class FakeRunnerClient : ISessionRunnerClient
    {
        public IReadOnlyList<SessionRunnerSessionDto> Sessions { get; init; } = [];
        public Exception? ListError { get; init; }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            ListError is not null
                ? Task.FromException<IReadOnlyList<SessionRunnerSessionDto>>(ListError)
                : Task.FromResult(Sessions);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }
}

/// <summary>
/// The one thing the service tests above cannot catch: the WIRING. A missing DI registration is a
/// 500 and a mistyped group route is a 404, and both are invisible to every test that constructs
/// the service by hand.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class AttentionApiTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AntiphonWebAppFactory _factory;
    private Guid _taskId;

    public AttentionApiTests(AntiphonWebAppFactory factory) => _factory = factory;

    [After(Test)]
    public async Task CleanupAsync()
    {
        if (_taskId == Guid.Empty)
            return;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.AgentTasks.Where(t => t.Id == _taskId).ExecuteDeleteAsync();
    }

    [Test]
    public async Task the_attention_route_serves_the_projection()
    {
        _taskId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AgentTasks.Add(new AgentTask
            {
                Id = _taskId,
                RootTaskId = _taskId,
                Title = "api wiring probe",
                Goal = "prove the route exists",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Code,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = Path.GetTempPath(),
                Status = AgentTaskStatus.Blocked,
                ReplyTo = AgentTaskReplyTo.None,
                FailureReason = "Which branch should this land on?",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20),
                DispatchedAt = DateTime.UtcNow.AddMinutes(-20),
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/attention");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AttentionDto>(Json);
        payload.ShouldNotBeNull();
        // Scoped to the row this test made — the projection is fleet-global and returns other
        // suites' work by design.
        payload!.Items.Single(i => i.TaskId == _taskId).Kind.ShouldBe(AttentionKind.BlockedQuestion);
        payload.GeneratedAt.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-5));
    }
}
