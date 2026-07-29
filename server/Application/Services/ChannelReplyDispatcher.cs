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

    private sealed record ReplyTarget(string Provider, string? ReplyHandle, string ConversationId);

    // The last turn we replied for, per session. Claude can keep writing AssistantText AFTER the
    // TurnEnd that triggered dispatch (observed live 2026-07-29, AZ Care: TurnEnd, AssistantText,
    // TurnEnd — the first dispatch consumed the correlations with only the turn's interim narration
    // extracted, and the real answer landed one second later with nothing left to match). Remembering
    // the dispatched turn's watermark lets that trailing text go out as a follow-up reply. Record
    // equality (value-compared watermark, reference-compared targets) makes TryUpdate an atomic
    // claim, so racing triggers can't double-send.
    private readonly ConcurrentDictionary<Guid, DispatchedTurn> _dispatched = new();

    private sealed record DispatchedTurn(long PromptSeq, long MaxTextSeq, IReadOnlyList<ReplyTarget> Targets);

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
        try
        {
            if (_pending.TryGetValue(sessionId, out var queue) && !queue.IsEmpty)
            {
                EvictStale(queue);
                if (!queue.IsEmpty)
                    await DispatchAsync(sessionId, queue, ct);
            }

            // Trailing text for an already-answered turn (stop marker mid-stream) goes out as a
            // follow-up. No-op unless this session's last dispatched turn is still the live one.
            if (_dispatched.ContainsKey(sessionId))
                await DispatchFollowUpAsync(sessionId, ct);
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

        // Extract the response BEFORE consuming any correlations: Claude sometimes writes the
        // turn's stop marker before its reply text (observed live 2026-07-24: TurnEnd seq N,
        // AssistantText seq N+1) — consuming on a text-less TurnEnd loses the reply forever.
        // With no text yet the correlations stay pending; the AssistantText that follows (or a
        // later TurnEnd) re-triggers dispatch, and genuinely silent turns' correlations age out
        // via the TTL.
        var (responseText, maxTextSeq) = await ExtractTurnResponseAsync(db, sessionId, userPrompt.Sequence, ct);
        if (string.IsNullOrWhiteSpace(responseText))
        {
            _logger.LogDebug(
                "Turn on session {SessionId} has no assistant text yet; correlations stay pending", sessionId);
            return;
        }

        // Only answer turns WE started: match the turn's prompt against pending correlations. A human
        // typing directly into the terminal produces turns that match nothing and are skipped. A
        // BATCHED turn (several queued channel messages coalesced into one body) matches — and
        // consumes — every constituent correlation by containment.
        var matches = TakeAllMatching(queue, promptText);
        if (matches.Count == 0)
            return;

        // Consuming the correlations closes the turn — remember its watermark so text that lands
        // AFTER this dispatch (stop marker mid-stream) can still be delivered as a follow-up.
        var targets = matches
            .GroupBy(m => (m.Provider, m.ConversationId))
            .Select(g => g.Last())
            .Select(m => new ReplyTarget(m.Provider, m.ReplyHandle, m.ConversationId))
            .ToList();
        _dispatched[sessionId] = new DispatchedTurn(userPrompt.Sequence, maxTextSeq, targets);

        // The frozen silent-turn contract: a whole-turn NO_REPLY consumes the correlations and
        // sends nothing — system notes and housekeeping turns must never spam the chat.
        if (ChannelContracts.IsNoReply(responseText))
        {
            _logger.LogInformation(
                "Silent turn (NO_REPLY) on session {SessionId}; {Count} correlation(s) consumed without a reply",
                sessionId, matches.Count);
            return;
        }

        var (bodyText, attachments) = PrepareReplyBody(responseText, sessionId);
        var text = Truncate(bodyText);
        var kind = ClassifyKind(bodyText);

        // One reply per distinct conversation, addressed via the NEWEST match's handle. With
        // same-conversation batching this loop is degenerate (exactly one send) — the fan-out is a
        // deliberate latent safety net in case batching scope ever widens to cross-conversation.
        foreach (var target in targets)
        {
            var reply = new ChannelReply
            {
                Channel = target.Provider,
                ReplyHandle = target.ReplyHandle,
                ConversationId = target.ConversationId,
                Text = text.Length == 0 ? null : text,
                Kind = kind,
                Attachments = attachments,
            };
            await _producer.SendAsync(reply, ct);
            _logger.LogInformation(
                "Sent {Kind} reply ({Chars} chars, {AttachmentCount} attachment(s)) to {Provider} conversation {ConversationId} from session {SessionId}",
                reply.Kind, text.Length, attachments.Count, target.Provider, target.ConversationId, sessionId);
        }
    }

    /// <summary>
    /// Resolves the agent's <c>[[attach: path]]</c> markers into inline attachments: marker lines
    /// come out of the text, files are read from THIS host's disk (agent and server share the
    /// machine; the remote gateway cannot), and files that are missing or blow the raw budget are
    /// skipped with a note appended to the text so the agent's mistake is visible in the chat.
    /// </summary>
    private (string Text, IReadOnlyList<OutboundAttachment> Attachments) PrepareReplyBody(
        string responseText, Guid sessionId)
    {
        var (text, paths) = ChannelContracts.ExtractAttachments(responseText);
        if (paths.Count == 0)
            return (responseText, []);

        var attachments = new List<OutboundAttachment>();
        var notes = new List<string>();
        var budget = _settings.MaxAttachmentBytes; // cumulative: all attachments share one Kafka message

        foreach (var path in paths)
        {
            FileInfo file;
            try { file = new FileInfo(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                notes.Add($"⚠️ attachment path invalid: {path}");
                continue;
            }

            if (!file.Exists)
            {
                notes.Add($"⚠️ attachment not found: {path}");
                _logger.LogWarning("Session {SessionId} attach marker points at a missing file: {Path}", sessionId, path);
                continue;
            }
            if (file.Length > budget)
            {
                notes.Add($"⚠️ attachment skipped — {file.Name} is {file.Length / (1024 * 1024)} MB, over the {_settings.MaxAttachmentBytes / (1024 * 1024)} MB limit");
                _logger.LogWarning(
                    "Session {SessionId} attachment {Path} ({Bytes} bytes) exceeds the remaining budget {Budget}",
                    sessionId, path, file.Length, budget);
                continue;
            }

            attachments.Add(new OutboundAttachment
            {
                Kind = InferAttachmentKind(file.Extension),
                Source = file.FullName,
                Content = File.ReadAllBytes(file.FullName),
                Name = file.Name,
                Mime = InferMime(file.Extension),
            });
            budget -= file.Length;
        }

        if (notes.Count > 0)
            text = text.Length == 0 ? string.Join("\n", notes) : $"{text}\n\n{string.Join("\n", notes)}";
        return (text, attachments);
    }

    private static AttachmentKind InferAttachmentKind(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => AttachmentKind.Image,
        ".mp4" or ".mov" or ".webm" => AttachmentKind.Video,
        ".mp3" or ".m4a" or ".wav" or ".flac" => AttachmentKind.Audio,
        ".ogg" or ".oga" => AttachmentKind.Voice,
        _ => AttachmentKind.File,
    };

    private static string InferMime(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".txt" or ".md" => "text/plain",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".zip" => "application/zip",
        ".mp4" => "video/mp4",
        ".mp3" => "audio/mpeg",
        ".ogg" or ".oga" => "audio/ogg",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream",
    };

    // A turn we already replied for can gain more AssistantText: Claude sometimes writes a stop
    // marker mid-stream, dispatch fires with only the text so far (e.g. interim narration between
    // tool calls), and the real answer follows seconds later. The correlations are gone by then, so
    // route the trailing text to the same targets. The watermark claim via TryUpdate keeps racing
    // triggers (AssistantText arrival + the closing TurnEnd) from double-sending.
    private async Task DispatchFollowUpAsync(Guid sessionId, CancellationToken ct)
    {
        if (!_dispatched.TryGetValue(sessionId, out var turn))
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A newer prompt means the dispatched turn is over — anything after it belongs to the next
        // turn's dispatch, so the record is done.
        var latestPromptSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt)
            .MaxAsync(t => (long?)t.Sequence, ct);
        if (latestPromptSeq != turn.PromptSeq)
        {
            _dispatched.TryRemove(new KeyValuePair<Guid, DispatchedTurn>(sessionId, turn));
            return;
        }

        var late = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.AssistantText
                && t.Sequence > turn.MaxTextSeq)
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.Sequence, t.Text })
            .ToListAsync(ct);
        var texts = late.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();
        if (texts.Count == 0)
            return;

        // Claim the trailing entries before sending; the loser of a race sees the moved watermark.
        var claimed = turn with { MaxTextSeq = late[^1].Sequence };
        if (!_dispatched.TryUpdate(sessionId, claimed, turn))
            return;

        var joined = string.Join("\n\n", texts.Select(l => l.Text!)).Trim();
        if (ChannelContracts.IsNoReply(joined))
            return;

        var (bodyText, attachments) = PrepareReplyBody(joined, sessionId);
        var text = Truncate(bodyText);
        var kind = ClassifyKind(bodyText);
        foreach (var target in turn.Targets)
        {
            var reply = new ChannelReply
            {
                Channel = target.Provider,
                ReplyHandle = target.ReplyHandle,
                ConversationId = target.ConversationId,
                Text = text.Length == 0 ? null : text,
                Kind = kind,
                Attachments = attachments,
            };
            await _producer.SendAsync(reply, ct);
            _logger.LogInformation(
                "Sent follow-up {Kind} reply ({Chars} chars, {AttachmentCount} attachment(s)) to {Provider} conversation {ConversationId} from session {SessionId} — text arrived after the turn's dispatch",
                reply.Kind, text.Length, attachments.Count, target.Provider, target.ConversationId, sessionId);
        }
    }

    private string Truncate(string responseText) =>
        responseText.Length > _settings.MaxReplyChars
            ? responseText[.._settings.MaxReplyChars] + "…"
            : responseText;

    // The turn's response = all assistant text after its prompt up to the NEXT prompt — NOT capped
    // at the TurnEnd sequence, because the stop marker can precede the reply text in Claude's
    // transcript ordering. At dispatch time the next turn hasn't produced entries yet, so an open
    // upper bound is safe. Also returns the highest sequence included, so trailing text that lands
    // after this dispatch can be told apart from what was already sent.
    private static async Task<(string? Text, long MaxSeq)> ExtractTurnResponseAsync(
        AppDbContext db, Guid sessionId, long promptSeq, CancellationToken ct)
    {
        var nextPromptSeq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
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
            .Select(t => new { t.Sequence, t.Text })
            .ToListAsync(ct);

        var maxSeq = entries.Count > 0 ? entries[^1].Sequence : promptSeq;
        var joined = string.Join("\n\n", entries.Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        return (string.IsNullOrWhiteSpace(joined) ? null : joined.Trim(), maxSeq);
    }

    // The delivered prompt and the transcript's UserPrompt should be byte-identical, but hooks can
    // append suffixes and long prompts may be normalised — match on a generous probe. Consumes
    // EVERY match: a batched turn's body contains several pending prompts, and each must be
    // settled by the one reply (queue order is preserved for non-matches).
    private List<PendingChannelReply> TakeAllMatching(ConcurrentQueue<PendingChannelReply> queue, string turnPrompt)
    {
        var normalizedTurn = Normalize(turnPrompt);
        var retained = new List<PendingChannelReply>();
        var matches = new List<PendingChannelReply>();

        while (queue.TryDequeue(out var candidate))
        {
            if (PromptsMatch(Normalize(candidate.Prompt), normalizedTurn))
                matches.Add(candidate);
            else
                retained.Add(candidate);
        }
        foreach (var keep in retained)
            queue.Enqueue(keep);

        return matches;
    }

    private static bool PromptsMatch(string pending, string turn)
    {
        var probe = pending.Length <= 120 ? pending : pending[..120];
        // Containment, not prefix: an unbatched turn IS the pending prompt (plus possible hook
        // suffixes); a batched turn embeds it after the batch markers. A human-typed turn contains
        // neither — the 120-char enveloped probe ([Telegram "…" — …] …) is not something a human
        // types into the terminal by accident.
        return turn.Contains(probe, StringComparison.Ordinal);
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
