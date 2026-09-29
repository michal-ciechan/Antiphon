using System.Text.RegularExpressions;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>The shared identity of a desktop origin and a runner checkout.</summary>
public static partial class RepositoryCloneSource
{
    [GeneratedRegex(@"^git@(?<host>[A-Za-z0-9.-]+):(?<path>[^\s?#]+)$")]
    private static partial Regex ScpOrigin();

    public static bool TryNormalize(string? source, out string identity)
    {
        identity = "";
        var value = source?.Trim();
        if (string.IsNullOrEmpty(value))
            return false;

        var scp = ScpOrigin().Match(value);
        if (scp.Success)
            return TryHttps(scp.Groups["host"].Value, scp.Groups["path"].Value, out identity);

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "https" or "ssh")
            {
                if (uri.Scheme == "ssh" && uri.UserInfo != "git")
                    return false;
                if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                    || uri.Host.Length == 0 || (uri.Scheme == "https" && uri.UserInfo.Length > 0))
                    return false;
                return TryHttps(uri.Host, Uri.UnescapeDataString(uri.AbsolutePath), out identity);
            }
            if (uri.IsFile && Path.IsPathFullyQualified(uri.LocalPath))
            {
                identity = value;
                return true;
            }
            return false;
        }

        if (Path.IsPathFullyQualified(value))
        {
            identity = value;
            return true;
        }
        return false;
    }

    private static bool TryHttps(string host, string path, out string identity)
    {
        identity = "";
        var segments = path.Trim('/').Split('/');
        if (segments.Length < 2 || segments.Any(s => s.Length == 0 || s is "." or ".."
            || s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))))
            return false;
        var normalizedPath = string.Join('/', segments).ToLowerInvariant();
        if (normalizedPath.EndsWith(".git", StringComparison.Ordinal))
            normalizedPath = normalizedPath[..^4];
        if (normalizedPath.EndsWith('/'))
            return false;
        identity = "https://" + host.ToLowerInvariant() + "/" + normalizedPath + ".git";
        return true;
    }

    public static bool TryDeriveName(string identity, out string name)
    {
        name = "";
        if (!TryNormalize(identity, out var normalized))
            return false;
        // Local identities retain their native separator, including Windows paths.
        var segment = normalized.TrimEnd('/', '\\').Split('/', '\\').Last();
        if (segment.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            segment = segment[..^4];
        segment = segment.ToLowerInvariant();
        if (segment.Length is < 1 or > 64 || !char.IsAsciiLetterOrDigit(segment[0])
            || segment.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            return false;
        name = segment;
        return true;
    }

    public static bool IsAdmitted(string identity, IEnumerable<string> prefixes, string primary) =>
        string.Equals(identity, primary, StringComparison.Ordinal)
        || prefixes.Any(prefix => identity.StartsWith(prefix, StringComparison.Ordinal));
}
