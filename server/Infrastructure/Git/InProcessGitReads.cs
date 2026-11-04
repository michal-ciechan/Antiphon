using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using LibGit2Sharp;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// Answers a small set of read-only git commands in process with libgit2 (LibGit2Sharp) instead of
/// starting <c>git</c>: on Windows each git process costs ~40–80 ms to start, and one land runs
/// ~270 of them, most of them these reads.
/// <para>
/// Only the success shape of each command is answered, with git's exact exit code and output. Every
/// other outcome (missing ref, lookup error, unusual argument, any libgit2 exception) returns null and
/// the caller runs the real git command, so failure output and exit codes stay git's own. Commands
/// that print paths are never answered: libgit2 returns the caller's spelling, git the on-disk one.
/// <c>remote get-url</c> is not answered when any <c>insteadOf</c>/<c>pushInsteadOf</c> rewrite is
/// configured. <c>ANTIPHON_GIT_INPROCESS=off</c> turns it all off.
/// </para>
/// <para>
/// Each <see cref="Repositories"/> owns opened repositories (one per working path), so a land's
/// operation scope reuses one handle for the whole operation; outside a scope a set is opened per call.
/// libgit2 re-reads loose refs, packed-refs and config files when they change on disk, so a change made
/// by another process mid-land is seen (covered by <c>InProcessGitReadsTests</c>).
/// </para>
/// </summary>
internal static class InProcessGitReads
{
    public const string EnvVar = "ANTIPHON_GIT_INPROCESS";

    public static bool Enabled { get; } =
        !string.Equals(Environment.GetEnvironmentVariable(EnvVar)?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>Opened repositories keyed by working path; each handle is used under its own lock.</summary>
    public sealed class Repositories : IDisposable
    {
        private readonly ConcurrentDictionary<string, Handle?> _byPath = new(StringComparer.Ordinal);
        private bool _disposed;

        public sealed class Handle(Repository repository) : IDisposable
        {
            public Repository Repository { get; } = repository;
            public object Gate { get; } = new();
            public void Dispose() => Repository.Dispose();
        }

        public Handle? For(string path)
        {
            if (_disposed) return null;
            // Opened through the working directory itself, never a discovered git dir: libgit2 opened
            // at a linked worktree's <common>/worktrees/<name> skips the common config (no origin).
            // Only a checkout root or a .git directory opens; a subdirectory is git's to answer.
            return _byPath.GetOrAdd(path, p =>
                Repository.IsValid(p) ? new Handle(new Repository(p)) : null);
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var handle in _byPath.Values) handle?.Dispose();
            _byPath.Clear();
        }
    }

    /// <summary>git's own result for <paramref name="arguments"/> run in <paramref name="repository"/>, or null to run git.</summary>
    public static LandingGitResult? TryAnswer(string repository, IReadOnlyList<string> arguments, Repositories repositories,
        bool allowRemoteConfig)
    {
        if (!Enabled || arguments.Count < 2) return null;
        try
        {
            var handle = repositories.For(repository);
            if (handle is null) return null;
            lock (handle.Gate)
                return Answer(handle.Repository, arguments, allowRemoteConfig);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or ArgumentException
            or UnauthorizedAccessException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            return null;
        }
    }

