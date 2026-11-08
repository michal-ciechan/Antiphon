namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-1776. When <see cref="Application.Settings.GitSettings.IdentityFile"/> is set, every git
/// command refuses before the process starts if that file is missing, empty, unreadable, or has
/// no <c>user.name</c> and <c>user.email</c>. An empty path is the desktop and is not probed.
/// The file is read on each check and is not cached, so the maximum staleness is zero.
/// Name and email are not a secret; the token probe is separate and does not read token bytes.
/// </summary>
public static class ServerGitIdentity
{
    public const string AbsentReason = "git_identity_absent";
    private const int MaxBytes = 16384;

    public static string? Refusal(string? identityFile)
    {
        var path = (identityFile ?? "").Trim();
        if (path.Length == 0)
            return null;
        return Usable(path) ? null : AbsentReason;
    }

    private static bool Usable(string path)
    {
        if (!Path.IsPathRooted(path))
            return false;
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.Directory)
                || info.Length <= 0 || info.Length > MaxBytes)
                return false;
            return HasUserIdentity(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    internal static bool HasUserIdentity(string text)
    {
        string? section = null;
        var name = false;
        var email = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;
            if (line[0] == '[')
            {
                var end = line.IndexOf(']');
                if (end <= 1)
                {
                    section = null;
                    continue;
                }
                var header = line[1..end].Trim();
                var space = header.IndexOf(' ');
                section = (space < 0 ? header : header[..space]).Trim();
                continue;
            }
            if (!string.Equals(section, "user", StringComparison.OrdinalIgnoreCase))
                continue;
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length == 0)
                continue;
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                name = true;
            else if (key.Equals("email", StringComparison.OrdinalIgnoreCase))
                email = true;
        }
        return name && email;
    }
}
