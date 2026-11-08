using System.Text.RegularExpressions;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>
/// CARD-1776. When <see cref="Application.Settings.GitSettings.IdentityFile"/> is set, every git
/// command refuses before the process starts if that file is missing, empty, unreadable, or has
/// no <c>user.name</c> and <c>user.email</c>. An empty path is the desktop and is not probed.
/// The file is read on each check and is not cached, so the maximum staleness is zero.
/// Name and email are not a secret; the token probe is separate and does not read token bytes.
/// CARD-1787: the file is mounted into the process that holds the push token, so it may hold
/// only <c>[user]</c> <c>name</c> and <c>email</c>. Any other section (a quoted or dotted
/// <c>user</c> subsection included), key, include, key before a section, key on a header line,
/// continuation line or malformed header refuses with <see cref="InvalidReason"/>. A configured
/// identity also removes the inherited variables that would outrank it (<see cref="ScrubInherited"/>).
/// </summary>
public static partial class ServerGitIdentity
{
    public const string AbsentReason = "git_identity_absent";
    public const string InvalidReason = "git_identity_invalid";
    private const int MaxBytes = 16384;

    public static string? Refusal(string? identityFile)
    {
        var path = (identityFile ?? "").Trim();
        if (path.Length == 0)
            return null;
        return Read(path) is { } text ? Verdict(text) : AbsentReason;
    }

    /// <summary>
    /// Removes the git identity and command-line config variables a git child would otherwise
    /// inherit: <c>GIT_AUTHOR_*</c>, <c>GIT_COMMITTER_*</c>, <c>GIT_CONFIG_PARAMETERS</c>,
    /// <c>GIT_CONFIG_COUNT</c> and every <c>GIT_CONFIG_KEY_n</c>/<c>GIT_CONFIG_VALUE_n</c>.
    /// Each outranks the global file, so the pinned identity holds only once they are gone.
    /// </summary>
    public static void ScrubInherited(IDictionary<string, string?> environment)
    {
        foreach (var name in environment.Keys.Where(Scrubbed).ToList())
            environment.Remove(name);
    }

    private static bool Scrubbed(string name) =>
        name.StartsWith("GIT_AUTHOR_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("GIT_COMMITTER_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase)
        || name.Equals("GIT_CONFIG_COUNT", StringComparison.OrdinalIgnoreCase)
        || name.Equals("GIT_CONFIG_PARAMETERS", StringComparison.OrdinalIgnoreCase);

    private static string? Read(string path)
    {
        if (!Path.IsPathRooted(path))
            return null;
        try
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return null;
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.Directory)
                || info.Length <= 0 || info.Length > MaxBytes)
                return null;
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Null for an allow-listed file with both keys, <see cref="InvalidReason"/> for any
    /// refused line, otherwise <see cref="AbsentReason"/>. Refused content wins over a missing key.</summary>
    internal static string? Verdict(string text)
    {
        var inUser = false;
        var name = false;
        var email = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;
            if (line[0] == '[')
            {
                // Exactly [user]: a quoted ([user "x"]) or dotted ([user.x]) subsection is another
                // section to git, and a key after the header on the same line is not read here.
                if (!UserHeader().IsMatch(line))
                    return InvalidReason;
                inUser = true;
                continue;
            }
            if (!inUser || line[^1] == '\\')
                return InvalidReason;
            var eq = line.IndexOf('=');
            var key = (eq < 0 ? line : line[..eq]).Trim();
            var hasValue = eq > 0 && line[(eq + 1)..].Trim().Length > 0;
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                name |= hasValue;
            else if (key.Equals("email", StringComparison.OrdinalIgnoreCase))
                email |= hasValue;
            else
                return InvalidReason;
        }
        return name && email ? null : AbsentReason;
    }

    [GeneratedRegex(@"^\[user\]\s*(?:[#;].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UserHeader();
}
