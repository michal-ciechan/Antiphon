using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Host-independent identity of a stored working-directory or repository path. Project and agent
/// paths may have been stored by a Windows or a Linux host, so the comparison must not depend on
/// this process's <see cref="Path.DirectorySeparatorChar"/> or case rules. One rule serves every
/// direction that matches a stored path against another stored path: project readiness (agents
/// whose working directory is the project's checkout) and setup's reverse lookup (the project that
/// owns a working directory, CARD-0558). It is intended for the orchestrator-workspace lookup too
/// (CARD-2058), and <c>AgentService.PathsMatch</c> and the attention worktree-cwd test follow it
/// (CARD-2070).
/// <para>
/// The rule, decided purely from the spelling of the path:
/// </para>
/// <list type="bullet">
/// <item><description><b>Windows path</b>: it starts with a drive letter and a colon (<c>C:</c>,
/// <c>C:\</c>, <c>C:/</c>) or with a UNC prefix (<c>\\server\share</c>, also written
/// <c>\/server</c>). Windows paths compare case-insensitively (invariant culture) and <c>/</c>
/// equals <c>\</c>.</description></item>
/// <item><description><b>POSIX path</b>: everything else. POSIX paths compare ordinally
/// (case-sensitive) and a backslash is an ordinary file-name character, so <c>alpha\repo</c> and
/// <c>alpha/repo</c>, or <c>Team/repo</c> and <c>team/repo</c>, stay distinct directories. A
/// leading <c>//</c> is POSIX, not UNC.</description></item>
/// <item><description>A Windows path never equals a POSIX path.</description></item>
/// <item><description>Trailing separators are dropped on both kinds, but never below the root: the
/// drive root <c>C:\</c> (or <c>C:/</c>), the UNC prefix <c>\\</c> and the POSIX root <c>/</c>
/// keep theirs, so <c>C:\work\</c> equals <c>C:\work</c> and <c>/repo/</c> equals <c>/repo</c>
/// while <c>C:\</c> stays distinct from <c>C:</c>.</description></item>
/// <item><description>Nothing else is rewritten: no whitespace trimming, no duplicate-separator
/// collapsing, no <c>.</c>/<c>..</c> resolution and no filesystem access.</description></item>
/// </list>
/// <para>
/// SQL side: <see cref="CandidateFilter{T}"/> is a translatable superset predicate evaluated by
/// PostgreSQL that does not depend on the database's collation or ctype:
/// <c>lower(rtrim(replace(col, '\', '/'), '/') COLLATE "C") LIKE @pattern ESCAPE '!'</c>, guarded by
/// the cheaper necessary condition <c>lower(replace(col, '\', '/') COLLATE "C") LIKE @pattern || '%'</c>
/// so that <c>rtrim</c>, the costliest step per row, runs only on rows that already share the
/// probe's spelling as a prefix (Review 0d322ed5: a full-table <c>rtrim</c> plus <c>ILIKE</c> cost
/// 1.7 times the predicate it replaced). Under
/// the <c>C</c> collation <c>lower</c> folds only ASCII letters, on every server, and <c>LIKE</c>
/// compares code points (<c>_</c> matches exactly one). The pattern (<see cref="CandidatePattern"/>)
/// is the probe's loose spelling (backslashes as slashes, trailing slashes trimmed) with each code
/// point kept as its ASCII lower case (escaped), except that the one-character wildcard <c>_</c>
/// replaces every code point whose invariant case class the database cannot fold: a code point
/// whose class (the code points with the same <c>ToLowerInvariant</c>) holds two members that
/// differ after ASCII-only lower-casing (é/É, the Kelvin sign with <c>k</c>/<c>K</c>). A code point
/// with no case variant (CJK, digits, most punctuation) stays literal, so a CJK probe does not
/// admit every same-length sibling (Review f14ac37a round 3: <c>C:/repos/支付服务组</c> admitted all
/// of <c>C:/repos/00000</c>..<c>99999</c>). <c>ToLowerInvariant</c> maps each code point to exactly
/// one code point, so two spellings with equal <see cref="Key"/>s have loose spellings with the same
/// number of code points, pairwise in one class; wherever the pattern keeps a literal, that class
/// has a single ASCII-lowered spelling, so both agree. The prefilter therefore admits every pair <see cref="Same"/> accepts, whatever the
/// database locale (Review 0d322ed5: an <c>ILIKE</c> under a C-locale database did not). Callers
/// keep only the returned candidates for which <see cref="Same"/> holds. LIKE wildcards in a path
/// are escaped, so they match only themselves.
/// </para>
/// </summary>
public static class WorkingDirectoryIdentity
{
    /// <summary>True when <paramref name="path"/> is spelled as a Windows path (drive or UNC).</summary>
    public static bool IsWindowsPath(string path) =>
        (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
        || (path.Length >= 2 && path[0] == '\\' && (path[1] == '\\' || path[1] == '/'));

    /// <summary>
    /// The comparison key: equal keys mean the same directory under the rule above. Windows keys
    /// start with a lower-case drive letter and colon or with <c>\\</c>; POSIX keys never do, so
    /// the two kinds cannot collide.
    /// </summary>
    public static string Key(string path)
    {
        var key = CasePreservingKey(path);
        return IsWindowsPath(path) ? key.ToLowerInvariant() : key;
    }

    internal static string CasePreservingKey(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (IsWindowsPath(path))
        {
            var windows = path.Replace('/', '\\');
            var root = windows[1] == ':'
                ? (windows.Length >= 3 && windows[2] == '\\' ? 3 : 2)
                : 2;
            return TrimTrailing(windows, '\\', root);
        }

        return TrimTrailing(path, '/', 1);
    }

    /// <summary>True when both paths are present and name the same directory under the rule above.</summary>
    public static bool Same(string? left, string? right) => Same(left, right, preserveWindowsOrdinalCase: false);

    /// <summary>
    /// Dispatch uses the historical ordinal case rule for Windows paths, including Unicode
    /// aliases such as sigma and final sigma. The spelling still selects Windows or POSIX.
    /// </summary>
    internal static bool Same(string? left, string? right, bool preserveWindowsOrdinalCase)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        if (preserveWindowsOrdinalCase && IsWindowsPath(left) && IsWindowsPath(right))
            return string.Equals(CasePreservingKey(left), CasePreservingKey(right), StringComparison.OrdinalIgnoreCase);
        return string.Equals(Key(left), Key(right), StringComparison.Ordinal);
    }

