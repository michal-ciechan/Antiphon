using System.Text;
using Antiphon.Server.Application.Dtos;
using LibGit2Sharp;

namespace Antiphon.GitInProcess;

/// <summary>
/// More read-only commands answered in process (same contract as the rest of <see cref="InProcessGitReads"/>: git's
/// exact output for the success shape, null for anything else so git runs):
/// <list type="bullet">
/// <item><c>ls-remote --refs --exit-code &lt;local path&gt; &lt;ref&gt;...</c>: the refs a local-path remote advertises
/// (what <c>git-upload-pack</c> would list), tail-matched as ls-remote matches them. URL remotes stay with git
/// (credential helpers, transports, proxies), as do glob patterns and remotes that hide refs.</item>
/// <item><c>for-each-ref --format=…</c> (refname and objectname in either order, refname alone, or refname, objectname
/// and symref) with literal prefix patterns or none, sorted by name as git sorts them.</item>
/// <item><c>diff-tree --no-commit-id --raw -r -z --no-abbrev --no-ext-diff --no-textconv --find-renames A B --</c>
/// when no rename can be found (no path is both added and deleted), from a walk of the two trees.</item>
/// <item><c>diff-tree --root --no-commit-id --name-only --no-renames -r -z &lt;commit oid&gt;</c> for a root or
/// one-parent commit, from the same walk.</item>
/// <item><c>ls-tree -r -z [--full-tree] &lt;commit or tree oid&gt;</c>, from a walk of the tree.</item>
/// </list>
/// </summary>
internal static partial class InProcessGitReads
{
    private static readonly char[] GlobCharacters = ['*', '?', '[', '\\'];

    private static LandingGitResult? LsRemote(IReadOnlyList<string> a)
    {
        if (a.Count < 5 || a[1] != "--refs" || a[2] != "--exit-code") return null;
        // An optional "--" only ends the options: ls-remote --refs --exit-code -- <endpoint> <ref>... is the same read.
        var at = a[3] == "--" ? 4 : 3;
        if (a.Count < at + 2) return null;
        var endpoint = a[at];
        var patterns = a.Skip(at + 1).ToList();
        if (patterns.Any(p => p.Length == 0 || p.StartsWith('-') || p.IndexOfAny(GlobCharacters) >= 0)) return null;
        if (endpoint.Contains("://", StringComparison.Ordinal) || !Path.IsPathFullyQualified(endpoint)
            || !Directory.Exists(endpoint))
            return null;
        try
        {
            using var remote = new Repository(endpoint);
            if (Unusual(remote) || HidesRefs(remote)) return null;
            var lines = new List<(string Name, string Sha)>();
            foreach (var reference in remote.Refs)
            {
                var name = reference.CanonicalName;
                if (!name.StartsWith("refs/", StringComparison.Ordinal)) continue;
                // ls-remote matches "/<ref>" against "*/<pattern>": the pattern is the whole name or a tail after a '/'.
                if (!patterns.Any(p => name == p || name.EndsWith("/" + p, StringComparison.Ordinal))) continue;
                if (reference.ResolveToDirectReference()?.TargetIdentifier is not { } sha || !GitText.IsOid(sha)) return null;
                lines.Add((name, sha));
            }
            if (lines.Count == 0) return Exit(2);
            var output = new StringBuilder();
            foreach (var (name, sha) in lines.OrderBy(l => l.Name, StringComparer.Ordinal))
                output.Append(sha).Append('\t').Append(name).Append('\n');
            return Ok(output.ToString());
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            return null;
        }
    }

    /// <summary>uploadpack.hideRefs / transfer.hideRefs change what the remote advertises: git's to apply.</summary>
    private static bool HidesRefs(Repository remote) =>
        remote.Config.Any(e => e.Key.Equals("uploadpack.hiderefs", StringComparison.OrdinalIgnoreCase)
            || e.Key.Equals("transfer.hiderefs", StringComparison.OrdinalIgnoreCase));

