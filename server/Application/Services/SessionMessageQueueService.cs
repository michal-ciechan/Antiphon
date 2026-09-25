using System.Collections.Concurrent;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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

/// <summary>Late-confirmation evidence produced while processing one turn-end boundary.</summary>
public sealed record SessionQueueTurnEndResult(
    IReadOnlySet<Guid> LateConfirmedMessageIds,
    IReadOnlySet<Guid> LateConfirmedChannelMessageIds);

/// <summary>
/// Manages messages queued to a live agent session. "Send now" delivers immediately; "wait until idle"
/// holds the message until the agent reaches a turn-end (<c>stop_reason: end_turn</c>), then delivers the
/// oldest pending message — one per turn. When a turn ends with an empty queue the session is considered
/// completely finished and a <c>SessionFinished</c> signal is broadcast (badge + notification).
///
/// Singleton: it owns per-session flush locks and is invoked from the (singleton) <see cref="AgentSessionRuntime"/>
/// transcript observer. DB access is via a scope per operation, mirroring the runtime's own pattern.
/// </summary>
public sealed partial class SessionMessageQueueService
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentSessionRuntime _runtime;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly DeliveryVerificationSettings _verification;
    private readonly Settings.ChannelBridgeSettings _bridgeSettings;
    private readonly DelegationSettings _delegationSettings;
    private readonly PtyDeliveryProfile? _ptyProfile;
    private readonly RemoteSpillCourier? _remoteSpills;
    private readonly SessionDeliveryProfile? _sessionProfile;
    private readonly ILogger<SessionMessageQueueService> _logger;
    private readonly CapacityRecoveryService? _capacityRecovery;

    public SessionMessageQueueService(
        IServiceScopeFactory scopeFactory,
        AgentSessionRuntime runtime,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<SessionMessageQueueService> logger,
        IOptions<SupervisionSettings>? supervisionSettings = null,
        IOptions<Settings.ChannelBridgeSettings>? bridgeSettings = null,
        IOptions<DelegationSettings>? delegationSettings = null,
        PtyDeliveryProfile? ptyProfile = null,
        SessionDeliveryProfile? sessionProfile = null,
        CapacityRecoveryService? capacityRecovery = null,
        // CARD-0604 G-21. Absent, a remote spill still never writes a desktop file; it simply
        // types the whole body instead, which is the safe direction.
        RemoteSpillCourier? remoteSpills = null)
    {
        _remoteSpills = remoteSpills;
        _ptyProfile = ptyProfile;
        _sessionProfile = sessionProfile;
        _scopeFactory = scopeFactory;
        _runtime = runtime;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _verification = (supervisionSettings?.Value ?? new SupervisionSettings()).DeliveryVerification;
        _bridgeSettings = bridgeSettings?.Value ?? new Settings.ChannelBridgeSettings();
        _delegationSettings = delegationSettings?.Value ?? new DelegationSettings();
        _logger = logger;
        _capacityRecovery = capacityRecovery;
    }

    /// <summary>
    /// Process-wide pty ceilings (CARD-0037) — no-profile / test fallback. Delivery paths that
    /// know a session id resolve via <see cref="CeilingsForSessionAsync"/> (CARD-0161).
    /// </summary>
    private PtyDeliveryCeilings Ceilings =>
        _ptyProfile?.Ceilings
        ?? _delegationSettings.CeilingsFor(PtyBackend.InboxConhost, "no pty profile — assuming the default backend");

    /// <summary>CARD-0161: per-session ceilings. Falls back to process-wide when no session profile.</summary>
    private async Task<PtyDeliveryCeilings> CeilingsForSessionAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        if (_sessionProfile is null)
            return Ceilings;
        return await _sessionProfile.ForSessionAsync(db, sessionId, ct);
    }

    private static void ValidateMentionIdentity(SessionQueuedMessage existing, Guid sessionId, string? digest)
    {
        if (existing.AgentSessionId != sessionId || existing.Origin != QueuedMessageOrigin.Mention
            || existing.ContentDigest != digest)
            throw new ConflictException("Mention occurrence identity has a different destination or body.");
    }

    private string NowModeFileStem() =>
        $"now-{_timeProvider.GetUtcNow().UtcDateTime:yyyyMMddHHmmss}";

    /// <summary>
    /// CARD-0025: if <paramref name="body"/> is over the single-write envelope, write it to
    /// <c>{cwd}/.antiphon/inbox/{fileStem}.md</c> and return the pointer to type. Under the
    /// ceiling, or if the file cannot be written (empty cwd, IO error), the original is
    /// returned so <see cref="DeliverAsync"/>'s tripwire still fires.
    /// </summary>
    /// <summary>
    /// CARD-0647. Stage a brief the dispatcher already fitted for a runner session. The Input
    /// frame that types the pointer writes the body inside <paramref name="runnerCwd"/>.
    /// </summary>
    internal void StageRemoteSpill(Guid sessionId, string runnerCwd, PhoneHomeInputSpill spill)
    {
        if (_remoteSpills is null || string.IsNullOrWhiteSpace(runnerCwd) || string.IsNullOrWhiteSpace(spill.RelativePath))
            return;
        _remoteSpills.Stage(sessionId, runnerCwd, spill);
    }

    private RemoteSpillCourier.StagedSpill? BindStagedSpill(Guid sessionId, SessionQueuedMessage row)
    {
        if (_remoteSpills is null || !_remoteSpills.TryPeek(sessionId, row.Body, out var staged))
            return null;
        var relative = TypedBodySpill.InboxRelativePath(row.Id.ToString("D"));
        row.Body = row.Body.Replace(staged.Spill.RelativePath, relative, StringComparison.Ordinal);
        row.RemoteSpillBody = staged.Spill.Body;
        row.RemoteSpillRelativePath = relative;
        return staged;
    }

    private void BindGeneratedSpill(Guid sessionId, SessionQueuedMessage row, string wire)
    {
        if (_remoteSpills is null || !_remoteSpills.TryPeek(sessionId, wire, out var staged))
            return;
        var relative = TypedBodySpill.InboxRelativePath(row.Id.ToString("D"));
        if (!string.Equals(staged.Spill.RelativePath, relative, StringComparison.Ordinal))
            return;
        row.RemoteSpillBody = staged.Spill.Body;
        row.RemoteSpillRelativePath = relative;
        _remoteSpills.Ack(sessionId, staged);
    }

    /// <summary>
    /// A spill pointer with no stored bytes is recovered from the source text when that text is
    /// still here, or from the frozen Completion batch when the pointer names one. Otherwise the
    /// row is canceled. False means the pointer must not be typed.
    /// </summary>
    private async Task<bool> HoldSpillBodyForDeliveryAsync(
        AppDbContext db, Guid sessionId, SessionQueuedMessage row, string wire, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(row.RemoteSpillBody) || !IsOwnedSpillPointer(wire, row.Id))
            return true;
        if (await RemoteSpillCourier.HasCompleteMatchingUserPromptAsync(db, sessionId, wire, ct))
            return true;

        var frozen = await RemoteSpillCourier.TryComposeFrozenSpillAsync(db, row, ct);
        if (frozen.Kind == FrozenSpillCompose.Composed)
        {
            await PersistHeldSpillAsync(db, sessionId, row, frozen.Body!, ct);
            return true;
        }

        if (frozen.Kind == FrozenSpillCompose.Incomplete)
        {
            await RemoteSpillCourier.CancelUndeliverableAsync(db, row, frozen.MemberIds, ct);
            return false;
        }

        string? staged = null;
        if (_remoteSpills is not null && _remoteSpills.TryPeek(sessionId, wire, out var peek))
            staged = peek.Spill.Body;
        var repair = RemoteSpillCourier.Inspect(row, staged);
        if (repair.Kind == SpillBodyRepairKind.Recovered)
        {
            await PersistHeldSpillAsync(db, sessionId, row, repair.Body!, ct);
            return true;
        }

        if (repair.Kind != SpillBodyRepairKind.Undeliverable)
            return true;
        await RemoteSpillCourier.CancelUndeliverableAsync(db, row, [], ct);
        return false;
    }

    private async Task PersistHeldSpillAsync(
        AppDbContext db, Guid sessionId, SessionQueuedMessage row, string body, CancellationToken ct)
    {
        var relative = row.RemoteSpillRelativePath
            ?? TypedBodySpill.InboxRelativePath(row.Id.ToString("D"));
        row.RemoteSpillBody = body;
        row.RemoteSpillRelativePath = relative;
        if (_remoteSpills is null)
            return;
        var cwd = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.RunnerCwd)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(cwd))
            return;
        _remoteSpills.Stage(sessionId, cwd, new PhoneHomeInputSpill(relative, body, row.Id));
    }

    private static bool IsOwnedSpillPointer(string? body, Guid messageId) =>
        !string.IsNullOrEmpty(body)
        && body.Contains(TypedBodySpill.InboxRelativePath(messageId.ToString("D")), StringComparison.Ordinal);

    internal async Task<string> SpillQueueBodyAsync(
        Guid sessionId,
        string body,
        string fileStem,
        string? channelEnvelope,
        AppDbContext? db,
        CancellationToken ct,
        PtyDeliveryCeilings? ceilings = null, string? specialistInputPolicyJson = null)
    {
        ceilings ??= db is not null
            ? await CeilingsForSessionAsync(db, sessionId, ct)
            : Ceilings;
        if (SpecialistInputPolicy.Read(specialistInputPolicyJson) is { } inputPolicy)
        {
            if (db is null)
                throw new SpecialistInputUnsupportedException("Specialist input requires a durable queue owner.");
            var inputSession = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId, ct);
            inputPolicy.RequireSession(inputSession);
            return inputPolicy.Fit(body, inputSession.AgentKind, ceilings.Backend);
        }
        if (System.Text.Encoding.UTF8.GetByteCount(body) <= ceilings.SingleWriteMaxBytes)
            return body;

        string? cwd = null;
        string? runnerCwd = null;
        var kind = AgentKind.ClaudeCode;
        if (db is not null)
        {
            var session = await db.AgentSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.Cwd, s.AgentKind, s.RunnerCwd })
                .FirstOrDefaultAsync(ct);
            cwd = session?.Cwd;
            runnerCwd = session?.RunnerCwd;
            kind = session?.AgentKind ?? AgentKind.ClaudeCode;
        }
        else
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var scoped = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await scoped.AgentSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.Cwd, s.AgentKind, s.RunnerCwd })
                .FirstOrDefaultAsync(ct);
            cwd = session?.Cwd;
            runnerCwd = session?.RunnerCwd;
            kind = session?.AgentKind ?? AgentKind.ClaudeCode;
        }

        var relative = TypedBodySpill.InboxRelativePath(fileStem);

        // CARD-0604 G-21. A REMOTE session's file is written by the RUNNER, inside the session's
        // own cwd, and the body travels with the Input frame that types the pointer. The desktop
        // writes nothing: its Cwd is a Windows path the session cannot see, so a desktop write
        // would leave a file no one reads behind a prompt pointing at nothing.
        if (!string.IsNullOrWhiteSpace(runnerCwd))
        {
            var fit = TypedBodySpill.Fit(new TypedBodySpill.Request(
                Body: body,
                CeilingBytes: ceilings.SingleWriteMaxBytes,
                // No absolute path: nothing is written here.
                AbsoluteSpillPath: null,
                RelativeSpillPath: relative,
                AgentKind: kind,
                EnvelopePrefix: channelEnvelope,
                // The relative path IS where the runner will put it, so the pointer is correct
                // even though this process never touched a filesystem.
                ApiFallback: relative,
                Logger: _logger));
            if (fit.Spilled)
                _remoteSpills?.Stage(sessionId, runnerCwd!, new PhoneHomeInputSpill(relative, body));
            return fit.ToType;
        }

        string? absolute = null;
        if (!string.IsNullOrWhiteSpace(cwd))
            absolute = TypedBodySpill.InboxAbsolutePath(cwd, fileStem);

        return TypedBodySpill.Fit(new TypedBodySpill.Request(
            Body: body,
            CeilingBytes: ceilings.SingleWriteMaxBytes,
            AbsoluteSpillPath: absolute,
            RelativeSpillPath: relative,
            AgentKind: kind,
            EnvelopePrefix: channelEnvelope,
            Logger: _logger)).ToType;
    }

    /// <summary>
    /// Typing attempts a queued message gets before it parks for a human (CARD-0055). Floored at 1:
    /// a misconfigured 0 would park every message on creation, i.e. deliver nothing at all.
    /// </summary>
    private int MaxAttempts => Math.Max(1, _verification.MaxDeliveryAttempts);

    /// <summary>
    /// Queue a message ("wait until idle") or deliver it immediately ("send now").
    /// When a WhenIdle row is persisted, <paramref name="onCreated"/> receives its id
    /// (CARD-0248 — the closing-line nudge records this so settle-anyway can wait on SentAt).
    /// Mode.Now creates no row and does not invoke it.
    /// </summary>
    public Task<SessionQueueDto> EnqueueAsync(
        Guid sessionId, string body, MessageSendMode mode, CancellationToken ct,
        QueuedMessageOrigin origin = QueuedMessageOrigin.Ui, string? conversationKey = null,
        Guid? sourceTaskId = null, string? contentDigest = null, string? noteHeader = null,
        Guid? sourceScheduleId = null,
        Action<Guid>? onCreated = null,
        // CARD-0312 S2. WhenIdle on a live, idle session delivers INLINE, which is right for a
        // note somebody is waiting on and wrong for a body enqueued from inside a launch: the
        // delivery's own verification budget then runs on the launch's thread and holds the
        // launch-queue slot (measured — it timed out HerdrAlwaysOnChannelParityTests' launches).
        // false enqueues the row and leaves delivery to the machinery that already exists: the
        // turn-end flush, or FlushStrandedQueuesAsync within StrandedAgeSeconds on an always-on
        // session. Ignored by Mode.Now, which has no row at all.
        bool deliverIfIdle = true,
        DateTime? holdUntil = null,
        DateTime? executionDeadlineAt = null, Guid? executionTaskId = null,
        string? capacityRecoveryActionKey = null,
        Guid? capacityWaitId = null,
        Guid? sourceLandNotificationId = null,
        Func<Guid, CancellationToken, Task>? afterLandQueueInsert = null)
        => EnqueueCoreAsync(sessionId, body, mode, origin, conversationKey, sourceTaskId,
            contentDigest, noteHeader, sourceScheduleId, onCreated, deliverIfIdle, holdUntil,
            executionDeadlineAt, executionTaskId, capacityRecoveryActionKey, capacityWaitId,
            sourceLandNotificationId, afterLandQueueInsert, ct: ct);

    internal async Task<Guid> EnqueueMentionAsync(Guid sessionId, string body, Guid occurrenceId, CancellationToken ct)
    {
        if (occurrenceId == Guid.Empty)
            throw new ValidationException(nameof(occurrenceId), "A mention occurrence must have an identity.");
        var normalized = (body ?? string.Empty).Trim().ReplaceLineEndings("\n");
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalized)));
        var accepted = false;
        try
        {
            await EnqueueCoreAsync(sessionId, normalized, MessageSendMode.WhenIdle,
                origin: QueuedMessageOrigin.Mention, contentDigest: digest, deliverIfIdle: false,
                onCreated: _ => accepted = true, mentionOccurrenceId: occurrenceId, ct: ct);
        }
        catch (Exception ex) when (accepted)
        {
            _logger.LogWarning(ex, "Mention {MessageId} was accepted; queue activity refresh failed", occurrenceId);
        }
        return occurrenceId;
    }

    private async Task<SessionQueueDto> EnqueueCoreAsync(
        Guid sessionId, string body, MessageSendMode mode,
        QueuedMessageOrigin origin = QueuedMessageOrigin.Ui, string? conversationKey = null,
        Guid? sourceTaskId = null, string? contentDigest = null, string? noteHeader = null,
        Guid? sourceScheduleId = null,
        Action<Guid>? onCreated = null,
        bool deliverIfIdle = true,
        DateTime? holdUntil = null,
        DateTime? executionDeadlineAt = null, Guid? executionTaskId = null,
        string? capacityRecoveryActionKey = null,
        Guid? capacityWaitId = null,
        Guid? sourceLandNotificationId = null,
        Func<Guid, CancellationToken, Task>? afterLandQueueInsert = null,
        Guid? mentionOccurrenceId = null, CancellationToken ct = default)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ValidationException(nameof(body), "Message must not be empty.");

        var session = await RequireSessionAsync(sessionId, ct);
        var kind = session.AgentKind;
        string? specialistInputPolicyJson = null;
        if (executionTaskId is { } inputTaskId)
        {
            await using var inputScope = _scopeFactory.CreateAsyncScope();
            var inputDb = inputScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inputTask = await inputDb.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == inputTaskId, ct);
            specialistInputPolicyJson = inputTask?.SpecialistInputPolicyJson;
            if (SpecialistInputPolicy.Read(specialistInputPolicyJson) is { } policy)
            {
                if (mode != MessageSendMode.WhenIdle || !AgentTaskRoles.CarriesFullInlineInput(inputTask!.Role)
                    || policy.TaskId != inputTaskId || origin != QueuedMessageOrigin.Delegation)
                    throw new SpecialistInputUnsupportedException("Specialist input requires its durable Check brief queue.");
                trimmed = await SpillQueueBodyAsync(sessionId, trimmed, "", null, inputDb, ct,
                    specialistInputPolicyJson: specialistInputPolicyJson);
            }
        }
        if (TryGetForbiddenReason(kind, trimmed, out var forbiddenReason))
            throw new ValidationException(nameof(body), forbiddenReason);

        if (mode == MessageSendMode.Now)
        {
            await RequireImmediateAdmissionAsync(session, ct);

            // CARD-0161: resolve ceilings + blocked defer for send-now (same as flush).
            await using (var preScope = _scopeFactory.CreateAsyncScope())
            {
                var preDb = preScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var nowCeilings = await CeilingsForSessionAsync(preDb, sessionId, ct);
                if (nowCeilings.Backend == DeliveryBackend.HerdrPane
                    && _runtime.TryGetLiveMetadata(sessionId, out var nowMeta)
                    && string.Equals(nowMeta.AgentStatus, "blocked", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConflictException(
                        $"Agent session '{sessionId}' is blocked in herdr (permission/approval UI); try again when idle.");
                }

                if (_runtime.TryGetLiveMetadata(sessionId, out var pendingMeta)
                    && string.Equals(pendingMeta.Pending, HerdrPendingReasons.Unreachable, StringComparison.Ordinal))
                {
                    throw new ConflictException(
                        $"Agent session '{sessionId}' cannot accept input because herdr is unreachable; try again when it is back.");
                }
            }

            // CARD-0137 S7: take the per-session lock the poll already holds, so a Mode.Now send
            // cannot interleave with a poll (or another Now) in one composer. DeliverAsync itself
            // must not take the lock — SendNowAsync and the turn-end flush already hold it.
            var nowLock = GetLock(sessionId);
            await nowLock.WaitAsync(ct);
            DeliveryOutcome outcome;
            try
            {
                await RequireImmediateAdmissionAsync(await RequireSessionAsync(sessionId, ct), ct);
                await EnsureRulesAllowOrdinaryInputAsync(sessionId, ct);
                await EnsureNoUnconfirmedExpectationBodyAsync(sessionId, ct);
                var nowBody = await SpillQueueBodyAsync(
                    sessionId, trimmed, NowModeFileStem(),
                    origin == QueuedMessageOrigin.Channel
                        ? TypedBodySpill.TryReadChannelEnvelope(trimmed)
                        : null,
                    db: null, ct);
                // CARD-0164: capture the floor BEFORE typing so Mode:Now's post-verdict grace uses the
                // same baseline the confirm loop did (sequence when observable; wall-clock when not).
                var nowBaseline = _verification.TranscriptConfirmEnabled
                    ? await CaptureTranscriptBaselineAsync(sessionId, ct)
                    : default;
                DateTime? nowConfirmFrom = nowBaseline.Observable
                    ? null
                    : UtcNow() - TimeSpan.FromSeconds(
                        Math.Max(0, _verification.UnobservableBaselineConfirmClockToleranceSeconds));

                var capturedGeneration = await CaptureSessionGenerationAsync(sessionId, ct);
                outcome = await DeliverAsync(sessionId, nowBody, ct, nowBaseline);
                if (outcome.Verdict == DeliveryVerdict.ForbiddenBody)
                {
                    await HandleForbiddenBodyAsync(sessionId, null, nowBody, outcome.RecordText, ct);
                    throw new ValidationException(
                        nameof(body),
                        outcome.RecordText ?? "This body is forbidden for this agent kind.");
                }
                if (outcome.Verdict == DeliveryVerdict.Truncated)
                {
                    await HandleTruncationAsync(sessionId, null, nowBody, outcome.RecordText, ct);
                    throw new ConflictException(
                        "Message delivery reached the transcript truncated "
                        + $"({Describe(outcome.Verdict)}). See the agent's incidents.");
                }
                if (outcome.UnavailabilityCode is not null) throw PhoneHomeInputUnavailable();
                if (outcome.Verdict == DeliveryVerdict.BackendUnreachable)
                {
                    throw new ConflictException(
                        $"Agent session '{sessionId}' cannot accept input because herdr is unreachable; try again when it is back.");
                }
                if (outcome.Verdict == DeliveryVerdict.ModalBlocked)
                {
                    return (await GetQueueAsync(sessionId, ct)) with
                    {
                        ModalBlocked = true,
                        ModalBlockedReason = "Remote Control menu blocks input",
                    };
                }

                if (outcome.Verdict != DeliveryVerdict.Delivered)
                {
                    // CARD-0164 B4: Mode:Now has no message row, so HandleDeliveryFailureAsync's
                    // grace (gated on messageIds) never ran. Pull-and-recheck for NoSubmitOutput /
                    // NoTranscriptRecord only — never NoComposerEvidence (Enter withheld).
                    if (outcome.Verdict is DeliveryVerdict.NoSubmitOutput
                        or DeliveryVerdict.NoTranscriptRecord)
                    {
                        var grace = await TryGraceConfirmModeNowAsync(
                            sessionId, nowBody, nowBaseline, nowConfirmFrom, ct);
                        if (grace.Verdict == DeliveryVerdict.Delivered)
                        {
                            _logger.LogInformation(
                                "Mode:Now delivery to session {SessionId} grace-confirmed after a "
                                + "{Verdict} verdict — returning success with no incident",
                                sessionId, outcome.Verdict);
                            return (await GetQueueAsync(sessionId, ct)) with
                            {
                                LastDelivery = ToReceipt(grace),
                            };
                        }
                        if (grace.Verdict == DeliveryVerdict.Truncated)
                        {
                            await HandleTruncationAsync(sessionId, null, nowBody, grace.RecordText, ct);
                            throw new ConflictException(
                                "Message delivery reached the transcript truncated "
                                + $"({Describe(grace.Verdict)}). See the agent's incidents.");
                        }
                    }

                    await HandleDeliveryFailureAsync(sessionId, null, outcome.Verdict, ct, capturedGeneration);
                    throw new ConflictException(
                        "Message delivery could not be verified — the terminal did not accept it "
                        + $"({Describe(outcome.Verdict)}). See the agent's incidents.");
                }
            }
            finally
            {
                nowLock.Release();
            }
            return (await GetQueueAsync(sessionId, ct)) with { LastDelivery = ToReceipt(outcome) };
        }

        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = UtcNow();

            if (mentionOccurrenceId is Guid occurrenceId)
            {
                var existing = await db.SessionQueuedMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == occurrenceId, ct);
                if (existing is not null)
                {
                    ValidateMentionIdentity(existing, sessionId, contentDigest);
                    onCreated?.Invoke(existing.Id);
                    return await GetQueueAsync(sessionId, ct);
                }
            }

            if (sourceLandNotificationId is Guid notificationId)
            {
                var existing = await db.SessionQueuedMessages.AsNoTracking()
                    .SingleOrDefaultAsync(m => m.SourceLandNotificationId == notificationId, ct);
                if (existing is not null)
                {
                    if (existing.AgentSessionId != sessionId || existing.ContentDigest != contentDigest)
                        throw new ConflictException("Land notification identity has a different destination or payload.");
                    onCreated?.Invoke(existing.Id);
                    if (scope.ServiceProvider.GetService<LandDeliveryBoundary>() is { } boundary)
                        await boundary.ReachedAsync("queue-existing-key", sourceTaskId ?? Guid.Empty, existing.Id, ct);
                    return await GetQueueAsync(sessionId, ct);
                }
                if (scope.ServiceProvider.GetService<LandDeliveryBoundary>() is { } absentBoundary)
                    await absentBoundary.ReachedAsync("queue-key-absent", sourceTaskId ?? Guid.Empty, notificationId, ct);
            }

            // CARD-0320: the per-session queue lock serialises two EnqueueAsync calls but used
            // to let both insert. A Delegation note with the same SourceTaskId+ContentDigest
            // is the same completion, not a second turn. The task stamp survives queue retention.
            // An existing unstamped row is the partial-enqueue hole: repair the stamp here
            // rather than treating the queue row as proof the stamp already exists.
            if (origin == QueuedMessageOrigin.Delegation
                && sourceLandNotificationId is null
                && sourceTaskId is Guid sourced
                && !string.IsNullOrEmpty(contentDigest))
            {
                var existingQueuedAt = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.SourceTaskId == sourced
                        && m.SourceLandNotificationId == null
                        && m.ContentDigest == contentDigest
                        && m.Origin == QueuedMessageOrigin.Delegation)
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => (DateTime?)m.CreatedAt)
                    .FirstOrDefaultAsync(ct);
                if (existingQueuedAt is not null
                    || await db.AgentTasks.AsNoTracking().AnyAsync(
                        t => t.Id == sourced && t.CompletionNoteDigest == contentDigest, ct))
                {
                    if (existingQueuedAt is not null
                        && CompletionNoteStamp.IsCallerCompletionNote(
                            origin, sourceLandNotificationId, sourced, conversationKey))
                    {
                        await CompletionNoteStamp.ApplyAsync(
                            db, sourced, contentDigest, existingQueuedAt.Value, ct);
                    }

                    _logger.LogWarning(
                        "Skipping duplicate Delegation note for task {SourceTaskId} on session {SessionId} (CARD-0320)",
                        sourced, sessionId);
                    return await GetQueueAsync(sessionId, ct);
                }
            }

            if (!string.IsNullOrEmpty(capacityRecoveryActionKey))
            {
                var existingKeyed = await db.SessionQueuedMessages
                    .FirstOrDefaultAsync(m => m.CapacityRecoveryActionKey == capacityRecoveryActionKey, ct);
                if (existingKeyed is not null)
                    return await GetQueueAsync(sessionId, ct);
            }

            var nextSequence = (await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == sessionId)
                .MaxAsync(m => (long?)m.Sequence, ct) ?? 0) + 1;

            var row = new SessionQueuedMessage
            {
                Id = mentionOccurrenceId ?? Guid.NewGuid(),
                AgentSessionId = sessionId,
                Body = trimmed,
                Status = QueuedMessageStatus.Pending,
                Sequence = nextSequence,
                CreatedAt = now,
                Origin = origin,
                ConversationKey = conversationKey,
                SourceTaskId = sourceTaskId,
                SourceLandNotificationId = sourceLandNotificationId,
                SourceScheduleId = sourceScheduleId,
                ContentDigest = contentDigest,
                NoteHeader = noteHeader,
                HoldUntil = holdUntil,
                ExecutionDeadlineAt = executionDeadlineAt,
                ExecutionTaskId = executionTaskId,
                SpecialistInputPolicyJson = specialistInputPolicyJson,
                CapacityRecoveryActionKey = capacityRecoveryActionKey,
                CapacityWaitId = capacityWaitId,
            };
            // A producer can fit a remote brief before it knows the queue row Id. Bind the
            // pointer and bytes to that Id in the same insert; neither can then be replaced by
            // a later brief for this busy session.
            var stagedBrief = BindStagedSpill(sessionId, row);
            db.SessionQueuedMessages.Add(row);
            var stampCompletion = CompletionNoteStamp.IsCallerCompletionNote(
                origin, sourceLandNotificationId, sourceTaskId, conversationKey);
            await using var completionTx = stampCompletion
                ? await db.Database.BeginTransactionAsync(ct)
                : null;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (mentionOccurrenceId is not null
                && ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                db.Entry(row).State = EntityState.Detached;
                var existing = await db.SessionQueuedMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == mentionOccurrenceId, ct);
                if (existing is null) throw;
                ValidateMentionIdentity(existing, sessionId, contentDigest);
                onCreated?.Invoke(existing.Id);
                return await GetQueueAsync(sessionId, ct);
            }
            catch (DbUpdateException ex) when (sourceLandNotificationId is not null
                && ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                db.Entry(row).State = EntityState.Detached;
                var existing = await db.SessionQueuedMessages.AsNoTracking()
                    .SingleAsync(m => m.SourceLandNotificationId == sourceLandNotificationId, ct);
                if (existing.AgentSessionId != sessionId || existing.ContentDigest != contentDigest)
                    throw new ConflictException("Land notification identity has a different destination or payload.");
                onCreated?.Invoke(existing.Id);
                return await GetQueueAsync(sessionId, ct);
            }
            if (stagedBrief is not null)
                _remoteSpills?.Ack(sessionId, stagedBrief);

            if (stampCompletion && sourceTaskId is Guid completionTaskId)
                await CompletionNoteStamp.ApplyAsync(db, completionTaskId, contentDigest, now, ct);

            if (completionTx is not null)
                await completionTx.CommitAsync(ct);

            onCreated?.Invoke(row.Id);
            if (sourceLandNotificationId is not null && afterLandQueueInsert is not null)
                await afterLandQueueInsert(row.Id, ct);

            // If the agent is already idle (waiting at the prompt), there is no upcoming turn-end to
            // flush on — deliver right away so the message isn't stranded. Non-Running sessions stay
            // Pending here; DeliverNextLockedAsync is the single automatic admission predicate
            // (CARD-0574 D-10). The launch path flushes the queue itself the moment boot completes
            // (FlushSessionAsync).
            var working = await IsWorkingAsync(db, sessionId, ct);
            if (deliverIfIdle
                && _runtime.IsLiveOrUnknown(session)
                && !working)
            {
                await DeliverNextLockedAsync(db, sessionId, ct);
            }
            else if (origin == QueuedMessageOrigin.Supervision && working)
            {
                // Cancel-not-strand (CARD-0082): a Pending /compact flushed at the *next turn end*
                // would compact a session that just became active. Drop it; a later sweep re-derives.
                row.Status = QueuedMessageStatus.Canceled;
                row.CanceledAt = UtcNow();
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Canceled Supervision compact {MessageId} on session {SessionId}: session is working",
                    row.Id, sessionId);
            }
        }
        finally
        {
            sem.Release();
        }

        var dto = await GetQueueAsync(sessionId, ct);
        await PublishQueueChangedAsync(dto, ct);
        return dto;
    }

    /// <summary>
    /// CARD-0241: persist a Delegation (or other) queue row, then type immediately even while the
    /// session is working. Mode:Now stays row-less; this is the overlay-answer path so a later
    /// 409 can late-confirm against the stored baseline. Throws the same
    /// <see cref="ConflictException"/>s Mode:Now does when verification fails.
    /// </summary>
    public async Task<SessionQueueDto> EnqueueDeliveringNowAsync(
        Guid sessionId, string body, CancellationToken ct,
        QueuedMessageOrigin origin = QueuedMessageOrigin.Ui)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            throw new ValidationException(nameof(body), "Message must not be empty.");

        var session = await RequireSessionAsync(sessionId, ct);
        var kind = session.AgentKind;
        if (TryGetForbiddenReason(kind, trimmed, out var forbiddenReason))
            throw new ValidationException(nameof(body), forbiddenReason);

        await RequireImmediateAdmissionAsync(session, ct);

        await using (var preScope = _scopeFactory.CreateAsyncScope())
        {
            var preDb = preScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var nowCeilings = await CeilingsForSessionAsync(preDb, sessionId, ct);
            if (nowCeilings.Backend == DeliveryBackend.HerdrPane
                && _runtime.TryGetLiveMetadata(sessionId, out var nowMeta)
                && string.Equals(nowMeta.AgentStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    $"Agent session '{sessionId}' is blocked in herdr (permission/approval UI); try again when idle.");
            }

            if (_runtime.TryGetLiveMetadata(sessionId, out var pendingMeta)
                && string.Equals(pendingMeta.Pending, HerdrPendingReasons.Unreachable, StringComparison.Ordinal))
            {
                throw new ConflictException(
                    $"Agent session '{sessionId}' cannot accept input because herdr is unreachable; try again when it is back.");
            }
        }

        var nowLock = GetLock(sessionId);
        await nowLock.WaitAsync(ct);
        DeliveryOutcome outcome;
        SessionQueuedMessage row;
        TranscriptBaseline nowBaseline;
        try
        {
            await RequireImmediateAdmissionAsync(await RequireSessionAsync(sessionId, ct), ct);
            await EnsureRulesAllowOrdinaryInputAsync(sessionId, ct);
            await EnsureNoUnconfirmedExpectationBodyAsync(sessionId, ct);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = UtcNow();
            var nextSequence = (await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == sessionId)
                .MaxAsync(m => (long?)m.Sequence, ct) ?? 0) + 1;

            row = new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = sessionId,
                Body = trimmed,
                Status = QueuedMessageStatus.Pending,
                Sequence = nextSequence,
                CreatedAt = now,
                Origin = origin,
            };
            db.SessionQueuedMessages.Add(row);
            await db.SaveChangesAsync(ct);

            var nowBody = await SpillQueueBodyAsync(
                sessionId, trimmed, row.Id.ToString("D"),
                origin == QueuedMessageOrigin.Channel
                    ? TypedBodySpill.TryReadChannelEnvelope(trimmed)
                    : null,
                db, ct);
            if (!ReferenceEquals(nowBody, trimmed) && nowBody != trimmed)
                row.Body = nowBody;
            BindGeneratedSpill(sessionId, row, nowBody);
            if (!await HoldSpillBodyForDeliveryAsync(db, sessionId, row, nowBody, ct))
                throw new ConflictException(RemoteSpillUndeliverableException.MissingBodyReason);

            nowBaseline = _verification.TranscriptConfirmEnabled
                ? await CaptureTranscriptBaselineAsync(db, sessionId, ct)
                : default;
            row.Status = QueuedMessageStatus.Sent;
            row.SentAt = now;
            row.DeliveryAttempts = 1;
            row.LastDeliveryStartedAt = now;
            row.LastDeliveryGeneration = await CaptureSessionGenerationAsync(sessionId, ct);
            row.LastDeliveryBaselineSequence = nowBaseline.Observable ? nowBaseline.MaxSequence : null;
            ClearAttemptVerdict(row);
            await db.SaveChangesAsync(ct);

            var capturedGeneration = await CaptureSessionGenerationAsync(sessionId, ct);
            outcome = await DeliverAsync(sessionId, nowBody, ct, nowBaseline);
            if (outcome.Verdict == DeliveryVerdict.ForbiddenBody)
            {
                await RevertRunAsync(db, [row]);
                await HandleForbiddenBodyAsync(sessionId, [row.Id], nowBody, outcome.RecordText, ct);
                throw new ValidationException(
                    nameof(body),
                    outcome.RecordText ?? "This body is forbidden for this agent kind.");
            }

            if (outcome.Verdict == DeliveryVerdict.Truncated)
            {
                await HandleTruncationAsync(sessionId, [row.Id], nowBody, outcome.RecordText, ct);
                throw new ConflictException(
                    "Message delivery reached the transcript truncated "
                    + $"({Describe(outcome.Verdict)}). See the agent's incidents.");
            }

            if (outcome.UnavailabilityCode is not null)
            {
                db.SessionQueuedMessages.Remove(row);
                await db.SaveChangesAsync(CancellationToken.None);
                throw PhoneHomeInputUnavailable();
            }
            if (outcome.Verdict == DeliveryVerdict.BackendUnreachable)
            {
                row.Status = QueuedMessageStatus.Pending;
                row.SentAt = null;
                row.DeliveryAttempts = 0;
                row.LastDeliveryStartedAt = null;
                row.LastDeliveryGeneration = null;
                row.LastDeliveryBaselineSequence = null;
                ClearAttemptVerdict(row);
                await db.SaveChangesAsync(ct);
                throw new ConflictException(
                    $"Agent session '{sessionId}' cannot accept input because herdr is unreachable; try again when it is back.");
            }

            if (outcome.Verdict == DeliveryVerdict.SpillBodyMissing)
                throw new ConflictException(RemoteSpillUndeliverableException.MissingBodyReason);
            if (outcome.Verdict == DeliveryVerdict.Delivered)
            {
                StampAttemptVerdict([row], DeliveryVerdict.Delivered, UtcNow(),
                    releaseSpillBody: AcceptedByCompleteUserPrompt(outcome));
                await db.SaveChangesAsync(ct);
            }
            else
            {
                await HandleDeliveryFailureAsync(sessionId, [row.Id], outcome.Verdict, ct, capturedGeneration);
                await using var verifyScope = _scopeFactory.CreateAsyncScope();
                var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var persisted = await verifyDb.SessionQueuedMessages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == row.Id, ct);
                if (persisted?.Status == QueuedMessageStatus.Sent)
                {
                    _logger.LogInformation(
                        "Overlay Now delivery to session {SessionId} grace-confirmed after a {Verdict} verdict",
                        sessionId, outcome.Verdict);
                    return (await GetQueueAsync(sessionId, ct)) with
                    {
                        LastDelivery = ToReceipt(DeliveryOutcome.Confirmed(DeliveryConfirmedBy.Transcript)),
                    };
                }

                throw new ConflictException(
                    "Message delivery could not be verified — the terminal did not accept it "
                    + $"({Describe(outcome.Verdict)}). See the agent's incidents.");
            }
        }
        finally
        {
            nowLock.Release();
        }

        return (await GetQueueAsync(sessionId, ct)) with { LastDelivery = ToReceipt(outcome) };
    }

    /// <summary>Pending messages for the session, plus whether the agent is currently working.</summary>
    public async Task<SessionQueueDto> GetQueueAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await BuildQueueDtoAsync(db, sessionId, Math.Max(1, _verification.MaxDeliveryAttempts), ct);
    }

    /// <summary>Remove a pending message before it is delivered.</summary>
    public async Task<SessionQueueDto> CancelAsync(Guid sessionId, Guid messageId, CancellationToken ct)
    {
        var canceled = await CancelUnderLockAsync(
            sessionId, messageId, m => m.Status == QueuedMessageStatus.Pending, ct);
        if (canceled is null)
            throw new NotFoundException(nameof(SessionQueuedMessage), messageId);

        return await GetQueueAsync(sessionId, ct);
    }

    /// <summary>
    /// Prepend <paramref name="prefix"/> to a still-Pending, never-typed message and re-fit it
    /// to <paramref name="ceiling"/>. CARD-0074: the check-note sweep uses this so a task that
    /// settled while the note sat in the queue is marked, not silently delivered as live.
    ///
    /// <para>Same lock, same scoped context, same re-check-under-the-lock shape as
    /// <see cref="CancelAsync"/>. A read-modify-write from the sweep's own scope races
    /// <c>FlushAsync</c>: the flush reads <c>head.Body</c> into memory and stamps Sent in a later
    /// SaveChanges, and an amend landing in that gap makes the stored body disagree with what
    /// was typed — exactly the disagreement CARD-0055/CARD-0024 exist to detect, which can then
    /// trigger the always-on kill on a false <c>NoTranscriptRecord</c>.</para>
    ///
    /// <para>Returns false when the row is gone, no longer Pending, already typed
    /// (<c>DeliveryAttempts != 0</c>), or already carries the prefix. The caller decides what
    /// to say; this only applies it safely.</para>
    /// </summary>
    public async Task<bool> AmendPendingBodyAsync(
        Guid sessionId, Guid messageId, string prefix, int ceiling, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return false;

        var applied = false;
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.SessionQueuedMessages
                .FirstOrDefaultAsync(m => m.Id == messageId && m.AgentSessionId == sessionId, ct);
            if (message is null)
                return false;

            if (message.Status != QueuedMessageStatus.Pending || message.DeliveryAttempts != 0)
                return false;
            if (message.MaintenanceKind != RemoteControlMaintenanceKind.None)
                return false;

            var intro = prefix.TrimEnd();
            if (message.Body.StartsWith(intro, StringComparison.Ordinal))
                return false;

            message.Body = PrependWithinCeiling(intro, message.Body, ceiling);
            await db.SaveChangesAsync(ct);
            applied = true;
        }
        finally
        {
            sem.Release();
        }

        if (applied)
        {
            var dto = await GetQueueAsync(sessionId, ct);
            await PublishQueueChangedAsync(dto, ct);
        }

        return applied;
    }

    /// <summary>
    /// Cancels a still-Pending message only if it has never been typed. The superseded-check sweep
    /// uses the same lock and re-check-under-lock shape as <see cref="CancelAsync"/> so it cannot
    /// race a flush that has already captured the body for delivery.
    /// </summary>
    public async Task<bool> CancelPendingIfUntypedAsync(Guid sessionId, Guid messageId, CancellationToken ct) =>
        await CancelUnderLockAsync(
            sessionId, messageId,
            m => m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts == 0, ct) == true;

    /// <summary>
    /// CARD-0354: after boot successfully arms remote-control (or finds the bridge already live),
    /// drop leftover queued <c>/remote-control</c> rows. Health-watch enqueues that body WhenIdle
    /// as Ui origin; a kill/restart leaves the row on the persistent session, and delivering it
    /// again after the preamble already armed opens the CARD-0292 management menu. The body also
    /// writes no UserPrompt, so CARD-0055 used to <c>NoTranscriptRecord</c>-kill the always-on
    /// agent — the ClaudeBot-Antiphon restart loop.
    /// </summary>
    public async Task<int> CancelPendingRemoteControlAsync(Guid sessionId, CancellationToken ct)
    {
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        var canceled = 0;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = await db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
                .ToListAsync(ct);
            if (pending.Count == 0)
                return 0;

            var now = UtcNow();
            foreach (var message in pending)
            {
                if (message.MaintenanceKind == RemoteControlMaintenanceKind.AutomaticArm
                    && message.DeliveryAttempts == 0
                    && message.MaintenanceResult is RemoteControlArmResult.Requested
                        or null)
                {
                    message.Status = QueuedMessageStatus.Canceled;
                    message.CanceledAt = now;
                    message.MaintenanceSlotActive = false;
                    canceled++;
                    continue;
                }

                if (message.MaintenanceKind != RemoteControlMaintenanceKind.None)
                    continue;
                if (message.DeliveryAttempts != 0)
                    continue;
                if (message.Origin is QueuedMessageOrigin.Ui or QueuedMessageOrigin.Channel)
                    continue;
                if (!string.Equals(FirstCommandToken(message.Body), "/remote-control", StringComparison.OrdinalIgnoreCase))
                    continue;
                message.Status = QueuedMessageStatus.Canceled;
                message.CanceledAt = now;
                canceled++;
            }

            if (canceled > 0)
                await db.SaveChangesAsync(ct);
        }
        finally
        {
            sem.Release();
        }

        if (canceled > 0)
        {
            _logger.LogInformation(
                "Canceled {Count} leftover queued /remote-control on session {SessionId} (CARD-0354)",
                canceled, sessionId);
            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
        }

        return canceled;
    }

    /// <summary>
    /// Cancel still-Pending copies of a schedule (CARD-0057 D3). One outstanding copy per
    /// recurring schedule: a daily prompt that missed three days delivers once on boot.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> CancelPendingBySourceScheduleAsync(
        Guid sourceScheduleId, CancellationToken ct)
    {
        List<(Guid SessionId, Guid MessageId)> pending;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            pending = (await db.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.SourceScheduleId == sourceScheduleId
                    && m.Status == QueuedMessageStatus.Pending)
                .Select(m => new { m.AgentSessionId, m.Id })
                .ToListAsync(ct))
                .Select(m => (m.AgentSessionId, m.Id))
                .ToList();
        }

        var canceled = new List<Guid>(pending.Count);
        foreach (var (sessionId, messageId) in pending)
        {
            if (await CancelPendingIfUntypedAsync(sessionId, messageId, ct))
                canceled.Add(messageId);
        }

        return canceled;
    }

    /// <summary>
    /// CARD-0091. Cancels a message only if it is still Pending and parked while its session lock
    /// is held. A Send-now or late confirm that marked it Sent first wins; an earlier human Drop
    /// simply returns false.
    /// </summary>
    public async Task<bool> CancelParkedIfStaleAsync(Guid sessionId, Guid messageId, CancellationToken ct) =>
        await CancelUnderLockAsync(
            sessionId, messageId,
            m => m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts >= MaxAttempts, ct) == true;

    /// <summary>
    /// The single cancel write primitive. A null result means the row did not exist; false means
    /// it existed but no longer met the caller's precondition. The lock is shared with all queue
    /// delivery paths, so a caller's evidence is always rechecked immediately before the write.
    /// </summary>
    private async Task<bool?> CancelUnderLockAsync(
        Guid sessionId,
        Guid messageId,
        Func<SessionQueuedMessage, bool> when,
        CancellationToken ct)
    {
        var canceled = false;
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.SessionQueuedMessages
                .FirstOrDefaultAsync(m => m.Id == messageId && m.AgentSessionId == sessionId, ct);
            if (message is null)
                return null;

            if (!when(message))
                return false;

            message.Status = QueuedMessageStatus.Canceled;
            message.CanceledAt = UtcNow();
            await db.SaveChangesAsync(ct);
            canceled = true;
        }
        finally
        {
            sem.Release();
        }

        if (canceled)
            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
        return canceled;
    }

    /// <summary>
    /// Keep the prefix whole and trim the original body's tail when the pair exceeds the
    /// ceiling. The banner is worth more to the reader than the last block of a snapshot
    /// they have just been told is historical.
    /// </summary>
    internal static string PrependWithinCeiling(string prefix, string body, int ceiling)
    {
        var intro = prefix.TrimEnd() + "\n\n";
        if (ceiling < 1)
            ceiling = 1;
        if (intro.Length >= ceiling)
            return intro[..ceiling];
        var room = ceiling - intro.Length;
        return body.Length <= room ? intro + body : intro + body[..room];
    }

    /// <summary>Promote a specific queued message: deliver it immediately and remove it from the queue.</summary>
    public async Task<SessionQueueDto> SendNowAsync(Guid sessionId, Guid messageId, CancellationToken ct)
    {
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await db.SessionQueuedMessages
                .FirstOrDefaultAsync(m => m.Id == messageId && m.AgentSessionId == sessionId, ct)
                ?? throw new NotFoundException(nameof(SessionQueuedMessage), messageId);

            if (message.Status != QueuedMessageStatus.Pending)
                throw new ConflictException("Message is no longer pending.");

            if (message.MaintenanceKind is RemoteControlMaintenanceKind.AutomaticArm
                or RemoteControlMaintenanceKind.LegacyUnclassified)
            {
                throw new ConflictException(
                    "This remote-control maintenance request cannot be forced through SendNow.");
            }

            await RequireImmediateAdmissionAsync(await RequireSessionAsync(sessionId, ct), ct);
            var beforeCall = db.Entry(message).CurrentValues.Clone();
            await EnsureRulesAllowOrdinaryInputAsync(sessionId, ct);
            await EnsureNoUnconfirmedExpectationBodyAsync(sessionId, ct);

            if (await IsModalBlockedAsync(sessionId, ct))
                return await GetQueueAsync(sessionId, ct) with
                {
                    ModalBlocked = true,
                    ModalBlockedReason = "Remote Control menu blocks input",
                };

            // Same late-confirm as the automatic paths: a previously attempted message whose body
            // is already in the transcript went in, and re-typing it here would put it in twice.
            // Truncated is also "handled" — the body submitted, so re-typing would double-send.
            if ((await LateConfirmAttemptedMessagesAsync(db, sessionId, [message], ct)).Handled > 0)
            {
                var confirmed = await GetQueueAsync(sessionId, ct);
                await PublishQueueChangedAsync(confirmed, ct);
                return confirmed;
            }

            var baseline = await CaptureTranscriptBaselineAsync(db, sessionId, ct);
            var sendNowBody = await SpillQueueBodyAsync(
                sessionId, message.Body, message.Id.ToString("D"),
                message.Origin == QueuedMessageOrigin.Channel
                    ? TypedBodySpill.TryReadChannelEnvelope(message.Body)
                    : null,
                db, ct, specialistInputPolicyJson: message.SpecialistInputPolicyJson);
            if (sendNowBody != message.Body)
                message.Body = sendNowBody;
            BindGeneratedSpill(sessionId, message, sendNowBody);
            if (!await HoldSpillBodyForDeliveryAsync(db, sessionId, message, sendNowBody, ct))
                throw new ConflictException(RemoteSpillUndeliverableException.MissingBodyReason);
            if (await CancelExpiredBriefsAsync(db, [message], ct))
                return await BuildQueueDtoAsync(db, sessionId, MaxAttempts, ct);
            message.Status = QueuedMessageStatus.Sent;
            message.SentAt = UtcNow();
            message.DeliveryAttempts++;
            message.LastDeliveryStartedAt = UtcNow();
            message.LastDeliveryGeneration = await CaptureSessionGenerationAsync(sessionId, ct);
            message.LastDeliveryBaselineSequence = baseline.Observable ? baseline.MaxSequence : null;
            ClearAttemptVerdict(message);
            await db.SaveChangesAsync(ct);
            if (await CancelJustClaimedExpiredBriefsAsync(db, [message], ct))
                return await BuildQueueDtoAsync(db, sessionId, MaxAttempts, ct);
            DeliveryOutcome outcome;
            var capturedGeneration = await CaptureSessionGenerationAsync(sessionId, ct);
            try
            {
                outcome = await DeliverAsync(sessionId, sendNowBody, ct, baseline,
                    firstInputDeadlineAt: FirstInputDeadline([message]));
            }
            catch (OptionalBriefExpiredException)
            {
                await CancelJustClaimedExpiredBriefsAsync(db, [message], ct);
                return await BuildQueueDtoAsync(db, sessionId, MaxAttempts, ct);
            }
            if (outcome.UnavailabilityCode is not null)
            {
                // A pre-body refusal did not consume this attempt. Restore every scalar, including
                // the OLD baseline/verdict and spill, rather than clearing prior delivery evidence.
                db.Entry(message).CurrentValues.SetValues(beforeCall);
                await db.SaveChangesAsync(CancellationToken.None);
                throw PhoneHomeInputUnavailable();
            }
            if (outcome.Verdict == DeliveryVerdict.SpillBodyMissing)
                throw new ConflictException(RemoteSpillUndeliverableException.MissingBodyReason);
            if (outcome.Verdict == DeliveryVerdict.ForbiddenBody)
            {
                await HandleForbiddenBodyAsync(sessionId, [message.Id], sendNowBody, outcome.RecordText, ct);
                throw new ValidationException(
                    "body",
                    outcome.RecordText ?? "This body is forbidden for this agent kind.");
            }
            if (outcome.Verdict == DeliveryVerdict.Truncated)
            {
                await HandleTruncationAsync(sessionId, [message.Id], sendNowBody, outcome.RecordText, ct);
                throw new ConflictException(
                    "Message delivery reached the transcript truncated "
                    + $"({Describe(outcome.Verdict)}). The message has been parked in the queue.");
            }
            if (outcome.Verdict != DeliveryVerdict.Delivered)
            {
                await HandleDeliveryFailureAsync(sessionId, [message.Id], outcome.Verdict, ct, capturedGeneration);
                throw new ConflictException(
                    "Message delivery could not be verified — the terminal did not accept it "
                    + $"({Describe(outcome.Verdict)}). The message has been returned to the queue.");
            }

            StampAttemptVerdict([message], DeliveryVerdict.Delivered, UtcNow(),
                releaseSpillBody: AcceptedByCompleteUserPrompt(outcome));
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            sem.Release();
        }

        var dto = await GetQueueAsync(sessionId, ct);
        await PublishQueueChangedAsync(dto, ct);
        return dto;
    }

    /// <summary>
    /// Called when a session reaches a turn-end (idle). Delivers the next queued message if any; otherwise
    /// the agent has completely finished, so broadcast <c>SessionFinished</c>.
    /// </summary>
    public async Task<SessionQueueTurnEndResult> OnTurnEndAsync(Guid sessionId, CancellationToken ct)
    {
        FlushResult flush;
        var lateConfirmed = new LateConfirmCollector();
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Cancel-not-strand: a Supervision /compact that sat through a turn must not fire now.
            await CancelPendingSupervisionLockedAsync(db, sessionId, "turn-end", ct);
            flush = await DeliverNextLockedAsync(db, sessionId, ct, lateConfirmed);
        }
        finally
        {
            sem.Release();
        }

        if (flush == FlushResult.Nothing)
        {
            await PublishFinishedAsync(sessionId, ct);
        }
        else
        {
            // Delivered (queue shrank) or Failed (message reverted to Pending) — either way the
            // queue view changed. A failed flush is NOT "finished".
            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
        }

        return lateConfirmed.ToResult();
    }

    /// <summary>
    /// Stranded-queue watchdog (called periodically by the session-health hosted service): delivers
    /// pending messages that have been sitting on an IDLE, live, always-on session longer than
    /// <see cref="DeliveryVerificationSettings.StrandedAgeSeconds"/>. This is the redelivery half of
    /// delivery verification — after a verification failure kills a wedged session and the
    /// supervisor resumes it (same session id), nothing else would flush the reverted message until
    /// the next turn end, which an idle session never produces.
    /// </summary>
    public async Task<int> FlushStrandedQueuesAsync(CancellationToken ct)
    {
        var now = UtcNow();
        var cutoff = now - TimeSpan.FromSeconds(_verification.StrandedAgeSeconds);
        var interruptedAge = now - InterruptedAttemptAge;
        var interruptedFloor = now - InterruptedAttemptWindow;

        List<Guid> candidates;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // CARD-0501 re-review R2: discovery is <see cref="QueueAttention.NeedsAttention"/> and
            // nothing else. This query used to carry its own hand-written filter, and the hand-written
            // filter excluded rows at the attempts cap on EVERY arm — right for a Pending row (parking
            // means exactly "no automatic retry", and a session whose only pending message is parked
            // must not even be woken up for it), wrong for an interrupted Sent one. A crashed Sent row
            // at the cap has NOT parked yet: it still owes a late-confirm and a revert to the visible
            // parked shape, and the flush path it would reach withholds the Enter on its own. Excluding
            // it from discovery meant a session holding only that row was never brought into scope at
            // all, and the row sat Sent forever — past this sweep, past ParkedMessageSweepService (which
            // reads Pending-at-the-cap), past everything but an unrelated direct flush.
            var maxAttempts = MaxAttempts;
            var scopedSessionIds = await db.SessionQueuedMessages
                .AsNoTracking()
                .Where(QueueAttention.NeedsAttention(maxAttempts, cutoff, interruptedAge, interruptedFloor))
                .Select(m => m.AgentSessionId)
                .Distinct()
                .ToListAsync(ct);
            if (scopedSessionIds.Count == 0)
                return 0;

            // Always-on agents' sessions: their composer is guaranteed fresh after a
            // verification-failure restart, so re-typing cannot double up. Other sessions keep
            // their pending messages visible for a human to resend — EXCEPT delegation briefs,
            // which no human is watching: a delegate whose boot brief stranded sits at an idle
            // prompt forever (live miss 2026-08-09, CARD-0003). A stranded-then-reverted brief is
            // safe to re-type: a transport failure never reached the terminal, and a verification
            // failure withheld the submitting Enter.
            var keys = scopedSessionIds.Select(id => id.ToString("D")).ToList();
            var alwaysOnKeys = await db.Agents
                .AsNoTracking()
                .Where(a => a.AlwaysOn && a.PersistentSessionId != null && keys.Contains(a.PersistentSessionId))
                .Select(a => a.PersistentSessionId!)
                .ToListAsync(ct);
            var machineOriginSessionIds = await db.SessionQueuedMessages
                .AsNoTracking()
                .Where(m => scopedSessionIds.Contains(m.AgentSessionId))
                .Where(QueueAttention.MachineOriginNeedsAttention(
                    maxAttempts, cutoff, interruptedAge, interruptedFloor))
                .Select(m => m.AgentSessionId)
                .Distinct()
                .ToListAsync(ct);
            candidates = alwaysOnKeys.Select(Guid.Parse)
                .Union(machineOriginSessionIds)
                .ToList();
        }

        if (candidates.Count == 0)
            return 0;

        // CARD-0679 (review 87af1bf6): an unknown phone-home session keeps its queued work moving;
        // a runner that is really unreachable leaves the row Pending (BackendUnreachable).
        var live = _runtime.ListLiveOrUnknownSessions().ToHashSet();
        await using (var candidateScope = _scopeFactory.CreateAsyncScope())
        {
            var candidateDb = candidateScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessions = await candidateDb.AgentSessions.AsNoTracking()
                .Where(s => candidates.Contains(s.Id) && s.Status == SessionStatus.Running)
                .Select(s => new { s.Id, s.RunnerId }).ToListAsync(ct);
            candidates = sessions.Where(s => _runtime.IsAcceptedRunnerBinding(s.RunnerId)
                    && (live.Contains(s.Id) || _runtime.RemoteInventoryPending(s.RunnerId)))
                .Select(s => s.Id).ToList();
        }
        var flushed = 0;
        foreach (var sessionId in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var result = FlushResult.Nothing;
            var sem = GetLock(sessionId);
            await sem.WaitAsync(ct);
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!await IsWorkingAsync(db, sessionId, ct))
                    result = await DeliverNextLockedAsync(db, sessionId, ct);
            }
            finally
            {
                sem.Release();
            }

            if (result == FlushResult.Delivered)
            {
                flushed++;
                _logger.LogInformation(
                    "Stranded-queue watchdog delivered a pending message to idle session {SessionId}", sessionId);
            }

            // A late-confirm is not a delivery — nothing was typed — but the queue still changed.
            if (result is FlushResult.Delivered or FlushResult.LateConfirmed)
                await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
        }

        return flushed;
    }

    /// <summary>
    /// Deliver pending messages to a session that just finished booting. The launch paths call
    /// this right after their boot typing completes, because the enqueue path deliberately
    /// refuses to type into a Starting session — without this nudge a fresh delegate's brief
    /// would wait for the stranded-queue watchdog's next sweep.
    /// </summary>
    public async Task FlushSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        FlushResult result;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Boot is not the moment to compact — cancel-not-strand, a later sweep re-derives.
            await CancelPendingSupervisionLockedAsync(db, sessionId, "boot-flush", ct);
            result = !await IsWorkingAsync(db, sessionId, ct)
                ? await DeliverNextLockedAsync(db, sessionId, ct)
                : FlushResult.Nothing;
        }
        finally
        {
            sem.Release();
        }

        if (result != FlushResult.Nothing)
            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
    }

    /// <summary>
    /// NARROW flush for a manual compaction boundary (CARD-0041): deliver the next queued message
    /// if the session is idle, and nothing else. A manual boundary IS a turn end for the working
    /// rule — without a flush here, messages queued before the compaction sit until the stranded
    /// watchdog's next sweep, which only serves always-on sessions (the CARD-0029 delegation brief
    /// is the live case), and a session that never takes another turn never flushes at all.
    ///
    /// Deliberately NOT <see cref="OnTurnEndAsync"/>: an empty queue must NOT publish
    /// <c>SessionFinished</c> (every idle /compact would fire a spurious "Agent finished" toast —
    /// the SessionFinishedDuplicateTests domain), and the channel/review/task dispatchers must NOT
    /// run (task settlement would be attempted against the STALE pre-compaction report, the exact
    /// mis-settle CARD-0029 warns about). Compaction is not a report.
    /// </summary>
    public async Task FlushIfIdleAsync(Guid sessionId, CancellationToken ct)
    {
        var result = FlushResult.Nothing;
        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await IsWorkingAsync(db, sessionId, ct))
            {
                // A pending auto-compact after a *manual* compact is redundant; drop it.
                await CancelPendingSupervisionLockedAsync(db, sessionId, "idle-flush", ct);
                result = await DeliverNextLockedAsync(db, sessionId, ct);
            }
        }
        finally
        {
            sem.Release();
        }

        if (result != FlushResult.Nothing)
            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
    }

    /// <summary>Replace an optional completion only while it can still win the delivery claim.</summary>
    public async Task<string?> TryApplyDistillationAsync(
        DistillRequest request, string digest, string distilled, CancellationToken ct)
    {
        if (request.Mode != OutputDistillerMode.Apply) return "shadow";
        if (request.QueuedMessageId is not Guid noteId) return "note-missing";
        if (_timeProvider.GetUtcNow() >= request.DeadlineAt) return "deadline";
        using var budget = new ExecutionBudget(request.DeadlineAt, _timeProvider, ct);
        try
        {
            var token = budget.Token;
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sessionId = await db.SessionQueuedMessages.Where(m => m.Id == noteId)
                .Select(m => (Guid?)m.AgentSessionId).FirstOrDefaultAsync(token);
            if (sessionId is not Guid session) return "note-missing";
            // Disk validation consumes the original request budget, before the delivery/DB locks.
            // Read an untracked snapshot so the locked read below actually observes a changed path.
            var snapshot = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == request.TaskId, token);
            var store = scope.ServiceProvider.GetService<IAgentReportStore>();
            var usablePath = snapshot is not null && store is not null
                && await store.IsUsableAsync(snapshot.ResultFilePath, snapshot.Result ?? "", token)
                    ? snapshot.ResultFilePath : null;
            IReadOnlyList<string> attachments;
            try { attachments = snapshot is null ? [] : DeliverableBundleService.ListAttachableFiles(snapshot); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "metadata-unavailable"; }
            if (AfterReportValidationAsync is not null) await AfterReportValidationAsync(token);
            var sem = GetLock(session);
            await sem.WaitAsync(token);
            try
            {
                if (_timeProvider.GetUtcNow() >= request.DeadlineAt) return "deadline";
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var milliseconds = Math.Max(1, (long)(request.DeadlineAt - _timeProvider.GetUtcNow()).TotalMilliseconds);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('statement_timeout', {milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}, true)", token);
                // The source row lock also orders this decision against full-report polling.
                var source = await db.AgentTasks.FromSqlInterpolated(
                    $"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {request.TaskId} FOR UPDATE")
                    .SingleOrDefaultAsync(token);
                var note = await db.SessionQueuedMessages.FromSqlInterpolated(
                    $"SELECT * FROM \"SessionQueuedMessages\" WHERE \"Id\" = {noteId} FOR UPDATE")
                    .SingleOrDefaultAsync(token);
                if (source is null || note is null) return "note-missing";
                // CARD-0544 D-9: only a Completion obligation's keyed row may be distilled; every other
                // keyed kind keeps its immutable Body. The rendering freezes with the first attempt, so
                // the unclaimed-row check below is also what rejects a late replacement.
                var keyedProfiled = note.SourceLandNotificationId is Guid keyedId
                    && await db.AgentTaskLandNotifications.AsNoTracking().AnyAsync(n => n.Id == keyedId
                        && n.Kind == LandNotificationKind.TaskCompletion && n.CompletionSnapshotJson != null, token);
                if (note.SourceLandNotificationId != null && !keyedProfiled
                    || note.SourceTaskId != request.TaskId || note.ContentDigest != digest
                    || note.Origin != QueuedMessageOrigin.Delegation
                    || DelegationNoteDigest.Compute(source.Result ?? source.FailureReason ?? "") != digest)
                    return "identity";
                if (note.Status != QueuedMessageStatus.Pending || note.DeliveryAttempts != 0) return "delivery-claimed";
                if (source.LastPolledResultHash == digest) return "full-report-read";
                if (source.DeliverableBundleDir != snapshot?.DeliverableBundleDir
                    || source.DeliverablePdfPath != snapshot?.DeliverablePdfPath) return "metadata-changed";
                if (BeforeDistillationUpdateAsync is not null) await BeforeDistillationUpdateAsync(token);
                if (_timeProvider.GetUtcNow() >= request.DeadlineAt) return "deadline";
                var header = string.IsNullOrWhiteSpace(note.NoteHeader) ? "" : note.NoteHeader.TrimEnd();
                var selectedPath = source.ResultFilePath == usablePath && source.Result == snapshot?.Result
                    ? usablePath : null;
                var body = DelegationReportFormatter.BuildDistilledNoteBody(header, distilled, source, selectedPath, attachments);
                var deadline = request.DeadlineAt.UtcDateTime;
                var changed = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"SessionQueuedMessages\" SET \"Body\" = {body}, \"HoldUntil\" = NULL WHERE \"Id\" = {noteId} AND clock_timestamp() < {deadline}", token);
                if (changed == 0 || _timeProvider.GetUtcNow() >= request.DeadlineAt) return "deadline";
                await transaction.CommitAsync(token);
                return null;
            }
            finally { sem.Release(); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && budget.Token.IsCancellationRequested) { return "deadline"; }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "57014" && !ct.IsCancellationRequested)
        { return "deadline"; }
    }

    private async Task CancelPendingSupervisionLockedAsync(
        AppDbContext db, Guid sessionId, string reason, CancellationToken ct)
    {
        var pending = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId
                && m.Status == QueuedMessageStatus.Pending
                && m.Origin == QueuedMessageOrigin.Supervision)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return;

        var now = UtcNow();
        foreach (var message in pending)
        {
            message.Status = QueuedMessageStatus.Canceled;
            message.CanceledAt = now;
        }
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Canceled {Count} Supervision compact(s) on session {SessionId} ({Reason})",
            pending.Count, sessionId, reason);
    }

    // The DB session status is the ready gate: Starting means the launch's ready probe has not
    // yet seen an idle composer, so nothing may type into the terminal (see EnqueueAsync).
    private async Task<bool> IsAcceptingInputAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.Status == SessionStatus.Running, ct);
    }

    private enum FlushResult { Nothing, Delivered, Failed, LateConfirmed }

    private async Task EnsureRulesAllowOrdinaryInputAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId, ct);
        if (GrokRulesRefreshService.IsClosed(session))
            throw new ConflictException(session.GrokRulesFailure ?? "Rules acknowledgement is pending; queued work is retained.",
                session.GrokRulesState == GrokRulesState.Failed
                    ? (session.GrokRulesFailure?.Split(':')[0] ?? "grok_rules_initialization_failed")
                    : "grok_rules_initialization_pending");
    }

    // Claims and delivers the oldest pending message (caller holds the per-session lock). With
    // batching enabled, a CONTIGUOUS head run of Channel-origin messages from the SAME conversation
    // coalesces into one delivery under the batch markers (OpenClaw's 'collect' model): a run of 1
    // is literally today's behaviour; UI/System messages and conversation changes break the run, so
    // cross-origin FIFO order is preserved and operator messages keep 1:1 turns.
    // Null generation is reserved for pre-migration rows: only a launch after their typing
    // proves the old composer gone. Missing evidence must never authorize cancellation.
    private static bool DeliveryGenerationChanged(SessionQueuedMessage message, DateTime currentGeneration) =>
        message.LastDeliveryGeneration is { } generation
            ? !SessionGeneration.Equal(generation, currentGeneration)
            : message.LastDeliveryStartedAt is { } started
                && SessionGeneration.Compare(currentGeneration, started) > 0;

    // True when a Pending row that this flush is NOT going to deliver may still be holding the
    // composer: it was typed at least once, its typing belongs to the CURRENT generation, and the
    // whole head of its body is still on the rendered screen. Deliberately the same predicate pair
    // (<see cref="DeliveryGenerationChanged"/> + HeadFragmentIsVisibleWhole) the Enter-only
    // recovery uses to decide "that body is standing in the composer" — the two must agree, or the
    // queue would re-press Enter for a body it simultaneously believed was gone.
    //
    // An unreadable snapshot holds too: "the composer cannot be shown empty" is the same answer
    // RecoverDeliveryRunLockedAsync gives, and guessing empty is the direction that corrupts a
    // delivery rather than delaying one.
    private bool HeldBackTypingBlocksTheComposer(
        Guid sessionId,
        IReadOnlyList<SessionQueuedMessage> pending,
        IReadOnlyList<SessionQueuedMessage> deliverable,
        DateTime sessionGeneration)
    {
        var deliverableIds = deliverable.Select(m => m.Id).ToHashSet();
        var heldBack = pending
            .Where(m => !deliverableIds.Contains(m.Id))
            .Where(m => m.DeliveryAttempts > 0 && !DeliveryGenerationChanged(m, sessionGeneration))
            .ToList();
        if (heldBack.Count == 0)
            return false;

        if (!_runtime.TryGetLiveSnapshot(sessionId, out var snapshot))
        {
            _logger.LogInformation(
                "Holding delivery to session {SessionId}: {Count} held-back message(s) were typed in "
                + "this generation and the rendered snapshot is unavailable, so the composer cannot "
                + "be shown empty",
                sessionId, heldBack.Count);
            return true;
        }

        var standing = heldBack.FirstOrDefault(
            m => ComposerDeliveryEvidence.HeadFragmentIsVisibleWhole(snapshot.RenderedScreen, m.Body));
        if (standing is null)
            return false;

        _logger.LogWarning(
            "Holding delivery to session {SessionId}: message {MessageId} ({Attempts} attempt(s)) is "
            + "still standing whole in the composer, so nothing may be typed on top of it. Release is "
            + "a cleared composer, a late-confirm, or a new session generation",
            sessionId, standing.Id, standing.DeliveryAttempts);
        return true;
    }

    private async Task<bool> CancelDeadBriefsAsync(AppDbContext db,
        IEnumerable<SessionQueuedMessage> messages, DateTime currentGeneration, CancellationToken ct)
    {
        var pending = messages.Where(m => m.Status == QueuedMessageStatus.Pending
            && m.ExecutionTaskId != null).ToList();
        var changed = await CancelExpiredBriefsAsync(db, pending, ct);
        var taskIds = pending.Where(m => m.Status == QueuedMessageStatus.Pending)
            .Select(m => m.ExecutionTaskId!.Value).Distinct().ToList();
        if (taskIds.Count == 0) return changed;
        var tasks = await db.AgentTasks.AsNoTracking().Where(t => taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Status }).ToDictionaryAsync(t => t.Id, ct);
        foreach (var message in pending.Where(m => m.Status == QueuedMessageStatus.Pending))
        {
            if (!tasks.TryGetValue(message.ExecutionTaskId!.Value, out var task)
                || !AgentTaskService.IsSettled(task.Status))
                continue;
            var untyped = message.DeliveryAttempts == 0;
            if (!untyped && !DeliveryGenerationChanged(message, currentGeneration))
                continue;

            message.Status = QueuedMessageStatus.Canceled;
            message.CanceledAt = UtcNow();
            changed = true;
            _logger.LogInformation(
                "Canceled orphaned brief {MessageId} for terminal task {TaskId} ({Status}): {Reason}",
                message.Id, task.Id.ToString("N")[..8], task.Status,
                untyped ? "never typed" : "delivery generation changed");
        }
        if (changed) await db.SaveChangesAsync(ct);
        return changed;
    }

    // Human SendNow and first-input deadline checks deliberately retain expiry-only semantics.
    private async Task<bool> CancelExpiredBriefsAsync(AppDbContext db,
        IEnumerable<SessionQueuedMessage> messages, CancellationToken ct)
    {
        var expired = messages.Where(m => m.Status == QueuedMessageStatus.Pending
            && m.ExecutionDeadlineAt <= UtcNow() && m.DeliveryAttempts == 0
            && m.ExecutionTaskId != null).ToList();
        foreach (var message in expired)
        {
            message.Status = QueuedMessageStatus.Canceled;
            message.CanceledAt = UtcNow();
            await db.AgentTasks.Where(AgentTaskRoles.OptionalWork)
                .Where(t => t.Id == message.ExecutionTaskId && t.Status == AgentTaskStatus.Dispatched)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled)
                    .SetProperty(t => t.CompletedAt, UtcNow())
                    .SetProperty(t => t.FailureReason, "Optional work expired before execution.")
                    .SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()), ct);
        }
        if (expired.Count > 0) await db.SaveChangesAsync(ct);
        return expired.Count > 0;
    }

    /// <summary>Test barrier after disk validation, before taking delivery ownership.</summary>
    internal Func<CancellationToken, Task>? AfterReportValidationAsync { get; set; }
    internal Func<CancellationToken, Task>? BeforeDistillationUpdateAsync { get; set; }

    // Only this invocation knows the first claim has not typed anything yet. Recovered
    // attempts must never use this path: their first byte may already be in the composer.
    private async Task<bool> CancelJustClaimedExpiredBriefsAsync(AppDbContext db,
        IEnumerable<SessionQueuedMessage> messages, CancellationToken ct)
    {
        var expired = messages.Where(m => m.Status == QueuedMessageStatus.Sent
            && m.DeliveryAttempts == 1 && m.ExecutionTaskId != null
            && m.ExecutionDeadlineAt <= UtcNow()).ToList();
        foreach (var message in expired)
        {
            message.Status = QueuedMessageStatus.Pending;
            message.SentAt = null;
            message.DeliveryAttempts = 0;
        }
        return await CancelExpiredBriefsAsync(db, expired, ct);
    }

    private sealed class OptionalBriefExpiredException : Exception;

    private static DateTime? FirstInputDeadline(IEnumerable<SessionQueuedMessage> messages) =>
        messages.Where(m => m.DeliveryAttempts == 1 && m.ExecutionTaskId != null)
            .Select(m => m.ExecutionDeadlineAt).Min();

    private async Task<FlushResult> DeliverNextLockedAsync(
        AppDbContext db,
        Guid sessionId,
        CancellationToken ct,
        LateConfirmCollector? lateConfirmed = null)
    {
        var rulesSession = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (rulesSession is null || !_runtime.IsLiveOrUnknown(rulesSession)) return FlushResult.Nothing;
        try { await _runtime.EnsureInputTransportAvailableAsync(rulesSession, ct); }
        catch (ServiceUnavailableException ex) when (ex.Code == PhoneHomeProblemTypes.Unavailable)
        {
            return FlushResult.Nothing;
        }
        using var rulesScope = _scopeFactory.CreateScope();
        var rules = rulesScope.ServiceProvider.GetService<GrokRulesRefreshService>();
        if (rules is not null)
        {
            await rules.ReconcileAsync(sessionId, ct);
            rulesSession = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        }
        // CARD-0574 D-10: one automatic admission predicate. Never type into a session that is
        // not Running. Starting means the launch's ready probe has not seen an idle composer;
        // Created has no process; Stopping/Stopped/Failed are written only by kill and failure.
        // Live miss 2026-08-09, session 429445c3: a Starting session's write landed during TUI
        // boot, Claude started working, and the launch's ready probe — which waits for an IDLE
        // composer — timed out and killed a healthy already-working delegate at 2m41s.
        if (rulesSession is null || rulesSession.Status != SessionStatus.Running)
            return FlushResult.Nothing;
        var rulesClosed = GrokRulesRefreshService.IsClosed(rulesSession);
        if (rulesSession.GrokRulesState == GrokRulesState.Failed) return FlushResult.Nothing;
        // CARD-0161: resolve ceilings once per flush for this session (herdr vs pty).
        var ceilings = await CeilingsForSessionAsync(db, sessionId, ct);

        // CARD-0161 B4: herdr agent_blocked → defer (Nothing). Never park/fail/kill.
        if (ceilings.Backend == DeliveryBackend.HerdrPane
            && _runtime.TryGetLiveMetadata(sessionId, out var blockedMeta)
            && string.Equals(blockedMeta.AgentStatus, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Deferring delivery to herdr session {SessionId}: agent_status=blocked", sessionId);
            return FlushResult.Nothing;
        }

        // CARD-0186 S3: herdr unreachable (pending adoption, or 503 herdr_unreachable) → defer.
        // No attempt charged, nothing parked, never killed.
        if (_runtime.TryGetLiveMetadata(sessionId, out var pendingMeta)
            && string.Equals(pendingMeta.Pending, HerdrPendingReasons.Unreachable, StringComparison.Ordinal))
        {
            _logger.LogDebug(
                "Deferring delivery to herdr session {SessionId}: Pending={Pending}",
                sessionId, pendingMeta.Pending);
            return FlushResult.Nothing;
        }

        if (await IsModalBlockedLockedAsync(db, sessionId, ct))
            return FlushResult.Nothing;

        var interrupted = await LoadInterruptedSentRunAsync(db, sessionId, ct);
        if (rulesClosed && interrupted.Any(m => m.RulesRefreshKey is null))
        {
            // A boundary must hold new input, not discard evidence for input already sent.
            // Confirm from the transcript only: ordinary recovery must not re-press Enter
            // or retype through the closed rules barrier.
            var confirmed = await LateConfirmAttemptedMessagesAsync(db, sessionId, interrupted, ct);
            lateConfirmed?.Record(confirmed);
            interrupted = interrupted.Where(m => m.Status == QueuedMessageStatus.Sent
                && m.DeliveryVerdict == null).ToList();
            if (interrupted.Any(m => m.RulesRefreshKey is null))
                return confirmed.Handled > 0 ? FlushResult.LateConfirmed : FlushResult.Nothing;
        }
        if (interrupted.Count > 0)
        {
            var recovered = await RecoverDeliveryRunLockedAsync(
                db, sessionId, interrupted, ct, ceilings, lateConfirmed);
            if (recovered != FlushResult.Nothing)
                return recovered;
            if (interrupted.Any(m => m.Status == QueuedMessageStatus.Sent))
                return FlushResult.Nothing;
        }

        var pending = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
            .Where(m => !rulesClosed || (m.RulesRefreshKey != null && m.RulesCoveredByMessageId == null
                && m.RulesAcknowledgedAt == null && m.RulesFailure == null))
            .OrderByDescending(m => m.RulesRefreshKey != null).ThenBy(m => m.Sequence)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return FlushResult.Nothing;

        var sessionGeneration = SessionGeneration.Normalize(
            await db.AgentSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => s.StartedAt)
                .FirstAsync(ct));
        if (await CancelDeadBriefsAsync(db, pending, sessionGeneration, ct))
            pending = pending.Where(m => m.Status == QueuedMessageStatus.Pending).ToList();
        if (pending.Count == 0) return FlushResult.Nothing;

        // CARD-0330: a completion note the distiller is still improving is not in the flush's
        // head run. SendNow ignores holds; a HoldUntil in the past is not a hold.
        var nowUtc = UtcNow();
        pending = pending.Where(m => m.HoldUntil is null || m.HoldUntil <= nowUtc).ToList();
        if (pending.Count == 0)
            return FlushResult.Nothing;

        // THE anti-duplicate keystone (CARD-0055 D3): nothing may re-type a message that has been
        // typed before without first asking the transcript whether it actually went in. A delivery
        // fails verification for two very different reasons — the body never reached Claude, or it
        // did and the matcher was blind (ingestion stall, a fork, a text transform) — and only the
        // transcript can tell them apart. Automatic retry is safe BECAUSE the retry looks first.
        var late = await LateConfirmAttemptedMessagesAsync(db, sessionId, pending, ct);
        lateConfirmed?.Record(late);
        if (late.Handled > 0)
            pending = pending.Where(m => m.Status == QueuedMessageStatus.Pending).ToList();

        // Parked messages (at the attempts cap) stay Pending and visible, but no automatic path
        // types them again — that is what "parks for a human" means. They are still late-confirmed
        // above, so a park resolves itself if the body turns out to have landed complete. A
        // truncated park stays parked: identity-without-completeness is not Sent.
        var deliverable = pending
            .Where(m => m.DeliveryAttempts < MaxAttempts)
            .Where(m => m.MaintenanceKind != RemoteControlMaintenanceKind.LegacyUnclassified)
            .Where(m => m.DeferredFromRunAttemptId == null
                || m.MaintenanceAcceptedStartedAt is not { } deferredG
                || SessionGeneration.Equal(deferredG, sessionGeneration))
            .ToList();
        if (deliverable.Count == 0)
            return late.Handled > 0 ? FlushResult.LateConfirmed : FlushResult.Nothing;

        // CARD-0501 review F1: SKIPPING a held-back row does not EMPTY its composer. A row that was
        // typed in this generation and never confirmed may still be standing there unsubmitted (the
        // Enter-only park above is exactly how that state is reached, and parking deliberately does
        // not restart the session). Typing the next body on top of it makes the terminal receive
        // BOTH as one prompt, and the containment matcher would then mark the next row Delivered for
        // a prompt it never owned. So: hold everything until the composer is demonstrably clear, or
        // a new generation proves the old composer gone. This is a defer — no attempt is charged,
        // nothing is parked, and the no-kill guards above are untouched.
        //
        // The ordinary exit is the late-confirm that already ran: a held body that really did land
        // becomes Sent and stops being a held-back row at all. CancelDeadBriefsAsync and a relaunch
        // are the other two.
        if (HeldBackTypingBlocksTheComposer(
                sessionId, pending, deliverable, sessionGeneration))
            return late.Handled > 0 ? FlushResult.LateConfirmed : FlushResult.Nothing;

        // CARD-0650 D-7: the same hold for an unconfirmed watchdog prompt of this generation.
        if (await ExpectationBodyBlocksComposerAsync(db, sessionId, sessionGeneration, ct))
            return late.Handled > 0 ? FlushResult.LateConfirmed : FlushResult.Nothing;

        pending = deliverable;

        if (pending.Count > 0
            && pending[0].MaintenanceKind == RemoteControlMaintenanceKind.AutomaticArm)
        {
            await using var recoveryScope = _scopeFactory.CreateAsyncScope();
            var recovery = recoveryScope.ServiceProvider.GetService<RemoteControlRecoveryService>();
            if (recovery is not null)
                await recovery.ExecuteAutomaticArmUnderLockAsync(sessionId, pending[0].Id, ct);
            return FlushResult.Nothing;
        }

        // CARD-0132 S1.3: the per-session lock already protects this read and cancellation from a
        // concurrent flush. A check whose subject settled never consumes the caller's next turn;
        // a missing completion note retains the CARD-0074 bannered fallback instead.
        var changedSuperseded = false;
        foreach (var message in pending.Where(m => m.Origin == QueuedMessageOrigin.Check).ToList())
        {
            if (message.SourceLandNotificationId is Guid legacyNotification
                && await db.AgentTaskLandNotifications.AsNoTracking().AnyAsync(
                    n => n.Id == legacyNotification && n.Kind == LandNotificationKind.LegacyCheckNote, ct))
                continue;
            if (!AgentTaskCheckService.TryParseCheckConversationKey(message.ConversationKey, out var taskId))
                continue;
            var supersession = await AgentTaskCheckService.EvaluateAsync(db, taskId, ct);
            if (supersession is not { Settled: true } settled)
                continue;

            var task = await db.AgentTasks.AsNoTracking().Where(t => t.Id == taskId)
                .Select(t => new { t.ParentSessionId, t.RootTaskId }).FirstOrDefaultAsync(ct);
            if (task?.ParentSessionId is Guid parentSession
                && await AgentTaskCheckService.HasCompletionNoteAsync(db, parentSession, task.RootTaskId, ct))
            {
                message.Status = QueuedMessageStatus.Canceled;
                message.CanceledAt = UtcNow();
                changedSuperseded = true;
                continue;
            }

            var capturedAt = AgentTaskCheckService.TryReadCapturedAt(message.Body, out var parsed)
                ? parsed : message.CreatedAt;
            var banner = AgentTaskCheckService.SupersededBanner(
                settled.Status, settled.SettledAt, capturedAt);
            if (!message.Body.StartsWith(AgentTaskCheckService.SupersededMarker, StringComparison.Ordinal))
            {
                message.Body = PrependWithinCeiling(banner, message.Body, ceilings.ReplyInlineMaxChars);
                changedSuperseded = true;
            }
        }
        if (changedSuperseded)
            await db.SaveChangesAsync(ct);
        pending = pending.Where(m => m.Status == QueuedMessageStatus.Pending).ToList();
        if (pending.Count == 0)
            return changedSuperseded ? FlushResult.LateConfirmed : FlushResult.Nothing;

        // CARD-0132 S3b: a status poll only replaces a completion report when this particular
        // queued row recorded the exact report the parent session read. Unlike the Check-origin
        // supersession loop above, this never cancels a row: the short pointer is still delivered.
        var changedShrunk = false;
        if (_delegationSettings.ShrinkPolledCompletionNotes)
        {
            foreach (var message in pending.Where(m =>
                         m.Origin == QueuedMessageOrigin.Delegation
                         && m.DeliveryAttempts == 0
                         && m.SourceTaskId is not null
                         && m.ContentDigest is not null).ToList())
            {
                // CARD-0544 D-9: a keyed row shrinks only when it is an unfrozen Completion obligation.
                if (message.SourceLandNotificationId is Guid keyed
                    && !await db.AgentTaskLandNotifications.AsNoTracking().AnyAsync(n => n.Id == keyed
                        && n.Kind == LandNotificationKind.TaskCompletion && n.CompletionSnapshotJson != null
                        && n.CompletionDeliveryJson == null, ct))
                    continue;
                var contentDigest = message.ContentDigest;
                var noteHeader = message.NoteHeader;
                if (contentDigest is null || noteHeader is null)
                    continue;
                var task = await db.AgentTasks.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == message.SourceTaskId, ct);
                if (task is null
                    || !AgentTaskService.IsSettled(task.Status)
                    || task.LastPolledResultHash is null
                    || task.LastPolledResultAt is null
                    || !string.Equals(contentDigest, task.LastPolledResultHash, StringComparison.Ordinal))
                    continue;

                var reportChars = message.Body.Length;
                message.Body = DelegationReportFormatter.BuildPolledNoteBody(
                    noteHeader, task, reportChars, task.LastPolledResultAt.Value);
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(),
                    AgentTaskId = task.Id,
                    Type = AgentTaskEventType.NoteShrunk,
                    Detail = $"Polled {task.LastPolledResultAt.Value:O}; digest {contentDigest[..Math.Min(8, contentDigest.Length)]}; withheld {reportChars:N0} chars.",
                    At = UtcNow(),
                });
                _logger.LogInformation(
                    "Polled completion note on task {ShortId} {Outcome} for session {SessionId}",
                    DelegationReportFormatter.Short(task.Id), "shrunk", sessionId);
                changedShrunk = true;
            }
        }
        if (changedShrunk)
            await db.SaveChangesAsync(ct);

        pending = await ApplyCapacityHoldAsync(db, sessionId, pending, ct);
        if (pending.Count == 0)
            return FlushResult.Nothing;

        if (_capacityRecovery is { IsEnabled: true }
            && pending[0].CapacityRecoveryActionKey is { } redeemKey
            && pending[0].CapacityWaitId is { } redeemWaitId)
        {
            var wait = await db.CapacityRecoveryWaits.FirstOrDefaultAsync(w => w.Id == redeemWaitId, ct);
            if (wait is not null)
            {
                var result = await _capacityRecovery.RedeemAsync(
                    wait.Id, redeemKey, wait.ExecutionKind, CapacityRedemptionPath.Queue, ct);
                if (!result.Ok)
                    return FlushResult.Nothing;
            }
        }

        var head = pending[0];
        var run = new List<SessionQueuedMessage> { head };

        // Delegation batches for the same reason Channel does — five delegates finishing together
        // should reach the orchestrator as ONE note, not five turns — but with a size cap. Task
        // reports run to thousands of characters where chat messages run to tens, so an uncapped
        // run could push 100k into a TUI in a single paste. The overflow simply rides the next
        // turn-end, which the queue already does naturally.
        var batches = _bridgeSettings.BatchingEnabled
            && head.SpecialistInputPolicyJson is null
            && head.ConversationKey is not null
            && head.Origin is QueuedMessageOrigin.Channel or QueuedMessageOrigin.Delegation;

        if (batches)
        {
            var budget = head.Origin == QueuedMessageOrigin.Delegation
                ? Math.Max(head.Body.Length, ceilings.ReplyInlineMaxChars)
                : int.MaxValue;
            var used = head.Body.Length;

            foreach (var m in pending.Skip(1))
            {
                if (m.SpecialistInputPolicyJson is not null
                    || m.Origin != head.Origin || m.ConversationKey != head.ConversationKey)
                    break;
                if (used + m.Body.Length > budget)
                    break;
                used += m.Body.Length;
                run.Add(m);
            }
        }

        var composed = run.Count == 1
            ? head.Body
            : ChannelPromptFormat.FormatBatch(
                run.Take(run.Count - 1).Select(m => m.Body).ToList(), run[^1].Body);

        // CARD-0025: spill an oversize composed body BEFORE the Sent stamp, and persist the
        // POINTER as each row's Body in that same SaveChanges. Confirmation, late-confirm,
        // PromptsMatch and CARD-0024 completeness all compare the stored Body against the
        // transcript; leaving the original while typing a pointer would fire Truncated on
        // every successful spill and leave Channel replies unroutable. Under-ceiling batches
        // keep their original per-row bodies so each still matches by containment.
        var channelEnvelope = head.Origin == QueuedMessageOrigin.Channel
            ? TypedBodySpill.TryReadChannelEnvelope(run[^1].Body)
            : null;
        // CARD-0544 D-9: Completion obligations freeze their rendering with the first typed
        // attempt. A retry replays that committed wire text instead of recomposing it.
        var completionRows = await CompletionNotificationsForRunAsync(db, run, ct);
        if (ResetUnauthorizedCompletionRenderings(run, completionRows))
        {
            // A rendering lost its snapshot header: restore the raw fallback, type nothing now.
            await db.SaveChangesAsync(ct);
            return FlushResult.Nothing;
        }
        var committedWire = completionRows.Count > 0
            && completionRows.All(n => TaskCompletionNotification.TryReadDelivery(n.CompletionDeliveryJson) is { } d
                && d.MemberQueueIds.Count == run.Count && run.All(m => d.MemberQueueIds.Contains(m.Id)))
            ? TaskCompletionNotification.TryReadDelivery(completionRows[0].CompletionDeliveryJson)!.WireText
            : null;
        var logicalBodies = run.ToDictionary(m => m.Id, m => m.Body);
        var body = committedWire ?? await SpillQueueBodyAsync(
            sessionId, composed, head.Id.ToString("D"), channelEnvelope, db, ct, ceilings,
            head.SpecialistInputPolicyJson);
        BindGeneratedSpill(sessionId, head, body);
        if (!await HoldSpillBodyForDeliveryAsync(db, sessionId, head, body, ct))
            return FlushResult.Failed;
        var spilled = committedWire is null && !ReferenceEquals(body, composed) && body != composed;
        if (committedWire is not null && head.RemoteSpillBody is null)
            await RestageCommittedSpillAsync(db, sessionId, run, completionRows, committedWire, ct);

        // CARD-0340 S3 / CARD-0342: a previously typed body still standing in the composer gets
        // Enter only in the process that took the typing, with the whole head visible (CARD-0501).
        // A new generation proves the old composer gone even when history shows the same body.
        if (run.Any(m => m.DeliveryAttempts > 0)
            && run.Where(m => m.DeliveryAttempts > 0)
                .All(m => !DeliveryGenerationChanged(m, sessionGeneration)))
        {
            if (!_runtime.TryGetLiveSnapshot(sessionId, out var retrySnap))
            {
                _logger.LogInformation(
                    "Deferring redelivery to session {SessionId}: rendered snapshot is unavailable, "
                    + "so the composer cannot be shown empty",
                    sessionId);
                return FlushResult.Nothing;
            }

            if (ComposerDeliveryEvidence.HeadFragmentIsVisibleWhole(retrySnap.RenderedScreen, body))
                return await EnterOnlyConfirmLockedAsync(db, sessionId, run, body, ct, ceilings);
        }

        // Stamped BEFORE a byte is typed, and deliberately NOT undone by the revert on failure: the
        // attempt happened, and the baseline is what the next attempt's late-confirm reads. A crash
        // between here and the write costs one attempt, which is the safe direction to be wrong in.
        var now = UtcNow();
        var baseline = await CaptureTranscriptBaselineAsync(db, sessionId, ct);
        if (await CancelExpiredBriefsAsync(db, run, ct)) return FlushResult.Nothing;
        foreach (var m in run)
        {
            if (m.RulesRefreshKey is not null && m.RulesDeadlineAt is null)
            {
                var rulesSettings = rulesScope.ServiceProvider.GetService<IOptions<GrokRulesSettings>>()?.Value ?? new();
                m.RulesDeadlineAt = now.AddSeconds(m.RulesRefreshKey.StartsWith("launch:", StringComparison.Ordinal)
                    ? rulesSettings.InitializationTimeoutSeconds : rulesSettings.RefreshTimeoutSeconds);
            }
            m.Status = QueuedMessageStatus.Sent;
            m.SentAt = now;
            m.DeliveryAttempts++;
            m.LastDeliveryStartedAt = now;
            m.LastDeliveryGeneration = sessionGeneration;
            m.LastDeliveryBaselineSequence = baseline.Observable ? baseline.MaxSequence : null;
            ClearAttemptVerdict(m);
            if (spilled)
                m.Body = body;
        }
        if (committedWire is null && completionRows.Count > 0)
        {
            // One transaction: the attempt/floor claim and the frozen rendering commit together,
            // before the first byte is typed, or neither does.
            await using var renderTx = db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(ct) : null;
            await db.SaveChangesAsync(ct);
            await FreezeCompletionRenderingAsync(db, sessionId, run, completionRows, logicalBodies, composed, body,
                spilled, now, ct);
            if (renderTx is not null)
                await renderTx.CommitAsync(ct);
        }
        else
            await db.SaveChangesAsync(ct);

        DeliveryOutcome outcome;
        DateTime? capturedGeneration = null;
        try
        {
            foreach (var landRow in run.Where(m => m.SourceLandNotificationId != null))
                if (rulesScope.ServiceProvider.GetService<LandDeliveryBoundary>() is { } landBoundary)
                    await landBoundary.ReachedAsync("queue-before-typing", landRow.SourceTaskId!.Value, landRow.Id, ct);
            if (await CancelJustClaimedExpiredBriefsAsync(db, run, ct)) return FlushResult.Nothing;
            using var observation = new RuntimePhase(_logger, _timeProvider, sessionId, "queue.delivery-confirm");
            capturedGeneration = await CaptureSessionGenerationAsync(sessionId, ct);
            outcome = await DeliverAsync(sessionId, body, ct, baseline, ceilings, FirstInputDeadline(run));
        }
        catch (OptionalBriefExpiredException)
        {
            await CancelJustClaimedExpiredBriefsAsync(db, run, ct);
            return FlushResult.Nothing;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown — but the run is already marked Sent, and a Sent-but-never-delivered
            // message is invisible to every retry path (live miss 2026-08-09: four delegated
            // tasks' briefs stranded this way). Revert before propagating.
            await RevertRunAsync(db, run);
            throw;
        }
        catch (Exception ex)
        {
            // Transport failure: the runner 500'd, was unreachable, or timed out (an HttpClient
            // timeout is an OperationCanceledException with OUR token not cancelled — treat it as
            // transport, never as shutdown). The terminal never saw the write, so reverting to
            // Pending cannot double-type; redelivery comes via the stranded-queue watchdog or the
            // next turn-end flush, and the incident makes the failure visible on the agent.
            _logger.LogWarning(
                ex,
                "Delivery to session {SessionId} threw before the terminal accepted it; reverting {Count} message(s) to Pending",
                sessionId, run.Count);
            await RevertRunAsync(db, run);
            await RecordTransportFailureAsync(sessionId, ex, ct);
            return FlushResult.Failed;
        }

        if (outcome.Verdict == DeliveryVerdict.Delivered)
        {
            foreach (var landRow in run.Where(m => m.SourceLandNotificationId != null))
                if (rulesScope.ServiceProvider.GetService<LandDeliveryBoundary>() is { } landBoundary)
                    await landBoundary.ReachedAsync("queue-before-verdict", landRow.SourceTaskId!.Value, landRow.Id, ct);
            StampAttemptVerdict(run, DeliveryVerdict.Delivered, UtcNow(),
                releaseSpillBody: AcceptedByCompleteUserPrompt(outcome));
            await ArmBootReplyWatchAsync(db, sessionId, ct);
            await db.SaveChangesAsync(ct);
            return FlushResult.Delivered;
        }

        if (outcome.Verdict == DeliveryVerdict.SpillBodyMissing)
            return FlushResult.Failed;

        if (outcome.Verdict == DeliveryVerdict.BackendUnreachable)
        {
            // Stamped Sent + attempts++ before typing; refund so this is identical to the blocked
            // gate (FlushResult.Nothing, zero attempts charged).
            foreach (var m in run)
            {
                m.Status = QueuedMessageStatus.Pending;
                m.SentAt = null;
                if (m.DeliveryAttempts > 0)
                    m.DeliveryAttempts--;
                m.LastDeliveryStartedAt = null;
                m.LastDeliveryGeneration = null;
                m.LastDeliveryBaselineSequence = null;
                ClearAttemptVerdict(m);
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Deferring delivery to session {SessionId}: herdr unreachable (no attempt charged)",
                sessionId);
            return FlushResult.Nothing;
        }

        var ids = run.Select(m => m.Id).ToList();
        if (outcome.Verdict == DeliveryVerdict.ForbiddenBody)
        {
            await HandleForbiddenBodyAsync(sessionId, ids, body, outcome.RecordText, ct);
            return FlushResult.Failed;
        }
        if (outcome.Verdict == DeliveryVerdict.Truncated)
        {
            await HandleTruncationAsync(sessionId, ids, body, outcome.RecordText, ct);
            return FlushResult.Failed;
        }

        await HandleDeliveryFailureAsync(sessionId, ids, outcome.Verdict, ct, capturedGeneration);
        return FlushResult.Failed;
    }

    /// <summary>
    /// CARD-0055 D3's late-confirm: for every Pending message that has already been typed at least
    /// once, re-run the prompt matcher over the <c>UserPrompt</c> rows that arrived after that
    /// attempt's stored baseline. A COMPLETE match means the body DID reach Claude — the first
    /// attempt's verification was simply blind (ingestion stall, a mid-session fork, a slow tailer)
    /// — so the message is marked Sent with ZERO writes to the terminal and never typed again.
    /// Identity without completeness is CARD-0024 truncation: park, do not mark Sent (a splice
    /// that lands after the confirm window must not be promoted on the next flush).
    ///
    /// This is what makes the automatic retry above safe. The re-pressed Enter inside a delivery
    /// cannot double-submit (empty composer, per-session lock); the place a duplicate to a human
    /// could originate is a REDELIVERY that re-types, and this runs before every one of them.
    ///
    /// Two deliberate restrictions:
    /// <list type="bullet">
    /// <item>Text match only. The weak arm — "any UserPrompt past the baseline counts" — is fine
    /// inside a 30-second confirm window but not here, where the window is however long the message
    /// has sat Pending: some prompt will always have arrived. A short body that cannot be identified
    /// by text is therefore redelivered rather than assumed delivered. Duplicating an auto-continue
    /// is cheap; silently dropping a human's "yes please" is not.</item>
    /// <item>CARD-0164: a message with no stored sequence baseline (unobservable at attempt time)
    /// is late-confirmed against a <b>wall-clock</b> floor keyed on <c>LastDeliveryStartedAt</c>
    /// (same <c>UnobservableBaselineConfirmClockToleranceSeconds</c> as the inline confirm). The
    /// old claim that "no floor, so a match would prove nothing" is superseded — the floor is the
    /// attempt's own wall clock, and CARD-0056's argument applies: copied history and backfill keep
    /// original timestamps. Without this arm, a WhenIdle first flush on a fresh herdr session that
    /// reverted for NoSubmitOutput was re-typed on the very turn-end its own delivery produced.</item>
    /// </list>
    /// </summary>
    private async Task<LateConfirmCounts> LateConfirmAttemptedMessagesAsync(
        AppDbContext db, Guid sessionId, IReadOnlyList<SessionQueuedMessage> pending, CancellationToken ct)
    {
        if (!_verification.TranscriptConfirmEnabled)
            return LateConfirmCounts.Empty;

        var confirmed = 0;
        var truncated = 0;
        var confirmedMessageIds = new List<Guid>();
        var confirmedChannelMessageIds = new List<Guid>();
        var tolerance = TimeSpan.FromSeconds(
            Math.Max(0, _verification.UnobservableBaselineConfirmClockToleranceSeconds));
        foreach (var m in pending)
        {
            if (m.DeliveryAttempts == 0)
                continue;
            if (!PromptSubmissionMatch.RequiresTextMatch(m.Body))
                continue;

            TranscriptConfirm match;
            if (m.LastDeliveryBaselineSequence is { } floor)
            {
                match = await TryFindConfirmingRecordAsync(db, sessionId, m.Body, floor, ct);
            }
            else if (m.LastDeliveryStartedAt is { } started)
            {
                // CARD-0164 null-baseline arm: wall-clock floor from the attempt's own start.
                match = await TryFindUnobservableConfirmingRecordAsync(
                    db, sessionId, m.Body, started - tolerance, ct);
            }
            else
            {
                continue;
            }

            if (!match.Identity)
                continue;

            if (!match.Complete)
            {
                // Park the tracked entity in THIS context so the rest of the flush (the
                // deliverable filter) sees the cap. HandleTruncationAsync uses its own scope
                // for the incident and the durable park; the two writes are the same values.
                if (m.Status == QueuedMessageStatus.Sent)
                {
                    m.Status = QueuedMessageStatus.Pending;
                    m.SentAt = null;
                }
                m.DeliveryAttempts = Math.Max(m.DeliveryAttempts, MaxAttempts);
                await HandleTruncationAsync(sessionId, [m.Id], m.Body, match.Text, ct);
                truncated++;
                continue;
            }

            m.Status = QueuedMessageStatus.Sent;
            m.SentAt = UtcNow();
            m.DeliveryVerdict = DeliveryVerdict.LateConfirmed;
            m.DeliveryVerdictAt = m.SentAt;
            m.RemoteSpillBody = null;
            confirmed++;
            confirmedMessageIds.Add(m.Id);
            if (m.Origin == QueuedMessageOrigin.Channel)
                confirmedChannelMessageIds.Add(m.Id);
            _logger.LogInformation(
                "Message {MessageId} on session {SessionId} late-confirmed: its body became a UserPrompt "
                + "record after attempt {Attempt} (baseline {Baseline}), so it is marked Sent and the "
                + "redelivery is skipped",
                m.Id, sessionId, m.DeliveryAttempts,
                m.LastDeliveryBaselineSequence?.ToString() ?? $"wall-clock from {m.LastDeliveryStartedAt:o}");
        }

        if (confirmed > 0)
            await db.SaveChangesAsync(ct);

        return new LateConfirmCounts(confirmed, truncated, confirmedMessageIds, confirmedChannelMessageIds);
    }

    /// <summary>CARD-0544 D-9. The Completion obligations (untracked) whose keyed rows are in this run.</summary>
    private static async Task<List<AgentTaskLandNotification>> CompletionNotificationsForRunAsync(
        AppDbContext db, IReadOnlyList<SessionQueuedMessage> run, CancellationToken ct)
    {
        var keyed = run.Where(m => m.SourceLandNotificationId != null)
            .Select(m => m.SourceLandNotificationId!.Value).ToList();
        if (keyed.Count == 0)
            return [];
        return await db.AgentTaskLandNotifications.AsNoTracking()
            .Where(n => keyed.Contains(n.Id) && n.Kind == LandNotificationKind.TaskCompletion && n.CompletionSnapshotJson != null)
            .ToListAsync(ct);
    }

    /// <summary>
    /// CARD-0544 G-108. Before a first attempt, every Completion rendering (distilled, polled,
    /// raw) must still start with its snapshot's exact header; one that does not is replaced by
    /// the raw fallback. Frozen renderings are replayed, never re-judged.
    /// </summary>
    private static bool ResetUnauthorizedCompletionRenderings(
        IReadOnlyList<SessionQueuedMessage> run, IReadOnlyList<AgentTaskLandNotification> completions)
    {
        var changed = false;
        foreach (var notification in completions)
        {
            if (notification.CompletionDeliveryJson is not null)
                continue;
            var snapshot = TaskCompletionNotification.TryReadSnapshot(notification.CompletionSnapshotJson);
            var row = run.First(m => m.SourceLandNotificationId == notification.Id);
            if (snapshot is null || row.DeliveryAttempts > 0 || TaskCompletionNotification.RenderingKeepsHeader(snapshot, row.Body))
                continue;
            row.Body = notification.Body;
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// CARD-0604 D-3. Re-stage a REMOTE session's spilled body when a frozen Completion rendering
    /// is replayed. The frozen wire text is the pointer; the body itself is reconstructed from the
    /// members' own frozen logical notes, which is exactly what was composed and spilled on the
    /// first attempt. A local session, an unspilled rendering or a rendering whose members are not
    /// all frozen stages nothing.
    /// </summary>
    internal async Task RestageCommittedSpillAsync(
        AppDbContext db, Guid sessionId, IReadOnlyList<SessionQueuedMessage> run,
        IReadOnlyList<AgentTaskLandNotification> completions, string wire, CancellationToken ct)
    {
        if (_remoteSpills is null)
            return;
        var runnerCwd = await db.AgentSessions.AsNoTracking().Where(s => s.Id == sessionId)
            .Select(s => s.RunnerCwd).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(runnerCwd))
            return;

        var logicals = new List<string>(run.Count);
        foreach (var row in run)
        {
            var notification = completions.FirstOrDefault(n => n.Id == row.SourceLandNotificationId);
            if (notification is null
                || TaskCompletionNotification.TryReadDelivery(notification.CompletionDeliveryJson) is not { } delivery)
                return;
            logicals.Add(delivery.LogicalNote);
        }

        var composed = logicals.Count == 1
            ? logicals[0]
            : ChannelPromptFormat.FormatBatch(logicals.Take(logicals.Count - 1).ToList(), logicals[^1]);
        // The wire text IS the body when nothing was spilled; only a pointer needs a body behind it.
        if (string.Equals(wire, composed, StringComparison.Ordinal))
            return;

        var relative = TypedBodySpill.InboxRelativePath(run[0].Id.ToString("D"));
        _remoteSpills.Stage(sessionId, runnerCwd!, new PhoneHomeInputSpill(relative, composed));
    }

    /// <summary>
    /// CARD-0544 G-106/G-110. Freeze each Completion member's logical note, the exact composed wire
    /// text, batch membership and any spill identity. Only a still-unfrozen row is written, so a
    /// concurrent replay cannot replace a committed rendering.
    /// </summary>
    internal static async Task FreezeCompletionRenderingAsync(
        AppDbContext db, Guid sessionId, IReadOnlyList<SessionQueuedMessage> run,
        IReadOnlyList<AgentTaskLandNotification> completions, IReadOnlyDictionary<Guid, string> logicalBodies,
        string composed, string wire, bool spilled, DateTime now, CancellationToken ct)
    {
        string? spillPath = null;
        string? spillSha = null;
        if (spilled)
        {
            var session = await db.AgentSessions.AsNoTracking().Where(s => s.Id == sessionId)
                .Select(s => new { s.Cwd, s.RunnerCwd }).FirstOrDefaultAsync(ct);
            // CARD-0604 D-4. A REMOTE session's file is written by the RUNNER, under RunnerCwd,
            // with POSIX separators -- the same join RunnerWorkspaceService.WriteSpillAsync makes.
            // Deriving it from the desktop Cwd froze a Windows path that exists on neither
            // machine, so the receipt named a file nobody could ever open.
            if (!string.IsNullOrWhiteSpace(session?.RunnerCwd))
            {
                spillPath = session.RunnerCwd!.Replace('\\', '/').TrimEnd('/')
                    + "/" + TypedBodySpill.InboxRelativePath(run[0].Id.ToString("D"));
                spillSha = TaskCompletionNotification.Sha256(composed);
            }
            else if (!string.IsNullOrWhiteSpace(session?.Cwd))
            {
                spillPath = TypedBodySpill.InboxAbsolutePath(session.Cwd!, run[0].Id.ToString("D"));
                spillSha = TaskCompletionNotification.Sha256(composed);
            }
        }
        var members = run.Select(m => m.Id).ToList();
        foreach (var notification in completions)
        {
            if (notification.CompletionDeliveryJson is not null)
                continue;
            var row = run.First(m => m.SourceLandNotificationId == notification.Id);
            var logical = logicalBodies[row.Id];
            var kind = logical == notification.Body ? "raw"
                : logical.Contains("Report withheld", StringComparison.Ordinal) ? "polled" : "distilled";
            if (run.Count > 1) kind += "+batch";
            if (spilled) kind += "+spill";
            var json = TaskCompletionNotification.SerializeDelivery(new TaskCompletionNotification.Delivery(
                TaskCompletionNotification.SnapshotVersion, kind, logical, wire, TaskCompletionNotification.Sha256(wire),
                members, spillPath, spillSha, now));
            var id = notification.Id;
            await db.AgentTaskLandNotifications
                .Where(n => n.Id == id && n.CompletionDeliveryJson == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.CompletionDeliveryJson, json), ct);
        }
    }

    private static async Task RevertRunAsync(AppDbContext db, IReadOnlyList<SessionQueuedMessage> run)
    {
        foreach (var m in run)
        {
            m.Status = QueuedMessageStatus.Pending;
            m.SentAt = null;
        }
        // Not the caller's token: when the revert is racing shutdown, completing it is the point.
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<List<SessionQueuedMessage>> LoadInterruptedSentRunAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var now = UtcNow();
        var ageFloor = now - InterruptedAttemptAge;
        var windowFloor = now - InterruptedAttemptWindow;
        // CARD-0501 re-review R2: the SAME expression the sweep discovers on, so the two cannot
        // drift. This selector has no attempts-cap clause on purpose — see QueueAttention.
        var rows = await db.SessionQueuedMessages
            .Where(m => m.AgentSessionId == sessionId)
            .Where(QueueAttention.InterruptedSent(ageFloor, windowFloor))
            .OrderBy(m => m.Sequence)
            .ToListAsync(ct);
        if (rows.Count == 0)
            return rows;

        var head = rows[0];
        return rows
            .Where(m => m.LastDeliveryStartedAt == head.LastDeliveryStartedAt
                && m.Origin == head.Origin
                && m.ConversationKey == head.ConversationKey)
            .ToList();
    }

    /// <summary>
    /// CARD-0340 S3 / CARD-0342 shared recovery: transcript first, then Enter-only if the
    /// composed body is still on screen, otherwise revert a <c>Sent</c> run to Pending with
    /// attempts kept. Never claims the composer is empty when a snapshot cannot be read.
    /// </summary>
    private async Task<FlushResult> RecoverDeliveryRunLockedAsync(
        AppDbContext db,
        Guid sessionId,
        IReadOnlyList<SessionQueuedMessage> run,
        CancellationToken ct,
        PtyDeliveryCeilings ceilings,
        LateConfirmCollector? lateConfirmed = null)
    {
        if (run.Count == 0)
            return FlushResult.Nothing;
        if (run.Any(m => m.MaintenanceKind != RemoteControlMaintenanceKind.None))
            return FlushResult.Nothing;
        if (await IsModalBlockedLockedAsync(db, sessionId, ct))
            return FlushResult.Nothing;

        var late = await LateConfirmAttemptedMessagesAsync(db, sessionId, run, ct);
        lateConfirmed?.Record(late);
        var remaining = run
            .Where(m => m.Status == QueuedMessageStatus.Sent && m.DeliveryVerdict == null)
            .ToList();
        if (remaining.Count == 0)
        {
            if (late.Confirmed > 0)
                return FlushResult.LateConfirmed;
            return late.Truncated > 0 ? FlushResult.Failed : FlushResult.Nothing;
        }

        // CARD-0501 re-review F3: the attempts cap is DURABLE, but it is filtered in
        // DeliverNextLockedAsync over the PENDING set only — and interrupted-Sent recovery runs
        // before that filter, selecting purely on Status/verdict/age. A crash between the Enter-only
        // charge and its failure handler (the seam EnterOnlyConfirmLockedAsync documents) leaves the
        // row Sent, verdict null, ALREADY AT the cap; without this the next sweep presses Enter
        // again and charges MaxAttempts+1, and the cycle that CARD-0501 exists to bound repeats
        // without limit.
        //
        // The cap is therefore enforced here too, by falling through to the revert below: Pending
        // with attempts kept is exactly the parked shape every `DeliveryAttempts >= MaxAttempts`
        // predicate already reads, so the row becomes visible-and-parked, stays late-confirmable on
        // every later flush, and is covered by the F1 composer hold — which is what keeps the body
        // still standing in the composer from being typed on top of. No verdict is invented: the
        // crash lost that observation, and inventing one would claim evidence we never had.
        //
        // All-or-nothing across the run: a recovered run is ONE composed body under ONE Enter, so a
        // single capped row means none of it may be submitted.
        var atCap = remaining.Any(m => m.DeliveryAttempts >= MaxAttempts);

        var currentGeneration = await CaptureSessionGenerationAsync(sessionId, ct);
        var generationChanged = currentGeneration is { } current
            && remaining.Any(m => DeliveryGenerationChanged(m, current));
        if (!generationChanged && !atCap)
        {
            var body = ReconstructRunBody(remaining);
            if (!_runtime.TryGetLiveSnapshot(sessionId, out var snapshot))
            {
                _logger.LogInformation(
                    "Leaving interrupted delivery on session {SessionId} untouched: rendered snapshot "
                    + "is unavailable, so the composer cannot be shown empty",
                    sessionId);
                return FlushResult.Nothing;
            }

            if (ComposerDeliveryEvidence.HeadFragmentIsVisibleWhole(snapshot.RenderedScreen, body))
                return await EnterOnlyConfirmLockedAsync(db, sessionId, remaining, body, ct, ceilings);
        }

        foreach (var message in remaining.Where(m => m.Status == QueuedMessageStatus.Sent))
        {
            message.Status = QueuedMessageStatus.Pending;
            message.SentAt = null;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Reverted {Count} interrupted Sent message(s) on session {SessionId} to Pending "
            + "(attempts kept); {Reason}, so {Next}",
            remaining.Count, sessionId,
            atCap ? $"a message in the run has already been charged {MaxAttempts} attempt(s)"
                : generationChanged ? "delivery generation changed"
                : "the whole body head is not on screen",
            atCap ? "it parks for a human rather than consuming another attempt"
                : "the ordinary path may re-type");
        return FlushResult.Nothing;
    }

    private async Task<FlushResult> EnterOnlyConfirmLockedAsync(
        AppDbContext db,
        Guid sessionId,
        IReadOnlyList<SessionQueuedMessage> run,
        string body,
        CancellationToken ct,
        PtyDeliveryCeilings ceilings)
    {
        var kind = await TryGetSessionKindAsync(sessionId, ct);
        var head = run[0];
        if (head.SpecialistInputPolicyJson is not null)
        {
            await SpillQueueBodyAsync(sessionId, body, "", null, db, ct, ceilings,
                head.SpecialistInputPolicyJson);
            if (head.ExecutionDeadlineAt <= UtcNow()) return FlushResult.Nothing;
        }
        var observable = head.LastDeliveryBaselineSequence is not null;
        var baseline = new TranscriptBaseline(observable, head.LastDeliveryBaselineSequence ?? 0);
        DateTime? unobservableFrom = null;
        if (!observable && head.LastDeliveryStartedAt is { } started)
        {
            unobservableFrom = started - TimeSpan.FromSeconds(
                Math.Max(0, _verification.UnobservableBaselineConfirmClockToleranceSeconds));
        }

        if (!_runtime.TryGetLiveSnapshot(sessionId, out var before))
            return FlushResult.Nothing;

        var submitBaseline = await SettlePostEvidenceAsync(sessionId, ct);
        var capturedGeneration = await CaptureSessionGenerationAsync(sessionId, ct);
        try
        {
            await _runtime.SendInputAsync(sessionId, "\r", ct);
        }
        catch (Exception ex) when (IsHerdrUnreachable(ex))
        {
            return FlushResult.Nothing;
        }

        _logger.LogInformation(
            "Enter-only recovery for {Count} message(s) on session {SessionId}: the body head is "
            + "visible on screen, so nothing is re-typed",
            run.Count, sessionId);

        DeliveryOutcome outcome;
        try
        {
            outcome = await WaitForTranscriptConfirmAsync(
                sessionId, body, baseline, submitBaseline.Sequence, before.RenderedScreen, kind,
                ct, ceilings, unobservableFrom, firstInputDeadlineAt: FirstInputDeadline(run));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Enter-only recovery for session {SessionId} threw; leaving {Count} message(s) for a later sweep",
                sessionId, run.Count);
            return FlushResult.Nothing;
        }

        var ids = run.Select(m => m.Id).ToList();
        if (outcome.Verdict == DeliveryVerdict.Delivered)
        {
            var now = UtcNow();
            var accepted = AcceptedByCompleteUserPrompt(outcome);
            foreach (var message in run)
            {
                message.Status = QueuedMessageStatus.Sent;
                message.SentAt ??= now;
                message.DeliveryVerdict = DeliveryVerdict.Delivered;
                message.DeliveryVerdictAt = now;
                if (accepted)
                    message.RemoteSpillBody = null;
            }

            await ArmBootReplyWatchAsync(db, sessionId, ct);
            await db.SaveChangesAsync(ct);
            return FlushResult.Delivered;
        }

        if (outcome.Verdict == DeliveryVerdict.Truncated)
        {
            await HandleTruncationAsync(sessionId, ids, body, outcome.RecordText, ct);
            return FlushResult.Failed;
        }

        if (outcome.Verdict == DeliveryVerdict.BackendUnreachable)
            return FlushResult.Nothing;

        // Success finishes the original typing. Failure consumes another bounded recovery cycle;
        // retain its typing time, transcript floor and generation for late-confirm.
        //
        // The charge is saved BEFORE the handler runs and in a different scope from it, deliberately
        // (CARD-0501 D-3): HandleDeliveryFailureAsync reloads the rows and computes "parked" from the
        // count it reads, so the count has to be durable first. A crash in between therefore costs
        // the attempt and loses the incident, which is the safe direction — the alternative is an
        // Enter-only cycle that can repeat without bound. The boundary below is the observation seam
        // for that ordering; production installs the no-op base class.
        foreach (var message in run)
            message.DeliveryAttempts++;
        await db.SaveChangesAsync(ct);
        using (var boundaryScope = _scopeFactory.CreateScope())
            if (boundaryScope.ServiceProvider.GetService<LandDeliveryBoundary>() is { } chargeBoundary)
                await chargeBoundary.ReachedAsync(
                    "queue-enter-only-charged", head.ExecutionTaskId ?? Guid.Empty, head.Id, ct);
        await HandleDeliveryFailureAsync(sessionId, ids, outcome.Verdict, ct, capturedGeneration,
            enterOnlyRecovery: true);
        return FlushResult.Failed;
    }

    private static string ReconstructRunBody(IReadOnlyList<SessionQueuedMessage> run)
    {
        if (run.Count == 1)
            return run[0].Body;
        if (run.All(m => m.Body == run[0].Body))
            return run[0].Body;
        return ChannelPromptFormat.FormatBatch(
            run.Take(run.Count - 1).Select(m => m.Body).ToList(), run[^1].Body);
    }

    // The transport-failure sibling of HandleDeliveryFailureAsync: records the incident (visible on
    // the agent card + alert feed) but never kills the session — the terminal is not wedged, the
    // path to it failed, and a kill issued over that same path would fail too.
    private async Task RecordTransportFailureAsync(Guid sessionId, Exception failure, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);
            if (agent is null)
                return;

            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                agent.Id, sessionId, AgentIncidentKind.DeliveryTransportFailed, AlertSeverity.Error,
                $"Message delivery failed in transport before the terminal accepted it: {failure.Message} "
                + "The message has been returned to the queue for redelivery.",
                ct: ct);
            await db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to record delivery transport failure for session {SessionId}", sessionId);
        }
    }

    private const int DeliveryUnverifiedDedupMinutes = 10;

    private DeliveryReceiptDto ToReceipt(DeliveryOutcome outcome)
    {
        var degraded = outcome.ConfirmedBy == DeliveryConfirmedBy.Screen;
        return new DeliveryReceiptDto(
            outcome.Verdict.ToString(),
            outcome.ConfirmedBy,
            degraded,
            Reason: degraded
                ? "this session has no transcript bound (or has not written one yet)"
                : null,
            At: UtcNow());
    }

    /// <summary>
    /// CARD-0180 S3: the screen-only fallback proved a redraw, not a UserPrompt. Observation
    /// only — no kill, no re-type, no change to the Delivered verdict. Deduped per session
    /// per 10 minutes. Reached from EVERY delivery path that ends in the fallback — a queued
    /// WhenIdle body on a pre-first-turn session as much as a Mode:Now send (CARD-0201) — so the
    /// message must not name one of them.
    /// </summary>
    private async Task RecordDeliveryUnverifiedAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await SessionOwnerLookup.ResolveOwningAgentIdAsync(db, sessionId, ct);
            if (owner is not Guid agentId)
                return;

            var window = TimeSpan.FromMinutes(DeliveryUnverifiedDedupMinutes);
            var last = await db.AgentIncidents
                .Where(i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DeliveryUnverified)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => (DateTime?)i.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (last is DateTime at && UtcNow() - at < window)
                return;

            var channelBound = await db.ChatChannels.AnyAsync(c => c.AgentId == agentId, ct);
            var severity = channelBound ? AlertSeverity.Critical : AlertSeverity.Warning;
            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                agentId,
                sessionId,
                AgentIncidentKind.DeliveryUnverified,
                severity,
                "A message was typed but could not be confirmed: this session has no transcript bound "
                + "(or has not written one yet). The terminal redrew, so the text probably landed — "
                + "nothing can verify it.",
                ct: ct);
            await db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to record DeliveryUnverified for session {SessionId}", sessionId);
        }
    }

    // Sibling of RecordTransportFailureAsync: surfaces an oversize delivery on the agent card and
    // the alert feed. Best-effort by design — an unowned session (no agent row) still gets the log
    // line above, and failing to record must never abort the delivery it is only annotating.
    private async Task RecordOversizeAsync(
        Guid sessionId, int length, PtyDeliveryCeilings ceilings, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);
            if (agent is null)
                return;

            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                agent.Id, sessionId, AgentIncidentKind.OversizedTerminalDelivery, AlertSeverity.Warning,
                $"A {length:N0}-byte message was written into this terminal, past the "
                + $"{ceilings.SingleWriteMaxBytes:N0} bytes measured to arrive whole on {ceilings.Backend}. "
                + (ceilings.IsPastePath
                    ? "Beyond that envelope nothing has been measured, and a paste the composer "
                      + "abandons leaves NOTHING rather than a fragment."
                    : "The receiving TUI keeps ONE read chunk per event-loop turn and discards the "
                      + "rest, so part of this — the head, the middle, or all but one chunk — may be "
                      + "missing.")
                + " There is no visible sign either way. Treat what the agent read as unverified.",
                ct: ct);
            await db.SaveChangesAsync(ct);
            await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to record oversized delivery for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Result of <see cref="TypeLocalCommandAsync"/> — the shared core of the poll transport and
    /// the local-command arm of <see cref="DeliverAsync"/>. Callers own Esc-before/after,
    /// Navigation, buffer capture, incidents, and kill.
    /// </summary>
    private enum LocalCommandTypeResult
    {
        /// <summary>Composer never showed the command; Enter was withheld.</summary>
        NotAccepted,
        /// <summary>Enter went out but the output sequence did not advance.</summary>
        NotAdvanced,
        Sent,
    }

    private readonly record struct DeliveryOutcome(
        DeliveryVerdict Verdict, string? RecordText = null, string ConfirmedBy = DeliveryConfirmedBy.None,
        string? UnavailabilityCode = null)
    {
        public static DeliveryOutcome Delivered { get; } = new(DeliveryVerdict.Delivered);
        public static DeliveryOutcome Of(DeliveryVerdict verdict, string? recordText = null) =>
            new(verdict, recordText);
        public static DeliveryOutcome Confirmed(string confirmedBy) =>
            new(DeliveryVerdict.Delivered, ConfirmedBy: confirmedBy);
    }

    private readonly record struct TranscriptConfirm(bool Identity, bool Complete, string? Text)
    {
        public static TranscriptConfirm None { get; } = new(false, false, null);

        public static TranscriptConfirm Classify(string? body, string? recordText, bool fullInline = false)
        {
            if (!PromptSubmissionMatch.IsConfirmedBy(body, recordText))
                return None;
            return new(true, fullInline
                ? SpecialistInputPolicy.CompletePromptEquals(body!, recordText)
                : PromptSubmissionMatch.IsCompleteIn(body, recordText), recordText);
        }
    }

    private static Task<bool> RequiresCompleteSpecialistPromptAsync(
        AppDbContext db, Guid sessionId, string body, CancellationToken ct) =>
        db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == sessionId
            && m.Body == body && m.SpecialistInputPolicyJson != null, ct);

    private async Task<bool> RequiresCompleteSpecialistPromptAsync(
        Guid sessionId, string body, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return await RequiresCompleteSpecialistPromptAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(), sessionId, body, ct);
    }

    private sealed class LateConfirmCollector
    {
        private readonly HashSet<Guid> _messageIds = [];
        private readonly HashSet<Guid> _channelMessageIds = [];

        public void Record(LateConfirmCounts result)
        {
            _messageIds.UnionWith(result.ConfirmedMessageIds);
            _channelMessageIds.UnionWith(result.ConfirmedChannelMessageIds);
        }

        public SessionQueueTurnEndResult ToResult() => new(_messageIds, _channelMessageIds);
    }

    private readonly record struct LateConfirmCounts(
        int Confirmed,
        int Truncated,
        IReadOnlyList<Guid> ConfirmedMessageIds,
        IReadOnlyList<Guid> ConfirmedChannelMessageIds)
    {
        public static LateConfirmCounts Empty { get; } = new(0, 0, [], []);
        public int Handled => Confirmed + Truncated;
    }

    private static ServiceUnavailableException PhoneHomeInputUnavailable() => new(
        "Phone-home runner is unavailable; no message body was sent. Retry when the runner reconnects.",
        PhoneHomeProblemTypes.Unavailable);

    private static bool IsPreBodyPhoneHomeUnavailable(Exception ex) =>
        ex is ServiceUnavailableException { Code: PhoneHomeProblemTypes.Unavailable }
        or PhoneHomeTransportException { Code: PhoneHomeProblemTypes.ConnectionClosedBeforeSend };

    private static bool IsHerdrUnreachable(Exception ex) =>
        ex is ServiceUnavailableException { Code: HerdrProblemTypes.Unreachable }
        || ex is ServiceUnavailableException { Code: var code }
            && (code == PhoneHomeProblemTypes.Unavailable
                || string.Equals(code, "phone_home_unavailable", StringComparison.Ordinal))
        // CARD-0679 D-5: the connection closed before the request was written, so the runner never
        // saw it. A close with the request IN FLIGHT is deliberately not here, nor is a timeout: the
        // runner may have typed it, and refunding the attempt clears the floor the next flush's
        // late-confirm reads, so the body would be typed twice (review 914a96fd D1). Those fall to
        // the transport-failure revert, which keeps the attempt and its baseline.
        || ex is PhoneHomeTransportException { Code: PhoneHomeProblemTypes.ConnectionClosedBeforeSend };

    private static string Describe(DeliveryVerdict verdict) => verdict switch
    {
        DeliveryVerdict.NoComposerEvidence => "the typed message never appeared in the composer",
        DeliveryVerdict.NoSubmitOutput => "the submitting Enter produced no output",
        DeliveryVerdict.NoTranscriptRecord => "the submitted prompt never became a transcript record",
        DeliveryVerdict.Truncated => "the submitted prompt reached the transcript truncated",
        DeliveryVerdict.ForbiddenBody => "the body is forbidden for this agent kind",
        DeliveryVerdict.LocalCommandNotAccepted => "the local TUI command was not accepted by the composer",
        DeliveryVerdict.BackendUnreachable => "herdr is unreachable",
        DeliveryVerdict.LateConfirmed => "late-confirmed by a matching UserPrompt",
        DeliveryVerdict.SpillBodyMissing => RemoteSpillUndeliverableException.MissingBodyReason,
        _ => "delivered",
    };

    private TimeSpan InterruptedAttemptAge =>
        TimeSpan.FromSeconds(
            Math.Max(0, _verification.TranscriptConfirmTimeoutSeconds)
            + Math.Max(0, _verification.PostFailureConfirmGraceSeconds)
            + Math.Max(0, _verification.UnobservableBaselineConfirmClockToleranceSeconds));

    private TimeSpan InterruptedAttemptWindow =>
        TimeSpan.FromMinutes(Math.Max(0, _verification.InterruptedAttemptWindowMinutes));

    private static void ClearAttemptVerdict(SessionQueuedMessage message)
    {
        message.DeliveryVerdict = null;
        message.DeliveryVerdictAt = null;
    }

    /// <summary>
    /// Screen-only <see cref="DeliveryVerdict.Delivered"/> has no UserPrompt. Clearing the spill
    /// there drops the bytes before the recipient accepts them, and the retry cannot rewrite them.
    /// </summary>
    private static bool AcceptedByCompleteUserPrompt(DeliveryOutcome outcome) =>
        outcome.ConfirmedBy == DeliveryConfirmedBy.Transcript;

    private static void StampAttemptVerdict(
        IEnumerable<SessionQueuedMessage> run, DeliveryVerdict verdict, DateTime at,
        bool releaseSpillBody = false)
    {
        foreach (var message in run)
        {
            message.DeliveryVerdict = verdict;
            message.DeliveryVerdictAt = at;
            if (releaseSpillBody)
                message.RemoteSpillBody = null;
        }
    }

    /// <summary>
    /// What the session's transcript looked like the instant before we typed. <see cref="Observable"/>
    /// is the CARD-0055 observability gate: with no stored entry at all the transcript is either not
    /// bound yet (a fresh session's launch note is queued before its JSONL exists — CARD-0006) or
    /// binding failed, and there is no ground truth to confirm against. Degrade to the legacy
    /// screen-only verdict there; never fail a delivery for want of a signal.
    ///
    /// <see cref="MaxSequence"/> is the confirmation floor. Stored sequences are ARRIVAL-ordered and
    /// rebased past the session max (the 2026-08-08 backfill bullet), so anything ingested after
    /// this moment sits strictly above it — backfill reordering can neither fake nor hide a match.
    /// </summary>
    private readonly record struct TranscriptBaseline(bool Observable, long MaxSequence);

    private async Task<TranscriptBaseline> CaptureTranscriptBaselineAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await CaptureTranscriptBaselineAsync(db, sessionId, ct);
    }

    private static async Task<TranscriptBaseline> CaptureTranscriptBaselineAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var max = await db.TranscriptEntries
            .AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence, ct);
        return new TranscriptBaseline(max is not null, max ?? 0);
    }

    // Inject text into the session terminal and submit it, reusing the runtime's input path (which also
    // kicks off manual-turn tracking). The body and the submitting carriage return are sent as two
    // separate writes with a short pause between — NOT concatenated. Claude Code's TUI treats text and a
    // trailing CR arriving in a single write as a bracketed paste and folds the CR into a literal newline,
    // so the message lands in the composer but never submits. A delayed, separate CR is the same path
    // RunnerTerminalSession.SendLineAsync uses for prompts, and it submits reliably.
    //
    // For Claude sessions the gap between the two writes is also the VERIFICATION window: the rendered
    // screen must show evidence of the typed body (ComposerDeliveryEvidence — the contract pinned by
    // ClaudeComposerRenderCanaryTests) before the Enter is sent. A wedged terminal leaves no
    // fingerprint, and crucially the Enter is withheld so the message is never lost into a dead
    // composer.
    //
    // What happens AFTER the Enter is CARD-0055's subject. "The output sequence advanced" used to be
    // the delivery verdict, and it is satisfied by any redraw — a spinner, a status line, the composer
    // re-rendering the text it is STILL HOLDING. Measured consequences (session cefed08a): one note
    // marked Sent at 15:16:20Z did not reach Claude until 17:00:09Z, when the NEXT delivery's Enter
    // pushed it in; the next note's own Enter submitted that STALE body, a new UserPrompt record duly
    // appeared with the wrong text, and its own body died with the composer — never in the transcript
    // at all. So a record ARRIVING is not confirmation either: the record's TEXT must be ours.
    //
    // <paramref name="stampedBaseline"/> is the floor the caller already captured and persisted on
    // the message rows before typing; passing it through keeps the stored baseline and the one this
    // confirm loop reads identical. Callers with nothing to persist (Now-mode) pass none.
    private async Task<DeliveryOutcome> DeliverAsync(
        Guid sessionId, string body, CancellationToken ct, TranscriptBaseline? stampedBaseline = null,
        PtyDeliveryCeilings? ceilings = null, DateTime? firstInputDeadlineAt = null,
        // CARD-0650 D-7: the watchdog's direct send disables both overlay arms (S6 proactive Esc,
        // S5 Esc-and-retype). Every existing caller keeps the default.
        bool overlayRecovery = true)
    {
        // Line endings are normalized to LF before anything touches the PTY. Measured against real
        // Claude (probe runs 2026-07-31): a \n in written input is ALWAYS a literal newline in the
        // composer, while a \r MID-body acts as Enter and SUBMITS the fragment before it — and
        // current conhost builds strip the bracketed-paste markers from written input, so the wrap
        // alone cannot protect a CR-carrying body. CRLF bodies (Windows/Telegram sources) would
        // fragment exactly like the 2026-07-29 live miss. Shared with every other typing path via
        // PtyInputEncoding (SendLineAsync callers were the 2026-08-08 miss).
        var trimmed = Antiphon.Agents.Pty.PtyInputEncoding.NormalizeBody(body);

        if (await IsModalBlockedAsync(sessionId, ct))
            return DeliveryOutcome.Of(DeliveryVerdict.ModalBlocked, "remote-control-modal");

        // CARD-0137 S3 / L0: refuse Forbidden bodies before a byte is typed. Codex `/usage` is the
        // founding case — typing it opens a picker whose highlighted option redeems the account's
        // one usage-limit reset, and CARD-0055's confirm loop would re-press Enter into that picker.
        // Matching is the first whitespace-delimited token so `/usage --json` is refused too.
        // Belt-and-braces for rows already in the queue when the EnqueueAsync pre-check lands, and
        // for any future caller that reaches DeliverAsync without going through EnqueueAsync.
        var kind = await TryGetSessionKindAsync(sessionId, ct);
        if (kind is { } refusedKind && TryGetForbiddenReason(refusedKind, trimmed, out var forbiddenReason))
        {
            _logger.LogError(
                "Refusing forbidden body '{Token}' for {Kind} session {SessionId}: {Reason}",
                FirstCommandToken(trimmed), refusedKind, sessionId, forbiddenReason);
            return DeliveryOutcome.Of(DeliveryVerdict.ForbiddenBody, forbiddenReason);
        }

        // Size gate. Above the measured-safe ceiling the pty drops whole 1024-byte chunks of the
        // body and reports success — and because a surviving head or tail is enough for it, the
        // composer-evidence check below certifies the splice as Delivered. That is exactly how a
        // 5 203-char brief and a 5 368-char report reached their readers as coherent-looking
        // fragments on 2026-08-10.
        //
        // The gate is NOT a guarantee that a body under it arrives whole: on 2026-08-11 four
        // bodies of 1 366-2 320 chars arrived as their final 1024-byte chunk alone, passing
        // straight through here without a word. We still deliver —
        // refusing would strand the message with no path forward — but never silently: the caller
        // paths that produce multi-KB bodies (delegation briefs and reports) now spill to a file
        // instead, so anything still arriving here is a case we have not yet given a file path to.
        // Measured in UTF-8 BYTES, because that is the unit loss is measured in (CARD-0027). This
        // used to compare string.Length against PtyInlineSafeChars (4 000 CHARACTERS), which left
        // everything from ~1 KB to 4 KB typed, clipped and silent — the window that swallowed four
        // briefs on 2026-08-11 without raising a thing.
        //
        // WHERE the threshold sits is the pty's business, not ours (CARD-0037): on the inbox conhost
        // it is one 1 024-byte read chunk, on the shipped modern pseudoconsole it is the 86 400-byte
        // single write measured whole. The tripwire is not removed on the modern backend, only
        // moved — anything past the measured envelope is past all evidence, and a delivery nobody
        // has ever watched arrive is exactly what this exists to name. CARD-0025 spills at the
        // call sites, so this arm is the backstop for a write failure (or a future typer). A
        // successful spill must not reach here.
        // CARD-0161: caller threads per-session ceilings; fall back to process-wide for tests.
        ceilings ??= Ceilings;
        await using (var inputScope = _scopeFactory.CreateAsyncScope())
        {
            var inputDb = inputScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inputPolicyJson = await inputDb.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == sessionId && m.Body == trimmed && m.SpecialistInputPolicyJson != null)
                .Select(m => m.SpecialistInputPolicyJson).FirstOrDefaultAsync(ct);
            if (SpecialistInputPolicy.Read(inputPolicyJson) is { } policy)
            {
                var inputSession = await inputDb.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId, ct);
                policy.RequireSession(inputSession);
                policy.Fit(trimmed, inputSession.AgentKind, ceilings.Backend);
                ceilings = ceilings with { SingleWriteMaxBytes = policy.MaxUtf8Bytes };
            }
        }
        var bodyBytes = System.Text.Encoding.UTF8.GetByteCount(trimmed);
        if (bodyBytes > ceilings.SingleWriteMaxBytes)
        {
            _logger.LogError(
                "Delivering an OVERSIZED body to session {SessionId}: {Bytes:N0} UTF-8 bytes is past "
                + "the {Limit:N0}-byte single write measured whole on {Backend}. Beyond it we have no "
                + "evidence the body survives, and the recipient cannot tell. Give this path a spill file.",
                sessionId, bodyBytes, ceilings.SingleWriteMaxBytes, ceilings.Backend);
            await RecordOversizeAsync(sessionId, bodyBytes, ceilings, ct);
        }

        // CARD-0137 S4 / L1: a declared local command that writes no UserPrompt must not enter
        // CARD-0055's confirm loop (there is no row coming; the timeout would kill). ONE Enter —
        // a re-press lands on a picker's highlighted option (CARD-0141). WritesUserPrompt:true
        // (Claude /compact) and undeclared bodies keep today's path, byte for byte.
        if (kind is { } localKind
            && TryGetLocalCommandFact(localKind, trimmed, out var localFact)
            && !localFact.WritesUserPrompt)
        {
            var typed = await TypeLocalCommandAsync(sessionId, trimmed, ct);
            if (typed == LocalCommandTypeResult.Sent)
                return DeliveryOutcome.Delivered;
            return DeliveryOutcome.Of(DeliveryVerdict.LocalCommandNotAccepted);
        }

        var verify = _verification.Enabled && await IsVerifiedDeliverySessionAsync(sessionId, ct);
        AgentSessionLiveSnapshot before = default!;
        if (verify && !_runtime.TryGetLiveSnapshot(sessionId, out before))
        {
            // The screen is unobservable (snapshot endpoint down, adopted-but-resyncing, …).
            // That is an observability failure, not a terminal failure — deliver blind rather
            // than wrongly declare the session wedged (the echo-probe lesson).
            _logger.LogDebug(
                "Delivery to session {SessionId} is unverifiable (no live snapshot); sending blind", sessionId);
            verify = false;
        }

        // The confirmation floor, captured BEFORE a byte is written: everything ingested from here
        // on sits above it. CARD-0164: observability no longer gates WHETHER we enter the confirm
        // loop — an unobservable baseline runs a wall-clock-floored variant that looks for the
        // FIRST matching UserPrompt, with the screen-only verdict retained only as the deadline
        // fallback (bind-failed sessions still degrade-Delivered rather than hard-failing).
        var fullInline = await RequiresCompleteSpecialistPromptAsync(sessionId, trimmed, ct);
        var baseline = fullInline || (verify && _verification.TranscriptConfirmEnabled)
            ? stampedBaseline ?? await CaptureTranscriptBaselineAsync(sessionId, ct)
            : default;
        var confirmTranscript = fullInline || (verify && _verification.TranscriptConfirmEnabled);
        // Captured BEFORE the body write — same shape as BootConfirmClockTolerance (CARD-0056).
        DateTime? unobservableConfirmFrom = null;
        if (confirmTranscript && !baseline.Observable)
        {
            unobservableConfirmFrom = UtcNow() - TimeSpan.FromSeconds(
                Math.Max(0, _verification.UnobservableBaselineConfirmClockToleranceSeconds));
            _logger.LogDebug(
                "Delivery to session {SessionId} has no transcript entries yet (unbound or "
                + "pre-first-turn); confirming via the first matching UserPrompt after {ConfirmFrom:o}, "
                + "with screen-advance as the deadline fallback",
                sessionId, unobservableConfirmFrom);
        }

        // Multi-line bodies MUST travel as one bracketed paste (\e[200~..\e[201~): ConPTY chunks
        // large writes at arbitrary boundaries, and without the markers the TUI's paste heuristic
        // fragments the body at line breaks — live miss 2026-07-29, where a 2.4 KB calendar message
        // reached the agent as only its final fragment. The markers delimit the paste regardless of
        // read chunking; the submitting CR below stays a separate, unbracketed write.
        // CARD-0137 S6: proactive detector. Match measured overlay fragments against the
        // pre-send snapshot. A generic "looks like a modal" match is refused (CARD-0047).
        // At most one Esc per delivery — if this arm fires, S5 must not send another.
        var overlayDismissed = false;
        if (!overlayRecovery && kind is { } guardKind)
        {
            // CARD-0650 D-7: no Esc arm, so a detected overlay is a modal refusal before any byte.
            var guardScreen = verify ? before.RenderedScreen
                : _runtime.TryGetLiveSnapshot(sessionId, out var guardSnap) ? guardSnap.RenderedScreen : null;
            if (guardScreen is not null && ShowsTerminalOverlay(guardKind, guardScreen))
            {
                _logger.LogWarning(
                    "Refusing a no-Escape delivery to session {SessionId}: a terminal overlay or question popup is showing",
                    sessionId);
                return DeliveryOutcome.Of(DeliveryVerdict.ModalBlocked, "terminal-overlay");
            }
        }
        if (overlayRecovery && verify && kind is { } overlayKind)
        {
            // CARD-0241 S4: the question popup's body is the answer. Do not Esc — DetectFragments
            // for Grok stays /usage-only so S6 cannot dismiss a live question.
            if (GrokQuestionPopup.IsPresent(before.RenderedScreen))
            {
                _logger.LogDebug(
                    "Question popup present on session {SessionId}; withholding Esc so the typed body is the answer",
                    sessionId);
            }
            else
            {
                var overlay = ProviderContractCatalog.For(overlayKind).TerminalOverlay;
                if (overlay.DetectFragments.Count > 0
                    && overlay.DetectFragments.Any(f =>
                        ComposerDeliveryEvidence.FragmentIsVisible(before.RenderedScreen, f)))
                {
                    try
                    {
                        overlayDismissed = await TryDismissOverlayAsync(sessionId, overlayKind, ct);
                    }
                    catch (Exception ex) when (IsPreBodyPhoneHomeUnavailable(ex))
                    {
                        // This proactive Esc precedes the body's first write. Use the same refusal
                        // outcome as an unsent body so immediate callers restore the old attempt or
                        // remove their provisional row. In-flight errors and cancellation still escape.
                        return new DeliveryOutcome(DeliveryVerdict.BackendUnreachable,
                            UnavailabilityCode: PhoneHomeProblemTypes.Unavailable);
                    }
                    if (overlayDismissed && _runtime.TryGetLiveSnapshot(sessionId, out var afterDismiss))
                        before = afterDismiss;
                }
            }
        }

        var payload = Antiphon.Agents.Pty.PtyInputEncoding.WrapIfMultiline(trimmed);
        if (firstInputDeadlineAt <= UtcNow()) throw new OptionalBriefExpiredException();
        try
        {
            await _runtime.SendInputAsync(sessionId, payload, ct);
        }
        // CARD-0693: an unreachable body write is refundable even after an overlay Esc: no body
        // byte has left. Once the body leaves, an unreachable Enter or re-Enter must keep the
        // attempt and its floor so the next flush can recover without typing the body again.
        catch (Exception ex) when (IsHerdrUnreachable(ex))
        {
            return new DeliveryOutcome(DeliveryVerdict.BackendUnreachable,
                UnavailabilityCode: IsPreBodyPhoneHomeUnavailable(ex) ? PhoneHomeProblemTypes.Unavailable : null);
        }
        catch (RemoteSpillUndeliverableException)
        {
            return DeliveryOutcome.Of(DeliveryVerdict.SpillBodyMissing);
        }

        try
        {
            if (verify && !await WaitForComposerEvidenceAsync(sessionId, before.RenderedScreen, trimmed, ct))
            {
                // CARD-0137 S5: reactive overlay recovery. One-shot, idle-gated Esc-and-retype.
                // The Esc is gated on working == false AFTER a fresh CatchUpTranscriptAsync pull —
                // a session parked on a tool-permission modal is mid-turn, so working is true and
                // no Esc is sent. Re-typing is legal here: Enter was withheld, so nothing submitted.
                var recovered = false;
                if (overlayRecovery
                    && !overlayDismissed
                    && kind is { } recoverKind
                    && await TryDismissOverlayAsync(sessionId, recoverKind, ct))
                {
                    if (_runtime.TryGetLiveSnapshot(sessionId, out var recoveredSnap))
                        before = recoveredSnap;
                    try
                    {
                        await _runtime.SendInputAsync(sessionId, payload, ct);
                    }
                    catch (RemoteSpillUndeliverableException)
                    {
                        return DeliveryOutcome.Of(DeliveryVerdict.SpillBodyMissing);
                    }
                    recovered = await WaitForComposerEvidenceAsync(
                        sessionId, before.RenderedScreen, trimmed, ct);
                    if (recovered)
                    {
                        _logger.LogInformation(
                            "Overlay recovery restored composer evidence for session {SessionId} after one Esc",
                            sessionId);
                    }
                }

                if (!recovered)
                {
                    _logger.LogWarning(
                        "Delivery verification failed for session {SessionId}: body ({Length} chars) produced no "
                        + "composer evidence within {Timeout}s — submit Enter withheld",
                        sessionId, trimmed.Length, _verification.EvidenceTimeoutSeconds);
                    return DeliveryOutcome.Of(DeliveryVerdict.NoComposerEvidence);
                }
            }

            var submitBaseline = verify
                ? await SettlePostEvidenceAsync(sessionId, ct)
                : default;

            await Task.Delay(TimeSpan.FromMilliseconds(20), _timeProvider, ct);
            // CARD-0693: the body already left, so an unreachable runner here is not a refund (see
            // the body write above): it propagates and the attempt keeps its floor.
            await _runtime.SendInputAsync(sessionId, "\r", ct);

            if (confirmTranscript)
            {
                return await WaitForTranscriptConfirmAsync(
                    sessionId, trimmed, baseline, submitBaseline.Sequence, submitBaseline.Screen, kind,
                    ct, ceilings, unobservableConfirmFrom, firstInputDeadlineAt);
            }

            // TranscriptConfirmEnabled off: legacy screen-only path (unchanged).
            if (submitBaseline.Sequence is { } advanceFrom
                && !await WaitForSequenceAdvanceAsync(sessionId, advanceFrom, ct))
            {
                _logger.LogWarning(
                    "Delivery verification failed for session {SessionId}: submit Enter produced no output "
                    + "within {Timeout}s",
                    sessionId, _verification.PostSubmitAdvanceTimeoutSeconds);
                return DeliveryOutcome.Of(DeliveryVerdict.NoSubmitOutput);
            }

            return DeliveryOutcome.Delivered;
        }
        catch (Exception ex) when (IsPreBodyPhoneHomeUnavailable(ex))
        {
            // The body already reached the transport. A later unavailable Enter is uncertainty,
            // never the unchanged-state admission refusal used for a body that did not leave.
            throw new PhoneHomeTransportException(PhoneHomeProblemTypes.ConnectionClosedInFlight,
                "The message body was written but submission is uncertain; retained attempt evidence must be reconciled.");
        }
    }

    /// <summary>
    /// CARD-0055's confirm loop, and the only thing that may now produce <c>Delivered</c> on a
    /// transcript-observable Claude session: poll for a <c>UserPrompt</c> row past
    /// <paramref name="baseline"/> whose text carries our FULL body, re-pressing Enter every
    /// <c>ReEnterIntervalSeconds</c> until <c>SubmitAttempts</c> is spent.
    ///
    /// Identity without completeness is <c>Truncated</c> and stops the loop immediately: the
    /// UserPrompt is written once, waiting will not grow it, and another Enter would submit
    /// whatever is now in the composer — not repair the splice.
    ///
    /// The retry is ENTER-ONLY and this is not negotiable. If the first Enter really did submit,
    /// the composer is empty and a re-press is a no-op (the documented <c>VerifiedSubmitOptions</c>
    /// contract the boot path has relied on since 2026-08-08); the per-session queue lock guarantees
    /// no OTHER body can be standing in the composer for a re-press to submit. Re-TYPING the body
    /// would be the one move that can double-send to a human, so nothing here does it — and the
    /// redelivery path that could (slice 3) late-confirms before it types.
    ///
    /// Both measured shapes resolve here: a swallowed Enter gets a re-press that submits the body
    /// still held in the composer, and a stale-body submit produces a record whose text FAILS the
    /// match, so the re-press submits ours and the next record matches.
    /// </summary>
    private async Task<DeliveryOutcome> WaitForTranscriptConfirmAsync(
        Guid sessionId, string body, TranscriptBaseline baseline, long? sequenceBeforeSubmit,
        string screenBeforeSubmit, AgentKind? kind, CancellationToken ct,
        PtyDeliveryCeilings? ceilings = null, DateTime? unobservableConfirmFrom = null,
        DateTime? firstInputDeadlineAt = null)
    {
        var strong = PromptSubmissionMatch.RequiresTextMatch(body);
        var fullInline = await RequiresCompleteSpecialistPromptAsync(sessionId, body, ct);
        var observable = baseline.Observable;
        var deadline = UtcNow() + TimeSpan.FromSeconds(_verification.TranscriptConfirmTimeoutSeconds);
        if (fullInline && firstInputDeadlineAt is { } inputDeadline && inputDeadline < deadline)
            deadline = inputDeadline;
        var reEnterAfter = TimeSpan.FromSeconds(Math.Max(0, _verification.ReEnterIntervalSeconds));
        var lastEnter = UtcNow();
        var entersSent = 1; // the caller's submitting Enter
        var sawSequenceAdvance = false;
        var sawPositiveSubmit = false;
        var workingLatched = false;
        DateTime? emptiedSince = null;
        var emptiedSettle = TimeSpan.FromMilliseconds(
            Math.Clamp(_verification.PostEvidenceSettleMs, 0, 3_000));
        // CARD-0164: pull cadence for the unobservable branch — CatchUpTranscriptAsync, never
        // SyncTranscriptAsync (turn-boundary flush re-enters the queue under the caller's lock).
        var pullEvery = TimeSpan.FromMilliseconds(Math.Max(1000, _verification.PollIntervalMs));
        var lastPull = DateTime.MinValue;

        while (true)
        {
            var match = observable
                ? await TryFindConfirmingRecordAsync(sessionId, body, baseline.MaxSequence, ct)
                : await TryFindUnobservableConfirmingRecordAsync(
                    sessionId, body, unobservableConfirmFrom ?? DateTime.MinValue, ct);
            if (match.Identity)
            {
                if (!match.Complete)
                {
                    _logger.LogWarning(
                        "Delivery to session {SessionId} reached a UserPrompt record past sequence "
                        + "{Baseline} after {Enters} Enter(s) but the body is truncated "
                        + "(sent {Sent} normalized chars, recorded {Recorded})",
                        sessionId, observable ? baseline.MaxSequence : 0, entersSent,
                        PromptSubmissionMatch.Normalize(body).Length,
                        PromptSubmissionMatch.Normalize(match.Text ?? "").Length);
                    return DeliveryOutcome.Of(DeliveryVerdict.Truncated, match.Text);
                }

                _logger.LogDebug(
                    "Delivery to session {SessionId} confirmed by a UserPrompt record past sequence "
                    + "{Baseline} after {Enters} Enter(s) ({Strength} match{Unobs})",
                    sessionId, observable ? baseline.MaxSequence : 0, entersSent,
                    strong ? "text" : "weak",
                    observable ? "" : ", unobservable-baseline");
                return DeliveryOutcome.Confirmed(DeliveryConfirmedBy.Transcript);
            }

            // Kept only as a wedge signal for the log on the observable path; on the unobservable
            // path it ALSO decides the deadline fallback (CARD-0164: screen advance → degraded
            // Delivered, exactly today's claim, made no sooner).
            if (!sawSequenceAdvance
                && sequenceBeforeSubmit is { } from
                && _runtime.TryGetLiveMetadata(sessionId, out var meta)
                && meta.LastSequence > from)
            {
                sawSequenceAdvance = true;
            }

            if (kind is AgentKind.Codex or AgentKind.Grok)
            {
                if (_runtime.TryGetLiveSnapshot(sessionId, out var snapshot))
                {
                    var screenNow = snapshot.RenderedScreen;
                    if (kind == AgentKind.Codex && CodexWorkingIndicator.IsVisible(screenNow))
                    {
                        workingLatched = true;
                        sawPositiveSubmit = true;
                    }
                    else if (!workingLatched)
                    {
                        // CARD-0299 / CARD-0342: do not latch emptied-composer on a single poll.
                        // A mid-redraw empty/ghost/MCP-spinner frame used to suppress every
                        // re-Enter and take degraded Sent at 30s while the durable last frame
                        // still held the body. Grok has no measured Working indicator; sequence
                        // advance stays diagnostic and cannot make this true.
                        if (SubmitEvidence.IsEmptiedComposer(screenBeforeSubmit, screenNow, body))
                        {
                            emptiedSince ??= UtcNow();
                            sawPositiveSubmit = UtcNow() - emptiedSince.Value >= emptiedSettle;
                        }
                        else
                        {
                            emptiedSince = null;
                            sawPositiveSubmit = false;
                        }
                    }
                }
            }
            else if (!sawPositiveSubmit)
            {
                // Claude retains the existing advance-based screen fallback. The baseline is
                // now settled before Enter, so that proof is strictly stronger than a raw redraw.
                sawPositiveSubmit = sawSequenceAdvance;
            }

            if (UtcNow() >= deadline)
            {
                if (fullInline)
                    return DeliveryOutcome.Of(DeliveryVerdict.NoTranscriptRecord);
                if (!observable)
                {
                    // CARD-0299: the durable last frame is the deadline's ground truth. A
                    // transient empty snapshot must not have certified Sent if the body is
                    // still standing in the composer. Working-indicator stays immediate
                    // positive — do not unlatch it when the body is also still echoed.
                    if (kind is AgentKind.Codex or AgentKind.Grok
                        && !workingLatched
                        && _runtime.TryGetLiveSnapshot(sessionId, out var deadlineSnap)
                        && ComposerDeliveryEvidence.HeadFragmentIsVisible(
                            deadlineSnap.RenderedScreen, body))
                    {
                        _logger.LogWarning(
                            "Delivery verification failed for session {SessionId}: submit Enter produced no "
                            + "output within {Timeout}s after {Enters} Enter(s); the body is still visible "
                            + "in the composer (unobservable baseline)",
                            sessionId, _verification.TranscriptConfirmTimeoutSeconds, entersSent);
                        return DeliveryOutcome.Of(DeliveryVerdict.NoSubmitOutput);
                    }

                    // NoTranscriptRecord is deliberately NOT produced here: its post-verdict grace
                    // would re-pull what this loop already pulled, and its meaning presupposes a
                    // bound transcript the session may not have.
                    if (sawPositiveSubmit)
                    {
                        _logger.LogWarning(
                            "Delivery to session {SessionId} confirmed by degraded screen-only verdict "
                            + "after {Timeout}s with no transcript row (bind-failed / pre-first-turn "
                            + "fallback); {Enters} Enter(s) sent",
                            sessionId, _verification.TranscriptConfirmTimeoutSeconds, entersSent);
                        await RecordDeliveryUnverifiedAsync(sessionId, ct);
                        return DeliveryOutcome.Confirmed(DeliveryConfirmedBy.Screen);
                    }

                    _logger.LogWarning(
                        "Delivery verification failed for session {SessionId}: submit Enter produced no "
                        + "output within {Timeout}s after {Enters} Enter(s) and no transcript row "
                        + "arrived (unobservable baseline)",
                        sessionId, _verification.TranscriptConfirmTimeoutSeconds, entersSent);
                    return DeliveryOutcome.Of(DeliveryVerdict.NoSubmitOutput);
                }

                _logger.LogWarning(
                    "Delivery verification failed for session {SessionId}: the body ({Length} chars) never "
                    + "became a UserPrompt record past sequence {Baseline} within {Timeout}s after {Enters} "
                    + "Enter(s); screen output {Advanced} in that window",
                    sessionId, body.Length, baseline.MaxSequence,
                    _verification.TranscriptConfirmTimeoutSeconds, entersSent,
                    sawSequenceAdvance ? "DID advance (the terminal redrew but nothing was submitted)" : "never advanced");
                return DeliveryOutcome.Of(DeliveryVerdict.NoTranscriptRecord);
            }

            // Full-inline Check evidence must survive a dropped live transcript stream on
            // warm sessions too. CatchUp preserves native file order and never re-enters flush.
            if ((fullInline || !observable) && UtcNow() - lastPull >= pullEvery)
            {
                await _runtime.CatchUpTranscriptAsync(sessionId, ct);
                lastPull = UtcNow();
            }

            // Re-press when the submit may have been swallowed. On the unobservable path, once
            // sequence has advanced the screen already shows a submit happened — further Enters
            // cannot help transcript confirmation (and would break the measured empty-composer
            // contract only if something else stood in the composer). Observable path keeps today's
            // schedule unconditionally (stale-body recovery needs the re-press even after a redraw).
            var mayReEnter = entersSent < _verification.SubmitAttempts
                && UtcNow() - lastEnter >= reEnterAfter
                && (observable || !sawPositiveSubmit);
            if (mayReEnter)
            {
                // CARD-0161 B4: withhold re-press Enter while herdr reports blocked (CARD-0141
                // hazard — a keystroke into a permission picker). entersSent not incremented.
                // Shared by the unobservable loop by construction (CARD-0164 decision 11).
                if (ceilings?.Backend == DeliveryBackend.HerdrPane
                    && _runtime.TryGetLiveMetadata(sessionId, out var live)
                    && (string.Equals(live.AgentStatus, "blocked", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(live.Pending, HerdrPendingReasons.Unreachable, StringComparison.Ordinal)))
                {
                    _logger.LogInformation(
                        "Withholding confirm-loop Enter for herdr session {SessionId}: agent_status={Status} pending={Pending}",
                        sessionId, live.AgentStatus, live.Pending);
                }
                else
                {
                    _logger.LogInformation(
                        "No transcript record yet for the delivery to session {SessionId}; pressing Enter again "
                        + "(attempt {Attempt} of {Max}). This never re-types the body — if the first Enter did "
                        + "submit, the composer is empty and this is a no-op",
                        sessionId, entersSent + 1, _verification.SubmitAttempts);
                    // CARD-0693: never the attempt's first write (the caller's Enter went first), so
                    // an unreachable runner propagates rather than refunding the attempt.
                    await _runtime.SendInputAsync(sessionId, "\r", ct);
                    entersSent++;
                    lastEnter = UtcNow();
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_verification.PollIntervalMs), _timeProvider, ct);
        }
    }

    /// <summary>
    /// CARD-0164: unobservable-baseline confirm — any UserPrompt/QueuedUserPrompt whose
    /// <c>Timestamp</c> is at/after <paramref name="confirmFrom"/> may confirm. Sequence floor is
    /// effectively 0; the wall clock is the floor (resume-history / backfill safety). Null
    /// timestamps are never evidence.
    /// </summary>
    private async Task<TranscriptConfirm> TryFindUnobservableConfirmingRecordAsync(
        Guid sessionId, string body, DateTime confirmFrom, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await TryFindUnobservableConfirmingRecordAsync(db, sessionId, body, confirmFrom, ct);
    }

    private static async Task<TranscriptConfirm> TryFindUnobservableConfirmingRecordAsync(
        AppDbContext db, Guid sessionId, string body, DateTime confirmFrom, CancellationToken ct)
    {
        var fullInline = await RequiresCompleteSpecialistPromptAsync(db, sessionId, body, ct);
        var candidates = await db.TranscriptEntries
            .AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt
                    || (t.Kind == TranscriptKinds.ToolResult
                        && (t.ToolName == GrokQuestionTool.AskUserQuestionName
                            || (t.Text != null
                                && t.Text.StartsWith(GrokQuestionTool.CompletedAnswerPrefix)))))
                && (!fullInline || t.Kind == TranscriptKinds.UserPrompt)
                && t.Timestamp != null
                && t.Timestamp >= confirmFrom)
            .OrderBy(t => t.Sequence)
            .Select(t => t.Text)
            .ToListAsync(ct);

        foreach (var text in candidates)
        {
            var match = TranscriptConfirm.Classify(body, text, fullInline);
            if (match.Identity)
                return match;
        }

        return TranscriptConfirm.None;
    }

    /// <summary>
    /// CARD-0164 B4: Mode:Now has no queued message row, so the messageIds-gated
    /// <see cref="GraceConfirmAsync"/> never runs. This is the same pull-and-recheck for a bare
    /// body — <see cref="PostFailureConfirmGraceSeconds"/>, same floors as the confirm loop.
    /// Only turns a failure into success on positive evidence; never the reverse.
    /// </summary>
    private async Task<DeliveryOutcome> TryGraceConfirmModeNowAsync(
        Guid sessionId, string body, TranscriptBaseline baseline, DateTime? unobservableConfirmFrom,
        CancellationToken ct)
    {
        var grace = TimeSpan.FromSeconds(Math.Max(0, _verification.PostFailureConfirmGraceSeconds));
        if (!_verification.TranscriptConfirmEnabled || grace <= TimeSpan.Zero)
            return DeliveryOutcome.Of(DeliveryVerdict.NoSubmitOutput);

        var deadline = UtcNow() + grace;
        while (true)
        {
            await _runtime.CatchUpTranscriptAsync(sessionId, ct);
            var match = baseline.Observable
                ? await TryFindConfirmingRecordAsync(sessionId, body, baseline.MaxSequence, ct)
                : await TryFindUnobservableConfirmingRecordAsync(
                    sessionId, body, unobservableConfirmFrom ?? DateTime.MinValue, ct);
            if (match.Identity)
            {
                if (!match.Complete)
                    return DeliveryOutcome.Of(DeliveryVerdict.Truncated, match.Text);
                return DeliveryOutcome.Confirmed(DeliveryConfirmedBy.Transcript);
            }

            if (UtcNow() >= deadline)
                return DeliveryOutcome.Of(DeliveryVerdict.NoSubmitOutput);

            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Max(1000, _verification.PollIntervalMs)), _timeProvider, ct);
        }
    }

    /// <summary>
    /// Keep looking for the confirming record for a short window after the verdict, and return the
    /// ids that turned out to have landed.
    ///
    /// <para>This is the SAME evidence <see cref="LateConfirmAttemptedMessagesAsync"/> requires —
    /// a text match against a real stored baseline — just consulted before the kill instead of on
    /// the next flush. It can only ever turn a failure into a success, never the reverse, and the
    /// text-match restriction means a body too short to identify (an auto-continue) is never
    /// grace-confirmed: those take the ordinary failure path exactly as before.</para>
    /// </summary>
    private async Task<(HashSet<Guid> Confirmed, HashSet<Guid> Truncated)> GraceConfirmAsync(
        Guid sessionId, IReadOnlyList<Guid> messageIds, CancellationToken ct)
    {
        var confirmed = new HashSet<Guid>();
        var truncated = new HashSet<Guid>();
        var grace = TimeSpan.FromSeconds(Math.Max(0, _verification.PostFailureConfirmGraceSeconds));
        if (!_verification.TranscriptConfirmEnabled || grace <= TimeSpan.Zero)
            return (confirmed, truncated);

        var deadline = UtcNow() + grace;
        while (true)
        {
            // PULL the runner's own transcript before every check. This is the whole point: the
            // live event stream is not a reliable clock, and on the measured failure the records
            // sat unstored for 45s and only appeared when the session ended — i.e. the kill was
            // what produced the evidence that the kill was wrong. Waiting longer does not fix that
            // (90s was tried and still lost by 1.2s); asking the runner does.
            await _runtime.CatchUpTranscriptAsync(sessionId, ct);

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var outstanding = (await db.SessionQueuedMessages
                        .Where(m => messageIds.Contains(m.Id) && m.AgentSessionId == sessionId)
                        .ToListAsync(ct))
                    .Where(m => !confirmed.Contains(m.Id) && !truncated.Contains(m.Id))
                    .ToList();

                foreach (var message in outstanding)
                {
                    var late = await LateConfirmAttemptedMessagesAsync(db, sessionId, [message], ct);
                    if (late.Confirmed > 0)
                        confirmed.Add(message.Id);
                    else if (late.Truncated > 0)
                        truncated.Add(message.Id);
                }
            }

            if (confirmed.Count + truncated.Count == messageIds.Count)
                return (confirmed, truncated);

            if (UtcNow() >= deadline)
            {
                // Said out loud: the next thing that happens may be a kill, and if the record turns
                // up seconds later then the window — not the delivery — is what was wrong.
                _logger.LogWarning(
                    "Post-failure grace of {Grace}s expired for session {SessionId} with {Confirmed} of "
                    + "{Total} message(s) confirmed ({Truncated} truncated); proceeding to the failure path",
                    grace.TotalSeconds, sessionId, confirmed.Count, messageIds.Count, truncated.Count);
                return (confirmed, truncated);
            }

            // Deliberately slower than PollIntervalMs: each iteration fetches a whole transcript
            // over HTTP, and the thing being waited on is a runner round trip, not a DB commit.
            await Task.Delay(
                TimeSpan.FromMilliseconds(Math.Max(1000, _verification.PollIntervalMs)), _timeProvider, ct);
        }
    }

    // A fresh scope per poll: this runs outside any caller's DbContext and must see rows the
    // transcript ingestion path is committing from its own scope, concurrently.
    private async Task<TranscriptConfirm> TryFindConfirmingRecordAsync(
        Guid sessionId, string body, long baselineSequence, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await TryFindConfirmingRecordAsync(db, sessionId, body, baselineSequence, ct);
    }

    private static async Task<TranscriptConfirm> TryFindConfirmingRecordAsync(
        AppDbContext db, Guid sessionId, string body, long baselineSequence, CancellationToken ct)
    {
        var fullInline = await RequiresCompleteSpecialistPromptAsync(db, sessionId, body, ct);
        var texts = await db.TranscriptEntries
            .AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt
                    // CARD-0241: a completed ask_user_question update is a ToolResult whose
                    // text contains the typed option. CARD-0292's QueueEnqueue/Dequeue/Remove
                    // stay excluded — opposite policy, do not copy.
                    || (t.Kind == TranscriptKinds.ToolResult
                        && (t.ToolName == GrokQuestionTool.AskUserQuestionName
                            || (t.Text != null
                                && t.Text.StartsWith(GrokQuestionTool.CompletedAnswerPrefix)))))
                && (!fullInline || t.Kind == TranscriptKinds.UserPrompt)
                && t.Sequence > baselineSequence)
            .OrderBy(t => t.Sequence)
            .Select(t => t.Text)
            .ToListAsync(ct);

        foreach (var text in texts)
        {
            var match = TranscriptConfirm.Classify(body, text, fullInline);
            if (match.Identity)
                return match;
        }

        return TranscriptConfirm.None;
    }

    private async Task<bool> WaitForComposerEvidenceAsync(
        Guid sessionId, string screenBefore, string body, CancellationToken ct)
    {
        var deadline = UtcNow() + TimeSpan.FromSeconds(_verification.EvidenceTimeoutSeconds);
        while (true)
        {
            if (_runtime.TryGetLiveSnapshot(sessionId, out var after)
                && ComposerDeliveryEvidence.IsVisible(screenBefore, after.RenderedScreen, body))
            {
                return true;
            }

            if (UtcNow() >= deadline)
                return false;

            await Task.Delay(TimeSpan.FromMilliseconds(_verification.PollIntervalMs), _timeProvider, ct);
        }
    }

    private async Task<bool> WaitForSequenceAdvanceAsync(
        Guid sessionId, long baseline, CancellationToken ct, int? timeoutSeconds = null)
    {
        var seconds = timeoutSeconds ?? _verification.PostSubmitAdvanceTimeoutSeconds;
        var deadline = UtcNow() + TimeSpan.FromSeconds(Math.Max(1, seconds));
        while (true)
        {
            if (_runtime.TryGetLiveMetadata(sessionId, out var meta) && meta.LastSequence > baseline)
                return true;

            if (UtcNow() >= deadline)
                return false;

            await Task.Delay(TimeSpan.FromMilliseconds(_verification.PollIntervalMs), _timeProvider, ct);
        }
    }

    private readonly record struct SubmitBaseline(long? Sequence, string Screen);

    /// <summary>
    /// Lets the composer's final render frames finish before taking the output mark that the
    /// following Enter must beat. The bounded settle is intentionally shared by every verified
    /// provider; Codex is the measured case, but no provider benefits from crediting its body's
    /// own redraw to the submit key.
    /// </summary>
    private async Task<SubmitBaseline> SettlePostEvidenceAsync(Guid sessionId, CancellationToken ct)
    {
        var settleFor = TimeSpan.FromMilliseconds(Math.Clamp(_verification.PostEvidenceSettleMs, 0, 3_000));
        var deadline = UtcNow() + TimeSpan.FromSeconds(3);
        var lastChange = UtcNow();
        long? lastSequence = null;
        var screen = string.Empty;

        while (true)
        {
            if (_runtime.TryGetLiveSnapshot(sessionId, out var snapshot))
                screen = snapshot.RenderedScreen;

            if (!_runtime.TryGetLiveMetadata(sessionId, out var metadata))
                return new SubmitBaseline(null, screen);

            if (lastSequence != metadata.LastSequence)
            {
                lastSequence = metadata.LastSequence;
                lastChange = UtcNow();
            }

            var now = UtcNow();
            if (now - lastChange >= settleFor || now >= deadline)
                return new SubmitBaseline(lastSequence, screen);

            var remaining = deadline - now;
            var pollFor = TimeSpan.FromMilliseconds(Math.Max(1, _verification.PollIntervalMs));
            await Task.Delay(remaining < pollFor ? remaining : pollFor, _timeProvider, ct);
        }
    }

    /// <summary>
    /// CARD-0312 S1: arm the boot-reply watch off a delivery that reached the transcript. This is
    /// rung 5 being stamped by rung 4 — the first point at which "our bytes became a prompt" is
    /// ground truth, so it is the only honest place to start waiting for an answer.
    ///
    /// <para>The delivery-verified gate lives inside <c>BootReplyWatch.TryArmAsync</c>, not here,
    /// so this path and the sweep cannot disagree about which kinds are watchable at all.</para>
    ///
    /// <para>Never fatal: the arm is a convenience over the sweep, which re-derives an unarmed
    /// watch from the same predicate on its next tick.</para>
    /// </summary>
    private async Task ArmBootReplyWatchAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        try
        {
            await BootReplyWatch.TryArmAsync(
                db, sessionId, _delegationSettings.BootModelWaitDeadlineMinutes, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(
                ex, "Could not arm the boot-reply watch on session {SessionId}", sessionId);
        }
    }

    private async Task<bool> IsVerifiedDeliverySessionAsync(Guid sessionId, CancellationToken ct)
    {
        // Claude, Grok and Codex all echo the composer's content on the rendered screen (Grok
        // measured 1.0.5, CARD-0080 S1: typed and pasted bodies render, no placeholder collapse at
        // 4.4 KB; Codex measured 0.147.0, CARD-0099 S1: a typed body renders and Enter on an empty
        // composer submits nothing) and all three have a structured transcript for CARD-0055's
        // confirm to poll — Grok's rows come from its ACP updates.jsonl (CARD-0080 S2), Codex's
        // from its rollout JSONL (CARD-0099 S1). OpenCode/Raw sessions deliver blind.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var kind = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (AgentKind?)s.AgentKind)
            .FirstOrDefaultAsync(ct);
        return kind is { } k
            && ProviderContractCatalog.For(k).DeliveryVerification.State
                == AgentTuiCapabilityState.Supported;
    }

    /// <summary>
    /// CARD-0024: identity matched but the stored UserPrompt does not contain the full body.
    /// The submit happened — re-typing would double-send, killing would abort a live turn —
    /// so park immediately, raise <see cref="AgentIncidentKind.TruncatedTerminalDelivery"/>,
    /// and leave the session alone. Deduped on the message id so a later late-confirm of the
    /// same splice does not raise a second row.
    /// </summary>
    private async Task HandleTruncationAsync(
        Guid sessionId,
        IReadOnlyList<Guid>? messageIds,
        string body,
        string? recordText,
        CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);

            if (messageIds is { Count: > 0 })
            {
                var messages = await db.SessionQueuedMessages
                    .Where(m => messageIds.Contains(m.Id) && m.AgentSessionId == sessionId)
                    .ToListAsync(ct);
                foreach (var message in messages)
                {
                    if (message.Status == QueuedMessageStatus.Sent)
                    {
                        message.Status = QueuedMessageStatus.Pending;
                        message.SentAt = null;
                    }

                    message.DeliveryAttempts = Math.Max(message.DeliveryAttempts, MaxAttempts);
                    message.DeliveryVerdict = DeliveryVerdict.Truncated;
                    message.DeliveryVerdictAt = UtcNow();
                }
            }

            var sentLen = PromptSubmissionMatch.Normalize(body).Length;
            var recordedLen = PromptSubmissionMatch.Normalize(recordText ?? string.Empty).Length;

            if (agent is not null)
            {
                var keys = messageIds is { Count: > 0 }
                    ? messageIds.Select(id => id.ToString("D")).ToList()
                    : [$"now:{sessionId:D}"];
                var failureReason = string.Join(",", keys.OrderBy(k => k, StringComparer.Ordinal));
                var already = await db.AgentIncidents
                    .Where(i => i.AgentId == agent.Id
                        && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery
                        && i.FailureReason != null)
                    .Select(i => i.FailureReason!)
                    .ToListAsync(ct);
                var covered = keys.All(key =>
                    already.Any(reason => reason.Contains(key, StringComparison.Ordinal)));

                if (!covered)
                {
                    var channelBound = await db.ChatChannels.AnyAsync(c => c.AgentId == agent.Id, ct);
                    var severity = channelBound ? AlertSeverity.Critical : AlertSeverity.Warning;
                    var detail =
                        $"A {sentLen:N0}-character message reached this terminal as {recordedLen:N0} "
                        + "characters in the UserPrompt record (normalized). The submit happened — the "
                        + "body is a splice. The message is PARKED; it will not be re-typed (that would "
                        + "send a second copy). The session was not restarted."
                        + (channelBound
                            ? " This agent is channel-bound: someone is waiting on a reply."
                            : string.Empty);

                    var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                    await supervisor.RecordIncidentAsync(
                        agent.Id, sessionId, AgentIncidentKind.TruncatedTerminalDelivery, severity,
                        detail, failureReason: failureReason, ct: ct);
                }
            }

            await db.SaveChangesAsync(ct);

            if (agent is not null)
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);

            _logger.LogWarning(
                "Delivery to session {SessionId} was truncated (sent {Sent} normalized chars, "
                + "recorded {Recorded}); agent={AgentName}, parked={Parked}, killed=false",
                sessionId, sentLen, recordedLen, agent?.Name ?? "<none>",
                messageIds is { Count: > 0 });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to handle truncated delivery for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// CARD-0137 S3 / L0: a body whose first token is in this kind's
    /// <see cref="LocalCommandContract.Forbidden"/> map. Nothing was typed — retrying is
    /// pointless — so park immediately, raise <see cref="AgentIncidentKind.ForbiddenTerminalBody"/>
    /// at Error (never Critical), and never kill.
    /// </summary>
    private async Task HandleForbiddenBodyAsync(
        Guid sessionId,
        IReadOnlyList<Guid>? messageIds,
        string body,
        string? reason,
        CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);

            if (messageIds is { Count: > 0 })
            {
                var messages = await db.SessionQueuedMessages
                    .Where(m => messageIds.Contains(m.Id) && m.AgentSessionId == sessionId)
                    .ToListAsync(ct);
                foreach (var message in messages)
                {
                    if (message.Status == QueuedMessageStatus.Sent)
                    {
                        message.Status = QueuedMessageStatus.Pending;
                        message.SentAt = null;
                    }

                    message.DeliveryAttempts = Math.Max(message.DeliveryAttempts, MaxAttempts);
                    message.DeliveryVerdict = DeliveryVerdict.ForbiddenBody;
                    message.DeliveryVerdictAt = UtcNow();
                }
            }

            var token = FirstCommandToken(body);
            var detailReason = string.IsNullOrWhiteSpace(reason)
                ? "this body is forbidden for this agent kind"
                : reason;

            if (agent is not null)
            {
                var keys = messageIds is { Count: > 0 }
                    ? messageIds.Select(id => id.ToString("D")).ToList()
                    : [$"now:{sessionId:D}"];
                var failureReason = string.Join(",", keys.OrderBy(k => k, StringComparer.Ordinal));
                var already = await db.AgentIncidents
                    .Where(i => i.AgentId == agent.Id
                        && i.Kind == AgentIncidentKind.ForbiddenTerminalBody
                        && i.FailureReason != null)
                    .Select(i => i.FailureReason!)
                    .ToListAsync(ct);
                var covered = keys.All(key =>
                    already.Any(existing => existing.Contains(key, StringComparison.Ordinal)));

                if (!covered)
                {
                    var parked = messageIds is { Count: > 0 };
                    var detail =
                        $"Refused to type '{token}' into this terminal: {detailReason}. "
                        + "Nothing was written. The session was not restarted."
                        + (parked
                            ? " The message is PARKED; retrying a body we refuse to type is pointless."
                            : string.Empty);

                    var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                    await supervisor.RecordIncidentAsync(
                        agent.Id, sessionId, AgentIncidentKind.ForbiddenTerminalBody, AlertSeverity.Error,
                        detail, failureReason: failureReason, ct: ct);
                }
            }

            await db.SaveChangesAsync(ct);

            if (agent is not null)
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

            await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);

            _logger.LogWarning(
                "Delivery to session {SessionId} refused forbidden body '{Token}'; agent={AgentName}, "
                + "parked={Parked}, killed=false, reason={Reason}",
                sessionId, token, agent?.Name ?? "<none>",
                messageIds is { Count: > 0 }, detailReason);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to handle forbidden body for session {SessionId}", sessionId);
        }
    }

    // Verification failed: return the message to the queue (never silently lose it), record an
    // incident against the owning agent (which also raises an alert), and for always-on agents kill
    // the wedged session — the supervisor's ladder restarts it (resuming the SAME session row, so
    // the reverted message redelivers via the stranded-queue watchdog), and the kill guarantees a
    // fresh composer so redelivery cannot double-type.
    //
    // CARD-0055 adds two brakes to that kill. A session that is now WORKING is evidence the submit
    // may have succeeded with the matcher blind — killing it would abort a live turn to settle a
    // bookkeeping doubt, so it is left alone and the next turn-end flush late-confirms the message.
    // The guard covers every verdict, not just NoTranscriptRecord, and costs no wedge recovery: a
    // session that reads working already blocks every QUEUED delivery, so the only deliveries that
    // can reach one are human-initiated (Now-mode / send-now), where killing is plainly wrong.
    // And a message that has hit MaxDeliveryAttempts PARKS: still Pending and visible in the queue
    // UI, but no automatic path types it again, and the incident escalates to Critical when the
    // agent is channel-bound, because a parked channel reply is a human waiting on a dead line.
    //
    // CARD-0103 adds a THIRD brake, narrower than both: a NoComposerEvidence verdict on a session
    // that has never produced a transcript row, inside PreFirstTurnNoEvidenceGraceMinutes of the
    // message being enqueued, refunds the attempt, withholds the kill and reports ONE Warning. That
    // is not a wedged session, it is a Claude TUI that is painted but not yet draining stdin — a
    // state measured at 48-200 seconds under load, i.e. wide enough to swallow the whole 3-attempt
    // budget in ~2.5 minutes and park a brief in a session that was healthy the entire time. The
    // refund only ever applies to that triple condition; "started working and then stalled" keeps
    // CARD-0055's behaviour exactly, because that session HAS a baseline.
    private async Task<DateTime?> CaptureSessionGenerationAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generation = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (DateTime?)s.StartedAt)
            .FirstOrDefaultAsync(ct);
        return generation is { } value ? SessionGeneration.Normalize(value) : null;
    }

    // Internal for controlled failure-handoff coverage: a missing captured token must never
    // be replaced with the row's current generation here, even though new Enter-only calls capture it.
    internal async Task HandleDeliveryFailureAsync(
        Guid sessionId, IReadOnlyList<Guid>? messageIds, DeliveryVerdict verdict, CancellationToken ct,
        DateTime? capturedGeneration = null, bool enterOnlyRecovery = false)
    {
        if (messageIds is { Count: > 0 })
        {
            await using var rulesScope = _scopeFactory.CreateAsyncScope();
            var rulesDb = rulesScope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await rulesDb.SessionQueuedMessages.AnyAsync(m => messageIds.Contains(m.Id) && m.RulesRefreshKey != null, ct))
                return; // Rules deadlines own failure; never kill an established session here.
        }
        try
        {
            // Last look for the evidence before anything destructive happens. A NoTranscriptRecord
            // verdict says our ingestion had not caught up inside the confirm window, which is NOT
            // the same claim as "the submit failed" — and the difference is a session's life. See
            // DeliveryVerificationSettings.PostFailureConfirmGraceSeconds for the measured miss.
            // A truncated classification during grace is handled (park + incident), not confirmed:
            // it must not fall through as NoTranscriptRecord and kill the session.
            HashSet<Guid> lateConfirmed = [];
            HashSet<Guid> lateTruncated = [];
            if (verdict == DeliveryVerdict.NoTranscriptRecord && messageIds is { Count: > 0 })
                (lateConfirmed, lateTruncated) = await GraceConfirmAsync(sessionId, messageIds, ct);
            if (messageIds is { Count: > 0 }
                && lateConfirmed.Count + lateTruncated.Count == messageIds.Count)
            {
                if (lateTruncated.Count == 0)
                {
                    _logger.LogInformation(
                        "Delivery to session {SessionId} verified late: all {Count} message(s) reached the "
                        + "transcript within the post-failure grace window. No incident, and the session is "
                        + "NOT restarted — it took the message correctly, our ingestion was just behind",
                        sessionId, messageIds.Count);
                }
                else
                {
                    _logger.LogWarning(
                        "Delivery to session {SessionId} classified {Truncated} message(s) as truncated "
                        + "during the post-failure grace window ({Confirmed} confirmed). The session is "
                        + "NOT restarted — the submit happened",
                        sessionId, lateTruncated.Count, lateConfirmed.Count);
                }
                await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var agent = await db.Agents.FirstOrDefaultAsync(
                a => a.PersistentSessionId == sessionId.ToString("D"), ct);

            // Revert the whole failed batch (null = Now-mode, nothing persisted to revert), minus
            // anything the grace window just proved landed. The attempt metadata deliberately
            // survives the revert — it is the retry brake.
            var parked = 0;
            var canceledSupervision = 0;
            var allSupervision = false;
            var refunded = 0;
            var preFirstTurn = false;
            var persistBlind = false;
            AgentSessionRuntime.TranscriptPersistFailure? persistFailure = null;
            DateTime? refundOldestCreatedAt = null;
            if (messageIds is { Count: > 0 })
            {
                var messages = await db.SessionQueuedMessages
                    .Where(m => messageIds.Contains(m.Id) && m.AgentSessionId == sessionId)
                    .ToListAsync(ct);
                messages = messages
                    .Where(m => !lateConfirmed.Contains(m.Id) && !lateTruncated.Contains(m.Id))
                    .ToList();

                if (verdict == DeliveryVerdict.NoSubmitOutput
                    && await TryHandleBootWedgeAsync(sessionId, messages, agent, db, scope, ct))
                {
                    await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
                    return;
                }

                var reverting = messages.Where(m => m.Status == QueuedMessageStatus.Sent).ToList();
                // CARD-0561: capture SentAt before the revert nulls it — persistBlind compares
                // the mark against the attempt, not the reverted row.
                var sentAtById = reverting.ToDictionary(m => m.Id, m => m.SentAt);
                var verdictAt = UtcNow();
                foreach (var message in messages)
                {
                    message.DeliveryVerdict = verdict;
                    message.DeliveryVerdictAt = verdictAt;
                }

                foreach (var message in reverting)
                {
                    message.Status = QueuedMessageStatus.Pending;
                    message.SentAt = null;
                }

                // CARD-0103: the attempt is REFUNDED on the same revert, for one shape only. The
                // stamp-before-type crash-safety is untouched — a crash still leaves the attempt
                // charged, because only an OBSERVED verdict can reach here — and the counter stays
                // the honest thing every DeliveryAttempts < MaxAttempts predicate reads.
                //
                // Why it is safe on this verdict and no other: NoComposerEvidence means the body
                // never rendered, so the submitting Enter was withheld and nothing can have been
                // submitted. Why it is right: a session with zero transcript rows at type time has
                // not taken a single turn yet, and a Claude TUI that is painted but not yet draining
                // stdin was measured deaf for 48-200 seconds (2026-08-20) — long enough to spend all
                // three attempts on a session that is perfectly healthy and merely still waking.
                // A session that DID start working and then stalled has a non-null baseline and is
                // deliberately left to CARD-0055's original design.
                if (verdict == DeliveryVerdict.NoComposerEvidence)
                {
                    var grace = TimeSpan.FromMinutes(
                        Math.Max(0, _verification.PreFirstTurnNoEvidenceGraceMinutes));
                    var now = UtcNow();
                    foreach (var message in reverting)
                    {
                        if (message.LastDeliveryBaselineSequence is not null
                            || message.DeliveryAttempts <= 0
                            || now - message.CreatedAt >= grace)
                            continue;

                        message.DeliveryAttempts--;
                        refunded++;
                        refundOldestCreatedAt = refundOldestCreatedAt is { } oldest && oldest < message.CreatedAt
                            ? oldest
                            : message.CreatedAt;
                    }

                    // All-or-nothing for the kill and the incident: a mixed run (one pre-first-turn
                    // row batched with one that is past its grace) is not the shape this covers, and
                    // the destructive default is the safer place to land.
                    preFirstTurn = reverting.Count > 0 && refunded == reverting.Count;
                }

                // CARD-0561: NoTranscriptRecord means "no UserPrompt row", and a UserPrompt row
                // cannot exist when this session's transcript store has been refusing rows. That is
                // a blind matcher, not a dead session: withhold the kill, refund the attempt, and
                // let the stranded sweep retry once persistence recovers. Same refund shape as
                // CARD-0103; same all-or-nothing rule for the kill.
                var clockTolerance = TimeSpan.FromSeconds(30);
                if (verdict == DeliveryVerdict.NoTranscriptRecord
                    && reverting.Count > 0
                    && _runtime.TryGetTranscriptPersistFailure(sessionId, out var failure)
                    && reverting.All(m =>
                        sentAtById.TryGetValue(m.Id, out var sent)
                        && sent is { } sentAt
                        && failure.LastFailedAtUtc >= sentAt - clockTolerance))
                {
                    persistBlind = true;
                    persistFailure = failure;
                    foreach (var message in reverting)
                    {
                        if (message.DeliveryAttempts > 0)
                            message.DeliveryAttempts--;
                        refunded++;
                        refundOldestCreatedAt = refundOldestCreatedAt is { } oldest && oldest < message.CreatedAt
                            ? oldest
                            : message.CreatedAt;
                    }
                }

                allSupervision = messages.Count > 0
                    && messages.All(m => m.Origin == QueuedMessageOrigin.Supervision);
                foreach (var message in messages.Where(m =>
                             m.Origin == QueuedMessageOrigin.Supervision
                             && m.DeliveryAttempts >= MaxAttempts))
                {
                    // Cancel-not-park (CARD-0082): parking exists for human-owed content. An
                    // auto-compact that spent its attempts is dropped; a later sweep re-derives.
                    message.Status = QueuedMessageStatus.Canceled;
                    message.CanceledAt = UtcNow();
                    canceledSupervision++;
                }

                parked = messages.Count(m =>
                    m.Origin != QueuedMessageOrigin.Supervision && m.DeliveryAttempts >= MaxAttempts);
            }

            // Asked before the kill decision AND before the incident text is written, so both tell
            // the same story. Never kill over a Supervision compact — the session may be the
            // operator's own live conversation (CARD-0056 re-adoption).
            var working = await IsWorkingAsync(db, sessionId, ct);
            // CARD-0103 withholds the always-on kill for the refunded shape too: the fresh composer
            // a kill buys is worthless against a TUI that has not started reading, and killing and
            // relaunching straight back into the same race is CARD-0047's restart loop by another
            // route. The message stays Pending and the 60s stranded sweep retries it.
            var kill = agent is { AlwaysOn: true } && !working && !allSupervision && !preFirstTurn && !persistBlind
                && verdict is not (DeliveryVerdict.ForbiddenBody
                    or DeliveryVerdict.LocalCommandNotAccepted
                    or DeliveryVerdict.BackendUnreachable);

            if (allSupervision && canceledSupervision > 0)
            {
                var compactMessage =
                    $"Idle auto-compact delivery could not be verified ({Describe(verdict)}) after "
                    + $"{MaxAttempts} attempt(s) and was canceled rather than parked.";
                if (agent is not null)
                {
                    var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                    await supervisor.RecordIncidentAsync(
                        agent.Id, sessionId, AgentIncidentKind.AutoCompactFailed, AlertSeverity.Warning,
                        compactMessage, failureReason: "DeliveryFailed", ct: ct);
                }
                else
                {
                    _logger.LogWarning(
                        "AUTO-COMPACT FAILED on unclaimed session {SessionId}: {Message}",
                        sessionId, compactMessage);
                    var alerts = scope.ServiceProvider.GetService<IAlertService>();
                    if (alerts is not null)
                    {
                        await alerts.RaiseAsync(
                            new AlertRaise(
                                AlertSeverity.Warning,
                                Source: "supervisor",
                                Title: $"{AgentIncidentKind.AutoCompactFailed}: idle auto-compact",
                                Detail: compactMessage,
                                DedupKey: ContextCompactionService.AutoCompactFailedDedupKey(sessionId),
                                AgentId: null,
                                SessionId: sessionId),
                            ct);
                    }
                }
            }
            else if (persistBlind && agent is not null && persistFailure is not null)
            {
                var since = refundOldestCreatedAt ?? UtcNow();
                var alreadyReported = await db.AgentIncidents.AnyAsync(
                    i => i.SessionId == sessionId
                        && i.Kind == AgentIncidentKind.DeliveryVerificationFailed
                        && i.CreatedAt >= since,
                    ct);
                if (!alreadyReported)
                {
                    var channelBound = await db.ChatChannels.AnyAsync(c => c.AgentId == agent.Id, ct);
                    var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                    await supervisor.RecordIncidentAsync(
                        agent.Id, sessionId, AgentIncidentKind.DeliveryVerificationFailed,
                        channelBound ? AlertSeverity.Error : AlertSeverity.Warning,
                        $"Message delivery could not be verified: {Describe(verdict)}. Transcript "
                        + $"persistence for this session failed {persistFailure.Failures} time(s), last at "
                        + $"{persistFailure.LastFailedAtUtc:u} ({persistFailure.Detail}); the matcher was blind, "
                        + "so the session was NOT restarted and the attempt was refunded.",
                        ct: ct);
                }
            }
            else if (preFirstTurn && agent is not null)
            {
                // One Warning, not one Error per attempt. Today's signature was six sessions x 3
                // attempts of Error spam describing a race none of them caused; the fault is real
                // but it is ONE fault per message, and it self-heals on the next sweep.
                var since = refundOldestCreatedAt ?? UtcNow();
                var alreadyReported = await db.AgentIncidents.AnyAsync(
                    i => i.SessionId == sessionId
                        && i.Kind == AgentIncidentKind.DeliveryVerificationFailed
                        && i.CreatedAt >= since,
                    ct);
                if (!alreadyReported)
                {
                    var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                    await supervisor.RecordIncidentAsync(
                        agent.Id, sessionId, AgentIncidentKind.DeliveryVerificationFailed,
                        AlertSeverity.Warning,
                        $"Message delivery could not be verified: {Describe(verdict)}. The session has "
                        + "produced no transcript activity at all, so it is still becoming "
                        + "input-responsive rather than wedged (CARD-0103): the attempt was refunded "
                        + "instead of charged, the session was NOT restarted, and the stranded-queue "
                        + $"watchdog retries within ~{Math.Max(1, _verification.StrandedAgeSeconds)}s. "
                        + $"Past {Math.Max(0, _verification.PreFirstTurnNoEvidenceGraceMinutes)} minutes "
                        + "from enqueue, attempts charge normally and the message parks.",
                        ct: ct);
                }
            }
            else if (agent is not null)
            {
                var channelBound = await db.ChatChannels.AnyAsync(c => c.AgentId == agent.Id, ct);
                var severity = parked > 0 && channelBound ? AlertSeverity.Critical : AlertSeverity.Error;
                var fate = parked > 0
                    ? $" It has now failed {MaxAttempts} delivery attempts and is PARKED in the queue"
                      + " for a human — nothing will retry it automatically."
                      + (channelBound ? " This agent is channel-bound: someone is waiting on a reply." : string.Empty)
                    : working
                        ? " The session is mid-turn, so the submit may have succeeded unseen. The message"
                          + " stays queued and is re-checked against the transcript before any redelivery."
                        : " The message has been returned to the queue.";
                var restart = kill
                    ? " Restarting the session; a fresh composer is what makes redelivery safe."
                    : agent.AlwaysOn && working
                        ? " The session was NOT restarted — killing it would abort a live turn."
                        : string.Empty;
                var detail = (enterOnlyRecovery
                    ? " This was after an Enter-only recovery: the composer showed the body's head but no prompt was recorded."
                    : string.Empty) + fate + restart;

                var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                await supervisor.RecordIncidentAsync(
                    agent.Id, sessionId, AgentIncidentKind.DeliveryVerificationFailed, severity,
                    $"Message delivery could not be verified: {Describe(verdict)}." + detail,
                    ct: ct);
            }

            await db.SaveChangesAsync(ct);

            if (agent is not null)
            {
                await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
                if (kill)
                {
                    if (capturedGeneration is not { } generation)
                    {
                        _logger.LogWarning(
                            "Delivery to session {SessionId} failed verification but the attempt retained no generation; declining the recovery kill.",
                            sessionId);
                        if (agent is not null)
                        {
                            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                            await supervisor.RecordIncidentAsync(
                                agent.Id, sessionId, AgentIncidentKind.DeliveryVerificationFailed,
                                AlertSeverity.Warning,
                                "Destructive recovery was declined: this delivery attempt retained no accepted generation.",
                                ct: ct);
                            await db.SaveChangesAsync(ct);
                        }
                    }
                    else
                    {
                        var sessions = scope.ServiceProvider.GetRequiredService<AgentSessionService>();
                        await sessions.KillGenerationAsync(
                            sessionId, generation, SessionTerminationSource.SystemRequest, ct);
                    }
                }
            }

            _logger.LogWarning(
                "Delivery to session {SessionId} failed verification ({Verdict}); agent={AgentName}, "
                + "alwaysOn={AlwaysOn}, working={Working}, killed={Killed}, parked={Parked}, "
                + "canceledSupervision={CanceledSupervision}, refundedAttempts={Refunded}",
                sessionId, verdict, agent?.Name ?? "<none>", agent?.AlwaysOn ?? false, working, kill,
                parked, canceledSupervision, refunded);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to handle delivery verification failure for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// CARD-0299 S2: a cold delegate's first delivery returned NoSubmitOutput. Cancel the
    /// queue row (CARD-0117: Pending on a dead session is a stranded retry), kill, and relaunch
    /// once. Returns true when the conjunction matched and this method owned the failure.
    /// Mode:Now has no row and never reaches here.
    ///
    /// <para><b>CARD-0312 S4 generalised the KIND, and nothing else.</b> The trigger was
    /// <c>AgentKind.Codex</c> because that is where the shape was measured (3 of 55 sessions,
    /// 5.5%), but "the composer holds the brief and Enter produced no output" is not a Codex fact
    /// — it is a fact about a TUI, and Grok's swallowed Enter produces the identical row. The gate
    /// is now "the kind's delivery is transcript-verified", i.e. exactly the kinds whose
    /// <c>NoSubmitOutput</c> verdict means anything. Everything downstream — the cancel, the
    /// incident, the kill, <c>RelaunchWedgedAsync</c>, <c>FailWedgedAtLimitAsync</c>, the limit and
    /// the durable counter — is untouched, and Codex's outcome is byte-identical (the MCP-boot
    /// clause stays Codex-only because <c>CodexMcpBoot</c> is a Codex screen fact).</para>
    ///
    /// <para><b>This is not the boot-REPLY watch.</b> That one (CARD-0312/CARD-0353) fires when
    /// the brief DID become a prompt and the model never answered; this one fires when the brief
    /// never became a prompt at all. Mutually exclusive by construction, and deliberately so —
    /// the third overlapping mechanism is what CARD-0312's plan forbids.</para>
    /// </summary>
    private async Task<bool> TryHandleBootWedgeAsync(
        Guid sessionId,
        List<SessionQueuedMessage> messages,
        Agent? agent,
        AppDbContext db,
        IServiceScope scope,
        CancellationToken ct)
    {
        if (messages.Count == 0
            || messages.Any(m => m.Origin != QueuedMessageOrigin.Delegation
                || m.DeliveryAttempts != 1
                || m.LastDeliveryBaselineSequence is not null))
            return false;

        var session = await db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not { Status: SessionStatus.Running })
            return false;
        if (ProviderContractCatalog.For(session.AgentKind).DeliveryVerification.State
            != AgentTuiCapabilityState.Supported)
        {
            return false;
        }

        var task = await db.AgentTasks
            .Where(t => t.AgentSessionId == sessionId && t.Status == AgentTaskStatus.Dispatched)
            .OrderByDescending(t => t.DispatchedAt)
            .FirstOrDefaultAsync(ct);
        if (task is null)
            return false;

        var dispatcher = scope.ServiceProvider.GetService<AgentTaskDispatcher>();
        if (dispatcher is null)
            return false;

        var pending = scope.ServiceProvider.GetService<BootWedgeRelaunchState>();
        pending?.Mark(task.Id);

        var now = UtcNow();
        foreach (var message in messages)
        {
            message.Status = QueuedMessageStatus.Canceled;
            message.CanceledAt = now;
        }

        var screen = _runtime.TryGetLiveSnapshot(sessionId, out var snap) ? snap.RenderedScreen : "";
        var mcpVisible = session.AgentKind == AgentKind.Codex && CodexMcpBoot.IsVisible(screen);
        var already = await db.AgentIncidents.AnyAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.BootWedged, ct);
        if (agent is not null && !already)
        {
            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            var detail = "TUI stopped painting; brief still in composer"
                + (mcpVisible ? "; MCP boot line still visible" : "")
                + ".";
            await supervisor.RecordIncidentAsync(
                agent.Id, sessionId, AgentIncidentKind.BootWedged, AlertSeverity.Warning,
                detail, ct: ct);
        }

        await db.SaveChangesAsync(ct);

        var sessions = scope.ServiceProvider.GetRequiredService<AgentSessionService>();
        try
        {
            await sessions.KillAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not kill boot-wedged session {SessionId}", sessionId);
        }

        var limit = Math.Max(0, _delegationSettings.BootWedgeRelaunchLimit);
        if (task.BootWedgeRelaunchCount >= limit)
        {
            await dispatcher.FailWedgedAtLimitAsync(task.Id, sessionId, ct);
            pending?.Forget(task.Id);
            return true;
        }

        await dispatcher.RelaunchWedgedAsync(task.Id, sessionId, ct);
        pending?.Forget(task.Id);
        return true;
    }

    /// <summary>
    /// CARD-0281: Channel and Scheduled rows stay Pending behind a terminal provider-capacity
    /// recovery (WallModelPaused / WallParked / NeedsHuman / UnknownExhausted / WallUnparsed)
    /// newer than the last UserPrompt. Supervision and Ui still deliver — an operator's
    /// typed /login or a human resume is what un-sticks it.
    /// </summary>
    private async Task<List<SessionQueuedMessage>> ApplyCapacityHoldAsync(
        AppDbContext db, Guid sessionId, List<SessionQueuedMessage> pending, CancellationToken ct)
    {
        var held = pending
            .Where(m => m.Origin is QueuedMessageOrigin.Channel or QueuedMessageOrigin.Scheduled)
            .ToList();
        if (held.Count == 0)
            return pending;

        if (!await HasTerminalCapacityHoldAsync(db, sessionId, ct))
            return pending;

        if (_capacityRecovery is { IsEnabled: true })
        {
            var wait = await db.CapacityRecoveryWaits
                .FirstOrDefaultAsync(
                    w => w.SessionId == sessionId
                        && w.State != CapacityRecoveryWaitState.Progressed
                        && w.State != CapacityRecoveryWaitState.Canceled
                        && w.State != CapacityRecoveryWaitState.Superseded
                        && w.State != CapacityRecoveryWaitState.Exhausted,
                    ct);
            if (wait is not null)
            {
                var grant = await db.Set<CapacityRecoveryProviderState>()
                    .FirstOrDefaultAsync(s => s.Kind == wait.ExecutionKind, ct);
                if (grant is { GrantedWaitId: { } grantedId, GrantedActionKey: { } grantedKey }
                    && grantedId == wait.Id
                    && grantedKey == wait.ActionKey)
                {
                    var chosen = held
                        .Where(m => m.CapacityRecoveryActionKey == grantedKey
                            || m.CapacityRecoveryActionKey == null)
                        .OrderBy(m => m.CreatedAt)
                        .ThenBy(m => m.Sequence)
                        .FirstOrDefault();
                    if (chosen is not null && chosen.CapacityRecoveryActionKey is null)
                    {
                        chosen.CapacityRecoveryActionKey = grantedKey;
                        chosen.CapacityWaitId = wait.Id;
                        chosen.CapacityWaitVersion = wait.Version;
                        wait.SelectedMessageId = chosen.Id;
                        await db.SaveChangesAsync(ct);
                    }
                }
            }
        }

        var selectedKeys = held
            .Where(m => !string.IsNullOrEmpty(m.CapacityRecoveryActionKey))
            .Select(m => m.CapacityRecoveryActionKey!)
            .Distinct()
            .ToList();
        var selected = new List<SessionQueuedMessage>();
        if (selectedKeys.Count > 0)
        {
            var waits = await db.CapacityRecoveryWaits.AsNoTracking()
                .Where(w => selectedKeys.Contains(w.ActionKey)
                    && w.State != CapacityRecoveryWaitState.Exhausted
                    && w.State != CapacityRecoveryWaitState.Canceled)
                .ToListAsync(ct);
            var granted = waits.Select(w => w.ActionKey).ToHashSet(StringComparer.Ordinal);
            selected = held
                .Where(m => m.CapacityRecoveryActionKey is { } key && granted.Contains(key))
                .OrderBy(m => m.Sequence)
                .ToList();
        }

        var stamped = false;
        foreach (var message in held)
        {
            if (selected.Any(s => s.Id == message.Id))
                continue;
            if (string.IsNullOrEmpty(message.NoteHeader))
            {
                message.NoteHeader = "Held";
                stamped = true;
            }
        }

        if (stamped)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Holding {Count} Channel/Scheduled queue row(s) on session {SessionId}: "
                + "terminal provider-capacity recovery is in effect",
                held.Count - selected.Count, sessionId);
        }

        var others = pending
            .Where(m => m.Origin is not (QueuedMessageOrigin.Channel or QueuedMessageOrigin.Scheduled))
            .ToList();
        if (selected.Count == 0)
            return others;
        others.AddRange(selected);
        return others;
    }

    internal static async Task<bool> HasTerminalCapacityHoldAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var lastPromptSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt)
            .MaxAsync(t => (long?)t.Sequence, ct) ?? 0;

        return await db.ApiErrorRecoveries
            .AnyAsync(r => r.AgentSessionId == sessionId
                && r.StubSequence > lastPromptSeq
                && r.ResolvedReason != null
                && (r.ResolvedReason == ApiErrorRecoveryReasons.WallModelPaused
                    || r.ResolvedReason == ApiErrorRecoveryReasons.WallParked
                    || r.ResolvedReason == ApiErrorRecoveryReasons.NeedsHuman
                    || r.ResolvedReason == ApiErrorRecoveryReasons.UnknownExhausted
                    || r.ResolvedReason == ApiErrorRecoveryService.WallUnparsedFailureReason), ct);
    }

    // Internal so the agent list/detail can surface the SAME working signal on agent cards —
    // "Working" on a card must mean mid-turn right now, not merely "session started".
    internal static async Task<bool> IsWorkingAsync(AppDbContext db, Guid sessionId, CancellationToken ct) =>
        (await IsWorkingBatchAsync(db, [sessionId], ct))[sessionId];

    internal static Task<IReadOnlyDictionary<Guid, bool>> IsWorkingBatchAsync(
        AppDbContext db, IReadOnlyCollection<Guid> sessionIds, CancellationToken ct) =>
        TranscriptWorkingStateQuery.ReadAsync(db, sessionIds, ct);

    /// <param name="maxAttempts">
    /// The parking threshold, passed in rather than read here so the flag is decided by the SAME
    /// setting the attention projection reads (CARD-0035 slice 4). Parking is not a status: a parked
    /// message is Pending like any other, and a queue that could not say so showed CARD-0055's
    /// parked messages as ordinary pending ones — visible, and silently never going anywhere.
    /// </param>
    private static async Task<SessionQueueDto> BuildQueueDtoAsync(
        AppDbContext db, Guid sessionId, int maxAttempts, CancellationToken ct)
    {
        var messages = await db.SessionQueuedMessages
            .AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
            .OrderBy(m => m.Sequence)
            .Select(m => new QueuedMessageDto(
                m.Id,
                m.Sequence,
                m.Body,
                m.Status.ToString(),
                m.CreatedAt,
                m.DeliveryAttempts,
                m.Origin.ToString(),
                m.DeliveryAttempts >= maxAttempts,
                m.NoteHeader,
                false))
            .ToListAsync(ct);
        var working = await IsWorkingAsync(db, sessionId, ct);
        var session = await db.AgentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var modalBlocked = false;
        if (session is not null)
        {
            var generation = SessionGeneration.Normalize(session.StartedAt);
            modalBlocked = await db.RemoteControlModalEpisodes.AsNoTracking().AnyAsync(
                e => e.SessionId == sessionId
                    && e.AcceptedStartedAt == generation
                    && e.ResolvedAt == null, ct);
        }

        var dtos = modalBlocked
            ? messages.Select(m => m with { ModalBlocked = true }).ToList()
            : messages;
        return new SessionQueueDto(
            sessionId,
            dtos,
            working,
            ModalBlocked: modalBlocked,
            ModalBlockedReason: modalBlocked ? "Remote Control menu blocks input" : null);
    }

    internal async Task<bool> IsModalBlockedAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await IsModalBlockedLockedAsync(db, sessionId, ct);
    }

    internal static async Task<bool> IsModalBlockedLockedAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var session = await db.AgentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return false;
        var generation = SessionGeneration.Normalize(session.StartedAt);
        return await db.RemoteControlModalEpisodes.AsNoTracking().AnyAsync(
            e => e.SessionId == sessionId
                && e.AcceptedStartedAt == generation
                && e.ResolvedAt == null, ct);
    }

    private async Task<AgentSession> RequireSessionAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(nameof(AgentSession), sessionId);
    }

    private async Task RequireImmediateAdmissionAsync(AgentSession session, CancellationToken ct)
    {
        if (!_runtime.IsLiveOrUnknown(session))
            throw new ConflictException($"Agent session '{session.Id}' is not live; cannot send now.");
        // An active remote Starting row still gets the transport refusal before terminal readiness.
        await _runtime.EnsureInputTransportAvailableAsync(session, ct);
        if (session.Status != SessionStatus.Running)
            throw new ConflictException($"Agent session '{session.Id}' is still starting; its terminal is not ready for input yet.");
    }

    private async Task<AgentKind?> TryGetSessionKindAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (AgentKind?)s.AgentKind)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// First whitespace-delimited token of <paramref name="body"/>, used to match
    /// <see cref="LocalCommandContract.Forbidden"/> and <see cref="LocalCommandContract.Commands"/>.
    /// <c>/usage --json</c> matches <c>/usage</c>; <c>/compact Focus the summary…</c> matches
    /// <c>/compact</c>.
    /// </summary>
    private static string FirstCommandToken(string body)
    {
        var trimmed = body.AsSpan().Trim();
        if (trimmed.IsEmpty)
            return "";
        var end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
            end++;
        return trimmed[..end].ToString();
    }

    /// <summary>
    /// Per-kind body rule (CARD-0137 / CARD-0057). Exposed so a schedule can refuse a Grok-forbidden
    /// body at create rather than at 09:00.
    /// </summary>
    public static bool TryGetForbiddenReason(AgentKind kind, string body, out string reason)
    {
        reason = "";
        var token = FirstCommandToken(body);
        if (token.Length == 0)
            return false;
        return ProviderContractCatalog.For(kind).LocalCommands.Forbidden.TryGetValue(token, out reason!);
    }

    /// <summary>
    /// CARD-0137: send the kind's measured dismiss key at most once, only when a fresh transcript
    /// pull says the session is idle. Returns false (and types nothing) when the contract is not
    /// Supported, recovery is disabled, or the session is working — the permission-dialog guard.
    /// </summary>
    private async Task<bool> TryDismissOverlayAsync(Guid sessionId, AgentKind kind, CancellationToken ct)
    {
        var overlay = ProviderContractCatalog.For(kind).TerminalOverlay;
        if (!_verification.OverlayRecoveryEnabled
            || overlay.State != AgentTuiCapabilityState.Supported
            || string.IsNullOrEmpty(overlay.DismissKey))
        {
            return false;
        }

        await _runtime.CatchUpTranscriptAsync(sessionId, ct);
        if (_runtime.TryGetLiveSnapshot(sessionId, out var popupSnap)
            && GrokQuestionPopup.IsPresent(popupSnap.RenderedScreen))
        {
            _logger.LogInformation(
                "Overlay dismiss withheld for session {SessionId}: Grok question popup is present (answer, do not Esc)",
                sessionId);
            return false;
        }

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await IsWorkingAsync(db, sessionId, ct))
            {
                _logger.LogInformation(
                    "Overlay dismiss withheld for session {SessionId}: session is working after transcript pull",
                    sessionId);
                return false;
            }
        }

        await _runtime.SendInputAsync(sessionId, overlay.DismissKey, ct, trackManualTurn: false);
        var settle = TimeSpan.FromMilliseconds(Math.Max(0, _verification.OverlaySettleMs));
        if (settle > TimeSpan.Zero)
            await Task.Delay(settle, _timeProvider, ct);
        return true;
    }

    private static bool TryGetLocalCommandFact(AgentKind kind, string body, out LocalCommandFact fact)
    {
        fact = null!;
        var token = FirstCommandToken(body);
        if (token.Length == 0)
            return false;
        return ProviderContractCatalog.For(kind).LocalCommands.Commands.TryGetValue(token, out fact!);
    }

    private async Task PublishQueueChangedAsync(SessionQueueDto dto, CancellationToken ct)
    {
        try
        {
            await _eventBus.PublishToGroupAsync(
                AgentSessionGroups.Session(dto.SessionId), "SessionQueueChanged", dto, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish queue change for session {SessionId}", dto.SessionId);
        }
    }

    private async Task PublishFinishedAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.AgentSessions
                .AsNoTracking()
                .Include(s => s.Card)
                .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

            Guid? cardId = session?.CardId;
            Guid? boardId = session?.Card?.BoardId;
            var label = session?.Card?.Identifier;
            Guid? agentId = null;
            if (label is null)
            {
                var agent = await db.Agents
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.PersistentSessionId == sessionId.ToString("D"), ct);
                label = agent?.Name ?? "Agent";
                agentId = agent?.Id;
            }

            var payload = new { sessionId, cardId, boardId, agentId, label };
            _logger.LogInformation(
                "Broadcasting SessionFinished for session {SessionId} ({Label})", sessionId, label);
            // Broadcast to all clients only — connections joined to the session group are part of
            // Clients.All, so an additional group-scoped publish would deliver the event twice to
            // anyone with that session's terminal open (duplicate toasts). Handlers that care about
            // a specific session filter by payload.sessionId.
            await _eventBus.PublishToAllAsync("SessionFinished", payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish finished signal for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// CARD-0143: type a local TUI command (e.g. Codex <c>/status</c>) into a live idle session
    /// without going through prompt delivery. Holds the per-session lock throughout so a poll
    /// cannot race a real message. Deliberately does NOT transcript-confirm, re-press Enter,
    /// call <c>HandleDeliveryFailureAsync</c>, or create a <c>SessionQueuedMessage</c> row —
    /// a local command writes no <c>UserPrompt</c>, so the normal path would time out and kill
    /// every always-on agent twice an hour.
    /// </summary>
    public async Task<LocalCommandPollResult> TryPollLocalCommandAsync(
        Guid sessionId, LocalCommandPoll poll, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(poll);
        if (string.IsNullOrWhiteSpace(poll.Command))
            throw new ArgumentException("Local-command poll requires a command body.", nameof(poll));

        var sem = GetLock(sessionId);
        await sem.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!_runtime.ListLiveSessions().Contains(sessionId))
                return new LocalCommandPollResult.Skipped("not live");
            if (!await IsAcceptingInputAsync(sessionId, ct))
                return new LocalCommandPollResult.Skipped("not Running");
            if (await IsWorkingAsync(db, sessionId, ct))
                return new LocalCommandPollResult.Skipped("working");
            if (await db.SessionQueuedMessages.AsNoTracking()
                .AnyAsync(m => m.AgentSessionId == sessionId
                    && m.Status == QueuedMessageStatus.Pending, ct))
            {
                return new LocalCommandPollResult.Skipped("pending messages");
            }
            // CARD-0650 D-7: an unconfirmed watchdog body may stand in the composer; no Esc, command or Enter on top.
            if (await ExpectationBodyBlocksCurrentGenerationAsync(db, sessionId, ct))
                return new LocalCommandPollResult.Skipped("unconfirmed expectation prompt");

            var forbidden = ProviderContractCatalog.For(poll.Kind).LocalCommands.Forbidden;
            foreach (var (body, reason) in forbidden)
            {
                if (string.Equals(body, poll.Command, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Forbidden local-command poll body '{poll.Command}' for {poll.Kind}: {reason}");
                }
            }

            var settle = TimeSpan.FromMilliseconds(Math.Max(0, poll.OverlaySettleMs));
            if (poll.OpensOverlay)
            {
                await _runtime.SendInputAsync(sessionId, "\u001b", ct, trackManualTurn: false);
                if (settle > TimeSpan.Zero)
                    await Task.Delay(settle, _timeProvider, ct);
            }

            var typed = await TypeLocalCommandAsync(sessionId, poll.Command, ct, poll.PanelTimeoutSeconds);
            if (typed == LocalCommandTypeResult.NotAccepted)
                return new LocalCommandPollResult.NotAccepted();
            if (typed != LocalCommandTypeResult.Sent)
            {
                if (poll.OpensOverlay)
                    await _runtime.SendInputAsync(sessionId, "\u001b", ct, trackManualTurn: false);
                return new LocalCommandPollResult.PanelNotRendered();
            }

            foreach (var key in poll.Navigation)
            {
                if (string.IsNullOrEmpty(key))
                    continue;
                await _runtime.SendInputAsync(sessionId, key, ct, trackManualTurn: false);
                if (settle > TimeSpan.Zero)
                    await Task.Delay(settle, _timeProvider, ct);
            }

            var snapshot = _runtime.GetBufferSnapshot(sessionId);

            if (poll.OpensOverlay)
                await _runtime.SendInputAsync(sessionId, "\u001b", ct, trackManualTurn: false);

            return new LocalCommandPollResult.Sent(snapshot.Buffer);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Type a local TUI command and prove the composer took it. No transcript confirm (a local
    /// command may write no UserPrompt row), ONE Enter (CARD-0141: a re-press lands on a picker's
    /// highlighted option), no incidents, no kill. Callers own Esc-before/after, Navigation,
    /// buffer capture, and everything else.
    /// </summary>
    private async Task<LocalCommandTypeResult> TypeLocalCommandAsync(
        Guid sessionId, string command, CancellationToken ct, int? sequenceTimeoutSeconds = null)
    {
        if (await IsModalBlockedAsync(sessionId, ct))
            return LocalCommandTypeResult.NotAccepted;

        if (!_runtime.TryGetLiveSnapshot(sessionId, out var before))
            return LocalCommandTypeResult.NotAccepted;

        await _runtime.SendInputAsync(sessionId, command, ct, trackManualTurn: false);

        if (!await WaitForComposerEvidenceAsync(sessionId, before.RenderedScreen, command, ct))
            return LocalCommandTypeResult.NotAccepted;

        long sequenceBefore = 0;
        if (_runtime.TryGetLiveMetadata(sessionId, out var metaBefore))
            sequenceBefore = metaBefore.LastSequence;

        await Task.Delay(TimeSpan.FromMilliseconds(20), _timeProvider, ct);
        await _runtime.SendInputAsync(sessionId, "\r", ct, trackManualTurn: false);

        if (!await WaitForSequenceAdvanceAsync(sessionId, sequenceBefore, ct, sequenceTimeoutSeconds))
            return LocalCommandTypeResult.NotAdvanced;

        return LocalCommandTypeResult.Sent;
    }

    internal SemaphoreSlim GetLock(Guid sessionId) =>
        _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
