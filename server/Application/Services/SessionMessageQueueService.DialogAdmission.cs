using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
    public const int PendingNotePageDefault = 50;
    public const int PendingNotePageMax = 100;

    private async Task<DialogWithhold?> ReadDialogWithholdAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var session = await db.AgentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return null;
        var generation = SessionGeneration.Normalize(session.StartedAt);
        if (await db.RemoteControlModalEpisodes.AsNoTracking().AnyAsync(
            e => e.SessionId == sessionId
                && e.AcceptedStartedAt == generation
                && e.ResolvedAt == null, ct))
            return new DialogWithhold("Remote Control menu blocks input", ConditionalInputOutcomes.ModalBlocked, false);
        if (session.AgentKind != AgentKind.ClaudeCode) return null;
        var observation = await _runtime.ObserveDialogAsync(sessionId, ct);
        return DialogDeliveryAdmission.Describe(observation, sessionId, generation);
    }

    /// <summary>
    /// One attempt for a durable observation withhold. Rows already attempted are left alone.
    /// A real dialog, a remote-control menu, and a missing snapshot do not call this.
    /// When a transcript floor exists, the start stamp is one microsecond before the accepted
    /// generation so later recovery does not treat the untyped row as standing in the composer.
    /// </summary>
    private async Task ChargeDurableWithholdAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var any = await db.SessionQueuedMessages.AnyAsync(m =>
            m.AgentSessionId == sessionId
            && m.Status == QueuedMessageStatus.Pending
            && m.DeliveryAttempts == 0, ct);
        if (!any) return;
        var baseline = await CaptureTranscriptBaselineAsync(db, sessionId, ct);
        var generation = SessionGeneration.Normalize(await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId).Select(s => s.StartedAt).SingleAsync(ct));
        var started = baseline.Observable
            ? generation.AddTicks(-SessionGeneration.MicrosecondTicks)
            : UtcNow();
        long? floor = baseline.Observable ? baseline.MaxSequence : null;
        await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId
                && m.Status == QueuedMessageStatus.Pending
                && m.DeliveryAttempts == 0)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.DeliveryAttempts, 1)
                .SetProperty(m => m.LastDeliveryStartedAt, started)
                .SetProperty(m => m.LastDeliveryBaselineSequence, floor), ct);
    }

    /// <summary>
    /// Pending notes for one destination, in sequence order, after <paramref name="since"/>.
    /// Reads only: no flush, no attempt charge, no terminal write.
    /// </summary>
    public async Task<PendingNotePageDto> ListPendingNotesAsync(
        Guid sessionId, long since, int limit, CancellationToken ct)
    {
        if (since < 0) since = 0;
        limit = Math.Clamp(limit <= 0 ? PendingNotePageDefault : limit, 1, PendingNotePageMax);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.AgentSessions.AsNoTracking().AnyAsync(s => s.Id == sessionId, ct))
            throw new NotFoundException(nameof(AgentSession), sessionId);
        var reason = await ModalBlockReasonAsync(db, sessionId, ct);
        var rows = await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId
                && m.Status == QueuedMessageStatus.Pending
                && m.Sequence > since)
            .OrderBy(m => m.Sequence)
            .Take(limit)
            .Select(m => new PendingNoteDto(
                m.Id, m.Sequence, m.Body, m.CreatedAt, m.DeliveryAttempts, m.Origin.ToString(),
                m.NoteHeader, reason))
            .ToListAsync(ct);
        var next = rows.Count == 0 ? since : rows[^1].Sequence;
        return new PendingNotePageDto(sessionId, since, next, reason is not null, reason, rows);
    }
}
