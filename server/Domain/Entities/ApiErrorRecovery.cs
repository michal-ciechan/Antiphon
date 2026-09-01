using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Durable retry schedule for one API-error stub that killed a turn (CARD-0072 S5a).
/// Keyed on <c>(AgentSessionId, StubSequence)</c> of the stub's <c>TurnEnd</c> row — not the
/// transcript uuid, which is nullable and shared by the AssistantText + TurnEnd pair that one
/// JSONL line becomes.
/// </summary>
public class ApiErrorRecovery
{
    public Guid Id { get; set; }
    public Guid AgentSessionId { get; set; }

    /// <summary>Sequence of the stub's TurnEnd row — the turn's end is what is being retried.</summary>
    public long StubSequence { get; set; }

    /// <summary>Forensics only; never the dedup key.</summary>
    public string? StubUuid { get; set; }

    public ApiErrorClassification Classification { get; set; }
    public string? ApiErrorClass { get; set; }
    public int? ApiErrorStatus { get; set; }

    public DateTime DetectedAt { get; set; }
    public int AttemptCount { get; set; }

    /// <summary>Null means parked or resolved — the fire pass will not pick this row up.</summary>
    public DateTime? NextAttemptAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedReason { get; set; }
    public DateTime? LastEnqueuedAt { get; set; }

    public AgentSession AgentSession { get; set; } = null!;
}

/// <summary>Values written to <see cref="ApiErrorRecovery.ResolvedReason"/>.</summary>
public static class ApiErrorRecoveryReasons
{
    /// <summary>A UserPrompt landed after the stub — someone already continued the conversation.</summary>
    public const string Superseded = "Superseded";

    /// <summary>A newer stub exists for this session; the ladder tracks the latest death.</summary>
    public const string Replaced = "Replaced";

    public const string NeedsHuman = "NeedsHuman";
    public const string UnknownExhausted = "UnknownExhausted";
    public const string WallParked = "WallParked";

    /// <summary>
    /// CARD-0022: a per-model cap (or an unparseable Wall with a resolved alias) paused that
    /// model with <c>DisabledUntil = null</c>. No WallPrompt. The open task Fails via CARD-0071.
    /// </summary>
    public const string WallModelPaused = "WallModelPaused";
}
