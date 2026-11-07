using System.Runtime.InteropServices;
using System.Text;
using Antiphon.Server.Application.Dtos;
using LibGit2Sharp;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.GitInProcess;

/// <summary>
/// Answers a small set of read-only git commands in process with libgit2 (LibGit2Sharp) instead of
/// starting <c>git</c>: on Windows each git process costs ~40–80 ms to start, and one land runs
/// ~270 of them, most of them reads.
/// <para>
/// Only the success shape of each command is answered, with git's exact exit code and output. Every
/// other outcome (missing ref, lookup error, unusual argument or repository, any libgit2 exception)
/// returns null and the caller runs the real git command, so failures stay git's own. Shapes and
/// fallbacks are pinned against the real CLI by <c>LibGit2VsGitDifferentialTests</c>.
/// </para>
/// <list type="bullet">
/// <item>Config is never read: <c>remote get-url</c> and anything else whose answer depends on config
/// (includes, includeIf, worktree config, GIT_CONFIG_* environment, insteadOf) stays with git.</item>
/// <item>Paths are printed the way git prints them: the real path (on-disk case, links and subst
/// drives resolved, <see cref="GitRealPath"/>) with forward slashes on Windows. UNC paths fall back.</item>
/// <item>A repository with replace refs, grafts or a shallow file, or a reftable ref store, falls back
/// for every command: libgit2 does not apply them the way git does.</item>
/// <item>Each call opens its own handle and closes it before returning: a handle kept open goes stale
/// (shallow boundary, a worktree re-registered at the same path) and, on Windows, keeps pack files
/// from being deleted by another process's repack.</item>
/// </list>
/// <c>ANTIPHON_GIT_INPROCESS=off</c> turns it all off.
/// </summary>
internal static partial class InProcessGitReads
{
    public const string EnvVar = "ANTIPHON_GIT_INPROCESS";

