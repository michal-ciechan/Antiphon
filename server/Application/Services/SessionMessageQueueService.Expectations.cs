using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
    private const string UnconfirmedExpectationBodyReason =
        "An unconfirmed expectation-watchdog prompt may still stand in this session's composer; "
        + "nothing is typed on top of it until it is confirmed, the composer is clear, or the session restarts.";

    /// <summary>
    /// CARD-0650 D-7. The watchdog's direct Now send. Under the per-session lock it rechecks the
    /// owner binding and generation, rules, herdr, modal and composer safety (held-back Pending
    /// rows, unreconciled Sent rows and earlier unconfirmed watchdog bodies), commits the attempt
    /// through <paramref name="commitAttempt"/>, then reuses <see cref="DeliverAsync"/> with its
    /// overlay arms off. It never tests IsWorking, never creates a queue row, and never calls
    /// delivery-failure, truncation or forbidden-body recovery: every outcome is returned typed.
    /// </summary>
    internal async Task<ExpectationSendResult> SendExpectationNowAsync(
        Guid sessionId,
        DateTime expectedGeneration,
        Guid ownerAgentId,
        string body,
        Func<ExpectationSendAttempt, CancellationToken, Task<bool>> commitAttempt,
        CancellationToken ct)
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

            DeliveryOutcome outcome;
            try
            {
                outcome = await DeliverAsync(sessionId, trimmed, ct, baseline, ceilings, overlayRecovery: false);
            }
            catch (Exception ex)
            {
                // Bytes may or may not have reached the terminal. Uncertain, never retried here.
                _logger.LogWarning(ex,
                    "Expectation prompt to session {SessionId} ended with {Error}; recorded Uncertain with no recovery",
                    sessionId, ex.GetType().Name);
                return new ExpectationSendResult(
                    ExpectationSendOutcome.Uncertain,
                    ex is OperationCanceledException ? "cancelled" : "transport_failure:" + ex.GetType().Name,
                    AttemptCommitted: true);
            }

            return outcome.Verdict switch
            {
                DeliveryVerdict.Delivered when outcome.ConfirmedBy == DeliveryConfirmedBy.Transcript && baseline.Observable =>
                    new ExpectationSendResult(ExpectationSendOutcome.Confirmed, "transcript", true, UtcNow()),
                DeliveryVerdict.Delivered =>
                    new ExpectationSendResult(ExpectationSendOutcome.Unconfirmed,
                        baseline.Observable ? "no_transcript_receipt" : "no_observable_baseline", true),
                DeliveryVerdict.ModalBlocked or DeliveryVerdict.ForbiddenBody or DeliveryVerdict.SpillBodyMissing =>
                    new ExpectationSendResult(ExpectationSendOutcome.Refused, Describe(outcome.Verdict), true),
                DeliveryVerdict.BackendUnreachable =>
                    new ExpectationSendResult(ExpectationSendOutcome.Uncertain, Describe(outcome.Verdict), true),
                _ => new ExpectationSendResult(ExpectationSendOutcome.Unconfirmed, Describe(outcome.Verdict), true),
            };
        }
        finally
        {
            sessionLock.Release();
        }
    }

    /// <summary>
    /// True when an unconfirmed watchdog attempt of this generation may still stand in the
    /// composer. Same release rules as <see cref="HeldBackTypingBlocksTheComposer"/>: a full late
    /// receipt (the row becomes Confirmed), the body no longer visible whole, or a new generation.
    /// An unreadable snapshot holds.
    /// </summary>
    private async Task<bool> ExpectationBodyBlocksComposerAsync(
        AppDbContext db, Guid sessionId, DateTime generation, CancellationToken ct)
    {
        var attempts = await db.ExpectationNudges.AsNoTracking()
            .Where(n => n.DestinationSessionId == sessionId
                && (n.AttemptState == ExpectationAttemptState.Attempting
                    || n.AttemptState == ExpectationAttemptState.Uncertain
                    || n.AttemptState == ExpectationAttemptState.Unconfirmed))
            .Select(n => new { n.Id, n.Body, n.DestinationGeneration })
            .ToListAsync(ct);
        var current = attempts
            .Where(n => n.DestinationGeneration is { } typed && SessionGeneration.Equal(typed, generation))
            .ToList();
        if (current.Count == 0)
            return false;

        if (!_runtime.TryGetLiveSnapshot(sessionId, out var snapshot))
        {
            _logger.LogInformation(
                "Holding input to session {SessionId}: {Count} unconfirmed expectation prompt(s) of this "
                + "generation and no rendered snapshot to show the composer empty",
                sessionId, current.Count);
            return true;
        }

        var standing = current.FirstOrDefault(n => ComposerDeliveryEvidence.HeadFragmentIsVisibleWhole(
            snapshot.RenderedScreen, PtyInputEncoding.NormalizeBody(n.Body.Trim())));
        if (standing is null)
            return false;
        _logger.LogWarning(
            "Holding input to session {SessionId}: expectation nudge {NudgeId} is unconfirmed and still "
            + "standing whole in the composer",
            sessionId, standing.Id);
        return true;
    }

    private async Task EnsureNoUnconfirmedExpectationBodyAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var startedAt = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => (DateTime?)s.StartedAt)
            .FirstOrDefaultAsync(ct);
        if (startedAt is { } started
            && await ExpectationBodyBlocksComposerAsync(db, sessionId, SessionGeneration.Normalize(started), ct))
            throw new ConflictException(UnconfirmedExpectationBodyReason, "expectation_prompt_unconfirmed");
    }
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
        CancellationToken ct) =>
        queue.SendExpectationNowAsync(sessionId, expectedGeneration, ownerAgentId, body, commitAttempt, ct);
}
