using System.Globalization;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// One structured fragment parsed from a single Claude Code JSONL transcript line. A single line
/// can yield several parts (an assistant message has thinking + text + multiple tool_use blocks).
/// SessionId/Sequence are assigned by the tailer, not here.
/// </summary>
public readonly record struct TranscriptPart(
    string Kind,
    string? Uuid,
    string? ParentUuid,
    DateTimeOffset? Timestamp,
    string? Role,
    string? Text,
    string? ToolName,
    string? ToolInput,
    string? ToolUseId,
    bool? ToolIsError,
    string? StopReason,
    // API-call attribution (assistant records only): the Anthropic message id plus its usage
    // numbers. All JSONL lines of one API call share the id and repeat identical usage, so
    // consumers group by ApiCallId and count usage once.
    string? ApiCallId = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    int? CacheReadTokens = null,
    int? CacheCreationTokens = null,
    // API-error stub evidence (CARD-0072): the raw record's top-level error/isApiErrorMessage/
    // apiErrorStatus, stamped on the stub's AssistantText and TurnEnd parts (null everywhere else).
    bool? IsApiError = null,
    string? ApiErrorClass = null,
    int? ApiErrorStatus = null);

/// <summary>
/// Normalizes one line of a Claude Code session JSONL transcript into zero or more
/// <see cref="TranscriptPart"/>s (user prompts, assistant text/thinking, tool calls/results,
/// turn titles, turn-end markers). Lossy by design: pure-metadata records (mode, permission-mode,
/// attachments, file-history snapshots, the redundant last-prompt) are skipped — the verbatim record
/// still lives in the PTY .ansi.log and the .jsonl file itself.
/// </summary>
public static class TranscriptNormalizer
{
    private const int MaxToolInputChars = 10_000;
    private const int MaxToolResultChars = 4_000;
    private const string TruncationMarker = "…[truncated]";

