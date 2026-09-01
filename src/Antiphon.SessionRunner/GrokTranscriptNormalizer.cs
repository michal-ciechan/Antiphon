using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Normalizes Grok Build's ACP update stream (<c>updates.jsonl</c> rows, one JSON-RPC notification
/// per line) into the same <see cref="TranscriptPart"/>s the Claude normalizer produces, so
/// everything downstream — working/idle, the turn-end queue flush, CARD-0055 transcript-confirmed
/// delivery, channel reply dispatch — runs unchanged. Row shapes measured against real grok 1.0.5
/// (CARD-0080 S1, canaries in <c>GrokCanaryTests</c>; fixtures from a real session's file):
///
/// <list type="bullet">
/// <item><c>user_message_chunk</c> → <c>UserPrompt</c>, emitted immediately. The recorded text is
/// what the composer kept — Grok drops every newline from typed/pasted input with no separator,
/// which is <see cref="PromptSubmissionMatch"/>'s problem, not this normalizer's.</item>
/// <item><c>agent_message_chunk</c> / <c>agent_thought_chunk</c> → coalesced per
/// <c>promptId</c> and emitted as ONE <c>AssistantText</c> / <c>Thinking</c> when the turn's
/// <c>turn_completed</c> arrives (17 message chunks and 114 thought chunks in the reference
/// session would otherwise be 131 rows). A chunk arriving AFTER its turn's <c>turn_completed</c>
/// — file order is not eventId order; an interrupted turn was measured writing a trailing chunk
/// after its cancelled turn_completed — is emitted immediately with its own (earlier) timestamp,
/// which is exactly what the server/client working rules' timestamp override exists to rank.</item>
/// <item><c>tool_call</c> → <c>ToolCall</c> (prefer <c>_meta["x.ai/tool"].name</c> over
/// title, then rawInput, toolCallId), emitted immediately — the mid-turn activity signal.
/// Non-completed <c>tool_call_update</c> is still skipped (rendering). A <c>status=completed</c>
/// update of a question-tool (<c>name == ask_user_question</c> or <c>kind == ask_user</c>,
/// joined by <c>toolCallId</c> because the completed row has empty title / no <c>_meta</c>)
/// becomes one <c>ToolResult</c> (CARD-0241). Completed updates of other tools stay skipped
/// so command output does not flood the transcript.</item>
/// <item><c>turn_completed</c> → <c>TurnEnd</c> carrying <c>stop_reason</c> verbatim
/// (<c>end_turn</c>; <c>cancelled</c> for Esc — explicitly marked, so none of Claude's
/// interrupt/compaction marker predicates are needed for Grok) and the usage block when present
/// (a cancelled stop has none). <c>prompt_id</c> rides as ApiCallId.</item>
/// <item><c>auto_compact_completed</c> → one usage-bearing <c>CompactBoundary</c> (CARD-0157):
/// Text is <c>Context compacted (auto): tokens {before} -&gt; {after}</c> (never
/// <c>(manual)</c> — housekeeping to every working rule; Grok writes no user chunk and no
/// <c>turn_completed</c> so there is nothing to strand). <c>InputTokens = tokens_after</c>.
/// A missing <c>tokens_after</c> degrades to a plain (non-usage-bearing) boundary.</item>
/// <item><c>compaction_checkpoint</c> stays skipped — checkpoint bookkeeping, no token
/// payload.</item>
/// <item>Everything else is skipped, lossy by design like the Claude normalizer:
/// <c>plan</c>, <c>task_backgrounded</c>/<c>task_completed</c>, <c>session_recap</c> (the
/// /recap summary — NOT compaction; S1 corrected the plan on this).</item>
/// </list>
///
/// Stateful because of the coalescing; one instance per tailed file, and a re-tail from offset 0
/// over the same bytes reproduces the same parts in the same order (uuids are the rows' own
/// <c>eventId</c>s), so sequences and dedup keys are stable across restarts.
/// </summary>
public sealed class GrokTranscriptNormalizer
{
    private const int MaxToolInputChars = 10_000;
    private const int MaxToolResultChars = 4_000;
    private const string TruncationMarker = "…[truncated]";
    private const int CompletedPromptIdsKept = 64;
    private const int ToolCallMetaKept = 64;

