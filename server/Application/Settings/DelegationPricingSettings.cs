using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Published list prices per tier, per million tokens — bound from <c>Delegation:Pricing</c> so a
/// price change is a config edit, not a deploy. The defaults below are the shipped snapshot; every
/// field can be overridden, and a missing config section leaves them exactly as they are.
///
/// The four token counters are priced SEPARATELY because they cost very different amounts. Claude
/// Code re-reads its whole cached prefix on every turn, so in any agentic session the cache-READ
/// term dominates and grows with turn count — pricing it at the full input rate (which is what
/// CARD-0023 found) overstates a run by roughly an order of magnitude and makes the per-root
/// ceiling fire on spend that never happened.
/// </summary>
public sealed class DelegationPricingSettings
{
    /// <summary>A cache read costs ~0.1x base input.</summary>
    public decimal CacheReadMultiplier { get; set; } = 0.10m;

    /// <summary>
    /// A cache WRITE costs 1.25x base input at the 5-minute TTL, 2x at the 1-hour TTL. The
    /// transcript records one <c>cache_creation_input_tokens</c> figure and does NOT say which TTL
    /// produced it, so this assumes the 5-minute default Claude Code uses. Raise it to 2.0 if a
    /// deployment moves to 1-hour caching — cache writes are a small share of the total either way.
    /// </summary>
    public decimal CacheWriteMultiplier { get; set; } = 1.25m;

    /// <summary>
    /// Keyed by <see cref="AgentModelLevel"/> name. A tier missing here falls back to High, then to
    /// the built-in High default — a typo in config must never silently price work at zero.
    /// </summary>
    public Dictionary<string, ModelRateSettings> Rates { get; set; } = DefaultRates();

    /// <summary>
    /// The shipped snapshot, as of 2026-08-10. Frontier→fable, High→opus, Medium→sonnet, Low→haiku
    /// (see <see cref="AgentModelLevel"/>).
    /// </summary>
    public static Dictionary<string, ModelRateSettings> DefaultRates() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Claude Fable 5
            [nameof(AgentModelLevel.Frontier)] = new() { InputPerMillion = 10m, OutputPerMillion = 50m },
            // Claude Opus 5
            [nameof(AgentModelLevel.High)] = new() { InputPerMillion = 5m, OutputPerMillion = 25m },
            // Claude Sonnet 5 — list $3/$15, with an introductory $2/$10 running through 2026-08-31.
            [nameof(AgentModelLevel.Medium)] = new()
            {
                InputPerMillion = 3m,
                OutputPerMillion = 15m,
                PromoInputPerMillion = 2m,
                PromoOutputPerMillion = 10m,
                PromoUntilUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            // Claude Haiku 4.5
            [nameof(AgentModelLevel.Low)] = new() { InputPerMillion = 1m, OutputPerMillion = 5m },
        };
}

/// <summary>
/// One tier's rates. The optional promotional pair covers introductory pricing that ends on a known
/// date, so the table doesn't need editing the morning the window closes.
/// </summary>
public sealed class ModelRateSettings
{
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }

    /// <summary>Overrides <c>Input x CacheReadMultiplier</c> when set. Not affected by the promo window.</summary>
    public decimal? CacheReadPerMillion { get; set; }

    /// <summary>Overrides <c>Input x CacheWriteMultiplier</c> when set. Not affected by the promo window.</summary>
    public decimal? CacheWritePerMillion { get; set; }

    public decimal? PromoInputPerMillion { get; set; }
    public decimal? PromoOutputPerMillion { get; set; }

    /// <summary>Null means "already running".</summary>
    public DateTime? PromoFromUtc { get; set; }

    /// <summary>EXCLUSIVE — list price resumes at this instant. Null means the promo never ends.</summary>
    public DateTime? PromoUntilUtc { get; set; }

    /// <summary>Whether the promotional pair applies at <paramref name="atUtc"/>.</summary>
    public bool PromoAppliesAt(DateTime atUtc) =>
        PromoInputPerMillion is not null
        && PromoOutputPerMillion is not null
        && (PromoFromUtc is not { } from || atUtc >= from)
        && (PromoUntilUtc is not { } until || atUtc < until);
}