    /// <summary>The escape character of <see cref="CandidatePattern"/>.</summary>
    public const string CandidatePatternEscape = "!";

    /// <summary>The collation the prefilter folds and matches under: ASCII-only folding on every server.</summary>
    public const string CandidateCollation = "C";

    /// <summary>
    /// The <c>LIKE</c> pattern for <paramref name="path"/>, matched against the stored path's loose
    /// spelling lower-cased under the <c>C</c> collation. Each code point of the probe's loose
    /// spelling becomes its ASCII lower case (with <c>!</c>, <c>%</c> and <c>_</c> escaped by
    /// <see cref="CandidatePatternEscape"/>), or <c>_</c> when it is in
    /// <see cref="CaseAmbiguousCodePoints"/>. The pattern never uses <c>%</c> as a wildcard, so it
    /// matches only loose spellings with the same number of code points.
    /// </summary>
    public static string CandidatePattern(string path)
    {
        var loose = LooseSpelling(path);
        var pattern = new StringBuilder(loose.Length);
        foreach (var rune in loose.EnumerateRunes())
        {
            if (CaseAmbiguousCodePoints.Contains(rune.Value))
            {
                pattern.Append('_');
                continue;
            }

            var lower = AsciiLower(rune.Value);
            if (lower is '!' or '%' or '_')
                pattern.Append(CandidatePatternEscape);
            pattern.Append(char.ConvertFromUtf32(lower));
        }

        return pattern.ToString();
    }

    /// <summary>
    /// The Unicode scalar values the pattern wildcards: every member of an invariant case class
    /// (scalar values with the same <c>ToLowerInvariant</c>, the folding <see cref="Key"/> uses)
    /// whose members do not all have one spelling after ASCII-only lower-casing, which is all that
    /// <c>lower(... COLLATE "C")</c> does. That is every non-ASCII letter with a case variant
    /// (é/É, σ/Σ, the Deseret pairs), its lower-case target, and the ASCII letters a non-ASCII code
    /// point lower-cases onto (<c>k</c>/<c>K</c> with the Kelvin sign). Computed from the runtime's
    /// tables over all scalar values once per process.
    /// </summary>
    internal static readonly FrozenSet<int> CaseAmbiguousCodePoints = ComputeCaseAmbiguousCodePoints();

