using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
    private static bool IsTaskBriefPointer(string body) =>
        TypedBodySpill.TryReadOpeningTaskMarker(body) is { } marker
        && DispatchBriefEvidence.TryReadPointerLocation(body, marker, out _);

    private static bool IsCodexPointer(AgentKind? kind, SessionQueuedMessage row) =>
        kind == AgentKind.Codex && row.Origin == QueuedMessageOrigin.Delegation && IsTaskBriefPointer(row.Body);

    private sealed record PointerAttempt(Guid QueueId, DateTime? Generation, DateTime Started, TranscriptBaseline Baseline);

    private static PointerAttempt? PointerFor(AgentKind? kind, SessionQueuedMessage row, DateTime? generation = null) =>
        IsCodexPointer(kind, row)
            ? new(row.Id, row.LastDeliveryGeneration ?? generation, row.LastDeliveryStartedAt ?? row.CreatedAt,
                new(row.LastDeliveryBaselineSequence is not null, row.LastDeliveryBaselineSequence ?? 0))
            : null;

    private async Task<TranscriptConfirm> ReadPointerReceiptAsync(Guid sessionId, string body, PointerAttempt attempt, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var from = attempt.Started - TimeSpan.FromSeconds(Math.Max(0, _verification.UnobservableBaselineConfirmClockToleranceSeconds));
        if (attempt.Generation is { } generation && generation > from) from = generation;
        var prompts = db.TranscriptEntries.AsNoTracking().Where(t => t.AgentSessionId == sessionId
            && t.Kind == TranscriptKinds.UserPrompt);
        prompts = attempt.Baseline.Observable
            ? prompts.Where(t => t.Sequence > attempt.Baseline.MaxSequence)
            : prompts.Where(t => t.Timestamp != null && t.Timestamp >= from);
        if (attempt.Generation is { } accepted)
            prompts = prompts.Where(t => t.Timestamp == null || t.Timestamp >= accepted);
        var incomplete = TranscriptConfirm.None;
        foreach (var text in await prompts.OrderBy(t => t.Sequence).Select(t => t.Text).ToListAsync(ct))
        {
            var match = TranscriptConfirm.Classify(body, text);
            if (match.Complete) return match;
            if (match.Identity && !incomplete.Identity) incomplete = match;
        }
        return incomplete;
    }

    private readonly record struct PointerObservation(bool CompleteComposer, int Prefix, TranscriptConfirm Receipt);

    private async Task<PointerObservation> ObservePointerAsync(Guid sessionId, string body, PointerAttempt attempt, CancellationToken ct)
    {
        if (attempt.Generation is not { } generation
            || await CaptureSessionGenerationAsync(sessionId, ct) is not { } current
            || !SessionGeneration.Equal(generation, current)) return default;
        var receipt = await ReadPointerReceiptAsync(sessionId, body, attempt, ct);
        if (receipt.Identity) return new(false, 0, receipt);
        await using var scope = _scopeFactory.CreateAsyncScope();
        if (await ReadWorkingAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), sessionId, ct)) return default;
        if (await _runtime.TryGetLiveSnapshotAsync(sessionId, ct) is not { } snapshot) return default;
        return new(CodexPointerComposerEvidence.IsComplete(snapshot.RenderedScreen, body),
            CodexPointerComposerEvidence.MatchingPrefixLength(snapshot.RenderedScreen, body), receipt);
    }

    private void PointerMilestone(Guid sessionId, PointerAttempt attempt, string milestone, int length = 0) =>
        _logger.LogDebug("Codex pointer session={SessionId} queue={QueueId} generation={Generation:o} milestone={Milestone} length={Length}",
            sessionId, attempt.QueueId, attempt.Generation, milestone, length);

    private async Task<DeliveryOutcome> SubmitConsumedPointerAsync(Guid sessionId, string body, PointerAttempt attempt,
        PtyDeliveryCeilings? ceilings, DateTime? firstInputDeadlineAt, CancellationToken ct)
    {
        var deadline = UtcNow().AddSeconds(_verification.EvidenceTimeoutSeconds);
        var lastPull = DateTime.MinValue;
        var prefix = 0;
        PointerObservation observation;
        while (true)
        {
            if (UtcNow() - lastPull >= TimeSpan.FromMilliseconds(Math.Max(1000, _verification.PollIntervalMs)))
            {
                await _runtime.CatchUpTranscriptAsync(sessionId, ct);
                lastPull = UtcNow();
            }
            observation = await ObservePointerAsync(sessionId, body, attempt, ct);
            if (observation.Receipt.Identity) return ReceiptOutcome(observation.Receipt);
            if (observation.Prefix > prefix)
            {
                prefix = observation.Prefix;
                PointerMilestone(sessionId, attempt, "prefix-advanced", prefix);
            }
            if (observation.CompleteComposer) break;
            if (UtcNow() >= deadline) return DeliveryOutcome.Of(DeliveryVerdict.NoComposerEvidence);
            await Task.Delay(TimeSpan.FromMilliseconds(_verification.PollIntervalMs), _timeProvider, ct);
        }
        PointerMilestone(sessionId, attempt, "whole-composer", body.Length);
        var submit = await SettlePostEvidenceAsync(sessionId, ct);
        await Task.Delay(TimeSpan.FromMilliseconds(20), _timeProvider, ct);
        await _runtime.CatchUpTranscriptAsync(sessionId, ct);
        observation = await ObservePointerAsync(sessionId, body, attempt, ct);
        if (observation.Receipt.Identity) return ReceiptOutcome(observation.Receipt);
        if (!observation.CompleteComposer) return DeliveryOutcome.Of(DeliveryVerdict.NoComposerEvidence);
        PointerMilestone(sessionId, attempt, "enter");
        await _runtime.SendInputAsync(sessionId, "\r", ct);
        var outcome = await WaitForTranscriptConfirmAsync(sessionId, body, attempt.Baseline, submit.Sequence,
            submit.Screen, AgentKind.Codex, ct, ceilings, attempt.Started - TimeSpan.FromSeconds(
                Math.Max(0, _verification.UnobservableBaselineConfirmClockToleranceSeconds)), firstInputDeadlineAt, attempt);
        return outcome with { PointerConsumed = true };

        DeliveryOutcome ReceiptOutcome(TranscriptConfirm receipt)
        {
            if (!receipt.Complete) return DeliveryOutcome.Of(DeliveryVerdict.Truncated, receipt.Text);
            PointerMilestone(sessionId, attempt, "complete-receipt");
            return DeliveryOutcome.Confirmed(DeliveryConfirmedBy.Transcript);
        }
    }

    private async Task<FlushResult?> RecoverPointerAsync(AppDbContext db, Guid sessionId,
        IReadOnlyList<SessionQueuedMessage> run, PtyDeliveryCeilings ceilings, CancellationToken ct)
    {
        var kind = await TryGetSessionKindAsync(sessionId, ct);
        var pointers = run.Where(m => IsCodexPointer(kind, m)
            && (m.DeliveryAttempts > 0 || m.LastDeliveryStartedAt is not null)).ToList();
        if (pointers.Count == 0) return null;
        var current = await CaptureSessionGenerationAsync(sessionId, ct);
        if (current is { } generation && pointers.All(m => DeliveryGenerationChanged(m, generation))) return null;
        if (run.Count == 1 && PointerFor(kind, run[0], current) is { } attempt)
        {
            await _runtime.CatchUpTranscriptAsync(sessionId, ct);
            var late = await LateConfirmAttemptedMessagesAsync(db, sessionId, run, ct);
            if (late.Handled > 0) return late.Confirmed > 0 ? FlushResult.LateConfirmed : FlushResult.Failed;
            if ((await ObservePointerAsync(sessionId, run[0].Body, attempt, ct)).CompleteComposer)
                return await EnterOnlyConfirmLockedAsync(db, sessionId, run, run[0].Body, ct, ceilings);
        }
        await DeferPointersAsync(db, sessionId, pointers, ct);
        return FlushResult.Nothing;
    }

    private async Task DeferPointersAsync(AppDbContext db, Guid sessionId, IReadOnlyList<SessionQueuedMessage> rows, CancellationToken ct)
    {
        foreach (var row in rows)
        {
            if (row.Status == QueuedMessageStatus.Sent) row.Status = QueuedMessageStatus.Pending;
            row.SentAt = null;
            row.DeliveryVerdict = DeliveryVerdict.NoComposerEvidence;
            row.DeliveryVerdictAt = UtcNow();
        }
        await db.SaveChangesAsync(ct);
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.PersistentSessionId == sessionId.ToString("D"), ct);
        if (agent is not null)
        {
            foreach (var row in rows)
            {
                var key = $"pointer-consumption-unconfirmed queue={row.Id:D} generation={row.LastDeliveryGeneration:o}";
                if (await db.AgentIncidents.AnyAsync(i => i.SessionId == sessionId
                    && i.Kind == AgentIncidentKind.DeliveryVerificationFailed && i.Message.Contains(key), ct)) continue;
                await using var scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AgentSupervisorService>().RecordIncidentAsync(
                    agent.Id, sessionId, AgentIncidentKind.DeliveryVerificationFailed, AlertSeverity.Warning,
                    key + ": Complete pointer consumption is unconfirmed; Enter withheld. Retaining the typed attempt. "
                    + "Release requires a complete UserPrompt, a complete idle composer for Enter-only recovery, or a new generation.", ct: ct);
            }
        }
        await PublishQueueChangedAsync(await GetQueueAsync(sessionId, ct), ct);
    }
}
