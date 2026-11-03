using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
    private readonly SessionStateStore? _states;
    private readonly DeliveryVerificationSettings _verification;
    private readonly DataRetentionTaskCursor _taskCursor;

    public DataRetentionService(
        AppDbContext db,
        IOptions<RetentionSettings> settings,
        IOptions<AuditSettings> auditSettings,
        TimeProvider timeProvider,
        ILogger<DataRetentionService> logger,
        AuditService auditService,
        SessionStateStore? states = null,
        IOptions<DeliveryVerificationSettings>? verification = null,
        DataRetentionTaskCursor? taskCursor = null)
    {
        _taskCursor = taskCursor ?? new DataRetentionTaskCursor();
        _states = states;
        _db = db;
        _settings = settings.Value;
        _auditSettings = auditSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _auditService = auditService;
        _verification = verification?.Value ?? new DeliveryVerificationSettings();
    }

    /// <summary>
    /// Runs the implemented table passes: sessions → transcripts → queued messages → tasks → audit.
    /// Slice 2 owns sessions; slice 1 owns transcripts and queued messages; slice 3 owns tasks;
    /// slice 4 archives audit FullContent past <see cref="AuditSettings.RetentionDays"/>.
    /// Tasks run last among the deletes so the session pass still sees surviving task-session
    /// references (the windows self-sequence: a session outlives its tasks, then becomes eligible
    /// next sweep). There is no FK either way between AgentTask and AgentSession, so this is not a
    /// constraint hazard — only an eligibility delay of one sweep.
    /// CARD-1654: each pass is independent. A failing pass is logged by name and exception type
    /// and recorded in <see cref="DataRetentionSweepResult.Passes"/>; the later passes still run.
    /// </summary>
    public async Task<DataRetentionSweepResult> RunOnceAsync(CancellationToken ct)
    {
        var passes = new List<DataRetentionPassOutcome>();
        await RunPassAsync(passes, "CheckCompactionRecoveries", true,
            () => PruneCheckCompactionRecoveriesAsync(ct), ct);
        await RunPassAsync(passes, "ConfirmedPublicationIdentities", true, async () =>
        {
            await RetainConfirmedPublicationIdentitiesAsync(ct);
            return 0;
        }, ct);
        await RunPassAsync(passes, "ChannelInboundPayloads", _settings.ChannelInboundPayloadGraceHours > 0,
            () => PruneChannelInboundPayloadsAsync(ct), ct);
        var sessions = await RunPassAsync(passes, "Sessions", _settings.SessionRetentionDays > 0,
            () => PruneSessionsAsync(ct), ct);
        var transcripts = await RunPassAsync(passes, "Transcripts", _settings.TranscriptRetentionDays > 0,
            () => PruneTranscriptsAsync(ct), ct);
        var queued = await RunPassAsync(passes, "QueuedMessages", _settings.QueuedMessageRetentionDays > 0,
            () => PruneQueuedMessagesAsync(ct), ct);
        await RunPassAsync(passes, "ResolvedModalEpisodes", _settings.QueuedMessageRetentionDays > 0,
            () => PruneResolvedModalEpisodesAsync(ct), ct);
        var taskTally = new TaskPruneTally();
        await RunPassAsync(passes, "Tasks", _settings.TaskRetentionDays > 0,
            () => PruneTaskBatchesAsync(taskTally, ct), ct);
        // CARD-1658: the passes after Tasks run as bounded batches and report their progress.
        var usageTally = new RowBatchTally();
        await RunPassAsync(passes, "SubscriptionUsageSamples", _settings.SubscriptionUsageRetentionDays > 0,
            () => PruneSubscriptionUsageSamplesAsync(usageTally, ct), ct, usageTally);
        var residueTally = new RowBatchTally();
        await RunPassAsync(passes, "WorktreeResidueHeldRows",
            _settings.WorktreeResidueHeldRowRetentionDays > 0, () => PruneWorktreeResidueHeldRowsAsync(residueTally, ct), ct,
            residueTally);
        var auditTally = new RowBatchTally();
        await RunPassAsync(passes, "AuditArchive", _auditSettings.RetentionDays > 0,
            () => ArchiveAuditFullContentAsync(auditTally, ct), ct, auditTally);

        // A failed Tasks or row pass still reports the batches it committed before the failure.
        var taskPrune = _settings.TaskRetentionDays > 0 ? taskTally.Snapshot(_taskCursor.After) : null;
        return new DataRetentionSweepResult(transcripts, queued, sessions, taskTally.Rows, auditTally.Rows, usageTally.Rows,
            residueTally.Rows, passes, taskPrune);
    }

    /// <summary>
    /// CARD-1654. Runs one pass and contains its failure. Shutdown cancellation still propagates.
    /// The log names the pass and the exception type chain only: an exception message can carry
    /// row data (a constraint detail, a parameter value), so it is never logged here.
    /// CARD-1658: a batched row pass also records its progress, including a failed pass's
    /// committed batches.
    /// </summary>
    private async Task<int> RunPassAsync(
        List<DataRetentionPassOutcome> passes, string name, bool enabled, Func<Task<int>> pass, CancellationToken ct,
        RowBatchTally? tally = null)
    {
        if (!enabled)
        {
            passes.Add(new DataRetentionPassOutcome(name, DataRetentionPassStatus.Skipped));
            return 0;
        }

        try
        {
            var count = await pass();
            passes.Add(new DataRetentionPassOutcome(name, DataRetentionPassStatus.Completed, null, tally?.Snapshot()));
            return count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A failed SaveChanges must not leave tracked edits for a later pass to resubmit.
            _db.ChangeTracker.Clear();
            var types = ExceptionTypeChain(ex);
            _logger.LogWarning("Data retention sweep pass {Pass} failed: {ExceptionType}", name, types);
            passes.Add(new DataRetentionPassOutcome(name, DataRetentionPassStatus.Failed, types, tally?.Snapshot()));
            return 0;
        }
    }

    private static string ExceptionTypeChain(Exception ex)
    {
        var names = new List<string>();
        for (Exception? e = ex; e is not null && names.Count < 4; e = e.InnerException)
            names.Add(e.GetType().FullName ?? e.GetType().Name);
        return string.Join(" > ", names);
    }

    /// <summary>
    /// Clear only the serialized journal copy after a complete submitted recipient prompt has
    /// survived its own grace window. Discovery reads identity metadata, never attachment bytes.
    /// The identity row remains the broker replay tombstone.
    /// </summary>
    public async Task<int> PruneChannelInboundPayloadsAsync(CancellationToken ct)
    {
        if (_settings.ChannelInboundPayloadGraceHours <= 0)
            return 0;

        const int pageSize = 64;
        var cutoff = UtcNow().AddHours(-_settings.ChannelInboundPayloadGraceHours);
        var tolerance = TimeSpan.FromSeconds(
            Math.Max(0, _verification.UnobservableBaselineConfirmClockToleranceSeconds));
        long cursor = 0;
        var purged = 0;
        var missingOwner = 0;
        var missingPrompt = 0;
        while (true)
        {
            var page = await _db.ChannelInbounds.AsNoTracking()
                .Where(i => i.EnvelopeJson != null && i.AcceptanceSequence > cursor)
                .OrderBy(i => i.AcceptanceSequence)
                .Select(i => new { i.Id, i.QueueMessageId, i.AcceptanceSequence })
                .Take(pageSize).ToListAsync(ct);
            if (page.Count == 0) break;
            cursor = page[^1].AcceptanceSequence;
            foreach (var candidate in page)
            {
                if (candidate.QueueMessageId is not Guid ownerId) continue;
                var owner = await _db.SessionQueuedMessages.AsNoTracking()
                    .SingleOrDefaultAsync(q => q.Id == ownerId, ct);
                if (owner is null) { missingOwner++; continue; }
                if (owner.Origin != QueuedMessageOrigin.Channel
                    || owner.Status != QueuedMessageStatus.Sent
                    || owner.DeliveryAttempts <= 0)
                    continue;

                // Sequence is the original attempt floor when present. Otherwise require the
                // native timestamp relative to the original write (or legacy SentAt fallback).
                long sequence = owner.LastDeliveryBaselineSequence ?? 0;
                var confirmed = false;
                var hasCompleteReceipt = false;
                while (true)
                {
                    var prompts = await _db.TranscriptEntries.AsNoTracking()
                        .Where(t => t.AgentSessionId == owner.AgentSessionId
                            && t.Kind == TranscriptKinds.UserPrompt
                            && t.Sequence > sequence)
                        .OrderBy(t => t.Sequence).Take(pageSize).ToListAsync(ct);
                    if (prompts.Count == 0) break;
                    foreach (var prompt in prompts)
                    {
                        if (ChannelPromptCorrelation.Matches(owner, prompt, tolerance, out _))
                        {
                            hasCompleteReceipt = true;
                            if (prompt.CreatedAt <= cutoff)
                            {
                                confirmed = true;
                                break;
                            }
                        }
                    }
                    if (confirmed) break;
                    sequence = prompts[^1].Sequence;
                }
                if (!confirmed)
                {
                    if (!hasCompleteReceipt)
                        missingPrompt++;
                    continue;
                }
                purged += await _db.ChannelInbounds
                    .Where(i => i.Id == candidate.Id && i.EnvelopeJson != null
                        && i.QueueMessageId == ownerId
                        && _db.SessionQueuedMessages.Any(q => q.Id == ownerId
                            && q.Origin == QueuedMessageOrigin.Channel
                            && q.Status == QueuedMessageStatus.Sent))
                    .ExecuteUpdateAsync(u => u.SetProperty(i => i.EnvelopeJson, (string?)null), ct);
            }
        }
        if (purged > 0 || missingOwner > 0 || missingPrompt > 0)
            _logger.LogInformation(
                "Channel inbound payload retention cleared {Purged} envelope(s); {MissingOwner} mapped envelope(s) lacked an owner and {MissingPrompt} lacked a complete recipient UserPrompt",
                purged, missingOwner, missingPrompt);
        return purged;
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
        var unresolvedRecovery = CheckCompactionRecoveryStates.Unresolved;
        var outboundSessions = ChannelOutboundEvidence.ProtectedSessions(_db);
        var supervision = TaskSupervisionNoticeService.Unresolved(_db);

        // Terminal + stale LastSeenAt + not a PersistentSessionId + no surviving task
        // names this row via AgentSessionId OR ParentSessionId.
        var query = _db.AgentSessions.Where(s =>
            (s.Status == SessionStatus.Stopped || s.Status == SessionStatus.Failed)
            && s.LastSeenAt < cutoff
            && !outboundSessions.Contains(s.Id)
            && !supervision.Any(n => n.SessionId == s.Id || n.RecipientSessionId == s.Id)
            && !_db.SessionRuntimeActions.Any(a => a.AgentSessionId == s.Id)
            && !_db.CheckCompactionRecoveries.Any(r => unresolvedRecovery.Contains(r.State)
                && (r.SessionId == s.Id || r.ResumeSessionId == s.Id))
            && !_db.LegacyCheckNotePublications.Any(p =>
                p.InterpreterSessionId == s.Id
                && (p.State == LegacyCheckNoteState.Captured
                    || (p.State == LegacyCheckNoteState.Produced
                        && !_db.AgentTaskLandNotifications.Any(n =>
                            n.Id == p.NotificationId && n.ConfirmedAt != null))))
            && !_db.AgentTaskLandNotifications.Any(n => n.ParentSessionId == s.Id && n.ConfirmedAt == null
                && n.State != LandNotificationState.NotRequired)
            && !_db.AgentTaskDispatchWarningIntents.Any(i => i.ParentSessionId == s.Id
                && i.MaterializedAt == null && i.InitialState != LandNotificationState.NotRequired)
            && !_db.AgentTasks.Any(t => t.AgentSessionId == s.Id || t.ParentSessionId == s.Id)
            && !_db.SessionQueuedMessages.Any(m => m.AgentSessionId == s.Id && m.DeferredFromRunAttemptId != null)
            && !_db.ChannelInbounds.Any(i => i.EnvelopeJson != null && i.QueueMessageId != null
                && _db.SessionQueuedMessages.Any(m => m.Id == i.QueueMessageId && m.AgentSessionId == s.Id))
            && !_db.RemoteControlModalEpisodes.Any(e => e.SessionId == s.Id && e.ResolvedAt == null));

        if (protectedIds.Count > 0)
        {
            var protectedList = protectedIds.ToList();
            query = query.Where(s => !protectedList.Contains(s.Id));
        }

        var removed = 0;
        foreach (var sessionId in await query.Select(s => s.Id).ToListAsync(ct))
            removed += await MutateTranscriptAsync(sessionId,
                () => query.Where(s => s.Id == sessionId).ExecuteDeleteAsync(ct), ct);
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
        var unresolvedRecovery = CheckCompactionRecoveryStates.Unresolved;

        var protectedIds = await LoadPersistentSessionIdsAsync(ct);
        var outboundSessions = ChannelOutboundEvidence.ProtectedSessions(_db);
        var supervision = TaskSupervisionNoticeService.Unresolved(_db);

        var candidates = await _db.AgentSessions
            .Where(s => (s.Status == SessionStatus.Stopped || s.Status == SessionStatus.Failed)
                && s.LastSeenAt < cutoff
                && !outboundSessions.Contains(s.Id)
                && !supervision.Any(n => n.SessionId == s.Id || n.RecipientSessionId == s.Id)
                && !_db.SessionRuntimeActions.Any(a => a.AgentSessionId == s.Id)
                && !_db.CheckCompactionRecoveries.Any(r => unresolvedRecovery.Contains(r.State)
                    && (r.SessionId == s.Id || r.ResumeSessionId == s.Id))
                && !_db.LegacyCheckNotePublications.Any(p =>
                    p.InterpreterSessionId == s.Id
                    && (p.State == LegacyCheckNoteState.Captured
                        || (p.State == LegacyCheckNoteState.Produced
                            && !_db.AgentTaskLandNotifications.Any(n =>
                                n.Id == p.NotificationId && n.ConfirmedAt != null))))
                && !_db.AgentTaskLandNotifications.Any(n => n.ParentSessionId == s.Id && n.ConfirmedAt == null
                    && n.State != LandNotificationState.NotRequired)
                && !_db.AgentTaskDispatchWarningIntents.Any(i => i.ParentSessionId == s.Id
                    && i.MaterializedAt == null && i.InitialState != LandNotificationState.NotRequired)
                && !_db.ChannelInbounds.Any(i => i.EnvelopeJson != null && i.QueueMessageId != null
                    && _db.SessionQueuedMessages.Any(m => m.Id == i.QueueMessageId && m.AgentSessionId == s.Id)))
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
            var deleted = await MutateTranscriptAsync(sessionId, async () =>
            {
                // Recheck under the ingest gate: a catch-up may have committed after discovery.
                if (await _db.SessionRuntimeActions.AnyAsync(a => a.AgentSessionId == sessionId, ct))
                    return 0;
                if (await ChannelOutboundEvidence.ProtectedSessions(_db).ContainsAsync(sessionId, ct))
                    return 0;
                if (await _db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == sessionId && t.CreatedAt >= cutoff, ct))
                    return 0;
                if (await _db.ChannelInbounds.AnyAsync(i => i.EnvelopeJson != null && i.QueueMessageId != null
                    && _db.SessionQueuedMessages.Any(m => m.Id == i.QueueMessageId && m.AgentSessionId == sessionId), ct))
                    return 0;
                return await _db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId).ExecuteDeleteAsync(ct);
            }, ct);
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

    private async Task<int> MutateTranscriptAsync(Guid sessionId, Func<Task<int>> mutation, CancellationToken ct)
    {
        if (_states is null) return await mutation();
        if (_db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Transcript retention must own its commit boundary.");
        // Same gate and reseed as cascade deletion. ExecuteDelete is its own commit.
        await using var fence = await _states.BeginMutationAsync([sessionId], ct);
        int removed;
        try { removed = await mutation(); }
        catch
        {
            // Includes an ambiguous ExecuteDelete outcome. A failed reload leaves unavailable state.
            await fence.ReconcileAsync();
            throw;
        }

        if (removed > 0) await fence.PublishCommittedAsync(CancellationToken.None);
        return removed;
    }

    /// <summary>
    /// Deletes settled queue rows past the window. Never a <c>Pending</c> row (parked messages
    /// stay Pending by design) and never an unsettled Channel-origin correlation (CARD-0067).
    /// Independent of session liveness — this is what bounds a long-lived always-on session's queue.
    /// Stamps an unstamped caller completion note before deleting its row so retention cannot
    /// reopen the CARD-0478 replay hole.
    /// </summary>
    public async Task<int> PruneQueuedMessagesAsync(CancellationToken ct)
    {
        if (_settings.QueuedMessageRetentionDays <= 0)
            return 0;

        var cutoff = UtcNow().AddDays(-_settings.QueuedMessageRetentionDays);
        var unresolvedRecovery = CheckCompactionRecoveryStates.Unresolved;
        var outboundSources = ChannelOutboundEvidence.ProtectedSources(_db);
        var supervision = TaskSupervisionNoticeService.Unresolved(_db);
        await CompletionNoteStamp.RepairFromAsync(
            _db,
            _db.SessionQueuedMessages.Where(m =>
                (m.Status == QueuedMessageStatus.Sent || m.Status == QueuedMessageStatus.Canceled)
                && m.CreatedAt < cutoff),
            ct);
        var eligible = _db.SessionQueuedMessages
            .Where(m => (m.Status == QueuedMessageStatus.Sent || m.Status == QueuedMessageStatus.Canceled)
                && m.CreatedAt < cutoff
                && !supervision.Any(n => n.Id == m.SourceTaskSupervisionNoticeId || n.QueueMessageId == m.Id)
                && !_db.SessionRuntimeActions.Any(a => a.AgentSessionId == m.AgentSessionId)
                && m.MaintenanceResult != RemoteControlArmResult.ArmUnconfirmed
                && !m.MaintenanceSlotActive
                && m.DeferredFromRunAttemptId == null
                && (m.SourceLandNotificationId == null || _db.AgentTaskLandNotifications.Any(n =>
                    n.Id == m.SourceLandNotificationId && n.ConfirmedAt != null))
                && (m.Origin != QueuedMessageOrigin.Channel || m.ChannelReplySettledAt != null)
                && (m.ReviewThreadId == null || m.ReviewReplyConsumedAt != null)
                && !outboundSources.Any(s => s.Id == m.Id)
                && !_db.ChannelInbounds.Any(i => i.EnvelopeJson != null && i.QueueMessageId == m.Id)
                && !_db.CheckCompactionRecoveries.Any(r => unresolvedRecovery.Contains(r.State)
                    && (r.SessionId == m.AgentSessionId || r.ResumeSessionId == m.AgentSessionId
                        || (r.OwningCheckTaskId != null && r.OwningCheckTaskId == m.SourceTaskId))));
        var removed = 0;
        foreach (var sessionId in await eligible.Select(m => m.AgentSessionId).Distinct().ToListAsync(ct))
            removed += await MutateTranscriptAsync(sessionId,
                () => eligible.Where(m => m.AgentSessionId == sessionId).ExecuteDeleteAsync(ct), ct);
        if (removed > 0)
        {
            _logger.LogInformation(
                "Pruned {Count} settled queued message(s) past retention",
                removed);
        }

        return removed;
    }

    /// <summary>
    /// CARD-0514: resolved modal episodes follow ordinary queue retention. Open episodes are never
    /// pruned — Attention reads them independently of incident history.
    /// </summary>
    public async Task<int> PruneResolvedModalEpisodesAsync(CancellationToken ct)
    {
        if (_settings.QueuedMessageRetentionDays <= 0)
            return 0;
        var cutoff = UtcNow().AddDays(-_settings.QueuedMessageRetentionDays);
        return await _db.RemoteControlModalEpisodes
            .Where(e => e.ResolvedAt != null && e.ResolvedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Deletes a whole AgentTask tree past <see cref="RetentionSettings.TaskRetentionDays"/>
    /// when every row sharing that <c>RootTaskId</c> is terminal
    /// (<see cref="AgentTaskStatus.Succeeded"/> / <see cref="AgentTaskStatus.Failed"/> /
    /// <see cref="AgentTaskStatus.Canceled"/>) and the tree's newest
    /// <c>COALESCE(CompletedAt, CreatedAt)</c> is older than the cutoff. Never a partial tree:
    /// <c>ParentTaskId</c> is Restrict on purpose, so deletes go children-first (Depth
    /// descending) inside one transaction. <c>AgentTaskEvents</c> cascade.
    /// CARD-1654: the scan is batched. See <see cref="PruneTaskBatchesAsync"/>.
    /// </summary>
    public async Task<int> PruneTasksAsync(CancellationToken ct) =>
        await PruneTaskBatchesAsync(new TaskPruneTally(), ct);

    /// <summary>
    /// CARD-1654. The whole-table eligibility query outgrew the 30 s Npgsql default command
    /// timeout on the desktop, and its failure aborted the sweep. Candidate roots are now taken
    /// <see cref="RetentionSettings.TaskPruneBatchSize"/> at a time in RootTaskId order, and each
    /// batch runs the unchanged eligibility rules restricted to its roots, then deletes the eligible
    /// trees in its own transaction with the batch command timeout. A candidate needs one row older
    /// than the cutoff, which every row of an eligible tree is, so no eligible tree is skipped and
    /// no kept tree becomes eligible. At most <see cref="RetentionSettings.TaskPruneMaxBatchesPerSweep"/>
    /// batches run per sweep; the shared cursor resumes there next sweep.
    /// CARD-1658: the outbound-task-id protection is loaded for every batch attempt, so a task that
    /// became referenced after an earlier batch is never deleted by a later one. A batch that fails
    /// <see cref="TaskBatchAttempts"/> times in a row is skipped (logged by exception type only) and
    /// the scan continues; the skipped trees are revisited when the scan wraps, and the pass still
    /// reports the last failure after it has moved on.
    /// CARD-1662: the failed attempts are budgeted per sweep. When a range ends with
    /// <see cref="RetentionSettings.TaskPruneMaxFailedAttemptsPerSweep"/> or more failed attempts
    /// spent, the pass stops for this sweep and reports Failed (the last failure), the later passes
    /// run, and the cursor resumes after that range (CARD-1670: a short final range completes the scan and
    /// still reports the spent budget). The budget is read when a range ends and a range gets all
    /// <see cref="TaskBatchAttempts"/> attempts, so one sweep spends at most the budget plus two.
    /// Without it a sweep in which every batch times out ran every batch up to the cap three times
    /// (about 2.5 hours at the defaults).
    /// </summary>
    private async Task<int> PruneTaskBatchesAsync(TaskPruneTally tally, CancellationToken ct)
    {
        if (_settings.TaskRetentionDays <= 0)
            return 0;

        var cutoff = UtcNow().AddDays(-_settings.TaskRetentionDays);
        var batchSize = _settings.TaskPruneBatchSize > 0 ? _settings.TaskPruneBatchSize : 100;
        var maxBatches = _settings.TaskPruneMaxBatchesPerSweep > 0 ? _settings.TaskPruneMaxBatchesPerSweep : 50;
        var failureBudget = _settings.TaskPruneMaxFailedAttemptsPerSweep > 0 ? _settings.TaskPruneMaxFailedAttemptsPerSweep : 6;
        var batchTimeout = TimeSpan.FromSeconds(
            _settings.TaskPruneBatchCommandTimeoutSeconds > 0 ? _settings.TaskPruneBatchCommandTimeoutSeconds : 60);

        Exception? skipped = null;
        Exception? lastFailure = null;
        var previousTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(batchTimeout);
        try
        {
            while (tally.Batches < maxBatches)
            {
                var candidates = _db.AgentTasks.Where(t => (t.CompletedAt ?? t.CreatedAt) < cutoff);
                if (_taskCursor.After is Guid after)
                    candidates = candidates.Where(t => t.RootTaskId.CompareTo(after) > 0);
                var batch = await candidates.Select(t => t.RootTaskId).Distinct()
                    .OrderBy(r => r).Take(batchSize).ToListAsync(ct);
                if (batch.Count == 0)
                {
                    _taskCursor.After = null;
                    tally.ScanComplete = true;
                    break;
                }

                tally.Batches++;
                tally.CandidateRoots += batch.Count;
                var started = Stopwatch.GetTimestamp();
                for (var attempt = 1; ; attempt++)
                {
                    // Not a range fault: a protection load failure (a malformed descriptor) ends the pass.
                    var outboundTaskIds = await LoadOutboundTaskIdsAsync(ct);
                    try
                    {
                        var (trees, rows) = await PruneTaskBatchAsync(batch, cutoff, outboundTaskIds, ct);
                        tally.Trees += trees;
                        tally.Rows += rows;
                        break;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        _db.ChangeTracker.Clear();
                        tally.FailedAttempts++;
                        lastFailure = ex;
                        if (attempt < TaskBatchAttempts)
                            continue;
                        tally.SkippedRanges++;
                        skipped = ex;
                        _logger.LogWarning(
                            "Data retention task prune skipped a failing root range after {Attempts} attempt(s): {ExceptionType}",
                            attempt, ExceptionTypeChain(ex));
                        break;
                    }
                }
                tally.BatchMilliseconds.Add((long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                // CARD-1670 (f). The budget is read at every range end, the short final range included, so a budget
                // spent there reports FailureBudgetSpent (and Failed) the same as one spent on a full range.
                var budgetSpent = tally.FailedAttempts >= failureBudget;
                if (budgetSpent)
                {
                    tally.FailureBudgetSpent = true;
                    skipped ??= lastFailure;
                    _logger.LogWarning(
                        "Data retention task prune stopped for this sweep after {FailedAttempts} failed attempt(s) (budget {Budget}); the scan resumes next sweep",
                        tally.FailedAttempts, failureBudget);
                }

                if (batch.Count < batchSize)
                {
                    _taskCursor.After = null;
                    tally.ScanComplete = true;
                    break;
                }

                _taskCursor.After = batch[^1];
                if (budgetSpent)
                    break;
            }
        }
        finally
        {
            _db.Database.SetCommandTimeout(previousTimeout);
        }

        if (tally.Rows > 0 || !tally.ScanComplete)
        {
            _logger.LogInformation(
                "Pruned {Count} task row(s) in {Trees} tree(s) past retention over {Batches} batch(es); scan complete {ScanComplete}; skipped range(s) {SkippedRanges}; batch ms {BatchMilliseconds}",
                tally.Rows, tally.Trees, tally.Batches, tally.ScanComplete, tally.SkippedRanges,
                string.Join(",", tally.BatchMilliseconds));
        }

        if (skipped is not null)
            ExceptionDispatchInfo.Capture(skipped).Throw();
        return tally.Rows;
    }

    private const int TaskBatchAttempts = 3;

    private async Task<(int Trees, int Rows)> PruneTaskBatchAsync(
        List<Guid> batch, DateTime cutoff, HashSet<Guid> outboundTaskIds, CancellationToken ct)
    {
        var unresolvedRecovery = CheckCompactionRecoveryStates.Unresolved;
        var supervision = TaskSupervisionNoticeService.Unresolved(_db);

        // A root is ineligible if ANY row in its tree is still live (Queued/Dispatched/Working/Blocked).
        var liveRootIds = _db.AgentTasks
            .Where(t => t.Status != AgentTaskStatus.Succeeded
                && t.Status != AgentTaskStatus.Failed
                && t.Status != AgentTaskStatus.Canceled)
            .Select(t => t.RootTaskId);

        var eligibleRootIds = await _db.AgentTasks
            .Where(t => batch.Contains(t.RootTaskId)
                && !liveRootIds.Contains(t.RootTaskId)
                && !supervision.Any(n => _db.AgentTasks.Any(member => member.RootTaskId == t.RootTaskId && member.Id == n.TaskId))
                && !_db.AgentTasks.Any(member => member.RootTaskId == t.RootTaskId
                    && outboundTaskIds.Contains(member.Id))
                && !_db.AgentTasks.Any(member => member.RootTaskId == t.RootTaskId && member.SourceLandingOperationId != null)
                && !_db.AgentTaskLandings.Any(op => _db.AgentTasks.Any(member => member.RootTaskId == t.RootTaskId && member.Id == op.TaskId))
                && !_db.AgentTaskLandRequests.Any(r => _db.AgentTasks.Any(member => member.RootTaskId == t.RootTaskId && member.Id == r.TaskId))
                && !_db.AgentTaskDispatchWarningIntents.Any(i => _db.AgentTasks.Any(member => member.RootTaskId == t.RootTaskId && member.Id == i.TaskId))
                && !_db.AgentTaskLandNotifications.Any(n => _db.AgentTasks.Any(member => member.RootTaskId == t.RootTaskId && member.Id == n.TaskId))
                && !_db.TaskWorktreeRetirements.Any(r => _db.AgentTasks.Any(member => member.RootTaskId == t.RootTaskId && member.Id == r.TaskId)
                    && r.Active && r.State != WorktreeRetirementState.Complete && r.State != WorktreeRetirementState.Revoked)
                && !_db.CheckCompactionRecoveries.Any(r => unresolvedRecovery.Contains(r.State)
                    && _db.AgentTasks.Any(member => member.RootTaskId == t.RootTaskId
                        && (member.Id == r.OwningCheckTaskId
                            || member.Id == r.UsefulCheckTaskId
                            || member.AgentSessionId == r.SessionId
                            || (r.ResumeSessionId != null && member.AgentSessionId == r.ResumeSessionId))))
                && !_db.LegacyCheckNotePublications.Any(p =>
                    p.InterpretationTaskId != null
                    && (p.State == LegacyCheckNoteState.Captured
                        || (p.State == LegacyCheckNoteState.Produced
                            && !_db.AgentTaskLandNotifications.Any(n =>
                                n.Id == p.NotificationId && n.ConfirmedAt != null)))
                    && _db.AgentTasks.Any(member =>
                        member.RootTaskId == t.RootTaskId && member.Id == p.InterpretationTaskId)))
            .GroupBy(t => t.RootTaskId)
            .Where(g => g.Max(t => t.CompletedAt ?? t.CreatedAt) < cutoff)
            .Select(g => g.Key)
            .ToListAsync(ct);

        if (eligibleRootIds.Count == 0)
            return (0, 0);

        // Children-first so Restrict on ParentTaskId never fires. One transaction so a
        // mid-loop failure cannot leave a half-deleted tree.
        var depths = await _db.AgentTasks
            .Where(t => eligibleRootIds.Contains(t.RootTaskId))
            .Select(t => t.Depth)
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync(ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var eligibleTaskIds = await _db.AgentTasks
            .Where(t => eligibleRootIds.Contains(t.RootTaskId))
            .Select(t => t.Id)
            .ToListAsync(ct);
        var retirementIds = await _db.TaskWorktreeRetirements
            .Where(r => eligibleTaskIds.Contains(r.TaskId))
            .Select(r => r.Id)
            .ToListAsync(ct);
        if (retirementIds.Count > 0)
        {
            await _db.TaskWorktreeRetirementAttempts
                .Where(a => retirementIds.Contains(a.RetirementId))
                .ExecuteDeleteAsync(ct);
            await _db.TaskWorktreeRetirements
                .Where(r => retirementIds.Contains(r.Id))
                .ExecuteDeleteAsync(ct);
        }

        var removed = 0;
        foreach (var depth in depths)
        {
            removed += await _db.AgentTasks
                .Where(t => eligibleRootIds.Contains(t.RootTaskId) && t.Depth == depth)
                .ExecuteDeleteAsync(ct);
        }

        await tx.CommitAsync(ct);
        return (eligibleRootIds.Count, removed);
    }

    /// <summary>
    /// CARD-0079. A confirmed legacy Check publication keeps its logical identity.
    /// Captured facts and render context age out. The produced body, digest, and
    /// ProducedAt stay, because a Produced row without them violates the completeness check.
    /// Deleting the row would let a later capture mint a second note.
    /// </summary>
    public async Task RetainConfirmedPublicationIdentitiesAsync(CancellationToken ct)
    {
        var updated = await _db.LegacyCheckNotePublications
            .Where(p => p.State == LegacyCheckNoteState.Produced
                && _db.AgentTaskLandNotifications.Any(n => n.Id == p.NotificationId && n.ConfirmedAt != null))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.FactsSnapshotJson, "{}")
                .SetProperty(p => p.RenderContextJson, "{}"), ct);
        if (updated > 0)
        {
            _logger.LogInformation(
                "Retained {Count} confirmed legacy Check publication identities as tombstones",
                updated);
        }
    }

    /// <summary>
    /// CARD-0079. Unresolved episodes stay. Terminal action rows stay for
    /// <see cref="CheckCompactionRecoveryStates.TerminalAuditDays"/>, which is longer
    /// than ordinary incident retention. The supervision-state receipt gate is a
    /// different row and is not cleared here.
    /// </summary>
    public async Task<int> PruneCheckCompactionRecoveriesAsync(CancellationToken ct)
    {
        var cutoff = UtcNow().AddDays(-CheckCompactionRecoveryStates.TerminalAuditDays);
        var unresolved = CheckCompactionRecoveryStates.Unresolved;
        var removed = await _db.CheckCompactionRecoveries
            .Where(r => !unresolved.Contains(r.State) && r.DetectedAt < cutoff)
            .ExecuteDeleteAsync(ct);
        if (removed > 0)
        {
            _logger.LogInformation(
                "Pruned {Count} terminal compaction-recovery row(s) past the 90-day audit window",
                removed);
        }

        return removed;
    }

    /// <summary>
    /// Deletes subscription-usage samples older than
    /// <see cref="RetentionSettings.SubscriptionUsageRetentionDays"/>. Independent of session
    /// liveness — the quota belongs to a subscription. <c>&lt;= 0</c> disables the pass.
    /// CARD-1658: deleted in bounded batches; see <see cref="PruneRowBatchesAsync"/>.
    /// </summary>
    public Task<int> PruneSubscriptionUsageSamplesAsync(CancellationToken ct) =>
        PruneSubscriptionUsageSamplesAsync(new RowBatchTally(), ct);

    private async Task<int> PruneSubscriptionUsageSamplesAsync(RowBatchTally tally, CancellationToken ct)
    {
        if (_settings.SubscriptionUsageRetentionDays <= 0)
            return 0;

        var cutoff = UtcNow().AddDays(-_settings.SubscriptionUsageRetentionDays);
        var removed = await PruneRowBatchesAsync(tally, size => _db.SubscriptionUsageSamples
            .Where(s => s.ObservedAt < cutoff)
            .OrderBy(s => s.Id).Take(size)
            .ExecuteDeleteAsync(ct));
        if (removed > 0 || !tally.Complete)
        {
            _logger.LogInformation(
                "Pruned {Count} subscription-usage sample(s) past retention over {Batches} batch(es); complete {Complete}; batch ms {BatchMilliseconds}",
                removed, tally.Batches, tally.Complete, string.Join(",", tally.BatchMilliseconds));
        }

        return removed;
    }

    /// <summary>
    /// CARD-1658. The audit pass: nulls <c>FullContent</c> on records older than
    /// <see cref="AuditSettings.RetentionDays"/>, keeping no copy (the same cutoff and effect as
    /// <see cref="AuditService.ArchiveFullContentAsync"/>, by design), in bounded batches.
    /// </summary>
    private async Task<int> ArchiveAuditFullContentAsync(RowBatchTally tally, CancellationToken ct)
    {
        var cutoff = AuditService.ArchiveCutoff(_auditSettings.RetentionDays);
        var archived = await PruneRowBatchesAsync(tally,
            size => _auditService.ArchiveFullContentBatchAsync(cutoff, size, ct));
        if (archived > 0 || !tally.Complete)
        {
            _logger.LogInformation(
                "Archived FullContent of {Count} audit record(s) past retention over {Batches} batch(es); complete {Complete}; batch ms {BatchMilliseconds}",
                archived, tally.Batches, tally.Complete, string.Join(",", tally.BatchMilliseconds));
        }

        return archived;
    }

    /// <summary>
    /// CARD-1658. The passes after Tasks were single unbounded statements under the 30 s default
    /// command timeout. Each now runs <paramref name="batch"/> (one statement touching at most
    /// <see cref="RetentionSettings.RowPruneBatchSize"/> rows, its own implicit transaction) under
    /// <see cref="RetentionSettings.RowPruneBatchCommandTimeoutSeconds"/>, until a batch comes back
    /// short or <see cref="RetentionSettings.RowPruneMaxBatchesPerSweep"/> is reached. The predicate
    /// is the pass's own, so what a pass deletes is unchanged; a capped pass continues next sweep.
    /// </summary>
    private async Task<int> PruneRowBatchesAsync(RowBatchTally tally, Func<int, Task<int>> batch)
    {
        var batchSize = _settings.RowPruneBatchSize > 0 ? _settings.RowPruneBatchSize : 1000;
        var maxBatches = _settings.RowPruneMaxBatchesPerSweep > 0 ? _settings.RowPruneMaxBatchesPerSweep : 50;
        var batchTimeout = TimeSpan.FromSeconds(
            _settings.RowPruneBatchCommandTimeoutSeconds > 0 ? _settings.RowPruneBatchCommandTimeoutSeconds : 60);
        var previousTimeout = _db.Database.GetCommandTimeout();
        _db.Database.SetCommandTimeout(batchTimeout);
        try
        {
            while (tally.Batches < maxBatches)
            {
                var started = Stopwatch.GetTimestamp();
                var rows = await batch(batchSize);
                tally.Batches++;
                tally.Rows += rows;
                tally.BatchMilliseconds.Add((long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (rows < batchSize)
                {
                    tally.Complete = true;
                    break;
                }
            }
        }
        finally
        {
            _db.Database.SetCommandTimeout(previousTimeout);
        }

        return tally.Rows;
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

    /// <summary>
    /// CARD-1446 D-5 (S5). Deletes <c>WorktreeResidueRunCandidates</c> rows with Outcome <c>Held</c> or
    /// <c>Preview</c> whose <c>EvaluatedAt</c> is past <see cref="RetentionSettings.WorktreeResidueHeldRowRetentionDays"/>.
    /// Run headers and action rows (<c>Queued</c>, <c>Refused</c>, <c>Partial</c>, <c>Removed</c>) are never
    /// touched, so every queued cleanup request keeps its provenance. Nonpositive days disables the pass.
    /// CARD-1658: deleted in bounded batches; see <see cref="PruneRowBatchesAsync"/>.
    /// </summary>
    public Task<int> PruneWorktreeResidueHeldRowsAsync(CancellationToken ct) =>
        PruneWorktreeResidueHeldRowsAsync(new RowBatchTally(), ct);

    private async Task<int> PruneWorktreeResidueHeldRowsAsync(RowBatchTally tally, CancellationToken ct)
    {
        if (_settings.WorktreeResidueHeldRowRetentionDays <= 0)
            return 0;

        var cutoff = UtcNow().AddDays(-_settings.WorktreeResidueHeldRowRetentionDays);
        var held = nameof(WorktreeResidueCandidateOutcome.Held);
        var preview = nameof(WorktreeResidueCandidateOutcome.Preview);
        var removed = await PruneRowBatchesAsync(tally, size => _db.WorktreeResidueRunCandidates
            .Where(c => (c.Outcome == held || c.Outcome == preview) && c.EvaluatedAt < cutoff)
            .OrderBy(c => c.Id).Take(size)
            .ExecuteDeleteAsync(ct));
        if (removed > 0 || !tally.Complete)
        {
            _logger.LogInformation(
                "Pruned {Count} worktree-residue Held/Preview candidate row(s) past retention over {Batches} batch(es); complete {Complete}; batch ms {BatchMilliseconds}; run headers and action rows kept",
                removed, tally.Batches, tally.Complete, string.Join(",", tally.BatchMilliseconds));
        }

        return removed;
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private sealed class TaskPruneTally
    {
        public int Batches;
        public int CandidateRoots;
        public int Trees;
        public int Rows;
        public bool ScanComplete;
        public int FailedAttempts;
        public int SkippedRanges;
        public bool FailureBudgetSpent;
        public List<long> BatchMilliseconds { get; } = [];

        public DataRetentionTaskPruneProgress Snapshot(Guid? cursor) =>
            new(Batches, CandidateRoots, Trees, Rows, ScanComplete, cursor, FailedAttempts, SkippedRanges,
                BatchMilliseconds.ToList(), FailureBudgetSpent);
    }

    /// <summary>CARD-1658. Progress of one batched row pass.</summary>
    private sealed class RowBatchTally
    {
        public int Batches;
        public int Rows;
        public bool Complete;
        public List<long> BatchMilliseconds { get; } = [];

        public DataRetentionBatchProgress Snapshot() => new(Batches, Rows, Complete, BatchMilliseconds.ToList());
    }

    private async Task<HashSet<Guid>> LoadOutboundTaskIdsAsync(CancellationToken ct)
    {
        var ids = (await ChannelOutboundEvidence.ProtectedSources(_db)
            .Where(m => m.SourceTaskId != null).Select(m => m.SourceTaskId!.Value).ToListAsync(ct)).ToHashSet();
        // Captures contain every batch/implied member, not only the legacy SourceTaskId.
        // Stream rows rather than loading all frozen descriptors into memory at once.
        await foreach (var delivery in ChannelOutboundEvidence.ProtectedDeliveries(_db)
            .AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
        {
            if (delivery.SourceTaskId is Guid source) ids.Add(source);
            if (delivery.ConversionTaskId is Guid converter) ids.Add(converter);
            if (delivery.CaptureJson is null) continue;
            // A malformed descriptor refuses this prune pass: deleting its unknown inputs
            // would make a visible, repairable failed intent irrecoverable.
            foreach (var task in ChannelReplyPreparation.Deserialize(delivery.CaptureJson).Tasks)
                ids.Add(task.TaskId);
        }
        return ids;
    }
}

public sealed record DataRetentionSweepResult(
    int Transcripts, int QueuedMessages, int Sessions, int Tasks, int AuditRecords,
    int SubscriptionUsageSamples = 0, int WorktreeResidueHeldRows = 0,
    IReadOnlyList<DataRetentionPassOutcome>? Passes = null,
    DataRetentionTaskPruneProgress? TaskPrune = null);

/// <summary>
/// CARD-1654. Where the task-prune scan stopped. A singleton, so a sweep that hits
/// <see cref="RetentionSettings.TaskPruneMaxBatchesPerSweep"/> resumes there next sweep.
/// </summary>
public sealed class DataRetentionTaskCursor
{
    private readonly object _gate = new();
    private Guid? _after;

    public Guid? After
    {
        get { lock (_gate) return _after; }
        set { lock (_gate) _after = value; }
    }
}

public enum DataRetentionPassStatus { Completed, Failed, Skipped }

/// <summary>
/// CARD-1654. One sweep pass. <c>ExceptionType</c> names types only, never row data.
/// CARD-1658: <c>Progress</c> is set for the batched row passes after Tasks.
/// </summary>
public sealed record DataRetentionPassOutcome(string Pass, DataRetentionPassStatus Status, string? ExceptionType = null,
    DataRetentionBatchProgress? Progress = null);

/// <summary>
/// CARD-1658. Batched row-pass progress for one sweep. <c>Complete</c> is false when the per-sweep
/// batch cap stopped the pass; <c>BatchMilliseconds</c> is each batch statement's wall time.
/// </summary>
public sealed record DataRetentionBatchProgress(int Batches, int Rows, bool Complete, IReadOnlyList<long> BatchMilliseconds);

/// <summary>
/// CARD-1654. Task-prune progress for one sweep. <c>ScanComplete</c> is false when the per-sweep
/// batch cap stopped the scan; the next sweep resumes after <c>Cursor</c>.
/// CARD-1658: <c>FailedAttempts</c> counts failed batch attempts, <c>SkippedRanges</c> the root ranges
/// skipped after three consecutive failures, <c>BatchMilliseconds</c> each batch's wall time.
/// CARD-1662: <c>FailureBudgetSpent</c> is true when <see cref="RetentionSettings.TaskPruneMaxFailedAttemptsPerSweep"/>
/// stopped the scan for this sweep.
/// </summary>
public sealed record DataRetentionTaskPruneProgress(
    int Batches, int CandidateRoots, int Trees, int Rows, bool ScanComplete, Guid? Cursor,
    int FailedAttempts = 0, int SkippedRanges = 0, IReadOnlyList<long>? BatchMilliseconds = null,
    bool FailureBudgetSpent = false);
