using Microsoft.Extensions.FileSystemGlobbing;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// What a settled task actually touched, mapped back onto the repo's areas (CARD-0063 S4).
/// </summary>
/// <param name="ObservedScope">
/// The areas (and any unmapped paths) the task's diff reached, as a comma-separated list in the
/// same shape as a declared scope. Null when nothing was touched.
/// </param>
/// <param name="Drifted">
/// Areas or paths the declared scope did not cover, each with one example path — empty when the
/// declaration held, and always empty when nothing was declared (there is nothing to drift from).
/// </param>
public sealed record ScopeDriftResult(string? ObservedScope, IReadOnlyList<string> Drifted);

/// <summary>
/// Records what a delegate wrote against what it said it would write. <b>Observability only</b> —
/// nothing here fails, holds, kills or re-types anything (CARD-0063 §2.5, D5).
///
/// <para>Blocking was considered and rejected on the evidence: a PreToolUse path hook can only ever
/// be armed in a task's own worktree, where an out-of-area write is already isolated and costs at
/// most a rebase — it could never protect a shared checkout, which is the only place an out-of-area
/// write actually hurts. And predicted file lists are wrong as a matter of course (an enum, a DI
/// registration, a settings class), so a blocking rule converts every wrong prediction into a stuck
/// delegate at exactly the moment it found the file nobody predicted.</para>
///
/// <para>Recording, by contrast, makes both the declaration and the map converge on the truth: a
/// drift that recurs is either a caller who should declare that area too, or a map missing a path.
/// Both are one-line fixes, and both are visible in the event log.</para>
///
/// <para>This is the one place a REAL glob matcher is warranted. Dispatch gating compares literal
/// prefixes because it is asking "might these two globs reach the same tree"; here the inputs are
/// concrete paths from a git diff, and the question is the precise one a matcher answers.</para>
/// </summary>
public static class ScopeDriftPolicy
{
    /// <summary>The <c>Scope</c>/<c>ObservedScope</c> column width.</summary>
    private const int MaxLength = 1000;

    public static ScopeDriftResult Evaluate(
        string? declaredScope, IEnumerable<string> touchedPaths, AreaMap map)
    {
        var paths = touchedPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
            return new ScopeDriftResult(null, []);

        // Which area each area's globs claim, matched once for the whole path set.
        var byArea = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var area in map.Areas)
            byArea[area.Name] = MatchAll(area.Paths, paths);

        // What the declaration itself claims: its own path globs, plus the globs of every area it
        // names. An unknown name owns nothing, so it covers nothing — which is exactly why an
        // unknown name earns a Warning at create time.
        var declared = ScopeResolver.Resolve(declaredScope, map);
        var declaredGlobs = declared.Elements.SelectMany(e => e.Globs).ToList();
        var covered = declaredGlobs.Count == 0
            ? []
            : MatchAll(declaredGlobs, paths);

        var observed = new List<string>();
        var drifted = new List<string>();
        foreach (var path in paths)
        {
            var area = byArea.FirstOrDefault(a => a.Value.Contains(path)).Key;
            var name = area ?? path;
            if (!observed.Contains(name, StringComparer.OrdinalIgnoreCase))
                observed.Add(name);

            // Nothing DECLARED means nothing to drift from: the observation is still recorded, but
            // a task that promised nothing cannot have broken a promise. An unknown area name is
            // not that case — the caller did promise something, it just resolves to no paths, so
            // everything it touched is outside it. That is the signal that pushes a name into the
            // map instead of leaving it a label forever.
            if (declared.Count == 0 || covered.Contains(path))
                continue;

            var sentence = area is null ? path : $"{area} ({path})";
            if (!drifted.Any(d => d.StartsWith(
                    area is null ? path : area + " (", StringComparison.OrdinalIgnoreCase)))
                drifted.Add(sentence);
        }

        return new ScopeDriftResult(Clamp(string.Join(",", observed)), drifted);
    }

    /// <summary>The <c>ScopeDrift</c> event's sentence, or null when nothing drifted.</summary>
    public static string? DescribeDrift(string? declaredScope, IReadOnlyList<string> drifted) =>
        drifted.Count == 0
            ? null
            : $"Touched {string.Join(", ", drifted)} outside declared [{declaredScope}].";

    /// <summary>The completion header's <c>drift=</c> value: the area names only, no paths.</summary>
    public static string? DescribeHeader(IReadOnlyList<string> drifted)
    {
        if (drifted.Count == 0)
            return null;

        var names = drifted.Select(d =>
        {
            var open = d.IndexOf(" (", StringComparison.Ordinal);
            return open < 0 ? d : d[..open];
        });
        return Clamp(string.Join(",", names));
    }

    private static HashSet<string> MatchAll(IEnumerable<string> globs, IReadOnlyList<string> paths)
    {
        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        var any = false;
        foreach (var glob in globs)
        {
            var normalized = Normalize(glob);
            if (normalized.Length == 0)
                continue;
            matcher.AddInclude(normalized);
            any = true;
            // A directory claim written without a wildcard ("server/Migrations") still means the
            // subtree — the dispatch-gating rule reads it that way, so drift must too.
            if (normalized.IndexOfAny(['*', '?']) < 0)
                matcher.AddInclude(normalized.TrimEnd('/') + "/**");
        }

        return any
            ? matcher.Match(paths).Files
                .Select(f => Normalize(f.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
    }

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.TrimStart('/');
    }

    private static string? Clamp(string value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : value.Length <= MaxLength ? value
        : value[..MaxLength];
}
