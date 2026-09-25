using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Reduced reconstruction of session df9109a5 sequences 11 and 271-276 (CARD-0714).
/// Not the captured transcript. Identity, chronology and the measured reminder are kept.
/// Sequence 274 is omitted on purpose.
/// </summary>
public static class Card0714Transcript
{
    public const string ReportApiCallId = "b3df71fa-7880-4310-a941-4a80990bd8d0:161";
    public const string AcknowledgementApiCallId = "task-completed-01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7:0";
    public const string BackgroundTaskId = "01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7";

    public const long RulesPromptSequence = 1;
    public const long RulesEndSequence = 10;
    public const long BriefSequence = 11;
    public const long ReportTextSequence = 271;
    public const long ReportEndSequence = 272;
    public const long ReminderSequence = 273;
    public const long AcknowledgementSequence = 275;
    public const long AcknowledgementEndSequence = 276;

    public const string Reminder =
        "<system-reminder>\n"
        + "Background task \"01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7\" completed (exit code: 1).\n"
        + "Description: Green-run the three affected integration classes | Duration: 241.6s\n"
        + "Use get_command_or_subagent_output(\"01a0d92a-4a3e-7c51-9420-b6ce3de4d0e7\") to see the full output.\n"
        + "</system-reminder>";

    public const string Acknowledgement = "Acknowledged. The background command finished.";

    public static string StoredReport(Guid taskId) =>
        ("The three integration classes are green.\n\n"
        + "Checkpoint counts were recorded against the fresh TRX.\n\n"
        + "--- next stage ---\n"
        + "next: review\n"
        + "handoff: attribution selects the report boundary\n"
        + "artifact: docs/session-runtime-invariants.md").ReplaceLineEndings("\n");

    public static string ReportText(Guid taskId) =>
        StoredReport(taskId) + "\n" + DelegationReportFormatter.ReportToken(taskId, "done");

    public static string Brief(Guid taskId)
    {
        var marker = DelegationReportFormatter.TaskMarker(taskId);
        return $"{marker} YOUR BRIEF IS NOT IN THIS MESSAGE. It is in .antiphon/task-{DelegationReportFormatter.Short(taskId)}.md {marker} {marker}";
    }

    public static async Task MarkProviderAsync(Guid sessionId, Guid taskId, AgentKind kind)
    {
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
        session.AgentKind = kind;
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.AgentKind = kind;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Rules prompt/end, the spill-pointer brief, the report and its TurnEnd, then the reminder
    /// and its acknowledgement. Native times keep the measured gaps and sit after
    /// <paramref name="dispatchedAt"/>. <paramref name="createdAt"/> stamps every row when catch-up
    /// should look fresh; otherwise each row's store time is its native time.
    /// </summary>
    public static async Task SeedReplayAsync(
        Guid sessionId,
        Guid taskId,
        DateTime dispatchedAt,
        DateTime? createdAt = null,
        bool includeReportText = true,
        bool includeReportBoundary = true,
        bool includeAcknowledgement = true,
        bool includeReminder = true,
        bool quoteTokensInRules = false,
        DateTime? reportBoundaryCreatedAt = null,
        int extraReminders = 0,
        bool includeBrief = true)
    {
        var anchor = dispatchedAt.AddMinutes(30);
        var rulesAt = anchor.AddMinutes(-20);
        var briefAt = anchor.AddMinutes(-10);
        var reportAt = anchor.AddMilliseconds(-500);
        var reportEndAt = anchor;
        var reminderAt = anchor.AddMilliseconds(28);
        var ackAt = anchor.AddSeconds(13.324);
        var ackEndAt = anchor.AddSeconds(13.624);

        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var rulesId = Guid.NewGuid();
        var rulesBody = GrokRulesRefreshService.Header(rulesId);
        if (quoteTokensInRules)
        {
            rulesBody += "\n" + DelegationReportFormatter.TaskMarker(taskId)
                + "\n" + DelegationReportFormatter.ReportToken(taskId, "done");
        }

        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = rulesId,
            AgentSessionId = sessionId,
            Sequence = 1,
            Origin = QueuedMessageOrigin.System,
            Status = QueuedMessageStatus.Sent,
            CreatedAt = rulesAt,
            SentAt = rulesAt,
            DeliveryAttempts = 1,
            RulesRefreshKey = "launch:" + Guid.NewGuid().ToString("N"),
            Body = rulesBody,
        });

        void Add(long sequence, string kind, string? text, DateTime native, string? apiCallId = null, string? stop = null)
        {
            var stored = sequence == ReportEndSequence && reportBoundaryCreatedAt is DateTime boundaryAt
                ? boundaryAt
                : createdAt ?? native;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Sequence = sequence,
                Kind = kind,
                Uuid = $"c714-{sequence:000}-{Guid.NewGuid():N}",
                Role = kind is TranscriptKinds.UserPrompt or TranscriptKinds.QueuedUserPrompt ? "user" : "assistant",
                Text = text,
                ApiCallId = apiCallId,
                StopReason = stop,
                Timestamp = native,
                CreatedAt = stored,
            });
        }

        Add(RulesPromptSequence, TranscriptKinds.UserPrompt, rulesBody, rulesAt);
        Add(RulesEndSequence, TranscriptKinds.TurnEnd, null, rulesAt.AddSeconds(1), stop: TranscriptKinds.StopReasons.EndTurn);
        if (includeBrief)
            Add(BriefSequence, TranscriptKinds.UserPrompt, Brief(taskId), briefAt);
        if (includeReportText)
            Add(ReportTextSequence, TranscriptKinds.AssistantText, ReportText(taskId), reportAt, ReportApiCallId);
        if (includeReportBoundary)
            Add(ReportEndSequence, TranscriptKinds.TurnEnd, null, reportEndAt, ReportApiCallId, TranscriptKinds.StopReasons.EndTurn);
        if (includeReminder)
            Add(ReminderSequence, TranscriptKinds.UserPrompt, Reminder, reminderAt);
        if (includeAcknowledgement)
        {
            Add(AcknowledgementSequence, TranscriptKinds.AssistantText, Acknowledgement, ackAt, AcknowledgementApiCallId);
            Add(AcknowledgementEndSequence, TranscriptKinds.TurnEnd, null, ackEndAt, AcknowledgementApiCallId, TranscriptKinds.StopReasons.EndTurn);
        }

        for (var i = 0; i < extraReminders; i++)
        {
            var id = Guid.NewGuid().ToString("D");
            var text = Reminder.Replace(BackgroundTaskId, id, StringComparison.Ordinal);
            var at = ackEndAt.AddSeconds(2 + i);
            var baseSeq = 280 + (i * 4);
            Add(baseSeq, TranscriptKinds.UserPrompt, text, at);
            Add(baseSeq + 1, TranscriptKinds.AssistantText, Acknowledgement, at.AddMilliseconds(10), "task-completed-" + id + ":0");
            Add(baseSeq + 2, TranscriptKinds.TurnEnd, null, at.AddMilliseconds(20), "task-completed-" + id + ":0", TranscriptKinds.StopReasons.EndTurn);
        }

        await db.SaveChangesAsync();
    }
}
