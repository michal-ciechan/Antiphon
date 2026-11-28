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
    public const int PendingNotePageMaxBytes = 256 * 1024;
    public const int WithholdReasonMaxLength = 200;
    internal const int ChannelInitialRetrySeconds = 10;
    private const int ChannelMaxRetrySeconds = 120;
    private readonly long _channelRetryEpoch;
    private readonly DateTime _channelRetryEpochUtc;

    private DateTime ChannelRetryNow() => _channelRetryEpochUtc + _timeProvider.GetElapsedTime(_channelRetryEpoch);

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
    /// Record one durable withhold cycle on notes that have never been typed. The first
    /// observation writes the record. A later observation whose reason differs advances
    /// <see cref="SessionQueuedMessage.WithholdCount"/> and
    /// <see cref="SessionQueuedMessage.LastWithheldAt"/> once and keeps
    /// <see cref="SessionQueuedMessage.FirstWithheldAt"/>. The same reason does not write
    /// again. Does not set <see cref="SessionQueuedMessage.DeliveryAttempts"/>,
    /// <see cref="SessionQueuedMessage.LastDeliveryStartedAt"/>, or
    /// <see cref="SessionQueuedMessage.LastDeliveryBaselineSequence"/>. A real dialog, a
    /// remote-control menu, and a missing snapshot do not call this.
    /// </summary>
    private async Task ChargeDurableWithholdAsync(
        AppDbContext db, Guid sessionId, string reason, CancellationToken ct, bool channelOnly = false)
    {
        var now = UtcNow();
        var recorded = reason.Length <= WithholdReasonMaxLength
            ? reason
            : reason[..WithholdReasonMaxLength];
        await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId
                && m.Status == QueuedMessageStatus.Pending
                && (!channelOnly || m.Origin == QueuedMessageOrigin.Channel)
                && m.DeliveryAttempts == 0
                && m.LastDeliveryStartedAt == null
                && m.LastDeliveryBaselineSequence == null
                && m.DeliveryVerdict == null
                && (m.WithholdCount == 0 || m.LastWithholdReason != recorded))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.WithholdCount, m => m.WithholdCount + 1)
                .SetProperty(m => m.FirstWithheldAt, m => m.FirstWithheldAt == null ? now : m.FirstWithheldAt)
                .SetProperty(m => m.LastWithheldAt, now)
                .SetProperty(m => m.LastWithholdReason, recorded), ct);
    }

    private async Task RecordChannelInputRefusalAsync(
        AppDbContext db, Guid sessionId, string? reason, CancellationToken ct)
    {
        if (reason is ConditionalInputOutcomes.ComposerMismatch or ConditionalInputOutcomes.ObservationUnknown)
            await ChargeDurableWithholdAsync(db, sessionId, reason, ct, channelOnly: true);
        await ScheduleChannelRetryAfterWithholdAsync(db, sessionId, ct);
    }

    private Task ScheduleChannelRetryAfterWithholdAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var now = ChannelRetryNow();
        var minuteAgo = now.AddMinutes(-1);
        var fiveMinutesAgo = now.AddMinutes(-5);
        return db.SessionQueuedMessages.Where(m => m.AgentSessionId == sessionId
                && m.Status == QueuedMessageStatus.Pending && m.Origin == QueuedMessageOrigin.Channel
                && m.FirstWithheldAt != null && (m.HoldUntil == null || m.HoldUntil <= now))
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.HoldUntil,
                m => m.FirstWithheldAt <= fiveMinutesAgo ? now.AddSeconds(ChannelMaxRetrySeconds)
                    : m.FirstWithheldAt <= minuteAgo ? now.AddSeconds(30)
                    : now.AddSeconds(ChannelInitialRetrySeconds)), ct);
    }

    // Each conversation has its own ready prefix within the recipient's queue. An exhausted
    // row remains visible for recovery, but its omission never blocks the next automatic row.
    private async Task<List<SessionQueuedMessage>> ApplyChannelAdmissionAsync(
        AppDbContext db, Guid sessionId, List<SessionQueuedMessage> candidates, CancellationToken ct)
    {
        var channelCandidates = candidates.Where(row => row.Origin == QueuedMessageOrigin.Channel)
            .Select(row => row.Id).ToHashSet();
        if (channelCandidates.Count == 0) return candidates;

        var now = ChannelRetryNow();
        var latest = now.AddSeconds(ChannelMaxRetrySeconds);
        var pending = db.SessionQueuedMessages
            .Where(row => row.AgentSessionId == sessionId && row.Origin == QueuedMessageOrigin.Channel
                && row.Status == QueuedMessageStatus.Pending);
        var ordered = await pending.AsNoTracking()
            .OrderBy(row => row.Sequence)
            .Select(row => new
            {
                row.Id, row.ConversationKey, row.DeliveryAttempts,
                Due = row.HoldUntil ?? (DateTime?)row.FirstWithheldAt!.Value.AddSeconds(ChannelInitialRetrySeconds)
            })
            .ToListAsync(ct);
        // A persisted deadline loaded after a backward clock change can exceed the maximum
        // whole delay. Clamp it durably once so subsequent scans cannot move the cap forward.
        if (ordered.Any(row => row.Due > latest))
        {
            var latestFirstHold = latest.AddSeconds(-ChannelInitialRetrySeconds);
            await pending.Where(row => row.HoldUntil > latest
                    || (row.HoldUntil == null && row.FirstWithheldAt > latestFirstHold))
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.HoldUntil, latest), ct);
        }

        var admitted = new HashSet<Guid>();
        var blocked = new HashSet<string?>(StringComparer.Ordinal);
        foreach (var row in ordered)
        {
            var selected = channelCandidates.Contains(row.Id);
            if (!selected && row.DeliveryAttempts >= MaxAttempts) continue;
            if (!selected || row.Due > now)
                blocked.Add(row.ConversationKey);
            else if (!blocked.Contains(row.ConversationKey))
                admitted.Add(row.Id);
        }
        return candidates.Where(row => row.Origin != QueuedMessageOrigin.Channel || admitted.Contains(row.Id)).ToList();
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
                m.NoteHeader, reason, m.WithholdCount, m.FirstWithheldAt, m.LastWithheldAt,
                m.LastWithholdReason))
            .ToListAsync(ct);
        var page = new List<PendingNoteDto>(rows.Count);
        var bytes = 0;
        foreach (var note in rows)
        {
            var size = System.Text.Encoding.UTF8.GetByteCount(note.Body);
            if (page.Count > 0 && bytes + size > PendingNotePageMaxBytes)
                break;
            page.Add(note);
            bytes += size;
        }

        var next = page.Count == 0 ? since : page[^1].Sequence;
        return new PendingNotePageDto(sessionId, since, next, reason is not null, reason, page);
    }
}
