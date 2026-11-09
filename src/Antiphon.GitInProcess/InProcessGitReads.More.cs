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
/// <item><c>for-each-ref --format=…</c> with literal prefix patterns, sorted by name as git sorts them.</item>
/// <item><c>diff-tree --no-commit-id --raw -r -z --no-abbrev --no-ext-diff --no-textconv --find-renames A B --</c>
/// when no rename can be found (no path is both added and deleted), from a walk of the two trees.</item>
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
        _ => null,
    };

    private static LandingGitResult? ForEachRef(Repository repo, IReadOnlyList<string> a)
    {
        if (a.Count < 3) return null;
        var nameFirst = a[1] == "--format=%(refname) %(objectname)";
        if (!nameFirst && a[1] != "--format=%(objectname) %(refname)") return null;
        var patterns = a.Skip(2).ToList();
        if (patterns.Any(p => p.Length == 0 || p.StartsWith('-') || p.IndexOfAny(GlobCharacters) >= 0)) return null;
        var lines = new List<(string Name, string Sha)>();
        foreach (var reference in repo.Refs)
        {
            var name = reference.CanonicalName;
            if (!name.StartsWith("refs/", StringComparison.Ordinal)) continue;
            // A literal pattern matches the whole name or a prefix ending at a '/'.
            if (!patterns.Any(p => name == p || name.StartsWith(p, StringComparison.Ordinal)
                    && (p.EndsWith('/') || name[p.Length] == '/'))) continue;
            // Per-worktree refs, and refs git skips as broken (a missing object, a dangling symref), are git's.
            if (name.StartsWith("refs/worktree/", StringComparison.Ordinal) || name.StartsWith("refs/bisect/", StringComparison.Ordinal)
                || name.StartsWith("refs/rewritten/", StringComparison.Ordinal)) return null;
            if (reference.ResolveToDirectReference()?.TargetIdentifier is not { } sha || !GitText.IsOid(sha)
                || !repo.ObjectDatabase.Contains(new ObjectId(sha))) return null;
            lines.Add((name, sha));
        }
        var output = new StringBuilder();
        foreach (var (name, sha) in lines.OrderBy(l => l.Name, StringComparer.Ordinal))
            output.Append(nameFirst ? name : sha).Append(' ').Append(nameFirst ? sha : name).Append('\n');
        return Ok(output.ToString());
    }

    private static LandingGitResult? DiffTree(Repository repo, IReadOnlyList<string> a)
    {
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

    /// <summary>A recursive two-tree walk in git's raw form. False for anything git reports differently from a plain
    /// add/delete/modify of files: a file replaced by a directory (or back), a type change, a submodule, a name that
    /// is not valid UTF-8.</summary>
    private static bool Diff(Tree? left, Tree? right, string prefix, List<(string, string)> changes)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        if (left is not null) foreach (var e in left) names.Add(e.Name);
        if (right is not null) foreach (var e in right) names.Add(e.Name);
        foreach (var name in names)
        {
            if (name.Contains('�')) return false;
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

    private static Tree? TreeOf(Repository repo, string sha) => repo.Lookup(sha) switch
    {
        Commit commit => commit.Tree,
        Tree tree => tree,
        _ => null,
    };
}
