using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
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
/// The delivery backstop (CARD-0003/CARD-0020): a Dispatched task whose session has ZERO transcript
/// entries after the delivery window never received its brief and must FAIL with the reason, never
/// sit Dispatched forever and never escalate. Live miss 2026-08-09: four delegated tasks lost their
/// boot prompt to a pty-host race and every surface reported Running for up to 26 minutes.
///
/// A global-sweep suite: FailNeverStartedAsync scans every Dispatched task in the shared test
/// database, so this class takes NotInParallel with NO group key (see the shared-Postgres rule in
/// CLAUDE.md) and every assertion is scoped to rows this test created.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class AgentTaskDeliveryWatchdogTests
{
    [Test]
    public async Task Explicit_runner_transcript_mismatch_fails_the_task_but_does_not_kill_the_session()
    {
        var (harness, stopper) = CreateHarness(runnerClient: new MismatchRunnerClient());
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11, kind: AgentKind.Codex);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("cannot tail a 'codex' transcript");
        failed.FailureReason.ShouldContain("restart-session-runner.ps1");
        stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value,
            "positive runner blindness is evidence to withhold the destructive half of the watchdog");
    }

    [Test]
    public async Task a_dispatched_task_with_no_transcript_after_the_window_fails_with_the_reason()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldNotBeNull();
        failed.FailureReason.ShouldContain("never delivered", customMessage: "the reason must say WHAT went wrong");
        stopper.Killed.ShouldContain(task.AgentSessionId!.Value, "the never-started session must be stopped");
    }

    [Test]
    public async Task the_reason_names_a_brief_stranded_pending()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        await SeedBriefAsync(task.AgentSessionId!.Value, task.Id, QueuedMessageStatus.Pending);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .FailureReason.ShouldContain("Pending", customMessage: "the queue's own state is the evidence");
    }

    [Test]
    public async Task a_real_user_prompt_after_dispatch_means_the_task_is_left_alone()
    {
        // Slow work is the stall scan's business; a real prompt after dispatch proves delivery happened.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 45);
        await SeedTranscriptEntryAsync(task.AgentSessionId!.Value);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value);
    }

    [Test]
    public async Task a_reused_session_whose_new_brief_never_landed_is_failed()
    {
        // CARD-0077: inherited history + the compact's five housekeeping records used to make
        // "any transcript entry" true, so the never-started branch was unreachable for every
        // reused session. Settlement's own filter sees zero turn prompts here — the watchdog must too.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        var dispatched = task.DispatchedAt!.Value;
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "[antiphon-task:deadbeef] The previous task's brief.",
            dispatched.AddMinutes(-20));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.TurnEnd, null, dispatched.AddMinutes(-12));
        await SeedReuseCompactionHousekeepingAsync(sessionId, dispatched.AddMinutes(2));

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed, "inherited history is not this task starting");
        failed.FailureReason.ShouldContain("never delivered");
        failed.FailureReason.ShouldContain("no turn prompt of either kind for this task");
        stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task a_reused_session_with_a_real_prompt_after_dispatch_is_left_alone()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        var dispatched = task.DispatchedAt!.Value;
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "[antiphon-task:deadbeef] The previous task's brief.",
            dispatched.AddMinutes(-20));
        await SeedReuseCompactionHousekeepingAsync(sessionId, dispatched.AddMinutes(2));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            dispatched.AddMinutes(4));

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        stopper.Killed.ShouldNotContain(sessionId);
    }

    [Test]
    public async Task a_fresh_session_with_zero_entries_still_fails()
    {
        // Regression: the new predicate must degenerate to today's behaviour on a brand-new session.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("never delivered");
        stopper.Killed.ShouldContain(task.AgentSessionId!.Value);
    }

    [Test]
    public async Task a_null_timestamp_real_prompt_counts_as_started()
    {
        // Clock tolerance mirroring CARD-0056 / LoadPromptsInSpanAsync: an untimestamped record
        // cannot be placed relative to DispatchedAt, so it is kept rather than dropped.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        await SeedEntryAsync(
            task.AgentSessionId!.Value, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            timestamp: null);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value);
    }

    [Test]
    public async Task a_previous_tasks_brief_is_not_this_tasks_queued_evidence()
    {
        // On a reused session the earlier brief is Sent and would have made the evidence read
        // "the brief is marked Sent" for THIS task, which never queued one.
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        await using (var db = CreateContext())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Body = "[antiphon-task:deadbeef]\n\nThe previous task.",
                Status = QueuedMessageStatus.Sent,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Delegation,
                CreatedAt = task.DispatchedAt!.Value.AddMinutes(-20),
                SentAt = task.DispatchedAt!.Value.AddMinutes(-19),
            });
            await db.SaveChangesAsync();
        }
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "[antiphon-task:deadbeef] The previous task's brief.",
            task.DispatchedAt!.Value.AddMinutes(-20));

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .FailureReason.ShouldContain("no brief was queued for this task after dispatch");
    }

    // ---- CARD-0135: a queued brief is started, for the watchdog too --------------------------

    [Test]
    public async Task a_queued_brief_after_dispatch_is_left_alone()
    {
        // The card's shape: the only post-dispatch prompt is a marker-bearing QueuedUserPrompt.
        // Delivery already marked the queue row Sent; the watchdog used to fail and kill at T+10.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        await SeedBriefAsync(sessionId, task.Id, QueuedMessageStatus.Sent);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            task.DispatchedAt!.Value.AddMinutes(1));

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        stopper.Killed.ShouldNotContain(sessionId);
    }

    [Test]
    public async Task a_reused_session_with_a_queued_brief_after_dispatch_is_left_alone()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        var dispatched = task.DispatchedAt!.Value;
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "[antiphon-task:deadbeef] The previous task's brief.",
            dispatched.AddMinutes(-20));
        await SeedReuseCompactionHousekeepingAsync(sessionId, dispatched.AddMinutes(2));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            dispatched.AddMinutes(4));

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        stopper.Killed.ShouldNotContain(sessionId);
    }

    [Test]
    public async Task no_prompt_of_either_kind_after_dispatch_still_fails_with_either_kind_wording()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        await SeedBriefAsync(task.AgentSessionId!.Value, task.Id, QueuedMessageStatus.Sent);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("never delivered");
        failed.FailureReason.ShouldContain("no turn prompt of either kind for this task");
        stopper.Killed.ShouldContain(task.AgentSessionId!.Value);
    }

    [Test]
    public async Task a_previous_tasks_queued_prompt_is_not_this_tasks_started_evidence()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        await SeedEntryAsync(
            sessionId, TranscriptKinds.QueuedUserPrompt,
            "[antiphon-task:deadbeef] The previous task's queued brief.",
            task.DispatchedAt!.Value.AddMinutes(-20));

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("no turn prompt of either kind for this task");
        stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task queued_only_evidence_is_named_in_the_information_log()
    {
        var logs = new List<string>();
        var (harness, _) = CreateHarness(logs: logs);
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        await SeedEntryAsync(
            task.AgentSessionId!.Value, TranscriptKinds.QueuedUserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            task.DispatchedAt!.Value.AddMinutes(1));

        await harness.FailNeverStartedAsync(CancellationToken.None);

        logs.ShouldContain(l => l.Contains("queued-only turn evidence", StringComparison.Ordinal)
            && l.Contains(task.AgentSessionId!.Value.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    /// The opposite failure to a lost boot prompt, and the one that actually stranded three tasks
    /// overnight on 2026-08-11: the session ran, worked and REPORTED, but no turn could be matched
    /// to the task, so nothing settled it. Starting is not the test of a healthy task — settling
    /// is. Red before this branch existed: the "any transcript entry" check above waved it through
    /// forever on the strength of a transcript it could not use.
    /// </summary>
    [Test]
    public async Task a_task_whose_report_could_never_be_correlated_fails_instead_of_hanging()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        await SeedTranscriptEntryAsync(sessionId);
        // CARD-0117 D9: a UserPrompt without a TurnEnd reads working, and the kill is withheld.
        // Arm 2 is not being deleted — the kill still fires on an idle session. The incident is
        // stamped AFTER DispatchedAt so S1's scope predicate still matches.
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, DateTime.UtcNow.AddSeconds(-30));
        await SeedUncorrelatedIncidentAsync(sessionId, minutesAgo: 5);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(
            AgentTaskStatus.Failed, "a task that reported but never correlated must not sit Dispatched");
        failed.FailureReason.ShouldContain(
            "could not be attributed", customMessage: "and the reason must not read as a lost prompt");
        failed.FailureReason.ShouldContain(
            sessionId.ToString(), customMessage: "the work may be real — say where to find it");
        stopper.Killed.ShouldContain(sessionId);
    }

    /// <summary>
    /// CARD-0117 S1: the live miss. A stale uncorrelated incident from a settled earlier task
    /// must not take arm 2; with the brief still Pending the watchdog fails with arm 1's wording.
    /// Idle so S4's defer does not swallow it.
    /// </summary>
    [Test]
    public async Task a_stale_uncorrelated_incident_does_not_take_arm_2()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        var dispatched = task.DispatchedAt!.Value;
        await SeedIdleTurnSinceAsync(sessionId, dispatched);
        await SeedBriefAsync(sessionId, task.Id, QueuedMessageStatus.Pending);
        await SeedUncorrelatedIncidentAsync(sessionId, minutesAgo: 20);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("still queued Pending");
        failed.FailureReason.ShouldNotContain("could not be attributed");
        failed.FailureReason.ShouldContain("none of them was this task's brief");
        stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task a_stale_uncorrelated_incident_with_a_sent_brief_is_left_alone()
    {
        // S1 in isolation: started, brief Sent, incident before DispatchedAt → arm 2 does not
        // fire, and arm 1 is not taken either. The task stays Dispatched.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        await SeedIdleTurnSinceAsync(sessionId, task.DispatchedAt!.Value);
        await SeedBriefAsync(sessionId, task.Id, QueuedMessageStatus.Sent);
        await SeedUncorrelatedIncidentAsync(sessionId, minutesAgo: 20);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        stopper.Killed.ShouldNotContain(sessionId);
    }

    /// <summary>
    /// CARD-0117 S2: prompts exist since dispatch AND the brief is Pending → arm 1, and the
    /// reason says those prompts were not this task's brief.
    /// </summary>
    [Test]
    public async Task prompts_since_dispatch_with_a_pending_brief_take_arm_1()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        await SeedIdleTurnSinceAsync(sessionId, task.DispatchedAt!.Value);
        await SeedBriefAsync(sessionId, task.Id, QueuedMessageStatus.Pending);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("none of them was this task's brief");
        failed.FailureReason.ShouldContain("still queued Pending");
        failed.FailureReason.ShouldNotContain("could not be attributed");
        stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task a_pending_brief_with_zero_attempts_reads_never_attempted()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        await SeedBriefAsync(task.AgentSessionId!.Value, task.Id, QueuedMessageStatus.Pending, deliveryAttempts: 0);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var reason = (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).FailureReason;
        reason.ShouldContain("never attempted");
        reason.ShouldNotContain("every delivery attempt failed");
    }

    [Test]
    public async Task a_pending_brief_with_failed_attempts_names_the_count()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        await SeedBriefAsync(task.AgentSessionId!.Value, task.Id, QueuedMessageStatus.Pending, deliveryAttempts: 2);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .FailureReason.ShouldContain("2 delivery attempt(s) failed");
    }

    [Test]
    public async Task a_pending_brief_parked_at_the_cap_says_so()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        await SeedBriefAsync(task.AgentSessionId!.Value, task.Id, QueuedMessageStatus.Pending, deliveryAttempts: 3);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .FailureReason.ShouldContain("parked at MaxDeliveryAttempts");
    }

    /// <summary>
    /// CARD-0117 S4 / D8: a working session with a Pending brief is neither failed nor killed.
    /// The same task with the session idle is failed and killed.
    /// </summary>
    [Test]
    public async Task a_working_session_with_a_pending_brief_is_neither_failed_nor_killed()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        await SeedBriefAsync(sessionId, task.Id, QueuedMessageStatus.Pending);
        await SeedWorkingSinceAsync(sessionId, task.DispatchedAt!.Value);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched, "the watchdog declined to judge");
        stopper.Killed.ShouldNotContain(sessionId);
    }

    [Test]
    public async Task the_same_pending_brief_on_an_idle_session_is_failed_and_killed()
    {
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        await SeedBriefAsync(sessionId, task.Id, QueuedMessageStatus.Pending);
        await SeedIdleTurnSinceAsync(sessionId, task.DispatchedAt!.Value);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id)).Status.ShouldBe(AgentTaskStatus.Failed);
        stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task a_sent_brief_on_a_working_session_is_not_deferred()
    {
        // The defer is gated on the queue row, not on working alone. A Sent brief with no turn
        // prompt (started = false) and a working tail (inherited ToolCall) takes arm 1 and Fails;
        // D9 withholds the kill.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        var dispatched = task.DispatchedAt!.Value;
        await SeedBriefAsync(sessionId, task.Id, QueuedMessageStatus.Sent);
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, dispatched.AddMinutes(-5));
        await SeedEntryAsync(sessionId, TranscriptKinds.ToolCall, null, dispatched.AddMinutes(2));

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed, "Sent is not the defer gate");
        failed.FailureReason.ShouldContain("never delivered");
        stopper.Killed.ShouldNotContain(sessionId, "D9: do not kill a working session");
    }

    [Test]
    public async Task arm_2_on_a_working_session_fails_the_task_but_does_not_kill()
    {
        // CARD-0117 D9 on arm 2: a turn ended and could not be attributed, but the session is
        // mid-turn again. Fail the task; leave the process.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 11);
        var sessionId = task.AgentSessionId!.Value;
        await SeedWorkingSinceAsync(sessionId, task.DispatchedAt!.Value);
        await SeedBriefAsync(sessionId, task.Id, QueuedMessageStatus.Sent);
        await SeedUncorrelatedIncidentAsync(sessionId, minutesAgo: 5);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("could not be attributed");
        stopper.Killed.ShouldNotContain(sessionId);
    }

    [Test]
    public async Task a_task_inside_the_delivery_window_is_not_touched()
    {
        // The stranded-queue watchdog gets the whole window to redeliver a reverted brief first.
        var (harness, stopper) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched);
        stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value);
    }

    /// <summary>
    /// CARD-0046's grace has to have a clock, and CARD-0248's job is to not treat that clock as a
    /// settle-anyway. The sweep still finds the unmarked FinalMessageMissing turn past the grace
    /// and nudges it; it then HOLDS until the nudge is answered or provably ignored — the same
    /// boundary, and an undelivered WhenIdle nudge, can never be the settle-anyway boundary.
    /// </summary>
    [Test]
    public async Task a_deferred_settlement_is_swept_after_the_grace_window()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);
        var sessionId = task.AgentSessionId!.Value;
        await SeedSplitTurnTailAsync(sessionId, task.Id, storedMinutesAgo: 3);

        // The sweep TickAsync runs, driven directly: TickAsync itself also dispatches every Queued
        // task in the shared test database, which is not this test's business (CLAUDE.md's
        // shared-Postgres rule) — the wiring is the one call in TickAsync above the early return.
        (await harness.SettleDeferredReportsAsync(CancellationToken.None))
            .ShouldBeGreaterThanOrEqualTo(1, "a global sweep count, so other suites' rows may add to it");

        await using (var mid = CreateContext())
        {
            var nudged = await mid.AgentTasks.SingleAsync(t => t.Id == task.Id);
            nudged.Status.ShouldBe(AgentTaskStatus.Dispatched);
            nudged.ReportNudgedAt.ShouldNotBeNull();
            nudged.ReportNudgedSequence.ShouldBe(3);
            nudged.ReportNudgeMessageId.ShouldNotBeNull();
            var queued = await mid.SessionQueuedMessages.SingleAsync(
                m => m.Id == nudged.ReportNudgeMessageId);
            queued.Origin.ShouldBe(QueuedMessageOrigin.Delegation);
            queued.SentAt.ShouldBeNull();
        }

        (await harness.SettleDeferredReportsAsync(CancellationToken.None))
            .ShouldBeGreaterThanOrEqualTo(1);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(
            AgentTaskStatus.Dispatched,
            "CARD-0248: the same boundary that was nudged can never be the settle-anyway boundary, "
            + "and the nudge has not even been delivered");
        settled.Result.ShouldBeNull();
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Legacy);
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Completed))
            .ShouldBeFalse();
    }

    [Test]
    public async Task an_undelivered_nudge_never_settles_the_task()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);
        var sessionId = task.AgentSessionId!.Value;
        await SeedSplitTurnTailAsync(sessionId, task.Id, storedMinutesAgo: 3);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);
        await harness.SettleDeferredReportsAsync(CancellationToken.None);
        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.ReportNudgedAt.ShouldNotBeNull();
        stored.Result.ShouldBeNull();
        var queued = await verify.SessionQueuedMessages.SingleAsync(
            m => m.Id == stored.ReportNudgeMessageId);
        queued.SentAt.ShouldBeNull();
    }

    [Test]
    public async Task a_delivered_nudge_with_the_same_boundary_still_does_not_settle()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);
        var sessionId = task.AgentSessionId!.Value;
        await SeedSplitTurnTailAsync(sessionId, task.Id, storedMinutesAgo: 3);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);
        await MarkNudgeDeliveredAsync(sessionId, DateTime.UtcNow.AddMinutes(-10));
        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(
            AgentTaskStatus.Dispatched,
            "CARD-0248: a delivered nudge still cannot settle the same boundary it was issued against");
        stored.Result.ShouldBeNull();
    }

    [Test]
    public async Task an_unmarked_reply_after_a_delivered_nudge_settles_unmarked_after_nudge()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);
        var sessionId = task.AgentSessionId!.Value;
        await SeedSplitTurnTailAsync(sessionId, task.Id, storedMinutesAgo: 3);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);
        var sentAt = DateTime.UtcNow.AddMinutes(-10);
        await MarkNudgeDeliveredAsync(sessionId, sentAt);
        const string reply = "Here is the report without a closing line.";
        await SeedPostNudgeTurnAsync(sessionId, reply, sentAt.AddMinutes(1), closingVerdict: false);

        // A with-text turn is the transcript observer's job, not the deferred sweep
        // (arm 1's predicate is "no AssistantText for the boundary's ApiCallId").
        await CreateReplyService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.UnmarkedAfterNudge);
        settled.Result.ShouldBe(reply);
        settled.Result.ShouldNotBe("I'll start by reading the spec.");
    }

    [Test]
    public async Task a_marked_reply_after_a_delivered_nudge_settles_marked()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);
        var sessionId = task.AgentSessionId!.Value;
        await SeedSplitTurnTailAsync(sessionId, task.Id, storedMinutesAgo: 3);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);
        var sentAt = DateTime.UtcNow.AddMinutes(-10);
        await MarkNudgeDeliveredAsync(sessionId, sentAt);
        const string reply = "Shipped Fizz. 142 passed, 0 failed.";
        await SeedPostNudgeTurnAsync(
            sessionId, reply, sentAt.AddMinutes(1), closingVerdict: true, taskId: task.Id);

        await CreateReplyService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        settled.Result.ShouldBe(reply);
    }

    [Test]
    public async Task a_textless_boundary_after_a_delivered_nudge_waits_the_response_window()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);
        var sessionId = task.AgentSessionId!.Value;
        await SeedSplitTurnTailAsync(sessionId, task.Id, storedMinutesAgo: 3);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        // Past FinalMessageGrace (120s) so the sweep will hand off, but still inside
        // ReportNudgeResponseSeconds (240s). CreatedAt must also post-date SentAt.
        var sentAt = DateTime.UtcNow.AddMinutes(-3);
        await MarkNudgeDeliveredAsync(sessionId, sentAt);
        await SeedPostNudgeTurnAsync(
            sessionId, assistantText: null, createdAt: DateTime.UtcNow.AddMinutes(-2.5));

        await harness.SettleDeferredReportsAsync(CancellationToken.None);
        await using (var insideWindow = CreateContext())
        {
            (await insideWindow.AgentTasks.SingleAsync(t => t.Id == task.Id))
                .Status.ShouldBe(
                    AgentTaskStatus.Dispatched,
                    "a text-less post-nudge boundary inside ReportNudgeResponseSeconds must wait");
        }

        await using (var db = CreateContext())
        {
            var stored = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            var nudge = await db.SessionQueuedMessages.SingleAsync(m => m.Id == stored.ReportNudgeMessageId);
            nudge.SentAt = DateTime.UtcNow.AddMinutes(-10);
            await db.SaveChangesAsync();
        }

        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.FinalMessageMissing);
        settled.Result.ShouldBe("I'll start by reading the spec.");
    }

    [Test]
    public async Task an_unchanged_boundary_is_not_rehanded_within_the_rehand_interval()
    {
        var logs = new List<string>();
        var (harness, _) = CreateHarness(logs: logs, settings: new DelegationSettings());
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);
        var sessionId = task.AgentSessionId!.Value;
        await SeedSplitTurnTailAsync(sessionId, task.Id, storedMinutesAgo: 3);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);
        var needle = sessionId.ToString();
        logs.Count(l => l.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && l.Contains("no text from the turn-ending response", StringComparison.Ordinal))
            .ShouldBe(1);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);
        logs.Count(l => l.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && l.Contains("no text from the turn-ending response", StringComparison.Ordinal))
            .ShouldBe(
                1,
                "CARD-0248: an unchanged boundary is not re-handed within ReportSweepRehandSeconds");
    }

    [Test]
    public async Task a_cancelled_end_is_skipped_by_the_deferred_report_sweep()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);
        var sessionId = task.AgentSessionId!.Value;
        var at = DateTime.UtcNow.AddMinutes(-3);
        var apiCallId = $"msg_{Guid.NewGuid():N}";
        await using (var db = CreateContext())
        {
            db.TranscriptEntries.AddRange(
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 1,
                    Kind = TranscriptKinds.UserPrompt,
                    Uuid = $"cancelled-{Guid.NewGuid():N}",
                    Role = "user",
                    Text = DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
                    Timestamp = at,
                    CreatedAt = at,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 2,
                    Kind = TranscriptKinds.AssistantText,
                    Uuid = $"cancelled-{Guid.NewGuid():N}",
                    Role = "assistant",
                    Text = "I'll start by reading the spec.",
                    ApiCallId = apiCallId,
                    Timestamp = at,
                    CreatedAt = at,
                },
                new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = 3,
                    Kind = TranscriptKinds.TurnEnd,
                    Uuid = $"cancelled-{Guid.NewGuid():N}",
                    Role = "assistant",
                    StopReason = TranscriptKinds.StopReasons.Cancelled,
                    ApiCallId = apiCallId,
                    Timestamp = at,
                    CreatedAt = at,
                });
            await db.SaveChangesAsync();
        }

        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(
            AgentTaskStatus.Dispatched,
            "a cancelled end is never a report — the grace sweep must not settle it");
        stored.Result.ShouldBeNull();
        stored.ReportNudgedAt.ShouldBeNull();
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Completed))
            .ShouldBeFalse();
    }

    [Test]
    public async Task a_deferred_settlement_inside_the_grace_window_is_left_alone()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 3);
        await SeedSplitTurnTailAsync(task.AgentSessionId!.Value, task.Id, storedMinutesAgo: 0);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched, "the text is very probably still ~1 s away");
    }

    /// <summary>
    /// Slice 4's grace needs the same clock, for the same reason: a turn that launched background
    /// subagents defers until their notifications return, and a subagent can die without ever
    /// notifying. Nothing else would come back for that task — the announcement turn already ended,
    /// so no transcript arrives to re-trigger settlement.
    /// </summary>
    [Test]
    public async Task a_task_waiting_on_a_dead_subagent_is_swept_after_the_subagent_grace()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 40);
        await SeedAbandonedSubagentFanOutAsync(
            task.AgentSessionId!.Value, task.Id, storedMinutesAgo: 35);

        (await harness.SettleDeferredReportsAsync(CancellationToken.None))
            .ShouldBeGreaterThanOrEqualTo(1, "a global sweep count, so other suites' rows may add to it");

        await using (var mid = CreateContext())
        {
            var nudged = await mid.AgentTasks.SingleAsync(t => t.Id == task.Id);
            nudged.Status.ShouldBe(AgentTaskStatus.Dispatched);
            nudged.ReportNudgedAt.ShouldNotBeNull("unmarked announcement is nudged once");
        }

        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(
            AgentTaskStatus.Dispatched,
            "CARD-0248: settling the fan-out announcement on an unanswered, undelivered nudge "
            + "is the production bug on the subagent arm");
        settled.Result.ShouldBeNull();
    }

    [Test]
    public async Task an_abandoned_fanout_settles_after_a_delivered_nudge_and_reply()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 40);
        var sessionId = task.AgentSessionId!.Value;
        await SeedAbandonedSubagentFanOutAsync(sessionId, task.Id, storedMinutesAgo: 35);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);
        // Past SubagentGraceMinutes (30) so extraction does not re-defer, and after SentAt so
        // the delivery gate passes.
        var sentAt = DateTime.UtcNow.AddMinutes(-40);
        await MarkNudgeDeliveredAsync(sessionId, sentAt);
        const string reply = "Reviewers never returned; here is what I have.";
        await SeedPostNudgeTurnAsync(sessionId, reply, DateTime.UtcNow.AddMinutes(-32), closingVerdict: false);

        await CreateReplyService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.UnmarkedAfterNudge);
        settled.Result.ShouldBe(reply);
        settled.Result.ShouldNotContain("Four review agents are running in parallel");
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning
                && e.Detail.Contains("background subagent")))
            .ShouldBeTrue("AbandonedSubagents is re-derived on the new extraction");
    }

    [Test]
    public async Task a_task_still_inside_the_subagent_grace_is_left_alone()
    {
        var (harness, _) = CreateHarness();
        var task = await SeedDispatchedTaskAsync(dispatchedMinutesAgo: 5);
        await SeedAbandonedSubagentFanOutAsync(task.AgentSessionId!.Value, task.Id, storedMinutesAgo: 2);

        await harness.SettleDeferredReportsAsync(CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Status.ShouldBe(AgentTaskStatus.Dispatched, "the reviewers are still working");
    }

    // ---- CARD-0085: recover a false-negative delivery-failed --------------------------------

    [Test]
    public async Task zero_transcript_plus_worktree_commit_recovers_succeeded_and_does_not_kill()
    {
        using var repo = new ScratchGitRepo("card0085-wt");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.GitAsync("branch", "feat/parent");

        var (worktrees, _) = CreateWorktreeService(repo);
        var draft = NewWorktreeDraft(repo.Path, "feat/parent");
        await worktrees.CreateForTaskAsync(draft, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(draft.WorktreePath!, "plan.md"), "the plan\n");
        (await ScratchGitRepo.GitInAsync(draft.WorktreePath!, "add", ".")).Ok.ShouldBeTrue();
        (await ScratchGitRepo.GitInAsync(draft.WorktreePath!, "commit", "-m", "docs: CARD-0083 plan")).Ok
            .ShouldBeTrue();
        var sha = (await ScratchGitRepo.GitInAsync(draft.WorktreePath!, "rev-parse", "--short", "HEAD"))
            .StdOut.Trim();

        var (harness, stopper) = CreateHarness();
        var task = await SeedRecoverableTaskAsync(
            dispatchedMinutesAgo: 11,
            workspace: WorkspaceMode.Worktree,
            workingDirectory: repo.Path,
            title: "CARD-0083 plan",
            worktreePath: draft.WorktreePath,
            worktreeBranch: draft.WorktreeBranch,
            mergeTargetRef: "feat/parent",
            repoPath: repo.Path,
            sessionCwd: draft.WorktreePath);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var recovered = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        recovered.Status.ShouldBe(AgentTaskStatus.Succeeded, "the work landed — Failed would redispatch on it");
        recovered.RecoveredAt.ShouldNotBeNull("a recovery settlement is not an observed completion");
        recovered.RecoveredAt.ShouldBe(recovered.CompletedAt);
        recovered.Result.ShouldNotBeNull();
        recovered.Result.ShouldContain(sha, customMessage: "Result names the commit that recovered it");
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning))
            .ShouldBeTrue("Succeeded but loud — the caveat is an event, not a silent flip");
        (await verify.AgentIncidents.AnyAsync(
            i => i.AgentId == task.AgentId && i.Kind == AgentIncidentKind.DelegateBindRefusalRecovered))
            .ShouldBeTrue("kind 24 is the recovery; TranscriptBindFailed stays the refusal");
        stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value, "do not kill a live unbound worker");
        (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == task.AgentSessionId))
            .ShouldBe(0, "recovery must not ingest or bind the refused file");
    }

    [Test]
    public async Task zero_transcript_plus_unrelated_shared_commit_still_fails_and_kills()
    {
        using var repo = new ScratchGitRepo("card0085-shared-unrelated");
        await repo.CommitFileAsync("README.md", "base\n");
        await repo.CommitFileAsync("neighbour.md", "someone else's work\n");

        var (harness, stopper) = CreateHarness();
        var task = await SeedRecoverableTaskAsync(
            dispatchedMinutesAgo: 11,
            workingDirectory: repo.Path,
            title: "Do the thing",
            sessionCwd: repo.Path);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(
            AgentTaskStatus.Failed,
            "a neighbouring commit on a shared checkout is not this task's — CARD-0006");
        failed.FailureReason.ShouldContain("never delivered");
        stopper.Killed.ShouldContain(task.AgentSessionId!.Value);
    }

    [Test]
    public async Task zero_transcript_plus_shared_commit_citing_the_card_recovers()
    {
        using var repo = new ScratchGitRepo("card0085-shared-card");
        await repo.CommitFileAsync("README.md", "base\n");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "plan.md"), "the plan\n");
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "docs(providers): CARD-0083 plan - the contract");
        var sha = (await repo.GitReadAsync("rev-parse", "--short", "HEAD")).Trim();

        var (harness, stopper) = CreateHarness();
        var task = await SeedRecoverableTaskAsync(
            dispatchedMinutesAgo: 11,
            workingDirectory: repo.Path,
            title: "CARD-0083 plan the provider contract",
            sessionCwd: repo.Path);

        await harness.FailNeverStartedAsync(CancellationToken.None);

        await using var verify = CreateContext();
        var recovered = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        recovered.Status.ShouldBe(AgentTaskStatus.Succeeded);
        recovered.Result.ShouldContain(sha);
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning)).ShouldBeTrue();
        stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value);
    }

    [Test]
    public async Task zero_transcript_plus_later_jsonl_needle_recovers_without_ingesting()
    {
        var projectsRoot = Directory.CreateTempSubdirectory("card0085-jsonl").FullName;
        var cwd = Directory.CreateTempSubdirectory("card0085-cwd").FullName;
        string? jsonl = null;
        try
        {
            var (harness, stopper) = CreateHarness(new DelegateBindRefusalRecoverySettings
            {
                ClaudeProjectsRoot = projectsRoot,
            });
            var task = await SeedRecoverableTaskAsync(
                dispatchedMinutesAgo: 11,
                workingDirectory: cwd,
                sessionCwd: cwd);

            var encoded = DelegateBindRefusalRecovery.EncodeClaudeProjectDir(cwd);
            var projectDir = Path.Combine(projectsRoot, encoded);
            Directory.CreateDirectory(projectDir);
            jsonl = Path.Combine(projectDir, task.AgentSessionId!.Value.ToString("D") + ".jsonl");
            var started = DateTime.UtcNow;
            await File.WriteAllTextAsync(jsonl,
                JsonlUser(
                    cwd,
                    $"{DelegationReportFormatter.TaskMarker(task.Id)} the plan is written.",
                    started)
                + "\n"
                + JsonlAssistant(cwd, "done.", started.AddMinutes(2))
                + "\n");

            await harness.FailNeverStartedAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var recovered = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
            recovered.Status.ShouldBe(AgentTaskStatus.Succeeded);
            recovered.Result.ShouldContain(jsonl, customMessage: "incident/result names the file, not a bind");
            (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == task.AgentSessionId))
                .ShouldBe(0, "Arm B does not ingest. C4 stays refused.");
            var incident = await verify.AgentIncidents.SingleAsync(
                i => i.AgentId == task.AgentId && i.Kind == AgentIncidentKind.DelegateBindRefusalRecovered);
            incident.Message.ShouldContain(jsonl);
            incident.Severity.ShouldBe(AlertSeverity.Warning);
            stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value);
        }
        finally
        {
            TryDeleteTree(projectsRoot);
            TryDeleteTree(cwd);
        }
    }

    [Test]
    public async Task a_queued_command_attachment_is_jsonl_recovery_evidence()
    {
        // CARD-0135 S4: an unbound session whose on-disk JSONL carries the brief only as a
        // queued_command attachment. CARD-0127's type=="user" gate used to miss it. The
        // 753cdb4e / another-known-session / assistant-record / C3-refused regressions below
        // stay red-if-widened-wrong — they are this slice's test 17.
        var projectsRoot = Directory.CreateTempSubdirectory("card0135-queued-jsonl").FullName;
        var cwd = Directory.CreateTempSubdirectory("card0135-queued-cwd").FullName;
        string? jsonl = null;
        try
        {
            var (harness, stopper) = CreateHarness(new DelegateBindRefusalRecoverySettings
            {
                ClaudeProjectsRoot = projectsRoot,
            });
            var task = await SeedRecoverableTaskAsync(
                dispatchedMinutesAgo: 11,
                workingDirectory: cwd,
                sessionCwd: cwd);

            var encoded = DelegateBindRefusalRecovery.EncodeClaudeProjectDir(cwd);
            var projectDir = Path.Combine(projectsRoot, encoded);
            Directory.CreateDirectory(projectDir);
            jsonl = Path.Combine(projectDir, task.AgentSessionId!.Value.ToString("D") + ".jsonl");
            var started = DateTime.UtcNow;
            await File.WriteAllTextAsync(jsonl,
                JsonlQueuedCommand(
                    cwd,
                    $"{DelegationReportFormatter.TaskMarker(task.Id)} the plan is written.",
                    started)
                + "\n"
                + JsonlAssistant(cwd, "done.", started.AddMinutes(2))
                + "\n");

            await harness.FailNeverStartedAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var recovered = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
            recovered.Status.ShouldBe(AgentTaskStatus.Succeeded);
            recovered.Result.ShouldContain(jsonl);
            (await verify.TranscriptEntries.CountAsync(t => t.AgentSessionId == task.AgentSessionId))
                .ShouldBe(0, "Arm B does not ingest. C4 stays refused.");
            stopper.Killed.ShouldNotContain(task.AgentSessionId!.Value);
        }
        finally
        {
            TryDeleteTree(projectsRoot);
            TryDeleteTree(cwd);
        }
    }

    [Test]
    public async Task a_codex_task_never_recovers_from_a_matching_claude_jsonl_regression_753cdb4e()
    {
        // CARD-0127: task 753cdb4e was Codex but recovered from another delegate's Claude JSONL.
        var projectsRoot = Directory.CreateTempSubdirectory("card0127-codex").FullName;
        var cwd = Directory.CreateTempSubdirectory("card0127-codex-cwd").FullName;
        try
        {
            var (harness, stopper) = CreateHarness(new DelegateBindRefusalRecoverySettings
            {
                ClaudeProjectsRoot = projectsRoot,
            });
            var task = await SeedRecoverableTaskAsync(
                dispatchedMinutesAgo: 11,
                workingDirectory: cwd,
                sessionCwd: cwd,
                kind: AgentKind.Codex);

            var projectDir = Path.Combine(projectsRoot, DelegateBindRefusalRecovery.EncodeClaudeProjectDir(cwd));
            Directory.CreateDirectory(projectDir);
            await File.WriteAllTextAsync(
                Path.Combine(projectDir, Guid.NewGuid().ToString("D") + ".jsonl"),
                JsonlUser(cwd, DelegationReportFormatter.TaskMarker(task.Id), DateTime.UtcNow) + "\n");

            await harness.FailNeverStartedAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
            failed.Status.ShouldBe(AgentTaskStatus.Failed);
            failed.FailureReason.ShouldContain("Boot prompt was never delivered");
            stopper.Killed.ShouldContain(task.AgentSessionId!.Value);
        }
        finally
        {
            TryDeleteTree(projectsRoot);
            TryDeleteTree(cwd);
        }
    }

    [Test]
    public async Task a_jsonl_named_for_another_known_session_is_not_recovery_evidence()
    {
        var projectsRoot = Directory.CreateTempSubdirectory("card0127-other-session").FullName;
        var cwd = Directory.CreateTempSubdirectory("card0127-other-session-cwd").FullName;
        try
        {
            var (harness, stopper) = CreateHarness(new DelegateBindRefusalRecoverySettings
            {
                ClaudeProjectsRoot = projectsRoot,
            });
            var task = await SeedRecoverableTaskAsync(
                dispatchedMinutesAgo: 11,
                workingDirectory: cwd,
                sessionCwd: cwd);
            var otherSessionId = await SeedOtherSessionAsync(cwd, task.DispatchedAt!.Value);

            var projectDir = Path.Combine(projectsRoot, DelegateBindRefusalRecovery.EncodeClaudeProjectDir(cwd));
            Directory.CreateDirectory(projectDir);
            await File.WriteAllTextAsync(
                Path.Combine(projectDir, otherSessionId.ToString("D") + ".jsonl"),
                JsonlUser(cwd, DelegationReportFormatter.TaskMarker(task.Id), DateTime.UtcNow) + "\n");

            await harness.FailNeverStartedAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
            failed.Status.ShouldBe(AgentTaskStatus.Failed);
            failed.FailureReason.ShouldContain("Boot prompt was never delivered");
            stopper.Killed.ShouldContain(task.AgentSessionId!.Value);
        }
        finally
        {
            TryDeleteTree(projectsRoot);
            TryDeleteTree(cwd);
        }
    }

    [Test]
    public async Task a_card_id_in_an_assistant_record_is_not_jsonl_recovery_evidence()
    {
        var projectsRoot = Directory.CreateTempSubdirectory("card0127-card-id").FullName;
        var cwd = Directory.CreateTempSubdirectory("card0127-card-id-cwd").FullName;
        try
        {
            var (harness, stopper) = CreateHarness(new DelegateBindRefusalRecoverySettings
            {
                ClaudeProjectsRoot = projectsRoot,
            });
            var task = await SeedRecoverableTaskAsync(
                dispatchedMinutesAgo: 11,
                workingDirectory: cwd,
                title: "CARD-0010 incidental reference",
                sessionCwd: cwd);

            var projectDir = Path.Combine(projectsRoot, DelegateBindRefusalRecovery.EncodeClaudeProjectDir(cwd));
            Directory.CreateDirectory(projectDir);
            await File.WriteAllTextAsync(
                Path.Combine(projectDir, Guid.NewGuid().ToString("D") + ".jsonl"),
                JsonlAssistant(cwd, "I read CARD-0010 in another task.", DateTime.UtcNow) + "\n");

            await harness.FailNeverStartedAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
            failed.Status.ShouldBe(AgentTaskStatus.Failed);
            failed.FailureReason.ShouldContain("Boot prompt was never delivered");
            stopper.Killed.ShouldContain(task.AgentSessionId!.Value);
        }
        finally
        {
            TryDeleteTree(projectsRoot);
            TryDeleteTree(cwd);
        }
    }

    [Test]
    public async Task a_c3_refused_jsonl_is_not_evidence_even_when_the_needle_matches()
    {
        var projectsRoot = Directory.CreateTempSubdirectory("card0085-c3").FullName;
        var cwd = Directory.CreateTempSubdirectory("card0085-c3-cwd").FullName;
        try
        {
            var (harness, stopper) = CreateHarness(new DelegateBindRefusalRecoverySettings
            {
                ClaudeProjectsRoot = projectsRoot,
            });
            var task = await SeedRecoverableTaskAsync(
                dispatchedMinutesAgo: 11,
                workingDirectory: cwd,
                title: "CARD-0083 plan",
                sessionCwd: cwd);

            var encoded = DelegateBindRefusalRecovery.EncodeClaudeProjectDir(cwd);
            var projectDir = Path.Combine(projectsRoot, encoded);
            Directory.CreateDirectory(projectDir);
            var jsonl = Path.Combine(projectDir, Guid.NewGuid().ToString("D") + ".jsonl");
            var hourAgo = DateTime.UtcNow.AddHours(-1);
            await File.WriteAllTextAsync(jsonl,
                JsonlUser(cwd, "green", hourAgo) + "\n"
                + JsonlAssistant(cwd, $"CARD-0083 {DelegationReportFormatter.TaskMarker(task.Id)}", hourAgo.AddMinutes(1))
                + "\n");

            await harness.FailNeverStartedAsync(CancellationToken.None);

            await using var verify = CreateContext();
            var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
            failed.Status.ShouldBe(
                AgentTaskStatus.Failed,
                "a C3-refused file is the 2026-08-09 operator-collision — not this session");
            stopper.Killed.ShouldContain(task.AgentSessionId!.Value);
            (await verify.AgentIncidents.AnyAsync(
                i => i.AgentId == task.AgentId && i.Kind == AgentIncidentKind.DelegateBindRefusalRecovered))
                .ShouldBeFalse();
        }
        finally
        {
            TryDeleteTree(projectsRoot);
            TryDeleteTree(cwd);
        }
    }

    // ---- CARD-0158: historical idle-after-done is the watchdog's, not the escalate clock's --

    /// <summary>
    /// Fixture_2c40e79f replayed at dispatch + 10 min + ε: the settlement chain fails-with-a-pointer
    /// fifteen minutes before the old 25-minute escalate clock would have fired. No Escalated event;
    /// model level untouched. The arm itself is already pinned elsewhere — this pins it against the
    /// real historical timeline CARD-0158 asked for.
    /// </summary>
    [Test]
    public async Task The_2026_08_11_shape_fails_with_a_pointer_at_ten_minutes_not_an_escalation()
    {
        var (harness, stopper) = CreateHarness();
        // Compress the historical timeline into the watchdog window: run ~5 min, quiet ~5.5 min
        // after TurnEnd → dispatched ~10.5 min ago. Past DeliveryFailTimeoutMinutes (10), still
        // well under the retired EscalateAfterMinutes (25).
        var (task, sessionId) = await EscalateClockHistoricalFixture.Seed_2c40e79fAsync(
            status: AgentTaskStatus.Dispatched,
            quietAfterTurnEndMinutes: 5.5,
            runMinutesBeforeTurnEnd: 5.0);

        var ageAtFailure = DateTime.UtcNow - task.DispatchedAt!.Value;
        ageAtFailure.ShouldBeGreaterThan(TimeSpan.FromMinutes(10));
        ageAtFailure.ShouldBeLessThan(TimeSpan.FromMinutes(25),
            "must fire inside the window the old escalate clock used to own exclusively");

        await harness.FailNeverStartedAsync(CancellationToken.None);
        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);

        await using var verify = CreateContext();
        var failed = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        failed.Status.ShouldBe(AgentTaskStatus.Failed);
        failed.FailureReason.ShouldContain("could not be attributed");
        failed.FailureReason.ShouldContain(sessionId.ToString());
        failed.ModelLevel.ShouldBe(AgentModelLevel.High);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Escalated))
            .ShouldBe(0);
        // Idle after TurnEnd → kill is allowed (D9 withhold only when working).
        stopper.Killed.ShouldContain(sessionId);
        // FailNeverStartedAsync already Failed the row; nothing further to retire.
    }

    /// <summary>
    /// CARD-0158 D2 watch item, pinned as intentional current behaviour: the watchdog's
    /// uncorrelated arm queries Status == Dispatched only, so a Working-status task that strands
    /// uncorrelated raises the Warning incident and nothing else — no fail, no escalate. Zero
    /// occurrences in the data (both 2026-08-11 cases were Dispatched). Widening the arm is not
    /// obviously safe (a Working task has already correlated once; an uncorrelated turn there is
    /// more likely a stray human turn). See the plan's D2 residual gap.
    /// </summary>
    [Test]
    public async Task An_uncorrelated_report_on_a_Working_task_raises_the_incident_and_nothing_else()
    {
        var (harness, stopper) = CreateHarness();
        var (task, sessionId) = await EscalateClockHistoricalFixture.Seed_2c40e79fAsync(
            status: AgentTaskStatus.Working,
            quietAfterTurnEndMinutes: 5.5,
            runMinutesBeforeTurnEnd: 5.0);

        await harness.FailNeverStartedAsync(CancellationToken.None);
        (await harness.AutoEscalateStalledAsync(CancellationToken.None)).ShouldBe(0);

        await using var verify = CreateContext();
        var row = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        row.Status.ShouldBe(AgentTaskStatus.Working, "watchdog arm 2 is Dispatched-only by design");
        row.ModelLevel.ShouldBe(AgentModelLevel.High);
        row.FailureReason.ShouldBeNull();
        (await verify.AgentIncidents.CountAsync(
            i => i.SessionId == sessionId
                 && i.Kind == AgentIncidentKind.DelegateReportUncorrelated))
            .ShouldBe(1, "the incident OnTurnEndAsync wrote is the surviving surface");
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Escalated))
            .ShouldBe(0);
        stopper.Killed.ShouldBeEmpty();
        await EscalateClockHistoricalFixture.RetireAsync(task.Id);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static (AgentTaskDispatcher Dispatcher, RecordingSessionStopper Stopper) CreateHarness(
        DelegateBindRefusalRecoverySettings? recoverySettings = null,
        ISessionRunnerClient? runnerClient = null,
        List<string>? logs = null,
        DelegationSettings? settings = null)
    {
        var stopper = new RecordingSessionStopper();
        var services = new ServiceCollection();
        services.AddLogging();
        if (logs is not null)
            services.AddSingleton<ILogger<AgentTaskDispatcher>>(new ListLogger<AgentTaskDispatcher>(logs));
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        // Default settings on purpose: DeliveryFailTimeoutMinutes = 10 is the shipped window.
        // CARD-0248: tests that drive multi-step ladders re-hand every tick; the dedicated
        // watermark test passes a settings object with the shipped ReportSweepRehandSeconds.
        settings ??= new DelegationSettings { ReportSweepRehandSeconds = 0 };
        services.AddSingleton(Options.Create(settings));
        services.AddSingleton<DeferredReportSweepMarks>();
        services.AddOptions<AgentRegistrySettings>();
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper>(stopper);
        if (runnerClient is not null)
            services.AddSingleton<ISessionRunnerClient>(runnerClient);
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddSingleton(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-delivery-wt"),
        }));
        services.AddSingleton<IWorktreeManager, Antiphon.Server.Infrastructure.Git.WorktreeManager>();
        services.AddSingleton<IGitService, Antiphon.Server.Infrastructure.Git.GitService>();
        services.AddScoped<DelegationWorktreeService>();
        services.AddScoped<AgentTaskService>();
        // CARD-0046: the dispatcher's deferred-settlement sweep calls into the reply service, so it
        // has to be a real one here (it is optional in the constructor — an unregistered one simply
        // leaves the sweep unarmed, which every other harness relies on).
        services.AddSingleton<AgentTaskReplyService>();
        // CARD-0085: the bind-refusal recovery gate. Predating tests still Fail when neither arm
        // finds evidence (TempPath is not a repo and the title has no CARD-NNNN).
        services.AddSingleton<GitWorkspaceService>();
        services.AddSingleton(Options.Create(recoverySettings ?? new DelegateBindRefusalRecoverySettings()));
        services.AddSingleton<DelegateBindRefusalRecovery>();
        services.AddScoped<AgentTaskDispatcher>();

        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<AgentTaskDispatcher>(), stopper);
    }

    /// <summary>
    /// Direct OnTurnEndAsync for post-nudge turns that have their own final-message text — the
    /// deferred sweep will not re-hand those (arm 1 requires no AssistantText for the boundary).
    /// </summary>
    private static AgentTaskReplyService CreateReplyService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(TestDbFixture.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings()));
        services.AddSingleton(Options.Create(new AgentSessionSettings()));
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        var provider = services.BuildServiceProvider();
        return new AgentTaskReplyService(
            new ReplyScopeFactory(provider),
            Options.Create(new DelegationSettings()),
            provider.GetRequiredService<IEventBus>(),
            TimeProvider.System,
            NullLogger<AgentTaskReplyService>.Instance);
    }

    private sealed class ReplyScopeFactory(ServiceProvider provider) : IServiceScopeFactory, IServiceScope
    {
        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => provider;
        public void Dispose() { }
    }

    private static async Task<AgentTask> SeedDispatchedTaskAsync(int dispatchedMinutesAgo, AgentKind kind = AgentKind.ClaudeCode)
    {
        var sessionId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var dispatched = DateTime.UtcNow.AddMinutes(-dispatchedMinutesAgo);
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = kind,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            Cols = 120,
            Rows = 30,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = dispatched,
        });
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Delivery watchdog test",
            Goal = "Do the thing.",
            Role = AgentTaskRole.Plan,
            AgentKind = kind,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task SeedBriefAsync(
        Guid sessionId, Guid taskId, QueuedMessageStatus status, int deliveryAttempts = 0)
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
            DeliveryAttempts = deliveryAttempts,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A real (non-housekeeping) UserPrompt after dispatch, no TurnEnd — <c>started</c> and
    /// <c>IsWorkingAsync</c> both true. The live miss's Codex compact-as-work shape.
    /// </summary>
    private static async Task SeedWorkingSinceAsync(Guid sessionId, DateTime dispatchedAt)
    {
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "[delegated task] Do the thing.", dispatchedAt.AddMinutes(2));
    }

    /// <summary>
    /// A real UserPrompt after dispatch that has already ended — <c>started</c> true,
    /// <c>IsWorkingAsync</c> false. S4's defer does not apply.
    /// </summary>
    private static async Task SeedIdleTurnSinceAsync(Guid sessionId, DateTime dispatchedAt)
    {
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "[delegated task] Do the thing.", dispatchedAt.AddMinutes(2));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.TurnEnd, null, dispatchedAt.AddMinutes(4));
    }

    private static async Task SeedTranscriptEntryAsync(Guid sessionId)
    {
        // After dispatch of a typical 11- or 45-minute-old Dispatched row, so the new
        // "since this task was dispatched" predicate still sees a real turn prompt.
        var at = DateTime.UtcNow.AddMinutes(-1);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt, "[delegated task] Do the thing.", at);
    }

    /// <summary>
    /// What a reuse-path manual <c>/compact</c> actually leaves behind (CARD-0077 §1.3 / session
    /// e55b3b86 seqs 83-87): raw typed line, CompactBoundary, continuation prompt, wrapper,
    /// stdout. Four USER records, none of them a prompt anybody typed.
    /// </summary>
    private static async Task SeedReuseCompactionHousekeepingAsync(Guid sessionId, DateTime at)
    {
        const string typed =
            "/compact This session is being handed NEW, unrelated work. Keep only context useful for: X";
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, typed, at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.CompactBoundary,
            $"Context compacted {TranscriptKinds.ManualCompactMarker}", at.AddSeconds(44));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            TranscriptKinds.CompactionContinuationPromptPrefix
            + " that ran out of context. The summary below covers…", at.AddSeconds(35));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "<command-name>/compact</command-name>\n            <command-message>compact</command-message>\n"
            + "            <command-args>This session is being handed NEW, unrelated work. Keep only context useful for: X</command-args>",
            at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            "<local-command-stdout>Compacted (ctrl+o to see full summary)</local-command-stdout>",
            at.AddSeconds(44));
    }

    private static async Task SeedEntryAsync(
        Guid sessionId, string kind, string? text, DateTime? timestamp)
    {
        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;
        var at = timestamp ?? DateTime.UtcNow;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = seq + 1,
            Kind = kind,
            Uuid = $"delivery-{Guid.NewGuid():N}",
            Role = kind == TranscriptKinds.UserPrompt ? "user" : "assistant",
            Text = text,
            Timestamp = timestamp,
            CreatedAt = at,
            StopReason = kind == TranscriptKinds.TurnEnd ? "end_turn" : null,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// CARD-0046's split shape, stored <paramref name="storedMinutesAgo"/> ago: the marked brief,
    /// mid-turn narration under its own API call, and the turn-ending response's BARE TurnEnd — a
    /// thinking record carrying only the response id. Its text never arrives.
    ///
    /// CreatedAt is what the grace reads (never the record Timestamp, which is backdated up to 30 s),
    /// so that is what this back-dates.
    /// </summary>
    private static async Task SeedSplitTurnTailAsync(Guid sessionId, Guid taskId, int storedMinutesAgo)
    {
        var at = DateTime.UtcNow.AddMinutes(-storedMinutesAgo);
        await using var db = CreateContext();
        db.TranscriptEntries.AddRange(
            new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 1,
                Kind = TranscriptKinds.UserPrompt,
                Uuid = $"deferred-{Guid.NewGuid():N}",
                Role = "user",
                Text = DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the thing.",
                Timestamp = at,
                CreatedAt = at,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 2,
                Kind = TranscriptKinds.AssistantText,
                Uuid = $"deferred-{Guid.NewGuid():N}",
                Role = "assistant",
                Text = "I'll start by reading the spec.",
                ApiCallId = $"msg_{Guid.NewGuid():N}",
                Timestamp = at,
                CreatedAt = at,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = 3,
                Kind = TranscriptKinds.TurnEnd,
                Uuid = $"deferred-{Guid.NewGuid():N}",
                Role = "assistant",
                StopReason = "end_turn",
                ApiCallId = $"msg_{Guid.NewGuid():N}",
                Timestamp = at,
                CreatedAt = at,
            });
        await db.SaveChangesAsync();
    }

    /// <summary>The mark the reply path leaves when a finished turn fails the marker gate.</summary>
    private static async Task SeedUncorrelatedIncidentAsync(Guid sessionId, int minutesAgo = 5)
    {
        var name = $"wd-{Guid.NewGuid():N}"[..16];
        await using var db = CreateContext();
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = name,
            WorkingDirectory = Path.GetTempPath(),
            Details = "Delivery watchdog test delegate.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Medium,
            IsPoolDelegate = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agent.Id,
            SessionId = sessionId,
            Kind = AgentIncidentKind.DelegateReportUncorrelated,
            Severity = AlertSeverity.Warning,
            Message = "Report could not be correlated to the task.",
            CreatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// CARD-0046 slice 4's shape (session ac09cffd), all of it stored
    /// <paramref name="storedMinutesAgo"/> ago: the marked brief, four background <c>Agent</c>
    /// launches each answered by the async-launch marker, and the announcement turn that really did
    /// end — text and TurnEnd under one ApiCallId, so the slice-1 arm of the sweep passes it over.
    /// No notification ever arrives.
    ///
    /// CreatedAt is the grace clock; Timestamp only has to sit after the task's dispatch.
    /// </summary>
    private static async Task SeedAbandonedSubagentFanOutAsync(
        Guid sessionId, Guid taskId, int storedMinutesAgo)
    {
        var at = DateTime.UtcNow.AddMinutes(-storedMinutesAgo);
        var seq = 0L;
        TranscriptEntry Entry(string kind, string? text) => new()
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = ++seq,
            Kind = kind,
            Uuid = $"subagent-{Guid.NewGuid():N}",
            Text = text,
            Timestamp = at,
            CreatedAt = at,
        };

        await using var db = CreateContext();
        db.TranscriptEntries.Add(Entry(
            TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(taskId) + "\n\nJudge whether commit ce48f50 is correct."));

        for (var i = 0; i < 4; i++)
        {
            var toolUseId = $"toolu_{Guid.NewGuid():N}";
            var call = Entry(TranscriptKinds.ToolCall, null);
            call.ToolName = TranscriptKinds.AgentToolName;
            call.ToolUseId = toolUseId;
            call.ApiCallId = $"msg_{Guid.NewGuid():N}";
            db.TranscriptEntries.Add(call);

            var result = Entry(
                TranscriptKinds.ToolResult,
                TranscriptKinds.AsyncAgentLaunchMarker + " successfully. (internal metadata)");
            result.ToolUseId = toolUseId;
            db.TranscriptEntries.Add(result);
        }

        var announcementCall = $"msg_{Guid.NewGuid():N}";
        var announcement = Entry(
            TranscriptKinds.AssistantText,
            "Four review agents are running in parallel — I'll synthesize when they report.");
        announcement.ApiCallId = announcementCall;
        db.TranscriptEntries.Add(announcement);

        var end = Entry(TranscriptKinds.TurnEnd, null);
        end.StopReason = "end_turn";
        end.ApiCallId = announcementCall;
        db.TranscriptEntries.Add(end);

        await db.SaveChangesAsync();
    }

    private static async Task MarkNudgeDeliveredAsync(Guid sessionId, DateTime sentAt)
    {
        await using var db = CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.AgentSessionId == sessionId);
        task.ReportNudgeMessageId.ShouldNotBeNull();
        var msg = await db.SessionQueuedMessages.SingleAsync(m => m.Id == task.ReportNudgeMessageId);
        msg.SentAt = sentAt;
        msg.Status = QueuedMessageStatus.Sent;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A later turn on the same brief: no new UserPrompt, so ExtractMarkedTurnAsync still
    /// correlates to the original marker. <paramref name="assistantText"/> null is a bare TurnEnd
    /// (FinalMessageMissing on the join of earlier narration).
    /// </summary>
    private static async Task SeedPostNudgeTurnAsync(
        Guid sessionId,
        string? assistantText,
        DateTime createdAt,
        bool closingVerdict = false,
        Guid? taskId = null)
    {
        if (closingVerdict && assistantText is not null && taskId is Guid id)
            assistantText = assistantText.TrimEnd() + "\n" + DelegationReportFormatter.ReportToken(id, "done");

        await using var db = CreateContext();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => t.Sequence);
        var apiCallId = $"msg_{Guid.NewGuid():N}";
        if (assistantText is not null)
        {
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = ++seq,
                Kind = TranscriptKinds.AssistantText,
                Uuid = $"post-nudge-{Guid.NewGuid():N}",
                Role = "assistant",
                Text = assistantText,
                ApiCallId = apiCallId,
                Timestamp = createdAt,
                CreatedAt = createdAt,
            });
        }

        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = ++seq,
            Kind = TranscriptKinds.TurnEnd,
            Uuid = $"post-nudge-{Guid.NewGuid():N}",
            Role = "assistant",
            StopReason = "end_turn",
            ApiCallId = assistantText is null ? $"msg_{Guid.NewGuid():N}" : apiCallId,
            Timestamp = createdAt,
            CreatedAt = createdAt,
        });
        await db.SaveChangesAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    /// <summary>
    /// A Dispatched task plus its pool delegate, so a recovered incident has an AgentId to hang on.
    /// Optional worktree / cwd coordinates for the two CARD-0085 evidence arms.
    /// </summary>
    private static async Task<AgentTask> SeedRecoverableTaskAsync(
        int dispatchedMinutesAgo,
        WorkspaceMode workspace = WorkspaceMode.Shared,
        string? workingDirectory = null,
        string? title = null,
        string? goal = null,
        string? worktreePath = null,
        string? worktreeBranch = null,
        string? mergeTargetRef = null,
        string? repoPath = null,
        string? sessionCwd = null,
        AgentKind kind = AgentKind.ClaudeCode)
    {
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var agentName = $"rec-{agentId:N}"[..16];
        var dispatched = DateTime.UtcNow.AddMinutes(-dispatchedMinutesAgo);
        var cwd = sessionCwd ?? workingDirectory ?? Path.GetTempPath();
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = kind,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = dispatched,
            StartedAt = dispatched,
            LastSeenAt = dispatched,
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = agentName,
            Slug = agentName,
            WorkingDirectory = cwd,
            Details = "CARD-0085 recovery test delegate.",
            Status = AgentStatus.Running,
            ModelLevel = AgentModelLevel.Frontier,
            IsPoolDelegate = true,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = dispatched,
            UpdatedAt = dispatched,
        });
        var task = new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = title ?? "Delivery watchdog recovery test",
            Goal = goal ?? "Do the thing.",
            Role = AgentTaskRole.Plan,
            AgentKind = kind,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = workspace,
            WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
            RepoPath = repoPath,
            WorktreePath = worktreePath,
            WorktreeBranch = worktreeBranch,
            MergeTargetRef = mergeTargetRef,
            AgentId = agentId,
            AgentName = agentName,
            AgentSessionId = sessionId,
            Status = AgentTaskStatus.Dispatched,
            CreatedAt = dispatched,
            DispatchedAt = dispatched,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<Guid> SeedOtherSessionAsync(string cwd, DateTime startedAt)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            DefinitionName = "other",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = startedAt,
            StartedAt = startedAt,
            LastSeenAt = startedAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static AgentTask NewWorktreeDraft(string repoPath, string mergeTarget) => new()
    {
        Id = Guid.NewGuid(),
        RootTaskId = Guid.NewGuid(),
        Title = "CARD-0083 plan",
        Goal = "plan the provider contract",
        Workspace = WorkspaceMode.Worktree,
        WorkingDirectory = repoPath,
        RepoPath = repoPath,
        MergeTargetRef = mergeTarget,
        CreatedAt = DateTime.UtcNow,
    };

    private static (DelegationWorktreeService Service, WorktreeManager Manager) CreateWorktreeService(
        ScratchGitRepo repo)
    {
        var manager = new WorktreeManager(
            Options.Create(new GitSettings
            {
                WorktreeBasePath = repo.WorktreeRoot,
                WorktreeStaleAfterDays = 7,
                WorktreeJanitorIntervalHours = 24,
            }),
            TimeProvider.System,
            NullLogger<WorktreeManager>.Instance);
        var service = new DelegationWorktreeService(
            manager,
            new GitService(NullLogger<GitService>.Instance),
            NullLogger<DelegationWorktreeService>.Instance,
            new GitWorkspaceService(NullLogger<GitWorkspaceService>.Instance));
        return (service, manager);
    }

    private static string JsonlUser(string cwd, string text, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new
        {
            type = "user",
            uuid = Guid.NewGuid().ToString("D"),
            cwd,
            timestamp = timestamp.UtcDateTime.ToString("o"),
            message = new { role = "user", content = text },
        });

    private static string JsonlQueuedCommand(string cwd, string prompt, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new
        {
            type = "attachment",
            uuid = Guid.NewGuid().ToString("D"),
            cwd,
            timestamp = timestamp.UtcDateTime.ToString("o"),
            attachment = new { type = "queued_command", prompt },
        });

    private static string JsonlAssistant(string cwd, string text, DateTimeOffset timestamp) =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            uuid = Guid.NewGuid().ToString("D"),
            cwd,
            timestamp = timestamp.UtcDateTime.ToString("o"),
            message = new { role = "assistant", content = new[] { new { type = "text", text } } },
        });

    private static void TryDeleteTree(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
                sink.Add($"{formatter(state, exception)}");
        }
    }

    private sealed class MismatchRunnerClient : ISessionRunnerClient
    {
        private static readonly RunnerCapabilityMismatch Mismatch = new(
            TranscriptFormats.Codex,
            new RunnerCapabilitiesDto("InboxConhost", "inbox", "test", false,
                [TranscriptFormats.Claude, TranscriptFormats.Grok]),
            "The session runner at :17204 cannot tail a 'codex' transcript. Rebuild and restart it: pwsh -File scripts/restart-session-runner.ps1.");

        public Task<RunnerCapabilityMismatch?> GetTranscriptCapabilityMismatchAsync(AgentKind kind, CancellationToken ct) =>
            Task.FromResult<RunnerCapabilityMismatch?>(kind == AgentKind.Codex ? Mismatch : null);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield break;
        }
    }
}
