namespace Antiphon.Checkpoints;

public static class AfterSelector
{
    public static IReadOnlyList<string> Expand(string token)
    {
        var text = token.Trim();
        if (text.Length == 0)
            return [];
        if (text.Equals("all", StringComparison.OrdinalIgnoreCase))
            return ["all"];

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<string>();
        foreach (var part in parts)
            result.AddRange(ExpandOne(part));
        return result;
    }

    public static bool IsSelected(IReadOnlyList<string> rowAfter, IReadOnlyList<string>? selection)
    {
        if (selection is null || selection.Count == 0 || selection.Any(s => s.Equals("all", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (rowAfter.Any(s => s.Equals("all", StringComparison.OrdinalIgnoreCase)))
            return true;
        var selected = new HashSet<string>(selection, StringComparer.OrdinalIgnoreCase);
        return rowAfter.Count > 0 && rowAfter.All(selected.Contains);
    }

    private static IEnumerable<string> ExpandOne(string part)
    {
        var range = part.Split('-', 2, StringSplitOptions.TrimEntries);
        if (range.Length == 2 && SliceNumber(range[0]) is int start && SliceNumber(range[1]) is int end && end >= start
            && range[0].StartsWith('S') && range[1].StartsWith('S'))
        {
            for (var n = start; n <= end; n++)
                yield return "S" + n;
            yield break;
        }

        yield return part;
    }

    private static int? SliceNumber(string token)
    {
        if (token.Length < 2 || (token[0] != 'S' && token[0] != 's'))
            return null;
        return int.TryParse(token[1..], out var n) ? n : null;
    }
}
