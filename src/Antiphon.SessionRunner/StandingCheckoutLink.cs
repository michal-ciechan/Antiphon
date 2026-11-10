namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1825. lstat of a standing checkout. A final symlink or junction is not followed,
/// and <c>..</c> is not resolved. An unreadable path is treated as a link so launch fails closed.
/// </summary>
internal static class StandingCheckoutLink
{
    internal static bool IsSymlinkOrJunction(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            if (directory.LinkTarget is not null)
                return true;
            var file = new FileInfo(path);
            if (file.LinkTarget is not null)
                return true;
            if (!directory.Exists && !file.Exists)
                return false;
            var attributes = directory.Exists ? directory.Attributes : file.Attributes;
            // A missing path reports attributes as -1, and every bit of -1 is set.
            return (int)attributes >= 0 && (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    internal static bool IsRealDirectory(string path)
    {
        if (IsSymlinkOrJunction(path))
            return false;
        try
        {
            return new DirectoryInfo(path).Exists;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
