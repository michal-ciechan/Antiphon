using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public sealed class CleanResult
{
    public List<string> Deleted { get; init; } = [];
    public List<string> Refusals { get; init; } = [];
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
        Action? beforeDelete = null, IReadOnlyCollection<string>? createdPaths = null, string? gitExecutable = null,
        bool refuseUnrecorded = false)
    {
        if (exitCode != 0 && !cleanOnRed)
            return new CleanResult { KeptBecauseRed = true };

        var owned = new HashSet<string>(ownedIds.Where(IsIsolatedOutputId), StringComparer.Ordinal);
        var deleted = new List<string>();
        var refusals = new List<string>();
        var root = Path.GetFullPath(repoRoot);
        if (createdPaths is null)
        {
            refusals.Add("inventory-missing");
            return new CleanResult { Deleted = deleted, Refusals = refusals };
        }

        if (refuseUnrecorded)
        {
            foreach (var id in owned)
            {
                foreach (var match in MatchingIsolatedDirectories(root, id))
                {
                    if (!Listed(createdPaths, match))
                        refusals.Add("not-created-by-run");
                }
            }
        }

        foreach (var path in createdPaths)
        {
            string full;
            try { full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                refusals.Add("path-unsafe");
                continue;
            }

            var name = Path.GetFileName(full);
            var present = Directory.Exists(full) || File.Exists(full);
            if (!IsStrictlyInside(root, full))
            {
                if (present)
                    refusals.Add("outside-isolated-root");
                continue;
            }

            if (!IsIsolatedOutputId(name))
            {
                if (present)
                    refusals.Add("not-isolated-output");
                continue;
            }

            if (!owned.Contains(name) || !Directory.Exists(full))
                continue;

            var ledgerRefusal = SafeDelete.Refusal(full, new DeleteOwnership { IsolatedRoot = root, CreatedPaths = createdPaths, RequireInventory = true, RequireBinName = true, AllowOutsideRepository = false, GitExecutable = gitExecutable }) ?? OwnedOutputLedger.Check(full);
            if (ledgerRefusal is not null) { refusals.Add(ledgerRefusal); continue; }
            var result = SafeDelete.Apply(full, new DeleteOwnership
            {
                IsolatedRoot = root,
                CreatedPaths = createdPaths,
                RequireInventory = true,
                RequireBinName = true,
                DryRun = dryRun,
                AllowOutsideRepository = false,
                GitExecutable = gitExecutable,
            }, beforeDelete, () => OwnedOutputLedger.Check(full));
            if (result.Disposition == DeleteDisposition.Refused)
                refusals.Add(result.Reason ?? "refused");
            else if (result.Disposition is DeleteDisposition.Deleted or DeleteDisposition.DryRun)
                deleted.Add(result.Path);
        }

        return new CleanResult { Deleted = deleted, Refusals = refusals };
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

    public static CleanResult RemoveOlderRuns(string resultsRoot, TimeSpan olderThan, bool dryRun)
    {
        var deleted = new List<string>();
        var refusals = new List<string>();
        if (!Directory.Exists(resultsRoot))
            return new CleanResult { Deleted = deleted, Refusals = refusals };
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
            var refusal = SafeDelete.Refusal(dir, new DeleteOwnership { IsolatedRoot = resultsRoot, CreatedPaths = [dir], RequireBinName = false, AllowOutsideRepository = true }) ?? OwnedOutputLedger.Check(dir);
            if (refusal is not null) { refusals.Add(refusal); continue; }
            if (dryRun)
            {
                deleted.Add(dir);
                continue;
            }

            var removed = SafeDelete.Apply(dir, new DeleteOwnership { IsolatedRoot = resultsRoot, CreatedPaths = [dir], RequireBinName = false, AllowOutsideRepository = true }, inventoryCheck: () => OwnedOutputLedger.Check(dir));
            if (removed.Disposition == DeleteDisposition.Deleted && !Directory.Exists(dir) && !File.Exists(dir))
                deleted.Add(dir);
            else if (removed.Disposition == DeleteDisposition.Refused || Directory.Exists(dir) || File.Exists(dir))
                refusals.Add(string.IsNullOrWhiteSpace(removed.Reason) ? "still-present" : removed.Reason);
        }

        return new CleanResult { Deleted = deleted, Refusals = refusals };
    }

    public static string DeletedLine(bool dryRun, string path) =>
        (dryRun ? "would delete " : "deleted ") + path;

    private static bool Listed(IReadOnlyCollection<string> created, string full)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var candidate in created)
        {
            try
            {
                var other = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(other, trimmed, comparison))
                    return true;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        return false;
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
}
