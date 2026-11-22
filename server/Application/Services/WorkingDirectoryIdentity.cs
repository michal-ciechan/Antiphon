using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Host-independent identity of a stored working-directory or repository path. Project and agent
/// paths may have been stored by a Windows or a Linux host, so the comparison must not depend on
/// this process's <see cref="Path.DirectorySeparatorChar"/> or case rules. One rule serves every
/// direction that matches a stored path against another stored path: project readiness (agents
/// whose working directory is the project's checkout) and setup's reverse lookup (the project that
/// owns a working directory, CARD-0558). It is intended for the orchestrator-workspace lookup too
/// (CARD-2058).
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
/// PostgreSQL: backslashes as slashes, all trailing slashes trimmed, compared case-insensitively
/// with <c>ILIKE</c> against the probe's escaped <see cref="CandidateSpelling"/>. Callers then keep
/// only the returned candidates for which <see cref="Same"/> holds. Every pair that is the same
/// under this rule has the same <see cref="CandidateKey"/>, so the prefilter loses nothing
/// (identical spellings always pass because the database folds case on both sides; non-ASCII case
/// variants of a Windows path pass as far as the database's case folding agrees with invariant
/// lower-casing), and only the rows that share a loose spelling with the probe are evaluated in
/// process. LIKE wildcards in a path are escaped, so they match only themselves.
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
        ArgumentNullException.ThrowIfNull(path);
        if (IsWindowsPath(path))
        {
            var windows = path.Replace('/', '\\').ToLowerInvariant();
            var root = windows[1] == ':'
                ? (windows.Length >= 3 && windows[2] == '\\' ? 3 : 2)
                : 2;
            return TrimTrailing(windows, '\\', root);
        }

        return TrimTrailing(path, '/', 1);
    }

    /// <summary>True when both paths are present and name the same directory under the rule above.</summary>
    public static bool Same(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        return string.Equals(Key(left), Key(right), StringComparison.Ordinal);
    }

    /// <summary>
    /// The in-process model of the loose key the SQL prefilter compares: lower-cased, backslashes
    /// as slashes and every trailing slash trimmed. A function of <see cref="Key"/>, so equal keys
    /// give equal candidate keys; distinct directories may share one, which is why candidates are
    /// refined with <see cref="Same"/>.
    /// </summary>
    public static string CandidateKey(string path) => LooseSpelling(path).ToLowerInvariant();

    /// <summary>The escape character of <see cref="CandidatePattern"/>.</summary>
    public const string CandidatePatternEscape = "!";

    /// <summary>
    /// The <c>ILIKE</c> pattern for <paramref name="path"/>: its loose spelling (backslashes as
    /// slashes, trailing slashes trimmed) with <c>!</c>, <c>%</c> and <c>_</c> escaped by
    /// <see cref="CandidatePatternEscape"/>, so the pattern matches exactly that spelling up to case.
    /// </summary>
    public static string CandidatePattern(string path) =>
        LooseSpelling(path)
            .Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal);

    /// <summary>
    /// A PostgreSQL-translatable predicate selecting the rows whose stored path has the same loose
    /// spelling as <paramref name="path"/> up to case
    /// (<c>rtrim(replace(col, '\', '/'), '/') ILIKE @pattern ESCAPE '!'</c>, with the pattern
    /// sent as a parameter). It is server-only: it cannot be evaluated in process. Refine the
    /// result with <see cref="Same"/>; never treat a candidate as a match on its own.
    /// </summary>
    public static Expression<Func<T, bool>> CandidateFilter<T>(
        Expression<Func<T, string?>> storedPath, string path)
    {
        ArgumentNullException.ThrowIfNull(storedPath);
        var probe = new CandidateProbe(CandidatePattern(path));
        var column = storedPath.Body;
        var loose = Expression.Call(
            Expression.Call(column, StringReplace, Expression.Constant("\\"), Expression.Constant("/")),
            StringTrimEnd,
            Expression.Constant('/'));
        var pattern = Expression.Property(Expression.Constant(probe), nameof(CandidateProbe.Pattern));
        var matches = Expression.Call(
            ILike,
            Expression.Constant(EF.Functions),
            loose,
            pattern,
            Expression.Constant(CandidatePatternEscape));
        var body = Expression.AndAlso(
            Expression.NotEqual(column, Expression.Constant(null, typeof(string))),
            matches);
        return Expression.Lambda<Func<T, bool>>(body, storedPath.Parameters);
    }

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

    private static readonly MethodInfo StringReplace =
        typeof(string).GetMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo ILike =
        typeof(NpgsqlDbFunctionsExtensions).GetMethod(
            nameof(NpgsqlDbFunctionsExtensions.ILike),
            [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    private static readonly MethodInfo StringTrimEnd =
        typeof(string).GetMethod(nameof(string.TrimEnd), [typeof(char)])!;

    // A member access on a closure-like object, so EF Core sends the pattern as a SQL parameter
    // instead of inlining it as a literal.
    private sealed class CandidateProbe(string pattern)
    {
        public string Pattern { get; } = pattern;
    }
}
