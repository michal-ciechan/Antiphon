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

    /// <summary>True when a segment is <c>.</c>. A trailing slash is not a segment.</summary>
    public static bool HasDotSegment(string path) =>
        path.Replace('\\', '/').Split('/').Contains(".");

    /// <summary>True for <c>/</c> or a bare drive root such as <c>C:/</c>.</summary>
    public static bool IsFilesystemRoot(string path)
    {
        var normalized = Normalize(path);
        return normalized.Length == 0 || (normalized.Length == 2 && IsWindowsAbsolute(normalized + "/"));
    }

    /// <summary>
    /// CARD-1834. <c>PhoneHome:AllowedCwd</c> spellings that are empty, a filesystem root,
    /// a <c>.</c> or <c>..</c> segment, or a Windows extended prefix that collapses to a
    /// drive root (<c>//?/C:/</c>). Lexical only: <c>.</c> and <c>..</c> are collapsed here
    /// and are not resolved against the filesystem.
    /// </summary>
    public static bool IsRejectedAllowedCwd(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;
        if (IsFilesystemRoot(path) || HasParentSegment(path) || HasDotSegment(path))
            return true;
        return CollapsesToFilesystemRoot(path);
    }

    // //?/C:/ and \\?\C:\ become the drive root. Other dot segments are refused by HasDotSegment.
    private static bool CollapsesToFilesystemRoot(string path)
    {
        var form = path.Replace('\\', '/');
        if (form.StartsWith("//?/", StringComparison.Ordinal))
            form = form[4..];
        var absolutePosix = form.StartsWith('/');
        var stack = new List<string>();
        foreach (var segment in form.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            if (segment == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }

            stack.Add(segment);
        }

        var resolved = absolutePosix
            ? "/" + string.Join('/', stack)
            : string.Join('/', stack);
        return IsFilesystemRoot(resolved);
    }
}
