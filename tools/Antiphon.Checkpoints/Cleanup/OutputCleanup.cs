using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

public sealed class CleanResult
{
    public List<string> Deleted { get; init; } = [];
    public List<string> Refusals { get; init; } = [];
    public bool KeptBecauseRed { get; init; }
}

public sealed class CheckpointOutputCleanup(CheckpointCleanupContext context)
{
    private IEnumerable<string> Directories(string path) => context.Io.Enumerate(path).Where(context.Io.DirectoryExists);

    // PC-4 drops "bin" from this list. Descending into bin/ would delete a nested owned name.
    private readonly string[] PruneNames = ["bin", "obj", "workspace", ".git", "node_modules", ".antiphon"];

    public bool IsPathSegment(string value) => CheckpointOutputNames.IsPathSegment(value);
    public bool IsIsolatedOutputId(string value) => CheckpointOutputNames.IsIsolatedOutputId(value);

    public bool IsStrictlyInside(string root, string candidate)
    {
        try
        {
            var comparison = context.PathComparison;
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public CleanResult CleanOwnedOutputs(string repoRoot, IReadOnlyCollection<string> ownedIds, int exitCode, bool cleanOnRed, bool dryRun,
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
            var present = context.Io.DirectoryExists(full) || context.Io.FileExists(full);
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

            if (!owned.Contains(name) || !context.Io.DirectoryExists(full))
                continue;

            var ledgerRefusal = new CheckpointDelete(context).Refusal(full, new DeleteOwnership { IsolatedRoot = root, CreatedPaths = createdPaths, RequireInventory = true, RequireBinName = true, AllowOutsideRepository = false, GitExecutable = gitExecutable }) ?? new CheckpointOutputLedger(context).Check(full);
            if (ledgerRefusal is not null) { Console.Error.WriteLine("left in place: " + full + "; remove it manually"); refusals.Add(ledgerRefusal); continue; }
            var result = new CheckpointDelete(context).Apply(full, new DeleteOwnership
            {
                IsolatedRoot = root,
                CreatedPaths = createdPaths,
                RequireInventory = true,
                RequireBinName = true,
                DryRun = dryRun,
                AllowOutsideRepository = false,
                GitExecutable = gitExecutable,
            }, beforeDelete, () => new CheckpointOutputLedger(context).Check(full));
            if (result.Disposition == DeleteDisposition.Refused)
                refusals.Add(result.Reason ?? "refused");
            else if (result.Disposition is DeleteDisposition.Deleted or DeleteDisposition.DryRun)
                deleted.Add(result.Path);
        }

        return new CleanResult { Deleted = deleted, Refusals = refusals };
    }

    public List<string> MatchingIsolatedDirectories(string repoRoot, string buildId)
    {
        var found = new List<string>();
        if (!IsIsolatedOutputId(buildId) || string.IsNullOrWhiteSpace(repoRoot))
            return found;
        var root = Path.GetFullPath(repoRoot);
        if (!context.Io.DirectoryExists(root))
            return found;
        Collect(root, root, buildId, found);
        return found;
    }

    public void RecordCreatedOutputs(RunState state, string repoRoot, string buildId, IReadOnlyCollection<string> before)
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

    public CleanResult RemoveOlderRuns(string resultsRoot, TimeSpan olderThan, bool dryRun)
    {
        var deleted = new List<string>();
        var refusals = new List<string>();
        if (!context.Io.DirectoryExists(resultsRoot))
            return new CleanResult { Deleted = deleted, Refusals = refusals };
        var cutoff = context.Time.GetUtcNow().UtcDateTime - olderThan;
        foreach (var dir in Directories(resultsRoot))
        {
            var name = Path.GetFileName(dir);
            if (PruneNames.Contains(name) || name.StartsWith("bin", StringComparison.Ordinal))
                continue;
            if (context.Io.LastWriteTimeUtc(dir) >= cutoff)
                continue;
            var ownership = RunOwnershipStore.Read(dir, context);
            if (ownership is null || !ContainedCleanup.SafeTree(dir, 10000, context))
                continue;
            var observed = ownership.Phase == "preparing"
                ? new ProcessIdentityProbe(context).Observe(ownership.Starter)
                : new ToolCopyCleanup(context: context).ObserveExecutor(dir);
            if (observed.Verdict != ProcessVerdict.Dead)
                continue;
            var refusal = new CheckpointDelete(context).Refusal(dir, new DeleteOwnership { IsolatedRoot = resultsRoot, CreatedPaths = [dir], RequireBinName = false, AllowOutsideRepository = true }) ?? new CheckpointOutputLedger(context).Check(dir);
            if (refusal is not null) { refusals.Add(refusal); continue; }
            if (dryRun)
            {
                deleted.Add(dir);
                continue;
            }

            var removed = new CheckpointDelete(context).Apply(dir, new DeleteOwnership { IsolatedRoot = resultsRoot, CreatedPaths = [dir], RequireBinName = false, AllowOutsideRepository = true }, inventoryCheck: () => new CheckpointOutputLedger(context).Check(dir));
            if (removed.Disposition == DeleteDisposition.Deleted && !context.Io.DirectoryExists(dir) && !context.Io.FileExists(dir))
                deleted.Add(dir);
            else if (removed.Disposition == DeleteDisposition.Refused || context.Io.DirectoryExists(dir) || context.Io.FileExists(dir))
                refusals.Add(string.IsNullOrWhiteSpace(removed.Reason) ? "still-present" : removed.Reason);
        }

        return new CleanResult { Deleted = deleted, Refusals = refusals };
    }

    public string DeletedLine(bool dryRun, string path) =>
        (dryRun ? "would delete " : "deleted ") + path;

    private bool Listed(IReadOnlyCollection<string> created, string full)
    {
        var comparison = context.PathComparison;
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

    private void Collect(string repoRoot, string directory, string buildId, List<string> found)
    {
        IEnumerable<string> children;
        try { children = Directories(directory); }
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

public static class OutputCleanup
{
    public static bool IsPathSegment(string value) =>
        CheckpointOutputNames.IsPathSegment(value);
    public static bool IsIsolatedOutputId(string value) =>
        CheckpointOutputNames.IsIsolatedOutputId(value);
    public static bool IsStrictlyInside(string root, string candidate) =>
        new CheckpointOutputCleanup(CheckpointCleanupDefaults.Context).IsStrictlyInside(root, candidate);
    public static CleanResult CleanOwnedOutputs(string repoRoot, IReadOnlyCollection<string> ownedIds, int exitCode, bool cleanOnRed, bool dryRun,
        Action? beforeDelete = null, IReadOnlyCollection<string>? createdPaths = null, string? gitExecutable = null,
        bool refuseUnrecorded = false) =>
        new CheckpointOutputCleanup(CheckpointCleanupDefaults.Context).CleanOwnedOutputs(repoRoot, ownedIds, exitCode, cleanOnRed, dryRun, beforeDelete, createdPaths, gitExecutable, refuseUnrecorded);
    public static List<string> MatchingIsolatedDirectories(string repoRoot, string buildId) =>
        new CheckpointOutputCleanup(CheckpointCleanupDefaults.Context).MatchingIsolatedDirectories(repoRoot, buildId);
    public static void RecordCreatedOutputs(RunState state, string repoRoot, string buildId, IReadOnlyCollection<string> before) =>
        new CheckpointOutputCleanup(CheckpointCleanupDefaults.Context).RecordCreatedOutputs(state, repoRoot, buildId, before);
    public static CleanResult RemoveOlderRuns(string resultsRoot, TimeSpan olderThan, bool dryRun) =>
        new CheckpointOutputCleanup(CheckpointCleanupDefaults.Context).RemoveOlderRuns(resultsRoot, olderThan, dryRun);
    public static string DeletedLine(bool dryRun, string path) =>
        new CheckpointOutputCleanup(CheckpointCleanupDefaults.Context).DeletedLine(dryRun, path);
}
