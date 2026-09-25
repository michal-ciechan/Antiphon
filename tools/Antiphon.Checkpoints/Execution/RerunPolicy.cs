namespace Antiphon.Checkpoints;

public sealed class RerunDecision
{
    public List<string> Names { get; init; } = [];
    public string Filter { get; init; } = "";
}

public static class RerunPolicy
{
    public static RerunDecision Select(IReadOnlyList<string> failedNames, IReadOnlyList<string> knownFlaky)
    {
        var known = new HashSet<string>(knownFlaky.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);
        var names = failedNames.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
            return new RerunDecision();
        return new RerunDecision { Names = names, Filter = MethodFilter(names) };
    }

    public static string MethodFilter(IReadOnlyList<string> qualifiedNames)
    {
        var parts = new List<string>();
        foreach (var qualified in qualifiedNames)
        {
            var lastDot = qualified.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == qualified.Length - 1)
                continue;
            var method = qualified[(lastDot + 1)..];
            var left = qualified[..lastDot];
            var classDot = left.LastIndexOf('.');
            var className = classDot >= 0 ? left[(classDot + 1)..] : left;
            parts.Add($"/*/*/{className}/{method}");
        }

        return string.Join("|", parts);
    }
}