    private sealed class PendingTurn
    {
        public StringBuilder Message { get; } = new();
        public string? MessageUuid;
        public DateTimeOffset? MessageTimestamp;
        public StringBuilder Thought { get; } = new();
        public string? ThoughtUuid;
        public DateTimeOffset? ThoughtTimestamp;
    }

    // Keyed by promptId ("" when a chunk carries none — fakegrok's do not); insertion-ordered so a
    // flush emits keyless chunks alongside the turn they streamed with.
    private readonly Dictionary<string, PendingTurn> _pending = new(StringComparer.Ordinal);
    private readonly List<string> _pendingOrder = new();
    private readonly Dictionary<string, int> _nextSegment = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _lastSegmentId = new(StringComparer.Ordinal);
    private readonly Queue<string> _completedOrder = new();
    private readonly HashSet<string> _completedPromptIds = new(StringComparer.Ordinal);
    // CARD-0241: opening tool_call (and updates that still carry _meta["x.ai/tool"]) so a
    // completed update with empty title / no meta can still be recognized as a question-tool.
    private readonly Dictionary<string, ToolCallMeta> _toolCalls = new(StringComparer.Ordinal);
    private readonly Queue<string> _toolCallOrder = new();

    private readonly record struct ToolCallMeta(string? Name, string? Kind);

    public IReadOnlyList<TranscriptPart> Normalize(string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
            return [];

        JsonDocument doc;
        try { doc = JsonDocument.Parse(jsonLine); }
        catch (JsonException) { return []; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("params", out var p)
                || p.ValueKind != JsonValueKind.Object
                || !p.TryGetProperty("update", out var update)
                || update.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var uuid = GetMetaString(p, "eventId");
            var ts = GetTimestamp(root, p);
            var promptId = GetMetaString(p, "promptId");

            return GetString(update, "sessionUpdate") switch
            {
                "user_message_chunk" => FromUserChunk(update, uuid, ts),
                "agent_message_chunk" => AccumulateOrEmit(update, uuid, ts, promptId, thought: false),
                "agent_thought_chunk" => AccumulateOrEmit(update, uuid, ts, promptId, thought: true),
                "tool_call" => FromToolCall(update, uuid, ts, promptId),
                "tool_call_update" => FromToolCallUpdate(update, uuid, ts),
                "turn_completed" => FromTurnCompleted(update, uuid, ts),
                "auto_compact_completed" => FromAutoCompactCompleted(update, uuid, ts),
                _ => [],
            };
        }
    }

    /// <summary>
    /// Chunks streamed but the turn's <c>turn_completed</c> never arrived (child died mid-turn,
    /// tail cut). Emits what accumulated so the text is not lost; deliberately NO TurnEnd — the
    /// relaunch path's SessionRestartBoundary is what ends a turn the process abandoned.
    /// </summary>
    public IReadOnlyList<TranscriptPart> FlushPending()
    {
        var parts = new List<TranscriptPart>();
        foreach (var key in _pendingOrder.ToList())
            CloseCurrentSegment(parts, key.Length == 0 ? null : key);
        _pending.Clear();
        _pendingOrder.Clear();
        return parts;
    }

    private List<TranscriptPart> FromUserChunk(JsonElement update, string? uuid, DateTimeOffset? ts)
    {
        var text = GetContentText(update);
        return string.IsNullOrWhiteSpace(text)
            ? []
            : [new TranscriptPart(TranscriptKinds.UserPrompt, uuid, null, ts, "user", text, null, null, null, null, null)];
    }

