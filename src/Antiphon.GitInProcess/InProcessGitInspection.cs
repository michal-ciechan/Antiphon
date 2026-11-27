using System.Security.Cryptography;
using System.Text;
using LibGit2Sharp;

namespace Antiphon.GitInProcess;

/// <summary>What <see cref="InProcessGitInspection.Containment"/> found for the commits a source has beyond a target.</summary>
internal enum PatchContainment
{
    /// <summary>Every source commit beyond the target has a patch-equivalent commit on the target (or there are none).</summary>
    Contained,
    /// <summary>At least one source commit's change is not on the target.</summary>
    NotContained,
    /// <summary>The source side has a merge commit, which patch comparison cannot judge.</summary>
    HasMerge,
}

/// <summary>
/// Answers to the questions the worktree-base inspection asks, computed with libgit2 instead of a set of git
/// commands. They are the answers the callers need, not git's output. Null means "ask git": an unusual repository,
/// a missing object, a binary change, a side too long to compare, or any libgit2 error.
/// </summary>
internal static class InProcessGitInspection
{
    /// <summary>More commits than this on either side goes to git: libgit2 runs in the caller's thread, under its deadline.</summary>
    internal const int MaxSideCommits = 500;

    /// <summary>
    /// What <c>git rev-list --max-count=1 --min-parents=2 target..source</c> then <c>git cherry target source</c>
    /// decide together: a merge beyond the target, or whether each non-merge commit beyond the target has a
    /// patch-equivalent commit on the target's side (the commits the target has and the source lacks, merges
    /// excluded, as git cherry excludes them). The patch id is this method's own: the per-file change lines with
    /// whitespace removed, so it agrees with itself, not with <c>git patch-id</c> byte for byte.
    /// <para>
    /// The source's few commits are hashed first; a target-side commit is hashed only when it changes exactly the same
    /// paths as one of them (an equal patch must), so a target far ahead costs a tree comparison per commit, not a
    /// patch. <paramref name="ct"/> is checked before every commit.
    /// </para>
    /// </summary>
    internal static PatchContainment? Containment(string repository, string target, string source,
        CancellationToken ct = default)
    {
        if (!InProcessGitReads.Enabled) return null;
        try
        {
            using var repo = new Repository(repository);
            if (InProcessGitReads.Unusual(repo)) return null;
            if (CommitOf(repo, target) is not { } upstream || CommitOf(repo, source) is not { } head) return null;
            var mine = Side(repo, head, upstream, ct);
            if (mine is null) return null;
            if (mine.Any(c => c.Parents.Count() >= 2)) return PatchContainment.HasMerge;
            if (mine.Count == 0) return PatchContainment.Contained;
            var needed = new HashSet<string>(StringComparer.Ordinal);
            var shapes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var commit in mine)
            {
                ct.ThrowIfCancellationRequested();
                if (PatchId(repo, commit) is not { } id) return null;
                needed.Add(id);
                shapes.Add(PathsChanged(repo, commit));
            }
            var seen = 0;
            foreach (var commit in Walk(repo, upstream, head))
            {
                ct.ThrowIfCancellationRequested();
                if (++seen > MaxSideCommits) return null;
                if (commit.Parents.Count() >= 2 || !shapes.Contains(PathsChanged(repo, commit))) continue;
                if (PatchId(repo, commit) is not { } id) return null;
                if (needed.Remove(id) && needed.Count == 0) return PatchContainment.Contained;
            }
            return PatchContainment.NotContained;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a merge, rebase or <c>git am</c> is in progress in the checkout at <paramref name="checkout"/>: its own
    /// git directory holds MERGE_HEAD, rebase-merge or rebase-apply (the three paths the inspection asked
    /// <c>git rev-parse --git-path</c> for). Null when the checkout does not open.
    /// </summary>
    internal static bool? OperationInProgress(string checkout)
    {
        if (!InProcessGitReads.Enabled) return null;
        try
        {
            using var repo = new Repository(checkout);
            var gitDir = repo.Info.Path;
            return new[] { "MERGE_HEAD", "rebase-merge", "rebase-apply" }
                .Any(name => File.Exists(Path.Combine(gitDir, name)) || Directory.Exists(Path.Combine(gitDir, name)));
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            return null;
        }
    }

    private static Commit? CommitOf(Repository repo, string spec) =>
        GitText.IsOid(spec) ? repo.Lookup<Commit>(new ObjectId(spec)) : repo.Lookup(spec)?.Peel<Commit>();

    /// <summary>The commits reachable from <paramref name="from"/> and not from <paramref name="hide"/>, lazily.</summary>
    private static IEnumerable<Commit> Walk(Repository repo, Commit from, Commit hide) =>
        repo.Commits.QueryBy(new CommitFilter
        {
            IncludeReachableFrom = from, ExcludeReachableFrom = hide, SortBy = CommitSortStrategies.None,
        });

    /// <summary>The same walk collected, or null past the bound.</summary>
    private static List<Commit>? Side(Repository repo, Commit from, Commit hide, CancellationToken ct)
    {
        var commits = new List<Commit>();
        foreach (var commit in Walk(repo, from, hide))
        {
            ct.ThrowIfCancellationRequested();
            if (commits.Count == MaxSideCommits) return null;
            commits.Add(commit);
        }
        return commits;
    }

    /// <summary>The (old, new) paths a commit changes against its first parent, sorted: equal for equal patches.</summary>
    private static string PathsChanged(Repository repo, Commit commit)
    {
        using var changes = repo.Diff.Compare<TreeChanges>(commit.Parents.FirstOrDefault()?.Tree, commit.Tree);
        return string.Join('\n', changes.Select(c => c.OldPath + "\0" + c.Path).Order(StringComparer.Ordinal));
    }

    /// <summary>A hash of the commit's change against its one parent (or the empty tree), or null for a binary change.</summary>
    private static string? PatchId(Repository repo, Commit commit)
    {
        var parent = commit.Parents.FirstOrDefault()?.Tree;
        using var patch = repo.Diff.Compare<Patch>(parent, commit.Tree, new CompareOptions { ContextLines = 3 });
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        foreach (var change in patch.OrderBy(c => c.Path, StringComparer.Ordinal))
        {
            if (change.IsBinaryComparison) return null;
            Append(hash, change.OldPath + "\0" + change.Path + "\0");
            foreach (var line in change.Patch.Split('\n'))
            {
                if (line.StartsWith("diff --git ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal)
                    || line.StartsWith("@@", StringComparison.Ordinal)) continue;
                Append(hash, string.Concat(line.Where(c => !char.IsWhiteSpace(c))) + "\n");
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string text) => hash.AppendData(Encoding.UTF8.GetBytes(text));
}
