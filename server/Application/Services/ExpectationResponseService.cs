using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>Reconciles complete delivery and a later assistant ACK without typing input.</summary>
public sealed class ExpectationResponseService(AppDbContext db, TimeProvider time,
    IExpectationCatchUp catchUp, ExpectationTimingSettings timing)
{
    public async Task ReconcileAsync(ExpectationDirectiveSettings directive, CancellationToken ct)
    {
        var active = directive.Enabled
            && (directive.ActiveUntilUtc is null || directive.ActiveUntilUtc > time.GetUtcNow());
        var digest = await ExpectationConfigIdentity.ResolveAsync(db, directive, timing, ct);
        var now = time.GetUtcNow().UtcDateTime;
        // A long-lived standing agent can accumulate unacknowledged history indefinitely.
        // Rotate a bounded page each minute so old rows cannot consume the whole job budget.
        const int pageSize = 25;
        var unanswered = db.ExpectationNudges.AsNoTracking()
            .Where(n => n.DirectiveId == directive.Id && n.AnsweredAt == null);
        var unpublished = unanswered.Where(n => n.OperatorOutboxState != ExpectationOperatorOutboxState.Published
            && n.OperatorOutboxState != ExpectationOperatorOutboxState.Suppressed);
        var published = unanswered.Where(n => n.OperatorOutboxState == ExpectationOperatorOutboxState.Published);
        var nudges = await RotatedPageAsync(unpublished, now, pageSize, ct);
        // Published history has its own budget. It cannot crowd fresh deadlines out, but a
        // post-publication ACK must still be observed to stop reminders and start recurrence.
        nudges.AddRange(await RotatedPageAsync(published, now, pageSize, ct));
        foreach (var nudge in nudges)
        {
            if (!active || nudge.ConfigDigest != digest)
            {
                await SuppressStaleAsync(nudge.Id, ct);
                continue;
            }
            if (nudge.AttemptState == ExpectationAttemptState.None)
            {
                if (now >= nudge.CreatedAt.AddMinutes(timing.AnswerMinutes))
                    await DueAsync(nudge.Id, now, ct);
                continue;
            }

            if (nudge.AttemptState == ExpectationAttemptState.Attempting
                && now >= (nudge.AttemptStartedAt ?? nudge.CreatedAt).AddSeconds(20))
            {
                await using (var interrupted = await db.Database.BeginTransactionAsync(ct))
                {
                    var changed = await db.ExpectationNudges.Where(n => n.Id == nudge.Id
                            && n.AttemptState == ExpectationAttemptState.Attempting)
                        .ExecuteUpdateAsync(u => u
                            .SetProperty(n => n.AttemptState, ExpectationAttemptState.Uncertain), ct);
                    if (changed == 1)
                        await AuditAsync(nudge.Id, "Attempt interrupted; transcript reconciliation only.", ct);
                    await interrupted.CommitAsync(ct);
                }
                await DueAsync(nudge.Id, now, ct);
            }

            if (nudge.DestinationSessionId is Guid sessionId)
            {
                try { await catchUp.CatchUpAsync([sessionId], ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { /* Preserve the original deadline; an unavailable transcript is unknown. */ }

                if (nudge.BaselineSequence is { } floor
                    && nudge.DestinationGeneration is { } frozen)
                {
                    var started = await db.AgentSessions.AsNoTracking().Where(s => s.Id == sessionId)
                        .Select(s => (DateTime?)s.StartedAt).SingleOrDefaultAsync(ct);
                    if (started is { } current && SessionGeneration.Equal(frozen, SessionGeneration.Normalize(current)))
                    {
                        var typed = Antiphon.Agents.Pty.PtyInputEncoding.NormalizeBody(nudge.Body.Trim());
                        var prompts = await db.TranscriptEntries.AsNoTracking()
                            .Where(t => t.AgentSessionId == sessionId && t.Sequence > floor
                                && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt)
                            .OrderBy(t => t.Sequence).ToListAsync(ct);
                        var receipt = prompts.FirstOrDefault(t => PromptSubmissionMatch.IsConfirmedBy(typed, t.Text)
                            && PromptSubmissionMatch.IsCompleteIn(typed, t.Text));
                        if (receipt is not null)
                        {
                            if (nudge.ReceiptSequence is null)
                                await db.ExpectationNudges.Where(n => n.Id == nudge.Id && n.ReceiptSequence == null)
                                    .ExecuteUpdateAsync(u => u
                                        .SetProperty(n => n.ReceiptSequence, receipt.Sequence)
                                        .SetProperty(n => n.ReceiptAt, receipt.Timestamp ?? receipt.CreatedAt)
                                        .SetProperty(n => n.AttemptState, ExpectationAttemptState.Confirmed), ct);

                            var nextPrompt = await db.TranscriptEntries.AsNoTracking()
                                .Where(t => t.AgentSessionId == sessionId && t.Sequence > receipt.Sequence
                                    && (t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.UserPrompt
                                        || t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.QueuedUserPrompt))
                                .OrderBy(t => t.Sequence).Select(t => (long?)t.Sequence).FirstOrDefaultAsync(ct);
                            var answer = await db.TranscriptEntries.AsNoTracking()
                                .Where(t => t.AgentSessionId == sessionId && t.Sequence > receipt.Sequence
                                    && (nextPrompt == null || t.Sequence < nextPrompt)
                                    && t.Kind == Antiphon.SessionRunner.Contracts.TranscriptKinds.AssistantText)
                                .OrderBy(t => t.Sequence).ToListAsync(ct);
                            var match = answer.FirstOrDefault(t => ExpectationResponseMatcher.IsAnswer(t, nudge.Id));
                            if (match is not null)
                            {
                                await using var answered = await db.Database.BeginTransactionAsync(ct);
                                var changed = await db.ExpectationNudges.Where(n => n.Id == nudge.Id && n.AnsweredAt == null)
                                    .ExecuteUpdateAsync(u => u
                                        .SetProperty(n => n.AnsweredAt, match.Timestamp ?? match.CreatedAt)
                                        .SetProperty(n => n.AnsweredSequence, match.Sequence)
                                        .SetProperty(n => n.OperatorOutboxState,
                                            n => n.OperatorOutboxState == ExpectationOperatorOutboxState.Published
                                                ? ExpectationOperatorOutboxState.Published
                                                : ExpectationOperatorOutboxState.Suppressed), ct);
                                if (changed == 1)
                                {
                                    var page = await db.ExpectationNudges.AsNoTracking()
                                        .Where(n => n.Id == nudge.Id)
                                        .Select(n => new { n.OperatorOutboxState, n.OperatorPublicationOrdinal })
                                        .SingleAsync(ct);
                                    var detail = page.OperatorOutboxState == ExpectationOperatorOutboxState.Published
                                        ? $"Assistant ACK at sequence {match.Sequence}; reply after page {page.OperatorPublicationOrdinal}; page already sent; reminders stop."
                                        : $"Assistant ACK at sequence {match.Sequence}; unpublished operator debt suppressed.";
                                    await AuditAsync(nudge.Id, detail, ct);
                                }
                                await answered.CommitAsync(ct);
                                continue;
                            }
                        }
                    }
                }
            }

            var dueAt = nudge.AnswerDueAt ??
                (nudge.AttemptStartedAt ?? nudge.CreatedAt).AddMinutes(timing.AnswerMinutes);
            if (now >= dueAt)
                await DueAsync(nudge.Id, now, ct);
        }
    }

    private static async Task<List<ExpectationNudge>> RotatedPageAsync(
        IQueryable<ExpectationNudge> query, DateTime now, int pageSize, CancellationToken ct)
    {
        var count = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (count + pageSize - 1) / pageSize);
        var page = (int)((now.Ticks / TimeSpan.TicksPerMinute) % pageCount);
        return await query.OrderBy(n => n.CreatedAt).ThenBy(n => n.Id)
            .Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
    }

    private async Task DueAsync(Guid nudgeId, DateTime now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.ExpectationNudges.Where(n => n.Id == nudgeId && n.AnsweredAt == null
                && n.OperatorOutboxState == ExpectationOperatorOutboxState.None)
            .ExecuteUpdateAsync(u => u
                .SetProperty(n => n.OperatorOutboxState, ExpectationOperatorOutboxState.Due)
                .SetProperty(n => n.OperatorFirstDueAt, now)
                .SetProperty(n => n.OperatorNextAttemptAt, now), ct);
        if (changed == 1)
            await AuditAsync(nudgeId, "Operator page due at the persisted answer deadline.", ct);
        await tx.CommitAsync(ct);
    }

    private async Task SuppressStaleAsync(Guid nudgeId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.ExpectationNudges.Where(n => n.Id == nudgeId
                && n.OperatorOutboxState != ExpectationOperatorOutboxState.Published
                && n.OperatorOutboxState != ExpectationOperatorOutboxState.Suppressed)
            .ExecuteUpdateAsync(u => u
                .SetProperty(n => n.OperatorOutboxState, ExpectationOperatorOutboxState.Suppressed), ct);
        if (changed == 1)
            await AuditAsync(nudgeId, "Unpublished work retired after directive deactivation or config change.", ct);
        await tx.CommitAsync(ct);
    }

    private async Task AuditAsync(Guid nudgeId, string detail, CancellationToken ct)
    {
        var row = await db.ExpectationNudges.AsNoTracking().Where(n => n.Id == nudgeId)
            .Select(n => new { n.AuditCommentId, n.CheckEventIdsJson }).SingleAsync(ct);
        await ExpectationHoldAudit.AddAsync(db, row.AuditCommentId, row.CheckEventIdsJson,
            $"[expectation-nudge:{nudgeId:D}] {detail}", ExpectationLedger.AuditAuthor,
            time.GetUtcNow().UtcDateTime, ct);
        await db.SaveChangesAsync(ct);
    }
}
