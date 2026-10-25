namespace Antiphon.SessionRunner;

// Lexical launch admission: do not resolve traversal into an otherwise admitted path.
internal static class RunnerWorkspaceCwd
{
    internal static bool IsAdmitted(string? cwd, string allowedCwd, bool windows)
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
}