    private static LandingGitResult? MoreAnswer(Repository repo, IReadOnlyList<string> a) => a[0] switch
    {
        "for-each-ref" => ForEachRef(repo, a),
        "diff-tree" => DiffTree(repo, a),
        "ls-tree" => LsTree(repo, a),
        "cat-file" => CatFileExists(repo, a),
        _ => null,
    };

    /// <summary>
    /// <c>cat-file -e &lt;full id&gt;</c> and <c>cat-file -e &lt;full id&gt;^{commit}</c> for an object that is present
    /// (and, with the peel, is or peels to a commit): exit 0, no output. Anything else (an absent object, which a
    /// partial clone may lazily fetch; one that does not peel; an abbreviated id or a ref name) is git's.
    /// </summary>
    private static LandingGitResult? CatFileExists(Repository repo, IReadOnlyList<string> a)
    {
        if (a is not ["cat-file", "-e", var spec]) return null;
        var peel = spec.EndsWith("^{commit}", StringComparison.Ordinal);
        var id = peel ? spec[..^"^{commit}".Length] : spec;
        if (!GitText.IsOid(id)) return null;
        var target = repo.Lookup(id);
        if (target is null) return null;
        var peeled = target;
        while (peel && peeled is TagAnnotation tag) peeled = tag.Target;
        if (peel && peeled is not LibGit2Sharp.Commit) return null;
        return Ok("");
    }

    private static LandingGitResult? ForEachRef(Repository repo, IReadOnlyList<string> a)
    {
        if (a.Count < 2) return null;
        var format = a[1] switch
        {
            "--format=%(refname) %(objectname)" => RefFormat.NameSha,
            "--format=%(objectname) %(refname)" => RefFormat.ShaName,
            "--format=%(refname)" => RefFormat.Name,
            "--format=%(refname) %(objectname) %(symref)" => RefFormat.NameShaSymref,
            _ => (RefFormat?)null,
        };
        if (format is not { } shape) return null;
        // No pattern lists every ref under refs/.
        var patterns = a.Skip(2).ToList();
        if (patterns.Any(p => p.Length == 0 || p.StartsWith('-') || p.IndexOfAny(GlobCharacters) >= 0)) return null;
        var lines = new List<(string Name, string Sha, string Symref)>();
        // Two names that differ only in case (a loose Foo beside a packed foo): where the file system ignores case,
        // libgit2 reads one through the other's loose file while git's iterator keeps both values apart. Anywhere
        // in the repository, as a loose name outside the pattern still shadows a packed one inside it.
        var seen = RefsIgnoreCase(repo.Info.Path) ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
        foreach (var reference in repo.Refs)
        {
            var name = reference.CanonicalName;
            if (seen is not null && !seen.Add(name)) return null;
            if (!name.StartsWith("refs/", StringComparison.Ordinal)) continue;
            // A literal pattern matches the whole name or a prefix ending at a '/'.
            if (patterns.Count > 0 && !patterns.Any(p => name == p || name.StartsWith(p, StringComparison.Ordinal)
                    && (p.EndsWith('/') || name[p.Length] == '/'))) continue;
            // git sorts bytes; ordinal UTF-16 order is the same only for ASCII names.
            if (name.Any(c => c > 0x7f)) return null;
            // Per-worktree refs, and refs git skips as broken (a missing object, a dangling symref), are git's.
            if (name.StartsWith("refs/worktree/", StringComparison.Ordinal) || name.StartsWith("refs/bisect/", StringComparison.Ordinal)
                || name.StartsWith("refs/rewritten/", StringComparison.Ordinal)) return null;
            if (reference.ResolveToDirectReference()?.TargetIdentifier is not { } sha || !GitText.IsOid(sha)
                || !repo.ObjectDatabase.Contains(new ObjectId(sha))) return null;
            // %(symref): a symbolic ref's own target, here only one that is itself a direct ref (no chain).
            var symref = "";
            if (reference is SymbolicReference symbolic)
            {
                if (shape == RefFormat.NameShaSymref && (!IsPlainRef(symbolic.TargetIdentifier)
                        || repo.Refs[symbolic.TargetIdentifier] is not DirectReference))
                    return null;
                symref = symbolic.TargetIdentifier;
            }
            lines.Add((name, sha, symref));
        }
        var output = new StringBuilder();
        foreach (var (name, sha, symref) in lines.OrderBy(l => l.Name, StringComparer.Ordinal))
        {
            switch (shape)
            {
                case RefFormat.NameSha: output.Append(name).Append(' ').Append(sha); break;
                case RefFormat.ShaName: output.Append(sha).Append(' ').Append(name); break;
                case RefFormat.Name: output.Append(name); break;
                default: output.Append(name).Append(' ').Append(sha).Append(' ').Append(symref); break;
            }
            output.Append('\n');
        }
        return Ok(output.ToString());
    }

