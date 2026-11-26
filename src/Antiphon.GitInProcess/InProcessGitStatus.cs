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
/// <see cref="TryAnswer"/> serves the git runners (LandingGit, the session runner, WorktreeManager, GitWorkspaceService)
/// in git's own layout: a status (with or without -z) is libgit2's listing printed as git's porcelain v1, clean or
/// with changes (operator, 2026-11-25: each row's code is libgit2's reading of the work tree); ls-files --others
/// lists libgit2's path set in git's order; ls-files -v is rebuilt exactly; ls-files -z and ls-files --stage -z are
/// read byte for byte from the index file (<see cref="IndexListing"/>; they read no config, so need no agreement).
/// A status git prints in another shape is git's: a staged rename (git pairs it), a conflict, a path git orders or
/// (without -z) quotes differently. A status-like answer also needs the config to agree with the git child's
/// (<see cref="GitStatusConfigAgreement"/>), and a status needs no work-tree conversion libgit2 lacks
/// (<see cref="WorkTreeConversion"/>: filter drivers, working-tree-encoding). <c>LibGit2VsGitWorkingTreeVerdictTests</c>
/// pins every answer against the real CLI, byte for byte, across the repository shapes and argument forms the
/// product uses, including the conversions that diverge.
/// </para>
/// <list type="bullet">
/// <item>Index version 2 only. git writes version 3 as soon as any entry carries an extended flag
/// (skip-worktree, intent-to-add), and LibGit2Sharp cannot see those flags; version 4 and split
/// indexes are git's too.</item>
/// <item>Gitlinks (submodules) and sparse checkouts are git's.</item>
/// <item>Any libgit2 error (a path it cannot open) is git's.</item>
/// <item>A work-tree directory that cannot be listed is skipped, as git skips it (git only warns "could not
/// open directory"): nothing under it is listed, and the question is not asked of git.</item>
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
        IReadOnlyDictionary<string, string?>? childEnvironment)
    {
        if (!InProcessGitReads.Enabled) return null;
        // A child pointed at another repository, work tree or index reads something else than this: git's to answer.
        foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR" })
        {
            var value = childEnvironment is null ? Environment.GetEnvironmentVariable(name)
                : childEnvironment.TryGetValue(name, out var set) ? set : null;
            if (!string.IsNullOrEmpty(value)) return null;
        }
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
        if (a is ["ls-files", "-z"] or ["ls-files", "--stage", "-z"])
            return overrides.Count == 0 && IndexListing(checkout, stage: a.Length == 3) is { } listing ? Ok(listing) : null;
        if (a is ["ls-files", "--unmerged", "-z"])
            return overrides.Count == 0 && HasUnmerged(checkout) == false ? Ok("") : null;
        Func<Configuration, bool> agrees = config => GitStatusConfigAgreement.Agrees(config, childEnvironment, overrides);
        if (a is ["ls-files", "--others", "--exclude-standard", "-z"] or ["ls-files", "--others", "--ignored", "--exclude-standard", "-z"])
        {
            if (OtherPaths(checkout, ignored: a.Length == 5, agrees) is not { } others || others.Any(p => !GitOrdered(p))) return null;
            var listing = new StringBuilder();
            foreach (var path in others.Order(StringComparer.Ordinal)) listing.Append(path).Append('\0');
            return Ok(listing.ToString());
        }
        if (StatusForm(a) is { } form)
        {
            if (StatusEntries(checkout, form.Untracked, form.Ignored, agrees, childEnvironment) is not { } entries) return null;
            if (entries.Count == 0) return Ok("");
            // A tree with changes is libgit2's listing in git's porcelain v1 layout (operator, 2026-11-25): the
            // same path set git lists (pinned by LibGit2VsGitWorkingTreeVerdictTests), each row's code from
            // libgit2's own reading of the work tree, for every caller. A rename git would pair, a conflict
            // (git's two-letter unmerged codes), or a path git would order or quote differently is git's.
            return Porcelain(entries, form.Nul) is { } rows ? Ok(rows) : null;
        }
        return null;
    }

    /// <summary>
    /// libgit2's status entries as <c>git status --porcelain=v1</c> prints them: changed tracked paths, then
    /// untracked (<c>??</c>), then ignored (<c>!!</c>), each group in path order; NUL-terminated with -z, else
    /// LF-terminated. Null where git's rows could differ in shape: a conflict, a possible index rename (git
    /// pairs an added and a deleted path as <c>R</c>), a path git orders differently, a state with no code, or,
    /// without -z, a path git would C-quote.
    /// </summary>
    private static string? Porcelain(List<(string Path, FileStatus State)> entries, bool nul)
    {
        if (entries.Any(e => e.State.HasFlag(FileStatus.Conflicted))) return null;
        if (entries.Any(e => e.State.HasFlag(FileStatus.NewInIndex)) && entries.Any(e => e.State.HasFlag(FileStatus.DeletedFromIndex)))
            return null;
        var rows = new List<(int Group, string Path, string Code)>();
        foreach (var (path, state) in entries)
        {
            if (!GitOrdered(path) || (!nul && !PlainUnquoted(path))) return null;
            // An ignored path in another state too (removed from the index, still on disk and ignored) is two git
            // rows, "D  path" and "!! path": git's to list.
            if (state.HasFlag(FileStatus.Ignored) && state != FileStatus.Ignored) return null;
            // A path deleted from the index and present in the work tree is one libgit2 entry and two git rows:
            // "D  path" with the changes, "?? path" with the untracked files.
            var tracked = state;
            if (state != FileStatus.NewInWorkdir && state.HasFlag(FileStatus.NewInWorkdir))
            {
                if (!state.HasFlag(FileStatus.DeletedFromIndex)) return null;
                rows.Add((1, path, "??"));
                tracked = state & ~FileStatus.NewInWorkdir;
            }
            if (Code(tracked) is not { } code) return null;
            rows.Add((code == "??" ? 1 : code == "!!" ? 2 : 0, path, code));
        }
        var output = new StringBuilder();
        foreach (var row in rows.OrderBy(r => r.Group).ThenBy(r => r.Path, StringComparer.Ordinal))
            output.Append(row.Code).Append(' ').Append(row.Path).Append(nul ? '\0' : '\n');
        return output.ToString();
    }

    /// <summary>
    /// Whether git's status detects copies (status.renames, else diff.renames, is copies/copy): git then pairs an added
    /// path with a modified source as <c>C</c>, which libgit2's listing cannot show. Both are agreement keys, so
    /// libgit2's merged value is the child's.
    /// </summary>
    private static bool CopyDetection(Configuration config) =>
        (config.Get<string>("status.renames")?.Value ?? config.Get<string>("diff.renames")?.Value)?.Trim().ToLowerInvariant()
            is "copies" or "copy";

    /// <summary>Whether ordinal UTF-16 order is git's byte (UTF-8) order for this path: no surrogate or U+E000-U+FFFF char.</summary>
    private static bool GitOrdered(string path) => !path.Any(c => c >= '\uD800');

    /// <summary>Whether git prints this path as is in a listing without -z: printable ASCII, no space, quote or backslash.</summary>
    private static bool PlainUnquoted(string path) => path.All(c => c is > ' ' and < '\x7f' and not '"' and not '\\');

    /// <summary>
    /// <c>status --porcelain[=v1] [-z] --untracked-files=(all|no) [--ignore-submodules=none] [--ignored]</c>.
    /// Without --ignore-submodules=none git applies submodule ignore config, but any gitlink falls back.
    /// </summary>
    private static (bool Untracked, bool Ignored, bool Nul)? StatusForm(string[] a)
    {
        if (a.Length < 3 || a[0] != "status" || a[1] is not ("--porcelain" or "--porcelain=v1")) return null;
        var nul = a[2] == "-z";
        var at = nul ? 3 : 2;
        if (a.Length <= at) return null;
        bool untracked;
        if (a[at] == "--untracked-files=all") untracked = true;
        else if (a[at] == "--untracked-files=no") untracked = false;
        else return null;
        var ignored = false;
        foreach (var extra in a.Skip(at + 1))
            if (extra == "--ignore-submodules=none") continue;
            else if (extra == "--ignored" && untracked && !ignored) ignored = true;
            else return null;
        return (untracked, ignored, nul);
    }

    private static LandingGitResult Ok(string output) => new(0, output, "");

    /// <summary>
    /// The paths <c>git status --porcelain=v1 -z --untracked-files=(all|no) --ignore-submodules=none
    /// [--ignored]</c> lists, with '/' separators and nested repositories as "dir/". Empty is clean.
    /// Renames count as both paths.
    /// </summary>
    public static IReadOnlySet<string>? StatusPaths(string checkout, bool untracked, bool ignored = false,
        Func<Configuration, bool>? agrees = null, IReadOnlyDictionary<string, string?>? childEnvironment = null)
        => StatusEntries(checkout, untracked, ignored, agrees, childEnvironment) is { } entries
            ? entries.Select(e => e.Path).ToHashSet(StringComparer.Ordinal)
            : null;

    /// <summary>
    /// The same listing as porcelain-shaped rows (<c>XY path</c>, path order): a verdict, not git's bytes.
    /// The codes are libgit2's reading and renames are not paired.
    /// </summary>
    internal static IReadOnlyList<string>? StatusRows(string checkout, bool untracked, bool ignored = false,
        Func<Configuration, bool>? agrees = null, IReadOnlyDictionary<string, string?>? childEnvironment = null)
        => StatusEntries(checkout, untracked, ignored, agrees, childEnvironment) is { } entries
            ? entries.Select(e => (Code(e.State) ?? " M") + " " + e.Path).ToList()
            : null;

    /// <summary>
    /// libgit2s status entries, or null (git answers) where its reading could differ from the git childs, including a
    /// work-tree conversion it lacks (<see cref="WorkTreeConversion"/>, judged against <paramref name="childEnvironment"/>,
    /// null: this processs).
    /// </summary>
    private static List<(string Path, FileStatus State)>? StatusEntries(string checkout, bool untracked, bool ignored,
        Func<Configuration, bool>? agrees, IReadOnlyDictionary<string, string?>? childEnvironment = null)
        => Read(checkout, agrees, workTreeContent: true, childEnvironment: childEnvironment, answer: repo =>
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

    /// <summary>The porcelain v1 XY code of a libgit2 state; null for a state with no change git would show.</summary>
    private static string? Code(FileStatus state)
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
        return x == ' ' && y == ' ' ? null : $"{x}{y}";
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

    /// <summary>
    /// <c>git ls-files -z</c> (<paramref name="stage"/> false: "path\0" per entry) or <c>git ls-files --stage -z</c>
    /// ("mode oid stage\tpath\0"), run at the checkout root: git's bytes, decoded as the runner decodes them (UTF-8).
    /// Read from the index file itself, not from LibGit2Sharp's entries, whose paths carry native separators on
    /// Windows and whose modes are normalised. Null (git answers) for anything git might print differently or not
    /// at all: see <see cref="ListIndex"/>. A linked worktree renamed on disk is read too (<see cref="MovedLinkedWorktree"/>),
    /// and any core.worktree setting is git's.
    /// </summary>
    public static string? IndexListing(string checkout, bool stage)
        => Read(checkout, null, repo =>
        {
            byte[] data;
            var index = Path.Combine(repo.Info.Path, "index");
            // No index yet (a fresh repository nothing was ever added to): git lists nothing, and neither does this.
            // One created since Read looked passes the same plain-index check as any other before it is read.
            if (NoIndexYet(repo.Info.Path)) return "";
            if (!PlainIndex(repo.Info.Path)) return null;
            using (var stream = new FileStream(index, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > int.MaxValue / 2) return null;
                data = new byte[stream.Length];
                stream.ReadExactly(data);
            }
            return ListIndex(data, stage);
        }, indexOnly: true);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// An index file's entries in file order (git prints them in that order), from a version-2 SHA-1 index whose
    /// trailing checksum holds. Null for: another signature or version (v3 carries skip-worktree and
    /// intent-to-add, v4 compresses paths), a bad or skipped (all-zero) checksum, any entry above stage 0 (an
    /// unmerged path), a mode other than a regular, executable or symlink blob (a gitlink, or a mode git never
    /// writes), a path that is not valid UTF-8 or holds a control character, and any required (lower-case)
    /// extension such as a split index's "link" or a sparse index's "sdir". Optional extensions (TREE, REUC,
    /// UNTR, FSMN, EOIE, IEOT) change nothing ls-files prints.
    /// </summary>
    internal static string? ListIndex(byte[] data, bool stage)
    {
        const int Header = 12, Hash = 20, Fixed = 62;
        if (data.Length < Header + Hash || !data.AsSpan(0, 4).SequenceEqual("DIRC"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)) != 2)
            return null;
        var body = data.Length - Hash;
        if (!System.Security.Cryptography.SHA1.HashData(data.AsSpan(0, body)).AsSpan().SequenceEqual(data.AsSpan(body))) return null;
        var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
        var output = new StringBuilder();
        var at = Header;
        for (var i = 0u; i < count; i++)
        {
            if (at + Fixed + 1 > body) return null;
            var mode = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 24));
            var flags = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at + 60));
            // 0x4000 extended (never valid in v2), 0x3000 the stage.
            if ((flags & 0x7000) != 0) return null;
            if (mode is not (0x81A4 or 0x81ED or 0xA000)) return null; // 100644, 100755, 120000
            var nameStart = at + Fixed;
            int nameLength = flags & 0xFFF;
            if (nameLength == 0xFFF)
            {
                var nul = data.AsSpan(nameStart, body - nameStart).IndexOf((byte)0);
                if (nul < 0) return null;
                nameLength = nul;
            }
            if (nameLength == 0 || nameStart + nameLength >= body || data[nameStart + nameLength] != 0) return null;
            var name = data.AsSpan(nameStart, nameLength);
            foreach (var b in name)
                if (b < 0x20 || b == 0x7f) return null;
            string path;
            try { path = StrictUtf8.GetString(name); }
            catch (DecoderFallbackException) { return null; }
            if (stage)
            {
                output.Append(Convert.ToString((int)mode, 8)).Append(' ')
                    .Append(Convert.ToHexString(data, at + 40, Hash).ToLowerInvariant()).Append(" 0\t");
            }
            output.Append(path).Append('\0');
            // Entries are padded with 1-8 NULs to a multiple of eight bytes.
            at += (Fixed + nameLength + 8) & ~7;
        }
        while (at < body)
        {
            if (at + 8 > body) return null;
            var signature = data[at];
            var size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 4));
            if (signature is < (byte)'A' or > (byte)'Z' || size > (uint)(body - at - 8)) return null;
            at += 8 + (int)size;
        }
        return output.ToString();
    }

    private static T? Read<T>(string checkout, Func<Configuration, bool>? agrees, Func<Repository, T?> answer,
        bool indexOnly = false, bool workTreeContent = false, IReadOnlyDictionary<string, string?>? childEnvironment = null)
        where T : class
    {
        if (!InProcessGitReads.Enabled) return null;
        // A variable that moves git's objects, refs or history elsewhere (GIT_COMMON_DIR, an object store, shallow or
        // graft files, a namespace or replace-ref base) is kept by the hardened git children of the direct callers
        // (SafeDelete, the checkpoint confinement): git's to answer. Those children drop GIT_DIR, GIT_WORK_TREE and
        // GIT_INDEX_FILE, so they read this checkout's own index, as this does; TryAnswer checks a child environment
        // that may keep them. (GIT_NO_REPLACE_OBJECTS only turns replacement off, and a repository with replace refs
        // is already declined as unusual.)
        foreach (var name in new[] { "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE",
                     "GIT_REPLACE_REF_BASE", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE" })
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) return null;
        try
        {
            using var repo = new Repository(checkout);
            if (repo.Info.IsBare || repo.Info.WorkingDirectory is null || InProcessGitReads.Unusual(repo)) return null;
            if (!GitText.PathsEqual(repo.Info.WorkingDirectory.TrimEnd('\\', '/'), checkout.TrimEnd('\\', '/'))
                && !(indexOnly && MovedLinkedWorktree(repo, checkout))) return null;
            // An index-only read also answers a repository with no index at all yet (IndexListing lists nothing).
            if (!(indexOnly && NoIndexYet(repo.Info.Path)) && !PlainIndex(repo.Info.Path)) return null;
            // Config only through ConfigOf: touching repo.Config of a linked worktree would install LibGit2Sharp's
            // config, which lacks the repository's own file, into the libgit2 status below.
            if (InProcessGitReads.ConfigOf(repo) is not { } view) return null;
            try
            {
                if (view.Config.Get<bool>("core.sparseCheckout")?.Value == true) return null;
                if (indexOnly && view.Config.Get<string>("core.worktree") is not null) return null;
                if (repo.Index.Any(e => e.Mode == Mode.GitLink)) return null;
                if (agrees is not null && !agrees(view.Config)) return null;
                if (workTreeContent && WorkTreeConversion.GitOnly(repo, view.Config, childEnvironment) is not null) return null;
                if (workTreeContent && CopyDetection(view.Config)) return null;
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

    /// <summary>
    /// A linked worktree renamed on disk (the guarded removal moves a tree aside before deleting it): git run there
    /// follows the checkout's own .git file to the git directory, so it reads the index libgit2 opened, while libgit2
    /// still reports the work tree recorded in that git directory's gitdir file. Only a read of the index alone may
    /// rely on this. The .git file must be a plain "gitdir: &lt;path&gt;" file (git strips only the trailing line
    /// end) naming exactly the linked worktree's git directory.
    /// </summary>
    private static bool MovedLinkedWorktree(Repository repo, string checkout)
    {
        var dotGit = Path.Combine(checkout, ".git");
        if (!File.Exists(dotGit) || (File.GetAttributes(dotGit) & FileAttributes.ReparsePoint) != 0) return false;
        if (!File.Exists(Path.Combine(repo.Info.Path, "commondir"))) return false;
        var text = File.ReadAllText(dotGit, StrictUtf8);
        if (!text.StartsWith("gitdir: ", StringComparison.Ordinal)) return false;
        var target = text["gitdir: ".Length..].TrimEnd('\n', '\r');
        if (target.Length == 0 || target.Any(char.IsControl) || target != target.Trim()) return false;
        return GitText.PathsEqual(Path.GetFullPath(target, Path.GetFullPath(checkout)), repo.Info.Path);
    }

    /// <summary>
    /// Nothing at all at &lt;gitdir&gt;/index (not a file, not a directory) and no shared index: a repository nothing
    /// was ever added to, where git lists nothing. Anything else odd there is git's to report.
    /// </summary>
    private static bool NoIndexYet(string gitDir)
    {
        var index = Path.Combine(gitDir, "index");
        return !File.Exists(index) && !Directory.Exists(index) && !Directory.EnumerateFiles(gitDir, "sharedindex.*").Any();
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

    private static string Slashes(string path) => path.Replace('\\', '/');
}
