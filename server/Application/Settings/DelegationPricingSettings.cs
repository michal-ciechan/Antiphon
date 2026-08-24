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
    /// Per-KIND rate overlay, keyed by <see cref="AgentKind"/> name and then by
    /// <see cref="AgentModelLevel"/> name. Consulted BEFORE <see cref="Rates"/>, which is a Claude
    /// price ladder wearing a generic name: lookup runs (kind, level) → (kind, High) →
    /// <see cref="Rates"/>. A kind with no entry here — every kind except Grok today, including
    /// <see cref="AgentKind.ClaudeCode"/> itself — prices exactly as it did before this overlay
    /// existed, so no stored row and no existing caller moves by a cent (CARD-0084 S5).
    ///
    /// An overlay rather than a replacement because the counters are the same four everywhere;
    /// only the numbers differ. It is deliberately silent about who REPORTS the cost: Grok's
    /// <c>turn_completed</c> carries a self-reported dollar figure that nothing here reads — where
    /// provider-declared cost lives is CARD-0083's question, and this shape does not preclude it.
    /// </summary>
    public Dictionary<string, Dictionary<string, ModelRateSettings>> KindRates { get; set; }
        = DefaultKindRates();

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

    /// <summary>
    /// The shipped per-kind overlay. Grok only, from xAI's published model pricing
    /// (https://docs.x.ai/docs/models, retrieved 2026-08-18) — NOT from a model's memory of it,
    /// which is exactly how a rate table acquires a plausible wrong number.
    ///
    /// Tier mapping follows <see cref="Services.ModelLevelAliases.ForGrok"/>: every level launches
    /// <c>grok-4.6</c> (CARD-0169 — the operator's own instruction retired grok-4.5 from the
    /// ladder). All four levels therefore share identical rates; the per-level entries below stay
    /// separate only because <see cref="DefaultKindRates"/> is keyed by level, not because the
    /// price actually differs.
    ///
    /// Two documented caveats, both under-pricing rather than over-pricing (the safe direction for
    /// a ceiling that gates dispatch):
    ///
    /// - xAI bills a request whose prompt reaches 200k tokens at DOUBLE for every token in that
    ///   request. Our four counters are a per-task aggregate that carries no per-request prompt
    ///   size, so the sub-200k tier is the only one we can honestly apply. Long-context Grok work
    ///   therefore reads low; revisit if per-request sizes ever reach the rollup.
    /// - Grok's own <c>turn_completed</c> reports a dollar figure per turn. Reading it would settle
    ///   both caveats at once and is deliberately NOT done here — see <see cref="KindRates"/>.
    /// </summary>
    public static Dictionary<string, Dictionary<string, ModelRateSettings>> DefaultKindRates() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(AgentKind.Grok)] = new(StringComparer.OrdinalIgnoreCase)
            {
                // grok-4.6 — $2.00 in / $6.00 out / $0.50 cached input, per million (< 200k).
                // CARD-0169: every level now launches grok-4.6, so all four entries match.
                [nameof(AgentModelLevel.Frontier)] = GrokTextRates(cachedInputPerMillion: 0.50m),
                [nameof(AgentModelLevel.High)] = GrokTextRates(cachedInputPerMillion: 0.50m),
                [nameof(AgentModelLevel.Medium)] = GrokTextRates(cachedInputPerMillion: 0.50m),
                [nameof(AgentModelLevel.Low)] = GrokTextRates(cachedInputPerMillion: 0.50m),
            },
        };

    /// <summary>
    /// One Grok text model's four counters. Both cache rates are PINNED rather than left to
    /// <see cref="CacheReadMultiplier"/>/<see cref="CacheWriteMultiplier"/>, which are Anthropic's
    /// shape and wrong here on both counts: 0.10x of $2.00 would price cached input at $0.20 where
    /// xAI charges $0.30 (grok-4.5) and $0.50 (grok-4.6) — a SHALLOWER discount than Anthropic's —
    /// and xAI publishes no cache-WRITE price at all, caching being automatic, so a cache write is
    /// billed as ordinary input with no 1.25x TTL premium.
    /// </summary>
    private static ModelRateSettings GrokTextRates(decimal cachedInputPerMillion) => new()
    {
        InputPerMillion = 2m,
        OutputPerMillion = 6m,
        CacheReadPerMillion = cachedInputPerMillion,
        CacheWritePerMillion = 2m,
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