    private enum RefFormat { NameSha, ShaName, Name, NameShaSymref }

    // Whether the folder that holds the refs ignores case: the common dir's refs found as REFS. Probed there, not
    // on the whole path, as a case-insensitive mount can sit under a case-sensitive root (WSL /mnt/c, a bind mount,
    // a casefold folder). An unreadable common dir counts as ignoring case, which only sends more listings to git.
    private static bool RefsIgnoreCase(string gitDir)
    {
        if (CommonDirectory(gitDir) is not { } common) return true;
        return Directory.Exists(Path.Combine(common, "refs")) && Directory.Exists(Path.Combine(common, "REFS"));
    }

    private static LandingGitResult? DiffTree(Repository repo, IReadOnlyList<string> a)
    {
        if (a is ["diff-tree", "--root", "--no-commit-id", "--name-only", "--no-renames", "-r", "-z", var single]
            && GitText.IsOid(single))
            return CommitPaths(repo, single);
        if (a is not ["diff-tree", "--no-commit-id", "--raw", "-r", "-z", "--no-abbrev", "--no-ext-diff", "--no-textconv",
                "--find-renames", var from, var to, "--"]
            || !GitText.IsOid(from) || !GitText.IsOid(to))
            return null;
        if (TreeOf(repo, from) is not { } left || TreeOf(repo, to) is not { } right) return null;
        var changes = new List<(string Path, string Line)>();
        if (!Diff(left, right, "", changes)) return null;
        // --find-renames pairs an added path with a deleted one: with both present, git decides.
        var statuses = changes.Select(c => c.Line[^1]).ToHashSet();
        if (statuses.Contains('A') && statuses.Contains('D')) return null;
        var output = new StringBuilder();
        foreach (var (path, line) in changes.OrderBy(c => c.Path, StringComparer.Ordinal))
            output.Append(line).Append('\0').Append(path).Append('\0');
        return Ok(output.ToString());
    }

    /// <summary>
    /// <c>diff-tree --root --no-commit-id --name-only --no-renames -r -z &lt;full oid&gt;</c>: the paths a commit changes
    /// against its one parent, or every path of a root commit, NUL-terminated in git's order. A merge (plumbing
    /// diff-tree shows none without -m, -c or --cc; left to git all the same), anything but a commit (git's error),
    /// an id that is not a full SHA-1 one, and every shape <see cref="Diff"/> refuses are git's.
    /// </summary>
    private static LandingGitResult? CommitPaths(Repository repo, string sha)
    {
        if (sha.Length != 40 || ById(repo, sha) is not Commit commit) return null;
        var parents = commit.Parents.Take(2).ToList();
        if (parents.Count > 1) return null;
        var changes = new List<(string Path, string Line)>();
        if (!Diff(parents.Count == 1 ? parents[0].Tree : null, commit.Tree, "", changes)) return null;
        var output = new StringBuilder();
        foreach (var path in changes.Select(c => c.Path).OrderBy(p => p, StringComparer.Ordinal))
            output.Append(path).Append('\0');
        return Ok(output.ToString());
    }

