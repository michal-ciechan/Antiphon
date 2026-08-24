using System.Globalization;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// Normalizes a Codex rollout JSONL (<c>CODEX_HOME/sessions/YYYY/MM/DD/rollout-&lt;ts&gt;-&lt;uuid&gt;.jsonl</c>,
/// one JSON object per line) into the same <see cref="TranscriptPart"/>s the Claude and Grok
/// normalizers produce, so everything downstream — working/idle, the turn-end queue flush, report
/// extraction, CARD-0055 transcript-confirmed delivery — runs unchanged (CARD-0099 S1).
///
/// <para><b>Codex writes TWO dialects for the same content, and which one you get depends on the
/// launch mode, not the version</b> (measured 2026-08-20 against codex-cli 0.147.0 on this
/// machine — the plan's section 2 assumed one surface and the TUI disproved it):</para>
/// <list type="bullet">
/// <item><b>Flat</b> — <c>event_msg/user_message</c> and <c>event_msg/agent_message</c>. Produced by
/// <c>codex exec</c> (originator <c>codex_exec</c>) and by the Desktop app (originator
/// <c>Codex Desktop</c>, cli 0.148.0-alpha.9).</item>
/// <item><b>Thread items</b> — <c>event_msg/item_completed</c> carrying <c>item.type</c>
/// <c>UserMessage</c>/<c>AgentMessage</c>/<c>Reasoning</c>/…. Produced by the interactive TUI
/// (originator <c>codex-tui</c>) — <b>which is the mode Antiphon launches</b>. A TUI rollout
/// contains ZERO <c>user_message</c>/<c>agent_message</c> rows, so a normalizer that reads only
/// the flat dialect ingests nothing at all from a real Antiphon Codex session.</item>
/// </list>
/// Both are handled. No measured rollout carries both dialects for the same message, but rather
/// than bet on that, each of the two content kinds latches onto the FIRST dialect it sees in a
/// file and ignores the other for the rest of that file — so a future CLI that emits both cannot
/// double-emit, and one that splits them across dialects still loses nothing (the two latches are
/// independent). Latching is deterministic on a re-tail from offset 0.
///
/// <para>Structured rows this maps:</para>
/// <list type="bullet">
/// <item><c>user_message</c> / <c>item_completed{UserMessage}</c> → <c>UserPrompt</c>.</item>
/// <item><c>agent_message</c> / <c>item_completed{AgentMessage}</c> → <c>AssistantText</c>.</item>
/// <item><c>agent_reasoning</c> / <c>item_completed{Reasoning}</c> → <c>Thinking</c>, only when it
/// actually carries text (the TUI's Reasoning items have empty <c>summary_text</c>/<c>raw_content</c>
/// — that content is in the encrypted <c>response_item</c>, which v1 does not read).</item>
/// <item><c>task_complete</c> → <c>TurnEnd</c>. Codex has no <c>stop_reason</c> field, so the
/// NORMALIZED value <c>end_turn</c> is synthesized: <c>AgentSessionRuntime.IsTurnBoundary</c>
/// keys on exactly that string, and without it no queue flush, no report extraction and no task
/// settlement would ever fire for a Codex delegate. A <c>task_complete</c> carrying an
/// <c>error</c> (measured: an unsupported model returns HTTP 400 and the turn ends there) is
/// STILL <c>end_turn</c> — the turn is over and the queue must flush — with the failure carried on
/// the API-error fields instead.</item>
/// <item><c>token_count</c> → per-turn usage, stamped on that turn's <c>TurnEnd</c> (see
/// <see cref="TurnUsage"/> for why it is a delta and not the row's own numbers).</item>
/// </list>
///
/// <para>Skipped, lossy by design like the other two normalizers: <c>session_meta</c>,
/// <c>turn_context</c>, <c>world_state</c>, <c>thread_settings_applied</c>, <c>task_started</c>
/// (the TurnEnd is the boundary that matters; a UserPrompt is what starts a turn everywhere else
/// in this codebase) and every <c>response_item</c> — the last of those is deliberate and load
/// bearing, because <c>response_item{message, role:user}</c> repeats the prompt text verbatim and
/// mapping it too would emit every user prompt twice.</para>
///
/// <para><b>Compaction needs no special treatment</b> (measured on a real working session, rollout
/// <c>01a01193-07eb…</c>): the <c>compacted</c> + <c>event_msg/context_compacted</c> pair was
/// written 66 minutes INSIDE turn <c>01a01473…</c>, whose own <c>task_complete</c> arrived hours
/// later. Codex compaction is mid-turn housekeeping, so — unlike Claude's manual <c>/compact</c>
/// (CARD-0041) — it strands nothing and must not end a turn.</para>
///
/// Stateful (dialect latches + the usage baseline), one instance per tailed file; a re-tail from
/// offset 0 over the same bytes reproduces the same parts in the same order with the same uuids.
/// </summary>
public sealed class CodexTranscriptNormalizer
{
    /// <summary>TranscriptEntry.Uuid is varchar(64); a synthesized key must fit.</summary>
    private const int MaxUuidChars = 64;

