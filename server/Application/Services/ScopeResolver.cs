using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One element of a declared scope: either a named <b>area</b> (<c>delivery</c>) or a <b>path
/// glob</b> (<c>server/Migrations/**</c>). The two kinds compare by different rules — an area is an
/// identity and matches only itself, a path is a location and matches anything under it — which is
/// the whole point of splitting them (CARD-0063 §2.1).
/// </summary>
/// <param name="Value">The token as written, trimmed.</param>
/// <param name="IsPath">True when the token looks like a location rather than a label.</param>
public readonly record struct ScopeToken(string Value, bool IsPath);

/// <summary>
/// A scope token with its area map applied: a name has become the globs its area owns, a path is
/// its own glob, and a name the map does not know has become an opaque label with no globs at all.
/// </summary>
/// <param name="Token">What the caller wrote.</param>
/// <param name="AreaName">The area this element names, known or not; null for a path.</param>
/// <param name="Globs">Paths this element claims. Empty for an unknown area name.</param>
/// <param name="Weight">The area's weight; <see cref="AreaWeight.Serialise"/> for a path or a label.</param>
/// <param name="IsKnownArea">Whether <paramref name="AreaName"/> was found in the repo's map.</param>
public sealed record ResolvedScopeElement(
    string Token,
    string? AreaName,
    IReadOnlyList<string> Globs,
    AreaWeight Weight,
    bool IsKnownArea);

/// <summary>A whole declared scope, resolved against one repo's area map.</summary>
public sealed record ResolvedScope(IReadOnlyList<ResolvedScopeElement> Elements)
{
    public static ResolvedScope Empty { get; } = new([]);

    public int Count => Elements.Count;

    /// <summary>
    /// Area names the repo's map does not know. Accepted as labels and warned about, never
    /// rejected (CARD-0063 D1): a bookkeeping field must not be able to refuse a launch.
    /// </summary>
    public IReadOnlyList<string> UnknownAreaNames => Elements
        .Where(e => e.AreaName is not null && !e.IsKnownArea)
        .Select(e => e.AreaName!)
        .ToList();
}

/// <summary>
/// What two intersecting scopes have in common, and how hard it is.
/// </summary>
/// <param name="Any">Whether they intersect at all.</param>
/// <param name="AllAllow">
/// True when EVERY element involved in an intersection is weighted <see cref="AreaWeight.Allow"/>.
/// The per-area weight is a downgrade only, so this can lower a pair's policy and never raise it.
/// </param>
/// <param name="Areas">
/// Names of what intersected — area names where there is one, otherwise the path token — for the
/// sentence in the <c>Held</c>/<c>Warning</c> event.
/// </param>
public sealed record ScopeIntersection(bool Any, bool AllAllow, IReadOnlyList<string> Areas)
{
    public static ScopeIntersection None { get; } = new(false, false, []);

    public string Describe() => Areas.Count == 0 ? string.Empty : string.Join(", ", Areas);
}

/// <summary>
/// The advisory file lease's comparison: a list of elements resolved through the repo's area map,
/// rather than one string compared by prefix.
///
/// <para>Before CARD-0063 the whole scope column was treated as a single glob and compared by
/// string prefix, which produced exactly one hold in 623 tasks — a false one
/// (<c>card-reopen-cli</c> held <c>card-reopen-client</c> because one label is a string prefix of
/// the other) — and missed five genuine collisions, because callers have been writing
/// comma-separated lists since 2026-08-17 and a list only prefixes another list by accident.</para>
///
/// <para>The rules, deliberately small: split on commas; a token containing a separator, a dot or a
/// wildcard is a <b>path</b>, anything else is a <b>name</b>; a name resolves through the repo's
/// <c>antiphon.areas.json</c> to that area's globs, or to itself as an opaque label when the map
/// does not know it; two scopes intersect iff ANY element of one intersects any element of the
/// other; two globs intersect by the literal-prefix approximation (still the right cost for "might
/// these touch the same tree" — a real glob engine is only needed for drift mapping, over concrete
/// paths); two labels intersect by <b>exact</b> case-insensitive equality, never prefix.</para>
/// </summary>
public static class ScopeResolver
{
    private static readonly char[] Separators = [','];

    /// <summary>
    /// Whether the lease applies to this task at all. ReadOnly writes nothing, so it can neither
    /// hold a writer nor be held by one — the four scoped ReadOnly rows in the live DB could only
    /// ever have cost someone a wait for no reason.
    /// </summary>
    public static bool ParticipatesInLease(WorkspaceMode workspace) =>
        workspace != WorkspaceMode.ReadOnly;

    /// <summary>
    /// The directory two tasks are compared within. <see cref="AgentTask.RepoPath"/> is the right
    /// key: a task dispatched with <c>-Dir &lt;repo&gt;/client</c> carries a different
    /// <see cref="AgentTask.WorkingDirectory"/> from a repo-root task in the same checkout, and
    /// before this they never compared at all. Falls back to the working directory when the
    /// directory is not a git repo.
    /// </summary>
    public static string KeyFor(string? repoPath, string workingDirectory) =>
        string.IsNullOrWhiteSpace(repoPath) ? workingDirectory : repoPath;

