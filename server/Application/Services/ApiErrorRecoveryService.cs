using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0072 S5a: adopt dead API-error TurnEnd rows into durable <see cref="ApiErrorRecovery"/>
/// schedules, then fire one <see cref="SessionMessageQueueService.EnqueueAsync"/> when a rung
/// is due. A third one-minute sweep on <c>AgentSupervisorHostedService</c> — not a new queue
/// and hosted service, which would lose the schedule on restart and reproduce this card's
/// complaint.
///
/// Singleton: the hosted service is a singleton and this is the action it calls; per-tick
/// scopes are opened internally, matching <see cref="ContextCompactionService"/>.
/// </summary>
public sealed class ApiErrorRecoveryService
{
    internal const string DeadTimeFailureReason = "DeadTime";
    internal const string WallUnparsedFailureReason = "WallUnparsed";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionMessageQueueService _queue;
    private readonly AgentSessionRuntime _runtime;
    private readonly SupervisionSettings _supervision;
    private readonly ApiErrorRecoverySettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<ApiErrorRecoveryService> _logger;
    private readonly CapacityRecoveryService? _capacityRecovery;

    public ApiErrorRecoveryService(
        IServiceScopeFactory scopeFactory,
        SessionMessageQueueService queue,
        AgentSessionRuntime runtime,
        IOptions<SupervisionSettings> settings,
        TimeProvider time,
        ILogger<ApiErrorRecoveryService> logger,
        CapacityRecoveryService? capacityRecovery = null)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _runtime = runtime;
        _supervision = settings.Value;
        _settings = settings.Value.ApiErrorRecovery;
        _time = time;
        _logger = logger;
        _capacityRecovery = capacityRecovery;
    }

    /// <summary>
    /// Adopt any recent untracked stubs, then enqueue a resume for every due schedule. Returns
    /// how many resumes this pass actually enqueued. Other sessions may ride along on a shared
    /// database — callers must not assert on this number as a global count.
    /// </summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
            return 0;

        await AdoptAsync(ct);
        return await FireDueAsync(ct);
    }

    /// <summary>
    /// Insert the recovery row for this TurnEnd stub if it is not already there. Idempotent on
    /// <c>(sessionId, stubSequence)</c>. Used by the sweep and by the task defer arm so the first
    /// <c>OnTurnEndAsync</c> does not wait for the next tick to have a marker.
    /// A Wall already adopted with empty stub text is repaired when a later caller supplies
    /// the real AssistantText (CARD-0401).
    /// </summary>
    public async Task<ApiErrorRecovery> EnsureAdoptedAsync(
        Guid sessionId,
        long stubSequence,
        string? stubUuid,
        string? apiErrorClass,
        int? apiErrorStatus,
        string? errorText,
        CancellationToken ct,
        bool raiseIncident = true)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.ApiErrorRecoveries
            .FirstOrDefaultAsync(r => r.AgentSessionId == sessionId && r.StubSequence == stubSequence, ct);
        if (existing is not null)
        {
            await TryRepairEmptyWallAsync(
                db, scope.ServiceProvider, existing, sessionId, errorText, ct);
            return existing;
        }

        var now = UtcNow();
        var row = await BuildNewRowAsync(
            db, scope.ServiceProvider, sessionId, stubSequence, stubUuid, apiErrorClass, apiErrorStatus, errorText, now, ct);
        db.ApiErrorRecoveries.Add(row);

        var older = await db.ApiErrorRecoveries
            .Where(r => r.AgentSessionId == sessionId
                && r.StubSequence < stubSequence
                && r.ResolvedAt == null)
            .ToListAsync(ct);
        foreach (var prior in older)
            Resolve(prior, now, ApiErrorRecoveryReasons.Replaced);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueAdoptionConflict(ex))
        {
            // Unique (AgentSessionId, StubSequence) — a concurrent sweep/turn-end already wrote it.
            db.ChangeTracker.Clear();
            var raced = await db.ApiErrorRecoveries.SingleAsync(
                r => r.AgentSessionId == sessionId && r.StubSequence == stubSequence, ct);
            await TryRepairEmptyWallAsync(
                db, scope.ServiceProvider, raced, sessionId, errorText, ct);
            return raced;
        }

        _logger.LogInformation(
            "Adopted API-error stub session {SessionId} seq {Sequence} as {Classification} "
            + "(nextAttempt={NextAttempt:u}, reason={Reason})",
            sessionId, stubSequence, row.Classification, row.NextAttemptAt, row.ResolvedReason);

        if (raiseIncident)
            await RaiseAdoptIncidentAsync(scope.ServiceProvider, db, row, errorText, ct);

        return row;
    }

    private async Task AdoptAsync(CancellationToken ct)
    {
        var now = UtcNow();
        var cutoff = now - TimeSpan.FromMinutes(Math.Max(1, _settings.AdoptWindowMinutes));

        List<StubRow> stubs;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var turnEnds = await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.IsApiError == true
                    && t.Kind == TranscriptKinds.TurnEnd
                    && t.CreatedAt >= cutoff)
                .OrderBy(t => t.AgentSessionId)
                .ThenBy(t => t.Sequence)
                .Select(t => new StubRow(
                    t.AgentSessionId, t.Sequence, t.Uuid, t.ApiErrorClass, t.ApiErrorStatus, t.Text,
                    t.Timestamp, t.CreatedAt))
                .ToListAsync(ct);

            // CARD-0401: Claude puts the error string on the sibling AssistantText (same uuid);
            // TurnEnd.Text is null. Codex has no split — fall back to TurnEnd.Text.
            var siblings = await ApiErrorStubText.LoadSiblingsAsync(
                db,
                turnEnds.Select(t => (t.AgentSessionId, t.Uuid)).ToList(),
                ct);
            stubs = turnEnds
                .Select(t => t with
                {
                    Text = t.Uuid is { } uuid
                           && siblings.TryGetValue((t.AgentSessionId, uuid), out var assistant)
                        ? ApiErrorStubText.Prefer(assistant, t.Text)
                        : ApiErrorStubText.Prefer(null, t.Text),
                })
                .ToList();
        }

        foreach (var stub in stubs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await EnsureAdoptedAsync(
                    stub.AgentSessionId, stub.Sequence, stub.Uuid,
                    stub.ApiErrorClass, stub.ApiErrorStatus, stub.Text, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "API-error adopt failed for session {SessionId} seq {Sequence}",
                    stub.AgentSessionId, stub.Sequence);
            }
        }
    }

    private async Task<int> FireDueAsync(CancellationToken ct)
    {
        var now = UtcNow();
        List<Guid> dueIds;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dueIds = await db.ApiErrorRecoveries.AsNoTracking()
                .Where(r => r.ResolvedAt == null && r.NextAttemptAt != null && r.NextAttemptAt <= now)
                .Select(r => r.Id)
                .ToListAsync(ct);
        }

        var fired = 0;
        foreach (var id in dueIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await FireOneAsync(id, ct))
                    fired++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "API-error fire failed for recovery {RecoveryId}", id);
            }
        }

        return fired;
    }

    private async Task<bool> FireOneAsync(Guid recoveryId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recovery = await db.ApiErrorRecoveries.FirstOrDefaultAsync(r => r.Id == recoveryId, ct);
        if (recovery is null || recovery.ResolvedAt is not null || recovery.NextAttemptAt is null)
            return false;

        var session = await db.AgentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == recovery.AgentSessionId, ct);
        if (session is null || session.Status != SessionStatus.Running)
            return false;
        if (!_runtime.ListLiveSessions().Contains(recovery.AgentSessionId))
            return false;

        // Never act on "the transcript does not contain X" without pulling first (CARD-0055).
        await _runtime.CatchUpTranscriptAsync(recovery.AgentSessionId, ct);

        var laterPrompt = await db.TranscriptEntries.AsNoTracking().AnyAsync(
            t => t.AgentSessionId == recovery.AgentSessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Sequence > recovery.StubSequence, ct);
        if (laterPrompt)
        {
            Resolve(recovery, UtcNow(), ApiErrorRecoveryReasons.Superseded);
            await db.SaveChangesAsync(ct);
            return false;
        }

        var newerStub = await db.TranscriptEntries.AsNoTracking().AnyAsync(
            t => t.AgentSessionId == recovery.AgentSessionId
                && t.Kind == TranscriptKinds.TurnEnd
                && t.IsApiError == true
                && t.Sequence > recovery.StubSequence, ct);
        if (newerStub)
        {
            Resolve(recovery, UtcNow(), ApiErrorRecoveryReasons.Replaced);
            await db.SaveChangesAsync(ct);
            return false;
        }

        var prompt = recovery.Classification == ApiErrorClassification.Wall
            ? _settings.WallPrompt
            : _settings.TransientPrompt;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            _logger.LogWarning(
                "API-error resume prompt is empty for {Classification}; skipping fire on {RecoveryId}",
                recovery.Classification, recovery.Id);
            return false;
        }

        await _queue.EnqueueAsync(
            recovery.AgentSessionId, prompt, MessageSendMode.WhenIdle, ct,
            origin: QueuedMessageOrigin.Supervision);

        var now = UtcNow();
        recovery.AttemptCount++;
        recovery.LastEnqueuedAt = now;

        if (recovery.Classification == ApiErrorClassification.Unknown
            && ApiErrorRetrySchedule.UnknownIsExhausted(recovery.AttemptCount, _settings.UnknownAttemptCap))
        {
            Resolve(recovery, now, ApiErrorRecoveryReasons.UnknownExhausted);
        }
        else
        {
            var next = ApiErrorRetrySchedule.Interval(recovery.AttemptCount + 1, recovery.Classification);
            recovery.NextAttemptAt = next is TimeSpan gap ? now + gap : null;
        }

        await MaybeRaiseDeadTimeIncidentAsync(scope.ServiceProvider, db, recovery, now, ct);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Enqueued API-error resume for session {SessionId} seq {Sequence} "
            + "(attempt {Attempt}, next={Next:u}, classification={Classification})",
            recovery.AgentSessionId, recovery.StubSequence, recovery.AttemptCount,
            recovery.NextAttemptAt, recovery.Classification);
        return true;
    }

    private async Task<ApiErrorRecovery> BuildNewRowAsync(
        AppDbContext db,
        IServiceProvider services,
        Guid sessionId,
        long stubSequence,
        string? stubUuid,
        string? apiErrorClass,
        int? apiErrorStatus,
        string? errorText,
        DateTime now,
        CancellationToken ct)
    {
        var classification = ApiErrorClassifier.Classify(apiErrorClass, apiErrorStatus, errorText);
        var sibling = await CapacityEvidence.LoadSiblingAsync(db, sessionId, stubUuid, ct);
        var turnEnd = await CapacityEvidence.LoadTurnEndAsync(db, sessionId, stubSequence, ct);
        var evidence = CapacityEvidence.Resolve(
            turnEnd.Timestamp, sibling.Timestamp, turnEnd.CreatedAt, now);
        errorText = ApiErrorStubText.Prefer(sibling.Text, errorText);
        var row = new ApiErrorRecovery
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            StubSequence = stubSequence,
            StubUuid = stubUuid,
            Classification = classification,
            ApiErrorClass = apiErrorClass,
            ApiErrorStatus = apiErrorStatus,
            DetectedAt = now,
            AttemptCount = 0,
            EvidenceAt = evidence.At,
            EvidenceTimestampSource = evidence.Source,
            ParseVersion = UsageLimitWallParser.ParseVersion,
            EvidenceDigest = CapacityEvidence.Digest(errorText),
            EvidenceStatus = string.IsNullOrWhiteSpace(errorText) ? "empty" : "text",
        };

        if (classification == ApiErrorClassification.NeedsHuman)
        {
            Resolve(row, now, ApiErrorRecoveryReasons.NeedsHuman);
            return row;
        }

        if (classification == ApiErrorClassification.Wall)
        {
            var availability = services.GetRequiredService<ModelAvailability>();
            return await ApplyWallAsync(db, availability, row, sessionId, errorText, now, ct, isNew: true);
        }

        var interval = ApiErrorRetrySchedule.Interval(1, classification);
        row.NextAttemptAt = interval is TimeSpan gap ? now + gap : null;
        return row;
    }

    /// <summary>
    /// CARD-0022 / CARD-0335: two Wall subclasses. SessionLimit gets one resume at reset+2min.
    /// ModelCap (and unparseable reset) writes a timed hold at now +
    /// <see cref="ApiErrorRecoverySettings.ModelCapFallbackHoldHours"/> and never the 30-minute
    /// WallPrompt. Parse-null (no alias at all) keeps
    /// <see cref="WallUnparsedFailureReason"/> and still does not enter the 30-minute ladder.
    /// </summary>
    private async Task<ApiErrorRecovery> ApplyWallAsync(
        AppDbContext db,
        ModelAvailability availability,
        ApiErrorRecovery row,
        Guid sessionId,
        string? errorText,
        DateTime now,
        CancellationToken ct,
        bool isNew = true)
    {
        var evidenceAt = row.EvidenceAt ?? now;
        var fallback = await ResolveFallbackAliasAsync(db, sessionId, ct);
        var wall = UsageLimitWallParser.Parse(evidenceAt, errorText, fallback);

        var wallDeaths = await db.ApiErrorRecoveries.CountAsync(
            r => r.AgentSessionId == sessionId
                && r.Classification == ApiErrorClassification.Wall
                && r.ResolvedReason != ApiErrorRecoveryReasons.Superseded, ct);
        // A new row is not in the count yet; a repair of an existing row already is.
        var parked = !_supervision.CapacityRecovery.Enabled
            && ApiErrorRetrySchedule.WallIsParked(
                isNew ? wallDeaths + 1 : wallDeaths, _settings.WallDeathCap);

        if (wall is null)
        {
            // No alias at all — better than pausing a guessed model. Do not 30-minute-nudge.
            Resolve(row, now, parked ? ApiErrorRecoveryReasons.WallParked : WallUnparsedFailureReason);
            return row;
        }

        var disabledUntil = wall.ResetAt is { } reset
            ? reset + ModelAvailability.SessionLimitResumePadding
            : evidenceAt + TimeSpan.FromHours(_settings.EffectiveModelCapFallbackHoldHours);
        if (disabledUntil <= now && wall.ResetAt is not null)
        {
            // Delayed repair whose deadline is already past: still write the historical deadline;
            // the hold clear primitive expires it immediately rather than rolling from repair time.
        }
        var session = await db.AgentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var kind = session?.AgentKind ?? AgentKind.ClaudeCode;
        var openTaskId = await db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working))
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

        var hold = await availability.UpsertAutoDetectedAsync(
            kind,
            wall.ModelAlias,
            disabledUntil,
            UsageLimitWallParser.FormatReason(wall),
            wall.RawText,
            sessionId,
            openTaskId,
            ct,
            saveChanges: false,
            evidenceRecoveryId: row.Id);
        row.AppliedHoldId = hold.Id;
        row.AppliedHoldRevision = hold.Revision;
        row.ParseVersion = UsageLimitWallParser.ParseVersion;
        row.EvidenceDigest = CapacityEvidence.Digest(errorText);
        row.EvidenceStatus = string.IsNullOrWhiteSpace(errorText) ? "empty" : "text";

        if (_capacityRecovery is not null)
        {
            var wait = await _capacityRecovery.EnsureWaitOnAsync(db, new CapacityWaitRegistration
            {
                ConsumerKey = $"session:{sessionId:N}",
                ConsumerKind = CapacityWaitConsumerKind.LiveSession,
                ExecutionKind = kind,
                RequestedKind = kind,
                RequestedAlias = wall.ModelAlias,
                BlockedAt = now,
                SessionId = sessionId,
                TaskId = openTaskId,
                HoldId = hold.Id,
                HoldRevision = hold.Revision,
                HoldAlreadyCleared = hold.ClearedAt is not null,
                ClearCause = hold.ClearCause,
            }, ct);
            row.CapacityWaitId = wait.Id;
        }

        if (parked)
        {
            Resolve(row, now, ApiErrorRecoveryReasons.WallParked);
            return row;
        }

        if (wall.Kind == UsageLimitWallKind.SessionLimit)
        {
            row.ResolvedAt = null;
            row.ResolvedReason = null;
            row.NextAttemptAt = disabledUntil;
            return row;
        }

        Resolve(row, now, ApiErrorRecoveryReasons.WallModelPaused);
        return row;
    }

    /// <summary>
    /// CARD-0412: exact, idempotent enrichment. Missing/cleared hold, revision mismatch,
    /// changed source, no improvement or a second parse-version correction is a no-op.
    /// </summary>
    private async Task TryRepairEmptyWallAsync(
        AppDbContext db,
        IServiceProvider services,
        ApiErrorRecovery existing,
        Guid sessionId,
        string? errorText,
        CancellationToken ct)
    {
        if (existing.Classification != ApiErrorClassification.Wall)
            return;
        if (string.IsNullOrWhiteSpace(errorText))
            return;

        var digest = CapacityEvidence.Digest(errorText);
        var textImproved = existing.EvidenceStatus == "empty"
            || string.IsNullOrWhiteSpace(existing.EvidenceDigest)
            || existing.EvidenceDigest != digest;
        var parseUpgrade = existing.ParseVersion is null or < UsageLimitWallParser.ParseVersion
            && existing.EvidenceStatus != "parse-corrected";
        var siblingEarly = await CapacityEvidence.LoadSiblingAsync(db, sessionId, existing.StubUuid, ct);
        var turnEndEarly = await CapacityEvidence.LoadTurnEndAsync(db, sessionId, existing.StubSequence, ct);
        var incomingEarly = CapacityEvidence.Resolve(
            turnEndEarly.Timestamp, siblingEarly.Timestamp, turnEndEarly.CreatedAt, existing.DetectedAt);
        var currentEarly = new CapacityEvidence.Facts(
            existing.EvidenceAt ?? existing.DetectedAt,
            existing.EvidenceTimestampSource ?? CapacityEvidenceTimestampSource.DetectedAt,
            turnEndEarly.Timestamp, siblingEarly.Timestamp, turnEndEarly.CreatedAt);
        var improvedEarly = CapacityEvidence.TryImprove(currentEarly, incomingEarly);
        if (!textImproved && !parseUpgrade)
        {
            if (improvedEarly is { } onlyTime)
            {
                existing.EvidenceAt = onlyTime.At;
                existing.EvidenceTimestampSource = onlyTime.Source;
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        if (existing.AppliedHoldId is null)
            return;

        var hold = await db.ModelAvailabilityHolds
            .FirstOrDefaultAsync(h => h.Id == existing.AppliedHoldId, ct);
        if (hold is null || hold.ClearedAt is not null)
            return;
        if (existing.AppliedHoldRevision is { } expectedRev && hold.Revision != expectedRev)
            return;
        if (hold.EvidenceRecoveryId is { } latest && latest != existing.Id)
            return;
        if (hold.SourceSessionId is { } source && source != sessionId)
            return;

        var sibling = await CapacityEvidence.LoadSiblingAsync(db, sessionId, existing.StubUuid, ct);
        var turnEnd = await CapacityEvidence.LoadTurnEndAsync(db, sessionId, existing.StubSequence, ct);
        var incoming = CapacityEvidence.Resolve(
            turnEnd.Timestamp, sibling.Timestamp, turnEnd.CreatedAt, existing.DetectedAt);
        var current = new CapacityEvidence.Facts(
            existing.EvidenceAt ?? existing.DetectedAt,
            existing.EvidenceTimestampSource ?? CapacityEvidenceTimestampSource.DetectedAt,
            turnEnd.Timestamp, sibling.Timestamp, turnEnd.CreatedAt);
        if (CapacityEvidence.TryImprove(current, incoming) is { } improved)
        {
            existing.EvidenceAt = improved.At;
            existing.EvidenceTimestampSource = improved.Source;
        }

        var availability = services.GetRequiredService<ModelAvailability>();
        await ApplyWallAsync(db, availability, existing, sessionId, errorText, UtcNow(), ct, isNew: false);
        if (parseUpgrade && !textImproved)
            existing.EvidenceStatus = "parse-corrected";
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Repaired API-error wall session {SessionId} seq {Sequence} from empty stub text ({Length} chars)",
            sessionId, existing.StubSequence, errorText.Trim().Length);
    }

    private static async Task<string?> ResolveFallbackAliasAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var session = await db.AgentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var kind = session?.AgentKind ?? AgentKind.ClaudeCode;
        var fromSession = ModelAlias.Normalize(kind, session?.EffectiveModelId);
        if (fromSession is not null)
            return fromSession;

        var sessionIdText = sessionId.ToString("D");
        var agent = await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId == sessionIdText)
            .Select(a => new { a.ModelId, a.ModelLevel })
            .FirstOrDefaultAsync(ct);
        var fromAgent = ModelAlias.Normalize(kind, agent?.ModelId);
        if (fromAgent is not null)
            return fromAgent;

        var taskLevel = await db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (AgentModelLevel?)t.ModelLevel)
            .FirstOrDefaultAsync(ct);
        if (taskLevel is { } level)
            return ModelLevelAliases.For(kind, level);
        if (agent is not null)
            return ModelLevelAliases.For(kind, agent.ModelLevel);
        return ModelLevelAliases.For(kind, AgentModelLevel.High);
    }

    private static void Resolve(ApiErrorRecovery row, DateTime now, string reason)
    {
        row.ResolvedAt = now;
        row.ResolvedReason = reason;
        row.NextAttemptAt = null;
    }

    private static string QuoteError(string? errorText)
    {
        var quoted = string.IsNullOrWhiteSpace(errorText) ? "(no error text)" : errorText.Trim();
        return quoted.Length > 600 ? quoted[..600] + "…" : quoted;
    }

    private async Task RaiseAdoptIncidentAsync(
        IServiceProvider services, AppDbContext db, ApiErrorRecovery row, string? errorText,
        CancellationToken ct)
    {
        var hasOpenTask = await db.AgentTasks.AsNoTracking().AnyAsync(
            t => t.AgentSessionId == row.AgentSessionId
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working), ct);
        if (hasOpenTask)
            return; // the task defer/fail arm owns the incident (and the git-dirt detail)

        var already = await db.AgentIncidents.AsNoTracking().AnyAsync(
            i => i.SessionId == row.AgentSessionId && i.Kind == AgentIncidentKind.ApiErrorTurnDied, ct);
        if (already)
            return;

        var channelBound = await IsChannelBoundAsync(db, row.AgentSessionId, ct);
        AlertSeverity severity;
        string failureReason;
        string message;

        if (row.Classification == ApiErrorClassification.NeedsHuman)
        {
            severity = AlertSeverity.Critical;
            failureReason = ApiErrorRecoveryReasons.NeedsHuman;
            message =
                $"Session {row.AgentSessionId} died on a NeedsHuman API error "
                + $"({row.ApiErrorClass ?? "no error class"}). Nothing automatic can fix this.";
        }
        else if (row.ResolvedReason == ApiErrorRecoveryReasons.WallParked)
        {
            severity = AlertSeverity.Critical;
            failureReason = ApiErrorRecoveryReasons.WallParked;
            message =
                $"Session {row.AgentSessionId} hit {_settings.WallDeathCap} consecutive usage-limit "
                + "walls; the resume is parked.";
        }
        else if (row.ResolvedReason == ApiErrorRecoveryReasons.WallModelPaused)
        {
            severity = AlertSeverity.Warning;
            failureReason = ApiErrorRecoveryReasons.WallModelPaused;
            var quoted = QuoteError(errorText);
            var session = await db.AgentSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == row.AgentSessionId, ct);
            var kind = session?.AgentKind ?? AgentKind.ClaudeCode;
            var alias = await ResolveFallbackAliasAsync(db, row.AgentSessionId, ct) ?? "unknown";
            var statusBit = row.ApiErrorStatus is int s ? $" HTTP {s}" : "";
            message =
                $"{kind} {alias}: session {row.AgentSessionId} hit a per-model usage cap{statusBit} "
                + $"(no reset stated); dispatch is paused for that model until cleared. {quoted}";
        }
        else if (row.Classification == ApiErrorClassification.Wall
            && row.ResolvedReason == WallUnparsedFailureReason)
        {
            severity = AlertSeverity.Warning;
            failureReason = WallUnparsedFailureReason;
            var quoted = QuoteError(errorText);
            message =
                $"Session {row.AgentSessionId} hit a usage-limit wall whose model could not be "
                + $"resolved; no hold was written and no 30-minute nudge will fire. Unparsed text: {quoted}";
        }
        else if (row.Classification == ApiErrorClassification.Wall)
        {
            severity = AlertSeverity.Warning;
            failureReason = "SessionLimit";
            var quoted = QuoteError(errorText);
            message =
                $"Session {row.AgentSessionId} hit a session-limit wall; one resume is scheduled "
                + $"at {row.NextAttemptAt:u} and dispatch is paused for that model until then. {quoted}";
        }
        else
        {
            severity = channelBound ? AlertSeverity.Critical : AlertSeverity.Warning;
            failureReason = row.Classification.ToString();
            message =
                $"Session {row.AgentSessionId} died on an API error ({row.Classification}: "
                + $"{row.ApiErrorClass ?? "no error class"}"
                + (row.ApiErrorStatus is int s ? $", HTTP {s}" : string.Empty)
                + "). A timed resume is scheduled.";
        }

        if (channelBound && severity != AlertSeverity.Critical)
            severity = AlertSeverity.Critical;

        await RecordSessionIncidentAsync(
            services, db, row.AgentSessionId, severity, message, failureReason, ct);
    }

    private async Task MaybeRaiseDeadTimeIncidentAsync(
        IServiceProvider services, AppDbContext db, ApiErrorRecovery recovery, DateTime now,
        CancellationToken ct)
    {
        if (recovery.Classification is not (ApiErrorClassification.Transient or ApiErrorClassification.Unknown))
            return;
        if (now - recovery.DetectedAt < TimeSpan.FromHours(Math.Max(1, _settings.DeadTimeWarningHours)))
            return;

        var already = await db.AgentIncidents.AsNoTracking().AnyAsync(
            i => i.SessionId == recovery.AgentSessionId
                && i.Kind == AgentIncidentKind.ApiErrorTurnDied
                && i.FailureReason == DeadTimeFailureReason, ct);
        if (already)
            return;

        var channelBound = await IsChannelBoundAsync(db, recovery.AgentSessionId, ct);
        var hours = (int)(now - recovery.DetectedAt).TotalHours;
        await RecordSessionIncidentAsync(
            services, db, recovery.AgentSessionId,
            channelBound ? AlertSeverity.Critical : AlertSeverity.Warning,
            $"Session {recovery.AgentSessionId} has been dead on a {recovery.Classification} API error "
            + $"for {hours}h (threshold {_settings.DeadTimeWarningHours}h). Resumes continue.",
            DeadTimeFailureReason, ct);
    }

    private async Task RecordSessionIncidentAsync(
        IServiceProvider services, AppDbContext db, Guid sessionId, AlertSeverity severity,
        string message, string failureReason, CancellationToken ct)
    {
        var sessionIdText = sessionId.ToString("D");
        var agentId = await db.Agents
            .Where(a => a.PersistentSessionId == sessionIdText)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        if (agentId is not Guid owner)
        {
            _logger.LogError(
                "No agent owns session {SessionId}, so the API-error death could not be recorded as an "
                + "incident. {Message}",
                sessionId, message);
            return;
        }

        var supervisor = services.GetService<AgentSupervisorService>();
        if (supervisor is null)
            return;

        await supervisor.RecordIncidentAsync(
            owner, sessionId, AgentIncidentKind.ApiErrorTurnDied, severity, message,
            failureReason: failureReason, ct: ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<bool> IsChannelBoundAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var sessionIdText = sessionId.ToString("D");
        var agentId = await db.Agents
            .Where(a => a.PersistentSessionId == sessionIdText)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (agentId is not Guid id)
            return false;
        return await db.ChatChannels.AsNoTracking().AnyAsync(c => c.AgentId == id, ct);
    }

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;

    private static bool IsUniqueAdoptionConflict(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_ApiErrorRecoveries_AgentSessionId_StubSequence", StringComparison.OrdinalIgnoreCase)
            || message.Contains("23505", StringComparison.Ordinal); // unique_violation
    }

    private readonly record struct StubRow(
        Guid AgentSessionId,
        long Sequence,
        string? Uuid,
        string? ApiErrorClass,
        int? ApiErrorStatus,
        string? Text,
        DateTime? Timestamp,
        DateTime CreatedAt);
}
