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
    private readonly AuditSettings _auditSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DataRetentionService> _logger;
    private readonly AuditService _auditService;

    public DataRetentionService(
        AppDbContext db,
        IOptions<RetentionSettings> settings,
        IOptions<AuditSettings> auditSettings,
        TimeProvider timeProvider,
        ILogger<DataRetentionService> logger,
        AuditService auditService)
    {
        _db = db;
        _settings = settings.Value;
        _auditSettings = auditSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _auditService = auditService;
    }

    /// <summary>
    /// Runs the implemented table passes: sessions → transcripts → queued messages → tasks → audit.
    /// Slice 2 owns sessions; slice 1 owns transcripts and queued messages; slice 3 owns tasks;
    /// slice 4 archives audit FullContent past <see cref="AuditSettings.RetentionDays"/>.
    /// Tasks run last among the deletes so the session pass still sees surviving task-session
    /// references (the windows self-sequence: a session outlives its tasks, then becomes eligible
    /// next sweep). There is no FK either way between AgentTask and AgentSession, so this is not a
    /// constraint hazard — only an eligibility delay of one sweep.
    /// </summary>
    public async Task<DataRetentionSweepResult> RunOnceAsync(CancellationToken ct)
    {
        var sessions = await PruneSessionsAsync(ct);
        var transcripts = await PruneTranscriptsAsync(ct);
        var queued = await PruneQueuedMessagesAsync(ct);
        var tasks = await PruneTasksAsync(ct);
        var usage = await PruneSubscriptionUsageSamplesAsync(ct);
        var auditRecords = 0;
        if (_auditSettings.RetentionDays > 0)
        {
            var archive = await _auditService.ArchiveFullContentAsync(_auditSettings.RetentionDays, ct);
            auditRecords = archive.ArchivedCount;
        }

        return new DataRetentionSweepResult(transcripts, queued, sessions, tasks, auditRecords, usage);
    }

    /// <summary>
    /// Deletes a terminal AgentSession row past
    /// <see cref="RetentionSettings.SessionRetentionDays"/> when nothing still names it: not any
    /// agent's <c>PersistentSessionId</c>, and no surviving AgentTask via
    /// <c>AgentSessionId</c> or <c>ParentSessionId</c>. Those two loose Guids have no FK, so the
    /// exclusion is what keeps the 90d/180d windows self-sequencing — a session outlives its
    /// tasks automatically. <c>ExecuteDeleteAsync</c> on the session row is enough: Postgres
    /// cascades <c>TranscriptEntries</c> and <c>SessionQueuedMessages</c>, and nulls
    /// <c>RunAttempts.AgentSessionId</c> / <c>Cards.OwnerSessionId</c>.
    /// </summary>
    public async Task<int> PruneSessionsAsync(CancellationToken ct)
    {
        if (_settings.SessionRetentionDays <= 0)
            return 0;

        var cutoff = UtcNow().AddDays(-_settings.SessionRetentionDays);
        var protectedIds = await LoadPersistentSessionIdsAsync(ct);

        // Terminal + stale LastSeenAt + not a PersistentSessionId + no surviving task
        // names this row via AgentSessionId OR ParentSessionId.
        var query = _db.AgentSessions.Where(s =>
            (s.Status == SessionStatus.Stopped || s.Status == SessionStatus.Failed)
            && s.LastSeenAt < cutoff
            && !_db.AgentTasks.Any(t => t.AgentSessionId == s.Id || t.ParentSessionId == s.Id));

        if (protectedIds.Count > 0)
        {
            var protectedList = protectedIds.ToList();
            query = query.Where(s => !protectedList.Contains(s.Id));
        }

        var removed = await query.ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            _logger.LogInformation(
                "Pruned {Count} session row(s) past retention",
                removed);
        }

        return removed;
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

        var protectedIds = await LoadPersistentSessionIdsAsync(ct);

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

    /// <summary>
    /// Deletes a whole AgentTask tree past <see cref="RetentionSettings.TaskRetentionDays"/>
    /// when every row sharing that <c>RootTaskId</c> is terminal
    /// (<see cref="AgentTaskStatus.Succeeded"/> / <see cref="AgentTaskStatus.Failed"/> /
    /// <see cref="AgentTaskStatus.Canceled"/>) and the tree's newest
    /// <c>COALESCE(CompletedAt, CreatedAt)</c> is older than the cutoff. Never a partial tree:
    /// <c>ParentTaskId</c> is Restrict on purpose, so deletes go children-first (Depth
    /// descending) inside one transaction. <c>AgentTaskEvents</c> cascade.
    /// </summary>
    public async Task<int> PruneTasksAsync(CancellationToken ct)
    {
        if (_settings.TaskRetentionDays <= 0)
            return 0;

        var cutoff = UtcNow().AddDays(-_settings.TaskRetentionDays);

        // A root is ineligible if ANY row in its tree is still live (Queued/Dispatched/Working/Blocked).
        var liveRootIds = _db.AgentTasks
            .Where(t => t.Status != AgentTaskStatus.Succeeded
                && t.Status != AgentTaskStatus.Failed
                && t.Status != AgentTaskStatus.Canceled)
            .Select(t => t.RootTaskId);

        var eligibleRootIds = await _db.AgentTasks
            .Where(t => !liveRootIds.Contains(t.RootTaskId))
            .GroupBy(t => t.RootTaskId)
            .Where(g => g.Max(t => t.CompletedAt ?? t.CreatedAt) < cutoff)
            .Select(g => g.Key)
            .ToListAsync(ct);

        if (eligibleRootIds.Count == 0)
            return 0;

        // Children-first so Restrict on ParentTaskId never fires. One transaction so a
        // mid-loop failure cannot leave a half-deleted tree.
        var depths = await _db.AgentTasks
            .Where(t => eligibleRootIds.Contains(t.RootTaskId))
            .Select(t => t.Depth)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var removed = 0;
        foreach (var depth in depths)
        {
            removed += await _db.AgentTasks
                .Where(t => eligibleRootIds.Contains(t.RootTaskId) && t.Depth == depth)
                .ExecuteDeleteAsync(ct);
        }

        await tx.CommitAsync(ct);

        if (removed > 0)
        {
            _logger.LogInformation(
                "Pruned {Count} task row(s) in {Trees} tree(s) past retention",
                removed, eligibleRootIds.Count);
        }

        return removed;
    }

    /// <summary>
    /// Deletes subscription-usage samples older than
    /// <see cref="RetentionSettings.SubscriptionUsageRetentionDays"/>. Independent of session
    /// liveness — the quota belongs to a subscription. <c>&lt;= 0</c> disables the pass.
    /// </summary>
    public async Task<int> PruneSubscriptionUsageSamplesAsync(CancellationToken ct)
    {
        if (_settings.SubscriptionUsageRetentionDays <= 0)
            return 0;

        var cutoff = UtcNow().AddDays(-_settings.SubscriptionUsageRetentionDays);
        var removed = await _db.SubscriptionUsageSamples
            .Where(s => s.ObservedAt < cutoff)
            .ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            _logger.LogInformation(
                "Pruned {Count} subscription-usage sample(s) past retention",
                removed);
        }

        return removed;
    }

    /// <summary>
    /// PersistentSessionId is a loose string (no FK). Parse with the same Guid.TryParse
    /// semantics AgentSupervisorService.FindPersistentSessionAsync uses so a Failed row that
    /// CARD-0056 may re-adopt is never emptied or deleted.
    /// </summary>
    private async Task<HashSet<Guid>> LoadPersistentSessionIdsAsync(CancellationToken ct)
    {
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

        return protectedIds;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}

public sealed record DataRetentionSweepResult(
    int Transcripts, int QueuedMessages, int Sessions, int Tasks, int AuditRecords,
    int SubscriptionUsageSamples = 0);