    /// <summary>Split a declared scope into its elements. Never null; empty for a blank scope.</summary>
    public static IReadOnlyList<ScopeToken> Parse(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return [];

        var parts = scope.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return [];

        var tokens = new List<ScopeToken>(parts.Length);
        foreach (var part in parts)
            tokens.Add(new ScopeToken(part, LooksLikePath(part)));
        return tokens;
    }

    /// <summary>Parse a scope and resolve its names through <paramref name="map"/>.</summary>
    public static ResolvedScope Resolve(string? scope, AreaMap map)
    {
        var tokens = Parse(scope);
        if (tokens.Count == 0)
            return ResolvedScope.Empty;

        var elements = new List<ResolvedScopeElement>(tokens.Count);
        foreach (var token in tokens)
        {
            if (token.IsPath)
            {
                elements.Add(new ResolvedScopeElement(
                    token.Value, AreaName: null, [token.Value], AreaWeight.Serialise, IsKnownArea: false));
                continue;
            }

            if (map.TryGet(token.Value, out var area))
            {
                elements.Add(new ResolvedScopeElement(
                    token.Value, area.Name, area.Paths, area.Weight, IsKnownArea: true));
                continue;
            }

            // D1: an unknown name is a label, not an error. It owns no paths, so it can only ever
            // match another task that wrote the same label — strictly better than today, and it
            // never blocks a launch.
            elements.Add(new ResolvedScopeElement(
                token.Value, token.Value, [], AreaWeight.Serialise, IsKnownArea: false));
        }

        return new ResolvedScope(elements);
    }

    /// <summary>
    /// A token is a path if it names a location — it contains a separator, a dot (a file
    /// extension), or a wildcard. Everything else is an area name. Deliberately generous towards
    /// "path": a caller who writes <c>a.cs</c> means the file, and an area name with a dot in it
    /// would be a strange thing to call an area.
    /// </summary>
    private static bool LooksLikePath(string token) =>
        token.IndexOfAny(['/', '\\', '.', '*', '?', '[']) >= 0;

    /// <summary>
    /// Whether two declared scopes intersect, with no area map. Elements are compared pairwise and
    /// independently — this is what makes <c>a.cs,tests/**</c> collide with <c>tests/Foo.cs</c>,
    /// and what stops <c>card-reopen-cli</c> colliding with <c>card-reopen-client</c>.
    /// </summary>
    public static bool Intersects(string? a, string? b) =>
        Intersect(Resolve(a, AreaMap.Empty), Resolve(b, AreaMap.Empty)).Any;

    /// <inheritdoc cref="Intersects(string?, string?)"/>
    public static bool Intersects(ResolvedScope a, ResolvedScope b) => Intersect(a, b).Any;

    /// <summary>
    /// Every element pair that intersects, folded into one verdict: whether they touch, whether
    /// everything that touched is weighted <c>allow</c>, and what to name in the event.
    /// </summary>
    public static ScopeIntersection Intersect(ResolvedScope a, ResolvedScope b)
    {
        List<string>? areas = null;
        var allAllow = true;

        foreach (var left in a.Elements)
        foreach (var right in b.Elements)
        {
            if (!ElementsIntersect(left, right))
                continue;

            areas ??= [];
            Name(areas, left);
            Name(areas, right);
            if (left.Weight != AreaWeight.Allow || right.Weight != AreaWeight.Allow)
                allAllow = false;
        }

        return areas is null ? ScopeIntersection.None : new ScopeIntersection(true, allAllow, areas);

        static void Name(List<string> into, ResolvedScopeElement element)
        {
            var name = element.AreaName ?? element.Token;
            if (!into.Contains(name, StringComparer.OrdinalIgnoreCase))
                into.Add(name);
        }
    }

    private static bool ElementsIntersect(ResolvedScopeElement a, ResolvedScopeElement b)
    {
        // Two elements naming the same area are the same area, whether or not the map knows it.
        if (a.AreaName is not null && b.AreaName is not null
            && string.Equals(a.AreaName, b.AreaName, StringComparison.OrdinalIgnoreCase))
            return true;

        // An unknown name owns nothing, so it cannot claim a subtree — it is opaque, and the
        // equality above is the only way it ever matches.
        if (a.Globs.Count == 0 || b.Globs.Count == 0)
            return false;

        foreach (var left in a.Globs)
        foreach (var right in b.Globs)
        {
            if (GlobsIntersect(left, right))
                return true;
        }

        return false;
    }

    private static bool GlobsIntersect(string a, string b)
    {
        var left = LiteralPrefix(a);
        var right = LiteralPrefix(b);
        // A glob whose literal prefix is empty would match everything; that is a map defect, not a
        // claim on the whole tree, so it claims nothing instead.
        if (left.Length == 0 || right.Length == 0)
            return false;
        return left.StartsWith(right, StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The text before the first wildcard, separators normalised. Coarse on purpose: it answers
    /// "might these two globs reach the same subtree" without pretending to be a glob engine.
    /// </summary>
    public static string LiteralPrefix(string glob)
    {
        var normalized = glob.Replace('\\', '/');
        // Strip a leading "./" only — a blanket TrimStart('.', '/') also eats the dot off
        // ".claude/skills/**", which then names a directory that does not exist.
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        normalized = normalized.TrimStart('/');

        var wildcard = normalized.IndexOfAny(['*', '?', '[']);
        return wildcard < 0 ? normalized : normalized[..wildcard];
    }
}
