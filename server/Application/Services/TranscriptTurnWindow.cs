using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0233: the owning prompt of a turn, and the next prompt that may cap its extraction window.
/// Shared by <see cref="ChannelReplyDispatcher"/> and <see cref="ReviewReplyDispatcher"/> so the
/// kind set cannot drift (CARD-0154).
/// </summary>
internal static class TranscriptTurnWindow
{
    /// <summary>
    /// Owning prompt of the turn that ended at <paramref name="turnEndSeq"/>: latest
    /// <c>UserPrompt</c> in <c>(prevTurnEnd, turnEndSeq)</c>, else latest <c>QueuedUserPrompt</c>
    /// in that window. A mid-turn queued body (launch note, completion note) must not steal
    /// identity from the UserPrompt that opened the turn.
    /// </summary>
    public static async Task<TranscriptEntry?> FindOwningPromptAsync(
        AppDbContext db, Guid sessionId, long turnEndSeq, CancellationToken ct)
    {
        var prevTurnEndSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.TurnEnd
                && t.Sequence < turnEndSeq)
            .MaxAsync(t => (long?)t.Sequence, ct) ?? 0;

        var window = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt)
                && t.Sequence > prevTurnEndSeq
                && t.Sequence < turnEndSeq)
            .Select(t => new { t.Id, t.Sequence, t.Kind })
            .ToListAsync(ct);

        var owningId = window
            .Where(t => t.Kind == TranscriptKinds.UserPrompt)
            .OrderByDescending(t => t.Sequence)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefault()
            ?? window
                .Where(t => t.Kind == TranscriptKinds.QueuedUserPrompt)
                .OrderByDescending(t => t.Sequence)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefault();

        if (owningId is null)
            return null;

        return await db.TranscriptEntries.FirstAsync(t => t.Id == owningId, ct);
    }

    /// <summary>
    /// Sequence of the next turn-opening prompt after <paramref name="promptSeq"/>, or null if
    /// none. A <c>UserPrompt</c> always opens a turn. A <c>QueuedUserPrompt</c> opens one only
    /// when a <c>TurnEnd</c> sits between this prompt and it — an in-turn queued body (the
    /// CARD-0233 bootstrap) must not cap the extraction window.
    /// </summary>
    public static async Task<long?> FindNextTurnOpeningPromptSeqAsync(
        AppDbContext db, Guid sessionId, long promptSeq, CancellationToken ct)
    {
        var after = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Sequence > promptSeq
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt
                    || t.Kind == TranscriptKinds.TurnEnd))
            .Select(t => new { t.Sequence, t.Kind })
            .ToListAsync(ct);

        var sawTurnEnd = false;
        foreach (var row in after.OrderBy(r => r.Sequence))
        {
            if (row.Kind == TranscriptKinds.TurnEnd)
            {
                sawTurnEnd = true;
                continue;
            }

            if (row.Kind == TranscriptKinds.UserPrompt
                || (row.Kind == TranscriptKinds.QueuedUserPrompt && sawTurnEnd))
                return row.Sequence;
        }

        return null;
    }
}