    private enum Dialect
    {
        Unknown,
        Flat,
        ThreadItem,
    }

    /// <summary>
    /// Codex's <c>token_count</c> reports <c>total_token_usage</c> (cumulative for the SESSION) and
    /// <c>last_token_usage</c> (that ONE API call). A turn is many calls — 7 of them in the
    /// measured session <c>01a01189-2109…</c> — so stamping the last call's numbers on the TurnEnd
    /// would have reported 25,080 input tokens for a turn that actually spent 138,449.
    ///
    /// Per-turn spend is therefore the DELTA of <c>total_token_usage</c> across the turn, which was
    /// measured to equal the sum of that turn's <c>last_token_usage</c> values exactly
    /// (18,638 + 18,800 + 18,868 + 18,940 + 19,015 + 19,108 + 25,080 = 138,449 = 157,058 − 18,609).
    /// The delta is preferred over the sum because it stays correct even when a <c>token_count</c>
    /// row is never read (tail cut, mid-write line).
    ///
    /// <para>One counter is NOT 1:1 with the rollup's fields. <c>DelegationCost.TotalInputTokens</c>
    /// is <c>InputTokens + CacheReadTokens + CacheCreationTokens</c> — Anthropic's three are
    /// disjoint — whereas Codex's <c>cached_input_tokens</c> is a SUBSET of <c>input_tokens</c>
    /// (measured twice: 18,609 input + 13 output = 18,622 total with 11,008 of that input cached).
    /// Mapping straight through would bill every cached token twice, so the cached portion is
    /// subtracted out of <c>InputTokens</c> and reported as <c>CacheReadTokens</c>, leaving the
    /// three disjoint and their sum equal to <c>input_tokens</c>.
    /// <c>cache_write_input_tokens</c> was 0 in every measurement, so its containment in
    /// <c>input_tokens</c> is unverified; it maps straight through, which cannot under-feed a cost
    /// brake.</para>
    /// </summary>
    private readonly record struct TurnUsage(int Input, int Cached, int CacheWrite, int Output)
    {
        public static TurnUsage Read(JsonElement usage) => new(
            GetInt(usage, "input_tokens") ?? 0,
            GetInt(usage, "cached_input_tokens") ?? 0,
            GetInt(usage, "cache_write_input_tokens") ?? 0,
            GetInt(usage, "output_tokens") ?? 0);

        public static TurnUsage operator -(TurnUsage a, TurnUsage b) => new(
            a.Input - b.Input, a.Cached - b.Cached, a.CacheWrite - b.CacheWrite, a.Output - b.Output);
    }

    private Dialect _userDialect = Dialect.Unknown;
    private Dialect _agentDialect = Dialect.Unknown;
    private string? _rolloutSessionId;
    private long _lineIndex = -1;

    // Cumulative session totals: the running value, and the value at the previous turn's end.
    private TurnUsage? _cumulative;
    private TurnUsage _turnBaseline;
    private bool _sawUsageThisTurn;