    private List<TranscriptPart> AccumulateOrEmit(
        JsonElement update, string? uuid, DateTimeOffset? ts, string? promptId, bool thought)
    {
        var text = GetContentText(update);
        if (string.IsNullOrEmpty(text))
            return [];

        // A chunk for a turn that already completed (measured: eventId N-46 written after the
        // cancelled turn_completed N-47) — emit it now rather than hold it for a flush that will
        // never come. Its own timestamp predates the TurnEnd's, so the working rules' timestamp
        // override reads the session idle despite the row landing above the end in sequence.
        if (promptId is not null && _completedPromptIds.Contains(promptId))
        {
            return
            [
                new TranscriptPart(
                    thought ? TranscriptKinds.Thinking : TranscriptKinds.AssistantText,
                    uuid, null, ts, "assistant", text, null, null, null, null, null,
                    ApiCallId: promptId),
            ];
        }

        var key = promptId ?? "";
        if (!_pending.TryGetValue(key, out var turn))
        {
            _pending[key] = turn = new PendingTurn();
            _pendingOrder.Add(key);
        }

        if (thought)
        {
            turn.ThoughtUuid ??= uuid;
            turn.Thought.Append(text);
            turn.ThoughtTimestamp = ts ?? turn.ThoughtTimestamp;
        }
        else
        {
            turn.MessageUuid ??= uuid;
            turn.Message.Append(text);
            turn.MessageTimestamp = ts ?? turn.MessageTimestamp;
        }

        return [];
    }

    private List<TranscriptPart> FromToolCall(
        JsonElement update, string? uuid, DateTimeOffset? ts, string? promptId)
    {
        // CARD-0159 S4: a tool_call closes the current message segment so the turn-ending
        // response is its OWN AssistantText, the way Claude's per-message.id rows already are.
        var parts = new List<TranscriptPart>();
        CloseCurrentSegment(parts, promptId);

        var (metaName, metaKind) = GetXaiTool(update);
        var toolCallId = GetString(update, "toolCallId");
        var toolName = metaName ?? GetString(update, "title");
        if (toolCallId is not null)
            RememberToolCall(toolCallId, toolName, metaKind);

        var input = update.TryGetProperty("rawInput", out var raw)
            ? Truncate(raw.GetRawText(), MaxToolInputChars)
            : null;
        parts.Add(new TranscriptPart(
            TranscriptKinds.ToolCall, uuid, null, ts, "assistant",
            null, toolName, input, toolCallId, null, null));
        return parts;
    }

    /// <summary>
    /// CARD-0241: skip rendering updates; ingest a <c>status=completed</c> update only when
    /// the opening call (joined by <c>toolCallId</c>) is a question-tool. The completed row
    /// has empty title and no <c>_meta</c> — the map is the only way to recognize it.
    /// </summary>
    private List<TranscriptPart> FromToolCallUpdate(JsonElement update, string? uuid, DateTimeOffset? ts)
    {
        var toolCallId = GetString(update, "toolCallId");
        var (metaName, metaKind) = GetXaiTool(update);
        if (toolCallId is not null && (metaName is not null || metaKind is not null))
            RememberToolCall(toolCallId, metaName, metaKind);

        var status = GetString(update, "status");
        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
            || toolCallId is null
            || !_toolCalls.TryGetValue(toolCallId, out var meta)
            || !GrokQuestionTool.IsQuestionTool(meta.Name, meta.Kind))
        {
            return [];
        }