    public static bool Enabled { get; } =
        !string.Equals(Environment.GetEnvironmentVariable(EnvVar)?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>git's own result for <paramref name="arguments"/> run in <paramref name="repository"/>, or null to run git.</summary>
    public static LandingGitResult? TryAnswer(string repository, IReadOnlyList<string> arguments)
    {
        // --no-optional-locks stops git refreshing (writing) the index during a read. Nothing answered
        // here writes anything, so the read is the same with or without it.
        if (arguments.Count > 0 && arguments[0] == "--no-optional-locks") arguments = arguments.Skip(1).ToArray();
        if (!Enabled || arguments.Count < 2) return null;
        // check-ref-format of a land ref we built ourselves needs no repository: every name of
        // this shape is valid under git-check-ref-format(1). Any other name is git's.
        if (arguments.Count == 2 && arguments[0] == "check-ref-format")
            return LandRefName.IsMatch(arguments[1]) ? Ok("") : null;
        // ls-remote of a local-path remote opens the remote, not this repository.
        if (arguments[0] == "ls-remote") return LsRemote(arguments);
        if (!Handled(arguments)) return null;
        try
        {
            // Opened through the working directory itself, never a discovered git dir: libgit2 opened
            // at a linked worktree's <common>/worktrees/<name> skips the common config. Only a
            // checkout root or a .git directory opens (git_repository_open does not search upward);
            // anything else throws RepositoryNotFoundException, caught below, and git answers. One
            // open per call: a separate Repository.IsValid probe would open the repository twice.
            using var repo = new Repository(repository);
            if (Unusual(repo)) return null;
            return Answer(repo, repository, arguments);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            return null;
        }
    }

    private static bool Handled(IReadOnlyList<string> a) => a[0] is "rev-parse" or "show-ref" or "symbolic-ref" or "merge-base"
            or "for-each-ref" or "diff-tree" or "ls-tree"
        || a is ["worktree", "list", "--porcelain", "-z"] or ["worktree", "list", "--porcelain"];

    /// <summary>
    /// Whether a git child started from <paramref name="probe"/> (after the runner's own set-up) reads the repository
    /// libgit2 opens: the plain <c>git</c> executable and no variable that redirects the repository, its objects, refs
    /// or discovery. Config differences (HOME, GIT_CONFIG_*) do not matter: no answered command reads config.
    /// </summary>
    internal static bool ChildReadsSameRepository(System.Diagnostics.ProcessStartInfo probe)
    {
        string[] redirects = ["GIT_DIR", "GIT_COMMON_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY",
            "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE", "GIT_CEILING_DIRECTORIES", "GIT_DISCOVERY_ACROSS_FILESYSTEM",
            "GIT_REPLACE_REF_BASE", "GIT_NO_REPLACE_OBJECTS", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE"];
        return probe.FileName == "git" && !redirects.Any(name =>
            !string.IsNullOrEmpty(probe.Environment.TryGetValue(name, out var v) ? v : Environment.GetEnvironmentVariable(name)));
    }

    /// <summary>
    /// Whether a git child in <paramref name="repository"/> would list the endpoint exactly as given. git rewrites a
    /// command-line URL with url.&lt;base&gt;.insteadOf and reads a remote of that name instead of the path; both come
    /// from the child's config, so any url.*, include or matching remote.* key there, or config injected through the
    /// environment, leaves the read to git. The config is libgit2's view of the repository (its own levels plus this
    /// process's global and system files, which the runners' children inherit).
    /// </summary>
    private static bool EndpointTakenLiterally(string repository, IReadOnlyList<string> arguments)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIT_CONFIG_PARAMETERS"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GIT_CONFIG"))) return false;
        var endpoint = arguments.Count > 3 && arguments[3] == "--" ? arguments.ElementAtOrDefault(4) : arguments.ElementAtOrDefault(3);
        if (endpoint is null) return false;
        try
        {
            using var repo = new Repository(repository);
            if (Unusual(repo) || ConfigOf(repo) is not { } view) return false;
            try
            {
                return !view.Config.Any(e => e.Key.StartsWith("url.", StringComparison.OrdinalIgnoreCase)
                    || e.Key.StartsWith("include", StringComparison.OrdinalIgnoreCase)
                    || e.Key.StartsWith("remote." + endpoint + ".", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (view.Owned) view.Config.Dispose();
            }
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            return false;
        }
    }

    /// <summary>
    /// For runners that hand git's stderr to their callers as well (<c>WorktreeManager</c>,
    /// <c>GitWorkspaceService</c>): git's exit code and output only where git writes
    /// nothing to stderr, that is a success, or the silent exit 1 of <c>merge-base --is-ancestor</c>,
    /// <c>symbolic-ref -q</c> and <c>show-ref --verify --quiet</c>. Null runs git.
    /// </summary>
    internal static (int ExitCode, string Output)? TryAnswerSilently(string repository, IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 0 && arguments[0] == "--no-optional-locks") arguments = arguments.Skip(1).ToArray();
        if (arguments.Count == 0) return null;
        // ls-remote exits 0, or 2 under --exit-code when nothing matches, without a word on stderr.
        if (arguments[0] == "ls-remote")
            return EndpointTakenLiterally(repository, arguments) && LsRemote(arguments) is { ExitCode: 0 or 2 } listed
                ? (listed.ExitCode, listed.Output) : null;
        // Both runners' children inherit this process's environment (plus gc/maintenance keys no status
        // reads), so the status config is checked against this process's own. Answers are all exit 0.
        if (arguments[0] is "status" or "ls-files")
            return InProcessGitStatus.TryAnswer(repository, arguments, null) is { } clean ? (clean.ExitCode, clean.Output) : null;
        var answer = TryAnswer(repository, arguments);
        if (answer is null) return null;
        var silent = answer.ExitCode == 0
            || answer.ExitCode == 1 && arguments is ["merge-base", "--is-ancestor", _, _] or ["symbolic-ref", "-q", _]
                or ["show-ref", "--verify", "--quiet", _];
        return silent ? (answer.ExitCode, answer.Output) : null;
    }

    /// <summary>
    /// The land's own refs: refs/antiphon/land/&lt;task&gt;/&lt;operation&gt; plus up to two segments, each a
    /// lowercase hyphenated word or a 32-hex id (source, target-before, prepared, remote-observed,
    /// source-observed/&lt;id&gt;, source-observed/probe, cleanup-observed, ...). No such name contains
    /// "..", "//", "@{", a leading dot, a ".lock" suffix or a character git refuses.
    /// </summary>
    internal static readonly System.Text.RegularExpressions.Regex LandRefName = new(
        "^refs/antiphon/land/[0-9a-f]{32}/[0-9a-f]{32}(/(?:[0-9a-f]{32}|[a-z]+(?:-[a-z]+)*)){0,2}$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Layouts whose answers libgit2 does not compute the way git does.</summary>
    internal static bool Unusual(Repository repo)
    {
        var gitDir = repo.Info.Path;
        var common = CommonDirectory(gitDir);
        return common is null
            || File.Exists(Path.Combine(common, "shallow")) || File.Exists(Path.Combine(gitDir, "shallow"))
            || File.Exists(Path.Combine(common, "info", "grafts"))
            || Directory.Exists(Path.Combine(common, "refs", "replace")) && Directory.EnumerateFileSystemEntries(Path.Combine(common, "refs", "replace")).Any()
            || HasPackedReplaceRefs(common)
            || Directory.Exists(Path.Combine(common, "reftable"));
    }

    private static bool HasPackedReplaceRefs(string common)
    {
        var packed = Path.Combine(common, "packed-refs");
        if (!File.Exists(packed)) return false;
        // Shared for delete/replace: git rewrites packed-refs by renaming over it, and a reader
        // without FileShare.Delete makes a concurrent `git branch -D` or pack-refs fail on Windows.
        using var stream = new FileStream(packed, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            if (line.Contains(" refs/replace/", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>The common dir for a git dir, per gitrepository-layout(5): its commondir file, else itself.</summary>
    /// <summary>
    /// The config git reads for <paramref name="repo"/>. LibGit2Sharp builds <c>Repository.Config</c> with
    /// <c>&lt;gitdir&gt;/config</c> as its local file and installs it into libgit2 on first use. For a linked worktree
    /// that is <c>&lt;common&gt;/worktrees/&lt;name&gt;/config</c>, which git never reads, so the repository's own
    /// settings drop out of every read, libgit2's status included once that config is installed. For a linked
    /// worktree this is a separate view over <c>&lt;common&gt;/config</c> plus the global, XDG and system levels, and
    /// <c>repo.Config</c> must then not be touched (libgit2 keeps its own, correct config). Null when per-worktree
    /// config (<c>config.worktree</c>) is in play: git's to merge. <c>Owned</c>: the caller disposes it.
    /// </summary>
    internal static (Configuration Config, bool Owned)? ConfigOf(Repository repo)
    {
        var gitDir = repo.Info.Path;
        if (CommonDirectory(gitDir) is not { } common) return null;
        if (GitText.PathsEqual(Path.GetFullPath(common).TrimEnd('\\', '/'), Path.GetFullPath(gitDir).TrimEnd('\\', '/')))
            return (repo.Config, false);
        if (File.Exists(Path.Combine(gitDir, "config.worktree")) || File.Exists(Path.Combine(common, "config.worktree"))) return null;
        var config = Configuration.BuildFrom(Path.Combine(common, "config"));
        if (config.Get<bool>("extensions.worktreeConfig")?.Value == true)
        {
            config.Dispose();
            return null;
        }
        return (config, true);
    }

    private static string? CommonDirectory(string gitDir)
    {
        var file = Path.Combine(gitDir, "commondir");
        if (!File.Exists(file)) return gitDir;
        var lines = File.ReadAllText(file).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        // Any other surrounding whitespace is git's to judge (it refuses "../.. "); GetFullPath would trim it.
        if (lines.Length != 1 || lines[0].Length == 0 || lines[0] != lines[0].Trim()) return null;
        return Path.GetFullPath(lines[0], gitDir);
    }

    private static LandingGitResult? Answer(Repository repo, string repository, IReadOnlyList<string> a)
    {
        switch (a[0])
        {
            // rev-parse --verify [--quiet] <rev>^{commit}  |  rev-parse HEAD  |  rev-parse --is-inside-work-tree
            case "rev-parse" when a.Count == 3 && a[1] == "--verify" && a[2].EndsWith("^{commit}", StringComparison.Ordinal):
                return Commit(repo, a[2][..^"^{commit}".Length], peel: true);
            case "rev-parse" when a.Count == 4 && a[1] == "--verify" && a[2] == "--quiet" && a[3].EndsWith("^{commit}", StringComparison.Ordinal):
                return Commit(repo, a[3][..^"^{commit}".Length], peel: true);
            case "rev-parse" when a is [_, "--is-inside-work-tree"]:
                return InsideWorkTree(repo, repository);
            // rev-parse --show-toplevel | rev-parse --path-format=absolute --git-dir, run at a checkout root: the same
            // checks as --is-inside-work-tree (a root of a non-bare checkout git cannot see moved or bared), then the
            // work tree's or the git dir's real path as git prints it.
            case "rev-parse" when a is [_, "--show-toplevel"]:
                return InsideWorkTree(repo, repository) is null ? null : PathAnswer(repo.Info.WorkingDirectory);
            case "rev-parse" when a is [_, "--path-format=absolute", "--git-dir"]:
                return InsideWorkTree(repo, repository) is null ? null : PathAnswer(repo.Info.Path);
            case "rev-parse" when a.Count == 2 && a[1] == "HEAD":
                return Commit(repo, "HEAD", peel: false);

            // Paths, printed as git prints them (real path, forward slashes on Windows).
            case "rev-parse" when a.Count == 2 && a[1] == "--absolute-git-dir":
                return PathAnswer(repo.Info.Path);
            case "rev-parse" when a.Count == 3 && a[1] == "--path-format=absolute" && a[2] == "--git-common-dir":
                return PathAnswer(CommonDirectory(repo.Info.Path));
            case "rev-parse" when a.Count == 4 && a[1] == "--path-format=absolute" && a[2] == "--git-path" && a[3] == "index.lock":
                return GitRealPath(repo.Info.Path) is { } dir ? Ok(dir + "/index.lock\n") : null;

            // show-ref --verify --quiet <ref>: 0 for a direct ref; the silent 1 only when no ref of that name exists
            // anywhere git would look (no packed or loose ref, not even a broken one).
            case "show-ref" when a.Count == 4 && a[1] == "--verify" && a[2] == "--quiet" && IsPlainRef(a[3]):
                if (Direct(repo, a[3]) is not null) return Ok("");
                return repo.Refs[a[3]] is null && !HasLooseRef(repo, a[3]) ? Exit(1) : null;

            // show-ref --verify --hash <ref>  |  show-ref --verify <ref>...  |  show-ref --exists <ref>
            case "show-ref" when a.Count == 4 && a[1] == "--verify" && a[2] == "--hash":
                return Direct(repo, a[3]) is { } sha ? Ok(sha + "\n") : null;
            case "show-ref" when a.Count >= 3 && a[1] == "--verify" && a.Skip(2).All(IsPlainRef):
            {
                var lines = new StringBuilder();
                foreach (var name in a.Skip(2))
                {
                    if (Direct(repo, name) is not { } refSha) return null;
                    lines.Append(refSha).Append(' ').Append(name).Append('\n');
                }
                return Ok(lines.ToString());
            }
            case "show-ref" when a.Count == 3 && a[1] == "--exists" && IsPlainRef(a[2]):
                return repo.Refs[a[2]] is null ? Exit(2) : Ok("");

            // symbolic-ref -q <ref>: 0 + target when it points at a direct or missing ref, 1 when the
            // ref itself is direct. A chain (target is itself symbolic) is git's: git follows it.
            case "symbolic-ref" when a.Count == 3 && a[1] == "-q" && (a[2] == "HEAD" || IsPlainRef(a[2])):
            {
                var found = repo.Refs[a[2]];
                if (found is DirectReference) return Exit(1);
                if (found is not SymbolicReference symbolic || !IsPlainRef(symbolic.TargetIdentifier)) return null;
                return repo.Refs[symbolic.TargetIdentifier] is SymbolicReference ? null : Ok(symbolic.TargetIdentifier + "\n");
            }

            // worktree list --porcelain [-z]: git's registration listing, from the files git reads.
            case "worktree" when a is ["worktree", "list", "--porcelain", "-z"]:
                return WorktreeList(repo, nul: true);
            case "worktree" when a is ["worktree", "list", "--porcelain"]:
                return WorktreeList(repo, nul: false);

            // merge-base --is-ancestor <oid> <oid>
            case "merge-base" when a.Count == 4 && a[1] == "--is-ancestor" && GitText.IsOid(a[2]) && GitText.IsOid(a[3]):
            {
                var ancestor = repo.Lookup<Commit>(a[2]);
                var descendant = repo.Lookup<Commit>(a[3]);
                if (ancestor is null || descendant is null) return null;
                if (ancestor.Sha == descendant.Sha) return Ok("");
                var mergeBase = repo.ObjectDatabase.FindMergeBase(ancestor, descendant);
                return mergeBase?.Sha == ancestor.Sha ? Ok("") : Exit(1);
            }

            case "for-each-ref" or "diff-tree" or "ls-tree":
                return MoreAnswer(repo, a);
        }
        return null;
    }

    /// <summary>
    /// <c>git worktree list --porcelain -z</c> as git builds it (builtin/worktree.c, worktree.c): the
    /// main worktree first (real path of the common dir without its "/.git"), then each linked
    /// worktree from <c>worktrees/&lt;id&gt;/gitdir</c>, sorted by path; HEAD from each worktree's own
    /// HEAD file; "locked [reason]" from <c>locked</c> (reason trimmed); "prunable" when the gitdir
    /// file points at nothing (list never expires). Every shape git would read through config,
    /// relative paths, unborn or chained HEADs, or a case-dependent order is left to git.
    /// Without <paramref name="nul"/> each field ends in a newline instead: git prints paths as they are and quotes
    /// only a lock reason (<c>write_name_quoted</c>), so a reason holding a tab, quote or backslash is git's.
    /// </summary>
    private static LandingGitResult? WorktreeList(Repository repo, bool nul)
    {
        var common = CommonDirectory(repo.Info.Path);
        if (common is null || Path.GetFileName(Path.TrimEndingDirectorySeparator(common)) != ".git" || MovesOrBaresMain(common))
            return null;
        var commonReal = GitRealPath(common);
        if (commonReal is null || !commonReal.EndsWith("/.git", StringComparison.Ordinal) || commonReal.Length <= "C:/.git".Length)
            return null;
        var output = new StringBuilder();
        output.Append("worktree ").Append(commonReal[..^"/.git".Length]).Append('\0');
        if (!AppendHead(output, repo, Path.Combine(common, "HEAD"))) return null;
        output.Append('\0');

        var linked = new List<(string Path, string Record)>();
        var registry = Path.Combine(common, "worktrees");
        if (Directory.Exists(registry))
        {
            if ((File.GetAttributes(registry) & FileAttributes.ReparsePoint) != 0) return null;
            foreach (var entry in Directory.EnumerateFileSystemEntries(registry))
            {
                // A stray file, link or unreadable entry is listed (or skipped) by rules this does not copy.
                if (!Directory.Exists(entry) || (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return null;
                var gitdirFile = Path.Combine(entry, "gitdir");
                if (!File.Exists(gitdirFile)) return null;
                var gitdir = File.ReadAllText(gitdirFile).TrimEnd(' ', '\t', '\n', '\r', '\v', '\f');
                if (!gitdir.EndsWith("/.git", StringComparison.Ordinal) || !IsAbsoluteGitPath(gitdir)) return null;
                var path = gitdir[..^"/.git".Length];
                var record = new StringBuilder();
                record.Append("worktree ").Append(path).Append('\0');
                if (!AppendHead(record, repo, Path.Combine(entry, "HEAD"))) return null;
                var lockFile = Path.Combine(entry, "locked");
                if (File.Exists(lockFile))
                {
                    var reason = File.ReadAllText(lockFile).Trim(' ', '\t', '\n', '\r', '\v', '\f');
                    if (reason.Any(c => c > 0x7e || (c < 0x20 && c != '\t'))) return null;
                    if (!nul && reason.Any(c => c is '\t' or '"' or '\\')) return null;
                    record.Append(reason.Length == 0 ? "locked" : "locked " + reason).Append('\0');
                }
                else if (!File.Exists(gitdir) && !Directory.Exists(gitdir))
                    record.Append("prunable gitdir file points to non-existent location").Append('\0');
                record.Append('\0');
                linked.Add((path, record.ToString()));
            }
        }
        // git sorts by fspathcmp, which folds case when core.ignorecase is set. Answer only when the
        // order is the same either way, and byte order equals UTF-16 order (ASCII paths).
        if (linked.Count > 1)
        {
            if (linked.Any(l => l.Path.Any(c => c > 0x7e))) return null;
            var ordinal = linked.OrderBy(l => l.Path, StringComparer.Ordinal).Select(l => l.Path).ToList();
            var folded = linked.OrderBy(l => l.Path, StringComparer.OrdinalIgnoreCase).Select(l => l.Path).ToList();
            if (!ordinal.SequenceEqual(folded) || folded.Distinct(StringComparer.OrdinalIgnoreCase).Count() != folded.Count) return null;
        }
        foreach (var (_, record) in linked.OrderBy(l => l.Path, StringComparer.Ordinal))
            output.Append(record);
        // No field holds a NUL or a newline: paths and reasons with control characters fell back above.
        return Ok(nul ? output.ToString() : output.Replace('\0', '\n').ToString());
    }

    /// <summary>"HEAD &lt;oid&gt;" then "branch &lt;ref&gt;" or "detached", from a HEAD file; false for anything else.</summary>
    private static bool AppendHead(StringBuilder output, Repository repo, string headFile)
    {
        if (!File.Exists(headFile)) return false;
        var text = File.ReadAllText(headFile);
        if (text.EndsWith('\n')) text = text[..^1];
        if (text.Contains('\n') || text.Contains('\r')) return false;
        if (GitText.IsOid(text))
        {
            if (repo.Lookup(text) is null) return false;
            output.Append("HEAD ").Append(text).Append('\0').Append("detached").Append('\0');
            return true;
        }
        if (!text.StartsWith("ref: refs/heads/", StringComparison.Ordinal)) return false;
        var target = text["ref: ".Length..];
        if (Direct(repo, target) is not { } sha) return false;   // unborn, symbolic or odd: git's
        output.Append("HEAD ").Append(sha).Append('\0').Append("branch ").Append(target).Append('\0');
        return true;
    }

    /// <summary>An absolute path in git's spelling: "X:/..." on Windows, "/..." elsewhere; no backslashes or controls.</summary>
    private static bool IsAbsoluteGitPath(string path)
    {
        if (path.Any(c => c < 0x20 || c == '\\')) return false;
        return OperatingSystem.IsWindows()
            ? path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/'
            : path.StartsWith('/');
    }

    private static readonly System.Text.RegularExpressions.Regex MovedOrBare = new(
        @"^\s*(worktree\s*=|bare\s*(=\s*(true|yes|on|1)\s*)?$)",
        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase
        | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Config that would move or bare the main worktree, or that this text check cannot follow:
    /// core.bare true, core.worktree, [include]/[includeIf], or per-worktree config.worktree.
    /// </summary>
    private static bool MovesOrBaresMain(string common)
    {
        if (File.Exists(Path.Combine(common, "config.worktree"))) return true;
        var config = Path.Combine(common, "config");
        if (!File.Exists(config)) return true;
        var text = File.ReadAllText(config);
        return text.Contains("[include", StringComparison.OrdinalIgnoreCase) || MovedOrBare.IsMatch(text);
    }

    /// <summary>
    /// rev-parse of HEAD, a full ref name, a full object id or (peeled only) a short name that
    /// <see cref="ShortName"/> resolves exactly as git would. Without <paramref name="peel"/> the object must itself
    /// be a commit (a detached HEAD at a tag is git's).
    /// </summary>
    private static LandingGitResult? Commit(Repository repo, string revision, bool peel)
    {
        if (!(revision == "HEAD" || IsPlainRef(revision) || GitText.IsOid(revision)))
            return peel && ShortName(repo, revision) is { } full ? Commit(repo, full, peel) : null;
        if (revision != "HEAD" && IsPlainRef(revision) && repo.Refs[revision] is null) return null;
        var target = repo.Lookup(revision);
        var commit = target as Commit ?? (peel ? target?.Peel<Commit>() : null);
        return commit is null ? null : Ok(commit.Sha + "\n");
    }

    /// <summary>
    /// The full name git's <c>ref_rev_parse_rules</c> give a short name (<c>&lt;name&gt;</c>, <c>refs/</c>,
    /// <c>refs/tags/</c>, <c>refs/heads/</c>, <c>refs/remotes/</c>, <c>refs/remotes/&lt;name&gt;/HEAD</c>), only when
    /// git can read it no other way: not hex (an abbreviated object id), not all capitals (a pseudo-ref), no
    /// <c>@</c>, not a per-worktree or other-worktree name, and nothing of that name in the git directories (the
    /// first rule, <c>$GIT_DIR/&lt;name&gt;</c>); exactly one rule finds a ref and it is direct (so no ambiguity
    /// warning and no symref to follow), and every other rule's name is absent from libgit2's view and from the
    /// loose ref files (so no broken-ref warning). Anything else is null and git answers.
    /// </summary>
    private static string? ShortName(Repository repo, string name)
    {
        if (name.Length == 0 || name.StartsWith("refs/", StringComparison.Ordinal) || name.Contains('@')
            || !IsPlainRef("refs/heads/" + name)
            || name.All(char.IsAsciiHexDigit) || name.All(c => c is (>= 'A' and <= 'Z') or '_')
            || name.Split('/')[0] is "bisect" or "worktree" or "rewritten" or "main-worktree" or "worktrees")
            return null;
        var gitDir = repo.Info.Path;
        var common = CommonDirectory(gitDir);
        if (common is null) return null;
        foreach (var dir in new[] { gitDir, common })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path) || Directory.Exists(path)) return null;
        }
        string? found = null;
        foreach (var candidate in new[] { "refs/" + name, "refs/tags/" + name, "refs/heads/" + name, "refs/remotes/" + name, "refs/remotes/" + name + "/HEAD" })
        {
            var reference = repo.Refs[candidate];
            if (reference is null)
            {
                if (HasLooseRef(repo, candidate)) return null;
                continue;
            }
            if (reference is not DirectReference || found is not null) return null;
            found = candidate;
        }
        return found;
    }

    /// <summary>Whether a loose ref file of that name exists in the git directory or the common directory.</summary>
    private static bool HasLooseRef(Repository repo, string name)
    {
        var gitDir = repo.Info.Path;
        var common = CommonDirectory(gitDir);
        return common is null || File.Exists(Path.Combine(gitDir, name)) || File.Exists(Path.Combine(common, name));
    }

    /// <summary>
    /// <c>rev-parse --is-inside-work-tree</c> run at the root of a non-bare checkout: "true". Anywhere else (the git
    /// directory, a bare repository) and any config that could move or bare the work tree is git's.
    /// </summary>
    private static LandingGitResult? InsideWorkTree(Repository repo, string repository)
    {
        if (repo.Info.IsBare || repo.Info.WorkingDirectory is not { } work) return null;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(work)), comparison))
            return null;
        var common = CommonDirectory(repo.Info.Path);
        if (common is null || MovesOrBaresMain(common) || File.Exists(Path.Combine(repo.Info.Path, "config.worktree"))) return null;
        return Ok("true\n");
    }

    /// <summary>The commit id a direct, existing ref points at; null for symbolic or missing refs.</summary>
    private static string? Direct(Repository repo, string name) =>
        IsPlainRef(name) && repo.Refs[name] is DirectReference direct && GitText.IsOid(direct.TargetIdentifier)
            ? direct.TargetIdentifier : null;

    /// <summary>A full ref name git would accept as written (libgit2 normalises some it would refuse).</summary>
    private static bool IsPlainRef(string name)
    {
        if (!name.StartsWith("refs/", StringComparison.Ordinal) || name.EndsWith('/') || name.EndsWith(".lock", StringComparison.Ordinal))
            return false;
        if (name.Contains("//") || name.Contains("..") || name.Contains("@{") || name.Contains("/.")) return false;
        foreach (var c in name)
            if (c < 0x20 || c == 0x7f || c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or '\\') return false;
        return true;
    }

    private static LandingGitResult? PathAnswer(string? path) => path is not null && GitRealPath(path) is { } real ? Ok(real + "\n") : null;

    /// <summary>
    /// An existing directory's real path the way git prints it: on-disk case, junctions/symlinks and
    /// subst drives resolved, no trailing separator, forward slashes on Windows. Null for UNC paths
    /// or anything that cannot be resolved.
    /// </summary>
    internal static string? GitRealPath(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(trimmed)) return null;
        if (!OperatingSystem.IsWindows())
            return RealPathUnix(trimmed);
        var final = FinalPathWindows(trimmed);
        if (final is null) return null;
        if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return null;
        if (final.StartsWith(@"\\?\", StringComparison.Ordinal)) final = final[4..];
        if (final.Length < 2 || final[1] != ':' || final.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        final = char.ToUpperInvariant(final[0]) + final[1..];
        return Path.TrimEndingDirectorySeparator(final).Replace('\\', '/');
    }

    private static string? FinalPathWindows(string path)
    {
        const uint FileReadAttributes = 0x80, ShareAll = 0x7, OpenExisting = 3, BackupSemantics = 0x02000000;
        using var handle = CreateFileW(path, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0 /* FILE_NAME_NORMALIZED | VOLUME_NAME_DOS */);
        if (length == 0 || length >= buffer.Capacity) return null;
        return buffer.ToString(0, (int)length);
    }

    private static string? RealPathUnix(string path)
    {
        var resolved = realpath(path, IntPtr.Zero);
        if (resolved == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(resolved); }
        finally { free(resolved); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr realpath(string path, IntPtr resolved);

    [DllImport("libc")]
    private static extern void free(IntPtr pointer);

    private static LandingGitResult Ok(string output) => new(0, output, "");

    /// <summary>A non-zero answer carries the same diagnostic the process path sets for that exit code.</summary>
    private static LandingGitResult Exit(int code) => new(code, "", $"git_exit_{code}");
}
