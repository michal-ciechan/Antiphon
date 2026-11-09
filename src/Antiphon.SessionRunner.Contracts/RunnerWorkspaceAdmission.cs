namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// Lexical launch cwd admission. This type does not resolve <c>..</c>, follow symlinks,
/// or touch the filesystem. Standing checkouts are filled in by the CARD-1695 fix;
/// until then <see cref="IsStandingCheckout"/> is false and <see cref="IsAdmitted"/>
/// keeps the historical root-or-worktree rule.
/// </summary>
public static class RunnerWorkspaceAdmission
{
    public static bool IsAdmitted(string? cwd, string allowedCwd, bool windows)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            return false;
        // The native Windows filesystem accepts both separators and folds case. Keep
        // Unix backslashes literal; interpreting them there would widen its admission.
        if (windows)
        {
            cwd = cwd.Replace('\\', '/');
            allowedCwd = allowedCwd.Replace('\\', '/');
        }
        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = allowedCwd.TrimEnd('/');
        if (string.Equals(cwd, root, comparison))
            return true;
        var prefix = root + "/worktrees/";
        if (!cwd.StartsWith(prefix, comparison))
            return false;
        var name = cwd[prefix.Length..];
        return name.Length > 0
            && !name.Contains('/', StringComparison.Ordinal)
            && (!windows || (!name.Contains(':') && !name.EndsWith('.') && !name.EndsWith(' ')))
            && name != "."
            && name != "..";
    }

    public static bool IsStandingCheckout(string? cwd, string allowedCwd, bool windows)
    {
        if (windows && cwd is null && allowedCwd is null)
            return false;
        return false;
    }
}
