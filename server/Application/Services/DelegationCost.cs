using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Approximate dollar cost of a delegated task from its token counts and tier.
///
/// Claude Code does not report a price per turn, and the whole point of the tier ladder is that
/// cheap work stays cheap — so the board and the per-root ceiling need SOME number. These are
/// order-of-magnitude published list rates per million tokens, deliberately kept as one small
/// table: the ceiling exists to stop a runaway tree, and a runaway is off by 100x, not 20%.
/// </summary>
public static class DelegationCost
{
    private static readonly Dictionary<AgentModelLevel, (decimal InPerMillion, decimal OutPerMillion)> Rates = new()
    {
        [AgentModelLevel.Frontier] = (15m, 75m),
        [AgentModelLevel.High] = (15m, 75m),
        [AgentModelLevel.Medium] = (3m, 15m),
        [AgentModelLevel.Low] = (0.80m, 4m),
    };

    public static decimal Estimate(AgentModelLevel level, long tokensIn, long tokensOut)
    {
        var (inRate, outRate) = Rates.TryGetValue(level, out var rate) ? rate : Rates[AgentModelLevel.High];
        var cost = (tokensIn / 1_000_000m * inRate) + (tokensOut / 1_000_000m * outRate);
        return Math.Round(cost, 6, MidpointRounding.AwayFromZero);
    }
}