    public static IReadOnlyList<TranscriptPart> Normalize(string jsonLine)
    {
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

            return GetString(root, "type") switch
            {
                "assistant" => FromAssistant(root),
                "user" => FromUser(root),
                "ai-title" => FromTitle(root),
                "system" => FromSystem(root),
                _ => [],
            };
        }
    }

    private static List<TranscriptPart> FromAssistant(JsonElement root)
    {
        var parts = new List<TranscriptPart>();
        if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
            return parts;

        var uuid = GetString(root, "uuid");
        var parent = GetString(root, "parentUuid");
        var ts = GetTimestamp(root);
        var role = GetString(msg, "role") ?? "assistant";
        var stopReason = GetString(msg, "stop_reason");
        var apiCallId = GetString(msg, "id");
        var (inTok, outTok, cacheRead, cacheCreate) = GetUsage(msg);
        // API-error stub fields (CARD-0072), TOP-LEVEL on the record, carried verbatim: a dead turn
        // is one synthetic assistant line with error ("rate_limit"/"server_error"/…),
        // isApiErrorMessage:true and (sometimes) a numeric apiErrorStatus. The benign
        // "No response requested." synthetic carries isApiErrorMessage:FALSE and no error — carrying
        // the raw values (not a computed verdict) keeps that distinction visible downstream.
        var isApiError = GetBool(root, "isApiErrorMessage");
        var apiErrorClass = GetString(root, "error");
        var apiErrorStatus = GetInt(root, "apiErrorStatus");

        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                switch (GetString(block, "type"))
                {
                    case "text":
                        var text = GetString(block, "text");
                        if (!string.IsNullOrWhiteSpace(text))
                            parts.Add(new TranscriptPart(
                                TranscriptKinds.AssistantText, uuid, parent, ts, role, text, null, null, null, null, null,
                                apiCallId, inTok, outTok, cacheRead, cacheCreate,
                                isApiError, apiErrorClass, apiErrorStatus));
                        break;
                    case "thinking":
                        var thinking = GetString(block, "thinking");
                        if (!string.IsNullOrWhiteSpace(thinking))
                            parts.Add(new TranscriptPart(TranscriptKinds.Thinking, uuid, parent, ts, role, thinking, null, null, null, null, null, apiCallId, inTok, outTok, cacheRead, cacheCreate));
                        break;
                    case "tool_use":
                        var input = block.TryGetProperty("input", out var inp)
                            ? Truncate(inp.GetRawText(), MaxToolInputChars)
                            : null;
                        parts.Add(new TranscriptPart(
                            TranscriptKinds.ToolCall, uuid, parent, ts, role,
                            null, GetString(block, "name"), input, GetString(block, "id"), null, null,
                            apiCallId, inTok, outTok, cacheRead, cacheCreate));
                        break;
                }
            }
        }

        // A finished turn: stop_reason present and not "tool_use" (tool_use means the turn continues
        // after the tool runs). end_turn / stop_sequence / max_tokens all mean the agent is idle.
        if (!string.IsNullOrEmpty(stopReason) && stopReason != "tool_use")
            parts.Add(new TranscriptPart(
                TranscriptKinds.TurnEnd, uuid, parent, ts, role, null, null, null, null, null, stopReason,
                apiCallId, inTok, outTok, cacheRead, cacheCreate,
                isApiError, apiErrorClass, apiErrorStatus));

        return parts;
    }

    private static List<TranscriptPart> FromUser(JsonElement root)
    {
        var parts = new List<TranscriptPart>();
        // Skip meta/system-injected user records (e.g. isMeta:true command output, caveats).
        if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True)
            return parts;
        if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
            return parts;

        var uuid = GetString(root, "uuid");
        var parent = GetString(root, "parentUuid");
        var ts = GetTimestamp(root);

        if (!msg.TryGetProperty("content", out var content))
            return parts;

        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                parts.Add(new TranscriptPart(TranscriptKinds.UserPrompt, uuid, parent, ts, "user", text, null, null, null, null, null));
            return parts;
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                switch (GetString(block, "type"))
                {
                    case "text":
                        var text = GetString(block, "text");
                        if (!string.IsNullOrWhiteSpace(text))
                            parts.Add(new TranscriptPart(TranscriptKinds.UserPrompt, uuid, parent, ts, "user", text, null, null, null, null, null));
                        break;
                    case "tool_result":
                        parts.Add(new TranscriptPart(
                            TranscriptKinds.ToolResult, uuid, parent, ts, "user",
                            ExtractToolResultText(block), null, null,
                            GetString(block, "tool_use_id"), GetBool(block, "is_error"), null));
                        break;
                }
            }
        }

        return parts;
    }

    // System records are pure metadata EXCEPT the compact boundary — the signal compaction
    // recovery keys on. Shape pinned by ClaudeCompactionCanaryTests (Fixtures/compact-boundary.jsonl):
    // type=system, subtype=compact_boundary, compactMetadata{trigger,preTokens,...}. Deliberately
    // NOT a turn end HERE (no StopReason, no TurnEnd part): compaction happens between turns, not
    // as one. The working/idle rules do treat a MANUAL boundary as a turn end (CARD-0041) — they
    // key on the "(manual)" trigger this text carries, which is why the trigger must stay in it.
    private static List<TranscriptPart> FromSystem(JsonElement root)
    {
        if (GetString(root, "subtype") != "compact_boundary")
            return [];

        var trigger = root.TryGetProperty("compactMetadata", out var meta)
            ? GetString(meta, "trigger")
            : null;
        return
        [
            new TranscriptPart(
                TranscriptKinds.CompactBoundary,
                GetString(root, "uuid"),
                GetString(root, "parentUuid"),
                GetTimestamp(root),
                null,
                trigger is null ? "Context compacted" : $"Context compacted ({trigger})",
                null, null, null, null, null),
        ];
    }

    private static List<TranscriptPart> FromTitle(JsonElement root)
    {
        var title = GetString(root, "aiTitle");
        return string.IsNullOrWhiteSpace(title)
            ? []
            : [new TranscriptPart(
                TranscriptKinds.TurnTitle, GetString(root, "uuid"), GetString(root, "parentUuid"),
                GetTimestamp(root), null, title, null, null, null, null, null)];
    }

    private static string? ExtractToolResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var c))
            return null;
        if (c.ValueKind == JsonValueKind.String)
            return Truncate(c.GetString(), MaxToolResultChars);
        if (c.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var b in c.EnumerateArray())
                if (GetString(b, "type") == "text")
                    sb.Append(GetString(b, "text"));
            return Truncate(sb.ToString(), MaxToolResultChars);
        }
        return null;
    }

    // message.usage on assistant records: {input_tokens, output_tokens, cache_read_input_tokens,
    // cache_creation_input_tokens, ...}. Repeated verbatim on every JSONL line of the same API call.
    private static (int? In, int? Out, int? CacheRead, int? CacheCreate) GetUsage(JsonElement msg)
    {
        if (msg.ValueKind != JsonValueKind.Object
            || !msg.TryGetProperty("usage", out var usage)
            || usage.ValueKind != JsonValueKind.Object)
            return (null, null, null, null);
        return (
            GetInt(usage, "input_tokens"),
            GetInt(usage, "output_tokens"),
            GetInt(usage, "cache_read_input_tokens"),
            GetInt(usage, "cache_creation_input_tokens"));
    }

    private static int? GetInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;

    private static string? GetString(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(prop, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool? GetBool(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static DateTimeOffset? GetTimestamp(JsonElement root) =>
        DateTimeOffset.TryParse(GetString(root, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;

    private static string? Truncate(string? s, int max) =>
        s is { Length: > 0 } && s.Length > max ? s[..max] + TruncationMarker : s;
}
