using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskReplyIntegrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C714_Grok_batched_replay_uses_report_response(bool disableGrace)
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedGrokAsync(workspace.Path);
        await Card0714Transcript.SeedReplayAsync(sessionId, task.Id, task.DispatchedAt!.Value);
        var settings = disableGrace
            ? new DelegationSettings { ReplyInlineMaxChars = 20_000, FinalMessageGraceSeconds = 0 }
            : null;
        await CreateService(settings: settings).OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(Card0714Transcript.StoredReport(task.Id));
        settled.Result.ShouldNotContain(Card0714Transcript.Acknowledgement);
        settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        settled.NextStage.ShouldBe(PipelineHandoffKind.Review);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Completed)).ShouldBe(1);
        (await verify.AgentIncidents.AnyAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated)).ShouldBeFalse();
        settled.ReportNudgedAt.ShouldBeNull();
    }

    [Test]
    public async Task C714_Claude_ack_after_report_uses_report_response()
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-10);
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, configure: t => t.DispatchedAt = dispatched);
        const string report = "The report itself, before the acknowledgement.";
        await SeedTurnAsync(sessionId, DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.", report);
        await SeedSubagentNotificationAsync(sessionId, "toolu_ack", "Bare acknowledgement only.", dispatched.AddMinutes(5));
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(report);
        settled.Result.ShouldNotContain("Bare acknowledgement");
    }

    [Test]
    public async Task C714_Claude_notification_final_report_still_settles()
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-6);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);
        const string verdict = "Verdict: keep as is — no real problem found.";
        var service = CreateService();
        var launched = await SeedSubagentFanOutAsync(sessionId, dispatched, task.Id);
        for (var i = 0; i < 4; i++)
        {
            await SeedSubagentNotificationAsync(
                sessionId, launched[i], i == 3 ? verdict : $"Reviewer {i} came back clean.",
                dispatched.AddMinutes(2 + i), task.Id);
        }

        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(verdict);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Completed)).ShouldBe(1);
    }

    [Test]
    public async Task C714_Codex_final_answer_uses_turn_id()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.Codex);
        const string final = "The Codex final answer.";
        await SeedCodexTurnAsync(
            sessionId,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nDo the thing.",
            "commentary that is not the answer",
            final);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(final);
        settled.Result.ShouldNotContain("commentary");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C714_Parent_receives_selected_report_once(bool busy)
    {
        using var workspace = new TempWorkspace();
        var factory = NewDeliveryFactory();
        var parent = await SeedSessionAsync(workspace.Path);
        var terminal = AttachTerminal(factory, parent);
        await SeedParentHistoryAsync(parent);
        if (busy)
            await SeedEntryAsync(parent, TranscriptKinds.UserPrompt, "still writing the next instruction", DateTime.UtcNow);
        var (task, sessionId) = await SeedGrokAsync(workspace.Path, parent);
        await Card0714Transcript.SeedReplayAsync(sessionId, task.Id, task.DispatchedAt!.Value);
        var stored = Card0714Transcript.StoredReport(task.Id);
        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);

        if (busy)
        {
            await Queue(factory).FlushSessionAsync(parent, CancellationToken.None);
            terminal.SubmittedBodies.ShouldBeEmpty();
            await using var pending = CreateContext();
            (await pending.SessionQueuedMessages.SingleAsync(
                m => m.AgentSessionId == parent && m.Origin == QueuedMessageOrigin.Delegation))
                .Status.ShouldBe(QueuedMessageStatus.Pending);
            await SeedEntryAsync(parent, TranscriptKinds.TurnEnd, null, DateTime.UtcNow);
        }

        await Queue(factory).FlushSessionAsync(parent, CancellationToken.None);
        var (note, prompt) = await AssertParentReceivedNoteAsync(parent, task, stored);
        prompt.Text.ShouldNotContain(Card0714Transcript.Acknowledgement);
        note.Body.ShouldContain(stored);

        await CreateService(factory).OnTurnEndAsync(sessionId, CancellationToken.None);
        await Queue(factory).FlushSessionAsync(parent, CancellationToken.None);
        await AssertParentReceivedNoteAsync(parent, task, stored);
    }

    [Test]
    public async Task C714_Grok_repeated_housekeeping_is_skipped()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedGrokAsync(workspace.Path);
        await Card0714Transcript.SeedReplayAsync(
            sessionId, task.Id, task.DispatchedAt!.Value, extraReminders: 2);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Result.ShouldBe(Card0714Transcript.StoredReport(task.Id));
    }

    [Test]
    public async Task C714_Grok_rules_then_only_completion_cannot_settle()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"c714-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedGrokAsync(workspace.Path, configure: t => t.AgentId = agentId);
        await Card0714Transcript.SeedReplayAsync(
            sessionId, task.Id, task.DispatchedAt!.Value,
            includeReportText: false, includeReportBoundary: false, quoteTokensInRules: true);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
        stored.ReportNudgedAt.ShouldBeNull();
        (await verify.AgentIncidents.AnyAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated)).ShouldBeFalse();
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Completed)).ShouldBeFalse();
    }

    [Test]
    [Arguments(TranscriptKinds.UserPrompt)]
    [Arguments(TranscriptKinds.QueuedUserPrompt)]
    public async Task C714_Genuine_unmarked_turn_is_barrier(string kind)
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedGrokAsync(workspace.Path);
        await Card0714Transcript.SeedReplayAsync(sessionId, task.Id, task.DispatchedAt!.Value);
        var at = task.DispatchedAt!.Value.AddMinutes(50);
        await SeedEntryAsync(sessionId, kind, "a human typed this without the marker", at);
        await SeedEntryAsync(sessionId, TranscriptKinds.AssistantText, "The human's own answer.", at);
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, at);

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
    }

    /// <summary>
    /// The unmarked prompt and the completion reminder share one turn, so the turn end's
    /// immediate prompt is the reminder. The older marked report must not settle.
    /// </summary>
    [Test]
    public async Task C714_Grok_unmarked_prompt_before_reminder_is_barrier()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"c714-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedGrokAsync(workspace.Path, configure: t => t.AgentId = agentId);
        await SeedMarkedReportThenHousekeepingAsync(
            sessionId, task, TranscriptKinds.UserPrompt, Card0714Transcript.Reminder);
        await AssertOlderReportStaysUncorrelatedAsync(task.Id, sessionId);
    }

    [Test]
    public async Task C714_Grok_unmarked_queued_prompt_before_reminder_is_barrier()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"c714-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedGrokAsync(workspace.Path, configure: t => t.AgentId = agentId);
        await SeedMarkedReportThenHousekeepingAsync(
            sessionId, task, TranscriptKinds.QueuedUserPrompt, Card0714Transcript.Reminder);
        await AssertOlderReportStaysUncorrelatedAsync(task.Id, sessionId);
    }

    /// <summary>
    /// Claude: an unmarked prompt, then a task-notification, then an answer with no closing token.
    /// </summary>
    [Test]
    public async Task C714_Claude_unmarked_prompt_before_notification_is_barrier()
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddHours(-2);
        var agentId = await SeedAgentAsync(workspace.Path, $"c714-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, configure: t =>
        {
            t.AgentId = agentId;
            t.AgentKind = AgentKind.ClaudeCode;
            t.DispatchedAt = dispatched;
        });
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        task.DispatchedAt = dispatched;
        const string notification =
            "<task-notification>\n<task-id>a548067d72b9d6de9</task-id>\n"
            + "<tool-use-id>toolu_c714barrier</tool-use-id>\n<status>completed</status>\n"
            + "<result>The subagent's own report.</result>\n</task-notification>";
        await SeedMarkedReportThenHousekeepingAsync(
            sessionId, task, TranscriptKinds.UserPrompt, notification);
        await AssertOlderReportStaysUncorrelatedAsync(task.Id, sessionId);
    }

    /// <summary>
    /// Auto-compaction writes only the continuation prompt: no command wrapper, stdout, or raw
    /// echo. That prompt is the turn end's immediate record, and the marked brief in the same
    /// turn still has to settle. A manual <c>/compact</c> is covered by
    /// <c>a_compaction_the_delegate_runs_mid_task_still_settles_against_the_brief</c>.
    /// </summary>
    [Test]
    public async Task C714_Claude_auto_compaction_continuation_settles()
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-20);
        var (task, sessionId) = await SeedDispatchedTaskAsync(
            workspace.Path, configure: t => t.DispatchedAt = dispatched);
        const string report = "Verdict: keep as is. The guard is sound.";

        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(task.Id) + "\n\nReview the move guard.",
            dispatched.AddMinutes(1));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.AssistantText, "Reading the spec.", dispatched.AddMinutes(2));
        await SeedEntryAsync(
            sessionId, TranscriptKinds.UserPrompt,
            TranscriptKinds.CompactionContinuationPromptPrefix
            + " that ran out of context. The summary below covers…",
            dispatched.AddMinutes(5));
        await SeedResponseAsync(
            sessionId, report, dispatched.AddMinutes(8), DelegationReportFormatter.TaskMarker(task.Id));

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(report, "the report is the turn-ending response, not the continuation prompt");
    }

    [Test]
    public async Task C714_Codex_unmarked_user_turn_is_barrier()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedDispatchedTaskAsync(workspace.Path, configure: t =>
        {
            t.AgentKind = AgentKind.Codex;
            t.DispatchedAt = DateTime.UtcNow.AddHours(-2);
        });
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.Codex);
        await Card0714Transcript.SeedReplayAsync(sessionId, task.Id, task.DispatchedAt!.Value);
        var at = task.DispatchedAt!.Value.AddMinutes(50);
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, Card0714Transcript.Reminder, at);
        await SeedEntryAsync(sessionId, TranscriptKinds.AssistantText, "Ordinary Codex prose.", at);
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, at);
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
    }

    [Test]
    public async Task C714_Current_report_is_chosen_after_old_task_history()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedGrokAsync(workspace.Path);
        await Card0714Transcript.SeedReplayAsync(sessionId, task.Id, task.DispatchedAt!.Value);
        var old = DelegationReportFormatter.TaskMarker(Guid.NewGuid());
        var at = task.DispatchedAt!.Value.AddMinutes(1);
        await using (var db = CreateContext())
        {
            var prompt = NewEntry(sessionId, 2, TranscriptKinds.UserPrompt, old + "\n\nprevious task");
            prompt.Timestamp = at;
            var text = NewEntry(sessionId, 3, TranscriptKinds.AssistantText, "The previous task's report.");
            text.ApiCallId = "old-report";
            text.Timestamp = at;
            var end = NewEntry(sessionId, 4, TranscriptKinds.TurnEnd, null);
            end.ApiCallId = "old-report";
            end.StopReason = TranscriptKinds.StopReasons.EndTurn;
            end.Timestamp = at;
            db.TranscriptEntries.AddRange(prompt, text, end);
            await db.SaveChangesAsync();
        }

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(Card0714Transcript.StoredReport(task.Id));
        settled.Result.ShouldNotContain("previous task");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C714_Predispatch_current_marker_is_rejected(bool equalFloor)
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddHours(-2);
        var (task, sessionId) = await SeedGrokAsync(workspace.Path, configure: t => t.DispatchedAt = dispatched);
        var native = equalFloor ? dispatched : dispatched.AddTicks(-1);
        await using (var db = CreateContext())
        {
            var prompt = NewEntry(sessionId, 40, TranscriptKinds.UserPrompt, Card0714Transcript.Brief(task.Id));
            prompt.Timestamp = native;
            prompt.CreatedAt = DateTime.UtcNow;
            var text = NewEntry(sessionId, 41, TranscriptKinds.AssistantText, Card0714Transcript.ReportText(task.Id));
            text.ApiCallId = Card0714Transcript.ReportApiCallId;
            text.Timestamp = native;
            text.CreatedAt = DateTime.UtcNow;
            var end = NewEntry(sessionId, 42, TranscriptKinds.TurnEnd, null);
            end.ApiCallId = Card0714Transcript.ReportApiCallId;
            end.StopReason = TranscriptKinds.StopReasons.EndTurn;
            end.Timestamp = native;
            end.CreatedAt = DateTime.UtcNow;
            db.TranscriptEntries.AddRange(prompt, text, end);
            await db.SaveChangesAsync();
        }

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
        stored.ReportNudgedAt.ShouldBeNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task C714_Old_task_marker_is_never_current(int timeShape)
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddHours(-2);
        var (task, sessionId) = await SeedGrokAsync(workspace.Path, configure: t => t.DispatchedAt = dispatched);
        DateTime? native = timeShape switch
        {
            0 => dispatched.AddMinutes(-10),
            1 => dispatched.AddMinutes(5),
            _ => null,
        };
        var oldPrompt = DelegationReportFormatter.TaskMarker(Guid.NewGuid()) + "\n\nold brief";
        var assistant = "stale work\n" + DelegationReportFormatter.ReportToken(task.Id, "done");
        await using (var db = CreateContext())
        {
            var prompt = NewEntry(sessionId, 5, TranscriptKinds.UserPrompt, oldPrompt);
            prompt.Timestamp = native;
            prompt.CreatedAt = DateTime.UtcNow;
            var text = NewEntry(sessionId, 6, TranscriptKinds.AssistantText, assistant);
            text.ApiCallId = "old-call";
            text.Timestamp = native;
            text.CreatedAt = DateTime.UtcNow;
            var end = NewEntry(sessionId, 7, TranscriptKinds.TurnEnd, null);
            end.ApiCallId = "old-call";
            end.StopReason = TranscriptKinds.StopReasons.EndTurn;
            end.Timestamp = native;
            end.CreatedAt = DateTime.UtcNow;
            db.TranscriptEntries.AddRange(prompt, text, end);
            await db.SaveChangesAsync();
        }

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldNotBe(AgentTaskStatus.Succeeded);
        stored.Result.ShouldBeNull();
        stored.ReportNudgedAt.ShouldBeNull();
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Completed)).ShouldBeFalse();
    }

    [Test]
    public async Task C714_Reply_watermark_prevents_stale_report()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedGrokAsync(workspace.Path);
        await Card0714Transcript.SeedReplayAsync(sessionId, task.Id, task.DispatchedAt!.Value);
        await using (var db = CreateContext())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            row.Status = AgentTaskStatus.Working;
            row.RepliedAtSequence = Card0714Transcript.BriefSequence;
            await db.SaveChangesAsync();
        }

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Working);
        stored.Result.ShouldBeNull();
        (await verify.SessionQueuedMessages.CountAsync(
            m => m.AgentSessionId == sessionId && m.Origin == QueuedMessageOrigin.Delegation)).ShouldBe(0);
    }

    [Test]
    public async Task C714_Grok_selected_report_waits_for_own_text()
    {
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(workspace.Path, $"c714-{Guid.NewGuid():N}"[..20]);
        var (task, sessionId) = await SeedGrokAsync(workspace.Path, configure: t => t.AgentId = agentId);
        await Card0714Transcript.SeedReplayAsync(
            sessionId, task.Id, task.DispatchedAt!.Value,
            includeReportText: false, reportBoundaryCreatedAt: DateTime.UtcNow);
        var service = CreateService();
        await service.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using (var mid = CreateContext())
        {
            var waiting = await mid.AgentTasks.SingleAsync(t => t.Id == task.Id);
            waiting.Status.ShouldBe(AgentTaskStatus.Dispatched);
            waiting.Result.ShouldBeNull();
            (await mid.AgentIncidents.AnyAsync(
                i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated)).ShouldBeFalse();
        }

        await using (var db = CreateContext())
        {
            var row = NewEntry(sessionId, 277, TranscriptKinds.AssistantText, Card0714Transcript.ReportText(task.Id));
            row.ApiCallId = Card0714Transcript.ReportApiCallId;
            row.Timestamp = DateTime.UtcNow;
            row.CreatedAt = DateTime.UtcNow;
            db.TranscriptEntries.Add(row);
            await db.SaveChangesAsync();
        }

        await service.OnTurnEndAsync(sessionId, CancellationToken.None);
        await using var verify = CreateContext();
        (await verify.AgentTasks.SingleAsync(t => t.Id == task.Id))
            .Result.ShouldBe(Card0714Transcript.StoredReport(task.Id));
    }

    [Test]
    public async Task C714_Late_report_text_after_reminder_keeps_own_api_id()
    {
        using var workspace = new TempWorkspace();
        var (task, sessionId) = await SeedGrokAsync(workspace.Path);
        await Card0714Transcript.SeedReplayAsync(
            sessionId, task.Id, task.DispatchedAt!.Value,
            includeReportText: false, reportBoundaryCreatedAt: DateTime.UtcNow);
        await using (var db = CreateContext())
        {
            var row = NewEntry(sessionId, 277, TranscriptKinds.AssistantText, Card0714Transcript.ReportText(task.Id));
            row.ApiCallId = Card0714Transcript.ReportApiCallId;
            row.Timestamp = DateTime.UtcNow;
            row.CreatedAt = DateTime.UtcNow;
            db.TranscriptEntries.Add(row);
            await db.SaveChangesAsync();
        }

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var settled = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldBe(Card0714Transcript.StoredReport(task.Id));
        settled.Result.ShouldNotContain(Card0714Transcript.Acknowledgement);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C714_Selected_error_or_cancel_does_not_fall_back(bool apiError)
    {
        using var workspace = new TempWorkspace();
        var dispatched = DateTime.UtcNow.AddMinutes(-20);
        var (task, sessionId) = await SeedGrokAsync(workspace.Path, configure: t => t.DispatchedAt = dispatched);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        await SeedTurnAsync(sessionId, marker + "\n\nfirst attempt", "OLDER DONE REPORT");
        if (apiError)
        {
            await SeedApiErrorStubTurnAsync(sessionId, marker + "\n\nsecond attempt");
        }
        else
        {
            await SeedTurnAsync(
                sessionId, marker + "\n\nsecond attempt", "cancelled narration",
                stopReason: TranscriptKinds.StopReasons.Cancelled, closingVerdict: false);
        }

        var at = DateTime.UtcNow;
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, Card0714Transcript.Reminder, at);
        await SeedEntryAsync(sessionId, TranscriptKinds.AssistantText, Card0714Transcript.Acknowledgement, at, Card0714Transcript.AcknowledgementApiCallId);
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, at, Card0714Transcript.AcknowledgementApiCallId);

        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldNotBe(AgentTaskStatus.Succeeded);
        (stored.Result ?? "").ShouldNotContain("OLDER DONE REPORT");
        if (apiError)
        {
            (await verify.AgentTaskEvents.AnyAsync(
                e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.ApiErrorDeferred)).ShouldBeTrue();
        }
        else
        {
            var warning = await verify.AgentTaskEvents.SingleAsync(
                e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Warning);
            warning.Detail.ShouldContain("not a report");
        }
    }

    /// <summary>
    /// Older marked report, then a real unmarked prompt, then a housekeeping prompt, then an
    /// answer and one turn end. The housekeeping prompt is the turn end's immediate prompt.
    /// </summary>
    private static async Task SeedMarkedReportThenHousekeepingAsync(
        Guid sessionId, AgentTask task, string unmarkedKind, string housekeeping)
    {
        var at = task.DispatchedAt!.Value.AddMinutes(40);
        var reportId = "older-report-" + Guid.NewGuid().ToString("N");
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, Card0714Transcript.Brief(task.Id), at);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.AssistantText, Card0714Transcript.ReportText(task.Id), at, reportId);
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, at, reportId);
        var later = at.AddMinutes(10);
        await SeedEntryAsync(sessionId, unmarkedKind, "a human typed this without the marker", later);
        await SeedEntryAsync(sessionId, TranscriptKinds.UserPrompt, housekeeping, later);
        await SeedEntryAsync(
            sessionId, TranscriptKinds.AssistantText, "The answer after the housekeeping prompt.", later,
            "answer-" + Guid.NewGuid().ToString("N"));
        await SeedEntryAsync(sessionId, TranscriptKinds.TurnEnd, null, later);
    }

    private static async Task AssertOlderReportStaysUncorrelatedAsync(Guid taskId, Guid sessionId)
    {
        await CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
        (await verify.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == taskId && e.Type == AgentTaskEventType.Completed)).ShouldBeFalse();
        (await verify.AgentIncidents.AnyAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated)).ShouldBeTrue();
    }

    private static async Task<(AgentTask Task, Guid SessionId)> SeedGrokAsync(
        string path, Guid? parent = null, Action<AgentTask>? configure = null)
    {
        var dispatched = DateTime.UtcNow.AddHours(-2);
        var (task, sessionId) = await SeedDispatchedTaskAsync(path, parent, t =>
        {
            t.AgentKind = AgentKind.Grok;
            t.DispatchedAt = dispatched;
            configure?.Invoke(t);
        });
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.Grok);
        task.DispatchedAt = dispatched;
        return (task, sessionId);
    }
}
