namespace Antiphon.Agents.Pty;

/// <summary>
/// CARD-0383 / CARD-0213: locate <c>{GROK_HOME}/sessions/*/{id:D}/</c> by GUID match. The GUID is
/// globally unique, so a hit is positive evidence regardless of how grok encoded the cwd. Shared
/// by the server (pre-spawn resume vs create) and the runner (attach + dead-resume gate).
/// </summary>
public static class GrokNativeSessionStore
{
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
