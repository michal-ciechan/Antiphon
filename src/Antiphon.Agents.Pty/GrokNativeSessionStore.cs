namespace Antiphon.Agents.Pty;

/// <summary>
/// CARD-0383 / CARD-0213: locate <c>{GROK_HOME}/sessions/*/{id:D}/</c> by GUID match. The GUID is
/// globally unique, so a hit is positive evidence regardless of how grok encoded the cwd. Shared
/// by the server (pre-spawn resume vs create) and the runner (attach + dead-resume gate).
/// </summary>
public static class GrokNativeSessionStore
{
    public static NativeSessionPresence Probe(string grokHome, Guid id)
    {
        var root = Path.Combine(grokHome, "sessions");
        try
        {
            foreach (var cwd in Directory.GetDirectories(root))
            {
                try
                {
                    if ((File.GetAttributes(Path.Combine(cwd, id.ToString("D"))) & FileAttributes.Directory) != 0)
                        return NativeSessionPresence.Found;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
            return NativeSessionPresence.Missing;
        }
        // CARD-1669: Unix maps ENOTDIR (a file at the sessions root) to DirectoryNotFoundException;
        // unreadable storage is Unavailable, never Missing (Missing means only a fresh start helps).
        // CARD-1675: Windows and Unix both report a file at GROK_HOME or above the same way, so
        // only a chain of missing directories under an existing directory is Missing.
        catch (DirectoryNotFoundException) when (!FirstExistingAncestorIsNonDirectory(root)) { return NativeSessionPresence.Missing; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return NativeSessionPresence.Unavailable;
        }
    }

    private static bool FirstExistingAncestorIsNonDirectory(string path) =>
        FirstExistingAncestorIsNonDirectory(path, File.Exists, Directory.Exists, LinkTargetIsAbsent);

    // File.Exists is true for a link to a file and for a dangling file link on every OS: neither is a store.
    internal static bool FirstExistingAncestorIsNonDirectory(string path, Func<string, bool> fileExists,
        Func<string, bool> directoryExists, Func<string, bool> linkTargetIsAbsent)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (fileExists(current))
                return true;
            if (directoryExists(current))
                return false;
        }
        return false;
    }

    internal static bool LinkTargetIsAbsent(string path) => false;

    /// <summary>
    /// The session directory (parent of <c>updates.jsonl</c>), or <see langword="null"/> when
    /// nothing under <paramref name="grokHome"/> contains <c>{id:D}/</c>.
    /// </summary>
    public static string? TryLocateSessionDirectory(string grokHome, Guid id)
    {
        if (string.IsNullOrWhiteSpace(grokHome) || id == Guid.Empty)
            return null;

        var sessionsRoot = Path.Combine(grokHome, "sessions");
        if (!Directory.Exists(sessionsRoot))
            return null;

        var folder = id.ToString("D");
        string[] cwdDirs;
        try
        {
            cwdDirs = Directory.GetDirectories(sessionsRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var cwdDir in cwdDirs)
        {
            var candidate = Path.Combine(cwdDir, folder);
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>True when <see cref="TryLocateSessionDirectory"/> finds a directory for <paramref name="id"/>.</summary>
    public static bool Exists(string grokHome, Guid id) =>
        TryLocateSessionDirectory(grokHome, id) is not null;
}

public enum NativeSessionPresence { Found, Missing, Unavailable }
