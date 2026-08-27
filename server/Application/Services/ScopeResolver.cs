using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One element of a declared scope: either a named <b>area</b> (<c>delivery</c>) or a <b>path
/// glob</b> (<c>server/Migrations/**</c>). The two kinds compare by different rules — an area is an
/// identity and matches only itself, a path is a location and matches anything under it — which is
/// the whole point of splitting them (CARD-0063 §2.1).
/// </summary>
/// <param name="Value">
/// The token as written, trimmed. For a path this is also its <see cref="ScopeResolver.LiteralPrefix"/>
/// input; for a name it is compared case-insensitively and in full.
/// </param>
/// <param name="IsPath">True when the token looks like a location rather than a label.</param>
public readonly record struct ScopeToken(string Value, bool IsPath);

/// <summary>
/// The advisory file lease's comparison, as a list of elements rather than one string.
///
/// <para>Before CARD-0063 the whole <c>ScopeGlob</c> column was treated as a single glob and
/// compared by string prefix, which produced exactly one hold in 623 tasks — a false one
/// (<c>card-reopen-cli</c> held <c>card-reopen-client</c> because one label is a string prefix of
/// the other) — and missed five genuine collisions, because callers have been writing
/// comma-separated lists since 2026-08-17 and a list only prefixes another list by accident.</para>
///
/// <para>The rules, deliberately small: split on commas; a token containing a separator, a dot or a
/// wildcard is a <b>path</b>, anything else is a <b>name</b>; two scopes intersect iff ANY element
/// of one intersects any element of the other; two paths intersect by the old literal-prefix
/// approximation (still the right cost for "might these touch the same tree" — a real glob engine
/// is only needed for drift mapping, over concrete paths); two names intersect by <b>exact</b>
/// case-insensitive equality, never prefix.</para>
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

    /// <summary>
    /// A token is a path if it names a location — it contains a separator, a dot (a file
    /// extension), or a wildcard. Everything else is an area name. Deliberately generous towards
    /// "path": a caller who writes <c>a.cs</c> means the file, and an area name with a dot in it
    /// would be a strange thing to call an area.
    /// </summary>
    private static bool LooksLikePath(string token) =>
        token.IndexOfAny(['/', '\\', '.', '*', '?', '[']) >= 0;

    /// <summary>
    /// Whether two declared scopes intersect. Elements are compared pairwise and independently —
    /// this is what makes <c>a.cs,tests/**</c> collide with <c>tests/Foo.cs</c>, and what stops
    /// <c>card-reopen-cli</c> colliding with <c>card-reopen-client</c>.
    /// </summary>
    public static bool Intersects(string? a, string? b) => Intersects(Parse(a), Parse(b));

    /// <inheritdoc cref="Intersects(string?, string?)"/>
    public static bool Intersects(IReadOnlyList<ScopeToken> a, IReadOnlyList<ScopeToken> b)
    {
        foreach (var left in a)
        foreach (var right in b)
        {
            if (TokensIntersect(left, right))
                return true;
        }

        return false;
    }

    private static bool TokensIntersect(ScopeToken a, ScopeToken b)
    {
        // A name is an identity: it matches itself and nothing else. A name never matches a path
        // (with no area map there is nothing to resolve it to) — S2 gives names their globs.
        if (!a.IsPath || !b.IsPath)
            return !a.IsPath && !b.IsPath
                && string.Equals(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);

        var left = LiteralPrefix(a.Value);
        var right = LiteralPrefix(b.Value);
        return left.StartsWith(right, StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The text before the first wildcard, separators normalised. Coarse on purpose: it answers
    /// "might these two globs reach the same subtree" without pretending to be a glob engine.
    /// </summary>
    public static string LiteralPrefix(string glob)
    {
        var normalized = glob.Replace('\\', '/').TrimStart('.', '/');
        var wildcard = normalized.IndexOfAny(['*', '?', '[']);
        return wildcard < 0 ? normalized : normalized[..wildcard];
    }
}
