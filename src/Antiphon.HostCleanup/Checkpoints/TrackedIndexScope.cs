using Antiphon.HostCleanup.Checkpoints;

namespace Antiphon.Checkpoints;

// CARD-2052: one owned-output removal asks "is this entry tracked?" once per entry. Reading and parsing the whole
// index for each of a 2,190-entry output took 4-5 minutes on server2 (Review ad310231). The scope keeps one
// in-process listing together with the stamp observed both before and after it was read, and answers from it only
// while a fresh stamp still equals that one. Git and libgit2 replace the index through a lock file and a rename, so
// a write gives it a new inode and change time; the stamp also covers the .git entry, the git directory it names,
// both config files, the commondir file and the environment IndexListing declines on. A stamp that cannot be read,
// or any difference, reads the index again through every IndexListing check. Nothing outlives the scope.
internal sealed class TrackedIndexScope
{
    private string? _owner;
    private TrackedIndexStamp? _stamp;
    private HashSet<string>? _entries;
    private HashSet<string>? _directories;

    internal void Clear()
    {
        _owner = null;
        _stamp = null;
        _entries = null;
        _directories = null;
    }

    internal void Store(string owner, TrackedIndexStamp? stamp, string listing)
    {
        Clear();
        if (stamp is null)
            return;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var entries = new HashSet<string>(comparer);
        var directories = new HashSet<string>(comparer);
        foreach (var entry in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            entries.Add(entry);
            for (var at = entry.IndexOf('/'); at > 0; at = entry.IndexOf('/', at + 1))
                directories.Add(entry[..at]);
        }

        _owner = owner;
        _stamp = stamp;
        _entries = entries;
        _directories = directories;
    }

    // The same answer as SafeDelete's scan of the listing: an entry equal to a path, or under it as a directory,
    // or any entry at all for the checkout root. Null when the scope holds nothing for this stamp.
    internal bool? Answer(string owner, TrackedIndexStamp? stamp, IReadOnlyList<string> relatives)
    {
        if (stamp is null || _stamp is null || _entries is null || _directories is null
            || !string.Equals(owner, _owner, StringComparison.Ordinal) || stamp != _stamp)
            return null;
        foreach (var relative in relatives)
            if (relative.Length == 0 ? _entries.Count > 0 : _entries.Contains(relative) || _directories.Contains(relative))
                return true;
        return false;
    }
}

internal sealed record TrackedIndexStamp(
    string GitDirectory,
    string DotGitText,
    CheckpointStat DotGit,
    CheckpointStat Index,
    CheckpointStat Config,
    CheckpointStat WorktreeConfig,
    CheckpointStat CommonDirFile,
    string CommonDirText,
    CheckpointStat CommonConfig,
    string Environment)
{
    private const int MaxPointerBytes = 4096;

    private static readonly string[] DeclinedVariables =
    [
        "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE",
        "GIT_REPLACE_REF_BASE", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE", "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE",
    ];

    // Null (no reuse) for anything other than a plain .git directory or a plain "gitdir: <path>" file, an index
    // that is not a regular file with an inode and a change time, and any unreadable part.
    internal static TrackedIndexStamp? Read(CheckpointCleanupContext context, string owner)
    {
        try
        {
            CheckpointStat Stat(string path) => context.IndexStampStat is { } seam ? seam(path, context.Io.CustodyStat(path)) : context.Io.CustodyStat(path);
            var dotGitPath = Path.Combine(owner, ".git");
            var dotGit = Stat(dotGitPath);
            string gitDirectory;
            var dotGitText = "";
            if (dotGit.Kind == "directory")
                gitDirectory = dotGitPath;
            else if (dotGit.Kind == "file" && Pointer(dotGitPath, dotGit, "gitdir: ") is { } target)
            {
                dotGitText = target;
                gitDirectory = Path.GetFullPath(target, owner);
            }
            else
                return null;

            var index = Stat(Path.Combine(gitDirectory, "index"));
            if (index.Kind != "file" || index.Inode == 0 || index.Changed == 0)
                return null;
            var config = Stat(Path.Combine(gitDirectory, "config"));
            var worktreeConfig = Stat(Path.Combine(gitDirectory, "config.worktree"));
            var commonDirPath = Path.Combine(gitDirectory, "commondir");
            var commonDirFile = Stat(commonDirPath);
            var commonDirText = "";
            var commonConfig = new CheckpointStat("absent", 0, 0, 0, 0, 0);
            if (commonDirFile.Kind == "file")
            {
                if (Pointer(commonDirPath, commonDirFile, "") is not { } common)
                    return null;
                commonDirText = common;
                commonConfig = Stat(Path.Combine(Path.GetFullPath(common, gitDirectory), "config"));
            }
            else if (commonDirFile.Kind != "absent")
                return null;

            foreach (var part in new[] { config, worktreeConfig, commonConfig })
                if (part.Kind is not ("file" or "absent"))
                    return null;
            var environment = string.Join("\0", DeclinedVariables.Select(name => name + "=" + System.Environment.GetEnvironmentVariable(name)));
            return new TrackedIndexStamp(gitDirectory, dotGitText, dotGit, index, config, worktreeConfig, commonDirFile,
                commonDirText, commonConfig, environment);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException or System.Text.DecoderFallbackException)
        {
            return null;
        }
    }

    // A small pointer file's single line after the prefix (git strips only the trailing line end).
    private static string? Pointer(string path, CheckpointStat stat, string prefix)
    {
        if (stat.Size <= 0 || stat.Size > MaxPointerBytes)
            return null;
        var text = File.ReadAllText(path, new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true));
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var target = text[prefix.Length..].TrimEnd('\n', '\r');
        if (target.Length == 0 || target.Any(char.IsControl) || target != target.Trim())
            return null;
        return target;
    }
}
