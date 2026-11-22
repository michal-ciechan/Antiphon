using Antiphon.Server.Application.Review.Checks;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Review;

/// <summary>One reviewer group: its checks, whether it is a bypass group, its tier and its suffix size.</summary>
public sealed record ReviewGroup(string Key, IReadOnlyList<string> CheckIds, bool Bypass, string Tier, int Tokens);

/// <summary>
/// Grouping (design 4.5): selected checks are bin-packed into reviewer groups by category affinity
/// and the group-suffix token budget. A check in a bypass category always goes into that category's
/// own group. A group's tier comes from its most severe check (blast radius raises it later). Pure.
/// </summary>
public static class ReviewGrouping
{
    public const string DeepTier = "deep";
    public const string StandardTier = "standard";

    public static IReadOnlyList<ReviewGroup> Group(IReadOnlyList<ReviewCheck> checks, IReadOnlyList<string> bypassCategories, int tokenBudget)
    {
        var budget = Math.Max(1, tokenBudget);
        var groups = new List<ReviewGroup>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string Unique(string key)
        {
            var candidate = key;
            for (var n = 2; !keys.Add(candidate); n++)
                candidate = $"{key}-{n}";
            return candidate;
        }

        void Emit(string key, List<ReviewCheck> members, bool bypass)
        {
            if (members.Count == 0)
                return;
            var tier = members.Any(c => c.Severity >= ReviewSeverity.Major) ? DeepTier : StandardTier;
            groups.Add(new ReviewGroup(Unique(key), members.Select(c => c.Id).ToList(), bypass, tier, members.Sum(Tokens)));
        }

        var bypassOf = checks.ToDictionary(c => c.Id,
            c => bypassCategories.FirstOrDefault(b => c.Flags.Contains(b, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

        foreach (var category in bypassCategories)
        {
            var members = checks.Where(c => string.Equals(bypassOf[c.Id], category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            foreach (var part in Split(members, budget))
                Emit(category, part, bypass: true);
        }

        var bin = new List<ReviewCheck>();
        var binCategories = new List<string>();
        var binTokens = 0;
        foreach (var category in checks.Where(c => bypassOf[c.Id] is null).GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var members = category.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            var tokens = members.Sum(Tokens);
            if (binTokens + tokens <= budget)
            {
                bin.AddRange(members);
                binCategories.Add(category.Key);
                binTokens += tokens;
                continue;
            }

            Emit(string.Join('+', binCategories), bin, bypass: false);
            bin = [];
            binCategories = [];
            binTokens = 0;
            if (tokens <= budget)
            {
                bin.AddRange(members);
                binCategories.Add(category.Key);
                binTokens = tokens;
                continue;
            }

            foreach (var part in Split(members, budget))
                Emit(category.Key, part, bypass: false);
        }

        Emit(string.Join('+', binCategories), bin, bypass: false);
        return groups;
    }

    /// <summary>A check's share of a group suffix: its title and every <c>@model</c> section but the selector, at four characters a token.</summary>
    public static int Tokens(ReviewCheck check) =>
        Math.Max(1, (check.Title.Length + check.ModelSections.Where(s => s.Name != "Selector").Sum(s => s.Name.Length + s.Body.Length)) / 4);

    private static IEnumerable<List<ReviewCheck>> Split(List<ReviewCheck> members, int budget)
    {
        var part = new List<ReviewCheck>();
        var tokens = 0;
        foreach (var check in members)
        {
            var size = Tokens(check);
            if (part.Count > 0 && tokens + size > budget)
            {
                yield return part;
                part = [];
                tokens = 0;
            }

            part.Add(check);
            tokens += size;
        }

        if (part.Count > 0)
            yield return part;
    }
}
