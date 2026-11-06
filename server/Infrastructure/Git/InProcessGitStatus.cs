using System.Buffers.Binary;
using LibGit2Sharp;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// libgit2 (LibGit2Sharp) answers to the working-tree questions the product asks git <c>status</c> and
/// <c>ls-files</c>: is the checkout clean, which paths are dirty, untracked, ignored or unmerged, and is
/// any index entry flagged. These are verdicts, not git's bytes: callers only test emptiness or compare
/// path sets. Null means "ask git": every repository shape libgit2 might read differently falls back.
/// <para>
/// Not wired into any runner yet. <c>LibGit2VsGitWorkingTreeVerdictTests</c> pins each verdict against
/// the real CLI, across the repository shapes and argument forms the product uses.
/// </para>
/// <list type="bullet">
/// <item>Index version 2 only. git writes version 3 as soon as any entry carries an extended flag
/// (skip-worktree, intent-to-add), and LibGit2Sharp cannot see those flags; version 4 and split
/// indexes are git's too.</item>
/// <item>Gitlinks (submodules) and sparse checkouts are git's.</item>
/// <item>Any libgit2 error (an unreadable directory, a path it cannot open) is git's.</item>
/// <item>Racy entries: libgit2 re-reads an entry whose mtime is not before the index write at
/// nanosecond precision, git at second precision. They differ only for a same-size edit whose mtime is
/// put back inside the index's own second; git is blind to the same edit a second later. Accepted.</item>
/// </list>
/// </summary>
internal static class InProcessGitStatus
{
    /// <summary>
    /// The paths <c>git status --porcelain=v1 -z --untracked-files=(all|no) --ignore-submodules=none
    /// [--ignored]</c> lists, with '/' separators and nested repositories as "dir/". Empty is clean.
    /// Renames count as both paths.
    /// </summary>
    public static IReadOnlySet<string>? StatusPaths(string checkout, bool untracked, bool ignored = false)
        => Read(checkout, repo =>
        {
            var options = new StatusOptions
            {
                Show = StatusShowOption.IndexAndWorkDir,
                IncludeUntracked = untracked,
                RecurseUntrackedDirs = true,
                IncludeIgnored = ignored,
                RecurseIgnoredDirs = ignored,
                DetectRenamesInIndex = false,
                DetectRenamesInWorkDir = false,
                ExcludeSubmodules = false,
                IncludeUnaltered = false,
            };
            if ((untracked || ignored) && !WalkReadable(repo, ignored)) return null;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in repo.RetrieveStatus(options))
            {
                if (entry.State == FileStatus.Unaltered) continue;
                if (entry.State == FileStatus.Ignored && !ignored) continue;
                paths.Add(Slashes(entry.FilePath));
            }
            return (IReadOnlySet<string>)paths;
        });

    /// <summary>
    /// <c>git ls-files --others --exclude-standard -z</c> (<paramref name="ignored"/> false) or
    /// <c>git ls-files --others --ignored --exclude-standard -z</c> (true), as a path set.
    /// </summary>
    public static IReadOnlySet<string>? OtherPaths(string checkout, bool ignored)
        => Read(checkout, repo =>
        {
            var options = new StatusOptions
            {
                Show = StatusShowOption.WorkDirOnly,
                IncludeUntracked = !ignored,
                RecurseUntrackedDirs = true,
                IncludeIgnored = ignored,
                RecurseIgnoredDirs = true,
                DetectRenamesInIndex = false,
                DetectRenamesInWorkDir = false,
            };
            if (!WalkReadable(repo, ignored)) return null;
            var want = ignored ? FileStatus.Ignored : FileStatus.NewInWorkdir;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in repo.RetrieveStatus(options))
                if (entry.State == want) paths.Add(Slashes(entry.FilePath));
            return (IReadOnlySet<string>)paths;
        });

    /// <summary>
    /// <c>git ls-files -v -z</c> as (tag, path) in index order: 'H' cached, 'M' unmerged, lowercase when
    /// assume-unchanged. 'S' (skip-worktree) never appears: such an index is version 3 and falls back.
    /// </summary>
    public static IReadOnlyList<(char Tag, string Path)>? IndexTags(string checkout)
        => Read(checkout, repo => (IReadOnlyList<(char, string)>)repo.Index
            .Select(e =>
            {
                var tag = e.StageLevel == StageLevel.Staged ? 'H' : 'M';
                return (e.AssumeUnchanged ? char.ToLowerInvariant(tag) : tag, Slashes(e.Path));
            })
            .ToList());

    /// <summary>Whether <c>git ls-files --unmerged -z</c> lists anything.</summary>
    public static bool? HasUnmerged(string checkout)
        => Read<object>(checkout, repo => repo.Index.Any(e => e.StageLevel != StageLevel.Staged)) is bool any ? any : null;

    private static T? Read<T>(string checkout, Func<Repository, T?> answer) where T : class
    {
        if (!InProcessGitReads.Enabled) return null;
        try
        {
            using var repo = new Repository(checkout);
            if (repo.Info.IsBare || repo.Info.WorkingDirectory is null || InProcessGitReads.Unusual(repo)) return null;
            if (!LandingGit.PathsEqual(repo.Info.WorkingDirectory.TrimEnd('\\', '/'), checkout.TrimEnd('\\', '/'))) return null;
            if (!PlainIndex(repo.Info.Path)) return null;
            if (repo.Config.Get<bool>("core.sparseCheckout")?.Value == true) return null;
            if (repo.Index.Any(e => e.Mode == Mode.GitLink)) return null;
            return answer(repo);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            return null;
        }
    }

    /// <summary>An index file of version 2 and no split-index shared file beside it.</summary>
    private static bool PlainIndex(string gitDir)
    {
        Span<byte> header = stackalloc byte[8];
        using (var stream = new FileStream(Path.Combine(gitDir, "index"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            if (stream.Read(header) != 8) return false;
        return header[..4].SequenceEqual("DIRC"u8)
            && BinaryPrimitives.ReadUInt32BigEndian(header[4..]) == 2
            && !Directory.EnumerateFiles(gitDir, "sharedindex.*").Any();
    }

    /// <summary>
    /// Whether every directory git's untracked walk enters can be listed. git warns "could not open
    /// directory" and lists nothing beneath it (CARD-1714); libgit2 skips it silently and calls the tree
    /// clean, so any directory that cannot be listed sends the question to git. The walk mirrors git's:
    /// it skips .git, nested repositories (listed as "dir/", never entered) and reparse points, and it
    /// prunes ignored directories unless ignored files are listed too. Directories only, no file reads.
    /// </summary>
    private static bool WalkReadable(Repository repo, bool intoIgnored)
    {
        var root = repo.Info.WorkingDirectory;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = false, RecurseSubdirectories = false, ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        var pending = new Stack<(string Full, string Relative)>();
        pending.Push((root, ""));
        while (pending.Count != 0)
        {
            var (full, relative) = pending.Pop();
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(full, "*", options).ToList(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return false; }
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (relative.Length == 0 && string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase)) continue;
                var childRelative = relative + name + "/";
                if (Directory.Exists(Path.Combine(child, ".git")) || File.Exists(Path.Combine(child, ".git"))) continue;
                if (!intoIgnored && repo.Ignore.IsPathIgnored(childRelative)) continue;
                pending.Push((child, childRelative));
            }
        }
        return true;
    }

    private static string Slashes(string path) => path.Replace('\\', '/');
}
