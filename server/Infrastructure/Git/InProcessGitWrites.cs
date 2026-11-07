using Antiphon.GitInProcess;
using Antiphon.Server.Application.Dtos;
using LibGit2Sharp;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// The land's own small writes, answered in process (each ~70–200 ms as a git process):
/// <list type="bullet">
/// <item>the create-only observation pin <c>update-ref -m "antiphon observe &lt;ref&gt;" &lt;pin&gt; &lt;id&gt; &lt;zero id&gt;</c>
/// that every remote observation makes when the advertised commit is already local (five to seven per land);</item>
/// <item>the create-only recovery pin <c>update-ref &lt;land ref&gt; &lt;id&gt; &lt;zero id&gt;</c> (source, target-before,
/// prepared), flushed to disk because crash recovery reads it;</item>
/// <item><c>worktree lock --reason &lt;reason&gt; &lt;path&gt;</c> of the land worktree (see <c>TryLock</c>).</item>
/// </list>
/// <para>
/// A pin is answered only when libgit2's ref write is git's: a land ref name (<see cref="InProcessGitReads.LandRefName"/>,
/// built by us), an object that exists, a ref that does not, a plain files ref store with nothing libgit2 handles
/// differently (<see cref="InProcessGitReads.Unusual"/>), no <c>reference-transaction</c> hook (git runs it on
/// update-ref, libgit2 does not) and no reflog setting given through the child environment. The pin gets no reflog,
/// as git writes none for <c>refs/antiphon/</c> unless <c>core.logAllRefUpdates=always</c>, which libgit2 honours
/// the same way. Anything else, or any libgit2 error, returns null and git runs the command, so refusals (an
/// existing ref, a missing object, a name conflict) stay git's own. <c>ANTIPHON_GIT_INPROCESS=off</c> turns it off.
/// </para>
/// </summary>
internal static class InProcessGitWrites
{
    public static bool IsObservationPin(IReadOnlyList<string> a) =>
        a is ["update-ref", "-m", var message, var name, var sha, var zero]
        && message.StartsWith(LandingGit.ObservationMessagePrefix, StringComparison.Ordinal)
        && InProcessGitReads.LandRefName.IsMatch(name)
        && LandingGit.IsOid(sha) && zero.Length == sha.Length && zero.All(c => c == '0');

    /// <summary>The recovery pin (<c>update-ref &lt;land ref&gt; &lt;id&gt; &lt;zero id&gt;</c>, no message).</summary>
    public static bool IsRecoveryPin(IReadOnlyList<string> a) =>
        a is ["update-ref", var name, var sha, var zero]
        && InProcessGitReads.LandRefName.IsMatch(name)
        && LandingGit.IsOid(sha) && zero.Length == sha.Length && zero.All(c => c == '0');

    /// <summary><c>worktree lock --reason &lt;reason&gt; &lt;path&gt;</c>.</summary>
    public static bool IsWorktreeLock(IReadOnlyList<string> a) =>
        a is ["worktree", "lock", "--reason", var reason, var path] && reason.Length > 0 && !reason.Contains('\n')
        && Path.IsPathFullyQualified(path);

    public static bool Handles(IReadOnlyList<string> a) => IsObservationPin(a) || IsRecoveryPin(a) || IsWorktreeLock(a);

    /// <summary>git's result for a handled write made in process, or null to run git.</summary>
    public static LandingGitResult? TryWrite(string repository, IReadOnlyList<string> a,
        IReadOnlyDictionary<string, string?>? childEnvironment)
    {
        if (!InProcessGitReads.Enabled || childEnvironment is null) return null;
        if (IsWorktreeLock(a)) return TryLock(repository, a[3], a[4]);
        if (!IsObservationPin(a) && !IsRecoveryPin(a)) return null;
        var (message, name, sha) = a.Count == 6 ? (a[2], a[3], a[4]) : (null, a[1], a[2]);
        if (SetsConfig(childEnvironment, "core.logallrefupdates")) return null;
        try
        {
            using var repo = new Repository(repository);
            if (InProcessGitReads.Unusual(repo) || HasReferenceTransactionHook(repo, childEnvironment)) return null;
            var id = new ObjectId(sha);
            if (!repo.ObjectDatabase.Contains(id) || repo.Refs[name] is not null) return null;
            repo.Refs.Add(name, id, message, allowOverwrite: false);
            // git fsyncs a ref it writes (core.fsync includes references); libgit2 does not by default. Crash
            // recovery reads these pins, so the new loose ref is flushed to disk before success is reported.
            Flush(Path.Combine(CommonDirectory(repo), name.Replace('/', Path.DirectorySeparatorChar)));
            return new LandingGitResult(0, "", "");
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            return null;
        }
    }

