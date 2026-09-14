using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
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
/// CARD-0514: shared remote-control policy, maintenance reservation, idle-gated dismissal and
/// modal-episode persistence. Callers that already hold the per-session queue lock use the
/// UnderLock methods and must not re-enter <see cref="SessionMessageQueueService.GetLock"/>.
/// </summary>
public sealed class RemoteControlRecoveryService
{
    public const string EpisodeReasonPrefix = "rc-modal:";
    public const string EscPayload = "\u001b";
    public static readonly TimeSpan MenuSettleDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MenuDismissBudget = TimeSpan.FromSeconds(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionMessageQueueService _queue;
    private readonly ISessionRunnerClient _runner;
    private readonly IRcBridgeProbe? _probe;
    private readonly AgentSessionRuntime _runtime;
    private readonly ILaunchOwnership _launchOwnership;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly SupervisionSettings _settings;
    private readonly ILogger<RemoteControlRecoveryService> _logger;

    public RemoteControlRecoveryService(
        IServiceScopeFactory scopeFactory,
        SessionMessageQueueService queue,
        ISessionRunnerClient runner,
        AgentSessionRuntime runtime,
        ILaunchOwnership launchOwnership,
        IEventBus eventBus,
        TimeProvider timeProvider,
        IOptions<SupervisionSettings> settings,
        ILogger<RemoteControlRecoveryService> logger,
        IRcBridgeProbe? probe = null)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _runner = runner;
        _runtime = runtime;
        _launchOwnership = launchOwnership;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _settings = settings.Value;
        _logger = logger;
        _probe = probe;
    }

    /// <summary>
    /// Test seam: named persist point before SaveChanges. Return true to abort the persist.
    /// Production never sets this.
    /// </summary>
    public Func<string, Task<bool>>? BeforePersist { get; set; }

    /// <summary>Test seam: held during runner I/O after intent is committed. Production never sets this.</summary>
    public Func<Task>? HoldDuringIo { get; set; }

    public static string EpisodeReason(Guid episodeId) => $"{EpisodeReasonPrefix}{episodeId:D}";

    public static bool TryParseEpisodeReason(string? failureReason, out Guid episodeId)
    {
        episodeId = default;
        if (string.IsNullOrEmpty(failureReason) || !failureReason.StartsWith(EpisodeReasonPrefix, StringComparison.Ordinal))
            return false;
        return Guid.TryParse(failureReason[EpisodeReasonPrefix.Length..], out episodeId);
    }

