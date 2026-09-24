using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
    private static string UnconfirmedExpectationBodyReason(Guid sessionId) =>
        "An unconfirmed expectation-watchdog prompt may still stand in this session's composer; "
        + "nothing is typed on top of it until it is confirmed, the composer is clear, or the session restarts. "
        + ExpectationHoldAudit.ReleaseHint(sessionId);

    /// <summary>
    /// CARD-0650 D-7. The watchdog's direct Now send. Under the per-session lock it rechecks the
    /// owner binding and generation, rules, herdr, modal and composer safety (held-back Pending
    /// rows, unreconciled Sent rows and earlier unconfirmed watchdog bodies), commits the attempt
    /// through <paramref name="commitAttempt"/>, then reuses <see cref="DeliverAsync"/> with its
    /// overlay arms off. It never tests IsWorking, never creates a queue row, and never calls
    /// delivery-failure, truncation or forbidden-body recovery: every outcome is returned typed.
    /// A committed attempt's outcome goes to <paramref name="recordOutcome"/> before the lock is let
    /// go (review 8adb4cd6: a release waiting on the lock must not find a finished send Attempting).
    /// </summary>
    internal async Task<ExpectationSendResult> SendExpectationNowAsync(
        Guid sessionId,
        DateTime expectedGeneration,
        Guid ownerAgentId,
        string body,
        Func<ExpectationSendAttempt, CancellationToken, Task<bool>> commitAttempt,
        CancellationToken ct,
        Func<ExpectationSendResult, CancellationToken, Task>? recordOutcome = null)
    {
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return ExpectationSendResult.Refuse("empty_body");
        if (!_runtime.ListLiveSessions().Contains(sessionId))
            return ExpectationSendResult.Refuse("destination_not_live");

        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync(ct);
        try
        {
            TranscriptBaseline baseline;
            PtyDeliveryCeilings ceilings;
            DateTime generation;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
                if (session is null || session.Status != SessionStatus.Running)
                    return ExpectationSendResult.Refuse("destination_not_running");
                if (session.StandingAgentId != ownerAgentId)
                    return ExpectationSendResult.Refuse("destination_not_owned");
                var pointer = await db.Agents.AsNoTracking()
                    .Where(a => a.Id == ownerAgentId)
                    .Select(a => a.PersistentSessionId)
                    .FirstOrDefaultAsync(ct);
                if (!Guid.TryParse(pointer, out var currentSession) || currentSession != sessionId)
                    return ExpectationSendResult.Refuse("destination_changed");
                generation = SessionGeneration.Normalize(session.StartedAt);
                if (!SessionGeneration.Equal(generation, expectedGeneration))
                    return ExpectationSendResult.Refuse("destination_changed");
                if (GrokRulesRefreshService.IsClosed(session))
                    return ExpectationSendResult.Refuse("rules_closed");
                if (TryGetForbiddenReason(session.AgentKind, trimmed, out _))
                    return ExpectationSendResult.Refuse("forbidden_body");

                ceilings = await CeilingsForSessionAsync(db, sessionId, ct);
                if (_runtime.TryGetLiveMetadata(sessionId, out var meta))
                {
                    if (ceilings.Backend == DeliveryBackend.HerdrPane
                        && string.Equals(meta.AgentStatus, "blocked", StringComparison.OrdinalIgnoreCase))
                        return ExpectationSendResult.Refuse("herdr_blocked");
                    if (string.Equals(meta.Pending, HerdrPendingReasons.Unreachable, StringComparison.Ordinal))
                        return ExpectationSendResult.Refuse("backend_unreachable");
                }
                if (await IsModalBlockedLockedAsync(db, sessionId, ct))
                    return ExpectationSendResult.Refuse("modal_blocked");

                // A Sent row still awaiting reconciliation may own the composer, whatever its age.
                var windowFloor = UtcNow() - InterruptedAttemptWindow;
                if (await db.SessionQueuedMessages.AsNoTracking().AnyAsync(m => m.AgentSessionId == sessionId
                        && m.Status == QueuedMessageStatus.Sent
                        && m.DeliveryVerdict == null
                        && m.LastDeliveryStartedAt != null
                        && m.LastDeliveryStartedAt >= windowFloor, ct))
                    return ExpectationSendResult.Refuse("unreconciled_sent_row");

                // Every Pending row counts as held back: the watchdog delivers none of them.
                var pending = await db.SessionQueuedMessages.AsNoTracking()
                    .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
                    .ToListAsync(ct);
                if (HeldBackTypingBlocksTheComposer(sessionId, pending, [], generation))
                    return ExpectationSendResult.Refuse("held_back_body_in_composer");
                if (await ExpectationBodyBlocksComposerAsync(db, sessionId, generation, ct))
                    return ExpectationSendResult.Refuse("unconfirmed_expectation_body");

                if (System.Text.Encoding.UTF8.GetByteCount(PtyInputEncoding.NormalizeBody(trimmed)) > ceilings.SingleWriteMaxBytes)
                    return ExpectationSendResult.Refuse("body_over_ceiling");
                baseline = await CaptureTranscriptBaselineAsync(db, sessionId, ct);
            }

            var attempt = new ExpectationSendAttempt(
                sessionId, generation, baseline.Observable ? baseline.MaxSequence : null);
            if (!await commitAttempt(attempt, ct))
                return ExpectationSendResult.Refuse("attempt_claim_lost");

            ExpectationSendResult result;
            try
            {
                var outcome = await DeliverAsync(sessionId, trimmed, ct, baseline, ceilings, overlayRecovery: false);
                result = ResultOf(outcome, baseline);
            }
            catch (Exception ex)
            {
                // Bytes may or may not have reached the terminal. Uncertain, never retried here.
                _logger.LogWarning(ex,
                    "Expectation prompt to session {SessionId} ended with {Error}; recorded Uncertain with no recovery",
                    sessionId, ex.GetType().Name);
                result = new ExpectationSendResult(
                    ExpectationSendOutcome.Uncertain,
                    ex is OperationCanceledException ? "cancelled" : "transport_failure:" + ex.GetType().Name,
                    AttemptCommitted: true);
            }

            // The outcome write must not be lost to the send's own cancellation.
            if (recordOutcome is not null)
                await recordOutcome(result, CancellationToken.None);
            return result;
        }
        finally
        {
            sessionLock.Release();
        }
    }

    /// <summary>The typed result of a delivery that ran after the attempt was committed.</summary>
    private ExpectationSendResult ResultOf(DeliveryOutcome outcome, TranscriptBaseline baseline)
    {
        // The hold follows the evidence each verdict carries. Submitted (no hold) needs a
        // submitted-prompt record carrying the body: whole past no floor, or Truncated. Its echo
        // stays in the conversation, where the screen hold cannot tell it from a standing body
        // (review D1). NoSubmitOutput, NoTranscriptRecord and a screen-only verdict are not
        // evidence the body left the composer: a swallowed Enter redraws too, and a working
        // caller already shows Working (review R1). Those hold until a record shows the prompt,
        // the body is gone from the screen, or the generation changes, and they page the operator.
        return outcome.Verdict switch
        {
            DeliveryVerdict.Delivered when outcome.ConfirmedBy == DeliveryConfirmedBy.Transcript && baseline.Observable =>
                new ExpectationSendResult(ExpectationSendOutcome.Confirmed, "transcript", true, UtcNow()),
            DeliveryVerdict.Delivered when outcome.ConfirmedBy == DeliveryConfirmedBy.Transcript =>
                new ExpectationSendResult(ExpectationSendOutcome.Unconfirmed, "no_observable_baseline", true, Submitted: true),
            DeliveryVerdict.Delivered =>
                new ExpectationSendResult(ExpectationSendOutcome.Unconfirmed, "screen_only_submit", true),
            DeliveryVerdict.Truncated =>
                new ExpectationSendResult(ExpectationSendOutcome.Unconfirmed, Describe(outcome.Verdict), true, Submitted: true),
            DeliveryVerdict.NoTranscriptRecord or DeliveryVerdict.NoSubmitOutput =>
                new ExpectationSendResult(ExpectationSendOutcome.Unconfirmed, Describe(outcome.Verdict), true),
            DeliveryVerdict.ModalBlocked or DeliveryVerdict.ForbiddenBody or DeliveryVerdict.SpillBodyMissing =>
                new ExpectationSendResult(ExpectationSendOutcome.Refused, Describe(outcome.Verdict), true),
            DeliveryVerdict.BackendUnreachable =>
                new ExpectationSendResult(ExpectationSendOutcome.Uncertain, Describe(outcome.Verdict), true),
            _ => new ExpectationSendResult(ExpectationSendOutcome.Unconfirmed, Describe(outcome.Verdict), true),
        };
    }

    /// <summary>
    /// True when an unconfirmed watchdog attempt of this generation may still stand in the
    /// composer: Attempting, Uncertain, or Unconfirmed (Enter withheld, NoSubmitOutput,
    /// NoTranscriptRecord or a screen-only verdict). Submitted and Released attempts never hold.
    /// Release is positive evidence only, never time: a submitted-prompt record carrying the body
    /// past the attempt's floor (a full late receipt, or a partial one, since either means the
    /// composer was submitted), an empty composer, a new generation, or an operator's audited
    /// release. With no floor the transcript was empty when the attempt was committed, so every
    /// record is later than it. An unreadable snapshot holds.
    /// <para>Review 02e08ff1: where the kind's composer can be read (Claude's bottom box) only the
    /// composer counts: empty releases, anything in it holds, and an echo in the conversation above
    /// it is not evidence either way. Where it cannot be read, the body visible whole anywhere on
    /// screen holds, so a submitted echo with no record holds until the operator releases it.</para>
    /// <para>Review 8adb4cd6: for Claude an unreadable composer holds whatever else the screen shows.
    /// A long composer renders only its tail, and a dialog or a scrolled view hides the box, so the
    /// body's head being off screen is not evidence that it left. The whole-screen rule is only for
    /// kinds with no readable composer at all.</para>
    /// <para>Review 97ea55ef: an empty reading releases only when a second snapshot
    /// PostEvidenceSettleMs later reads empty too (<see cref="ComposerStaysEmptyAsync"/>).</para>
    /// </summary>
    private async Task<bool> ExpectationBodyBlocksComposerAsync(
        AppDbContext db, Guid sessionId, DateTime generation, CancellationToken ct)
    {
        var attempts = await db.ExpectationNudges.AsNoTracking()
            .Where(n => n.DestinationSessionId == sessionId
                && (n.AttemptState == ExpectationAttemptState.Attempting
                    || n.AttemptState == ExpectationAttemptState.Uncertain
                    || n.AttemptState == ExpectationAttemptState.Unconfirmed))
            .Select(n => new { n.Id, n.Body, n.DestinationGeneration, n.BaselineSequence })
            .ToListAsync(ct);
        var current = attempts
            .Where(n => n.DestinationGeneration is { } typed && SessionGeneration.Equal(typed, generation))
            .ToList();
        if (current.Count == 0)
            return false;

        var unrecorded = new List<(Guid Id, string Body)>();
        foreach (var attempt in current)
        {
            var record = await TryFindConfirmingRecordAsync(
                db, sessionId, attempt.Body.Trim(), attempt.BaselineSequence ?? long.MinValue, ct);
            if (!record.Identity)
                unrecorded.Add((attempt.Id, attempt.Body));
        }
        if (unrecorded.Count == 0)
            return false;

        if (!_runtime.TryGetLiveSnapshot(sessionId, out var snapshot))
        {
            _logger.LogInformation(
                "Holding input to session {SessionId}: {Count} unconfirmed expectation prompt(s) of this "
                + "generation, no transcript record of them, and no rendered snapshot to show the composer empty",
                sessionId, unrecorded.Count);
            return true;
        }

        var kind = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => s.AgentKind)
            .FirstOrDefaultAsync(ct);
        if (TryReadComposerRegion(kind, snapshot.RenderedScreen, out var composer))
        {
            if (ClaudeScreen.ComposerContentIsEmpty(composer))
            {
                if (await ComposerStaysEmptyAsync(kind, sessionId, ct))
                    return false;
                _logger.LogWarning(
                    "Holding input to session {SessionId}: expectation nudge {NudgeId} is unconfirmed, has no "
                    + "transcript record, and the composer read empty on one snapshot but not on the next. Release: {Route}",
                    sessionId, unrecorded[0].Id, ExpectationHoldAudit.ReleaseRoute(sessionId));
                return true;
            }
            _logger.LogWarning(
                "Holding input to session {SessionId}: expectation nudge {NudgeId} is unconfirmed, has no "
                + "transcript record, and the composer is not empty. Release: {Route}",
                sessionId, unrecorded[0].Id, ExpectationHoldAudit.ReleaseRoute(sessionId));
            return true;
        }
        if (kind == AgentKind.ClaudeCode)
        {
            _logger.LogWarning(
                "Holding input to session {SessionId}: expectation nudge {NudgeId} is unconfirmed, has no "
                + "transcript record, and Claude's composer cannot be read to show it empty. Release: {Route}",
                sessionId, unrecorded[0].Id, ExpectationHoldAudit.ReleaseRoute(sessionId));
            return true;
        }

        var standing = unrecorded
            .Where(n => ComposerDeliveryEvidence.HeadFragmentIsVisibleWhole(
                snapshot.RenderedScreen, PtyInputEncoding.NormalizeBody(n.Body.Trim())))
            .Select(n => (Guid?)n.Id)
            .FirstOrDefault();
        if (standing is not { } standingId)
            return false;
        _logger.LogWarning(
            "Holding input to session {SessionId}: expectation nudge {NudgeId} is unconfirmed, has no "
            + "transcript record, and is still visible whole on screen. Release: {Route}",
            sessionId, standingId, ExpectationHoldAudit.ReleaseRoute(sessionId));
        return true;
    }

    /// <summary>
    /// Repair 5 (review 97ea55ef): the second look behind an empty composer reading. A stale ghost
    /// frame (the body's tail, then an empty box above the hint bar) reads as a live, empty composer
    /// on its own while the body still stands in the real one. One empty snapshot is not evidence
    /// (CARD-0299), so the composer counts as empty only when a snapshot PostEvidenceSettleMs later
    /// is readable and empty too.
    /// </summary>
    private async Task<bool> ComposerStaysEmptyAsync(AgentKind kind, Guid sessionId, CancellationToken ct)
    {
        var settle = TimeSpan.FromMilliseconds(Math.Clamp(_verification.PostEvidenceSettleMs, 0, 3_000));
        if (settle > TimeSpan.Zero)
            await Task.Delay(settle, _timeProvider, ct);
        return _runtime.TryGetLiveSnapshot(sessionId, out var later)
            && TryReadComposerRegion(kind, later.RenderedScreen, out var composer)
            && ClaudeScreen.ComposerContentIsEmpty(composer);
    }

    /// <summary>
    /// The composer's content for a kind whose rendered composer can be told apart from the
    /// conversation: Claude's bottom box. Codex, Grok and the rest have no reliable region here;
    /// false keeps the whole-screen rule for them. A Claude frame without a readable box holds.
    /// </summary>
    private static bool TryReadComposerRegion(AgentKind kind, string renderedScreen, out string content)
    {
        content = string.Empty;
        return kind == AgentKind.ClaudeCode && ClaudeScreen.TryReadComposer(renderedScreen, out content);
    }

    /// <summary>
    /// CARD-0650 S4 repair 3. The operator's audited release of an expectation-watchdog composer
    /// hold on <paramref name="sessionId"/>: every Attempting, Uncertain or Unconfirmed attempt of
    /// the current generation becomes Released, with a comment on its audit card and a Check note on
    /// its subject tasks. Under the session lock, so it cannot interleave with a send. It never types,
    /// never touches the queue, and never clears operator debt. A session with no hold releases nothing.
    /// <para>Repair 4 (review 8adb4cd6): <paramref name="releasedBy"/> is the operator the route
    /// authenticated (the CARD-0658 operator token); it is written into every audit entry. A watchdog
    /// send records its outcome before it lets go of the same lock, so an Attempting attempt seen
    /// here is an interrupted one, never a send in flight.</para>
    /// </summary>
    public async Task<ExpectationHoldReleaseResult> ReleaseExpectationHoldAsync(
        Guid sessionId, string? reason, string releasedBy, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releasedBy);
        var why = (reason ?? string.Empty).Trim();
        if (why.Length == 0)
            throw new ValidationException("reason", "A reason is required to release an expectation-watchdog hold.");
        if (why.Length > MaxExpectationReleaseReasonChars)
            throw new ValidationException("reason", $"The reason must be at most {MaxExpectationReleaseReasonChars} characters.");

        var sessionLock = GetLock(sessionId);
        await sessionLock.WaitAsync(ct);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var startedAt = await db.AgentSessions.AsNoTracking()
                .Where(s => s.Id == sessionId)
                .Select(s => (DateTime?)s.StartedAt)
                .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Session", sessionId);
            var generation = SessionGeneration.Normalize(startedAt);

            var holding = (await db.ExpectationNudges.AsNoTracking()
                    .Where(n => n.DestinationSessionId == sessionId
                        && (n.AttemptState == ExpectationAttemptState.Attempting
                            || n.AttemptState == ExpectationAttemptState.Uncertain
                            || n.AttemptState == ExpectationAttemptState.Unconfirmed))
                    .Select(n => new { n.Id, n.AttemptState, n.DestinationGeneration, n.AuditCommentId, n.CheckEventIdsJson })
                    .ToListAsync(ct))
                .Where(n => n.DestinationGeneration is { } typed && SessionGeneration.Equal(typed, generation))
                .ToList();

            var released = new List<Guid>();
            var now = UtcNow();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var nudge in holding)
            {
                var rows = await db.ExpectationNudges
                    .Where(n => n.Id == nudge.Id && n.AttemptState == nudge.AttemptState)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(n => n.AttemptState, ExpectationAttemptState.Released)
                        .SetProperty(n => n.ConcurrencyToken, Guid.NewGuid()), ct);
                if (rows != 1)
                    continue;
                await ExpectationHoldAudit.AddAsync(
                    db, nudge.AuditCommentId, nudge.CheckEventIdsJson,
                    ExpectationHoldAudit.ReleasedNote(nudge.Id, sessionId, generation, nudge.AttemptState, releasedBy, why),
                    ExpectationHoldAudit.ReleaseAuthor(releasedBy), now, ct);
                released.Add(nudge.Id);
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            if (released.Count > 0)
            {
                _logger.LogWarning(
                    "Operator {ReleasedBy} released the expectation composer hold on session {SessionId} for nudge(s) {NudgeIds}: {Reason}",
                    releasedBy, sessionId, string.Join(", ", released), why);
            }
            return new ExpectationHoldReleaseResult(sessionId, released);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    private const int MaxExpectationReleaseReasonChars = 1000;

    private async Task EnsureNoUnconfirmedExpectationBodyAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await ExpectationBodyBlocksCurrentGenerationAsync(db, sessionId, ct))
            throw new ConflictException(UnconfirmedExpectationBodyReason(sessionId), "expectation_prompt_unconfirmed");
    }

    /// <summary><see cref="ExpectationBodyBlocksComposerAsync"/> at the session's current generation.</summary>
    private async Task<bool> ExpectationBodyBlocksCurrentGenerationAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var startedAt = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (DateTime?)s.StartedAt)
            .FirstOrDefaultAsync(ct);
        return startedAt is { } started
            && await ExpectationBodyBlocksComposerAsync(db, sessionId, SessionGeneration.Normalize(started), ct);
    }

    /// <summary>
    /// CARD-0650 review D2. With both overlay arms off there is no Escape to clear a measured overlay
    /// or question popup, so the watchdog must not type into one: its body would be discarded, or
    /// become the answer to a live question. Detection only, never a key.
    /// </summary>
    private static bool ShowsTerminalOverlay(AgentKind kind, string renderedScreen) =>
        GrokQuestionPopup.IsPresent(renderedScreen)
        || ProviderContractCatalog.For(kind).TerminalOverlay.DetectFragments.Any(f =>
            ComposerDeliveryEvidence.FragmentIsVisible(renderedScreen, f));
}

/// <summary>Adapter from the watchdog's I/O seam to the real queue service.</summary>
public sealed class SessionQueueExpectationPromptSender(SessionMessageQueueService queue) : IExpectationPromptSender
{
    public Task<ExpectationSendResult> SendAsync(
        Guid sessionId,
        DateTime expectedGeneration,
        Guid ownerAgentId,
        string body,
        Func<ExpectationSendAttempt, CancellationToken, Task<bool>> commitAttempt,
        CancellationToken ct,
        Func<ExpectationSendResult, CancellationToken, Task>? recordOutcome = null) =>
        queue.SendExpectationNowAsync(sessionId, expectedGeneration, ownerAgentId, body, commitAttempt, ct, recordOutcome);
}
