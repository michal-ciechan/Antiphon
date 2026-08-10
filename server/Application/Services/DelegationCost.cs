using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// What a delegated session's token counters cost, per tier. Claude Code does not report a price
/// per turn, and the whole point of the tier ladder is that cheap work stays cheap — so the board
/// and the per-root ceiling need SOME number.
///
/// The four counters are priced at four different rates (CARD-0023). Collapsing them and applying
/// the input rate to the total prices a cache READ — about a tenth of base input — as if it were
/// fresh input, and because Claude Code re-reads its cached prefix on every turn, that term
/// dominates and grows with turn count. The measured miss was ~13x on a 6-minute Sonnet task.
///
/// Rates live in <see cref="DelegationPricingSettings"/> (config section <c>Delegation:Pricing</c>),
/// not here, so a published price change or the end of an introductory window is a config edit.
/// </summary>
public static class DelegationCost
{
    /// <summary>
    /// Bumped whenever the COSTING MODEL changes (not when a rate does). Stamped on every task
    /// priced by this code; rows still carrying 0 were priced by the pre-CARD-0023 model and are
    /// surfaced as legacy estimates rather than silently trusted.
    /// </summary>
    public const int PricingVersion = 2;

    /// <summary>
    /// Dollar cost of one session's spend. <paramref name="atUtc"/> is when the work RAN — a task
    /// is priced by the rates in force then, so a backfill of old rows can't reprice history and
    /// an introductory window closes on its own date rather than on the day someone re-runs this.
    /// </summary>
    public static decimal Estimate(
        DelegationPricingSettings pricing, AgentModelLevel level, TokenSpend spend, DateTime atUtc)
    {
        var rates = RatesFor(pricing, level, atUtc);
        var cost =
            (spend.InputTokens / 1_000_000m * rates.InputPerMillion)
            + (spend.CacheReadTokens / 1_000_000m * rates.CacheReadPerMillion)
            + (spend.CacheCreationTokens / 1_000_000m * rates.CacheWritePerMillion)
            + (spend.OutputTokens / 1_000_000m * rates.OutputPerMillion);
        return Math.Round(cost, 6, MidpointRounding.AwayFromZero);
    }

    /// <summary>The four per-million rates in force for a tier at a moment in time.</summary>
    public static ModelRates RatesFor(DelegationPricingSettings pricing, AgentModelLevel level, DateTime atUtc)
    {
        var entry = Lookup(pricing, level);
        var promo = entry.PromoAppliesAt(atUtc);
        var input = promo ? entry.PromoInputPerMillion!.Value : entry.InputPerMillion;
        var output = promo ? entry.PromoOutputPerMillion!.Value : entry.OutputPerMillion;

        return new ModelRates(
            input,
            output,
            // Cache rates track the input rate — including through a promo window — unless the
            // config pins them explicitly.
            entry.CacheReadPerMillion ?? input * pricing.CacheReadMultiplier,
            entry.CacheWritePerMillion ?? input * pricing.CacheWriteMultiplier);
    }

    private static ModelRateSettings Lookup(DelegationPricingSettings pricing, AgentModelLevel level)
    {
        if (pricing.Rates.TryGetValue(level.ToString(), out var entry))
            return entry;
        if (pricing.Rates.TryGetValue(nameof(AgentModelLevel.High), out var high))
            return high;
        // Config emptied or mistyped: price at the shipped High rate rather than at zero, which
        // would make the per-root ceiling unreachable.
        return DelegationPricingSettings.DefaultRates()[nameof(AgentModelLevel.High)];
    }
}

/// <summary>
/// One session's token usage, kept SEPARATE per counter because each is priced differently:
/// uncached input 1.0x, cache read ~0.1x, cache write 1.25x (5m TTL) or 2x (1h), output its own rate.
/// </summary>
public readonly record struct TokenSpend(
    long InputTokens,
    long CacheReadTokens,
    long CacheCreationTokens,
    long OutputTokens)
{
    public static readonly TokenSpend Zero = new(0, 0, 0, 0);

    /// <summary>Everything the model read this session — what a human means by "tokens in".</summary>
    public long TotalInputTokens => InputTokens + CacheReadTokens + CacheCreationTokens;
}

public readonly record struct ModelRates(
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal CacheReadPerMillion,
    decimal CacheWritePerMillion);