        var text = Truncate(GetCompletedToolResultText(update), MaxToolResultChars);
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return
        [
            new TranscriptPart(
                TranscriptKinds.ToolResult, uuid, null, ts, "user",
                text, meta.Name ?? GrokQuestionTool.AskUserQuestionName, null, toolCallId, false, null),
        ];
    }

    private void RememberToolCall(string toolCallId, string? name, string? kind)
    {
        if (_toolCalls.TryGetValue(toolCallId, out var existing))
        {
            _toolCalls[toolCallId] = new ToolCallMeta(name ?? existing.Name, kind ?? existing.Kind);
            return;
        }

        _toolCalls[toolCallId] = new ToolCallMeta(name, kind);
        _toolCallOrder.Enqueue(toolCallId);
        while (_toolCallOrder.Count > ToolCallMetaKept)
        {
            var evicted = _toolCallOrder.Dequeue();
            _toolCalls.Remove(evicted);
        }
    }

    private static (string? Name, string? Kind) GetXaiTool(JsonElement update)
    {
        if (!update.TryGetProperty("_meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
            return (null, null);
        if (!meta.TryGetProperty(GrokQuestionTool.XaiToolMetaKey, out var tool)
            || tool.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        return (GetString(tool, "name"), GetString(tool, "kind"));
    }

    /// <summary>
    /// Measured completed-update payload (incident line 91):
    /// <c>content: [{ type: "content", content: { type: "text", text: "User has answered…" } }]</c>.
    /// </summary>
    private static string? GetCompletedToolResultText(JsonElement update)
    {
        if (!update.TryGetProperty("content", out var content))
            return null;

        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                var text = TextFromContentNode(item);
                if (!string.IsNullOrEmpty(text))
                    return text;
            }

            return null;
        }

        return TextFromContentNode(content);
    }

    private static string? TextFromContentNode(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return null;
        var direct = GetString(node, "text");
        if (!string.IsNullOrEmpty(direct))
            return direct;
        return node.TryGetProperty("content", out var inner) && inner.ValueKind == JsonValueKind.Object
            ? GetString(inner, "text")
            : null;
    }

    private List<TranscriptPart> FromTurnCompleted(JsonElement update, string? uuid, DateTimeOffset? ts)
    {
        var parts = new List<TranscriptPart>();

        // Everything still streaming belongs to the turn that just ended (chunks carry the turn's
        // promptId, or nothing at all); flush in arrival order so the parts land under the end.
        // CARD-0159 S4: each remaining pending is the LAST segment; TurnEnd.ApiCallId is that
        // segment's id so CARD-0046's "final message" is the closing response, not the join.
        var flushedKeys = _pendingOrder.ToList();
        foreach (var key in flushedKeys)
            CloseCurrentSegment(parts, key.Length == 0 ? null : key);
        _pending.Clear();
        _pendingOrder.Clear();

        var promptId = GetString(update, "prompt_id");
        if (promptId is not null)
        {
            if (_completedPromptIds.Add(promptId))
                _completedOrder.Enqueue(promptId);
            while (_completedOrder.Count > CompletedPromptIdsKept)
                _completedPromptIds.Remove(_completedOrder.Dequeue());
        }

        int? inTok = null, outTok = null, cacheRead = null, cacheCreate = null, modelCalls = null;
        string? model = null;
        if (update.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            inTok = GetInt(usage, "inputTokens");
            outTok = GetInt(usage, "outputTokens");
            cacheRead = GetInt(usage, "cachedReadTokens");
            cacheCreate = GetInt(usage, "cacheCreationTokens");
            modelCalls = GetInt(usage, "modelCalls");
            model = FirstModelUsageKey(usage);
        }

        // stop_reason verbatim: "end_turn" normally, "cancelled" for an Esc interrupt (which
        // carries no usage). Either way the turn is OVER — cancelled is still a turn end.
        // ApiCallId is the LAST segment's id when we segmented; otherwise the promptId (legacy).
        var turnApiCallId = promptId is not null
            && _lastSegmentId.TryGetValue(promptId, out var lastSeg)
            && lastSeg is not null
                ? lastSeg
                : promptId;
        parts.Add(new TranscriptPart(
            TranscriptKinds.TurnEnd, uuid, null, ts, "assistant", null, null, null, null, null,
            GetString(update, "stop_reason"),
            ApiCallId: turnApiCallId,
            InputTokens: inTok, OutputTokens: outTok,
            CacheReadTokens: cacheRead, CacheCreationTokens: cacheCreate,
            Model: model, ModelCalls: modelCalls));
        return parts;
    }

    private void CloseCurrentSegment(List<TranscriptPart> parts, string? promptId)
    {
        var key = promptId ?? "";
        var index = _nextSegment.GetValueOrDefault(key);
        var segmentId = key.Length == 0 ? $":{index}" : $"{key}:{index}";
        if (_pending.TryGetValue(key, out var turn))
        {
            EmitPending(parts, turn, segmentId);
            _pending.Remove(key);
            _pendingOrder.Remove(key);
        }

        // Always advance: a tool_call with no pending text still closes a segment, so
        // turn_completed's last-segment id is the empty post-tool response (CARD-0159 S4).
        _nextSegment[key] = index + 1;
        _lastSegmentId[key] = segmentId;
    }

    /// <summary>
    /// Grok's own occupancy number (CARD-0157). The row is named <c>auto_compact_completed</c>
    /// even when a typed <c>/compact</c> produced it, so the text always carries <c>(auto)</c>
    /// and never <c>(manual)</c> — housekeeping, not a turn end.
    /// </summary>
    private static List<TranscriptPart> FromAutoCompactCompleted(
        JsonElement update, string? uuid, DateTimeOffset? ts)
    {
        var before = GetInt(update, "tokens_before");
        var after = GetInt(update, "tokens_after");
        var text = before is int b && after is int a
            ? $"Context compacted (auto): tokens {b} -> {a}"
            : "Context compacted (auto)";
        return
        [
            new TranscriptPart(
                TranscriptKinds.CompactBoundary, uuid, null, ts, "assistant", text,
                null, null, null, null, null,
                InputTokens: after),
        ];
    }

    private void EmitPending(List<TranscriptPart> parts, PendingTurn turn, string? apiCallId)
    {
        if (turn.Thought.Length > 0)
        {
            parts.Add(new TranscriptPart(
                TranscriptKinds.Thinking, turn.ThoughtUuid, null, turn.ThoughtTimestamp,
                "assistant", turn.Thought.ToString(), null, null, null, null, null,
                ApiCallId: apiCallId));
        }

        if (turn.Message.Length > 0)
        {
            parts.Add(new TranscriptPart(
                TranscriptKinds.AssistantText, turn.MessageUuid, null, turn.MessageTimestamp,
                "assistant", turn.Message.ToString(), null, null, null, null, null,
                ApiCallId: apiCallId));
        }
    }

    private static string? GetContentText(JsonElement update) =>
        update.TryGetProperty("content", out var content)
        && content.ValueKind == JsonValueKind.Object
            ? GetString(content, "text")
            : null;

    // eventId/promptId/agentTimestampMs live in params._meta, NOT update._meta (which holds
    // modelId/promptIndex on user chunks and streaming metadata on agent chunks).
    private static string? GetMetaString(JsonElement p, string prop) =>
        p.TryGetProperty("_meta", out var meta) ? GetString(meta, prop) : null;

    private static DateTimeOffset? GetTimestamp(JsonElement root, JsonElement p)
    {
        if (p.TryGetProperty("_meta", out var meta)
            && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("agentTimestampMs", out var ms)
            && ms.ValueKind == JsonValueKind.Number
            && ms.TryGetInt64(out var msValue))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(msValue);
        }

        // Fallback: the row's top-level unix-seconds stamp.
        return root.TryGetProperty("timestamp", out var s)
            && s.ValueKind == JsonValueKind.Number
            && s.TryGetInt64(out var sValue)
                ? DateTimeOffset.FromUnixTimeSeconds(sValue)
                : null;
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? GetInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;

    /// <summary>
    /// The first <c>usage.modelUsage</c> property name (observed <c>grok-4.6-build</c>). That
    /// id is what <c>ContextWindowSettings.ModelOverrides</c> substring-matches against.
    /// </summary>
    private static string? FirstModelUsageKey(JsonElement usage)
    {
        if (!usage.TryGetProperty("modelUsage", out var modelUsage)
            || modelUsage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var prop in modelUsage.EnumerateObject())
            return prop.Name;
        return null;
    }

    private static string? Truncate(string? s, int max) =>
        s is { Length: > 0 } && s.Length > max ? s[..max] + TruncationMarker : s;
}
