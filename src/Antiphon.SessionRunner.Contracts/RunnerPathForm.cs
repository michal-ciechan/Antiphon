namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// The two absolute path spellings a phone-home runner's configuration may use: a POSIX path
/// (<c>/work</c>, the Linux container) or a drive-rooted Windows path (<c>C:/Antiphon/runner</c>
/// or <c>C:\Antiphon\runner</c>, a native Windows runner). The check is lexical so the server can
/// validate a runner entry for the other platform. UNC, device (<c>\\?\</c>) and drive-relative
/// (<c>C:work</c>) forms are refused.
/// </summary>
public static class RunnerPathForm
{
    public static bool IsAbsolute(string? path) => IsPosixAbsolute(path) || IsWindowsAbsolute(path);

    public static bool IsPosixAbsolute(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path[0] == '/' && !path.Contains('\0');

    public static bool IsWindowsAbsolute(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Length >= 3
        && char.IsAsciiLetter(path[0])
        && path[1] == ':'
        && path[2] is '/' or '\\'
        && !path.Contains('\0');

    /// <summary>Slash-normalized, trailing-separator-free form used to compare Windows spellings.</summary>
    public static string Normalize(string path) =>
        IsWindowsAbsolute(path) ? path.Replace('\\', '/').TrimEnd('/') : path.TrimEnd('/');

    /// <summary>True when the normalized path has a <c>..</c> segment.</summary>
    public static bool HasParentSegment(string path) =>
        path.Replace('\\', '/').Split('/').Contains("..");

    /// <summary>True for <c>/</c> or a bare drive root such as <c>C:/</c>.</summary>
    public static bool IsFilesystemRoot(string path)
    {
        var normalized = Normalize(path);
        return normalized.Length == 0 || (normalized.Length == 2 && IsWindowsAbsolute(normalized + "/"));
    }
}
