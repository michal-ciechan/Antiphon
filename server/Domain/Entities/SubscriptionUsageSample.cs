using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// Append-only point reading of a provider subscription's remaining quota (CARD-0143).
/// Keyed by subscription, not by agent or session: two agents on one TUI profile share one
/// weekly limit, and a session's death must not take the quota fact with it.
/// </summary>
public class SubscriptionUsageSample
{
    public Guid Id { get; set; }

    public AgentKind Provider { get; set; }

    /// <summary>
    /// <c>Agent.TuiProfileId</c> formatted as "D", or the kind name when the agent has no profile
    /// (the pre-CARD-0114 one-account-per-kind assumption, stated rather than assumed silently).
    /// </summary>
    public string SubscriptionKey { get; set; } = string.Empty;

    public string? PlanLabel { get; set; }

    /// <summary>Normalised to remaining, 0–100. Null when <see cref="ParseStatus"/> is Unparsed.</summary>
    public double? RemainingPercent { get; set; }

    /// <summary>Inferred UTC reset. Null when absent or the raw string was unparseable.</summary>
    public DateTime? ResetsAt { get; set; }

    /// <summary>The evidence behind <see cref="ResetsAt"/> — neither source carries year or timezone.</summary>
    public string? ResetsAtRaw { get; set; }

    public DateTime ObservedAt { get; set; }

    public Guid? AgentId { get; set; }

    public Guid AgentSessionId { get; set; }

    /// <summary>Literally what was typed — the durable proof that Codex only ever saw <c>/status</c>.</summary>
    public string SourceCommand { get; set; } = string.Empty;

    public SubscriptionUsageParseStatus ParseStatus { get; set; }

    /// <summary>ANSI-stripped matched region, capped at 500 chars.</summary>
    public string? RawExcerpt { get; set; }
}