    public async Task<bool> HasConditionalInputCapabilityAsync(CancellationToken ct)
    {
        try
        {
            var caps = await _runner.GetCapabilitiesAsync(ct);
            return caps?.Features is { } features
                && features.Contains(RunnerCapabilityFeatures.ConditionalMaintenanceInputV1, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Conditional-input capability probe failed");
            return false;
        }
    }

    public RemoteControlBridgeState ProbeBridge(int? pid)
    {
        if (_probe is null || pid is null)
            return RemoteControlBridgeState.Unknown;
        try
        {
            return RemoteControlBridgeClassifier.Classify(_probe.Probe(pid.Value), pid, probeFailed: false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RC bridge probe failed for pid {Pid}", pid);
            return RemoteControlBridgeClassifier.Classify(null, pid, probeFailed: true);
        }
    }

    public async Task<RemoteControlScreenObservation> ObserveAsync(Guid sessionId, DateTime expectedGeneration, CancellationToken ct)
    {
        SessionRunnerSnapshotDto snapshot;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var seconds = Math.Max(1, _settings.RcModalWatch.SnapshotTimeoutSeconds);
            timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
            snapshot = await _runner.GetSnapshotAsync(sessionId, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new RemoteControlScreenObservation(
                RemoteControlMenuScreen.Classify(null),
                null,
                false,
                0,
                "read-failure");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new RemoteControlScreenObservation(
                RemoteControlMenuScreen.Classify(null),
                null,
                false,
                0,
                "read-failure");
        }

        var proven = SessionGeneration.Equal(snapshot.AcceptedStartedAt, expectedGeneration);
        var reason = snapshot.AcceptedStartedAt is null
            ? "ObservationGenerationUnproven"
            : proven
                ? null
                : "generation-mismatch";
        return new RemoteControlScreenObservation(
            RemoteControlMenuScreen.Classify(snapshot.RenderedScreen),
            snapshot.AcceptedStartedAt,
            proven,
            snapshot.LastSequence,
            reason);
    }

    public async Task<bool> HasOpenModalBarrierAsync(Guid sessionId, DateTime generation, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RemoteControlModalEpisodes.AsNoTracking().AnyAsync(
            e => e.SessionId == sessionId
                && e.AcceptedStartedAt == SessionGeneration.Normalize(generation)
                && e.ResolvedAt == null, ct);
    }

    public async Task CloseGenerationEndedAsync(Guid sessionId, DateTime oldGeneration, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = SessionGeneration.Normalize(oldGeneration);
        var open = await db.RemoteControlModalEpisodes
            .Where(e => e.SessionId == sessionId && e.AcceptedStartedAt == normalized && e.ResolvedAt == null)
            .ToListAsync(ct);
        if (open.Count == 0)
            return;
        var now = UtcNow();
        foreach (var episode in open)
        {
            episode.ResolvedAt = now;
            episode.Resolution = RemoteControlEpisodeResolution.GenerationEnded;
            episode.LastTransition = nameof(RemoteControlEpisodeResolution.GenerationEnded);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task ReconcileLegacyRemoteControlRowsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unresolved = await db.SessionQueuedMessages
            .Where(m => m.MaintenanceKind == RemoteControlMaintenanceKind.None
                && (m.Status == QueuedMessageStatus.Pending || m.Status == QueuedMessageStatus.Sent)
                && m.DeliveryVerdict == null)
            .ToListAsync(ct);
        var now = UtcNow();
        var changed = 0;
        foreach (var row in unresolved)
        {
            if (!IsExactRemoteControlToken(row.Body))
                continue;
            row.MaintenanceKind = RemoteControlMaintenanceKind.LegacyUnclassified;
            row.MaintenanceEvidence = "legacy-unclassified";
            row.MaintenanceResultAt = now;
            changed++;
        }

        if (changed > 0)
            await db.SaveChangesAsync(ct);
    }

    public static bool IsExactRemoteControlToken(string body)
    {
        var trimmed = body.AsSpan().Trim();
        if (trimmed.IsEmpty)
            return false;
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
            end++;
        return trimmed[..end].Equals("/remote-control", StringComparison.Ordinal);
    }

    public async Task<Guid?> TryReserveAutomaticArmAsync(
        Guid sessionId,
        DateTime acceptedGeneration,
        QueuedMessageOrigin origin,
        CancellationToken ct)
    {
        if (origin is not (QueuedMessageOrigin.System or QueuedMessageOrigin.Supervision))
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Automatic RC must keep System or Supervision origin.");

        var generation = SessionGeneration.Normalize(acceptedGeneration);
        var sem = _queue.GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            return await ReserveAutomaticArmUnderLockAsync(sessionId, generation, origin, ct);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<Guid?> ReserveAutomaticArmUnderLockAsync(
        Guid sessionId,
        DateTime generation,
        QueuedMessageOrigin origin,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = SessionGeneration.Normalize(generation);

        if (await db.SessionQueuedMessages.AnyAsync(
                m => m.AgentSessionId == sessionId
                    && m.MaintenanceAcceptedStartedAt == normalized
                    && m.MaintenanceResult == RemoteControlArmResult.ArmUnconfirmed, ct))
            return null;

        var existing = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId && m.MaintenanceSlotActive)
            .OrderBy(m => m.Sequence)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing.Id;

        if (await BeforePersistInvoke("reservation", ct))
            return null;

        var now = UtcNow();
        var nextSequence = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId)
            .Select(m => (long?)m.Sequence)
            .MaxAsync(ct) ?? 0;
        var row = new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = "/remote-control",
            Status = QueuedMessageStatus.Pending,
            Sequence = nextSequence + 1,
            Origin = origin,
            CreatedAt = now,
            MaintenanceKind = RemoteControlMaintenanceKind.AutomaticArm,
            MaintenanceAcceptedStartedAt = normalized,
            MaintenanceResult = RemoteControlArmResult.Requested,
            MaintenanceResultAt = now,
            MaintenanceSlotActive = true,
        };
        db.SessionQueuedMessages.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == sessionId && m.MaintenanceSlotActive)
                .Select(m => m.Id)
                .FirstOrDefaultAsync(ct);
            return raced == Guid.Empty ? null : raced;
        }

