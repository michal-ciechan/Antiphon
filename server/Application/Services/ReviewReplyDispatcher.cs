using System.Collections.Concurrent;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Routes an agent's turn output back onto the review thread that asked for it — the review-loop
/// sibling of <see cref="ChannelReplyDispatcher"/>, sharing its shape: track a pending correlation
/// when the thread prompt is enqueued; on turn end, match the turn's prompt (typed
/// <c>UserPrompt</c> or <c>QueuedUserPrompt</c>) back to the pending envelope, extract the assistant
/// text for that turn, and append it as the thread's agent comment.
/// Prompt-matching (the unique <c>[Review #id]</c> tag) means several threads can be in flight and
/// each answer lands on its own thread; a turn a human triggered matches nothing and is skipped.
/// Singleton: owns the in-memory correlation map.
/// </summary>
public sealed class ReviewReplyDispatcher
{
    public sealed record PendingThreadReply(Guid ThreadId, string Prompt, DateTime EnqueuedAtUtc);

    private static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(60);

    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<PendingThreadReply>> _pending = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReviewReplyDispatcher> _logger;

    public ReviewReplyDispatcher(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ReviewReplyDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void Track(Guid sessionId, PendingThreadReply pending) =>
        _pending.GetOrAdd(sessionId, _ => new ConcurrentQueue<PendingThreadReply>()).Enqueue(pending);

    public int PendingCount(Guid sessionId) =>
        _pending.TryGetValue(sessionId, out var q) ? q.Count : 0;

    /// <summary>Called on every completed turn (and text arrival). Cheap no-op with nothing pending.</summary>
    public async Task OnTurnEndAsync(Guid sessionId, CancellationToken ct)
    {
        if (!_pending.TryGetValue(sessionId, out var queue) || queue.IsEmpty)
            return;
        EvictStale(queue);
        if (queue.IsEmpty)
            return;

        try
        {
            await DispatchAsync(sessionId, queue, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Review reply dispatch failed for session {SessionId}", sessionId);
        }
    }

    private async Task DispatchAsync(Guid sessionId, ConcurrentQueue<PendingThreadReply> queue, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var turnEndSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd)
            .MaxAsync(t => (long?)t.Sequence, ct);
        if (turnEndSeq is not long endSeq)
            return;

        // CARD-0154: same kind set as ChannelReplyDispatcher — a review prompt queued into a busy
        // composer is QueuedUserPrompt, ranked by Sequence, never by record-to-record Timestamp.
        var userPrompt = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt)
                && t.Sequence < endSeq)
            .OrderByDescending(t => t.Sequence)
            .FirstOrDefaultAsync(ct);
        if (userPrompt?.Text is not string promptText)
            return;

        // Extract BEFORE consuming (the stop-marker-before-text lesson from the channel dispatcher):
        // no text yet → correlations stay pending; the text's own arrival re-triggers us.
        var (responseText, containsApiErrorStub) =
            await ExtractTurnResponseAsync(db, sessionId, userPrompt.Sequence, ct);

        // S8 (CARD-0071): a turn killed by the API must never be posted as a review-thread reply.
        // The stub's error string is ordinary AssistantText, so without this check "API Error: 529
        // Overloaded" would be authored as the Agent on the thread — and TakeAllMatching would
        // CONSUME the in-memory correlation, flipping the thread to AwaitingHuman on garbage with
        // nothing left to re-answer. The whole turn is withheld, not just the stub line stripped:
        // a multi-call turn can produce real text before a later API call dies, and posting the
        // fragment would settle the correlation against half an answer. Correlations stay pending
        // — a resumed turn's real answer routes by the same [Review #id] tag match, and if nothing
        // ever answers, EvictStale's TTL drop is the designed backstop (no incident in this slice:
        // the thread stays visibly stuck at its prior status).
        if (containsApiErrorStub)
        {
            _logger.LogWarning(
                "Turn on session {SessionId} (prompt seq {PromptSeq}) was killed by an API error; withholding "
                + "the review-thread reply for the whole turn. {Count} correlation(s) stay pending for a resumed turn, "
                + "with the TTL eviction as the backstop.",
                sessionId, userPrompt.Sequence, queue.Count);
            return;
        }

        if (string.IsNullOrWhiteSpace(responseText))
            return;

        var matches = TakeAllMatching(queue, promptText);
        if (matches.Count == 0)
            return;

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var eventBus = scope.ServiceProvider.GetService<IEventBus>();
        foreach (var match in matches)
        {
            var thread = await db.ReviewThreads.FirstOrDefaultAsync(t => t.Id == match.ThreadId, ct);
            if (thread is null)
                continue;

            if (!ChannelContracts.IsNoReply(responseText))
            {
                db.ReviewComments.Add(new ReviewComment
                {
                    Id = Guid.NewGuid(),
                    ThreadId = thread.Id,
                    Author = ReviewCommentAuthor.Agent,
                    Body = responseText,
                    CreatedAt = now,
                });
            }
            thread.Status = ReviewThreadStatus.AwaitingHuman;
            thread.UpdatedAt = now;
            _logger.LogInformation(
                "Review thread {ThreadId} received the agent's reply ({Chars} chars) from session {SessionId}",
                thread.Id, responseText.Length, sessionId);
        }
        await db.SaveChangesAsync(ct);

        if (eventBus is not null)
        {
            foreach (var match in matches)
            {
                try
                {
                    await eventBus.PublishToAllAsync("ReviewThreadChanged", new { threadId = match.ThreadId }, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "ReviewThreadChanged publish failed");
                }
            }
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
        var nextPromptSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt)
                && t.Sequence > promptSeq)
            .MinAsync(t => (long?)t.Sequence, ct);

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

    private static List<PendingThreadReply> TakeAllMatching(
        ConcurrentQueue<PendingThreadReply> queue, string turnPrompt)
    {
        var retained = new List<PendingThreadReply>();
        var matches = new List<PendingThreadReply>();
        while (queue.TryDequeue(out var candidate))
        {
            // The [Review #id] tag is globally unique — containment is unambiguous even when the
            // delivery batched several queued prompts into one turn body.
            var probe = ReviewPromptFormat.EnvelopePrefix(candidate.ThreadId);
            if (turnPrompt.Contains(probe, StringComparison.Ordinal))
                matches.Add(candidate);
            else
                retained.Add(candidate);
        }
        foreach (var keep in retained)
            queue.Enqueue(keep);
        return matches;
    }

    private void EvictStale(ConcurrentQueue<PendingThreadReply> queue)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - PendingTtl;
        while (queue.TryPeek(out var head) && head.EnqueuedAtUtc < cutoff)
        {
            if (queue.TryDequeue(out var dropped))
                _logger.LogWarning("Dropped stale review reply correlation for thread {ThreadId}", dropped.ThreadId);
        }
    }
}
