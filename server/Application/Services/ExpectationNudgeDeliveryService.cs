using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>What one delivery pass did. A null outcome means another claimant owns the attempt.</summary>
public sealed record ExpectationDeliveryResult(Guid NudgeId, ExpectationSendOutcome? Outcome, string Reason);

/// <summary>
/// CARD-0650 S4 direct delivery of one committed nudge. A nudge is typed at most once: the
/// attempt (destination, generation, transcript floor) is committed as Attempting under the
/// session lock before any byte, by a conditional update that only one claimant can win. Every
/// later pass on an attempted nudge only looks for a late receipt; it never types again, even
/// after a crash. Anything but a receipt leaves durable operator debt (S5 publishes it).
/// This service never enqueues a WhenIdle row, waits for a repository lease or note
/// reconciliation, or calls delivery-failure recovery.
/// </summary>
public sealed class ExpectationNudgeDeliveryService
{
    private readonly AppDbContext _db;
    private readonly IExpectationPromptSender _sender;
    private readonly TimeProvider _time;
    private readonly IExpectationCatchUp _catchUp;

    public ExpectationNudgeDeliveryService(
        AppDbContext db,
        IExpectationPromptSender sender,
        TimeProvider time,
        IExpectationCatchUp? catchUp = null)
    {
        _db = db;
        _sender = sender;
        _time = time;
        _catchUp = catchUp ?? NoExpectationCatchUp.Instance;
    }

    public async Task<ExpectationDeliveryResult> DeliverAsync(
        ExpectationDirectiveSettings directive, Guid nudgeId, CancellationToken ct)
    {
        var nudge = await _db.ExpectationNudges.AsNoTracking().SingleOrDefaultAsync(n => n.Id == nudgeId, ct)
            ?? throw new InvalidOperationException($"Expectation nudge {nudgeId} does not exist.");
        if (!string.Equals(nudge.DirectiveId, directive.Id, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expectation nudge {nudgeId} belongs to directive '{nudge.DirectiveId}'.");

        if (nudge.AttemptState != ExpectationAttemptState.None)
            return await ReconcileAsync(nudge, ct);

        // Resolve the standing agent's current owned session for this nudge; never an old caller session.
        var agent = await _db.Agents.AsNoTracking()
            .Where(a => a.Id == directive.AgentId)
            .Select(a => new { a.Id, a.IsPoolDelegate, a.PersistentSessionId })
            .SingleOrDefaultAsync(ct);
        if (agent is null || agent.IsPoolDelegate)
            return await RefuseUnclaimedAsync(nudgeId, "no_standing_agent", ct);
        if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
            return await RefuseUnclaimedAsync(nudgeId, "no_current_session", ct);
        var session = await _db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.StandingAgentId, s.Status, s.StartedAt })
            .SingleOrDefaultAsync(ct);
        if (session is null || session.StandingAgentId != agent.Id || session.Status != SessionStatus.Running)
            return await RefuseUnclaimedAsync(nudgeId, "destination_unavailable", ct);

        var claimed = false;
        async Task<bool> CommitAttemptAsync(ExpectationSendAttempt attempt, CancellationToken token)
        {
            var rows = await _db.ExpectationNudges
                .Where(n => n.Id == nudgeId && n.AttemptState == ExpectationAttemptState.None)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(n => n.AttemptState, ExpectationAttemptState.Attempting)
                    .SetProperty(n => n.DestinationSessionId, attempt.SessionId)
                    .SetProperty(n => n.DestinationGeneration, attempt.Generation)
                    .SetProperty(n => n.BaselineSequence, attempt.BaselineSequence)
                    .SetProperty(n => n.ConcurrencyToken, Guid.NewGuid()), token);
            claimed = rows == 1;
            return claimed;
        }

        var result = await _sender.SendAsync(
            sessionId, SessionGeneration.Normalize(session.StartedAt), agent.Id, nudge.Body, CommitAttemptAsync, ct);

        if (!claimed)
        {
            // Refused before the claim, or another sweep won it. Only the unclaimed case is ours.
            return result.Reason == "attempt_claim_lost"
                ? new ExpectationDeliveryResult(nudgeId, null, result.Reason)
                : await RefuseUnclaimedAsync(nudgeId, result.Reason, ct);
        }

