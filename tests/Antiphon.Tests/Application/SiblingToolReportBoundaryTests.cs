using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1473. A stored ToolCall with the same ApiCallId, inside the same eligible
/// prompt span, proves an <c>end_turn</c> boundary is a sibling phantom. The reply
/// path must not nudge or settle on it. Text-only finals, <c>max_tokens</c>, other
/// response ids, and a newer real prompt stay on the existing path.
/// CARD-1821. The same proof applies when the nearest prompt is housekeeping
/// (a compaction continuation or a local command) or a Claude task notification.
/// The tool span runs from the marked owner to the next real prompt.
/// <c>max_tokens</c> and <c>cancelled</c> stay terminal.
/// A sibling phantom is not an ownership floor: the next housekeeping boundary
/// still settles on the marked brief. The phantom alone does not nudge or settle.
/// </summary>
[Category("Integration")]
public sealed class SiblingToolReportBoundaryTests
{
    private const string MeasuredApiCallId = "msg_011CgGrJrWdLLmVkHc21dPpT";

    [Test]
    public async Task Same_response_tool_siblings_do_not_nudge_or_settle()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);

        // Measured ca843435 sequences #1, #5, #6, #7, #8. Tools are stored, so the
        // end_turn records are siblings of that response, not a finished report.
        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nInvestigate the report nudge.", at),
            Turn(sessionId, 5, MeasuredApiCallId, TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(10)),
            Entry(sessionId, 6, TranscriptKinds.AssistantText,
                "Brief read. I'll gather the card and the source in parallel.", at.AddSeconds(10), MeasuredApiCallId),
            Turn(sessionId, 7, MeasuredApiCallId, TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(10)),
            Tool(sessionId, 8, MeasuredApiCallId, at.AddSeconds(11)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = AgentTaskReplyIntegrationTests.CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
        stored.CompletedAt.ShouldBeNull();
        stored.ReportNudgedAt.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Warning
            && e.Detail != null
            && e.Detail.Contains("closing report line")))
            .ShouldBe(0);
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId
            && m.Body.Contains("closing report line")))
            .ShouldBe(0);
    }

    [Test]
    public async Task Genuine_text_only_final_still_settles()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string api = "msg_text_only_final";
        var report = "Gathered the card and the source.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWrite the report.", at),
            Entry(sessionId, 2, TranscriptKinds.AssistantText, report, at.AddSeconds(2), api),
            Turn(sessionId, 3, api, TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(2)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = AgentTaskReplyIntegrationTests.CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Succeeded);
        stored.Result.ShouldBe("Gathered the card and the source.");
        stored.ReportNudgedAt.ShouldBeNull();
    }

    [Test]
    public async Task Max_tokens_with_tool_evidence_remains_terminal()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string api = "msg_max_tokens_truncation";
        var report = "The turn stopped on the token ceiling.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nContinue.", at),
            Tool(sessionId, 2, api, at.AddSeconds(1)),
            Entry(sessionId, 3, TranscriptKinds.AssistantText, report, at.AddSeconds(2), api),
            Turn(sessionId, 4, api, "max_tokens", at.AddSeconds(2)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = AgentTaskReplyIntegrationTests.CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Succeeded);
        stored.Result.ShouldBe("The turn stopped on the token ceiling.");
        stored.ReportNudgedAt.ShouldBeNull();
    }

    [Test]
    public async Task Other_response_tools_do_not_hide_a_real_boundary()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var report = "The final response called no tool.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nFinish.", at),
            Tool(sessionId, 2, "msg_earlier_tools", at.AddSeconds(1)),
            Entry(sessionId, 3, TranscriptKinds.AssistantText, report, at.AddSeconds(3), "msg_real_final"),
            Turn(sessionId, 4, "msg_real_final", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(3)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = AgentTaskReplyIntegrationTests.CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Succeeded);
        stored.Result.ShouldBe("The final response called no tool.");
        stored.ReportNudgedAt.ShouldBeNull();
    }

    [Test]
    public async Task New_real_prompt_is_still_an_attribution_barrier()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var olderReport = "The earlier brief already finished.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWrite the report.", at),
            Entry(sessionId, 2, TranscriptKinds.AssistantText, olderReport, at.AddSeconds(2), "msg_older_final"),
            Turn(sessionId, 3, "msg_older_final", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(2)),
            Entry(sessionId, 4, TranscriptKinds.UserPrompt, "what files are in this directory?", at.AddSeconds(5)),
            Entry(sessionId, 5, TranscriptKinds.AssistantText, "Listing the workspace.", at.AddSeconds(6), "msg_newer_response"),
            Turn(sessionId, 6, "msg_newer_response", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(6)),
            Tool(sessionId, 7, "msg_newer_response", at.AddSeconds(7)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = AgentTaskReplyIntegrationTests.CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
        stored.CompletedAt.ShouldBeNull();
    }

    /// <summary>
    /// CARD-1821 measured rows: marked brief, earlier text and tool, auto compact,
    /// continuation prompt, then an <c>end_turn</c> that shares <c>msg_b</c> with a
    /// tool stored after that prompt. The continuation is housekeeping, so the
    /// boundary must not nudge or settle.
    /// </summary>
    [Test]
    public async Task Compaction_continuation_sibling_end_turn_does_not_nudge_or_settle()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string earlier = "msg_1821_earlier";
        const string phantom = "msg_1821_phantom";

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nInvestigate the report nudge.", at),
            Entry(sessionId, 2, TranscriptKinds.AssistantText,
                "Brief read. I'll gather the card and the source in parallel.", at.AddSeconds(2), earlier),
            Tool(sessionId, 3, earlier, at.AddSeconds(3)),
            Compact(sessionId, 4, at.AddSeconds(4)),
            Entry(sessionId, 5, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(5)),
            Turn(sessionId, 6, phantom, TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(6)),
            Entry(sessionId, 7, TranscriptKinds.AssistantText,
                "Continuing after compaction. The tool call is still this response.", at.AddSeconds(6), phantom),
            Tool(sessionId, 8, phantom, at.AddSeconds(7)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await AssertNotNudgedOrSettledAsync(task.Id, sessionId);
    }

    /// <summary>
    /// The only matching tool sits after the marked brief and before the continuation.
    /// The span floor is that brief, not the housekeeping prompt.
    /// </summary>
    [Test]
    public async Task Sibling_tool_before_the_continuation_still_hides_the_end_turn()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string phantom = "msg_1821_before_continuation";

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nKeep going.", at),
            Tool(sessionId, 2, phantom, at.AddSeconds(2)),
            Entry(sessionId, 3, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(3)),
            Turn(sessionId, 4, phantom, TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(4)),
            Entry(sessionId, 5, TranscriptKinds.AssistantText,
                "The tool was stored before the continuation prompt.", at.AddSeconds(4), phantom));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await AssertNotNudgedOrSettledAsync(task.Id, sessionId);
    }

    [Test]
    public async Task Local_command_sibling_end_turn_does_not_nudge_or_settle()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string phantom = "msg_1821_local_command";

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nCheck status.", at),
            Entry(sessionId, 2, TranscriptKinds.UserPrompt,
                "<command-name>/status</command-name>", at.AddSeconds(2)),
            Turn(sessionId, 3, phantom, TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(3)),
            Entry(sessionId, 4, TranscriptKinds.AssistantText,
                "Status is still running.", at.AddSeconds(3), phantom),
            Tool(sessionId, 5, phantom, at.AddSeconds(4)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await AssertNotNudgedOrSettledAsync(task.Id, sessionId);
    }

    /// <summary>
    /// Claude walks a task notification back to the marked brief once the answer
    /// carries a closing token. A stored tool with that answer's id means the
    /// <c>end_turn</c> has not finished, even when the tool precedes the notification.
    /// </summary>
    [Test]
    public async Task Claude_notification_sibling_end_turn_does_not_settle()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string phantom = "msg_1821_notification";
        var report = "Folded the notification.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWait for the review.", at),
            Tool(sessionId, 2, phantom, at.AddSeconds(2)),
            Entry(sessionId, 3, TranscriptKinds.UserPrompt, NotificationText, at.AddSeconds(3)),
            Turn(sessionId, 4, phantom, TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(4)),
            Entry(sessionId, 5, TranscriptKinds.AssistantText, report, at.AddSeconds(4), phantom));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await AssertNotNudgedOrSettledAsync(task.Id, sessionId);
    }

    [Test]
    public async Task Text_only_final_after_compaction_continuation_still_settles()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string api = "msg_1821_text_final";
        var report = "The turn finished after compaction.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWrite the report.", at),
            Compact(sessionId, 2, at.AddSeconds(2)),
            Entry(sessionId, 3, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(3)),
            Entry(sessionId, 4, TranscriptKinds.AssistantText, report, at.AddSeconds(4), api),
            Turn(sessionId, 5, api, TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(4)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await AssertSettledAsync(task.Id, "The turn finished after compaction.");
    }

    [Test]
    public async Task Max_tokens_after_compaction_continuation_stays_terminal()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string api = "msg_1821_max_tokens";
        var report = "The turn stopped on the token ceiling after compaction.\n"
            + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nContinue.", at),
            Entry(sessionId, 2, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(2)),
            Tool(sessionId, 3, api, at.AddSeconds(3)),
            Entry(sessionId, 4, TranscriptKinds.AssistantText, report, at.AddSeconds(4), api),
            Turn(sessionId, 5, api, "max_tokens", at.AddSeconds(4)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await AssertSettledAsync(task.Id, "The turn stopped on the token ceiling after compaction.");
    }

    [Test]
    public async Task Cancelled_after_compaction_continuation_stays_terminal()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string api = "msg_1821_cancelled";

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nContinue.", at),
            Entry(sessionId, 2, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(2)),
            Tool(sessionId, 3, api, at.AddSeconds(3)),
            Turn(sessionId, 4, api, TranscriptKinds.StopReasons.Cancelled, at.AddSeconds(4)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await AssertNotNudgedOrSettledAsync(task.Id, sessionId);
        await using var verify = AgentTaskReplyIntegrationTests.CreateContext();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Warning
            && e.Detail != null
            && e.Detail.Contains("cancelled")
            && e.Detail.Contains("not a report")))
            .ShouldBe(1);
    }

    [Test]
    public async Task Claude_notification_text_only_final_still_settles()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string api = "msg_1821_notification_final";
        var report = "Folded the notification.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWait for the review.", at),
            Entry(sessionId, 2, TranscriptKinds.UserPrompt, NotificationText, at.AddSeconds(2)),
            Entry(sessionId, 3, TranscriptKinds.AssistantText, report, at.AddSeconds(3), api),
            Turn(sessionId, 4, api, TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(3)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await AssertSettledAsync(task.Id, "Folded the notification.");
    }

    /// <summary>
    /// A tool stored after the next real prompt belongs to that newer prompt.
    /// It must not hide the compaction continuation's own text-only final.
    /// </summary>
    [Test]
    public async Task Tool_after_the_next_real_prompt_does_not_hide_a_compaction_final()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        const string api = "msg_1821_capped";
        var report = "Finished before the later prompt.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWrite the report.", at),
            Entry(sessionId, 2, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(2)),
            Entry(sessionId, 3, TranscriptKinds.AssistantText, report, at.AddSeconds(3), api),
            Turn(sessionId, 4, api, TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(3)),
            Entry(sessionId, 5, TranscriptKinds.UserPrompt, "what files are in this directory?", at.AddSeconds(5)),
            Tool(sessionId, 6, api, at.AddSeconds(6)));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);

        await AssertSettledAsync(task.Id, "Finished before the later prompt.");
    }

    /// <summary>
    /// CARD-1821 D-1. Rows #1 marked brief, #2 auto compact, #3 continuation,
    /// #4 end_turn msg_b, #5 text msg_b, #6 tool msg_b, #7 text msg_c with the
    /// done token, #8 end_turn msg_c. The final boundary is the report.
    /// </summary>
    [Test]
    public async Task Compaction_phantom_then_genuine_final_selects_the_final_and_settles()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var report = "Real final.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWork.", at),
            Compact(sessionId, 2, at.AddSeconds(1)),
            Entry(sessionId, 3, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(2)),
            Turn(sessionId, 4, "msg_b", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(3)),
            Entry(sessionId, 5, TranscriptKinds.AssistantText, "mid", at.AddSeconds(3), "msg_b"),
            Tool(sessionId, 6, "msg_b", at.AddSeconds(4)),
            Entry(sessionId, 7, TranscriptKinds.AssistantText, report, at.AddSeconds(5), "msg_c"),
            Turn(sessionId, 8, "msg_c", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(5)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.Selected);
        selection.BoundarySequence.ShouldBe(8);

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertSettledAsync(task.Id, "Real final.");
    }

    /// <summary>
    /// CARD-1821 D-1 local-command shape. #1 brief, #2 status command, #3 end_turn
    /// msg_b, #4 tool msg_b, #5 text msg_c with the done token, #6 end_turn msg_c.
    /// </summary>
    [Test]
    public async Task Local_command_phantom_then_genuine_final_selects_the_final_and_settles()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var report = "LC final.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWork.", at),
            Entry(sessionId, 2, TranscriptKinds.UserPrompt, "<command-name>/status</command-name>", at.AddSeconds(1)),
            Turn(sessionId, 3, "msg_b", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(2)),
            Tool(sessionId, 4, "msg_b", at.AddSeconds(3)),
            Entry(sessionId, 5, TranscriptKinds.AssistantText, report, at.AddSeconds(4), "msg_c"),
            Turn(sessionId, 6, "msg_c", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(4)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.Selected);
        selection.BoundarySequence.ShouldBe(6);

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertSettledAsync(task.Id, "LC final.");
    }

    /// <summary>
    /// Two phantom-then-final cycles in one sweep. The genuine end_turn of the first
    /// final still closes the owner window, so the sweep settles that report. A
    /// phantom floor must not leave the task Dispatched.
    /// </summary>
    [Test]
    public async Task Phantom_then_final_twice_settles_the_owner_visible_final()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var first = "First final.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");
        var second = "Second final.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWork.", at),
            Compact(sessionId, 2, at.AddSeconds(1)),
            Entry(sessionId, 3, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(2)),
            Turn(sessionId, 4, "msg_b1", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(3)),
            Entry(sessionId, 5, TranscriptKinds.AssistantText, "mid one", at.AddSeconds(3), "msg_b1"),
            Tool(sessionId, 6, "msg_b1", at.AddSeconds(4)),
            Entry(sessionId, 7, TranscriptKinds.AssistantText, first, at.AddSeconds(5), "msg_c1"),
            Turn(sessionId, 8, "msg_c1", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(5)),
            Entry(sessionId, 9, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(6)),
            Turn(sessionId, 10, "msg_b2", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(7)),
            Tool(sessionId, 11, "msg_b2", at.AddSeconds(8)),
            Entry(sessionId, 12, TranscriptKinds.AssistantText, second, at.AddSeconds(9), "msg_c2"),
            Turn(sessionId, 13, "msg_c2", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(9)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.Selected);
        selection.BoundarySequence.ShouldBe(8);

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertSettledAsync(task.Id, "First final.");
    }

    /// <summary>
    /// The D-1 prefix with no later final. The phantom is skipped and nothing older
    /// remains, so the task stays Dispatched with no nudge.
    /// </summary>
    [Test]
    public async Task Compaction_phantom_without_a_final_does_not_nudge_or_settle()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWork.", at),
            Compact(sessionId, 2, at.AddSeconds(1)),
            Entry(sessionId, 3, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(2)),
            Turn(sessionId, 4, "msg_b", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(3)),
            Entry(sessionId, 5, TranscriptKinds.AssistantText, "mid", at.AddSeconds(3), "msg_b"),
            Tool(sessionId, 6, "msg_b", at.AddSeconds(4)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.None);
        selection.Skipped.ShouldContain(new TaskReportSkip(4, "sibling-tool"));

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertNotNudgedOrSettledAsync(task.Id, sessionId);
    }

    /// <summary>
    /// The tool that proves the phantom is stored before that end_turn. A later
    /// text-only final in the same span still settles.
    /// </summary>
    [Test]
    public async Task Tool_stored_before_the_phantom_end_turn_still_lets_the_later_final_settle()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var report = "Final after an early tool.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWork.", at),
            Tool(sessionId, 2, "msg_b", at.AddSeconds(1)),
            Entry(sessionId, 3, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(2)),
            Turn(sessionId, 4, "msg_b", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(3)),
            Entry(sessionId, 5, TranscriptKinds.AssistantText, report, at.AddSeconds(4), "msg_c"),
            Turn(sessionId, 6, "msg_c", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(4)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.Selected);
        selection.BoundarySequence.ShouldBe(6);

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertSettledAsync(task.Id, "Final after an early tool.");
    }

    /// <summary>
    /// Notification analogue of D-1: the phantom end_turn shares the notification's
    /// response id with a tool, then a later end_turn carries the report.
    /// </summary>
    [Test]
    public async Task Notification_phantom_then_genuine_final_settles()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var report = "Noted the review.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWait for the review.", at),
            Entry(sessionId, 2, TranscriptKinds.UserPrompt, NotificationText, at.AddSeconds(1)),
            Turn(sessionId, 3, "msg_b", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(2)),
            Entry(sessionId, 4, TranscriptKinds.AssistantText, "Still folding.", at.AddSeconds(2), "msg_b"),
            Tool(sessionId, 5, "msg_b", at.AddSeconds(3)),
            Entry(sessionId, 6, TranscriptKinds.AssistantText, report, at.AddSeconds(4), "msg_c"),
            Turn(sessionId, 7, "msg_c", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(4)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.Selected);
        selection.BoundarySequence.ShouldBe(7);

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertSettledAsync(task.Id, "Noted the review.");
    }

    [Test]
    public async Task Max_tokens_final_after_compaction_phantom_stays_terminal()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var report = "Stopped on the ceiling.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nContinue.", at),
            Compact(sessionId, 2, at.AddSeconds(1)),
            Entry(sessionId, 3, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(2)),
            Turn(sessionId, 4, "msg_b", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(3)),
            Tool(sessionId, 5, "msg_b", at.AddSeconds(4)),
            Entry(sessionId, 6, TranscriptKinds.AssistantText, report, at.AddSeconds(5), "msg_c"),
            Turn(sessionId, 7, "msg_c", "max_tokens", at.AddSeconds(5)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.Selected);
        selection.BoundarySequence.ShouldBe(7);

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertSettledAsync(task.Id, "Stopped on the ceiling.");
    }

    [Test]
    public async Task Cancelled_stop_after_compaction_phantom_stays_terminal()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nContinue.", at),
            Entry(sessionId, 2, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(1)),
            Turn(sessionId, 3, "msg_b", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(2)),
            Tool(sessionId, 4, "msg_b", at.AddSeconds(3)),
            Turn(sessionId, 5, "msg_c", TranscriptKinds.StopReasons.Cancelled, at.AddSeconds(4)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.Interrupted);
        selection.BoundarySequence.ShouldBe(5);

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertNotNudgedOrSettledAsync(task.Id, sessionId);
        await using var verify = AgentTaskReplyIntegrationTests.CreateContext();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task.Id
            && e.Type == AgentTaskEventType.Warning
            && e.Detail != null
            && e.Detail.Contains("cancelled")
            && e.Detail.Contains("not a report")))
            .ShouldBe(1);
    }

    /// <summary>
    /// CARD-1821 A-1. A marked nudge, a compaction continuation, and a sibling
    /// phantom must not fall through to the older unmarked-output boundary.
    /// The real-prompt analogue already returns none.
    /// </summary>
    [Test]
    public async Task Marked_nudge_then_compaction_phantom_does_not_select_the_older_boundary()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWork.", at),
            Entry(sessionId, 2, TranscriptKinds.AssistantText, "first turn, no token", at.AddSeconds(1), "msg_a"),
            Turn(sessionId, 3, "msg_a", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(1)),
            Entry(sessionId, 4, TranscriptKinds.UserPrompt,
                marker + " Your turn ended without the closing report line.", at.AddSeconds(2)),
            Entry(sessionId, 5, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(3)),
            Turn(sessionId, 6, "msg_b", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(4)),
            Entry(sessionId, 7, TranscriptKinds.AssistantText,
                "mid " + DelegationReportFormatter.ReportToken(task.Id, "done"), at.AddSeconds(4), "msg_b"),
            Tool(sessionId, 8, "msg_b", at.AddSeconds(5)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.None);
        selection.BoundarySequence.ShouldBeNull();

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertNotNudgedOrSettledAsync(task.Id, sessionId);
    }

    [Test]
    public async Task Marked_nudge_then_real_prompt_phantom_stays_unselected()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWork.", at),
            Entry(sessionId, 2, TranscriptKinds.AssistantText, "first turn, no token", at.AddSeconds(1), "msg_a"),
            Turn(sessionId, 3, "msg_a", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(1)),
            Entry(sessionId, 4, TranscriptKinds.UserPrompt,
                marker + " Your turn ended without the closing report line.", at.AddSeconds(2)),
            Turn(sessionId, 6, "msg_b", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(4)),
            Entry(sessionId, 7, TranscriptKinds.AssistantText, "mid", at.AddSeconds(4), "msg_b"),
            Tool(sessionId, 8, "msg_b", at.AddSeconds(5)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.None);
        selection.BoundarySequence.ShouldBeNull();

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertNotNudgedOrSettledAsync(task.Id, sessionId);
    }

    /// <summary>
    /// Notification analogue of A-1. The phantom answer carries the closing token
    /// and a stored tool. The older unmarked-output boundary must not be selected.
    /// </summary>
    [Test]
    public async Task Notification_phantom_does_not_select_the_older_marked_boundary()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWork.", at),
            Entry(sessionId, 2, TranscriptKinds.AssistantText, "first turn, no token", at.AddSeconds(1), "msg_a"),
            Turn(sessionId, 3, "msg_a", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(1)),
            Entry(sessionId, 4, TranscriptKinds.UserPrompt,
                marker + " Your turn ended without the closing report line.", at.AddSeconds(2)),
            Entry(sessionId, 5, TranscriptKinds.UserPrompt, NotificationText, at.AddSeconds(3)),
            Turn(sessionId, 6, "msg_b", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(4)),
            Entry(sessionId, 7, TranscriptKinds.AssistantText,
                "Folded.\n" + DelegationReportFormatter.ReportToken(task.Id, "done"), at.AddSeconds(4), "msg_b"),
            Tool(sessionId, 8, "msg_b", at.AddSeconds(5)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.None);
        selection.BoundarySequence.ShouldBeNull();

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertNotNudgedOrSettledAsync(task.Id, sessionId);
    }

    /// <summary>
    /// A later housekeeping end_turn with no tool and no report must not steal a
    /// genuine final that already closed the brief's turn.
    /// </summary>
    [Test]
    public async Task Later_tokenless_housekeeping_end_turn_does_not_steal_a_genuine_final()
    {
        using var workspace = new AgentTaskReplyIntegrationTests.TempWorkspace();
        var (task, sessionId) = await AgentTaskReplyIntegrationTests.SeedDispatchedTaskAsync(workspace.Path);
        await Card0714Transcript.MarkProviderAsync(sessionId, task.Id, AgentKind.ClaudeCode);
        var at = (task.DispatchedAt ?? DateTime.UtcNow).AddMinutes(1);
        var marker = DelegationReportFormatter.TaskMarker(task.Id);
        var report = "The real report.\n" + DelegationReportFormatter.ReportToken(task.Id, "done");

        await SeedAsync(sessionId, at,
            Entry(sessionId, 1, TranscriptKinds.UserPrompt, marker + "\n\nWrite the report.", at),
            Entry(sessionId, 2, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(1)),
            Entry(sessionId, 3, TranscriptKinds.AssistantText, report, at.AddSeconds(2), "msg_c"),
            Turn(sessionId, 4, "msg_c", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(2)),
            Entry(sessionId, 5, TranscriptKinds.UserPrompt, ContinuationText, at.AddSeconds(3)),
            Entry(sessionId, 6, TranscriptKinds.AssistantText, "compaction ack", at.AddSeconds(4), "msg_d"),
            Turn(sessionId, 7, "msg_d", TranscriptKinds.StopReasons.EndTurn, at.AddSeconds(4)));

        var selection = await SelectStoredAsync(task.Id, sessionId);
        selection.Kind.ShouldBe(TaskReportSelectionKind.Selected);
        selection.BoundarySequence.ShouldBe(4);

        await AgentTaskReplyIntegrationTests.CreateService().OnTurnEndAsync(sessionId, CancellationToken.None);
        await AssertSettledAsync(task.Id, "The real report.");
    }

    private const string ContinuationText =
        TranscriptKinds.CompactionContinuationPromptPrefix
        + " that ran out of context. The summary below covers…";

    private const string NotificationText =
        "<task-notification>\n<task-id>a548067d72b9d6de9</task-id>\n"
        + "<tool-use-id>toolu_c1821</tool-use-id>\n<status>completed</status>\n"
        + "<result>The subagent's own report.</result>\n</task-notification>";

    private static async Task<TaskReportSelection> SelectStoredAsync(Guid taskId, Guid sessionId)
    {
        await using var db = AgentTaskReplyIntegrationTests.CreateContext();
        var stored = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        return await TaskReportTurnSelector.SelectAsync(db, sessionId, stored, CancellationToken.None);
    }

    private static async Task AssertNotNudgedOrSettledAsync(Guid taskId, Guid sessionId)
    {
        await using var verify = AgentTaskReplyIntegrationTests.CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Dispatched);
        stored.Result.ShouldBeNull();
        stored.CompletedAt.ShouldBeNull();
        stored.ReportNudgedAt.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == taskId
            && e.Type == AgentTaskEventType.Warning
            && e.Detail != null
            && e.Detail.Contains("closing report line")))
            .ShouldBe(0);
        (await verify.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == sessionId
            && m.Body.Contains("closing report line")))
            .ShouldBe(0);
    }

    private static async Task AssertSettledAsync(Guid taskId, string result)
    {
        await using var verify = AgentTaskReplyIntegrationTests.CreateContext();
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
        stored.Status.ShouldBe(AgentTaskStatus.Succeeded);
        stored.Result.ShouldBe(result);
        stored.ReportNudgedAt.ShouldBeNull();
    }

    private static async Task SeedAsync(Guid sessionId, DateTime at, params TranscriptEntry[] rows)
    {
        await using var db = AgentTaskReplyIntegrationTests.CreateContext();
        foreach (var row in rows)
        {
            row.Timestamp ??= at;
            db.TranscriptEntries.Add(row);
        }

        await db.SaveChangesAsync();
    }

    private static TranscriptEntry Entry(
        Guid sessionId, long sequence, string kind, string text, DateTime at, string? apiCallId = null) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        Timestamp = at,
        ApiCallId = apiCallId,
        CreatedAt = DateTime.UtcNow,
    };

    private static TranscriptEntry Turn(
        Guid sessionId, long sequence, string apiCallId, string stopReason, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = TranscriptKinds.TurnEnd,
        StopReason = stopReason,
        ApiCallId = apiCallId,
        Timestamp = at,
        CreatedAt = DateTime.UtcNow,
    };

    private static TranscriptEntry Compact(Guid sessionId, long sequence, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = TranscriptKinds.CompactBoundary,
        Text = "Context compacted (auto)",
        Timestamp = at,
        CreatedAt = DateTime.UtcNow,
    };

    private static TranscriptEntry Tool(Guid sessionId, long sequence, string apiCallId, DateTime at) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = TranscriptKinds.ToolCall,
        ToolName = "Read",
        ToolUseId = "toolu_" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ApiCallId = apiCallId,
        Timestamp = at,
        CreatedAt = DateTime.UtcNow,
    };
}
