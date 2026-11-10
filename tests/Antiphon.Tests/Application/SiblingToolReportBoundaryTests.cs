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

    private const string ContinuationText =
        TranscriptKinds.CompactionContinuationPromptPrefix
        + " that ran out of context. The summary below covers…";

    private const string NotificationText =
        "<task-notification>\n<task-id>a548067d72b9d6de9</task-id>\n"
        + "<tool-use-id>toolu_c1821</tool-use-id>\n<status>completed</status>\n"
        + "<result>The subagent's own report.</result>\n</task-notification>";

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
