using System.Buffers.Binary;
using System.Text;
using Antiphon.Server.Application.Dtos;
using LibGit2Sharp;

namespace Antiphon.GitInProcess;

/// <summary>
/// libgit2 (LibGit2Sharp) answers to the working-tree questions the product asks git <c>status</c> and
/// <c>ls-files</c>: is the checkout clean, which paths are dirty, untracked, ignored or unmerged, and is
/// any index entry flagged. These are verdicts, not git's bytes: callers only test emptiness or compare
/// path sets. Null means "ask git": every repository shape libgit2 might read differently falls back.
/// <para>
/// <see cref="TryAnswer"/> serves the server's <c>LandingGit</c>'s runner and keeps its contract of git's exact
/// bytes or nothing: a status or ls-files --others/--unmerged is answered only when it is empty (a dirty
/// tree still runs git, so every caller that reads the listing gets git's own), and ls-files -v is
/// rebuilt exactly. A status-like answer also needs the config to agree with the git child's
/// (<see cref="GitStatusConfigAgreement"/>). <c>LibGit2VsGitWorkingTreeVerdictTests</c> pins each
/// verdict against the real CLI, across the repository shapes and argument forms the product uses.
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
    /// git's exact result for a working-tree read the product runs, or null to run git. Leading
    /// <c>-c key=value</c> pairs are command-level config. <paramref name="childEnvironment"/> is the git
    /// child's environment (null: this process's), with which the status config must agree.
    /// </summary>
    public static LandingGitResult? TryAnswer(string checkout, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? childEnvironment, bool verdictOnly = false)
    {
        if (!InProcessGitReads.Enabled) return null;
        var overrides = new List<KeyValuePair<string, string>>();
        var at = 0;
        while (at + 1 < arguments.Count && arguments[at] == "-c")
        {
            var pair = arguments[at + 1];
            var equals = pair.IndexOf('=');
            if (equals <= 0) return null;
            overrides.Add(new(pair[..equals], pair[(equals + 1)..]));
            at += 2;
        }
        var a = arguments.Skip(at).ToArray();
        if (a is ["ls-files", "-v", "-z"])
        {
            if (overrides.Count != 0 || IndexTags(checkout) is not { } tags) return null;
            var output = new StringBuilder();
            foreach (var (tag, path) in tags) output.Append(tag).Append(' ').Append(path).Append('\0');
            return Ok(output.ToString());
        }
        if (a is ["ls-files", "--unmerged", "-z"])
            return overrides.Count == 0 && HasUnmerged(checkout) == false ? Ok("") : null;
        Func<Configuration, bool> agrees = config => GitStatusConfigAgreement.Agrees(config, childEnvironment, overrides);
        if (a is ["ls-files", "--others", "--exclude-standard", "-z"] or ["ls-files", "--others", "--ignored", "--exclude-standard", "-z"])
            return OtherPaths(checkout, ignored: a.Length == 5, agrees) is { Count: 0 } ? Ok("") : null;
        if (StatusForm(a) is { } form)
        {
            if (StatusEntries(checkout, form.Untracked, form.Ignored, agrees) is not { } entries) return null;
            if (entries.Count == 0) return Ok("");
            // A dirty tree is answered only for a caller that asked for the verdict (GitTreeVerdict): it
            // tests emptiness, so porcelain-shaped rows are enough. Every other caller still gets git's bytes.
            // Only a certain entry decides it: a work-tree-only change to a tracked file is libgit2's own
            // reading of the bytes (line-ending conversion, filters, racy stat), where git may still say clean.
            if (!verdictOnly || !entries.Any(e => CertainlyDirty(e.State))) return null;
            var output = new StringBuilder();
            foreach (var (path, state) in entries) output.Append(Code(state)).Append(' ').Append(path).Append('\0');
            return Ok(output.ToString());
        }
        return null;
    }

    /// <summary>
    /// <c>status --porcelain[=v1] -z --untracked-files=(all|no) [--ignore-submodules=none] [--ignored]</c>.
    /// Without --ignore-submodules=none git applies submodule ignore config, but any gitlink falls back.
    /// </summary>
    private static (bool Untracked, bool Ignored)? StatusForm(string[] a)
    {
        if (a.Length < 4 || a[0] != "status" || a[1] is not ("--porcelain" or "--porcelain=v1") || a[2] != "-z") return null;
        bool untracked;
        if (a[3] == "--untracked-files=all") untracked = true;
        else if (a[3] == "--untracked-files=no") untracked = false;
        else return null;
        var ignored = false;
        foreach (var extra in a.Skip(4))
            if (extra == "--ignore-submodules=none") continue;
            else if (extra == "--ignored" && untracked && !ignored) ignored = true;
            else return null;
        return (untracked, ignored);
    }

    private static LandingGitResult Ok(string output) => new(0, output, "");

    /// <summary>
    /// The paths <c>git status --porcelain=v1 -z --untracked-files=(all|no) --ignore-submodules=none
    /// [--ignored]</c> lists, with '/' separators and nested repositories as "dir/". Empty is clean.
    /// Renames count as both paths.
    /// </summary>
    public static IReadOnlySet<string>? StatusPaths(string checkout, bool untracked, bool ignored = false,
        Func<Configuration, bool>? agrees = null)
        => StatusEntries(checkout, untracked, ignored, agrees) is { } entries
            ? entries.Select(e => e.Path).ToHashSet(StringComparer.Ordinal)
            : null;

    /// <summary>
    /// The same listing as porcelain-shaped rows (<c>XY path</c>, path order): a verdict, not git's bytes.
    /// The codes are libgit2's reading and renames are not paired.
    /// </summary>
    internal static IReadOnlyList<string>? StatusRows(string checkout, bool untracked, bool ignored = false,
        Func<Configuration, bool>? agrees = null)
        => StatusEntries(checkout, untracked, ignored, agrees) is { } entries
            ? entries.Select(e => Code(e.State) + " " + e.Path).ToList()
            : null;

    private static List<(string Path, FileStatus State)>? StatusEntries(string checkout, bool untracked, bool ignored,
        Func<Configuration, bool>? agrees)
        => Read(checkout, agrees, repo =>
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
            var entries = new SortedDictionary<string, FileStatus>(StringComparer.Ordinal);
            foreach (var entry in repo.RetrieveStatus(options))
            {
                if (entry.State == FileStatus.Unaltered) continue;
                if (entry.State == FileStatus.Ignored && !ignored) continue;
                var path = Slashes(entry.FilePath);
                entries[path] = entries.TryGetValue(path, out var seen) ? seen | entry.State : entry.State;
            }
            return entries.Select(e => (e.Key, e.Value)).ToList();
        });

    /// <summary>
    /// An entry git lists however it reads the work tree's bytes: untracked or ignored (the path sets are
    /// pinned against git), a conflict, or an index that differs from HEAD (object ids, no filters).
    /// A work-tree-only modification, type change or deletion of a tracked file is not.
    /// </summary>
    internal static bool CertainlyDirty(FileStatus state) =>
        state is FileStatus.NewInWorkdir or FileStatus.Ignored
        || (state & (FileStatus.Conflicted | FileStatus.NewInIndex | FileStatus.ModifiedInIndex
            | FileStatus.DeletedFromIndex | FileStatus.RenamedInIndex | FileStatus.TypeChangeInIndex)) != 0;

    /// <summary>Whether a verdict caller's dirty status is answered in process: some entry is certain.</summary>
    internal static bool? VerdictAnswersDirty(string checkout, bool untracked, bool ignored = false)
        => StatusEntries(checkout, untracked, ignored, null) is { } entries ? entries.Any(e => CertainlyDirty(e.State)) : null;

    private static string Code(FileStatus state)
    {
        if (state.HasFlag(FileStatus.Conflicted)) return "UU";
        if (state == FileStatus.Ignored) return "!!";
        if (state == FileStatus.NewInWorkdir) return "??";
        var x = state.HasFlag(FileStatus.NewInIndex) ? 'A'
            : state.HasFlag(FileStatus.DeletedFromIndex) ? 'D'
            : state.HasFlag(FileStatus.RenamedInIndex) ? 'R'
            : state.HasFlag(FileStatus.TypeChangeInIndex) ? 'T'
            : state.HasFlag(FileStatus.ModifiedInIndex) ? 'M' : ' ';
        var y = state.HasFlag(FileStatus.DeletedFromWorkdir) ? 'D'
            : state.HasFlag(FileStatus.TypeChangeInWorkdir) ? 'T'
            : state.HasFlag(FileStatus.ModifiedInWorkdir) || state.HasFlag(FileStatus.NewInWorkdir) ? 'M' : ' ';
        return x == ' ' && y == ' ' ? " M" : $"{x}{y}";
    }

    /// <summary>
    /// <c>git ls-files --others --exclude-standard -z</c> (<paramref name="ignored"/> false) or
    /// <c>git ls-files --others --ignored --exclude-standard -z</c> (true), as a path set.
    /// </summary>
    public static IReadOnlySet<string>? OtherPaths(string checkout, bool ignored, Func<Configuration, bool>? agrees = null)
        => Read(checkout, agrees, repo =>
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
        => Read(checkout, null, repo => (IReadOnlyList<(char, string)>)repo.Index
            .Select(e =>
            {
                var tag = e.StageLevel == StageLevel.Staged ? 'H' : 'M';
                return (e.AssumeUnchanged ? char.ToLowerInvariant(tag) : tag, Slashes(e.Path));
            })
            .ToList());

    /// <summary>Whether <c>git ls-files --unmerged -z</c> lists anything.</summary>
    public static bool? HasUnmerged(string checkout)
        => Read<object>(checkout, null, repo => repo.Index.Any(e => e.StageLevel != StageLevel.Staged)) is bool any ? any : null;

    private static T? Read<T>(string checkout, Func<Configuration, bool>? agrees, Func<Repository, T?> answer) where T : class
    {
        if (!InProcessGitReads.Enabled) return null;
        try
        {
            using var repo = new Repository(checkout);
            if (repo.Info.IsBare || repo.Info.WorkingDirectory is null || InProcessGitReads.Unusual(repo)) return null;
            if (!GitText.PathsEqual(repo.Info.WorkingDirectory.TrimEnd('\\', '/'), checkout.TrimEnd('\\', '/'))) return null;
            if (!PlainIndex(repo.Info.Path)) return null;
            // Config only through ConfigOf: touching repo.Config of a linked worktree would install LibGit2Sharp's
            // config, which lacks the repository's own file, into the libgit2 status below.
            if (InProcessGitReads.ConfigOf(repo) is not { } view) return null;
            try
            {
                if (view.Config.Get<bool>("core.sparseCheckout")?.Value == true) return null;
                if (repo.Index.Any(e => e.Mode == Mode.GitLink)) return null;
                if (agrees is not null && !agrees(view.Config)) return null;
                return answer(repo);
            }
            finally
            {
                if (view.Owned) view.Config.Dispose();
            }
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