    public IReadOnlyList<TranscriptPart> Normalize(string jsonLine)
    {
        _lineIndex++;
        if (string.IsNullOrWhiteSpace(jsonLine))
            return [];

        JsonDocument doc;
        try { doc = JsonDocument.Parse(jsonLine); }
        catch (JsonException) { return []; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return [];

            var type = GetString(root, "type");
            if (type == "session_meta")
            {
                // Line 0 of every rollout. Remembered only so synthesized uuids can be scoped to
                // the file they came from; nothing is emitted.
                if (root.TryGetProperty("payload", out var meta) && meta.ValueKind == JsonValueKind.Object)
                    _rolloutSessionId ??= GetString(meta, "session_id") ?? GetString(meta, "id");
                return [];
            }

            if (type != "event_msg"
                || !root.TryGetProperty("payload", out var payload)
                || payload.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var ts = GetTimestamp(root);
            var uuid = SynthesizeUuid(root);

            return GetString(payload, "type") switch
            {
                "user_message" => FromFlatUser(payload, uuid, ts),
                "agent_message" => FromFlatAgent(payload, uuid, ts),
                "agent_reasoning" => FromFlatReasoning(payload, uuid, ts),
                "item_completed" => FromThreadItem(payload, uuid, ts),
                "token_count" => AbsorbTokenCount(payload),
                "task_complete" => FromTaskComplete(payload, uuid, ts),
                _ => [],
            };
        }
    }

    // ------------------------------------------------------------------------- the flat dialect

    private List<TranscriptPart> FromFlatUser(JsonElement payload, string? uuid, DateTimeOffset? ts)
    {
        if (!TakeDialect(ref _userDialect, Dialect.Flat))
            return [];
        var text = GetString(payload, "message");
        return string.IsNullOrWhiteSpace(text)
            ? []
            : [Part(TranscriptKinds.UserPrompt, uuid, ts, "user", text)];
    }

    private List<TranscriptPart> FromFlatAgent(JsonElement payload, string? uuid, DateTimeOffset? ts)
    {
        if (!TakeDialect(ref _agentDialect, Dialect.Flat))
            return [];
        var text = GetString(payload, "message");
        return string.IsNullOrWhiteSpace(text)
            ? []
            : [Part(TranscriptKinds.AssistantText, uuid, ts, "assistant", text)];
    }

    // Reasoning rides no dialect latch: it is neither of the two content kinds a downstream
    // consumer acts on, so losing or duplicating a Thinking row changes no behaviour.
    private static List<TranscriptPart> FromFlatReasoning(JsonElement payload, string? uuid, DateTimeOffset? ts)
    {
        var text = GetString(payload, "text") ?? GetString(payload, "message");
        return string.IsNullOrWhiteSpace(text)
            ? []
            : [Part(TranscriptKinds.Thinking, uuid, ts, "assistant", text)];
    }

    // ------------------------------------------------------------------ the thread-item dialect

    private List<TranscriptPart> FromThreadItem(JsonElement payload, string? uuid, DateTimeOffset? ts)
    {
        if (!payload.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
            return [];

        // The item's own id is a better uuid than a positional one: it survives any future change
        // to how rows are numbered, and it is what a response_item cross-reference uses.
        var itemUuid = Fit(GetString(item, "id")) ?? uuid;
        var turnId = Fit(GetString(payload, "turn_id"));

        switch (GetString(item, "type"))
        {
            case "UserMessage":
            {
                if (!TakeDialect(ref _userDialect, Dialect.ThreadItem))
                    return [];
                var text = ItemText(item);
                return string.IsNullOrWhiteSpace(text)
                    ? []
                    : [Part(TranscriptKinds.UserPrompt, itemUuid, ts, "user", text)];
            }

            case "AgentMessage":
            {
                if (!TakeDialect(ref _agentDialect, Dialect.ThreadItem))
                    return [];
                var text = ItemText(item);
                return string.IsNullOrWhiteSpace(text)
                    ? []
                    : [Part(
                        TranscriptKinds.AssistantText, itemUuid, ts, "assistant", text,
                        ApiCallId: GetString(item, "phase") == "final_answer" ? turnId : null)];
            }

            case "Reasoning":
            {
                var text = JoinStrings(item, "summary_text");
                return string.IsNullOrWhiteSpace(text)
                    ? []
                    : [Part(TranscriptKinds.Thinking, itemUuid, ts, "assistant", text)];
            }

            // CommandExecution / Extension (web search) / FileChange / McpToolCall: real activity,
            // deliberately not mapped in v1. Nothing downstream needs a mid-turn activity signal
            // from Codex — task_started/task_complete bracket the turn explicitly, which is the
            // one thing the Claude pipeline always had to infer.
            default:
                return [];
        }
    }

    /// <summary>
    /// Concatenates an item's <c>content[]</c> text. The TUI uses <c>{"type":"text"}</c> on user
    /// items and <c>{"type":"Text"}</c> on agent items (measured — the casing really does differ),
    /// so the discriminator is not read at all: every element's <c>text</c> is taken.
    /// </summary>
    private static string? ItemText(JsonElement item)
    {
        if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;

        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object && GetString(block, "text") is { Length: > 0 } t)
                sb.Append(t);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    // ------------------------------------------------------------------------- usage + turn end

    private List<TranscriptPart> AbsorbTokenCount(JsonElement payload)
    {
        if (!payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
            return [];
        if (!info.TryGetProperty("total_token_usage", out var total) || total.ValueKind != JsonValueKind.Object)
            return [];

        var cumulative = TurnUsage.Read(total);
        if (_cumulative is null)
        {
            // A resumed/forked rollout opens with the previous conversation's totals already in
            // it, so the baseline is what this file's FIRST call started from — total minus that
            // call's own usage — not zero. On a fresh session the two are equal and this is 0.
            var last = info.TryGetProperty("last_token_usage", out var l) && l.ValueKind == JsonValueKind.Object
                ? TurnUsage.Read(l)
                : default;
            _turnBaseline = cumulative - last;
        }

        _cumulative = cumulative;
        _sawUsageThisTurn = true;
        return [];
    }

    private List<TranscriptPart> FromTaskComplete(JsonElement payload, string? uuid, DateTimeOffset? ts)
    {
        // turn_id is the natural API-call attribution key: DelegationUsageRollup groups by
        // ApiCallId and counts each group once, and one Codex turn is one billed unit here.
        var turnId = Fit(GetString(payload, "turn_id"));

        int? inTok = null, outTok = null, cacheRead = null, cacheWrite = null;
        if (_sawUsageThisTurn && _cumulative is { } now)
        {
            var delta = now - _turnBaseline;
            cacheRead = Math.Max(0, delta.Cached);
            // De-overlapped: see TurnUsage. Clamped so a future CLI that changes the containment
            // rule cannot produce a negative token count.
            inTok = Math.Max(0, delta.Input - delta.Cached);
            cacheWrite = Math.Max(0, delta.CacheWrite);
            outTok = Math.Max(0, delta.Output);
            _turnBaseline = now;
        }

        _sawUsageThisTurn = false;

        var (isError, errorClass, errorStatus) = ReadError(payload);

        return
        [
            new TranscriptPart(
                TranscriptKinds.TurnEnd, uuid ?? turnId, null, ts, "assistant",
                null, null, null, null, null,
                // Synthesized, not verbatim — Codex has no stop_reason. See the class remarks.
                StopReason: "end_turn",
                ApiCallId: turnId,
                InputTokens: inTok, OutputTokens: outTok,
                CacheReadTokens: cacheRead, CacheCreationTokens: cacheWrite,
                IsApiError: isError, ApiErrorClass: errorClass, ApiErrorStatus: errorStatus),
        ];
    }

    /// <summary>
    /// A failed turn's <c>error</c> block. Measured shape (an unsupported model on this account):
    /// <c>{"message":"{\"type\":\"error\",\"status\":400,\"error\":{\"type\":\"invalid_request_error\",…}}",
    /// "codex_error_info":"other"}</c> — the useful part is a JSON document inside a string, so it
    /// is re-parsed for the status and class. When it is not JSON, <c>codex_error_info</c> stands in.
    /// </summary>
    private static (bool? IsError, string? Class, int? Status) ReadError(JsonElement payload)
    {
        if (!payload.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            return (null, null, null);

        var fallbackClass = GetString(error, "codex_error_info");
        var message = GetString(error, "message");
        if (string.IsNullOrWhiteSpace(message))
            return (true, fallbackClass, null);

        try
        {
            using var inner = JsonDocument.Parse(message);
            var root = inner.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (true, fallbackClass, null);

            var status = GetInt(root, "status");
            var cls = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object
                ? GetString(e, "type")
                : null;
            return (true, cls ?? fallbackClass, status);
        }
        catch (JsonException)
        {
            return (true, fallbackClass, null);
        }
    }

    // ---------------------------------------------------------------------------------- plumbing

    /// <summary>
    /// One-way dialect latch (see the class remarks). Returns true when this row's dialect owns
    /// the kind and the row may be emitted.
    /// </summary>
    private static bool TakeDialect(ref Dialect latch, Dialect candidate)
    {
        if (latch == Dialect.Unknown)
            latch = candidate;
        return latch == candidate;
    }

    private static TranscriptPart Part(
        string kind, string? uuid, DateTimeOffset? ts, string role, string? text, string? ApiCallId = null) =>
        new(kind, uuid, null, ts, role, text, null, null, null, null, null, ApiCallId);

    /// <summary>
    /// A stable per-row key for the server's (Uuid, Kind) dedup. The interactive TUI stamps an
    /// <c>ordinal</c> on every row (measured 0.147.0; <c>codex exec</c> and the Desktop app do
    /// not), so the row's own position in the file stands in when it is absent — both are stable
    /// under a re-tail from offset 0, which is the only property the dedup needs.
    /// </summary>
    private string? SynthesizeUuid(JsonElement root)
    {
        var ordinal = GetInt(root, "ordinal") ?? _lineIndex;
        return Fit(_rolloutSessionId is { Length: > 0 } sid ? $"{sid}#{ordinal}" : $"#{ordinal}");
    }

    private static string? Fit(string? s) =>
        s is null or { Length: 0 } ? null : s.Length <= MaxUuidChars ? s : s[..MaxUuidChars];

    private static string? JoinStrings(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var array) || array.ValueKind != JsonValueKind.Array)
            return null;

        var sb = new StringBuilder();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(s);
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>Every rollout row carries a top-level ISO-8601 <c>timestamp</c> (measured).</summary>
    internal static DateTimeOffset? GetTimestamp(JsonElement root) =>
        root.TryGetProperty("timestamp", out var ts)
        && ts.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(
            ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? GetInt(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out var i)
            ? i
            : null;
}