    /// <summary>A recursive two-tree walk in git's raw form. False for anything git reports differently from a plain
    /// add/delete/modify of files (a file replaced by a directory or back, a type change, a submodule) and for any
    /// tree that is not <see cref="Plain"/> (not canonical, out of tree order, a name twice, a non-ASCII name).</summary>
    private static bool Diff(Tree? left, Tree? right, string prefix, List<(string, string)> changes)
    {
        // Both sides as git stores them (canonical bytes, tree order, no repeated name) with ASCII names, so a name
        // lookup finds git's entry and ordinal order is git's byte order.
        if (left is not null && !Plain(left) || right is not null && !Plain(right)) return false;
        var names = new SortedSet<string>(StringComparer.Ordinal);
        if (left is not null) foreach (var e in left) names.Add(e.Name);
        if (right is not null) foreach (var e in right) names.Add(e.Name);
        foreach (var name in names)
        {
            var l = left?[name];
            var r = right?[name];
            if (l?.Mode == Mode.GitLink || r?.Mode == Mode.GitLink) return false;
            var path = prefix + name;
            var lTree = l?.TargetType == TreeEntryTargetType.Tree;
            var rTree = r?.TargetType == TreeEntryTargetType.Tree;
            if (l is not null && r is not null)
            {
                if (lTree != rTree) return false;
                if (l.Target.Sha == r.Target.Sha && l.Mode == r.Mode) continue;
                if (lTree) { if (!Diff((Tree)l.Target, (Tree)r.Target, path + "/", changes)) return false; continue; }
                if ((l.Mode == Mode.SymbolicLink) != (r.Mode == Mode.SymbolicLink)) return false; // git: T
                changes.Add((path, Raw(l, r, 'M')));
            }
            else if (l is not null)
            {
                if (lTree) { if (!Diff((Tree)l.Target, null, path + "/", changes)) return false; continue; }
                changes.Add((path, Raw(l, null, 'D')));
            }
            else
            {
                if (rTree) { if (!Diff(null, (Tree)r!.Target, path + "/", changes)) return false; continue; }
                changes.Add((path, Raw(null, r, 'A')));
            }
        }
        return true;
    }

