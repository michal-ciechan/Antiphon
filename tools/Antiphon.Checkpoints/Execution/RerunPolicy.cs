namespace Antiphon.Checkpoints;

public sealed class RerunDecision
{
    public List<string> Names { get; init; } = [];
    public List<string> Filters { get; init; } = [];
}

public static class RerunPolicy
{
    public static RerunDecision Select(IReadOnlyList<string> failedNames, IReadOnlyList<string> knownFlaky)
    {
        var known = new HashSet<string>(knownFlaky.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);
        var names = failedNames.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
            return new RerunDecision();
        return new RerunDecision { Names = names, Filters = MethodFilters(names) };
    }

    public static List<string> MethodFilters(IReadOnlyList<string> qualifiedNames)
    {
        var order = new List<string>();
        var methods = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var qualified in qualifiedNames)
        {
            var lastDot = qualified.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == qualified.Length - 1)
                continue;
            var method = qualified[(lastDot + 1)..];
            var left = qualified[..lastDot];
            var classDot = left.LastIndexOf('.');
            var className = classDot >= 0 ? left[(classDot + 1)..] : left;
            if (!methods.TryGetValue(className, out var list))
            {
                list = [];
                methods[className] = list;
                order.Add(className);
            }

            if (!list.Contains(method, StringComparer.Ordinal))
                list.Add(method);
        }

        var filters = new List<string>();
        foreach (var className in order)
        {
            var list = methods[className];
            filters.Add(list.Count == 1
                ? $"/*/*/{className}/{list[0]}"
                : $"/*/*/{className}/({string.Join("*)|(", list)}*)");
        }

        return filters;
    }
}