        return row.Id;
    }

    public async Task<RemoteControlArmResult> ExecuteAutomaticArmUnderLockAsync(
        Guid sessionId, Guid requestId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.SessionQueuedMessages.FirstOrDefaultAsync(m => m.Id == requestId, ct);
        if (row is null || row.AgentSessionId != sessionId)
            return RemoteControlArmResult.WithheldUnknown;

        var session = await db.AgentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return await FinishArmAsync(db, row, RemoteControlArmResult.WithheldUnknown, "missing-session", ct);

        if (!RemoteControlPolicy.Permits(session.AgentKind))
            return await FinishArmAsync(db, row, RemoteControlArmResult.WithheldCapability, "kind-not-capable", ct);

        var currentGeneration = SessionGeneration.Normalize(session.StartedAt);
        if (row.MaintenanceAcceptedStartedAt is not { } stamped
            || !SessionGeneration.Equal(stamped, currentGeneration))
            return await FinishArmAsync(db, row, RemoteControlArmResult.SupersededGeneration, "generation-mismatch", ct);

        if (session.Status == SessionStatus.Stopping)
            return await FinishArmAsync(db, row, RemoteControlArmResult.WithheldStopping, "stopping", ct);

        if (row.Origin == QueuedMessageOrigin.Supervision && _launchOwnership.Owns(sessionId))
            return await FinishArmAsync(db, row, RemoteControlArmResult.WithheldLaunchOwner, "launch-owner", ct, closeSlot: false);

        if (!await HasConditionalInputCapabilityAsync(ct))
            return await FinishArmAsync(db, row, RemoteControlArmResult.WithheldTransport, "conditional-input-unsupported", ct);

        var pid = TryPid(sessionId);
        var bridge = ProbeBridge(pid);
        if (bridge == RemoteControlBridgeState.Armed)
            return await FinishArmAsync(db, row, RemoteControlArmResult.SuppressedAlreadyArmed, "already-armed", ct);
        if (bridge == RemoteControlBridgeState.Unknown)
            return await FinishArmAsync(db, row, RemoteControlArmResult.WithheldUnknown, "probe-unknown", ct);

        var observation = await ObserveAsync(sessionId, currentGeneration, ct);
        if (observation.Menu.IsPresent)
            return await FinishArmAsync(db, row, RemoteControlArmResult.WithheldMenuPresent, "menu-present", ct);

        await _runtime.CatchUpTranscriptAsync(sessionId, ct);
        if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct))
            return await FinishArmAsync(db, row, RemoteControlArmResult.WithheldNotIdle, "not-idle", ct, closeSlot: false);

        if (await BeforePersistInvoke("submission-started", ct))
            return RemoteControlArmResult.Requested;

        row.MaintenanceResult = RemoteControlArmResult.SubmissionStarted;
        row.MaintenanceResultAt = UtcNow();
        row.SubmissionStartedAt = UtcNow();
        await db.SaveChangesAsync(ct);

        if (HoldDuringIo is not null)
            await HoldDuringIo();

        var sequence = observation.LastSequence;
        var bodyWrite = await SendGuardedAsync(
            sessionId, currentGeneration, sequence, "/remote-control", ct);
        if (bodyWrite.Outcome != ConditionalInputOutcomes.Written)
            return await FinishArmAsync(db, row, MapWriteOutcome(bodyWrite.Outcome), bodyWrite.Outcome, ct, unconfirmed: bodyWrite.Outcome == ConditionalInputOutcomes.Unknown);

        if (!await WaitComposerAsync(sessionId, "/remote-control", ct))
            return await FinishArmAsync(db, row, RemoteControlArmResult.ArmUnconfirmed, "no-composer", ct, unconfirmed: true);

        var enterWrite = await SendGuardedAsync(sessionId, currentGeneration, bodyWrite.LastSequence ?? sequence, "\r", ct);
        if (enterWrite.Outcome != ConditionalInputOutcomes.Written)
            return await FinishArmAsync(db, row, MapWriteOutcome(enterWrite.Outcome), enterWrite.Outcome, ct, unconfirmed: enterWrite.Outcome == ConditionalInputOutcomes.Unknown);

        var after = ProbeBridge(TryPid(sessionId));
        if (after == RemoteControlBridgeState.Armed)
            return await FinishArmAsync(db, row, RemoteControlArmResult.ArmedObserved, "armed-observed", ct);

        return await FinishArmAsync(db, row, RemoteControlArmResult.ArmUnconfirmed, "arm-unconfirmed", ct, unconfirmed: true);
    }

    public async Task<RemoteControlModalEpisode?> DetectAsync(
        Guid sessionId, DateTime generation, RemoteControlScreenObservation observation, Guid? relatedQueueId, CancellationToken ct)
    {
        if (!observation.GenerationProven)
            return null;
        if (!observation.Menu.IsPresent && !observation.Menu.HasRemnant)
            return null;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = SessionGeneration.Normalize(generation);
        var existing = await db.RemoteControlModalEpisodes
            .FirstOrDefaultAsync(e => e.SessionId == sessionId && e.AcceptedStartedAt == normalized && e.ResolvedAt == null, ct);
        var now = SessionGeneration.Normalize(UtcNow());
        if (existing is not null)
        {
            existing.LastObservedAt = now;
            existing.AfterOutputSequence = observation.LastSequence;
            if (relatedQueueId is not null && existing.RelatedMaintenanceQueueId is null)
                existing.RelatedMaintenanceQueueId = relatedQueueId;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        if (!observation.Menu.IsPresent)
            return null;

        if (await BeforePersistInvoke("detection", ct))
            return null;

        var session = await db.AgentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var channelBound = session is not null && await IsChannelBoundAsync(db, session, ct);
        var episode = new RemoteControlModalEpisode
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            AcceptedStartedAt = normalized,
            FirstObservedAt = now,
            LastObservedAt = now,
            ChildPid = TryPid(sessionId),
            BeforeOutputSequence = observation.LastSequence,
            AfterOutputSequence = observation.LastSequence,
            RelatedMaintenanceQueueId = relatedQueueId,
            ChannelBound = channelBound,
            LastTransition = nameof(AgentIncidentKind.RemoteControlModalDetected),
            DismissalResult = RemoteControlDismissalResult.DetectionOnly,
        };
        db.RemoteControlModalEpisodes.Add(episode);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return await db.RemoteControlModalEpisodes
                .FirstOrDefaultAsync(e => e.SessionId == sessionId && e.AcceptedStartedAt == normalized && e.ResolvedAt == null, ct);
        }

        await RecordEpisodeIncidentAsync(
            db, sessionId, episode, AgentIncidentKind.RemoteControlModalDetected,
            channelBound ? AlertSeverity.Critical : AlertSeverity.Warning,
            "Remote Control menu blocks input",
            ct);
        return episode;
    }

    public async Task<RemoteControlDismissalResult> TryDismissIdleUnderLockAsync(
        Guid sessionId, Guid episodeId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var episode = await db.RemoteControlModalEpisodes.FirstOrDefaultAsync(e => e.Id == episodeId, ct);
        if (episode is null || episode.ResolvedAt is not null)
            return RemoteControlDismissalResult.ObservedClear;
        if (episode.DismissalIntentAt is not null)
            return episode.DismissalResult ?? RemoteControlDismissalResult.EscSentUnverified;

        await _runtime.CatchUpTranscriptAsync(sessionId, ct);
        var working = await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct);
        episode.TranscriptWorking = working;
        var bound = _runtime.TryGetLiveMetadata(sessionId, out var meta) && meta.TranscriptBound == true;
        if (!bound)
        {
            episode.DismissalResult = RemoteControlDismissalResult.WithheldUnboundTranscript;
            await db.SaveChangesAsync(ct);
            return RemoteControlDismissalResult.WithheldUnboundTranscript;
        }

        if (working)
        {
            episode.DismissalResult = RemoteControlDismissalResult.WithheldWorking;
            await db.SaveChangesAsync(ct);
            return RemoteControlDismissalResult.WithheldWorking;
        }

        var observation = await ObserveAsync(sessionId, episode.AcceptedStartedAt, ct);
        if (!observation.GenerationProven)
        {
            episode.DismissalResult = RemoteControlDismissalResult.WithheldUnprovenGeneration;
            await db.SaveChangesAsync(ct);
            return RemoteControlDismissalResult.WithheldUnprovenGeneration;
        }

        if (observation.Menu.HasRemnant && !observation.Menu.IsPresent)
        {
            episode.DismissalResult = RemoteControlDismissalResult.WithheldPartialScreen;
            await db.SaveChangesAsync(ct);
            return RemoteControlDismissalResult.WithheldPartialScreen;
        }

        if (!observation.Menu.IsPresent)
        {
            episode.ResolvedAt = UtcNow();
            episode.Resolution = RemoteControlEpisodeResolution.ObservedClear;
            episode.DismissalResult = RemoteControlDismissalResult.ObservedClear;
            episode.LastTransition = nameof(RemoteControlEpisodeResolution.ObservedClear);
            await db.SaveChangesAsync(ct);
            return RemoteControlDismissalResult.ObservedClear;
        }

        if (!await HasConditionalInputCapabilityAsync(ct))
        {
            episode.DismissalResult = RemoteControlDismissalResult.WithheldUnsupportedTransport;
            await db.SaveChangesAsync(ct);
            return RemoteControlDismissalResult.WithheldUnsupportedTransport;
        }

        if (await BeforePersistInvoke("esc-intent", ct))
            return RemoteControlDismissalResult.DetectionOnly;

        episode.DismissalIntentAt = UtcNow();
        episode.DismissalResult = RemoteControlDismissalResult.EscSentUnverified;
        await db.SaveChangesAsync(ct);

        if (HoldDuringIo is not null)
            await HoldDuringIo();

        var write = await SendGuardedAsync(
            sessionId, episode.AcceptedStartedAt, observation.LastSequence, EscPayload, ct);
        if (write.Outcome != ConditionalInputOutcomes.Written)
        {
            if (write.Outcome is ConditionalInputOutcomes.StaleObservation or ConditionalInputOutcomes.GenerationMismatch)
            {
                episode.DismissalIntentAt = null;
                episode.DismissalResult = write.Outcome == ConditionalInputOutcomes.GenerationMismatch
                    ? RemoteControlDismissalResult.GenerationMismatch
                    : RemoteControlDismissalResult.WithheldStaleSnapshot;
                await db.SaveChangesAsync(ct);
                return episode.DismissalResult.Value;
            }

            episode.DismissalSentAt = UtcNow();
            episode.Resolution = RemoteControlEpisodeResolution.DismissUnverified;
            await db.SaveChangesAsync(ct);
            return RemoteControlDismissalResult.EscSentUnverified;
        }

        episode.DismissalSentAt = UtcNow();
        await db.SaveChangesAsync(ct);

        var budget = UtcNow() + MenuDismissBudget;
        RemoteControlScreenObservation? firstClear = null;
        while (UtcNow() < budget)
        {
            await Task.Delay(MenuSettleDelay, _timeProvider, ct);
            RemoteControlScreenObservation post;
            try
            {
                post = await ObserveAsync(sessionId, episode.AcceptedStartedAt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                episode.DismissalResult = RemoteControlDismissalResult.EscSentUnverified;
                episode.Resolution = RemoteControlEpisodeResolution.DismissUnverified;
                await db.SaveChangesAsync(ct);
                return RemoteControlDismissalResult.EscSentUnverified;
            }

            if (!post.GenerationProven)
            {
                episode.DismissalResult = RemoteControlDismissalResult.GenerationMismatch;
                await db.SaveChangesAsync(ct);
                return RemoteControlDismissalResult.GenerationMismatch;
            }

            if (!post.Menu.IsClear)
            {
                firstClear = null;
                if (UtcNow() >= budget)
                    break;
                continue;
            }

            if (firstClear is null)
            {
                firstClear = post;
                continue;
            }

            episode.DismissalVerifiedAt = UtcNow();
            episode.DismissalResult = RemoteControlDismissalResult.DismissedVerified;
            episode.ResolvedAt = episode.DismissalVerifiedAt;
            episode.Resolution = RemoteControlEpisodeResolution.DismissedVerified;
            episode.AfterOutputSequence = post.LastSequence;
            episode.LastTransition = nameof(RemoteControlEpisodeResolution.DismissedVerified);
            await db.SaveChangesAsync(ct);
            await RecordEpisodeIncidentAsync(
                db, sessionId, episode, AgentIncidentKind.RemoteControlModalDismissed,
                AlertSeverity.Info, "Remote Control menu dismissed after verified idle Esc", ct);
            return RemoteControlDismissalResult.DismissedVerified;
        }

        episode.DismissalResult = RemoteControlDismissalResult.EscSentUnverified;
        episode.Resolution = RemoteControlEpisodeResolution.DismissUnverified;
        await db.SaveChangesAsync(ct);
        return RemoteControlDismissalResult.EscSentUnverified;
    }

    private async Task<bool> WaitComposerAsync(Guid sessionId, string body, CancellationToken ct)
    {
        var deadline = UtcNow() + TimeSpan.FromSeconds(Math.Max(1, _settings.DeliveryVerification.EvidenceTimeoutSeconds));
        while (UtcNow() < deadline)
        {
            if (_runtime.TryGetLiveSnapshot(sessionId, out var snap)
                && snap.RenderedScreen.Contains(body, StringComparison.Ordinal))
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(50), _timeProvider, ct);
        }

        return _runtime.TryGetLiveSnapshot(sessionId, out var last)
            && last.RenderedScreen.Contains(body, StringComparison.Ordinal);
    }

    private async Task<RunnerConditionalInputResult> SendGuardedAsync(
        Guid sessionId, DateTime generation, long sequence, string input, CancellationToken ct)
    {
        try
        {
            return await _runtime.SendConditionalInputAsync(
                sessionId,
                new RunnerConditionalInputRequest(generation, sequence, input),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Conditional RC write failed for session {SessionId}", sessionId);
            return new RunnerConditionalInputResult(sessionId, ConditionalInputOutcomes.Unknown, generation, sequence);
        }
    }

    private static RemoteControlArmResult MapWriteOutcome(string outcome) =>
        outcome switch
        {
            ConditionalInputOutcomes.GenerationMismatch => RemoteControlArmResult.SupersededGeneration,
            ConditionalInputOutcomes.Unsupported => RemoteControlArmResult.WithheldTransport,
            ConditionalInputOutcomes.StaleObservation => RemoteControlArmResult.ArmUnconfirmed,
            ConditionalInputOutcomes.Missing or ConditionalInputOutcomes.Exited => RemoteControlArmResult.WithheldUnknown,
            ConditionalInputOutcomes.Unknown => RemoteControlArmResult.ArmUnconfirmed,
            _ => RemoteControlArmResult.ArmUnconfirmed,
        };

    private async Task<RemoteControlArmResult> FinishArmAsync(
        AppDbContext db,
        SessionQueuedMessage row,
        RemoteControlArmResult result,
        string evidence,
        CancellationToken ct,
        bool unconfirmed = false,
        bool closeSlot = true)
    {
        if (await BeforePersistInvoke("arm-result", ct))
            return row.MaintenanceResult ?? RemoteControlArmResult.Requested;

        row.MaintenanceResult = unconfirmed ? RemoteControlArmResult.ArmUnconfirmed : result;
        row.MaintenanceResultAt = UtcNow();
        row.MaintenanceEvidence = Truncate(evidence, 500);
        if (closeSlot)
            row.MaintenanceSlotActive = false;
        if (row.MaintenanceResult is RemoteControlArmResult.ArmedObserved
            or RemoteControlArmResult.SuppressedAlreadyArmed
            or RemoteControlArmResult.SupersededGeneration
            or RemoteControlArmResult.WithheldUnknown
            or RemoteControlArmResult.WithheldMenuPresent
            or RemoteControlArmResult.WithheldCapability
            or RemoteControlArmResult.WithheldTransport
            or RemoteControlArmResult.ArmUnconfirmed)
        {
            row.Status = QueuedMessageStatus.Sent;
            row.SentAt ??= UtcNow();
        }

        await db.SaveChangesAsync(ct);
        return row.MaintenanceResult ?? result;
    }

    private async Task RecordEpisodeIncidentAsync(
        AppDbContext db,
        Guid sessionId,
        RemoteControlModalEpisode episode,
        AgentIncidentKind kind,
        AlertSeverity severity,
        string message,
        CancellationToken ct)
    {
        var agentId = await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId == sessionId.ToString("D"))
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        var recorder = _scopeFactory.CreateScope().ServiceProvider.GetService<IAgentIncidentRecorder>();
        if (recorder is not null)
        {
            await recorder.RecordIncidentAsync(
                agentId, sessionId, kind, severity, message,
                failureReason: EpisodeReason(episode.Id), raiseAlert: severity >= AlertSeverity.Warning, ct: ct);
        }
        else
        {
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                SessionId = sessionId,
                Kind = kind,
                Severity = severity,
                Message = message,
                FailureReason = EpisodeReason(episode.Id),
                CreatedAt = UtcNow(),
            });
            await db.SaveChangesAsync(ct);
        }

        episode.LastIncidentAt = UtcNow();
        await db.SaveChangesAsync(ct);
        if (agentId is Guid id)
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(id), ct);
    }

    private static async Task<bool> IsChannelBoundAsync(AppDbContext db, AgentSession session, CancellationToken ct)
    {
        var agentId = await db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId == session.Id.ToString("D"))
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (agentId is not Guid id)
            return false;
        return await db.ChatChannels.AsNoTracking().AnyAsync(ch => ch.AgentId == id, ct);
    }

    private int? TryPid(Guid sessionId)
    {
        try
        {
            if (_runtime.TryGetLiveMetadata(sessionId, out _))
            {
                var dto = _runner.GetAsync(sessionId, CancellationToken.None).GetAwaiter().GetResult();
                return dto.Pid;
            }
        }
        catch
        {
            // Probe absence is Unknown, never Unarmed.
        }

        return null;
    }

    private async Task<bool> BeforePersistInvoke(string name, CancellationToken ct)
    {
        if (BeforePersist is null)
            return false;
        var abort = await BeforePersist(name);
        ct.ThrowIfCancellationRequested();
        return abort;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}

public sealed record RemoteControlScreenObservation(
    RemoteControlMenuScreen.Classification Menu,
    DateTime? ObservedGeneration,
    bool GenerationProven,
    long LastSequence,
    string? Reason);
