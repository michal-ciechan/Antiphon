using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public static class ManifestValidator
{
    // CARD-0448: a trailing backslash or space must not pass. PC-1 widens this pattern.
    public const string OutputPathPattern = @"^bin-[A-Za-z0-9._-]+/$";

    private static readonly Regex OutputPathGuard = new(OutputPathPattern, RegexOptions.CultureInvariant);

    public static void Validate(CheckpointManifest manifest, string worktreeRoot)
    {
        if (manifest.SchemaVersion != 1)
            throw new ManifestValidationException("schemaVersion", "schemaVersion must be 1");

        var root = Path.GetFullPath(worktreeRoot);
        var results = Path.GetFullPath(Path.IsPathRooted(manifest.ResultsRoot)
            ? manifest.ResultsRoot
            : Path.Combine(root, manifest.ResultsRoot));
        if (!IsUnder(root, results))
            throw new ManifestValidationException("resultsRoot", $"resultsRoot '{manifest.ResultsRoot}' must be under the worktree");

        if (manifest.Builds.Count == 0 && manifest.Checkpoints.Any(c => !c.IsCommand))
            throw new ManifestValidationException("builds", "manifest has no builds for its filter rows");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var build in manifest.Builds)
        {
            if (string.IsNullOrWhiteSpace(build.Id))
                throw new ManifestValidationException("id", "build id is empty");
            if (!ids.Add(build.Id))
                throw new ManifestValidationException("id", $"duplicate build id '{build.Id}'");
            if (!OutputPathGuard.IsMatch(build.OutputPath ?? ""))
                throw new ManifestValidationException("outputPath",
                    $"outputPath '{build.OutputPath}' must be bin-<name>/ with a forward slash and no trailing space (CARD-0448)");
            if (string.IsNullOrWhiteSpace(build.Project))
                throw new ManifestValidationException("project", $"build '{build.Id}' has no project");
        }

        var cpIds = new HashSet<string>(StringComparer.Ordinal);
        var firstAfter = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in manifest.Checkpoints)
        {
            if (string.IsNullOrWhiteSpace(row.Id))
                throw new ManifestValidationException("id", "checkpoint id is empty");
            if (!cpIds.Add(row.Id))
                throw new ManifestValidationException("id", $"duplicate checkpoint id '{row.Id}'");
            if (row.After.Count == 0)
                throw new ManifestValidationException("after", $"{row.Id} has no after");
            if (row.EstimatedMinutes is int estimate && (estimate <= 0 || 3L * estimate > int.MaxValue))
                throw new ManifestValidationException("estimatedMinutes", $"{row.Id}: EstimatedMinutes must be positive with safe deadlines");
            if (row.EstimatedMinutesWindows is int windows && (windows <= 0 || 3L * windows > int.MaxValue))
                throw new ManifestValidationException("estimatedMinutesWindows", $"{row.Id}: EstimatedMinutesWindows must be positive with safe deadlines");

            var hasFilter = !string.IsNullOrWhiteSpace(row.Filter);
            var hasCommand = !string.IsNullOrWhiteSpace(row.Command);
            if (hasFilter == hasCommand)
                throw new ManifestValidationException("filter", $"{row.Id} must have exactly one of filter or command");
            if (hasCommand && row.MinExecuted is not null)
                throw new ManifestValidationException("minExecuted", $"{row.Id} is a command row and must not set minExecuted");

            if (hasFilter)
            {
                if (string.IsNullOrWhiteSpace(row.Build) || manifest.Builds.All(b => b.Id != row.Build))
                    throw new ManifestValidationException("build", $"{row.Id} references unknown build '{row.Build}'");
                var afterKey = string.Join(",", row.After);
                if (firstAfter.TryGetValue(row.Build!, out var existing) && !string.Equals(existing, afterKey, StringComparison.Ordinal))
                    throw new ManifestValidationException("after",
                        $"{row.Id} reuses build '{row.Build}' across a different after (CARD-0585 reuse rule)");
                firstAfter.TryAdd(row.Build!, afterKey);
            }
            ValidateEnvironment(row);
        }
    }

    public static void ValidateEnvironment(CheckpointSpec row)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in row.Environment ?? [])
        {
            if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)
                || name.IndexOfAny(['\0', '\n', '\r']) >= 0
                || value.IndexOfAny(['\0', '\n', '\r']) >= 0)
                throw new ManifestValidationException("environment", $"{row.Id}: Environment has invalid NAME=value entry '{name}'");
            if (!names.Add(name))
                throw new ManifestValidationException("environment", $"{row.Id}: Environment duplicates '{name}'");
        }
    }

    public static bool IsUnder(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
