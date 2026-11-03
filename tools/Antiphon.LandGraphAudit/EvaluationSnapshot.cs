using Microsoft.Build.Evaluation;

namespace Antiphon.LandGraphAudit;

/// <summary>D-6(b): compare evaluated properties, every item type and duplicate imports.</summary>
internal sealed class EvaluationSnapshot(string repo, string sourceRepo)
{
    private readonly SortedDictionary<string, string[]> _projects = new(StringComparer.Ordinal);

    // The unnormalised FullPath behind each item row, so a comparison can ask what the file is.
    private readonly Dictionary<string, string> _itemPaths = new(StringComparer.Ordinal);

    private string Normalize(string value)
    {
        // Repository relocation is the only normalization. Do not discard arbitrary properties,
        // item types, environment properties, or values changed by documentation.
        foreach (var root in new[] { repo, sourceRepo }.Distinct().OrderByDescending(p => p.Length))
        {
            value = value.Replace(root, "{repo}", StringComparison.Ordinal);
            value = value.Replace(root[Path.GetPathRoot(root)!.Length..], "{repo-no-root}", StringComparison.Ordinal);
        }
        return value;
    }

    public void Add(Project project, Dictionary<string, string> globals)
    {
        var key = Normalize(project.FullPath + "\0" + string.Join('\0', globals.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key + "=" + p.Value)));
        var items = project.AllEvaluatedItems.Select(i => (Row: "item\0" + i.ItemType + "\0" + Normalize(i.GetMetadataValue("FullPath")),
            Path: i.GetMetadataValue("FullPath"))).ToArray();
        foreach (var (row, path) in items) _itemPaths.TryAdd(row, path);
        var rows = project.AllEvaluatedProperties.Select(p => "property\0" + p.Name + "\0" + Normalize(p.EvaluatedValue))
            .Concat(items.Select(i => i.Row))
            .Concat(project.ImportsIncludingDuplicates.Select(i => "import\0" + Normalize(i.ImportedProject.FullPath)))
            .Order(StringComparer.Ordinal).ToArray();
        _projects.Add(key, rows);
    }

    /// <param name="ignoredFile">
    /// The canary's <see cref="CanaryRepository.IsIgnoredRepositoryFile"/>. The only tolerated row is
    /// an item row of this (repository) pass that the canary pass lacks entirely and whose FullPath is
    /// a git-ignored repository file: such a file is present only because a local run left it in a
    /// default glob, and the clean canary holds none. Every other row is compared: properties,
    /// imports, multiplicity, rows of copied or changed files, rows the canary pass adds, and rows
    /// gated on an ignored file (the canary pass evaluates the clean state for those).
    /// </param>
    public string? Difference(EvaluationSnapshot other, Func<string, bool> ignoredFile)
    {
        if (!_projects.Keys.SequenceEqual(other._projects.Keys)) return "evaluated project/global-property closure changed";
        foreach (var (project, all) in _projects)
        {
            var theirs = other._projects[project];
            if (all.SequenceEqual(theirs)) continue;
            var present = theirs.ToHashSet(StringComparer.Ordinal);
            var rows = all.Where(row => present.Contains(row)
                || !(_itemPaths.TryGetValue(row, out var path) && ignoredFile(path))).ToArray();
            if (rows.SequenceEqual(theirs)) continue;
            var changed = rows.Except(theirs).Concat(theirs.Except(rows)).FirstOrDefault();
            var label = changed is null ? "multiplicity" : string.Join(' ', changed.Split('\0').Take(2));
            return $"{project.Split('\0')[0]} {label} changed";
        }
        return null;
    }
}
