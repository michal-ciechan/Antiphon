using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// CARD-1718 A-D3. Mandatory ignored-name floor shared by the server classifier and the runner.
/// A match refuses removal wherever the path sits, including inside a disposable directory, and
/// it wins over every disposable pattern. Configuration cannot remove an entry.
/// <c>*.token</c> is not on this floor: names such as <c>design.token</c> are ordinary build
/// output and would hold ignored trees for a non-secret reason. <c>.aws/credentials</c> is covered
/// by <c>**/credentials*</c> (and descendants by <c>**/credentials*/**</c>), not a second glob.
/// A star after a secret directory name (<c>id_rsa*</c>, <c>id_ed25519*</c>, <c>credentials*</c>)
/// is the same conservative shape as the existing <c>id_rsa*</c> rule: <c>id_ed25519.pub</c> and
/// <c>credentials_helper.cs</c> hold the tree.
/// </summary>
public static class IgnoredSecretFloor
{
    /// <summary>
    /// Each file glob has a descendant companion (<c>/**</c>) so a secret-named directory keeps
    /// the files under it. Git lists the file, not the ancestor, so a leaf glob never matches them.
    /// </summary>
    public static IReadOnlyList<string> Patterns { get; } =
    [
        "**/appsettings.*.json", "**/appsettings.*.json/**",
        "**/.env*", "**/.env*/**",
        "**/*.pem", "**/*.pem/**",
        "**/*.key", "**/*.key/**",
        "**/id_rsa*", "**/id_rsa*/**",
        "**/id_ed25519*", "**/id_ed25519*/**",
        "**/*.pfx", "**/*.pfx/**",
        "**/*.p12", "**/*.p12/**",
        "**/*.jks", "**/*.jks/**",
        "**/*.kdbx", "**/*.kdbx/**",
        "**/credentials*", "**/credentials*/**",
        "**/.npmrc", "**/.npmrc/**",
        "**/.netrc", "**/.netrc/**",
        "**/.git-credentials", "**/.git-credentials/**",
        "**/.pgpass", "**/.pgpass/**",
        "**/secrets.json", "**/secrets.json/**",
        "**/kubeconfig", "**/kubeconfig/**",
        "**/.docker/config.json", "**/.docker/config.json/**",
    ];

    private static readonly Regex Compiled = new(
        "^(?:" + string.Join('|', Patterns.Select(pattern => Glob(Normalize(pattern)))) + @")\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public static bool Matches(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        var path = Normalize(relativePath);
        if (Escapes(path) || WorkspaceRetentionNames.HasControlCharacter(path))
            return false;
        return Compiled.IsMatch(path);
    }

    public static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    /// <summary>A leading slash, a drive letter, or any <c>..</c> segment.</summary>
    public static bool Escapes(string normalizedPath) =>
        normalizedPath.StartsWith('/')
        || normalizedPath.Length >= 2 && char.IsAsciiLetter(normalizedPath[0]) && normalizedPath[1] == ':'
        || normalizedPath.Split('/').Contains("..");

    /// <summary><c>**/</c> any leading directories, <c>/**</c> anything below, <c>*</c> and <c>?</c> within one segment.</summary>
    private static string Glob(string pattern)
    {
        var result = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                if ((i == 0 || pattern[i - 1] == '/') && i + 2 < pattern.Length && pattern[i + 2] == '/')
                {
                    result.Append("(?:[^/]*/)*");
                    i += 2;
                }
                else
                {
                    result.Append(".*");
                    i++;
                }
            }
            else if (c == '*')
                result.Append("[^/]*");
            else if (c == '?')
                result.Append("[^/]");
            else
                result.Append(Regex.Escape(c.ToString()));
        }
        return result.ToString();
    }
}