    private static LandingGitResult? Answer(Repository repo, IReadOnlyList<string> a, bool allowRemoteConfig)
    {
        switch (a[0])
        {
            // rev-parse --verify <rev>^{commit}  |  rev-parse HEAD
            case "rev-parse" when a.Count == 3 && a[1] == "--verify" && a[2].EndsWith("^{commit}", StringComparison.Ordinal):
                return Commit(repo, a[2][..^"^{commit}".Length]);
            case "rev-parse" when a.Count == 2 && a[1] == "HEAD":
                return Commit(repo, "HEAD");

            // show-ref --verify --hash <ref>  |  show-ref --verify <ref>...  |  show-ref --exists <ref>
            case "show-ref" when a.Count == 4 && a[1] == "--verify" && a[2] == "--hash":
                return Direct(repo, a[3]) is { } sha ? Ok(sha + "\n") : null;
            case "show-ref" when a.Count >= 3 && a[1] == "--verify" && a.Skip(2).All(IsFullRef):
            {
                var lines = new System.Text.StringBuilder();
                foreach (var name in a.Skip(2))
                {
                    if (Direct(repo, name) is not { } refSha) return null;
                    lines.Append(refSha).Append(' ').Append(name).Append('\n');
                }
                return Ok(lines.ToString());
            }
            case "show-ref" when a.Count == 3 && a[1] == "--exists" && IsFullRef(a[2]):
            {
                var found = repo.Refs[a[2]];
                return found is null ? new LandingGitResult(2, "", "") : Ok("");
            }

            // symbolic-ref -q <ref>: 0 + target when symbolic, 1 when a direct ref; missing -> git.
            case "symbolic-ref" when a.Count == 3 && a[1] == "-q" && (a[2] == "HEAD" || IsFullRef(a[2])):
            {
                var found = repo.Refs[a[2]];
                return found switch
                {
                    SymbolicReference symbolic => Ok(symbolic.TargetIdentifier + "\n"),
                    DirectReference => new LandingGitResult(1, "", ""),
                    _ => null,
                };
            }

            // remote get-url --push --all origin
            case "remote" when allowRemoteConfig && a.Count == 5 && a[1] == "get-url" && a[2] == "--push" && a[3] == "--all" && a[4] == "origin":
                return PushUrls(repo);

            // merge-base --is-ancestor <oid> <oid>
            case "merge-base" when a.Count == 4 && a[1] == "--is-ancestor" && LandingGit.IsOid(a[2]) && LandingGit.IsOid(a[3]):
            {
                var ancestor = repo.Lookup<Commit>(a[2]);
                var descendant = repo.Lookup<Commit>(a[3]);
                if (ancestor is null || descendant is null) return null;
                if (ancestor.Sha == descendant.Sha) return Ok("");
                var bases = repo.ObjectDatabase.FindMergeBase(ancestor, descendant);
                return bases?.Sha == ancestor.Sha ? Ok("") : new LandingGitResult(1, "", "");
            }
        }
        return null;
    }

    private static LandingGitResult? Commit(Repository repo, string revision)
    {
        if (revision.Length == 0 || revision.StartsWith('-') || revision.Contains(':') || revision.Contains(".."))
            return null;
        var target = repo.Lookup(revision);
        var commit = target is Commit c ? c : target?.Peel<Commit>();
        return commit is null ? null : Ok(commit.Sha + "\n");
    }

    /// <summary>The commit id a direct, existing ref points at; null for symbolic or missing refs.</summary>
    private static string? Direct(Repository repo, string name) =>
        IsFullRef(name) && repo.Refs[name] is DirectReference direct && LandingGit.IsOid(direct.TargetIdentifier)
            ? direct.TargetIdentifier : null;

    private static bool IsFullRef(string name) => name.StartsWith("refs/", StringComparison.Ordinal) && !name.Contains("..");

    private static LandingGitResult? PushUrls(Repository repo)
    {
        // A linked worktree's handle resolves refs through the common dir but does not load the
        // common config (no origin), so its push URL is git's to answer.
        if (File.Exists(Path.Combine(repo.Info.Path, "commondir"))) return null;
        var config = repo.Config;
        foreach (var entry in config)
        {
            var key = entry.Key.ToLowerInvariant();
            if (key.StartsWith("url.", StringComparison.Ordinal)
                && (key.EndsWith(".insteadof", StringComparison.Ordinal) || key.EndsWith(".pushinsteadof", StringComparison.Ordinal)))
                return null;
        }
        // Only the repository's own config: a git child may run with another HOME/global config
        // (LandingGit.ConfigureProcess), so an origin defined at any other level is git's to answer.
        if (config.Find(@"^remote\.origin\.(push)?url$").Any(e => e.Level != ConfigurationLevel.Local)) return null;
        var push = config.Find(@"^remote\.origin\.pushurl$", ConfigurationLevel.Local).Select(e => e.Value).ToList();
        var urls = push.Count != 0 ? push : config.Find(@"^remote\.origin\.url$", ConfigurationLevel.Local).Select(e => e.Value).ToList();
        if (urls.Count == 0) return null;
        return Ok(string.Concat(urls.Select(u => u + "\n")));
    }

    private static LandingGitResult Ok(string output) => new(0, output, "");
}
