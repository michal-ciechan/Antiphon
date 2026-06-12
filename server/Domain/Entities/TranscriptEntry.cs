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

    public DateTime CreatedAt { get; set; }

    public AgentSession AgentSession { get; set; } = null!;
}
