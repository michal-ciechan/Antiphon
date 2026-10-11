using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public sealed class CleanResult
{
    public List<string> Deleted { get; init; } = [];
    public bool KeptBecauseRed { get; init; }
}

public static class OutputCleanup
{
    // PC-4 drops "bin" from this list. Descending into bin/ would delete a nested owned name.
    private static readonly string[] PruneNames = ["bin", "obj", "workspace", ".git", "node_modules", ".antiphon"];

    // CARD-1199: a build id is a directory name only when it is an isolated bin-<name> output.
    private static readonly Regex IsolatedOutputId = new(@"^bin-[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant);

    public static bool IsPathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..")
            return false;
        if (value.Contains("..", StringComparison.Ordinal))
            return false;
        if (value.IndexOfAny(['/', '\\', ':']) >= 0)
            return false;
        return !Path.IsPathRooted(value);
    }

    public static bool IsIsolatedOutputId(string value) =>
        IsPathSegment(value) && IsolatedOutputId.IsMatch(value);

    public static bool IsStrictlyInside(string root, string candidate)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static CleanResult CleanOwnedOutputs(string repoRoot, IReadOnlyCollection<string> ownedIds, int exitCode, bool cleanOnRed, bool dryRun,
        Action? beforeDelete = null, IReadOnlyCollection<string>? createdPaths = null)
    {
        if (exitCode != 0 && !cleanOnRed)
            return new CleanResult { KeptBecauseRed = true };

        var owned = new HashSet<string>(ownedIds.Where(IsIsolatedOutputId), StringComparer.Ordinal);
        var deleted = new List<string>();
        var root = Path.GetFullPath(repoRoot);
        if (createdPaths is not null)
        {
            foreach (var path in createdPaths)
                TryDelete(root, path, owned, dryRun, deleted, beforeDelete);
            return new CleanResult { Deleted = deleted };
        }

        DeleteMatches(root, root, owned, dryRun, deleted, beforeDelete);
        return new CleanResult { Deleted = deleted };
    }

    public static List<string> MatchingIsolatedDirectories(string repoRoot, string buildId)
    {
        var found = new List<string>();
        if (!IsIsolatedOutputId(buildId) || string.IsNullOrWhiteSpace(repoRoot))
            return found;
        var root = Path.GetFullPath(repoRoot);
        if (!Directory.Exists(root))
            return found;
        Collect(root, root, buildId, found);
        return found;
    }

    public static void RecordCreatedOutputs(RunState state, string repoRoot, string buildId, IReadOnlyCollection<string> before)
    {
        state.CreatedOutputPaths ??= [];
        if (!IsIsolatedOutputId(buildId))
            return;
        var seen = new HashSet<string>(before.Select(Path.GetFullPath), StringComparer.Ordinal);
        foreach (var path in MatchingIsolatedDirectories(repoRoot, buildId))
        {
            if (seen.Contains(path) || state.CreatedOutputPaths.Contains(path))
                continue;
            state.CreatedOutputPaths.Add(path);
        }
    }

    public static bool ContainsTrackedFiles(string repoRoot, string candidate)
    {
        try
        {
            var root = Path.GetFullPath(repoRoot);
            var full = Path.GetFullPath(candidate);
            if (!Directory.Exists(root))
                return true;
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.Environment.Remove("GIT_DIR");
            start.Environment.Remove("GIT_WORK_TREE");
            start.Environment.Remove("GIT_INDEX_FILE");
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(root);
            start.ArgumentList.Add("ls-files");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(full);
            using var process = Process.Start(start);
            if (process is null)
                return true;
            var errorTask = Task.Run(() => process.StandardError.ReadToEnd());
            var stdout = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(15_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return true;
            }

            var stderr = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode == 0)
                return !string.IsNullOrWhiteSpace(stdout);
            return !stderr.Contains("not a git repository", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception
                                   or ArgumentException or NotSupportedException)
        {
            return true;
        }
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
            var ownership = RunOwnershipStore.Read(dir);
            if (ownership is null || !ContainedCleanup.SafeTree(dir, 10000))
                continue;
            var observed = ownership.Phase == "preparing"
                ? new ProcessIdentityProbe().Observe(ownership.Starter)
                : new ToolCopyCleanup().ObserveExecutor(dir);
            if (observed.Verdict != ProcessVerdict.Dead)
                continue;
            deleted.Add(dir);
            if (!dryRun)
                Directory.Delete(dir, recursive: true);
        }

        return deleted;
    }

    public static string DeletedLine(bool dryRun, string path) =>
        (dryRun ? "would delete " : "deleted ") + path;

    private static void TryDelete(string repoRoot, string candidate, HashSet<string> owned, bool dryRun, List<string> deleted, Action? beforeDelete)
    {
        string full;
        try { full = Path.GetFullPath(candidate); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!owned.Contains(name) || !Directory.Exists(full) || !CanDelete(repoRoot, full))
            return;
        beforeDelete?.Invoke();
        if (!CanDelete(repoRoot, full))
            return;
        deleted.Add(full);
        if (!dryRun)
            Directory.Delete(full, recursive: true);
    }

    private static bool CanDelete(string repoRoot, string candidate)
    {
        if (!IsStrictlyInside(repoRoot, candidate))
            return false;
        var name = Path.GetFileName(Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!IsIsolatedOutputId(name))
            return false;
        if (!ContainedCleanup.SafeAncestors(candidate) || !ContainedCleanup.SafeTree(candidate, 10000))
            return false;
        return !ContainsTrackedFiles(repoRoot, candidate);
    }

    private static void Collect(string repoRoot, string directory, string buildId, List<string> found)
    {
        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(directory); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (string.Equals(name, buildId, StringComparison.Ordinal))
            {
                if (IsStrictlyInside(repoRoot, child))
                    found.Add(Path.GetFullPath(child));
                continue;
            }

            if (PruneNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (name.StartsWith("bin-", StringComparison.Ordinal))
                continue;
            Collect(repoRoot, child, buildId, found);
        }
    }

    private static void DeleteMatches(string repoRoot, string directory, HashSet<string> owned, bool dryRun, List<string> deleted, Action? beforeDelete)
    {
        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(directory); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (owned.Contains(name))
            {
                if (CanDelete(repoRoot, child))
                {
                    beforeDelete?.Invoke();
                    if (!CanDelete(repoRoot, child))
                        continue;
                    deleted.Add(child);
                    if (!dryRun)
                        Directory.Delete(child, recursive: true);
                }

                continue;
            }

            if (PruneNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (name.StartsWith("bin-", StringComparison.Ordinal) && !owned.Contains(name))
                continue;
            beforeDelete?.Invoke();
            DeleteMatches(repoRoot, child, owned, dryRun, deleted, beforeDelete);
        }
    }
}
