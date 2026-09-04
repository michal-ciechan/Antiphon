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
using Microsoft.Extensions.Time.Testing;
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
    [Test]
    public async Task A_needs_decision_card_is_a_critical_row_whose_evidence_is_the_move_reason()
    {
        await using var scenario = new Scenario();
        var (cardId, boardId, movedAt) = await scenario.AddNeedsDecisionCardAsync("Which queue should own this?", minutesAgo: 12);

        var item = (await ItemsForAsync(scenario)).Single(i => i.CardId == cardId);

        item.Kind.ShouldBe(AttentionKind.CardNeedsDecision);
        item.Severity.ShouldBe(AlertSeverity.Critical);
        item.Evidence.ShouldBe("Which queue should own this?");
        item.SinceUtc!.Value.ShouldBeInRange(movedAt.AddTicks(-10), movedAt.AddTicks(10));
        item.BoardId.ShouldBe(boardId);
        item.Actions.ShouldBe([AttentionAction.OpenCard]);
    }

    [Test]
    public async Task A_card_reopened_straight_into_needs_decision_is_listed_with_the_reopen_reason()
    {
        await using var scenario = new Scenario();
        var (cardId, _, reopenedAt) = await scenario.AddNeedsDecisionCardAsync(
            "Should this be released before the migration?", minutesAgo: 8, kind: CardRevisionKind.Reopen);

        var item = (await ItemsForAsync(scenario)).Single(i => i.CardId == cardId);

        item.Kind.ShouldBe(AttentionKind.CardNeedsDecision);
        item.Evidence.ShouldBe("Should this be released before the migration?");
        item.SinceUtc!.Value.ShouldBeInRange(reopenedAt.AddTicks(-10), reopenedAt.AddTicks(10));
    }

    [Test]
    public async Task A_card_reopened_then_moved_within_needs_decision_shows_the_newest_reason_once()
    {
        await using var scenario = new Scenario();
        var (cardId, _, _) = await scenario.AddNeedsDecisionCardAsync(
            "Original question", minutesAgo: 12, kind: CardRevisionKind.Reopen);
        var movedAt = await scenario.AddNeedsDecisionRevisionAsync(cardId, "Clarified question", minutesAgo: 3);

        var rows = (await ItemsForAsync(scenario)).Where(i => i.CardId == cardId).ToList();

        rows.ShouldHaveSingleItem().Evidence.ShouldBe("Clarified question");
        rows[0].SinceUtc!.Value.ShouldBeInRange(movedAt.AddTicks(-10), movedAt.AddTicks(10));
    }

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
    [Arguments(AgentTaskRole.Check)]
    [Arguments(AgentTaskRole.Diagnose)]
    public async Task a_specialist_role_blocked_task_is_not_a_blocked_question(AgentTaskRole role)
    {
        // CARD-0302 S3 / CARD-0352: even before remap, a specialist Blocked row is not waiting
        // on a human. Reply/Cancel would hit the standing seat's session.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Blocked, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 5, role: role);
        await scenario.AddTaskEventAsync(task, AgentTaskEventType.Blocked,
            "LOOKS STUCK — session idle 28m.", minutesAgo: 5);

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.TaskId == task && i.Kind == AttentionKind.BlockedQuestion);
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

    [Test]
    public async Task a_long_report_that_ends_in_a_question_puts_the_question_on_the_row()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var findings = new string('a', 500);
        var question = "Should I accept negative inputs?";
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Blocked, dispatchedMinutesAgo: 20,
            result: $"{findings}\n\n{question}");
        await scenario.AddTaskEventAsync(task, AgentTaskEventType.Blocked, $"Delegate asked: {question}",
            minutesAgo: 5);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Evidence.ShouldContain(question);
        item.Evidence.ShouldNotContain(findings[..80]);
        item.Headline.ShouldBe("Blocked — waiting on a human answer.");
        item.Actions.ShouldContain(AttentionAction.Reply);
        item.Actions.ShouldNotContain(AttentionAction.Continue);
    }

    [Test]
    public async Task a_blocked_question_with_standing_authority_offers_continue_first()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Blocked, dispatchedMinutesAgo: 20,
            result: "Please approve this design and I'll begin the recorded TDD cycles.",
            standingAuthority: "start the remaining Coesite downloader epics one after another");
        await scenario.AddTaskEventAsync(task, AgentTaskEventType.Blocked,
            "Turn ended without `[antiphon-report:…]`; asked once and the session stayed idle. Waiting on a human.",
            minutesAgo: 5);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Actions[0].ShouldBe(AttentionAction.Continue);
        item.Actions.ShouldContain(AttentionAction.Reply);
        item.Evidence.ShouldBe("Please approve this design and I'll begin the recorded TDD cycles.");
    }

    [Test]
    public async Task a_cost_ceiling_block_does_not_offer_reply()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Blocked, dispatchedMinutesAgo: 20, neverDispatched: true,
            failureReason: "Run cost ceiling reached ($5.00).");
        await scenario.AddTaskEventAsync(task, AgentTaskEventType.Blocked, "Run cost ceiling reached ($5.00).",
            minutesAgo: 1);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Actions.ShouldNotContain(AttentionAction.Reply);
        item.Headline.ShouldBe("Blocked — run cost ceiling reached.");
        item.Evidence.ShouldContain("Run cost ceiling reached");
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

    // ---- CallerNoteUndelivered (CARD-0267) ------------------------------------------------------

    [Test]
    public async Task a_succeeded_task_delegation_note_past_grace_is_caller_note_undelivered()
    {
        await using var scenario = new Scenario();
        var caller = await scenario.AddSessionAsync();
        var delegateSession = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            delegateSession, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 12, parentSessionId: caller);
        var (message, createdAt) = await scenario.AddCallerNoteAsync(
            caller, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Pending,
            createdMinutesAgo: 11, sourceTaskId: task);

        var item = (await ItemsForAsync(scenario)).Single(
            i => i.MessageId == message && i.Kind == AttentionKind.CallerNoteUndelivered);

        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.TaskId.ShouldBe(task);
        item.SessionId.ShouldBe(caller);
        item.MessageId.ShouldBe(message);
        item.SinceUtc!.Value.ShouldBeInRange(createdAt.AddTicks(-10), createdAt.AddTicks(10));
        item.Actions.ShouldBe([AttentionAction.OpenDrawer]);
        item.Headline.ShouldContain("Delegation");
        item.Headline.ShouldContain("Pending");
        item.Headline.ShouldContain(caller.ToString("N")[..8]);
    }

    [Test]
    public async Task a_check_note_recovers_the_task_from_conversation_key()
    {
        await using var scenario = new Scenario();
        var caller = await scenario.AddSessionAsync();
        var delegateSession = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            delegateSession, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 12, parentSessionId: caller);
        var (message, _) = await scenario.AddCallerNoteAsync(
            caller, QueuedMessageOrigin.Check, QueuedMessageStatus.Pending,
            createdMinutesAgo: 11, sourceTaskId: null,
            conversationKey: AgentTaskCheckService.ConversationKey(task));

        var item = (await ItemsForAsync(scenario)).Single(
            i => i.MessageId == message && i.Kind == AttentionKind.CallerNoteUndelivered);

        item.TaskId.ShouldBe(task);
        item.SessionId.ShouldBe(caller);
        item.Headline.ShouldContain("Check");
        item.Actions.ShouldBe([AttentionAction.OpenDrawer]);
    }

    [Test]
    public async Task a_caller_note_inside_the_delivery_window_is_not_listed()
    {
        await using var scenario = new Scenario();
        var caller = await scenario.AddSessionAsync();
        var delegateSession = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            delegateSession, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 5, parentSessionId: caller);
        var (message, _) = await scenario.AddCallerNoteAsync(
            caller, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Pending,
            createdMinutesAgo: 9, sourceTaskId: task);

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.MessageId == message && i.Kind == AttentionKind.CallerNoteUndelivered);
    }

    [Test]
    public async Task a_caller_note_at_exact_grace_is_not_listed()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var caller = await scenario.AddSessionAsync();
        var delegateSession = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            delegateSession, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 12, parentSessionId: caller);
        var (message, _) = await scenario.AddCallerNoteAsync(
            caller, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Pending,
            createdAt: now.UtcDateTime.AddMinutes(-10), sourceTaskId: task);

        (await ItemsForAsync(scenario, clock)).ShouldNotContain(
            i => i.MessageId == message && i.Kind == AttentionKind.CallerNoteUndelivered);
    }

    [Test]
    public async Task a_sent_caller_note_is_not_listed()
    {
        await using var scenario = new Scenario();
        var caller = await scenario.AddSessionAsync();
        var delegateSession = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            delegateSession, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 12, parentSessionId: caller);
        var (message, _) = await scenario.AddCallerNoteAsync(
            caller, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Sent,
            createdMinutesAgo: 11, sourceTaskId: task);

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.MessageId == message && i.Kind == AttentionKind.CallerNoteUndelivered);
    }

    [Test]
    public async Task a_canceled_caller_note_is_not_listed()
    {
        await using var scenario = new Scenario();
        var caller = await scenario.AddSessionAsync();
        var delegateSession = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            delegateSession, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 12, parentSessionId: caller);
        var (message, _) = await scenario.AddCallerNoteAsync(
            caller, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Canceled,
            createdMinutesAgo: 11, sourceTaskId: task);

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.MessageId == message && i.Kind == AttentionKind.CallerNoteUndelivered);
    }

    [Test]
    public async Task an_unparseable_check_key_is_not_listed()
    {
        await using var scenario = new Scenario();
        var caller = await scenario.AddSessionAsync();
        var delegateSession = await scenario.AddSessionAsync();
        await scenario.AddTaskAsync(
            delegateSession, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 12, parentSessionId: caller);
        var (message, _) = await scenario.AddCallerNoteAsync(
            caller, QueuedMessageOrigin.Check, QueuedMessageStatus.Pending,
            createdMinutesAgo: 11, sourceTaskId: null,
            conversationKey: "check:not-a-guid");

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.MessageId == message && i.Kind == AttentionKind.CallerNoteUndelivered);
    }

    [Test]
    public async Task a_delegation_note_on_the_delegate_session_is_not_a_caller_note()
    {
        await using var scenario = new Scenario();
        var caller = await scenario.AddSessionAsync();
        var delegateSession = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            delegateSession, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 12, parentSessionId: caller);
        var (message, _) = await scenario.AddCallerNoteAsync(
            delegateSession, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Pending,
            createdMinutesAgo: 11, sourceTaskId: task);

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.MessageId == message && i.Kind == AttentionKind.CallerNoteUndelivered);
    }

    [Test]
    public async Task a_caller_note_row_disappears_once_sent_or_canceled()
    {
        await using var scenario = new Scenario();
        var caller = await scenario.AddSessionAsync();
        var delegateSession = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            delegateSession, AgentTaskStatus.Succeeded, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 12, parentSessionId: caller);
        var (sentId, _) = await scenario.AddCallerNoteAsync(
            caller, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Pending,
            createdMinutesAgo: 11, sourceTaskId: task, sequence: 1);
        var (canceledId, _) = await scenario.AddCallerNoteAsync(
            caller, QueuedMessageOrigin.Delegation, QueuedMessageStatus.Pending,
            createdMinutesAgo: 12, sourceTaskId: task, sequence: 2);

        var before = await ItemsForAsync(scenario);
        before.ShouldContain(i => i.MessageId == sentId && i.Kind == AttentionKind.CallerNoteUndelivered);
        before.ShouldContain(i => i.MessageId == canceledId && i.Kind == AttentionKind.CallerNoteUndelivered);

        await scenario.SetQueuedMessageStatusAsync(sentId, QueuedMessageStatus.Sent);
        await scenario.SetQueuedMessageStatusAsync(canceledId, QueuedMessageStatus.Canceled);

        var after = await ItemsForAsync(scenario);
        after.ShouldNotContain(i => i.MessageId == sentId && i.Kind == AttentionKind.CallerNoteUndelivered);
        after.ShouldNotContain(i => i.MessageId == canceledId && i.Kind == AttentionKind.CallerNoteUndelivered);
    }

    // ---- CardlessDetailsNoPrompt (CARD-0287) ----------------------------------------------------

    [Test]
    public async Task a_cardless_details_start_past_grace_with_no_prompt_is_a_warning()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (session, agent, _) = await scenario.AddCardlessDetailsCaseAsync(startedAt);

        var rows = (await ItemsForAsync(scenario, clock))
            .Where(i => i.Kind == AttentionKind.CardlessDetailsNoPrompt)
            .ToList();

        var item = rows.ShouldHaveSingleItem();
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.AgentId.ShouldBe(agent);
        item.SessionId.ShouldBe(session);
        item.TaskId.ShouldBeNull();
        item.MessageId.ShouldBeNull();
        item.Title.ShouldBe($"attn-{agent:N}"[..16]);
        item.SinceUtc.ShouldBe(startedAt);
        item.Actions.ShouldBe([AttentionAction.OpenAgent]);
        item.Headline.ShouldContain("still idle");
        item.Headline.ShouldContain("Details was not sent as a prompt");
        item.Evidence.ShouldContain("Current Details");
        item.Evidence.ShouldContain("No transcript");
        item.Evidence.ShouldContain("no UI start or message queue row");
        item.Evidence.ShouldContain("standing-job metadata");
        item.Evidence.ShouldContain("StartAgentRequest.Prompt");
    }

    [Test]
    public async Task a_cardless_details_start_is_absent_at_the_two_minute_grace_and_present_just_after()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var atGrace = now.UtcDateTime.AddMinutes(-2);
        var pastGrace = now.UtcDateTime.AddMinutes(-2).AddSeconds(-1);
        var (atGraceSession, _, _) = await scenario.AddCardlessDetailsCaseAsync(atGrace);
        var (pastGraceSession, _, _) = await scenario.AddCardlessDetailsCaseAsync(pastGrace);

        var rows = (await ItemsForAsync(scenario, clock))
            .Where(i => i.Kind == AttentionKind.CardlessDetailsNoPrompt)
            .ToList();

        rows.ShouldNotContain(i => i.SessionId == atGraceSession);
        rows.ShouldContain(i => i.SessionId == pastGraceSession);
    }

    [Test]
    public async Task blank_or_whitespace_details_does_not_raise_cardless_details_no_prompt()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (blankSession, _, _) = await scenario.AddCardlessDetailsCaseAsync(startedAt, details: "");
        var (whitespaceSession, _, _) = await scenario.AddCardlessDetailsCaseAsync(
            startedAt.AddSeconds(-5), details: "  \t  ");

        var rows = await ItemsForAsync(scenario, clock);
        rows.ShouldNotContain(i =>
            i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == blankSession);
        rows.ShouldNotContain(i =>
            i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == whitespaceSession);
    }

    [Test]
    public async Task a_non_current_owner_does_not_raise_cardless_details_no_prompt()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (session, _, _) = await scenario.AddCardlessDetailsCaseAsync(startedAt, currentOwner: false);

        (await ItemsForAsync(scenario, clock)).ShouldNotContain(
            i => i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == session);
    }

    [Test]
    public async Task a_non_running_session_does_not_raise_cardless_details_no_prompt()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (session, _, _) = await scenario.AddCardlessDetailsCaseAsync(
            startedAt, status: SessionStatus.Stopped);

        (await ItemsForAsync(scenario, clock)).ShouldNotContain(
            i => i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == session);
    }

    [Test]
    public async Task a_card_owned_session_does_not_raise_cardless_details_no_prompt()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (cardId, _, _) = await scenario.AddInProgressCardAsync(daysAgo: 1);
        var (session, _, _) = await scenario.AddCardlessDetailsCaseAsync(startedAt, cardId: cardId);

        (await ItemsForAsync(scenario, clock)).ShouldNotContain(
            i => i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == session);
    }

    [Test]
    public async Task a_resumed_session_does_not_raise_cardless_details_no_prompt()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (session, _, _) = await scenario.AddCardlessDetailsCaseAsync(
            startedAt, createdAt: startedAt.AddHours(-1));

        (await ItemsForAsync(scenario, clock)).ShouldNotContain(
            i => i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == session);
    }

    [Test]
    public async Task a_herdr_attached_session_does_not_raise_cardless_details_no_prompt()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (session, _, _) = await scenario.AddCardlessDetailsCaseAsync(
            startedAt, composedBundleStamp: null);

        (await ItemsForAsync(scenario, clock)).ShouldNotContain(
            i => i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == session);
    }

    [Test]
    public async Task any_transcript_entry_suppresses_cardless_details_no_prompt()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (session, _, _) = await scenario.AddCardlessDetailsCaseAsync(startedAt);
        await scenario.AddTranscriptAsync(session, (TranscriptKinds.Thinking, "booting", null));

        (await ItemsForAsync(scenario, clock)).ShouldNotContain(
            i => i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == session);
    }

    [Test]
    public async Task a_pending_ui_queue_row_suppresses_cardless_details_no_prompt()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (session, _, _) = await scenario.AddCardlessDetailsCaseAsync(startedAt);
        await scenario.AddCallerNoteAsync(
            session, QueuedMessageOrigin.Ui, QueuedMessageStatus.Pending, createdAt: startedAt);

        (await ItemsForAsync(scenario, clock)).ShouldNotContain(
            i => i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == session);
    }

    [Test]
    public async Task a_sent_ui_queue_row_suppresses_cardless_details_no_prompt()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (session, _, _) = await scenario.AddCardlessDetailsCaseAsync(startedAt);
        await scenario.AddCallerNoteAsync(
            session, QueuedMessageOrigin.Ui, QueuedMessageStatus.Sent, createdAt: startedAt);

        (await ItemsForAsync(scenario, clock)).ShouldNotContain(
            i => i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == session);
    }

    [Test]
    public async Task cardless_details_no_prompt_disappears_after_a_transcript_arrives()
    {
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        var startedAt = now.UtcDateTime.AddMinutes(-3);
        await using var scenario = new Scenario();
        var (session, _, _) = await scenario.AddCardlessDetailsCaseAsync(startedAt);

        (await ItemsForAsync(scenario, clock)).ShouldContain(
            i => i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == session);

        await scenario.AddTranscriptAsync(session, (TranscriptKinds.UserPrompt, "do the standing job", null));

        (await ItemsForAsync(scenario, clock)).ShouldNotContain(
            i => i.Kind == AttentionKind.CardlessDetailsNoPrompt && i.SessionId == session);
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

    // ---- CARD-0288 S3: ReportUnsettled ----------------------------------------------------------

    [Test]
    public async Task a_dispatched_task_with_a_marked_report_in_the_transcript_is_report_unsettled()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync(persistentSession: session);
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 8, agentId: agent);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(task) + "\nDo it", null),
            (TranscriptKinds.AssistantText,
                "Shipped.\n" + DelegationReportFormatter.ReportToken(task, "done"), null),
            (TranscriptKinds.TurnEnd, null, null));

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.ReportUnsettled);
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.SessionId.ShouldBe(session);
        item.AgentId.ShouldBe(agent);
        item.Headline.ShouldBe("Finished report is in the transcript; the task is still Dispatched.");
        item.Evidence.ShouldContain("Marked done");
        item.Evidence.ShouldContain("TurnEnd #");
        item.Evidence.ShouldContain("dispatcher re-hands");
        item.Actions.ShouldBe(
            [AttentionAction.OpenDrawer, AttentionAction.Retry, AttentionAction.Cancel]);
        item.Actions.ShouldNotContain(AttentionAction.KillSession);
        item.SinceUtc.ShouldNotBeNull();
    }

    [Test]
    public async Task report_unsettled_clears_once_the_task_is_no_longer_open()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 8);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(task) + "\nDo it", null),
            (TranscriptKinds.AssistantText,
                "Shipped.\n" + DelegationReportFormatter.ReportToken(task, "done"), null),
            (TranscriptKinds.TurnEnd, null, null));

        (await ItemsForAsync(scenario)).ShouldContain(i => i.TaskId == task && i.Kind == AttentionKind.ReportUnsettled);

        await scenario.SetTaskStatusAsync(task, AgentTaskStatus.Succeeded);

        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.TaskId == task && i.Kind == AttentionKind.ReportUnsettled);
    }

    [Test]
    public async Task a_dead_session_with_a_marked_report_stays_dead_session_not_report_unsettled()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Failed, endedMinutesAgo: 4);
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 8);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(task) + "\nDo it", null),
            (TranscriptKinds.AssistantText,
                "Shipped.\n" + DelegationReportFormatter.ReportToken(task, "done"), null),
            (TranscriptKinds.TurnEnd, null, null));

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);
        item.Kind.ShouldBe(AttentionKind.DeadSession);
    }

    [Test]
    public async Task never_started_is_not_report_unsettled()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 6);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);
        item.Kind.ShouldBe(AttentionKind.NeverStarted);
        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.TaskId == task && i.Kind == AttentionKind.ReportUnsettled);
    }

    [Test]
    public async Task an_uncorrelated_incident_without_this_tasks_token_is_not_report_unsettled()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync();
        var task = await scenario.AddTaskAsync(session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 8);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.AssistantText, "a report with no closing line", null),
            (TranscriptKinds.TurnEnd, null, null));
        await scenario.AddIncidentAsync(
            agent, session, AgentIncidentKind.DelegateReportUncorrelated, AlertSeverity.Error,
            "A delegate reported with no task marker.", minutesAgo: 2);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);
        item.Kind.ShouldBe(AttentionKind.UncorrelatedReport);
    }

    // ---- CARD-0022 S4: ModelAvailabilityHold ----------------------------------------------------

    [Test]
    public async Task An_active_hold_is_an_error_row_and_clears_when_ClearedAt_is_set()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var holdId = await scenario.AddHoldAsync(
            AgentKind.ClaudeCode, "fable", sessionId: session,
            until: DateTime.UtcNow.AddMinutes(30),
            reason: "session-limit resets 18:10 Europe/London",
            rawText: UsageLimitWallParser.SessionLimitFixtureText);

        var item = (await ItemsForAsync(scenario)).Single(i => i.SessionId == session
            && i.Kind == AttentionKind.ModelAvailabilityHold);
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.Headline.ShouldContain("fable exhausted");
        item.Headline.ShouldContain("dispatch paused for fable");
        item.ModelAlias.ShouldBe("fable");
        item.ModelKind.ShouldBe("ClaudeCode");
        item.Actions.ShouldBe([AttentionAction.ClearHold]);
        item.Evidence.ShouldContain(UsageLimitWallParser.SessionLimitFixtureText);

        await using (var db = CreateContext())
        {
            var row = await db.ModelAvailabilityHolds.SingleAsync(h => h.Id == holdId);
            row.ClearedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.Kind == AttentionKind.ModelAvailabilityHold && i.SessionId == session);
    }

    [Test]
    public async Task A_manual_hold_row_carries_kind_alias_and_clears_after_DELETE()
    {
        await using var scenario = new Scenario();
        var holdId = await scenario.AddHoldAsync(
            AgentKind.Grok, "grok-4.6",
            until: DateTime.UtcNow.AddDays(2),
            reason: "manual hold",
            source: ModelAvailabilitySource.Manual);

        var item = (await ItemsForAsync(scenario)).Single(i =>
            i.Kind == AttentionKind.ModelAvailabilityHold && i.ModelAlias == "grok-4.6");
        item.ModelKind.ShouldBe("Grok");
        item.ModelAlias.ShouldBe("grok-4.6");
        item.Actions.ShouldBe([AttentionAction.ClearHold]);
        item.Headline.ShouldContain("(manual)");
        item.Headline.ShouldContain("held until");

        await using (var db = CreateContext())
        {
            var row = await db.ModelAvailabilityHolds.SingleAsync(h => h.Id == holdId);
            row.ClearedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.Kind == AttentionKind.ModelAvailabilityHold && i.ModelAlias == "grok-4.6");
    }

    [Test]
    public async Task A_fallback_hold_row_does_not_call_the_retry_a_provider_reset()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var until = DateTime.UtcNow.AddHours(3);
        until = new DateTime(until.Year, until.Month, until.Day, until.Hour, until.Minute, until.Second, DateTimeKind.Utc);
        var holdId = await scenario.AddHoldAsync(
            AgentKind.ClaudeCode, "sonnet", sessionId: session,
            until: until,
            reason: "Sonnet per-model cap (no reset stated)",
            rawText: UsageLimitWallParser.FableModelCapIncidentText);

        var item = (await ItemsForAsync(scenario)).Single(i => i.SessionId == session
            && i.Kind == AttentionKind.ModelAvailabilityHold);
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.Headline.ShouldContain("provider gave no reset");
        item.Headline.ShouldContain("fallback retry");
        item.Headline.ShouldContain($"{until:yyyy-MM-ddTHH:mm:ssZ}");
        item.Headline.ShouldNotContain("resets");
        item.Headline.ShouldContain("dispatch paused for sonnet");
        item.Actions.ShouldBe([AttentionAction.ClearHold]);
        item.Evidence.ShouldContain(UsageLimitWallParser.FableModelCapIncidentText);
        item.Evidence.ShouldContain("disabled until");

        await using (var db = CreateContext())
        {
            var row = await db.ModelAvailabilityHolds.SingleAsync(h => h.Id == holdId);
            row.ClearedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.Kind == AttentionKind.ModelAvailabilityHold && i.SessionId == session);
    }

    [Test]
    [NotInParallel]
    public async Task A_legacy_null_auto_detected_hold_keeps_the_no_reset_stated_headline()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        await scenario.AddHoldAsync(
            AgentKind.ClaudeCode, "haiku", sessionId: session,
            until: null,
            reason: "Haiku per-model cap (no reset stated)",
            rawText: UsageLimitWallParser.FableModelCapIncidentText);

        var item = (await ItemsForAsync(scenario)).Single(i => i.SessionId == session
            && i.Kind == AttentionKind.ModelAvailabilityHold);
        item.Headline.ShouldContain("haiku exhausted (no reset stated)");
        item.Headline.ShouldNotContain("fallback retry");
        item.Actions.ShouldBe([AttentionAction.ClearHold]);
    }

    // ---- CARD-0294 S3: UnmarkedWaiting ----------------------------------------------------------

    [Test]
    public async Task a_nudged_idle_task_with_no_token_is_unmarked_waiting()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync(persistentSession: session);
        var nudgedAt = DateTime.UtcNow.AddMinutes(-2);
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 8, agentId: agent,
            reportNudgedAt: nudgedAt);
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(task) + "\nDo it", null),
            (TranscriptKinds.AssistantText,
                "Please approve this design and I'll begin the recorded TDD cycles.", null),
            (TranscriptKinds.TurnEnd, null, null));

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.UnmarkedWaiting);
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.Headline.ShouldBe("Ended a turn with no closing line; asked once, still idle.");
        item.Evidence.ShouldContain("Nudged");
        item.Evidence.ShouldContain("S1 will Block at 5m");
        item.Evidence.ShouldContain("Herdr");
        item.Actions.ShouldBe(
            [AttentionAction.OpenDrawer, AttentionAction.Retry, AttentionAction.Cancel]);
        item.Actions.ShouldNotContain(AttentionAction.Reply);
        item.SinceUtc.ShouldNotBeNull();
        item.SinceUtc!.Value.ShouldBeInRange(nudgedAt.AddSeconds(-2), nudgedAt.AddSeconds(2));
        AttentionSummaryDto.From(new AttentionDto(DateTime.UtcNow, true, [item])).Open.ShouldBe(1);
    }

    [Test]
    public async Task unmarked_waiting_clears_to_blocked_question_once_the_task_blocks()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 8,
            reportNudgedAt: DateTime.UtcNow.AddMinutes(-6));
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(task) + "\nDo it", null),
            (TranscriptKinds.AssistantText, "Please approve this design.", null),
            (TranscriptKinds.TurnEnd, null, null));

        (await ItemsForAsync(scenario)).ShouldContain(
            i => i.TaskId == task && i.Kind == AttentionKind.UnmarkedWaiting);

        await scenario.SetTaskStatusAsync(task, AgentTaskStatus.Blocked);
        await scenario.AddTaskEventAsync(task, AgentTaskEventType.Blocked,
            "Turn ended without `[antiphon-report:…]`; asked once and the session stayed idle.",
            minutesAgo: 0);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);
        item.Kind.ShouldBe(AttentionKind.BlockedQuestion);
        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.TaskId == task && i.Kind == AttentionKind.UnmarkedWaiting);
    }

    [Test]
    public async Task a_mid_turn_nudged_task_is_not_unmarked_waiting()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 8,
            reportNudgedAt: DateTime.UtcNow.AddMinutes(-6));
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(task) + "\nDo it", null),
            (TranscriptKinds.AssistantText, "I'll start.", null),
            (TranscriptKinds.TurnEnd, null, null),
            (TranscriptKinds.ToolCall, null, "Read"));

        (await ItemsForAsync(scenario)).ShouldNotContain(
            i => i.TaskId == task && i.Kind == AttentionKind.UnmarkedWaiting);
    }

    [Test]
    public async Task a_dead_session_wins_over_unmarked_waiting()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Failed, endedMinutesAgo: 4);
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Dispatched, dispatchedMinutesAgo: 8,
            reportNudgedAt: DateTime.UtcNow.AddMinutes(-6));
        await scenario.AddTranscriptAsync(session,
            (TranscriptKinds.UserPrompt, DelegationReportFormatter.TaskMarker(task) + "\nDo it", null),
            (TranscriptKinds.AssistantText, "Please approve this design.", null),
            (TranscriptKinds.TurnEnd, null, null));

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);
        item.Kind.ShouldBe(AttentionKind.DeadSession);
        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.TaskId == task && i.Kind == AttentionKind.UnmarkedWaiting);
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

    // ---- 7b. ProgressStalled (CARD-0153 S3) -----------------------------------------------------

    [Test]
    public async Task a_working_stalled_task_is_listed_as_progress_stalled()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 50, expectedMinutes: 10);
        await scenario.AddStallLoopAsync(session);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.ProgressStalled);
        item.Headline.ShouldContain("no novel progress");
        item.Actions.ShouldBe(
            [AttentionAction.Reply, AttentionAction.Cancel, AttentionAction.OpenDrawer]);
    }

    [Test]
    public async Task ProgressStalled_beats_Overdue_and_loses_to_PastExpectedIdle_when_idle()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 210, expectedMinutes: 10);
        await scenario.AddStallLoopAsync(session);

        var working = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);
        working.Kind.ShouldBe(AttentionKind.ProgressStalled, "the stall names the loop; Overdue only names a clock");

        await scenario.AddTranscriptAsync(session, (TranscriptKinds.TurnEnd, null, null));
        var idle = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);
        idle.Kind.ShouldBe(AttentionKind.PastExpectedIdle);
    }

    [Test]
    public async Task a_pending_refinement_is_named_on_the_stall_headline()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 50);
        await scenario.AddStallLoopAsync(session);
        await scenario.AddQueuedMessageAsync(
            session, "continue", attempts: 1, origin: QueuedMessageOrigin.Delegation);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);
        item.Kind.ShouldBe(AttentionKind.ProgressStalled);
        item.Headline.ShouldContain("1 refinement waiting");
    }

    // ---- CARD-0312: LivenessProbeFailed ----------------------------------------------------------

    [Test]
    public async Task An_open_boot_reply_incident_on_a_live_session_is_liveness_probe_failed()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync(persistentSession: session);
        await scenario.AddTranscriptAsync(session, (TranscriptKinds.UserPrompt, "the brief", null));
        await scenario.AddIncidentAsync(
            agent, session, AgentIncidentKind.LivenessProbeFailed, AlertSeverity.Warning,
            "Boot prompt confirmed at sequence 1; no assistant, thinking, tool or turn-end row in 8m00s.",
            minutesAgo: 5, failureReason: BootReplyWatchdogService.EpisodeKey(1));

        var item = (await ItemsForAsync(scenario)).Single(i => i.SessionId == session
            && i.Kind == AttentionKind.LivenessProbeFailed);

        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.AgentId.ShouldBe(agent);
        item.Actions.ShouldBe([AttentionAction.OpenAgent, AttentionAction.OpenDrawer]);
        item.Evidence.ShouldContain("delivery is not the problem", customMessage:
            "the row exists to stop the reading that cost CARD-0353 a plan pass");
    }

    [Test]
    public async Task A_boot_prompt_that_was_answered_after_the_incident_is_no_longer_listed()
    {
        // Read-time re-verification: the row exists because the condition holds NOW. Whoever
        // produced the answer — the retry, a late first token, or a human — closes it.
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync(persistentSession: session);
        await scenario.AddTranscriptAsync(
            session,
            (TranscriptKinds.UserPrompt, "the brief", null),
            (TranscriptKinds.AssistantText, "here at last", null));
        await scenario.AddIncidentAsync(
            agent, session, AgentIncidentKind.LivenessProbeFailed, AlertSeverity.Warning,
            "stale", minutesAgo: 5, failureReason: BootReplyWatchdogService.EpisodeKey(1));

        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.SessionId == session && i.Kind == AttentionKind.LivenessProbeFailed);
    }

    [Test]
    public async Task A_boot_reply_incident_on_a_dead_session_is_not_listed()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Failed, endedMinutesAgo: 10);
        var agent = await scenario.AddAgentAsync(persistentSession: session);
        await scenario.AddTranscriptAsync(session, (TranscriptKinds.UserPrompt, "the brief", null));
        await scenario.AddIncidentAsync(
            agent, session, AgentIncidentKind.LivenessProbeFailed, AlertSeverity.Warning,
            "stale", minutesAgo: 5, failureReason: BootReplyWatchdogService.EpisodeKey(1));

        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.SessionId == session && i.Kind == AttentionKind.LivenessProbeFailed);
    }

    // ---- CARD-0292: QueuedInputStuck ------------------------------------------------------------

    [Test]
    public async Task An_open_kind_43_incident_on_a_live_session_is_queued_input_stuck()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync(persistentSession: session);
        await scenario.AddTranscriptAsync(session, (TranscriptKinds.QueueEnqueue, "Hi", null));
        await scenario.AddIncidentAsync(
            agent, session, AgentIncidentKind.QueuedInputNeverConverted, AlertSeverity.Warning,
            "Input was accepted into the TUI's own composer queue and never became a prompt.",
            minutesAgo: 5, failureReason: QueuedInputWatchdogService.EpisodeKey(1));

        var item = (await ItemsForAsync(scenario)).Single(i => i.SessionId == session
            && i.Kind == AttentionKind.QueuedInputStuck);

        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.AgentId.ShouldBe(agent);
        item.Actions.ShouldBe([AttentionAction.OpenAgent, AttentionAction.OpenDrawer]);
    }

    [Test]
    public async Task A_closed_enqueue_episode_is_not_queued_input_stuck()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync(persistentSession: session);
        await scenario.AddTranscriptAsync(
            session,
            (TranscriptKinds.QueueEnqueue, "Hi", null),
            (TranscriptKinds.UserPrompt, "Hi", null));
        await scenario.AddIncidentAsync(
            agent, session, AgentIncidentKind.QueuedInputNeverConverted, AlertSeverity.Warning,
            "stale", minutesAgo: 5, failureReason: QueuedInputWatchdogService.EpisodeKey(1));

        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.SessionId == session && i.Kind == AttentionKind.QueuedInputStuck);
    }

    [Test]
    public async Task A_kind_43_incident_on_a_dead_session_is_not_queued_input_stuck()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync(SessionStatus.Failed, endedMinutesAgo: 10);
        var agent = await scenario.AddAgentAsync(persistentSession: session);
        await scenario.AddTranscriptAsync(session, (TranscriptKinds.QueueEnqueue, "Hi", null));
        await scenario.AddIncidentAsync(
            agent, session, AgentIncidentKind.QueuedInputNeverConverted, AlertSeverity.Warning,
            "stale", minutesAgo: 5, failureReason: QueuedInputWatchdogService.EpisodeKey(1));

        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.SessionId == session && i.Kind == AttentionKind.QueuedInputStuck);
    }

    // ---- CARD-0239: AgentOutlivedTask -----------------------------------------------------------

    [Test]
    public async Task a_running_idle_agent_with_a_nine_hour_old_transcript_and_no_task_is_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var (agent, session, newest) = await scenario.SeedLiveIdleAsync(now.UtcDateTime, TimeSpan.FromHours(9));

        var item = (await ItemsForAsync(scenario, clock))
            .Single(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);

        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.SessionId.ShouldBe(session);
        item.TaskId.ShouldBeNull();
        item.Actions.ShouldBe([AttentionAction.OpenAgent]);
        item.SinceUtc.ShouldBe(newest);
        item.Headline.ShouldContain("Standing agent idle");
        item.Headline.ShouldContain("with no task");
        item.Evidence.ShouldContain("No open task, not AlwaysOn, not channel-bound");
        item.Evidence.ShouldContain("Nothing will stop it automatically");
    }

    [Test]
    public async Task a_running_agent_mid_turn_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var (agent, _, _) = await scenario.SeedLiveIdleAsync(
            now.UtcDateTime, TimeSpan.FromHours(9), midTurn: true);

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_running_agent_with_a_one_hour_old_transcript_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var (agent, _, _) = await scenario.SeedLiveIdleAsync(now.UtcDateTime, TimeSpan.FromHours(1));

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_running_idle_agent_with_a_queued_task_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var (agent, session, _) = await scenario.SeedLiveIdleAsync(now.UtcDateTime, TimeSpan.FromHours(9));
        await scenario.AddTaskAsync(
            session, AgentTaskStatus.Queued, dispatchedMinutesAgo: 5, neverDispatched: true, agentId: agent);

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_running_agent_with_zero_transcript_rows_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var (agent, _, _) = await scenario.SeedLiveIdleAsync(
            now.UtcDateTime, TimeSpan.FromHours(9), withTranscript: false);

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_stopped_worktree_agent_untouched_for_three_days_is_a_leftover()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var cwd = Scenario.WorktreeCwd("task-x");
        var agent = await scenario.AddAgentAsync(
            isPoolDelegate: false,
            status: AgentStatus.Stopped,
            workingDirectory: cwd,
            createdAt: now.UtcDateTime.AddDays(-3),
            updatedAt: now.UtcDateTime.AddDays(-3));

        var item = (await ItemsForAsync(scenario, clock))
            .Single(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);

        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.SessionId.ShouldBeNull();
        item.Actions.ShouldBe([AttentionAction.OpenAgent]);
        item.SinceUtc.ShouldBe(now.UtcDateTime.AddDays(-3));
        item.Headline.ShouldContain("Left-over one-off agent");
        item.Headline.ShouldContain("Stopped");
        item.Evidence.ShouldContain("worktree path");
        item.Evidence.ShouldContain(cwd);
    }

    [Test]
    public async Task a_sole_agent_on_a_same_named_empty_board_idle_three_days_is_a_leftover()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var agentName = $"oneoff-{Guid.NewGuid():N}"[..16];
        var cwd = Scenario.UniqueCwd(agentName);
        var (_, boardId, _) = await scenario.AddBoardAsync(agentName, localRepositoryPath: Scenario.UniqueCwd());
        var agent = await scenario.AddAgentAsync(
            isPoolDelegate: false,
            status: AgentStatus.Idle,
            workingDirectory: cwd,
            boardId: boardId,
            name: agentName,
            createdAt: now.UtcDateTime.AddDays(-3),
            updatedAt: now.UtcDateTime.AddDays(-3));

        var item = (await ItemsForAsync(scenario, clock))
            .Single(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);

        item.SessionId.ShouldBeNull();
        item.Evidence.ShouldContain("sole agent on empty board");
        item.Evidence.ShouldContain(agentName);
        item.Headline.ShouldContain("Idle");
    }

    [Test]
    public async Task a_sole_agent_on_a_named_board_that_holds_a_card_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var agentName = $"oneoff-{Guid.NewGuid():N}"[..16];
        var cwd = Scenario.UniqueCwd(agentName);
        var (_, boardId, columnId) = await scenario.AddBoardAsync(agentName, localRepositoryPath: Scenario.UniqueCwd());
        await scenario.AddCardOnBoardAsync(boardId, columnId);
        var agent = await scenario.AddAgentAsync(
            isPoolDelegate: false,
            status: AgentStatus.Idle,
            workingDirectory: cwd,
            boardId: boardId,
            name: agentName,
            createdAt: now.UtcDateTime.AddDays(-3),
            updatedAt: now.UtcDateTime.AddDays(-3));

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_second_agent_sharing_the_empty_board_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var agentName = $"oneoff-{Guid.NewGuid():N}"[..16];
        var cwd = Scenario.UniqueCwd(agentName);
        var (_, boardId, _) = await scenario.AddBoardAsync(agentName, localRepositoryPath: Scenario.UniqueCwd());
        var agent = await scenario.AddAgentAsync(
            isPoolDelegate: false,
            status: AgentStatus.Idle,
            workingDirectory: cwd,
            boardId: boardId,
            name: agentName,
            createdAt: now.UtcDateTime.AddDays(-3),
            updatedAt: now.UtcDateTime.AddDays(-3));
        await scenario.AddAgentAsync(
            isPoolDelegate: false,
            status: AgentStatus.Idle,
            workingDirectory: Scenario.UniqueCwd(),
            boardId: boardId,
            name: $"{agentName}-b",
            createdAt: now.UtcDateTime.AddDays(-3),
            updatedAt: now.UtcDateTime.AddDays(-3));

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_sole_agent_on_an_unrelated_named_empty_board_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var agentName = $"alice-{Guid.NewGuid():N}"[..16];
        var cwd = Scenario.UniqueCwd($"cwd-{Guid.NewGuid():N}"[..12]);
        var (_, boardId, _) = await scenario.AddBoardAsync(
            $"unrelated-{Guid.NewGuid():N}"[..18], localRepositoryPath: Scenario.UniqueCwd());
        var agent = await scenario.AddAgentAsync(
            isPoolDelegate: false,
            status: AgentStatus.Idle,
            workingDirectory: cwd,
            boardId: boardId,
            name: agentName,
            createdAt: now.UtcDateTime.AddDays(-3),
            updatedAt: now.UtcDateTime.AddDays(-3));

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_leftover_shape_touched_one_day_ago_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var agent = await scenario.AddAgentAsync(
            isPoolDelegate: false,
            status: AgentStatus.Stopped,
            workingDirectory: Scenario.WorktreeCwd(),
            createdAt: now.UtcDateTime.AddDays(-3),
            updatedAt: now.UtcDateTime.AddDays(-1));

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task an_always_on_live_idle_agent_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var (agent, _, _) = await scenario.SeedLiveIdleAsync(
            now.UtcDateTime, TimeSpan.FromHours(9), alwaysOn: true);

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_channel_bound_live_idle_agent_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var (agent, _, _) = await scenario.SeedLiveIdleAsync(now.UtcDateTime, TimeSpan.FromHours(9));
        await scenario.AddChannelAsync(agent);

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_pool_delegate_live_idle_agent_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var (agent, _, _) = await scenario.SeedLiveIdleAsync(
            now.UtcDateTime, TimeSpan.FromHours(9), isPoolDelegate: true);

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_project_root_worker_on_a_live_card_board_is_not_outlived()
    {
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(now);
        await using var scenario = new Scenario();
        var cwd = Scenario.UniqueCwd();
        var (_, boardId, columnId) = await scenario.AddBoardAsync("Antiphon", localRepositoryPath: cwd);
        await scenario.AddCardOnBoardAsync(boardId, columnId);
        var (agent, _, _) = await scenario.SeedLiveIdleAsync(
            now.UtcDateTime, TimeSpan.FromHours(9), workingDirectory: cwd);

        (await ItemsForAsync(scenario, clock))
            .ShouldNotContain(i => i.AgentId == agent && i.Kind == AttentionKind.AgentOutlivedTask);
    }

    [Test]
    public async Task a_task_dispatched_under_stall_minutes_does_not_run_the_workspace_probe()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var dir = Path.Combine(Path.GetTempPath(), $"attn-probe-{Guid.NewGuid():N}");
        await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working, dispatchedMinutesAgo: 2, expectedMinutes: 240,
            workingDirectory: dir);
        var probe = new RecordingWorkspaceProbe();

        await ItemsForAsync(scenario, probe);

        probe.Directories.ShouldNotContain(d => d == dir);
    }

    [Test]
    public async Task a_task_past_stall_minutes_runs_the_workspace_probe()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var dir = Path.Combine(Path.GetTempPath(), $"attn-probe-{Guid.NewGuid():N}");
        await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working,
            dispatchedMinutesAgo: new DelegationSettings().StallDetection.StallMinutes + 1,
            expectedMinutes: 240,
            workingDirectory: dir);
        var probe = new RecordingWorkspaceProbe();

        await ItemsForAsync(scenario, probe);

        probe.Directories.ShouldContain(dir);
    }

    [Test]
    public async Task the_summary_never_runs_the_workspace_probe()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        await scenario.AddTaskAsync(
            session, AgentTaskStatus.Working,
            dispatchedMinutesAgo: new DelegationSettings().StallDetection.StallMinutes + 1,
            expectedMinutes: 240);
        var probe = new RecordingWorkspaceProbe();

        await BuildService(new FakeRunnerClient(), workspaceProgress: probe)
            .GetSummaryAsync(CancellationToken.None);

        probe.Calls.ShouldBe(0, "the badge path sets includeProgressProbe: false");
    }

    [Test]
    public async Task summary_counts_match_a_full_sweep_for_the_same_rows()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            session, AgentTaskStatus.Blocked, dispatchedMinutesAgo: 20);
        await scenario.AddTaskEventAsync(task, AgentTaskEventType.Blocked, "Which branch?", minutesAgo: 5);
        var (cardId, _, _) = await scenario.AddNeedsDecisionCardAsync("Ship tonight?", minutesAgo: 8);

        var service = BuildService(new FakeRunnerClient());
        var full = await service.GetAsync(CancellationToken.None, includeProgressProbe: true);
        var withoutProbe = await service.GetAsync(CancellationToken.None, includeProgressProbe: false);

        // Fleet-global totals race with other suites on the shared database; the property is
        // that skipping the workspace probe does not change THIS fixture's open/decision rows.
        var fullOwned = full.Items.Where(scenario.Owns).ToList();
        var summaryOwned = withoutProbe.Items.Where(scenario.Owns).ToList();
        fullOwned.ShouldContain(i => i.TaskId == task);
        fullOwned.ShouldContain(i => i.CardId == cardId);
        summaryOwned.Count(i => i.Kind != AttentionKind.RecentFailure)
            .ShouldBe(fullOwned.Count(i => i.Kind != AttentionKind.RecentFailure));
        summaryOwned.Count(i => i.Kind == AttentionKind.CardNeedsDecision)
            .ShouldBe(fullOwned.Count(i => i.Kind == AttentionKind.CardNeedsDecision));

        var live = await service.GetSummaryAsync(CancellationToken.None);
        live.Decisions.ShouldBeGreaterThanOrEqualTo(1);
        live.Open.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Test]
    public void summary_open_drops_recent_failures_and_counts_every_other_row()
    {
        var now = DateTime.UtcNow;
        var items = new AttentionItemDto[]
        {
            Row(AttentionKind.CardNeedsDecision),
            Row(AttentionKind.CardNeedsDecision),
            Row(AttentionKind.ParkedMessage),
            Row(AttentionKind.RecentFailure),
        };
        var summary = AttentionSummaryDto.From(new AttentionDto(now, true, items));

        summary.Open.ShouldBe(3);
        summary.Decisions.ShouldBe(2);
        summary.GeneratedAt.ShouldBe(now);
    }

    private static AttentionItemDto Row(AttentionKind kind) =>
        new(kind, AlertSeverity.Warning, null, null, null, null,
            "row", "headline", "evidence", DateTime.UtcNow, null, []);

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

    [Test]
    public async Task a_never_dispatched_failure_still_armed_is_FailureUnacknowledged()
    {
        await using var scenario = new Scenario();
        var parent = await scenario.AddSessionAsync();
        // Older than RecentFailure's 24h window on purpose: an unacknowledged pre-dispatch
        // failure must not age out of the counted band just because time passed.
        var task = await scenario.AddTaskAsync(
            parent, AgentTaskStatus.Failed, dispatchedMinutesAgo: 60 * 41,
            completedMinutesAgo: 60 * 40,
            failureReason: "Dispatch failed before a session existed: not a git repo",
            neverDispatched: true,
            nextCheckAt: DateTime.UtcNow.AddMinutes(5),
            checkCount: 2);

        var items = await ItemsForAsync(scenario);
        var item = items.Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.FailureUnacknowledged);
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.Headline.ShouldContain($"session {parent.ToString("N")[..8]}");
        item.Headline.ShouldContain("reminder 2/10");
        item.Evidence.ShouldContain("not a git repo");
        item.Actions.ShouldBe([AttentionAction.Retry, AttentionAction.OpenDrawer]);
        items.ShouldNotContain(i => i.TaskId == task && i.Kind == AttentionKind.RecentFailure);
        AttentionSummaryDto.From(new AttentionDto(DateTime.UtcNow, true, [item])).Open.ShouldBe(1);
    }

    [Test]
    public async Task an_orchestrator_investigation_incident_is_a_warning_process_row()
    {
        await using var scenario = new Scenario();
        var session = await scenario.AddSessionAsync();
        var agent = await scenario.AddAgentAsync(session);
        await scenario.AddIncidentAsync(
            agent, session, AgentIncidentKind.OrchestratorInvestigation, AlertSeverity.Warning,
            "8 reads over 77s across 4 files, no dispatch; nudged=no", minutesAgo: 3);

        var item = (await ItemsForAsync(scenario)).Single(i => i.SessionId == session
            && i.Kind == AttentionKind.OrchestratorInvestigation);

        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.Headline.ShouldContain("8 reads over 77s");
        item.Headline.ShouldContain("nudged=no");
        item.AgentId.ShouldBe(agent);
        item.Actions.ShouldContain(AttentionAction.OpenAgent);
    }

    [Test]
    public async Task once_disarmed_it_is_a_RecentFailure()
    {
        await using var scenario = new Scenario();
        var parent = await scenario.AddSessionAsync();
        var task = await scenario.AddTaskAsync(
            parent, AgentTaskStatus.Failed, dispatchedMinutesAgo: 20,
            completedMinutesAgo: 15,
            failureReason: "Dispatch failed before a session existed: not a git repo",
            neverDispatched: true,
            nextCheckAt: null);

        var item = (await ItemsForAsync(scenario)).Single(i => i.TaskId == task);

        item.Kind.ShouldBe(AttentionKind.RecentFailure);
        item.Severity.ShouldBe(AlertSeverity.Warning);
    }

    [Test]
    public async Task a_misfire_is_a_warning_row_until_the_next_good_fire()
    {
        await using var scenario = new Scenario();
        var agent = await scenario.AddAgentAsync(isPoolDelegate: false, alwaysOn: true);
        var scheduleId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.Schedules.Add(new Schedule
            {
                Id = scheduleId,
                Name = "Morning triage",
                Kind = ScheduleKind.Prompt,
                Repeat = ScheduleRepeat.Daily,
                TimeZoneId = "Europe/London",
                NextFireAt = DateTime.UtcNow.AddHours(1),
                Enabled = true,
                AtLocal = "09:00",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ConcurrencyToken = Guid.NewGuid(),
                AgentId = agent,
                PromptText = "triage",
                LastOutcome = ScheduleFireOutcome.SkippedNoSession,
                LastOutcomeDetail = "agent is down",
                LastFiredAt = DateTime.UtcNow.AddMinutes(-3),
            });
            await db.SaveChangesAsync();
        }

        var item = (await ItemsForAsync(scenario))
            .Single(i => i.Kind == AttentionKind.ScheduleMisfired && i.AgentId == agent);
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.Headline.ShouldContain("Morning triage");
        item.Actions.ShouldContain(AttentionAction.OpenAgent);

        await using (var db = CreateContext())
        {
            var row = await db.Schedules.SingleAsync(s => s.Id == scheduleId);
            row.LastOutcome = ScheduleFireOutcome.Delivered;
            row.LastOutcomeDetail = null;
            await db.SaveChangesAsync();
        }

        (await ItemsForAsync(scenario))
            .ShouldNotContain(i => i.Kind == AttentionKind.ScheduleMisfired && i.AgentId == agent);
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

    // ---- CARD-0040: a card in In Progress with nobody on it ---------------------------------------

    [Test]
    public async Task A_card_in_progress_past_the_threshold_with_nobody_on_it_is_a_warning_row()
    {
        await using var scenario = new Scenario();
        var (cardId, boardId, enteredAt) = await scenario.AddInProgressCardAsync(daysAgo: 9);
        await scenario.AddCardBoundTaskAsync(
            cardId, AgentTaskStatus.Failed, completedDaysAgo: 8, failureReason: "the delegate never reported");

        var item = (await ItemsForAsync(scenario, staleAfterDays: 7)).Single(i => i.Kind == AttentionKind.CardStalled);

        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.CardId.ShouldBe(cardId);
        item.BoardId.ShouldBe(boardId);
        item.Headline.ShouldContain("In Progress for 9 days");
        item.Evidence.ShouldContain("Failed");
        item.Evidence.ShouldContain("the delegate never reported");
        // SinceUtc is the moment it ENTERED In Progress, which is the revision, not the card row.
        item.SinceUtc!.Value.ShouldBeInRange(enteredAt.AddSeconds(-2), enteredAt.AddSeconds(2));
        item.Actions.ShouldBe([AttentionAction.OpenCard]);
    }

    [Test]
    public async Task A_stale_card_that_never_had_a_task_says_so()
    {
        await using var scenario = new Scenario();
        var (cardId, _, _) = await scenario.AddInProgressCardAsync(daysAgo: 20);
        // Its only bound row is a Check task, which is about a task and never about a card.
        await scenario.AddCardBoundTaskAsync(
            cardId, AgentTaskStatus.Dispatched, completedDaysAgo: 1, role: AgentTaskRole.Check);

        var item = (await ItemsForAsync(scenario, staleAfterDays: 7)).Single(i => i.Kind == AttentionKind.CardStalled);

        item.CardId.ShouldBe(cardId);
        item.Evidence.ShouldBe("no task has ever been bound to this card");
    }

    [Test]
    public async Task A_card_with_an_open_bound_task_is_not_stale()
    {
        await using var scenario = new Scenario();
        var (cardId, _, _) = await scenario.AddInProgressCardAsync(daysAgo: 30);
        await scenario.AddCardBoundTaskAsync(cardId, AgentTaskStatus.Working);

        (await ItemsForAsync(scenario, staleAfterDays: 7))
            .ShouldNotContain(i => i.Kind == AttentionKind.CardStalled && i.CardId == cardId);
    }

    [Test]
    public async Task A_card_with_a_live_session_is_not_stale()
    {
        await using var scenario = new Scenario();
        var (cardId, _, _) = await scenario.AddInProgressCardAsync(daysAgo: 30);
        var sessionId = await scenario.AddSessionAsync(SessionStatus.Running);
        await scenario.BindSessionToCardAsync(sessionId, cardId);

        (await ItemsForAsync(scenario, staleAfterDays: 7))
            .ShouldNotContain(i => i.Kind == AttentionKind.CardStalled && i.CardId == cardId);
    }

    [Test]
    public async Task A_card_owned_by_a_card_session_is_not_stale()
    {
        await using var scenario = new Scenario();
        var sessionId = await scenario.AddSessionAsync(SessionStatus.Stopped);
        // The RunAttempt / card-spawn path owns this one; two writers on one card is out of scope.
        var (cardId, _, _) = await scenario.AddInProgressCardAsync(daysAgo: 30, ownerSessionId: sessionId);

        (await ItemsForAsync(scenario, staleAfterDays: 7))
            .ShouldNotContain(i => i.Kind == AttentionKind.CardStalled && i.CardId == cardId);
    }

    [Test]
    public async Task A_card_under_the_threshold_is_not_stale()
    {
        await using var scenario = new Scenario();
        var (cardId, _, _) = await scenario.AddInProgressCardAsync(daysAgo: 3);
        await scenario.AddCardBoundTaskAsync(cardId, AgentTaskStatus.Succeeded, completedDaysAgo: 2);

        (await ItemsForAsync(scenario, staleAfterDays: 7))
            .ShouldNotContain(i => i.Kind == AttentionKind.CardStalled && i.CardId == cardId);
    }

    [Test]
    public async Task A_stale_card_with_no_history_dates_itself_from_started_at()
    {
        await using var scenario = new Scenario();
        var (cardId, _, enteredAt) = await scenario.AddInProgressCardAsync(daysAgo: 11, withHistory: false);
        await scenario.AddCardBoundTaskAsync(cardId, AgentTaskStatus.Succeeded, completedDaysAgo: 10);

        var item = (await ItemsForAsync(scenario, staleAfterDays: 7)).Single(i => i.Kind == AttentionKind.CardStalled);

        item.CardId.ShouldBe(cardId);
        item.SinceUtc!.Value.ShouldBeInRange(enteredAt.AddSeconds(-2), enteredAt.AddSeconds(2));
    }

    [Test]
    public async Task A_card_in_review_is_never_stale()
    {
        await using var scenario = new Scenario();
        var (cardId, _, _) = await scenario.AddNeedsDecisionCardAsync("not this one", minutesAgo: 5);

        // The condition is In Progress alone. A card waiting on a reviewer is not abandoned work.
        (await ItemsForAsync(scenario, staleAfterDays: 7))
            .ShouldNotContain(i => i.Kind == AttentionKind.CardStalled && i.CardId == cardId);
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

    /// <summary>The same id-scoped read, with a recording workspace probe.</summary>
    private static async Task<List<AttentionItemDto>> ItemsForAsync(
        Scenario scenario, IWorkspaceProgressProbe workspaceProgress)
    {
        var result = await BuildService(new FakeRunnerClient(), workspaceProgress: workspaceProgress)
            .GetAsync(CancellationToken.None);
        return result.Items.Where(scenario.Owns).ToList();
    }

    /// <summary>The same id-scoped read, with the CARD-0040 stale threshold pushed in.</summary>
    private static async Task<List<AttentionItemDto>> ItemsForAsync(Scenario scenario, int staleAfterDays)
    {
        var result = await BuildService(new FakeRunnerClient(), staleAfterDays).GetAsync(CancellationToken.None);
        return result.Items.Where(scenario.Owns).ToList();
    }

    /// <summary>The same id-scoped read, with an injected clock (CARD-0267 exact-grace).</summary>
    private static async Task<List<AttentionItemDto>> ItemsForAsync(Scenario scenario, TimeProvider time)
    {
        var result = await BuildService(new FakeRunnerClient(), timeProvider: time)
            .GetAsync(CancellationToken.None);
        return result.Items.Where(scenario.Owns).ToList();
    }

    /// <summary>What the runner says when it is running a session — the only status that can differ.</summary>
    private static SessionRunnerSessionDto Running(Guid sessionId, int? pid = 1234, int? hostPid = null) =>
        new(sessionId, pid, DateTime.UtcNow.AddMinutes(-45), "Running", null, AgentExitReason.Unknown,
            LastSequence: 900, HostPid: hostPid);

    private static AttentionService BuildService(
        ISessionRunnerClient runner,
        int staleAfterDays = 7,
        IWorkspaceProgressProbe? workspaceProgress = null,
        TimeProvider? timeProvider = null) =>
        new(CreateContext(), runner, Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()), timeProvider ?? TimeProvider.System,
            NullLogger<AttentionService>.Instance,
            workspaceProgress: workspaceProgress,
            cardTransitions: Options.Create(new CardWorkTransitionSettings { StaleAfterDays = staleAfterDays }));

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
        private readonly List<Guid> _cards = [];
        private readonly List<Guid> _boards = [];
        private readonly List<Guid> _projects = [];
        private readonly List<Guid> _holds = [];
        private readonly HashSet<string> _holdKeys = [];

        public bool Owns(AttentionItemDto item) =>
            (item.TaskId is { } t && _tasks.Contains(t))
            || (item.MessageId is { } m && _messages.Contains(m))
            || (item.TaskId is null && item.MessageId is null && item.AgentId is { } a && _agents.Contains(a))
            || (item.CardId is { } c && _cards.Contains(c))
            // Session-scoped rows (SessionDisagreement) may carry no task, message or agent at all —
            // an unclaimed runner session is by definition owned by nothing the database knows.
            || (item.TaskId is null && item.MessageId is null && item.SessionId is { } s && _sessions.Contains(s))
            || (item.Kind == AttentionKind.ModelAvailabilityHold
                && item.ModelKind is { } mk
                && item.ModelAlias is { } ma
                && _holdKeys.Contains($"{mk}/{ma}"));

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
            SessionStatus status = SessionStatus.Running,
            int? endedMinutesAgo = null,
            DateTime? createdAt = null,
            DateTime? startedAt = null,
            string? composedBundleStamp = null,
            Guid? cardId = null)
        {
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            db.AgentSessions.Add(new AgentSession
            {
                Id = id,
                CardId = cardId,
                DefinitionName = "attention-test",
                AgentKind = AgentKind.ClaudeCode,
                Status = status,
                Cwd = Path.GetTempPath(),
                Cols = 120,
                Rows = 30,
                CreatedAt = createdAt ?? DateTime.UtcNow.AddHours(-1),
                StartedAt = startedAt ?? DateTime.UtcNow.AddHours(-1),
                LastSeenAt = DateTime.UtcNow,
                EndedAt = endedMinutesAgo is { } ago ? DateTime.UtcNow.AddMinutes(-ago) : null,
                FailureReason = status == SessionStatus.Failed ? "the process vanished" : null,
                ComposedBundleStamp = composedBundleStamp,
            });
            await db.SaveChangesAsync();
            _sessions.Add(id);
            return id;
        }

        public async Task<Guid> AddAgentAsync(
            Guid? persistentSession = null,
            string details = "Attention projection test agent.",
            bool isPoolDelegate = true,
            bool alwaysOn = false,
            AgentStatus status = AgentStatus.Running,
            string? workingDirectory = null,
            Guid? boardId = null,
            string? name = null,
            DateTime? createdAt = null,
            DateTime? updatedAt = null)
        {
            var id = Guid.NewGuid();
            var agentName = name ?? $"attn-{id:N}"[..16];
            var at = createdAt ?? DateTime.UtcNow;
            await using var db = CreateContext();
            db.Agents.Add(new Agent
            {
                Id = id,
                Name = agentName,
                Slug = $"attn-{id:N}"[..16],
                WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
                Details = details,
                Status = status,
                ModelLevel = AgentModelLevel.Medium,
                AlwaysOn = alwaysOn,
                IsPoolDelegate = isPoolDelegate,
                PersistentSessionId = persistentSession?.ToString("D"),
                BoardId = boardId,
                CreatedAt = at,
                UpdatedAt = updatedAt ?? at,
            });
            await db.SaveChangesAsync();
            _agents.Add(id);
            return id;
        }

        /// <summary>
        /// CARD-0287: a fresh interactive launch (CreatedAt == StartedAt, non-null composed stamp)
        /// that a current owner can optionally claim. Empty-string stamp is a real composition.
        /// </summary>
        public async Task<(Guid SessionId, Guid AgentId, DateTime StartedAt)> AddCardlessDetailsCaseAsync(
            DateTime startedAt,
            string details = "Standing job: keep the gym stats current.",
            SessionStatus status = SessionStatus.Running,
            DateTime? createdAt = null,
            string? composedBundleStamp = "",
            Guid? cardId = null,
            bool currentOwner = true)
        {
            var sessionId = await AddSessionAsync(
                status,
                createdAt: createdAt ?? startedAt,
                startedAt: startedAt,
                composedBundleStamp: composedBundleStamp,
                cardId: cardId);
            var agentId = currentOwner
                ? await AddAgentAsync(persistentSession: sessionId, details: details)
                : await AddAgentAsync(details: details);
            return (sessionId, agentId, startedAt);
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
            string? result = null,
            decimal costUsd = 0m,
            string? workingDirectory = null,
            bool neverDispatched = false,
            Guid? parentSessionId = null,
            Guid? agentId = null,
            DateTime? reportNudgedAt = null,
            AgentTaskRole role = AgentTaskRole.Code,
            string? standingAuthority = null)
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
                Role = role,
                ModelLevel = AgentModelLevel.High,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = workingDirectory ?? Path.GetTempPath(),
                AgentId = agentId,
                AgentSessionId = neverDispatched ? null : sessionId,
                Status = status,
                ReplyTo = AgentTaskReplyTo.Session,
                ParentSessionId = parentSessionId ?? (neverDispatched ? sessionId : Guid.NewGuid()),
                ExpectedDurationMinutes = expectedMinutes,
                CheckCount = checkCount,
                NextCheckAt = nextCheckAt,
                FailureReason = failureReason,
                Result = result,
                CostUsd = costUsd,
                CreatedAt = dispatched,
                DispatchedAt = neverDispatched ? null : dispatched,
                CompletedAt = completedMinutesAgo is { } done ? DateTime.UtcNow.AddMinutes(-done) : null,
                ReportNudgedAt = reportNudgedAt,
                StandingAuthority = standingAuthority,
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

        public async Task AddStallLoopAsync(Guid sessionId)
        {
            await using var db = CreateContext();
            var seq = ((await db.TranscriptEntries
                .Where(e => e.AgentSessionId == sessionId)
                .MaxAsync(e => (long?)e.Sequence)) ?? 0);
            for (var i = 0; i < 14; i++)
            {
                var ago = 42 - i;
                var at = DateTime.UtcNow.AddMinutes(-ago);
                var kind = i % 3 == 0 ? TranscriptKinds.ToolCall
                    : i % 3 == 1 ? TranscriptKinds.ToolResult
                    : TranscriptKinds.Thinking;
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Sequence = ++seq,
                    Kind = kind,
                    Uuid = $"attn-stall-{Guid.NewGuid():N}",
                    ToolName = kind == TranscriptKinds.ToolCall ? "Read" : null,
                    ToolInput = kind == TranscriptKinds.ToolCall ? "{\"path\":\"src/loop.cs\"}" : null,
                    Text = kind == TranscriptKinds.ToolResult ? "file contents of loop.cs"
                        : kind == TranscriptKinds.Thinking ? $"thinking {i}" : null,
                    Timestamp = at,
                    CreatedAt = at,
                });
            }
            await db.SaveChangesAsync();
        }

        public async Task AddTranscriptAsync(
            Guid sessionId, params (string Kind, string? Text, string? Tool)[] entries)
        {
            var at = DateTime.UtcNow.AddMinutes(-5);
            await using var db = CreateContext();
            var seq = (await db.TranscriptEntries
                .Where(e => e.AgentSessionId == sessionId)
                .MaxAsync(e => (long?)e.Sequence)) ?? 0;
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

        public async Task AddTranscriptAtAsync(
            Guid sessionId, DateTime at, params (string Kind, string? Text, string? Tool)[] entries)
        {
            await using var db = CreateContext();
            var seq = (await db.TranscriptEntries
                .Where(e => e.AgentSessionId == sessionId)
                .MaxAsync(e => (long?)e.Sequence)) ?? 0;
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
                    Timestamp = at,
                    CreatedAt = at,
                });
            }
            await db.SaveChangesAsync();
        }

        public static string UniqueCwd(string? leaf = null) =>
            Path.Combine(Path.GetTempPath(), "outlived", leaf ?? Guid.NewGuid().ToString("N"));

        public static string WorktreeCwd(string? leaf = null) =>
            Path.Combine(Path.GetTempPath(), ".worktrees", leaf ?? Guid.NewGuid().ToString("N"));

        public async Task<(Guid AgentId, Guid SessionId, DateTime LastTranscriptAt)> SeedLiveIdleAsync(
            DateTime now,
            TimeSpan idleFor,
            bool alwaysOn = false,
            bool isPoolDelegate = false,
            bool withTranscript = true,
            bool midTurn = false,
            string? workingDirectory = null,
            string? name = null)
        {
            var sessionId = await AddSessionAsync();
            var cwd = workingDirectory ?? UniqueCwd();
            var agentId = await AddAgentAsync(
                persistentSession: sessionId,
                isPoolDelegate: isPoolDelegate,
                alwaysOn: alwaysOn,
                status: AgentStatus.Running,
                workingDirectory: cwd,
                name: name,
                createdAt: now.AddDays(-4),
                updatedAt: now - idleFor);
            var lastAt = now - idleFor;
            if (withTranscript)
            {
                if (midTurn)
                {
                    await AddTranscriptAtAsync(sessionId, lastAt, (TranscriptKinds.UserPrompt, "still going", null));
                }
                else
                {
                    await AddTranscriptAtAsync(
                        sessionId,
                        lastAt,
                        (TranscriptKinds.UserPrompt, "do the thing", null),
                        (TranscriptKinds.TurnEnd, null, null));
                }
            }

            return (agentId, sessionId, lastAt);
        }

        public async Task<(Guid ProjectId, Guid BoardId, Guid BacklogColumnId)> AddBoardAsync(
            string name, string? localRepositoryPath = null)
        {
            var now = DateTime.UtcNow;
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var backlogId = Guid.NewGuid();
            await using var db = CreateContext();
            db.Projects.Add(new Project
            {
                Id = projectId,
                Name = $"outlived-project-{projectId:N}"[..30],
                GitRepositoryUrl = "https://example.test/outlived.git",
                LocalRepositoryPath = localRepositoryPath ?? UniqueCwd($"proj-{projectId:N}"),
                BaseBranch = "main",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Boards.Add(new Board
            {
                Id = boardId,
                ProjectId = projectId,
                Name = name,
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.BoardColumns.Add(new BoardColumn
            {
                Id = backlogId,
                BoardId = boardId,
                StateKey = "backlog",
                Name = "Backlog",
                ColumnOrder = 0,
                CardStatus = CardStatus.Backlog,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
            _projects.Add(projectId);
            _boards.Add(boardId);
            return (projectId, boardId, backlogId);
        }

        public async Task<Guid> AddCardOnBoardAsync(Guid boardId, Guid columnId)
        {
            var now = DateTime.UtcNow;
            var cardId = Guid.NewGuid();
            await using var db = CreateContext();
            db.Cards.Add(new Card
            {
                Id = cardId,
                BoardId = boardId,
                BoardColumnId = columnId,
                Identifier = $"OUTL-{cardId:N}"[..16],
                Title = "A live card on this board",
                Status = CardStatus.Backlog,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
            _cards.Add(cardId);
            return cardId;
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

        public async Task<Guid> AddQueuedMessageAsync(
            Guid sessionId, string body, int attempts, QueuedMessageOrigin origin = QueuedMessageOrigin.Channel)
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
                Origin = origin,
                DeliveryAttempts = attempts,
                LastDeliveryStartedAt = DateTime.UtcNow.AddMinutes(-4),
                CreatedAt = DateTime.UtcNow.AddMinutes(-15),
            });
            await db.SaveChangesAsync();
            _messages.Add(id);
            return id;
        }

        public async Task<(Guid Id, DateTime CreatedAt)> AddCallerNoteAsync(
            Guid sessionId,
            QueuedMessageOrigin origin,
            QueuedMessageStatus status,
            int createdMinutesAgo = 11,
            DateTime? createdAt = null,
            Guid? sourceTaskId = null,
            string? conversationKey = null,
            long sequence = 1)
        {
            var id = Guid.NewGuid();
            var at = createdAt ?? DateTime.UtcNow.AddMinutes(-createdMinutesAgo);
            await using var db = CreateContext();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = id,
                AgentSessionId = sessionId,
                Body = "the caller has not heard this yet",
                Status = status,
                Sequence = sequence,
                Origin = origin,
                SourceTaskId = sourceTaskId,
                ConversationKey = conversationKey,
                CreatedAt = at,
                SentAt = status == QueuedMessageStatus.Sent ? at.AddMinutes(1) : null,
                CanceledAt = status == QueuedMessageStatus.Canceled ? at.AddMinutes(1) : null,
            });
            await db.SaveChangesAsync();
            _messages.Add(id);
            return (id, at);
        }

        public async Task SetQueuedMessageStatusAsync(Guid messageId, QueuedMessageStatus status)
        {
            await using var db = CreateContext();
            var row = await db.SessionQueuedMessages.SingleAsync(m => m.Id == messageId);
            row.Status = status;
            row.SentAt = status == QueuedMessageStatus.Sent ? DateTime.UtcNow : row.SentAt;
            row.CanceledAt = status == QueuedMessageStatus.Canceled ? DateTime.UtcNow : row.CanceledAt;
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// CARD-0040: a card sitting In Progress, with an "entered In Progress" Move revision at a
        /// chosen age. Returns the card and the moment it entered, which is what the row's
        /// <c>SinceUtc</c> must report.
        /// </summary>
        public async Task<(Guid CardId, Guid BoardId, DateTime EnteredAt)> AddInProgressCardAsync(
            int daysAgo, bool withHistory = true, Guid? ownerSessionId = null)
        {
            var now = DateTime.UtcNow;
            var enteredAt = now.AddDays(-daysAgo);
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var backlogId = Guid.NewGuid();
            var progressId = Guid.NewGuid();
            var cardId = Guid.NewGuid();
            await using var db = CreateContext();
            db.Projects.Add(new Project
            {
                Id = projectId,
                Name = $"stale-project-{projectId:N}"[..30],
                GitRepositoryUrl = "https://example.test/stale.git",
                LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"stale-{projectId:N}"),
                BaseBranch = "main",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Boards.Add(new Board
            {
                Id = boardId,
                ProjectId = projectId,
                Name = $"stale-board-{boardId:N}"[..30],
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.BoardColumns.AddRange(
                new BoardColumn { Id = backlogId, BoardId = boardId, StateKey = "backlog", Name = "Backlog", ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now },
                new BoardColumn { Id = progressId, BoardId = boardId, StateKey = "in-progress", Name = "In Progress", ColumnOrder = 1, CardStatus = CardStatus.InProgress, IsActive = true, CreatedAt = now, UpdatedAt = now });
            db.Cards.Add(new Card
            {
                Id = cardId,
                BoardId = boardId,
                BoardColumnId = progressId,
                Identifier = $"STAL-{cardId:N}"[..16],
                Title = "Nobody is on this",
                Status = CardStatus.InProgress,
                OwnerSessionId = ownerSessionId,
                // A card with no history dates its entry from StartedAt - the first active landing.
                StartedAt = enteredAt,
                CreatedAt = enteredAt.AddHours(-1),
                UpdatedAt = enteredAt,
                RevisionCount = withHistory ? 1 : 0,
            });
            if (withHistory)
            {
                db.CardRevisions.Add(new CardRevision
                {
                    Id = Guid.NewGuid(),
                    CardId = cardId,
                    RevisionNumber = 1,
                    Kind = CardRevisionKind.Move,
                    FromColumnId = backlogId,
                    ToColumnId = progressId,
                    FromStatus = CardStatus.Backlog,
                    ToStatus = CardStatus.InProgress,
                    Reason = "starting work",
                    CreatedAt = enteredAt,
                });
            }

            await db.SaveChangesAsync();
            _projects.Add(projectId);
            _boards.Add(boardId);
            _cards.Add(cardId);
            return (cardId, boardId, enteredAt);
        }

        /// <summary>A task bound to a card (CARD-0040), at whatever status the test needs.</summary>
        public async Task<Guid> AddCardBoundTaskAsync(
            Guid cardId,
            AgentTaskStatus status,
            int completedDaysAgo = 1,
            string? failureReason = null,
            AgentTaskRole role = AgentTaskRole.Code)
        {
            var id = Guid.NewGuid();
            var at = DateTime.UtcNow.AddDays(-completedDaysAgo);
            await using var db = CreateContext();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Depth = 0,
                Title = "Bound to a stale card",
                Goal = "Stale card test.",
                Kind = AgentTaskKind.Worker,
                Role = role,
                CardId = cardId,
                ModelLevel = AgentModelLevel.High,
                WorkingDirectory = Path.GetTempPath(),
                Status = status,
                DispatchedAt = at,
                CompletedAt = AgentTaskService.IsSettled(status) ? at : null,
                FailureReason = failureReason,
                CreatedAt = at,
            });
            await db.SaveChangesAsync();
            _tasks.Add(id);
            return id;
        }

        /// <summary>Points an existing session row at a card, so the card looks alive.</summary>
        public async Task BindSessionToCardAsync(Guid sessionId, Guid cardId)
        {
            await using var db = CreateContext();
            await db.AgentSessions.Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.CardId, cardId));
        }

        public async Task<(Guid CardId, Guid BoardId, DateTime MovedAt)> AddNeedsDecisionCardAsync(
            string reason, int minutesAgo, CardRevisionKind kind = CardRevisionKind.Move)
        {
            var now = DateTime.UtcNow;
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var backlogId = Guid.NewGuid();
            var decisionId = Guid.NewGuid();
            var cardId = Guid.NewGuid();
            var movedAt = now.AddMinutes(-minutesAgo);
            await using var db = CreateContext();
            db.Projects.Add(new Project
            {
                Id = projectId,
                Name = $"attention-project-{projectId:N}"[..30],
                GitRepositoryUrl = "https://example.test/attention.git",
                LocalRepositoryPath = Path.Combine(Path.GetTempPath(), $"attention-{projectId:N}"),
                BaseBranch = "main",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.Boards.Add(new Board
            {
                Id = boardId,
                ProjectId = projectId,
                Name = $"attention-board-{boardId:N}"[..30],
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.BoardColumns.AddRange(
                new BoardColumn { Id = backlogId, BoardId = boardId, StateKey = "backlog", Name = "Backlog", ColumnOrder = 0, CardStatus = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now },
                new BoardColumn { Id = decisionId, BoardId = boardId, StateKey = "needs-decision", Name = "Needs decision", ColumnOrder = 4, CardStatus = CardStatus.NeedsDecision, CreatedAt = now, UpdatedAt = now });
            db.Cards.Add(new Card
            {
                Id = cardId,
                BoardId = boardId,
                BoardColumnId = decisionId,
                Identifier = $"ATTN-{cardId:N}"[..16],
                Title = "Needs an operator decision",
                Status = CardStatus.NeedsDecision,
                CreatedAt = now.AddHours(-1),
                UpdatedAt = movedAt,
                RevisionCount = 1,
            });
            db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(),
                CardId = cardId,
                RevisionNumber = 1,
                Kind = kind,
                FromColumnId = backlogId,
                ToColumnId = decisionId,
                FromStatus = CardStatus.Backlog,
                ToStatus = CardStatus.NeedsDecision,
                Reason = reason,
                CreatedAt = movedAt,
            });
            await db.SaveChangesAsync();
            _projects.Add(projectId);
            _boards.Add(boardId);
            _cards.Add(cardId);
            return (cardId, boardId, movedAt);
        }

        public async Task<DateTime> AddNeedsDecisionRevisionAsync(Guid cardId, string reason, int minutesAgo)
        {
            var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
            await using var db = CreateContext();
            var card = await db.Cards.SingleAsync(c => c.Id == cardId);
            db.CardRevisions.Add(new CardRevision
            {
                Id = Guid.NewGuid(),
                CardId = cardId,
                RevisionNumber = ++card.RevisionCount,
                Kind = CardRevisionKind.Move,
                FromColumnId = card.BoardColumnId,
                ToColumnId = card.BoardColumnId,
                FromStatus = CardStatus.NeedsDecision,
                ToStatus = CardStatus.NeedsDecision,
                Reason = reason,
                CreatedAt = at,
            });
            await db.SaveChangesAsync();
            return at;
        }

        public async Task SetTaskStatusAsync(Guid taskId, AgentTaskStatus status)
        {
            await using var db = CreateContext();
            await db.AgentTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, status)
                    .SetProperty(t => t.CompletedAt, DateTime.UtcNow));
        }

        public async Task AddIncidentAsync(
            Guid agentId, Guid? sessionId, AgentIncidentKind kind, AlertSeverity severity,
            string message, int minutesAgo, string? failureReason = null)
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
                FailureReason = failureReason,
                CreatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
            });
            await db.SaveChangesAsync();
        }

        public async Task<Guid> AddHoldAsync(
            AgentKind kind,
            string alias,
            Guid? sessionId = null,
            DateTime? until = null,
            string reason = "session-limit resets 18:10 Europe/London",
            string? rawText = null,
            ModelAvailabilitySource source = ModelAvailabilitySource.AutoDetected)
        {
            var id = Guid.NewGuid();
            await using var db = CreateContext();
            await db.ModelAvailabilityHolds
                .Where(h => h.Kind == kind && h.ModelAlias == alias && h.ClearedAt == null)
                .ExecuteDeleteAsync();
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = id,
                Kind = kind,
                ModelAlias = alias,
                Source = source,
                DisabledUntil = until,
                HitAt = DateTime.UtcNow.AddMinutes(-5),
                Reason = reason,
                RawText = rawText,
                SourceSessionId = sessionId,
            });
            await db.SaveChangesAsync();
            _holds.Add(id);
            _holdKeys.Add($"{kind}/{alias}");
            return id;
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.ModelAvailabilityHolds.Where(h => _holds.Contains(h.Id)).ExecuteDeleteAsync();
            await db.TranscriptEntries.Where(e => _sessions.Contains(e.AgentSessionId)).ExecuteDeleteAsync();
            await db.SessionQueuedMessages.Where(m => _sessions.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
            await db.AgentTaskEvents.Where(e => _tasks.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            await db.AgentIncidents.Where(i => i.AgentId != null && _agents.Contains(i.AgentId.Value)).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => _tasks.Contains(t.Id)).ExecuteDeleteAsync();
            // A session pointed at one of our cards would block the card delete (CARD-0040 tests).
            await db.AgentSessions.Where(s => s.CardId != null && _cards.Contains(s.CardId!.Value))
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CardId, (Guid?)null));
            await db.CardRevisions.Where(r => _cards.Contains(r.CardId)).ExecuteDeleteAsync();
            await db.Cards.Where(c => _cards.Contains(c.Id)).ExecuteDeleteAsync();
            await db.BoardColumns.Where(c => _boards.Contains(c.BoardId)).ExecuteDeleteAsync();
            await db.Boards.Where(b => _boards.Contains(b.Id)).ExecuteDeleteAsync();
            await db.Projects.Where(p => _projects.Contains(p.Id)).ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => c.AgentId != null && _agents.Contains(c.AgentId!.Value))
                .ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => _sessions.Contains(s.Id)).ExecuteDeleteAsync();
            await db.ScheduleFires.Where(f => db.Schedules.Any(s => s.Id == f.ScheduleId && s.AgentId != null && _agents.Contains(s.AgentId.Value))).ExecuteDeleteAsync();
            await db.Schedules.Where(s => s.AgentId != null && _agents.Contains(s.AgentId.Value)).ExecuteDeleteAsync();
            await db.Agents.Where(a => _agents.Contains(a.Id)).ExecuteDeleteAsync();
        }
    }

    private sealed class RecordingWorkspaceProbe : IWorkspaceProgressProbe
    {
        public int Calls { get; private set; }
        public List<string> Directories { get; } = [];

        public Task<WorkspaceProgressArm> ProbeProgressAsync(
            string? workingDirectory, DateTime since, bool sharedCheckout, CancellationToken ct)
        {
            Calls++;
            if (workingDirectory is not null)
                Directories.Add(workingDirectory);
            return Task.FromResult(new WorkspaceProgressArm(false, null, null, sharedCheckout));
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

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

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

    [Test]
    public async Task the_summary_route_returns_counts_matching_the_full_projection()
    {
        _taskId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AgentTasks.Add(new AgentTask
            {
                Id = _taskId,
                RootTaskId = _taskId,
                Title = "summary wiring probe",
                Goal = "prove the counts route exists",
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
        var fullResponse = await client.GetAsync("/api/attention");
        var summaryResponse = await client.GetAsync("/api/attention/summary");

        fullResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        summaryResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var full = await fullResponse.Content.ReadFromJsonAsync<AttentionDto>(Json);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<AttentionSummaryDto>(Json);
        full.ShouldNotBeNull();
        summary.ShouldNotBeNull();
        full!.Items.ShouldContain(i => i.TaskId == _taskId);
        summary!.Open.ShouldBeGreaterThanOrEqualTo(1);
        summary.GeneratedAt.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-5));
    }
}