    /// <summary>
    /// <c>git worktree lock</c> (builtin/worktree.c): the linked worktree registered at exactly <paramref name="path"/>
    /// gets <c>worktrees/&lt;id&gt;/locked</c> holding the reason and a newline (write_file). The main worktree, an
    /// already locked one, a path matched only by git's suffix rule, or anything unexpected is git's.
    /// </summary>
    private static LandingGitResult? TryLock(string repository, string reason, string path)
    {
        try
        {
            using var repo = new Repository(repository);
            if (InProcessGitReads.Unusual(repo)) return null;
            var worktrees = Path.Combine(CommonDirectory(repo), "worktrees");
            if (!Directory.Exists(worktrees)) return null;
            var target = Path.GetFullPath(path);
            string? admin = null;
            foreach (var dir in Directory.EnumerateDirectories(worktrees))
            {
                var gitdir = Path.Combine(dir, "gitdir");
                if (!File.Exists(gitdir)) continue;
                var line = File.ReadAllText(gitdir).TrimEnd('\r', '\n');
                if (!Path.IsPathFullyQualified(line) || !string.Equals(Path.GetFileName(line), ".git", StringComparison.Ordinal)) continue;
                if (!LandingGit.PathsEqual(Path.GetDirectoryName(Path.GetFullPath(line))!, target)) continue;
                if (admin is not null) return null; // two registrations claim the path: git's to judge
                admin = dir;
            }
            if (admin is null) return null;
            var locked = Path.Combine(admin, "locked");
            if (File.Exists(locked)) return null;
            using (var stream = new FileStream(locked, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(System.Text.Encoding.UTF8.GetBytes(reason + "\n"));
                stream.Flush(true);
            }
            return new LandingGitResult(0, "", "");
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or OperationCanceledException))
        {
            return null;
        }
    }

    private static string CommonDirectory(Repository repo)
    {
        var commondir = Path.Combine(repo.Info.Path, "commondir");
        return Path.GetFullPath(File.Exists(commondir) ? Path.Combine(repo.Info.Path, File.ReadAllText(commondir).Trim()) : repo.Info.Path);
    }

    private static void Flush(string file)
    {
        if (!File.Exists(file)) return;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        stream.Flush(true);
    }

    /// <summary>A reference-transaction hook in the default hooks directory, in a core.hooksPath libgit2 reads, or
    /// in a core.hooksPath the child environment sets (GIT_CONFIG_COUNT/KEY/VALUE).</summary>
    private static bool HasReferenceTransactionHook(Repository repo, IReadOnlyDictionary<string, string?> env)
    {
        var common = CommonDirectory(repo);
        var directories = new List<string> { Path.Combine(common, "hooks") };
        // Through ConfigOf: a linked worktree's Repository.Config lacks the repository's own file. Per-worktree
        // config (no view) is git's to merge, so it counts as a hook and git writes.
        if (InProcessGitReads.ConfigOf(repo) is not { } view) return true;
        try
        {
            if (view.Config.Get<string>("core.hooksPath")?.Value is { Length: > 0 } configured) directories.Add(configured);
        }
        finally
        {
            if (view.Owned) view.Config.Dispose();
        }
        foreach (var value in ConfigValues(env, "core.hookspath")) if (!string.IsNullOrEmpty(value)) directories.Add(value);
        return directories.Any(d =>
        {
            var full = Path.IsPathRooted(d) ? d : Path.Combine(repo.Info.WorkingDirectory ?? repo.Info.Path, d);
            return File.Exists(Path.Combine(full, "reference-transaction"));
        });
    }

    private static bool SetsConfig(IReadOnlyDictionary<string, string?> env, string key) => ConfigValues(env, key).Any();

    private static IEnumerable<string?> ConfigValues(IReadOnlyDictionary<string, string?> env, string key)
    {
        if (!env.TryGetValue("GIT_CONFIG_COUNT", out var countText) || !int.TryParse(countText, out var count)) yield break;
        for (var i = 0; i < count; i++)
            if (env.TryGetValue($"GIT_CONFIG_KEY_{i}", out var k) && string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                yield return env.TryGetValue($"GIT_CONFIG_VALUE_{i}", out var v) ? v : null;
    }
}
