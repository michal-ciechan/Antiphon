using Microsoft.Build.Evaluation;

namespace Antiphon.LandGraphAudit;

/// <summary>D-6(b): compare evaluated properties, every item type and duplicate imports.</summary>
internal sealed class EvaluationSnapshot(string repo, string sourceRepo)
{
    private readonly SortedDictionary<string, string[]> _projects = new(StringComparer.Ordinal);

    private string Normalize(string value)
    {
        // Repository relocation is the only normalization. Do not discard arbitrary properties,
        // item types, environment properties, or values changed by documentation.
        foreach (var root in new[] { repo, sourceRepo }.Distinct().OrderByDescending(p => p.Length))
        {
            value = value.Replace(root, "{repo}", StringComparison.Ordinal);
            value = value.Replace(root.TrimStart(Path.DirectorySeparatorChar), "{repo-no-root}", StringComparison.Ordinal);
        }
        return value;
    }

    public void Add(Project project, Dictionary<string, string> globals)
    {
        var key = Normalize(project.FullPath + "\0" + string.Join('\0', globals.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key + "=" + p.Value)));
        var rows = project.AllEvaluatedProperties.Select(p => "property\0" + p.Name + "\0" + Normalize(p.EvaluatedValue))
            .Concat(project.AllEvaluatedItems.Select(i => "item\0" + i.ItemType + "\0" + Normalize(i.GetMetadataValue("FullPath"))))
            .Concat(project.ImportsIncludingDuplicates.Select(i => "import\0" + Normalize(i.ImportedProject.FullPath)))
            .Order(StringComparer.Ordinal).ToArray();
        _projects.Add(key, rows);
    }

    public string? Difference(EvaluationSnapshot other)
    {
        if (!_projects.Keys.SequenceEqual(other._projects.Keys)) return "evaluated project/global-property closure changed";
        foreach (var (project, rows) in _projects)
        {
            if (rows.SequenceEqual(other._projects[project])) continue;
            var changed = rows.Except(other._projects[project]).Concat(other._projects[project].Except(rows)).FirstOrDefault();
            var label = changed is null ? "multiplicity" : string.Join(' ', changed.Split('\0').Take(2));
            return $"{project.Split('\0')[0]} {label} changed";
        }
        return null;
    }
}