    /// <summary>
    /// A tree whose stored bytes are what libgit2's entries serialise to (<see cref="Canonical"/>), whose entries are
    /// strictly in git's tree order (a tree's name compared as if it ended in '/') with no name twice, and whose
    /// names are ASCII.
    /// </summary>
    private static bool Plain(Tree tree)
    {
        if (!Canonical(tree)) return false;
        string? previous = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in tree)
        {
            if (entry.Name.Any(c => c > 0x7F) || !seen.Add(entry.Name)) return false;
            var key = entry.TargetType == TreeEntryTargetType.Tree ? entry.Name + "/" : entry.Name;
            if (previous is not null && string.CompareOrdinal(previous, key) >= 0) return false;
            previous = key;
        }
        return true;
    }

    private static string Raw(TreeEntry? from, TreeEntry? to, char status) =>
        $":{ModeText(from)} {ModeText(to)} {from?.Target.Sha ?? Zero} {to?.Target.Sha ?? Zero} {status}";

    private static readonly string Zero = new('0', 40);

    private static string ModeText(TreeEntry? entry) => entry is null ? "000000" : Convert.ToString((int)entry.Mode, 8).PadLeft(6, '0');

    /// <summary>
    /// <c>ls-tree -r -z [--full-tree] &lt;full oid&gt;</c>. Without --full-tree git lists only the part of the tree under
    /// the directory it runs in; this opens only a checkout root or a git directory (never a subdirectory), where
    /// both forms list the whole tree.
    /// </summary>
    private static LandingGitResult? LsTree(Repository repo, IReadOnlyList<string> a)
    {
        var rev = a switch
        {
            ["ls-tree", "-r", "-z", var plain] => plain,
            ["ls-tree", "-r", "-z", "--full-tree", var full] => full,
            _ => null,
        };
        if (rev is null || !GitText.IsOid(rev) || rev.Length != 40 || TreeOf(repo, rev) is not { } tree) return null;
        var output = new StringBuilder();
        return Walk(tree, "", output) ? Ok(output.ToString()) : null;
    }

    /// <summary>
    /// ls-tree -r: blobs, links and submodules in tree order (a tree's stored order is git's). False for a name
    /// that is not valid UTF-8 or holds a control character, and for a tree whose stored bytes are not exactly
    /// what its entries, as libgit2 reports them, serialise to (<see cref="Canonical"/>).
    /// </summary>
    private static bool Walk(Tree tree, string prefix, StringBuilder output)
    {
        if (!Canonical(tree)) return false;
        foreach (var entry in tree)
        {
            if (entry.Name.Any(c => c == '�' || c < 0x20 || c == 0x7f)) return false;
            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                if (!Walk((Tree)entry.Target, prefix + entry.Name + "/", output)) return false;
                continue;
            }
            var type = entry.Mode == Mode.GitLink ? "commit" : "blob";
            output.Append(ModeText(entry)).Append(' ').Append(type).Append(' ').Append(entry.Target.Sha)
                .Append('\t').Append(prefix).Append(entry.Name).Append('\0');
        }
        return true;
    }

    /// <summary>
    /// Whether the tree object's id is the SHA-1 of its entries written back in git's canonical form ("mode name\0"
    /// then the raw id; modes 40000, 100644, 100755, 120000, 160000) in the order libgit2 lists them. libgit2
    /// normalises a stored mode (100664, a zero-padded 040000) by rules that differ from git's in corners (a
    /// group-only exec bit), and decodes names leniently; a tree that round-trips was stored canonically, so
    /// git prints exactly what libgit2 reports. Anything else is git's to print.
    /// </summary>
    private static bool Canonical(Tree tree)
    {
        if (tree.Sha.Length != 40) return false;
        using var content = new MemoryStream();
        foreach (var entry in tree)
        {
            var mode = entry.Mode switch
            {
                Mode.Directory => "40000",
                Mode.NonExecutableFile => "100644",
                Mode.ExecutableFile => "100755",
                Mode.SymbolicLink => "120000",
                Mode.GitLink => "160000",
                _ => null,
            };
            if (mode is null) return false;
            content.Write(Encoding.ASCII.GetBytes(mode + " "));
            content.Write(Encoding.UTF8.GetBytes(entry.Name));
            content.WriteByte(0);
            content.Write(Convert.FromHexString(entry.Target.Sha));
        }
        var header = Encoding.ASCII.GetBytes($"tree {content.Length}\0");
        using var sha1 = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA1);
        sha1.AppendData(header);
        sha1.AppendData(content.GetBuffer(), 0, (int)content.Length);
        return Convert.ToHexString(sha1.GetHashAndReset()).Equals(tree.Sha, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The object a full SHA-1 id names, read as an id, never as revision syntax: null for any other length, and
    /// when a ref reads as that hex name (git still reads the hex as the id, but warns "refname ... is ambiguous"
    /// on stderr first).
    /// </summary>
    private static GitObject? ById(Repository repo, string sha)
    {
        if (sha.Length != 40) return null;
        // git's first rule is the file $GIT_DIR/<name> (libgit2's Refs[hex] reads the hex as an object id instead).
        if (HasLooseRef(repo, sha)) return null;
        foreach (var name in new[] { "refs/" + sha, "refs/tags/" + sha, "refs/heads/" + sha, "refs/remotes/" + sha, "refs/remotes/" + sha + "/HEAD" })
            if (repo.Refs[name] is not null || HasLooseRef(repo, name)) return null;
        return repo.Lookup(new ObjectId(sha));
    }

    private static Tree? TreeOf(Repository repo, string sha) => ById(repo, sha) switch
    {
        Commit commit => commit.Tree,
        Tree tree => tree,
        _ => null,
    };
}