    /// <summary>
    /// A PostgreSQL-translatable predicate selecting a superset of the rows whose stored path is the
    /// same directory as <paramref name="path"/>
    /// (<c>lower(replace(col, '\', '/') COLLATE "C") LIKE @pattern || '%' ESCAPE '!' AND
    /// lower(rtrim(replace(col, '\', '/'), '/') COLLATE "C") LIKE @pattern ESCAPE '!'</c>, with both
    /// patterns sent as parameters). The first operand is implied by the second (trailing slashes
    /// only extend a match of the trimmed spelling) and exists only so that PostgreSQL, which
    /// evaluates the cheaper qual first and stops at the first false one, trims just the rows that
    /// share the probe's prefix. The explicit <c>C</c> collation gives the same result
    /// whatever the database's collation and ctype are. It is server-only: it cannot be evaluated in
    /// process. Refine the result with <see cref="Same"/>; never treat a candidate as a match on its
    /// own.
    /// </summary>
    public static Expression<Func<T, bool>> CandidateFilter<T>(
        Expression<Func<T, string?>> storedPath, string path)
    {
        ArgumentNullException.ThrowIfNull(storedPath);
        var pattern = CandidatePattern(path);
        var probe = new CandidateProbe(pattern + "%", pattern);
        var column = storedPath.Body;
        var slashed = Expression.Call(column, StringReplace, Expression.Constant("\\"), Expression.Constant("/"));
        var trimmed = Expression.Call(slashed, StringTrimEnd, Expression.Constant('/'));
        var body = Expression.AndAlso(
            Expression.AndAlso(
                Expression.NotEqual(column, Expression.Constant(null, typeof(string))),
                LikeUnderC(slashed, Expression.Property(Expression.Constant(probe), nameof(CandidateProbe.Prefix)))),
            LikeUnderC(trimmed, Expression.Property(Expression.Constant(probe), nameof(CandidateProbe.Pattern))));
        return Expression.Lambda<Func<T, bool>>(body, storedPath.Parameters);
    }

    private static MethodCallExpression LikeUnderC(Expression text, Expression pattern) =>
        Expression.Call(
            Like,
            Expression.Constant(EF.Functions),
            Expression.Call(
                Expression.Call(Collate, Expression.Constant(EF.Functions), text, Expression.Constant(CandidateCollation)),
                StringToLower),
            pattern,
            Expression.Constant(CandidatePatternEscape));

    private static string LooseSpelling(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static string TrimTrailing(string path, char separator, int rootLength)
    {
        var end = path.Length;
        while (end > rootLength && path[end - 1] == separator)
            end--;
        return end == path.Length ? path : path[..end];
    }

    private static int AsciiLower(int value) => value is >= 'A' and <= 'Z' ? value + 32 : value;

    private static FrozenSet<int> ComputeCaseAmbiguousCodePoints()
    {
        // Members of the class of each lower-case value L: every scalar value v with
        // ToLowerInvariant(v) == L and v != L (collected here), plus L itself when it is its own lower
        // case. Scalar values that no other value lower-cases onto and that are their own lower case
        // form a class of one and never need a wildcard.
        var classes = new Dictionary<int, List<int>>();
        for (var value = 0; value <= 0x10FFFF; value++)
        {
            if (value is >= 0xD800 and <= 0xDFFF)
                continue;
            var lower = Rune.ToLowerInvariant(new Rune(value)).Value;
            if (lower == value)
                continue;
            if (!classes.TryGetValue(lower, out var members))
                classes[lower] = members = [];
            members.Add(value);
        }

        var ambiguous = new HashSet<int>();
        foreach (var (lower, members) in classes)
        {
            if (Rune.ToLowerInvariant(new Rune(lower)).Value == lower)
                members.Add(lower);
            if (members.Select(AsciiLower).Distinct().Skip(1).Any())
                ambiguous.UnionWith(members);
        }

        return ambiguous.ToFrozenSet();
    }

    private static readonly MethodInfo StringReplace =
        typeof(string).GetMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo StringTrimEnd =
        typeof(string).GetMethod(nameof(string.TrimEnd), [typeof(char)])!;

    private static readonly MethodInfo StringToLower =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo Like =
        typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like),
            [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    private static readonly MethodInfo Collate =
        typeof(RelationalDbFunctionsExtensions).GetMethod(nameof(RelationalDbFunctionsExtensions.Collate))!
            .MakeGenericMethod(typeof(string));

    // Member accesses on a closure-like object, so EF Core sends the patterns as SQL parameters
    // instead of inlining them as literals.
    private sealed class CandidateProbe(string prefix, string pattern)
    {
        public string Prefix { get; } = prefix;

        public string Pattern { get; } = pattern;
    }
}
