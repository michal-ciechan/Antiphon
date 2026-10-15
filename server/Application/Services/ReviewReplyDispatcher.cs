using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Review reply authority is the typed, durable producer queue correlation.</summary>
public sealed class ReviewReplyDispatcher(
    IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<ReviewReplyDispatcher> logger)
{
    public int PendingCount(Guid sessionId)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-60);
        return db.SessionQueuedMessages.Count(m => m.AgentSessionId == sessionId
            && m.Origin == QueuedMessageOrigin.Channel && m.ReviewThreadId != null
            && m.ReviewReplyConsumedAt == null && m.CreatedAt >= cutoff);
    }

    public async Task OnTurnEndAsync(Guid sessionId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boundary = await db.TranscriptEntries.Where(t => t.AgentSessionId == sessionId
            && t.Kind == TranscriptKinds.TurnEnd).MaxAsync(t => (long?)t.Sequence, ct);
        if (boundary is not null) await DispatchAsync(sessionId, boundary.Value, ct);
    }

    internal async Task OnTranscriptActionAsync(Guid sessionId, long triggerSequence, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var boundary = await TranscriptTurnWindow.FindActionBoundaryAsync(db, sessionId, triggerSequence, ct);
        if (boundary is not null) await DispatchAsync(sessionId, boundary.Value, ct);
    }

    private async Task DispatchAsync(Guid sessionId, long boundary, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prompt = await TranscriptTurnWindow.FindOwningPromptAsync(db, sessionId, boundary, ct);
        if (prompt?.Text is not { } promptText) return;
        var (body, apiError) = await ExtractTurnResponseAsync(db, sessionId, prompt.Sequence, ct);
        if (apiError || string.IsNullOrWhiteSpace(body)) return;
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-60);
        var rows = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.AgentSessionId == sessionId
            && m.Origin == QueuedMessageOrigin.Channel && m.ReviewReplyConsumedAt == null
            && m.ConversationKey != null && m.ConversationKey.StartsWith("review:")
            && m.CreatedAt >= cutoff).OrderBy(m => m.Sequence).ToListAsync(ct);
        foreach (var candidate in rows)
        {
            if (candidate.ReviewThreadId is not { } target)
            {
                logger.LogWarning("Legacy review correlation {QueueId} in session {SessionId} has no durable target; explicit redispatch required",
                    candidate.Id, sessionId);
                continue;
            }
            if (candidate.ConversationKey != $"review:{target}"
                || !PromptSubmissionMatch.IsConfirmedBy(candidate.Body, promptText)
                || !PromptSubmissionMatch.IsCompleteIn(candidate.Body, promptText)) continue;
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            // The conditional update serializes sweep/action races; rollback also rolls back
            // consumption if comment insertion or thread update fails.
            var consumed = await db.SessionQueuedMessages.Where(m => m.Id == candidate.Id
                && m.ReviewReplyConsumedAt == null && m.ReviewThreadId == target
                && m.AgentSessionId == sessionId && m.Origin == QueuedMessageOrigin.Channel
                && m.ConversationKey == candidate.ConversationKey && m.Body == candidate.Body)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReviewReplyConsumedAt, now), ct);
            if (consumed == 0) { await tx.RollbackAsync(ct); continue; }
            var thread = await db.ReviewThreads.SingleOrDefaultAsync(t => t.Id == target, ct);
            if (thread is null) { await tx.RollbackAsync(ct); continue; }
            if (!ChannelContracts.IsNoReply(body))
                db.ReviewComments.Add(new ReviewComment { Id = Guid.NewGuid(), ThreadId = target,
                    Author = ReviewCommentAuthor.Agent, Body = body, CreatedAt = now });
            thread.Status = ReviewThreadStatus.AwaitingHuman;
            thread.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            if (scope.ServiceProvider.GetService<IEventBus>() is { } bus)
                await bus.PublishToAllAsync("ReviewThreadChanged", new { threadId = target }, ct);
        }
    }
    // Turn response = assistant text from the prompt to the NEXT prompt (open upper bound — the
    // stop marker can precede the text; see ChannelReplyDispatcher for the live misses behind this).
    // Also returns whether the window contains an API-error stub (CARD-0071 S8), which the caller
    // withholds the whole turn on. Stub rows are additionally excluded from the join so no refactor
    // of the withhold can ever let the error string ride out inside a review comment.
    private static async Task<(string? Text, bool ContainsApiErrorStub)> ExtractTurnResponseAsync(
        AppDbContext db, Guid sessionId, long promptSeq, CancellationToken ct)
    {
        var nextPromptSeq = await TranscriptTurnWindow.FindNextTurnOpeningPromptSeqAsync(
            db, sessionId, promptSeq, ct);

        var query = db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.AssistantText
                && t.Sequence > promptSeq);
        if (nextPromptSeq is long cap)
            query = query.Where(t => t.Sequence < cap);

        var entries = await query
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.Text, t.Kind, t.IsApiError })
            .ToListAsync(ct);

        var containsStub = entries.Any(t => TranscriptKinds.IsApiErrorStub(t.Kind, t.IsApiError));
        var joined = string.Join("\n\n", entries
            .Where(t => !TranscriptKinds.IsApiErrorStub(t.Kind, t.IsApiError))
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        return (string.IsNullOrWhiteSpace(joined) ? null : joined.Trim(), containsStub);
    }

}
