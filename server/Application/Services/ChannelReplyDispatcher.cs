using System.Collections.Concurrent;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Routes an agent's turn output back down the external channel that asked for it. The bridge
/// registers a pending correlation when it enqueues a channel message into a session; when that
/// session completes a turn (<c>TurnEnd</c>/<c>end_turn</c>, observed by <see cref="AgentSessionRuntime"/>),
/// this dispatcher matches the turn's <c>UserPrompt</c> back to the pending prompt, extracts the
/// assistant's text for that turn, classifies it (final answer vs question — see
/// <see cref="ChannelReplyKind"/>; Progress is reserved for future mid-turn notes), and produces a
/// <see cref="ChannelReply"/> to the outbound topic.
///
/// Singleton: owns the in-memory correlation map. Prompt-matching (not blind FIFO) means a turn a
/// human triggered directly in the terminal never sends a stray reply to the chat.
/// </summary>
public sealed class ChannelReplyDispatcher
{
    public sealed record PendingChannelReply(
        Guid ChannelId,
        string Provider,
        string? ReplyHandle,
        string ConversationId,
        string Prompt,
        DateTime EnqueuedAtUtc);

    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<PendingChannelReply>> _pending = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAntiphonMessagingProducer _producer;
    private readonly Settings.ChannelBridgeSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChannelReplyDispatcher> _logger;

    public ChannelReplyDispatcher(
        IServiceScopeFactory scopeFactory,
        IAntiphonMessagingProducer producer,
        IOptions<Settings.ChannelBridgeSettings> settings,
        TimeProvider timeProvider,
        ILogger<ChannelReplyDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _producer = producer;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Register "session X owes channel Y a reply for prompt P".</summary>
    public void Track(Guid sessionId, PendingChannelReply pending) =>
        _pending.GetOrAdd(sessionId, _ => new ConcurrentQueue<PendingChannelReply>()).Enqueue(pending);

    /// <summary>Pending correlations for a session (test/diagnostic surface).</summary>
    public int PendingCount(Guid sessionId) =>
        _pending.TryGetValue(sessionId, out var q) ? q.Count : 0;

    /// <summary>
    /// Called on every completed turn. Cheap no-op for sessions with no channel correlations.
    /// </summary>
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
            _logger.LogWarning(ex, "Failed to dispatch channel reply for session {SessionId}", sessionId);
        }
    }

    private async Task DispatchAsync(Guid sessionId, ConcurrentQueue<PendingChannelReply> queue, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The turn that just finished: latest TurnEnd, its preceding UserPrompt, and the assistant
        // text in between.
        var turnEndSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd)
            .MaxAsync(t => (long?)t.Sequence, ct);
        if (turnEndSeq is not long endSeq)
            return;

        var userPrompt = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Sequence < endSeq)
            .OrderByDescending(t => t.Sequence)
            .FirstOrDefaultAsync(ct);
        if (userPrompt?.Text is not string promptText)
            return;

        // Only answer turns WE started: match the turn's prompt against a pending correlation. A human
        // typing directly into the terminal produces turns that match nothing and are skipped.
        var pending = TakeMatching(queue, promptText);
        if (pending is null)
            return;

        var responseText = await ExtractTurnResponseAsync(db, sessionId, userPrompt.Sequence, endSeq, ct);
        if (string.IsNullOrWhiteSpace(responseText))
        {
            _logger.LogInformation(
                "Turn for channel {ChannelId} produced no assistant text; skipping reply", pending.ChannelId);
            return;
        }

        var text = responseText.Length > _settings.MaxReplyChars
            ? responseText[.._settings.MaxReplyChars] + "…"
            : responseText;

        var reply = new ChannelReply
        {
            Channel = pending.Provider,
            ReplyHandle = pending.ReplyHandle,
            ConversationId = pending.ConversationId,
            Text = text,
            Kind = ClassifyKind(responseText),
        };
        await _producer.SendAsync(reply, ct);
        _logger.LogInformation(
            "Sent {Kind} reply ({Chars} chars) to {Provider} conversation {ConversationId} from session {SessionId}",
            reply.Kind, text.Length, pending.Provider, pending.ConversationId, sessionId);
    }

    private static async Task<string?> ExtractTurnResponseAsync(
        AppDbContext db, Guid sessionId, long promptSeq, long endSeq, CancellationToken ct)
    {
        var texts = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.AssistantText
                && t.Sequence > promptSeq
                && t.Sequence < endSeq)
            .OrderBy(t => t.Sequence)
            .Select(t => t.Text)
            .ToListAsync(ct);

        var joined = string.Join("\n\n", texts.Where(t => !string.IsNullOrWhiteSpace(t)));
        return string.IsNullOrWhiteSpace(joined) ? null : joined.Trim();
    }

    // The delivered prompt and the transcript's UserPrompt should be byte-identical, but hooks can
    // append suffixes and long prompts may be normalised — match on a generous prefix, both directions.
    private PendingChannelReply? TakeMatching(ConcurrentQueue<PendingChannelReply> queue, string turnPrompt)
    {
        var normalizedTurn = Normalize(turnPrompt);
        var retained = new List<PendingChannelReply>();
        PendingChannelReply? match = null;

        while (queue.TryDequeue(out var candidate))
        {
            if (match is null && PromptsMatch(Normalize(candidate.Prompt), normalizedTurn))
            {
                match = candidate;
                continue;
            }
            retained.Add(candidate);
        }
        foreach (var keep in retained)
            queue.Enqueue(keep);

        return match;
    }

    private static bool PromptsMatch(string pending, string turn)
    {
        var probe = pending.Length <= 120 ? pending : pending[..120];
        return turn.StartsWith(probe, StringComparison.Ordinal);
    }

    private static string Normalize(string s) =>
        s.ReplaceLineEndings("\n").Trim();

    /// <summary>
    /// Final answer vs blocking question. Heuristic: the response's closing line asking something is
    /// the strongest signal the agent is waiting on the human. (Progress is reserved for future
    /// mid-turn notes; a completed turn is never Progress.)
    /// </summary>
    internal static ChannelReplyKind ClassifyKind(string responseText)
    {
        var lines = responseText.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return ChannelReplyKind.Answer;

        // Look at the tail of the response: a question mark ending any of the last two lines.
        return lines.TakeLast(2).Any(l => l.EndsWith('?'))
            ? ChannelReplyKind.Question
            : ChannelReplyKind.Answer;
    }

    private void EvictStale(ConcurrentQueue<PendingChannelReply> queue)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-_settings.PendingReplyTtlMinutes);
        while (queue.TryPeek(out var head) && head.EnqueuedAtUtc < cutoff)
        {
            if (queue.TryDequeue(out var dropped))
                _logger.LogWarning(
                    "Dropped stale channel reply correlation for channel {ChannelId} (enqueued {EnqueuedAt:u})",
                    dropped.ChannelId, dropped.EnqueuedAtUtc);
        }
    }
}
