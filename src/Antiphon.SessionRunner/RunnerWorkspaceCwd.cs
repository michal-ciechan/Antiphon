namespace Antiphon.SessionRunner;

// Lexical launch admission: do not resolve traversal into an otherwise admitted path.
internal static class RunnerWorkspaceCwd
{
    internal static bool IsAdmitted(string? cwd, string allowedCwd, bool windows)
    {
        if (string.IsNullOrWhiteSpace(cwd))
            return false;
        var root = allowedCwd.TrimEnd('/');
        if (string.Equals(cwd, root, StringComparison.Ordinal))
            return true;
        var prefix = root + "/worktrees/";
        if (!cwd.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var name = cwd[prefix.Length..];
        return name.Length > 0
            && !name.Contains('/', StringComparison.Ordinal)
            && name != "."
            && name != "..";
    }
}
