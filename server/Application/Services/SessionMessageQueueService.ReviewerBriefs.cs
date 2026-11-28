using Antiphon.Agents.Pty;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed partial class SessionMessageQueueService
{
    private sealed record QueuedSubmission(AppDbContext Db, IReadOnlyList<SessionQueuedMessage> Run,
        IReadOnlyDictionary<Guid, Guid>? ReviewerRuns);

    private static QueuedSubmission? SubmissionFor(AppDbContext db, IReadOnlyList<SessionQueuedMessage> run,
        IReadOnlyDictionary<Guid, Guid>? reviewerRuns = null) =>
        run.Any(m => m.ExecutionTaskId is { } id && (reviewerRuns is null || reviewerRuns.ContainsKey(id)))
            ? new QueuedSubmission(db, run, reviewerRuns) : null;

    private enum QueueSubmitResult { WroteEnter, Settled, StopRetrying }

    private async Task<QueueSubmitResult> SubmitDeliveryAsync(Guid sessionId, string body, AgentKind? kind,
        QueuedSubmission? submission, bool retry, CancellationToken ct)
    {
        if (submission is not null)
        {
            var (db, run, reviewerRuns) = submission;
            var tasks = run.Where(m => m.ExecutionTaskId != null).Select(m => m.ExecutionTaskId!.Value).Distinct().ToArray();
            // A pending flush already resolved the immutable task linkage. Interrupted Enter and
            // Send Now can arrive without that read. Each Enter still locks the run's live status.
            var links = reviewerRuns is null
                ? await db.AgentTasks.AsNoTracking().Where(t => tasks.Contains(t.Id) && t.ReviewerRunId != null)
                    .ToDictionaryAsync(t => t.Id, t => t.ReviewerRunId!.Value, ct)
                : reviewerRuns.Where(link => tasks.Contains(link.Key)).ToDictionary(link => link.Key, link => link.Value);
            if (links.Count != 0)
            {
                var runIds = links.Values.Distinct().ToArray();
                HashSet<Guid> refused;
                await using (var transaction = await db.Database.BeginTransactionAsync(ct))
                {
                    // Close changes the same row. Hold its shared lock only through this Enter,
                    // leaving the transcript confirmation loop outside the database transaction.
                    var open = await db.Database.SqlQuery<Guid>($"""
                        SELECT "Id" AS "Value" FROM "ReviewRuns"
                        WHERE "Id" = ANY({runIds}) AND "Status" <> 'Closed' ORDER BY "Id" FOR SHARE
                        """).ToListAsync(ct);
                    if (open.Count == runIds.Length)
                    {
                        await _runtime.SendInputAsync(sessionId, "\r", ct);
                        return QueueSubmitResult.WroteEnter;
                    }

                    refused = links.Where(link => !open.Contains(link.Value)).Select(link => link.Key).ToHashSet();
                }
                return await RefuseClosedReviewerBriefAsync(submission, refused, sessionId, body, kind, retry, ct);
            }
        }

        await _runtime.SendInputAsync(sessionId, "\r", ct);
        return QueueSubmitResult.WroteEnter;
    }

    private async Task<QueueSubmitResult> RefuseClosedReviewerBriefAsync(QueuedSubmission submission,
        IReadOnlySet<Guid> refused, Guid sessionId, string body, AgentKind? kind, bool retry, CancellationToken ct)
    {
        var (db, run, _) = submission;
        await _runtime.CatchUpTranscriptAsync(sessionId, ct);
        if ((await LateConfirmAttemptedMessagesAsync(db, sessionId, run, ct)).Handled > 0)
            return QueueSubmitResult.Settled;

        var clear = await ObserveReviewerDraftAsync(sessionId, body, kind, run, ct);
        if (retry && clear is null)
            // An earlier Enter was admitted. Leave time for its receipt to reach the transcript.
            return QueueSubmitResult.StopRetrying;

        foreach (var message in run)
        {
            message.SentAt = null;
            if (message.ExecutionTaskId is { } id && refused.Contains(id))
            {
                message.Status = QueuedMessageStatus.Canceled;
                message.CanceledAt = UtcNow();
            }
            else
                message.Status = QueuedMessageStatus.Pending;
        }
        await db.SaveChangesAsync(ct);

        if (clear is not null)
        {
            try
            {
                var result = await _runtime.SendConditionalInputAsync(sessionId, clear, ct);
                if (result.Outcome != ConditionalInputOutcomes.Written)
                    _logger.LogDebug("Closed reviewer draft on {SessionId} still needs composer clearance ({Outcome})", sessionId, result.Outcome);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The canceled attempt still holds its generation and composer evidence. The
                // ordinary canceled-typing hold and reviewer stop path own a refused clear.
                _logger.LogWarning(ex, "Could not clear the closed reviewer draft on {SessionId}", sessionId);
            }
        }
        return QueueSubmitResult.Settled;
    }

    private async Task<RunnerConditionalInputRequest?> ObserveReviewerDraftAsync(Guid sessionId, string body,
        AgentKind? kind, IReadOnlyList<SessionQueuedMessage> run, CancellationToken ct)
    {
        if (await CaptureSessionGenerationAsync(sessionId, ct) is not { } generation
            || run.Any(m => DeliveryGenerationChanged(m, generation)))
            return null;

        var dialog = kind == AgentKind.ClaudeCode ? await _runtime.ObserveDialogAsync(sessionId, ct) : null;
        if (await _runtime.TryGetLiveSnapshotAsync(sessionId, ct) is not { } snapshot)
            return null;

        var exactDraft = kind switch
        {
            AgentKind.ClaudeCode => dialog is not null && ClaudeScreen.TryReadComposer(snapshot.RenderedScreen, out var composer)
                && ComposerDeliveryEvidence.WholeBodyEquals(body, composer),
            AgentKind.Codex => CodexPointerComposerEvidence.IsComplete(snapshot.RenderedScreen, body),
            _ => false,
        };
        return exactDraft ? new RunnerConditionalInputRequest(generation, snapshot.LastSequence, ComposerInputProbe.KillLine,
            ExpectedInputRevision: dialog?.InputRevision, ExpectedRuntimeEpoch: dialog?.RuntimeEpoch) : null;
    }
}
