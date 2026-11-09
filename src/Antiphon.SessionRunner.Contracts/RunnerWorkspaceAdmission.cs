using System.Text;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// Lexical launch cwd admission for a phone-home runner. The check does not resolve
/// <c>..</c>, follow symlinks, or touch the filesystem. A standing checkout is exactly
/// one segment under <c>&lt;AllowedCwd&gt;/agents</c>.
/// </summary>
public static class RunnerWorkspaceAdmission
{
    /// <summary>Linux NAME_MAX and the Windows path-component limit. A longer agents segment cannot be a directory name.</summary>
    public const int MaxSegmentUtf8Bytes = 255;

    public static bool IsAdmitted(string? cwd, string allowedCwd, bool windows)
    {
        if (!TryNormalize(cwd, allowedCwd, windows, out var path, out var root, out var comparison))
            return false;
        if (string.Equals(path, root, comparison))
            return true;
        if (IsWorktreeCheckout(path, root, comparison, windows))
            return true;
        return IsAgentsCheckout(path, root, comparison, windows);
    }

    public static bool IsStandingCheckout(string? cwd, string allowedCwd, bool windows)
    {
        if (!TryNormalize(cwd, allowedCwd, windows, out var path, out var root, out var comparison))
            return false;
        return IsAgentsCheckout(path, root, comparison, windows);
    }

    private static bool TryNormalize(
        string? cwd, string allowedCwd, bool windows, out string path, out string root, out StringComparison comparison)
    {
        path = "";
        root = "";
        comparison = StringComparison.Ordinal;
        if (string.IsNullOrWhiteSpace(cwd))
            return false;
        // The native Windows filesystem accepts both separators and folds case. Keep
        // Unix backslashes literal; interpreting them there would widen its admission.
        if (windows)
        {
            cwd = cwd.Replace('\\', '/');
            allowedCwd = allowedCwd.Replace('\\', '/');
        }

        comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        path = cwd;
        root = allowedCwd.TrimEnd('/');
        return true;
    }

    // Historical worktree predicate. Do not add agents-only refusals here.
    private static bool IsWorktreeCheckout(string path, string root, StringComparison comparison, bool windows)
    {
        var prefix = root + "/worktrees/";
        if (!path.StartsWith(prefix, comparison))
            return false;
        var name = path[prefix.Length..];
        return name.Length > 0
            && !name.Contains('/', StringComparison.Ordinal)
            && (!windows || (!name.Contains(':') && !name.EndsWith('.') && !name.EndsWith(' ')))
            && name != "."
            && name != "..";
    }

    private static bool IsAgentsCheckout(string path, string root, StringComparison comparison, bool windows)
    {
        var prefix = root + "/agents/";
        if (!path.StartsWith(prefix, comparison))
            return false;
        var name = path[prefix.Length..];
        if (name.Length == 0
            || name.Contains('/')
            || name.Contains('\\')
            || name.Contains(':')
            || name is "." or "..")
            return false;
        if (windows && (name.EndsWith('.') || name.EndsWith(' ')))
            return false;
        if (Encoding.UTF8.GetByteCount(name) > MaxSegmentUtf8Bytes)
            return false;
        foreach (var c in name)
        {
            if (char.IsControl(c) || c > 0x7E)
                return false;
        }

        return true;
    }
}
