using System.Globalization;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-1708 (operator 2026-11-05, option b with salvage refs). Removing a worktree deletes its HEAD reflog, and the
/// branch delete after it deletes the branch reflog. A commit that only those reflogs reach (history rewritten locally,
/// never pushed) would become unrecoverable, so before anything moves each such commit gets
/// <c>refs/antiphon/salvage/&lt;taskId&gt;/&lt;sha&gt;</c>. Reachable means from any branch, tag or remote-tracking
/// ref. Writing the same ref again is a no-op, and any read or write failure refuses the removal (fail closed).
/// The desktop legacy release tick prunes a salvage ref only once its own reflog says it is older than the retention.
/// </summary>
public static class WorktreeReflogSalvage
{
    public const string RefPrefix = "refs/antiphon/salvage/";

    /// <summary>The most salvage refs one tree may need (after dropping commits another candidate reaches); more refuses.</summary>
    public const int MaxRefs = 64;

    /// <summary>The most salvage refs one prune pass reads.</summary>
    public const int MaxPrunedPerPass = 200;

    /// <summary>A reflog read, the reachability read or a ref write failed; nothing is removed.</summary>
    public const string Failed = "reflog_salvage_failed";

    /// <summary>The tree needs more than <see cref="MaxRefs"/> salvage refs; nothing is removed.</summary>
    public const string OverLimit = "reflog_salvage_over_limit";

    private const int ArgumentBatch = 200;

    private static readonly Regex ReflogTime = new(@"@\{(?<t>[0-9]+)\}\z", RegexOptions.CultureInvariant);

    public static string Scope(Guid taskId) => taskId.ToString("D");

    /// <summary>
    /// Null when every commit reachable only from the tree's HEAD reflog, its HEAD or the branch reflog has a salvage
    /// ref; else the refusal. <paramref name="live"/> is the caller's switch: read before the first write and, through
    /// the ambient destructive gate, again as each write's child starts. A refusal there is returned unchanged.
    /// </summary>
    public static async Task<string?> SalvageAsync(ILandingGit git, LandSourceCoordinates source, string scope,
        Func<CancellationToken, Task<string?>>? live, CancellationToken ct)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (directory, arguments) in new (string, string[])[]
        {
            (source.WorktreePath, ["reflog", "show", "--no-abbrev", "--format=%H", "HEAD"]),
            (source.WorktreePath, ["rev-parse", "--verify", "HEAD^{commit}"]),
            (source.RepositoryPath, ["reflog", "show", "--no-abbrev", "--format=%H", source.SourceFullRef]),
        })
        {
            var read = await git.RunAsync(directory, arguments, ct);
            if (!read.Succeeded) return Failed;
            foreach (var line in Lines(read.Output))
            {
                if (!LandingGit.IsOid(line)) return Failed;
                candidates.Add(line);
            }
        }
        if (candidates.Count == 0) return null;

        // Every commit the candidates reach that no branch, tag or remote-tracking ref reaches; only the candidates
        // themselves can need a ref (an unreachable ancestor is reached through its candidate).
        var unreachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var batch in candidates.Order(StringComparer.Ordinal).Chunk(ArgumentBatch))
        {
            var listed = await git.RunAsync(source.RepositoryPath,
                ["rev-list", .. batch, "--not", "--branches", "--tags", "--remotes"], ct);
            if (!listed.Succeeded) return Failed;
            foreach (var line in Lines(listed.Output))
                if (candidates.Contains(line)) unreachable.Add(line);
            if (unreachable.Count > MaxRefs * 4) return OverLimit;
        }
        if (unreachable.Count == 0) return null;
        var tips = unreachable.ToList();
        if (tips.Count > 1)
        {
            var independent = await git.RunAsync(source.RepositoryPath,
                ["merge-base", "--independent", .. tips.Order(StringComparer.Ordinal)], ct);
            if (!independent.Succeeded) return Failed;
            tips = Lines(independent.Output).ToList();
            if (tips.Count == 0 || tips.Any(t => !unreachable.Contains(t))) return Failed;
        }
        if (tips.Count > MaxRefs) return OverLimit;

        if (live is not null && await live(ct) is { } refused) return refused;
        string? stopped = null;
        using (LandingGit.GateDestructiveStep(async c => stopped = live is null ? null : await live(c)))
        {
            foreach (var sha in tips.Order(StringComparer.Ordinal))
            {
                var name = RefPrefix + scope + "/" + sha;
                var written = await git.RunAsync(source.RepositoryPath,
                    ["update-ref", "--create-reflog", "-m", "antiphon salvage " + scope, name, sha], ct);
                if (stopped is not null) return stopped;
                if (!written.Succeeded) return Failed;
            }
        }
        var check = await git.RunAsync(source.RepositoryPath,
            ["for-each-ref", "--format=%(refname) %(objectname)", RefPrefix + scope + "/"], ct);
        if (!check.Succeeded) return Failed;
        var present = Lines(check.Output).ToHashSet(StringComparer.Ordinal);
        return tips.All(sha => present.Contains(RefPrefix + scope + "/" + sha + " " + sha)) ? null : Failed;
    }

    /// <summary>
    /// Deletes each salvage ref whose newest reflog entry is at least <paramref name="keep"/> old. A ref with no
    /// readable reflog time is kept. Returns the number deleted; a failed read deletes nothing more.
    /// </summary>
    public static async Task<int> PruneAsync(ILandingGit git, string repository, DateTime nowUtc, TimeSpan keep,
        CancellationToken ct)
    {
        var listed = await git.RunAsync(repository, ["for-each-ref", "--format=%(refname) %(objectname)", RefPrefix], ct);
        if (!listed.Succeeded) return 0;
        var pruned = 0;
        foreach (var line in Lines(listed.Output).Take(MaxPrunedPerPass))
        {
            var parts = line.Split(' ');
            if (parts.Length != 2 || !parts[0].StartsWith(RefPrefix, StringComparison.Ordinal) || !LandingGit.IsOid(parts[1]))
                continue;
            var log = await git.RunAsync(repository, ["reflog", "show", "--date=unix", "--format=%gd", "-n", "1", parts[0]], ct);
            if (!log.Succeeded) continue;
            var match = ReflogTime.Match(log.Output.Trim());
            if (!match.Success || !long.TryParse(match.Groups["t"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                continue;
            var written = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            if (nowUtc - written < keep) continue;
            var deleted = await git.RunAsync(repository, ["update-ref", "-d", parts[0], parts[1]], ct);
            if (deleted.Succeeded) pruned++;
        }
        return pruned;
    }

    private static IEnumerable<string> Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length != 0);
}
