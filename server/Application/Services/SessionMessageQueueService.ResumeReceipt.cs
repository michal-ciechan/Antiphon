using System.Globalization;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

internal static class StandingResumeNote
{
    internal const string KeyPrefix = "standing-resume-note:";
    internal static string Key(DateTime generation) => KeyPrefix
        + SessionGeneration.Normalize(generation).Ticks.ToString(CultureInfo.InvariantCulture);
    internal static bool IsNote(SessionQueuedMessage row) => row.Origin == QueuedMessageOrigin.System
        && row.RuntimeActionKey?.StartsWith(KeyPrefix, StringComparison.Ordinal) == true;
}

public sealed partial class SessionMessageQueueService
{
    private sealed record ResumeReceiptFence(SessionQueuedMessage? Note, bool Ready, bool Confirmed = false);

    // Callers own the queue semaphore, never a state-store lease. ObserveReceiptReadinessAsync
    // owns its bounded pull/persist cycle and verifies the actual standing owner/current pointer.
    private async Task<ResumeReceiptFence> ObserveResumeReceiptFenceAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not { AgentKind: AgentKind.ClaudeCode, StandingAgentId: { } owner })
            return new(null, true);
        var key = StandingResumeNote.Key(session.StartedAt);
        var note = await db.SessionQueuedMessages.FirstOrDefaultAsync(m => m.AgentSessionId == sessionId
            && m.RuntimeActionKey == key && m.Origin == QueuedMessageOrigin.System
            && (m.Status == QueuedMessageStatus.Pending || m.Status == QueuedMessageStatus.Sent)
            && m.DeliveryVerdict != DeliveryVerdict.Delivered && m.DeliveryVerdict != DeliveryVerdict.LateConfirmed, ct);
        if (note is null) return new(null, true);

        var observation = await _runtime.ObserveReceiptReadinessAsync(sessionId, session.StartedAt, owner, ct);
        // Positive evidence can arrive even in a Behind snapshot. Reconcile it before Working,
        // selecting another row, or recovering an interrupted claim, with the original floor.
        if (note.DeliveryAttempts > 0)
            await LateConfirmAttemptedMessagesAsync(db, sessionId, [note], session.AgentKind, ct);
        if (note.DeliveryVerdict is DeliveryVerdict.Delivered or DeliveryVerdict.LateConfirmed)
            return new(note, true, true);
        return new(note, observation.Status == ReceiptReadinessStatus.Ready);
    }

    private async Task<bool> CanWriteResumeInputAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var fence = await ObserveResumeReceiptFenceAsync(db, sessionId, ct);
        if (!fence.Ready) return false;
        if (fence.Confirmed) return true;
        // An uncertain submit is still the original attempt. Once evidence catches up, decide
        // that attempt through the ordinary grace/failure policy before considering another write.
        if (fence.Note is { DeliveryAttempts: > 0, DeliveryVerdictAt: null } note
            && note.DeliveryVerdict is null or DeliveryVerdict.NoTranscriptRecord or DeliveryVerdict.NoSubmitOutput)
        {
            await HandleDeliveryFailureAsync(sessionId, [note.Id],
                note.DeliveryVerdict ?? DeliveryVerdict.NoTranscriptRecord, ct, note.LastDeliveryGeneration);
            await db.Entry(note).ReloadAsync(ct);
            return false;
        }
        return true;
    }

    private async Task<bool> CanWriteResumeInputAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return await CanWriteResumeInputAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), sessionId, ct);
    }

    private async Task<bool> HasResumeReceiptObligationAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not { AgentKind: AgentKind.ClaudeCode, StandingAgentId: not null }) return false;
        var key = StandingResumeNote.Key(session.StartedAt);
        return await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == sessionId
            && m.RuntimeActionKey == key && m.Origin == QueuedMessageOrigin.System
            && (m.Status == QueuedMessageStatus.Pending || m.Status == QueuedMessageStatus.Sent)
            && m.DeliveryVerdict != DeliveryVerdict.Delivered && m.DeliveryVerdict != DeliveryVerdict.LateConfirmed, ct);
    }

    // The row carries the observed failure, but no adjudication timestamp: absence cannot yet
    // reach incident/refund/restart policy. Preserve every original attempt coordinate.
    private async Task<bool> DeferResumeAbsenceAsync(Guid sessionId, IReadOnlyList<Guid>? ids,
        DeliveryVerdict verdict, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fence = await ObserveResumeReceiptFenceAsync(db, sessionId, ct);
        if (fence.Note is null) return false;
        if (fence.Confirmed && ids is { Count: > 0 } && ids.All(id => id == fence.Note.Id)) return true;
        if (fence.Ready) return false;
        if (ids is { Count: > 0 })
        {
            var rows = await db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId && ids.Contains(m.Id))
                .ToListAsync(ct);
            foreach (var row in rows.Where(m => m.DeliveryAttempts > 0
                         && m.DeliveryVerdict is not (DeliveryVerdict.Delivered or DeliveryVerdict.LateConfirmed)))
            {
                row.Status = QueuedMessageStatus.Pending;
                row.SentAt = null;
                row.DeliveryVerdict = verdict;
                row.DeliveryVerdictAt = null;
            }
            await db.SaveChangesAsync(ct);
        }
        return true;
    }
}
