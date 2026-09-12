using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

internal static class TurnSeeding
{
    public static async Task SeedTurnAsync(
        Func<AppDbContext> dbFactory,
        Guid sessionId,
        string prompt,
        string? assistantText,
        bool closingVerdict = true)
    {
        assistantText = ApplyClosingVerdict(prompt, assistantText, closingVerdict);
        await using var db = dbFactory();
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        db.TranscriptEntries.Add(NewEntry(sessionId, ++seq, TranscriptKinds.UserPrompt, prompt));
        if (assistantText is not null)
        {
            var entry = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, assistantText);
            entry.Timestamp = DateTime.UtcNow;
            db.TranscriptEntries.Add(entry);
        }
        var end = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        end.StopReason = TranscriptKinds.StopReasons.EndTurn;
        db.TranscriptEntries.Add(end);
        await db.SaveChangesAsync();
    }

    public static string? ApplyClosingVerdict(string prompt, string? assistantText, bool closingVerdict)
    {
        if (!closingVerdict || string.IsNullOrEmpty(assistantText))
            return assistantText;
        if (AgentTaskReplyService.LooksLikeAQuestion(assistantText))
            return assistantText;
        if (assistantText.Contains("[antiphon-report:", StringComparison.Ordinal))
            return assistantText;
        var shortId = DelegationReportFormatter.TryReadTaskMarkerId(prompt);
        if (shortId is null)
            return assistantText;
        return assistantText.TrimEnd() + "\n" + DelegationReportFormatter.ReportToken(shortId, "done");
    }

    private static TranscriptEntry NewEntry(Guid sessionId, long seq, string kind, string? text) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = seq,
        Kind = kind,
        Text = text,
        CreatedAt = DateTime.UtcNow,
    };
}
