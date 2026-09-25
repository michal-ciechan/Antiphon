namespace Antiphon.Checkpoints;

public sealed class CleanResult
{
    public List<string> Deleted { get; init; } = [];
    public bool KeptBecauseRed { get; init; }
}

public static class OutputCleanup
{
    // PC-4 drops "bin" from this list. Descending into bin/ would delete a nested owned name.
    private static readonly string[] PruneNames = ["bin", "obj", "workspace", ".git", "node_modules"];

    public static CleanResult CleanOwnedOutputs(string repoRoot, IReadOnlyCollection<string> ownedIds, int exitCode, bool cleanOnRed, bool dryRun)
    {
        if (exitCode != 0 && !cleanOnRed)
            return new CleanResult { KeptBecauseRed = true };

        var owned = new HashSet<string>(ownedIds, StringComparer.Ordinal);
        var deleted = new List<string>();
        DeleteMatches(Path.GetFullPath(repoRoot), owned, dryRun, deleted);
        return new CleanResult { Deleted = deleted };
    }

    public static List<string> RemoveOlderRuns(string resultsRoot, TimeSpan olderThan, bool dryRun)
    {
        var deleted = new List<string>();
        if (!Directory.Exists(resultsRoot))
            return deleted;
        var cutoff = DateTime.UtcNow - olderThan;
        foreach (var dir in Directory.EnumerateDirectories(resultsRoot))
        {
            var name = Path.GetFileName(dir);
            if (PruneNames.Contains(name) || name.StartsWith("bin", StringComparison.Ordinal))
                continue;
            if (Directory.GetLastWriteTimeUtc(dir) >= cutoff)
                continue;
            deleted.Add(dir);
            if (!dryRun)
                Directory.Delete(dir, recursive: true);
        }

        return deleted;
    }

    private static void DeleteMatches(string directory, HashSet<string> owned, bool dryRun, List<string> deleted)
    {
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(directory);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (owned.Contains(name))
            {
                deleted.Add(child);
                if (!dryRun)
                    Directory.Delete(child, recursive: true);
                continue;
            }

            if (PruneNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (name.StartsWith("bin-", StringComparison.Ordinal) && !owned.Contains(name))
                continue;
            DeleteMatches(child, owned, dryRun, deleted);
        }
    }
}
