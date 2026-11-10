namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// Lexical launch cwd admission for a phone-home runner. The check does not resolve
/// <c>..</c>, follow symlinks, or touch the filesystem. A standing checkout is exactly
/// one segment under <c>&lt;AllowedCwd&gt;/agents</c>. That segment matches
/// <c>[A-Za-z0-9][A-Za-z0-9._-]{0,254}</c>. The Windows flag also refuses a trailing dot
/// and a reserved device name. Launch lstats the checkout separately.
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

    /// <summary>
    /// The normalized agents directory and standing leaf when <paramref name="cwd"/> is a standing checkout.
    /// The strings are not resolved and are safe to lstat.
    /// </summary>
    public static bool TryGetStandingCheckout(
        string? cwd, string allowedCwd, bool windows, out string agentsDirectory, out string checkout)
    {
        agentsDirectory = "";
        checkout = "";
        if (!TryNormalize(cwd, allowedCwd, windows, out var path, out var root, out var comparison))
            return false;
        if (!IsAgentsCheckout(path, root, comparison, windows))
            return false;
        agentsDirectory = root + "/agents";
        checkout = path;
        return true;
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
        return IsStandingSegment(name, windows);
    }

    // [A-Za-z0-9][A-Za-z0-9._-]{0,254}. The worktree predicate does not use this.
    private static bool IsStandingSegment(string name, bool windows)
    {
        if (name.Length is < 1 or > MaxSegmentUtf8Bytes)
            return false;
        if (!IsStandingNameChar(name[0], first: true))
            return false;
        for (var i = 1; i < name.Length; i++)
        {
            if (!IsStandingNameChar(name[i], first: false))
                return false;
        }

        if (!windows)
            return true;
        if (name.EndsWith('.'))
            return false;
        return !IsWindowsReservedDevice(name);
    }

    private static bool IsStandingNameChar(char c, bool first) =>
        char.IsAsciiLetterOrDigit(c) || (!first && c is '.' or '_' or '-');

    // CON, PRN, AUX, NUL, COM1-COM9, LPT1-LPT9, and the same names with an extension (NUL.txt).
    private static bool IsWindowsReservedDevice(string name)
    {
        var stem = name;
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0)
            stem = name[..dot];
        if (stem.Length is < 3 or > 4)
            return false;
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return true;
        return stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && stem[3] is >= '1' and <= '9';
    }
}
