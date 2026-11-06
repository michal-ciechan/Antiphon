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
/// ref. A reflog protects both ids of each entry, so both are read (Review 9222cb00 B-1). A salvage ref is only ever a
/// direct ref created from nothing; a symbolic (even dangling, Review 39f7bfe1) or different existing one refuses (B-4).
/// Any read or write failure refuses the removal (fail closed). The desktop legacy release tick prunes, under the
/// repository lease and the live switch, only the refs of a task whose every release finished at least the retention
/// ago (B-2, B-3).
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

    /// <summary>Review 9222cb00 B-4: a salvage name is already symbolic or names another object; nothing is removed.</summary>
    public const string Collision = "reflog_salvage_collision";

    private const int ArgumentBatch = 200;

    private const string RefFormat = "--format=%(refname) %(objectname) %(symref)";

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
            var shown = Lines(read.Output).ToList();
            if (shown.Any(line => !LandingGit.IsOid(line))) return Failed;
            candidates.UnionWith(shown);
            if (arguments[0] != "reflog") continue;
            // Review 9222cb00 B-1: an entry's old id is protected too (the commit a reset left), and no reflog format
            // shows the oldest surviving entry's old id. Read both ids from the log Git keeps.
            var logged = await LoggedIdsAsync(git, directory, arguments[^1], shown.Count != 0, ct);
            if (logged is null) return Failed;
            candidates.UnionWith(logged);
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

        // B-4: only our own direct ref holding exactly its commit counts as present; a symbolic one, or one naming
        // another object, is a collision that is never written through.
        var namespaceRefs = RefPrefix + scope + "/";
        var existing = await git.RunAsync(source.RepositoryPath, ["for-each-ref", RefFormat, namespaceRefs], ct);
        if (!existing.Succeeded) return Failed;
        var held = Lines(existing.Output).ToList();
        // Review 39f7bfe1 B-4: for-each-ref omits a dangling symbolic ref and a create-only write replaces one, so ask
        // Git for each name's symbolic identity directly (0 symbolic, dangling or not; 1 direct or absent).
        foreach (var sha in tips.Order(StringComparer.Ordinal))
        {
            var symbolic = await git.RunAsync(source.RepositoryPath, ["symbolic-ref", "-q", namespaceRefs + sha], ct);
            if (symbolic.ExitCode == 0) return Collision;
            if (symbolic.ExitCode != 1) return Failed;
        }
        if (live is not null && await live(ct) is { } refused) return refused;
        string? stopped = null;
        using (LandingGit.GateDestructiveStep(async c => stopped = live is null ? null : await live(c)))
        {
            foreach (var sha in tips.Order(StringComparer.Ordinal))
            {
                var name = namespaceRefs + sha;
                if (held.Contains(name + " " + sha, StringComparer.Ordinal)) continue;
                if (held.Any(l => l.StartsWith(name + " ", StringComparison.Ordinal))) return Collision;
                // Create-only (the expected old value is the null id) and never through a symbolic ref.
                var written = await git.RunAsync(source.RepositoryPath,
                    ["update-ref", "--no-deref", "--create-reflog", "-m", "antiphon salvage " + scope, name, sha,
                        new string('0', sha.Length)], ct);
                if (stopped is not null) return stopped;
                if (!written.Succeeded) return Failed;
            }
        }
        var check = await git.RunAsync(source.RepositoryPath, ["for-each-ref", RefFormat, namespaceRefs], ct);
        if (!check.Succeeded) return Failed;
        var present = Lines(check.Output).ToHashSet(StringComparer.Ordinal);
        return tips.All(sha => present.Contains(namespaceRefs + sha + " " + sha)) ? null : Failed;
    }

    /// <summary>
    /// Review 9222cb00 B-2/B-3/B-4. Deletes each salvage ref of a <paramref name="prunable"/> task scope whose newest
    /// reflog entry is at least <paramref name="keep"/> old. Only a direct ref named after its own object is deleted,
    /// never through a symbolic ref and only while it still holds that object; a dangling symbolic ref is never
    /// listed, so never deleted. A ref with no readable reflog time is kept, and a failed read deletes nothing more. <paramref name="live"/> is read before each delete and again as
    /// its child starts; a refusal deletes nothing more and is returned as <c>Stopped</c>.
    /// </summary>
    public static async Task<(int Pruned, string? Stopped)> PruneAsync(ILandingGit git, string repository, DateTime nowUtc,
        TimeSpan keep, IReadOnlySet<string> prunable, Func<CancellationToken, Task<string?>> live, CancellationToken ct)
    {
        if (prunable.Count == 0) return (0, null);
        var listed = await git.RunAsync(repository, ["for-each-ref", RefFormat, RefPrefix], ct);
        if (!listed.Succeeded) return (0, null);
        var pruned = 0;
        string? stopped = null;
        using var gate = LandingGit.GateDestructiveStep(async c => stopped = await live(c));
        foreach (var line in Lines(listed.Output).Where(l => prunable.Contains(ScopeOf(l))).Take(MaxPrunedPerPass))
        {
            // A third field is a symbolic ref's target: never pruned.
            var parts = line.Split(' ');
            if (parts.Length != 2 || !LandingGit.IsOid(parts[1])
                || !string.Equals(parts[0], RefPrefix + ScopeOf(line) + "/" + parts[1], StringComparison.Ordinal))
                continue;
            var log = await git.RunAsync(repository, ["reflog", "show", "--date=unix", "--format=%gd", "-n", "1", parts[0]], ct);
            if (!log.Succeeded) continue;
            var match = ReflogTime.Match(log.Output.Trim());
            if (!match.Success || !long.TryParse(match.Groups["t"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                continue;
            var written = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            if (nowUtc - written < keep) continue;
            if (await live(ct) is { } refused) return (pruned, refused);
            var deleted = await git.RunAsync(repository, ["update-ref", "--no-deref", "-d", parts[0], parts[1]], ct);
            if (stopped is not null) return (pruned, stopped);
            if (deleted.Succeeded) pruned++;
        }
        return (pruned, null);
    }

    /// <summary>The task scope of a <c>for-each-ref</c> line under <see cref="RefPrefix"/>, else empty.</summary>
    private static string ScopeOf(string line)
    {
        if (!line.StartsWith(RefPrefix, StringComparison.Ordinal)) return "";
        var slash = line.IndexOf('/', RefPrefix.Length);
        return slash < 0 ? "" : line[RefPrefix.Length..slash];
    }

    /// <summary>
    /// B-1: every non-null old and new id of the reflog Git keeps for <paramref name="log"/>; null when it cannot be
    /// read completely, or is missing although <c>reflog show</c> listed entries (fail closed).
    /// </summary>
    private static async Task<IReadOnlyList<string>?> LoggedIdsAsync(ILandingGit git, string directory, string log,
        bool listed, CancellationToken ct)
    {
        var located = await git.RunAsync(directory, ["rev-parse", "--path-format=absolute", "--git-path", "logs/" + log], ct);
        var path = located.Output.Trim();
        if (!located.Succeeded || path.Length == 0 || path.Contains('\n') || !Path.IsPathFullyQualified(path)) return null;
        string text;
        try
        {
            if (!File.Exists(path)) return listed ? null : [];
            text = await File.ReadAllTextAsync(path, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        var ids = new List<string>();
        foreach (var line in Lines(text))
        {
            var fields = line.Split(' ', 3);
            if (fields.Length < 3 || !LandingGit.IsOid(fields[0]) || !LandingGit.IsOid(fields[1])) return null;
            foreach (var id in fields.Take(2))
                if (id.Any(c => c != '0')) ids.Add(id);
        }
        if (listed && ids.Count == 0) return null;
        return ids;
    }

    private static IEnumerable<string> Lines(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r').Trim()).Where(l => l.Length != 0);
}
