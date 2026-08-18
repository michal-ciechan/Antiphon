using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One retention pass with per-table windows (CARD-0044). Transcript deletion is per-session
/// all-or-nothing: a partial trim would be re-inserted by <c>PersistTranscriptAsync</c>'s
/// uuid-presence dedup, rebased past max — the stuck-"working" shape CARD-0041 exists to prevent.
/// </summary>
public sealed class DataRetentionService
{
    private readonly AppDbContext _db;
    private readonly RetentionSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DataRetentionService> _logger;

    public DataRetentionService(
        AppDbContext db,
        IOptions<RetentionSettings> settings,
        TimeProvider timeProvider,
        ILogger<DataRetentionService> logger)
    {
        _db = db;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs the implemented table passes in the planned order (tasks → sessions → transcripts →
    /// queued messages → audit). Slice 1 owns transcripts and queued messages; later slices fill
    /// the earlier/later slots.
    /// </summary>
    public async Task<DataRetentionSweepResult> RunOnceAsync(CancellationToken ct)
    {
        var transcripts = await PruneTranscriptsAsync(ct);
        var queued = await PruneQueuedMessagesAsync(ct);
        return new DataRetentionSweepResult(transcripts, queued);
    }

    /// <summary>
    /// Deletes a session's entire transcript row set, never a subset. Eligible only when the
    /// session is <see cref="SessionStatus.Stopped"/> or <see cref="SessionStatus.Failed"/>, is
    /// not any agent's <c>PersistentSessionId</c>, and both its newest row and <c>LastSeenAt</c>
    /// are older than <see cref="RetentionSettings.TranscriptRetentionDays"/>.
    /// </summary>
    public async Task<int> PruneTranscriptsAsync(CancellationToken ct)
    {
        if (_settings.TranscriptRetentionDays <= 0)
            return 0;

        var cutoff = UtcNow().AddDays(-_settings.TranscriptRetentionDays);

        // PersistentSessionId is a loose string (no FK). Parse with the same Guid.TryParse
        // semantics AgentSupervisorService.FindPersistentSessionAsync uses so a Failed row that
        // CARD-0056 may re-adopt is never emptied.
        var protectedIds = new HashSet<Guid>();
        var rawIds = await _db.Agents
            .Where(a => a.PersistentSessionId != null)
            .Select(a => a.PersistentSessionId!)
            .ToListAsync(ct);
        foreach (var raw in rawIds)
        {
            if (Guid.TryParse(raw, out var id))
                protectedIds.Add(id);
        }

        var candidates = await _db.AgentSessions
            .Where(s => (s.Status == SessionStatus.Stopped || s.Status == SessionStatus.Failed)
                && s.LastSeenAt < cutoff)
            .Select(s => s.Id)
            .ToListAsync(ct);
        candidates = candidates.Where(id => !protectedIds.Contains(id)).ToList();
        if (candidates.Count == 0)
            return 0;

        var newestBySession = await _db.TranscriptEntries
            .Where(t => candidates.Contains(t.AgentSessionId))
            .GroupBy(t => t.AgentSessionId)
            .Select(g => new { SessionId = g.Key, Newest = g.Max(t => t.CreatedAt) })
            .ToListAsync(ct);

        var eligible = newestBySession
            .Where(x => x.Newest < cutoff)
            .Select(x => x.SessionId)
            .ToList();

        var removed = 0;
        foreach (var sessionId in eligible)
        {
            // Whole session or nothing — the WHERE is AgentSessionId only, never CreatedAt.
            var deleted = await _db.TranscriptEntries
                .Where(t => t.AgentSessionId == sessionId)
                .ExecuteDeleteAsync(ct);
            if (deleted > 0)
            {
                _logger.LogInformation(
                    "Pruned {Count} transcript row(s) for session {SessionId} (whole session, past retention)",
                    deleted, sessionId);
                removed += deleted;
            }
        }

        return removed;
    }

    /// <summary>
    /// Deletes settled queue rows past the window. Never a <c>Pending</c> row (parked messages
    /// stay Pending by design) and never an unsettled Channel-origin correlation (CARD-0067).
    /// Independent of session liveness — this is what bounds a long-lived always-on session's queue.
    /// </summary>
    public async Task<int> PruneQueuedMessagesAsync(CancellationToken ct)
    {
        if (_settings.QueuedMessageRetentionDays <= 0)
            return 0;

        var cutoff = UtcNow().AddDays(-_settings.QueuedMessageRetentionDays);
        var removed = await _db.SessionQueuedMessages
            .Where(m => (m.Status == QueuedMessageStatus.Sent || m.Status == QueuedMessageStatus.Canceled)
                && m.CreatedAt < cutoff
                && (m.Origin != QueuedMessageOrigin.Channel || m.ChannelReplySettledAt != null))
            .ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            _logger.LogInformation(
                "Pruned {Count} settled queued message(s) past retention",
                removed);
        }

        return removed;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}

public sealed record DataRetentionSweepResult(int Transcripts, int QueuedMessages);
