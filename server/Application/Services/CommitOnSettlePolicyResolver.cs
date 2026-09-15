using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Resolved commit-on-settle behaviour after walking task &gt; project &gt; global.</summary>
public enum CommitOnSettleEffective
{
    Off = 0,
    Tier1 = 1,
    AgentOnly = 2,
}

/// <summary>
/// CARD-0527 D-3. Task value if set; else project; else global. Never skips both tiers,
/// Always forces tier 1, Agent skips tier 1 and goes to the Commit child.
/// </summary>
public static class CommitOnSettlePolicyResolver
{
    public static CommitOnSettleEffective Resolve(
        CommitOnSettlePolicy? task,
        bool? project,
        bool global) =>
        task switch
        {
            CommitOnSettlePolicy.Never => CommitOnSettleEffective.Off,
            CommitOnSettlePolicy.Always => CommitOnSettleEffective.Tier1,
            CommitOnSettlePolicy.Agent => CommitOnSettleEffective.AgentOnly,
            null => (project ?? global) ? CommitOnSettleEffective.Tier1 : CommitOnSettleEffective.Off,
            _ => (project ?? global) ? CommitOnSettleEffective.Tier1 : CommitOnSettleEffective.Off,
        };

    public static CommitOnSettlePolicy? ParseTaskValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse<CommitOnSettlePolicy>(value.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
            return parsed;
        throw new ValidationException(
            "CommitOnSettle",
            $"'{value}' is not a commitOnSettle value. Use Never, Always, or Agent.");
    }

    public static bool? ParseProjectValue(string? value, bool leaveUnchangedWhenNull, out bool leaveUnchanged)
    {
        leaveUnchanged = false;
        if (value is null)
        {
            leaveUnchanged = leaveUnchangedWhenNull;
            return null;
        }

        if (string.Equals(value, "On", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(value, "Inherit", StringComparison.OrdinalIgnoreCase))
            return null;

        throw new ValidationException(
            "CommitOnSettle",
            $"'{value}' is not a project commitOnSettle value. Use On, Off, or Inherit.");
    }

    public static string? FormatProjectValue(bool? stored) =>
        stored is null ? null : stored.Value ? "On" : "Off";

    public static bool MentionsDoNotCommit(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
            return false;
        ReadOnlySpan<string> needles =
        [
            "do not commit",
            "don't commit",
            "no commit",
            "without committing",
        ];
        foreach (var needle in needles)
        {
            if (goal.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