        // The outcome write must not be lost to the send's own cancellation.
        await RecordAsync(nudgeId, ExpectationAttemptState.Attempting, StateFor(result), result.ReceiptAt, CancellationToken.None);
        return new ExpectationDeliveryResult(nudgeId, result.Outcome, result.Reason);
    }

    /// <summary>
    /// Late receipt for an attempted nudge. Catch-up precedes the judgment; a pull failure keeps
    /// the state. An Attempting row found here was interrupted: it becomes Uncertain, never sendable.
    /// </summary>
    private async Task<ExpectationDeliveryResult> ReconcileAsync(ExpectationNudge nudge, CancellationToken ct)
    {
        var state = nudge.AttemptState;
        if (state is ExpectationAttemptState.Confirmed or ExpectationAttemptState.Refused)
            return new ExpectationDeliveryResult(nudge.Id, Outcome(state), "settled");

        if (state == ExpectationAttemptState.Attempting)
        {
            await RecordAsync(nudge.Id, ExpectationAttemptState.Attempting, ExpectationAttemptState.Uncertain, null, ct);
            state = ExpectationAttemptState.Uncertain;
        }

        if (nudge.DestinationSessionId is not { } destination)
            return new ExpectationDeliveryResult(nudge.Id, Outcome(state), "no_destination");
        try
        {
            await _catchUp.CatchUpAsync([destination], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ExpectationDeliveryResult(nudge.Id, Outcome(state), "catch_up_failed");
        }

        if (nudge.BaselineSequence is not { } floor)
            return new ExpectationDeliveryResult(nudge.Id, Outcome(state), "no_observable_baseline");

        var receipt = await FindReceiptAsync(destination, floor, nudge.Body, ct);
        if (receipt is null)
            return new ExpectationDeliveryResult(nudge.Id, Outcome(state), "no_receipt");

        await RecordAsync(nudge.Id, state, ExpectationAttemptState.Confirmed, receipt, ct);
        return new ExpectationDeliveryResult(nudge.Id, ExpectationSendOutcome.Confirmed, "late_receipt");
    }

    /// <summary>
    /// A complete submitted prompt (UserPrompt or QueuedUserPrompt, never queue housekeeping)
    /// in the frozen destination strictly past the committed floor.
    /// </summary>
    private async Task<DateTime?> FindReceiptAsync(Guid sessionId, long floor, string body, CancellationToken ct)
    {
        var typed = Antiphon.Agents.Pty.PtyInputEncoding.NormalizeBody(body.Trim());
        var candidates = await _db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Sequence > floor
                && (t.Kind == TranscriptKinds.UserPrompt || t.Kind == TranscriptKinds.QueuedUserPrompt))
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.Text, t.Timestamp, t.CreatedAt })
            .ToListAsync(ct);
        var match = candidates.FirstOrDefault(t => PromptSubmissionMatch.IsConfirmedBy(typed, t.Text)
            && PromptSubmissionMatch.IsCompleteIn(typed, t.Text));
        return match is null ? null : match.Timestamp ?? match.CreatedAt;
    }

    private async Task<ExpectationDeliveryResult> RefuseUnclaimedAsync(Guid nudgeId, string reason, CancellationToken ct)
    {
        await RecordAsync(nudgeId, ExpectationAttemptState.None, ExpectationAttemptState.Refused, null, ct);
        return new ExpectationDeliveryResult(nudgeId, ExpectationSendOutcome.Refused, reason);
    }

    /// <summary>
    /// Conditional on the state this pass observed, so a concurrent pass cannot be overwritten.
    /// Every non-receipt outcome is operator debt due now; a receipt never clears existing debt.
    /// </summary>
    private async Task RecordAsync(
        Guid nudgeId, ExpectationAttemptState from, ExpectationAttemptState to, DateTime? receiptAt, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var rows = _db.ExpectationNudges.Where(n => n.Id == nudgeId && n.AttemptState == from);
        if (to == ExpectationAttemptState.Confirmed)
        {
            await rows.ExecuteUpdateAsync(u => u
                .SetProperty(n => n.AttemptState, to)
                .SetProperty(n => n.ReceiptAt, receiptAt ?? now)
                .SetProperty(n => n.ConcurrencyToken, Guid.NewGuid()), ct);
            return;
        }

        await rows.ExecuteUpdateAsync(u => u
            .SetProperty(n => n.AttemptState, to)
            .SetProperty(n => n.OperatorOutboxState, n => n.OperatorOutboxState == ExpectationOperatorOutboxState.None
                ? ExpectationOperatorOutboxState.Due
                : n.OperatorOutboxState)
            .SetProperty(n => n.OperatorNextAttemptAt, n => n.OperatorOutboxState == ExpectationOperatorOutboxState.None
                ? now
                : n.OperatorNextAttemptAt)
            .SetProperty(n => n.ConcurrencyToken, Guid.NewGuid()), ct);
    }

    /// <summary>Submitted keeps the Unconfirmed receipt verdict; only the composer hold differs.</summary>
    private static ExpectationAttemptState StateFor(ExpectationSendResult result) => result.Outcome switch
    {
        ExpectationSendOutcome.Confirmed => ExpectationAttemptState.Confirmed,
        ExpectationSendOutcome.Unconfirmed => result.Submitted ? ExpectationAttemptState.Submitted : ExpectationAttemptState.Unconfirmed,
        ExpectationSendOutcome.Uncertain => ExpectationAttemptState.Uncertain,
        _ => ExpectationAttemptState.Refused,
    };

    private static ExpectationSendOutcome? Outcome(ExpectationAttemptState state) => state switch
    {
        ExpectationAttemptState.Confirmed => ExpectationSendOutcome.Confirmed,
        ExpectationAttemptState.Unconfirmed or ExpectationAttemptState.Submitted => ExpectationSendOutcome.Unconfirmed,
        ExpectationAttemptState.Uncertain => ExpectationSendOutcome.Uncertain,
        ExpectationAttemptState.Refused => ExpectationSendOutcome.Refused,
        _ => null,
    };
}
