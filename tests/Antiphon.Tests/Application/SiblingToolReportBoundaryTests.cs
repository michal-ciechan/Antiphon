using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1473. A stored ToolCall with the same ApiCallId, inside the same eligible
/// prompt span, proves an <c>end_turn</c> boundary is a sibling phantom. The reply
/// path must not nudge or settle on it. Text-only finals, <c>max_tokens</c>, other
/// response ids, and a newer real prompt stay on the existing path.
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
