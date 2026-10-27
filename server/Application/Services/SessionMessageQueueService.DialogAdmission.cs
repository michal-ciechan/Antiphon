using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
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
        var idle = await TranscriptIdleQuery.IsConfirmedIdleAsync(db, sessionId, ct);
        return DialogDeliveryAdmission.Decide(observation, sessionId, generation, idle, session.SessionBackend);
    }

    /// <summary>
    /// One floor for a durable withhold. Real dialogs, a remote-control menu, and a missing
    /// snapshot stay at zero attempts. Rows already attempted are left alone.
    /// </summary>
    private async Task ChargeDurableWithholdAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var now = UtcNow();
        var baseline = await CaptureTranscriptBaselineAsync(db, sessionId, ct);
        long? floor = baseline.Observable ? baseline.MaxSequence : null;
        await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId
                && m.Status == QueuedMessageStatus.Pending
                && m.DeliveryAttempts == 0)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.DeliveryAttempts, 1)
                .SetProperty(m => m.LastDeliveryStartedAt, now)
                .SetProperty(m => m.LastDeliveryBaselineSequence, floor), ct);
    }
}
