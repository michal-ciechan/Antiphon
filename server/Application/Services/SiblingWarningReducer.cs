namespace Antiphon.Server.Application.Services;

/// <summary>Reduces immutable observations using only proven commit containment.</summary>
internal static class SiblingWarningReducer
{
    internal sealed record Candidate(Guid TaskId, string Branch, DateTime CreatedAt, string? FullTipSha);
    internal sealed record Group(Candidate Representative, IReadOnlyList<Candidate> Members);

    internal static string? NormalizeTip(string? tip) =>
        tip is { Length: 40 or 64 } && tip.All(char.IsAsciiHexDigit) ? tip.ToLowerInvariant() : null;

    internal static IReadOnlyList<Group> Reduce(
        IEnumerable<Candidate> candidates, IEnumerable<(string Ancestor, string Descendant)> provenEdges)
    {
        var ordered = candidates.Select(c => c with { FullTipSha = NormalizeTip(c.FullTipSha) })
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Branch, StringComparer.Ordinal)
            .ThenBy(c => c.TaskId.ToString("N"), StringComparer.Ordinal).ToList();
        var classes = new List<List<Candidate>>();
        var tips = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in ordered)
        {
            if (candidate.FullTipSha is { } tip && tips.TryGetValue(tip, out var index))
                classes[index].Add(candidate);
            else
            {
                if (candidate.FullTipSha is { } known) tips.Add(known, classes.Count);
                classes.Add([candidate]);
            }
        }

        var reaches = new bool[classes.Count, classes.Count];
        foreach (var (ancestor, descendant) in provenEdges)
            if (NormalizeTip(ancestor) is { } a && NormalizeTip(descendant) is { } d
                && tips.TryGetValue(a, out var from) && tips.TryGetValue(d, out var to) && from != to)
                reaches[from, to] = true;
        // A failed direct probe does not erase a proven path through immutable commits.
        for (var via = 0; via < classes.Count; via++)
            for (var from = 0; from < classes.Count; from++)
                for (var to = 0; to < classes.Count; to++)
                    reaches[from, to] |= reaches[from, via] && reaches[via, to];

        var survivors = Enumerable.Range(0, classes.Count)
            .Where(from => !Enumerable.Range(0, classes.Count).Any(to => reaches[from, to])).ToList();
        var members = survivors.ToDictionary(i => i, i => new List<Candidate>(classes[i]));
        foreach (var covered in Enumerable.Range(0, classes.Count).Except(survivors))
        {
            var owner = survivors.First(i => reaches[covered, i]);
            members[owner].AddRange(classes[covered]);
        }
        return survivors.Select(i => new Group(classes[i][0], members[i])).ToList();
    }
}
