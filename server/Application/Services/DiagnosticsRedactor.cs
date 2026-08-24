using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Best-effort pattern redaction over diagnostics-bundle text (CARD-0179 R1).
/// It is not a guarantee: the operator should glance before uploading. Path
/// redaction is skipped when <paramref name="includePaths"/> is true.
/// </summary>
public sealed partial class DiagnosticsRedactor
{
    private readonly bool _includePaths;
    private readonly IReadOnlyList<(string Path, string Token)> _projectPaths;
    private readonly string? _homePath;
    private readonly string? _homePathForward;

    public DiagnosticsRedactor(bool includePaths, IEnumerable<string>? projectDirectories = null)
    {
        _includePaths = includePaths;
        _homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _homePathForward = string.IsNullOrEmpty(_homePath) ? null : _homePath.Replace('\\', '/');
        _projectPaths = (projectDirectories ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().TrimEnd('\\', '/'))
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Length)
            .Select((p, i) => (p, $"<project-{i + 1}>"))
            .ToList();
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var s = KeyPlaceholderRegex().Replace(text, "{{key:***}}");
        s = SkKeyRegex().Replace(s, "sk-***");
        s = SlackTokenRegex().Replace(s, "xox*-***");
        s = GitHubPatRegex().Replace(s, "ghp_***");
        s = GitHubFineGrainedPatRegex().Replace(s, "github_pat_***");
        s = BearerRegex().Replace(s, "Bearer ***");
        s = TelegramBotTokenRegex().Replace(s, "***TELEGRAM_TOKEN***");
        s = ConnectionStringPasswordRegex().Replace(s, "Password=***");

        if (!_includePaths)
        {
            if (!string.IsNullOrEmpty(_homePath))
                s = ReplaceOrdinalIgnoreCase(s, _homePath, "~");
            if (!string.IsNullOrEmpty(_homePathForward) && _homePathForward != _homePath)
                s = ReplaceOrdinalIgnoreCase(s, _homePathForward, "~");
            s = WindowsUserProfileRegex().Replace(s, "~");
            s = UnixUserProfileRegex().Replace(s, "~");

            foreach (var (path, token) in _projectPaths)
            {
                s = ReplaceOrdinalIgnoreCase(s, path, token);
                var forward = path.Replace('\\', '/');
                if (!string.Equals(forward, path, StringComparison.Ordinal))
                    s = ReplaceOrdinalIgnoreCase(s, forward, token);
            }
        }

        return s;
    }

    private static string ReplaceOrdinalIgnoreCase(string source, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(oldValue) || source.Length == 0)
            return source;

        var comparison = StringComparison.OrdinalIgnoreCase;
        var index = source.IndexOf(oldValue, comparison);
        if (index < 0)
            return source;

        var sb = new StringBuilder(source.Length);
        var last = 0;
        while (index >= 0)
        {
            sb.Append(source, last, index - last);
            sb.Append(newValue);
            last = index + oldValue.Length;
            index = source.IndexOf(oldValue, last, comparison);
        }

        sb.Append(source, last, source.Length - last);
        return sb.ToString();
    }

    // {{key:NAME}} API-key placeholders (CARD-0106).
    [GeneratedRegex(@"\{\{key:[^}]+\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPlaceholderRegex();

    // OpenAI / Anthropic-shaped secret keys.
    [GeneratedRegex(@"sk-[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex SkKeyRegex();

    [GeneratedRegex(@"xox[bp]-[A-Za-z0-9-]+", RegexOptions.CultureInvariant)]
    private static partial Regex SlackTokenRegex();

    [GeneratedRegex(@"ghp_[A-Za-z0-9]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubPatRegex();

    [GeneratedRegex(@"github_pat_[A-Za-z0-9_]{20,}", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubFineGrainedPatRegex();

    [GeneratedRegex(@"Bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"\d{8,}:[A-Za-z0-9_-]{35}", RegexOptions.CultureInvariant)]
    private static partial Regex TelegramBotTokenRegex();

    [GeneratedRegex(@"Password=[^;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringPasswordRegex();

    [GeneratedRegex(@"[A-Za-z]:\\Users\\[^\\/\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsUserProfileRegex();

    [GeneratedRegex(@"(?:/Users|/home)/[^\\/\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnixUserProfileRegex();
}
