namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// One normalized entry from an agent's Claude Code JSONL session transcript (a user prompt,
/// an assistant text/thinking block, a tool call/result, a turn title, or a turn-end marker).
/// The structured, lossless counterpart to the verbatim PTY <c>.ansi.log</c>.
/// <see cref="Sequence"/> is monotonic per session in transcript file order; (AgentSessionId, Sequence)
/// is unique, which makes ingestion idempotent across re-tails and stream reconnects.
/// </summary>
public class TranscriptEntry
{
    public Guid Id { get; set; }
    public Guid AgentSessionId { get; set; }

    /// <summary>Per-session monotonic ordering key (transcript file order).</summary>
    public long Sequence { get; set; }

    /// <summary>One of <see cref="SessionRunner.Contracts.TranscriptKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Source JSONL record uuid (for threading; may be null for synthetic records).</summary>
    public string? Uuid { get; set; }
    public string? ParentUuid { get; set; }

    /// <summary>The record's own timestamp (UTC), when present.</summary>
    public DateTime? Timestamp { get; set; }

    public string? Role { get; set; }

    /// <summary>Prompt / assistant text / thinking / tool-result text / turn title, depending on Kind.</summary>
    public string? Text { get; set; }

    public string? ToolName { get; set; }

    /// <summary>Raw JSON of a tool call's input (Kind == ToolCall), truncated for very large inputs.</summary>
    public string? ToolInput { get; set; }

    /// <summary>Correlates a ToolResult back to its ToolCall.</summary>
    public string? ToolUseId { get; set; }
    public bool? ToolIsError { get; set; }

    /// <summary>stop_reason for Kind == TurnEnd (end_turn / stop_sequence / max_tokens).</summary>
    public string? StopReason { get; set; }

    /// <summary>
    /// The Anthropic message id of the API call this entry belongs to (assistant records only).
    /// All entries of one API call share the id and repeat IDENTICAL usage numbers — group by
    /// ApiCallId and count usage once; summing per entry overcounts.
    /// </summary>
    public string? ApiCallId { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheReadTokens { get; set; }
    public int? CacheCreationTokens { get; set; }

    /// <summary>
    /// The raw <c>isApiErrorMessage</c> flag of an API-error stub — the synthetic assistant record
    /// Claude Code writes when a turn is killed by the API itself (CARD-0072). Stamped on the
    /// stub's AssistantText and TurnEnd rows; null on rows persisted before the fields were carried
    /// (deliberately no backfill) and on records that never carried the flag. Detection is
    /// <c>TranscriptKinds.IsApiErrorStub(Kind, IsApiError)</c> — structural, never text-matched.
    /// </summary>
    public bool? IsApiError { get; set; }

    /// <summary>The stub's raw top-level <c>error</c> class (rate_limit / server_error / authentication_failed / model_not_found).</summary>
    public string? ApiErrorClass { get; set; }

    /// <summary>The stub's <c>apiErrorStatus</c> HTTP status, when present (429, 529, 404 — absent on auth/connection-drop).</summary>
    public int? ApiErrorStatus { get; set; }

    /// <summary>
    /// <c>message.model</c> from the assistant record this entry was parsed from (CARD-0082).
    /// Null on rows persisted before the column existed (deliberately no backfill), on
    /// non-assistant kinds, and on API-error stubs (whose raw model is <c>&lt;synthetic&gt;</c>).
    /// The live model id a mid-session <c>/model</c> switch actually ran — not launch intent.
    /// </summary>
    public string? Model { get; set; }

    public DateTime CreatedAt { get; set; }

    public AgentSession AgentSession { get; set; } = null!;
}
