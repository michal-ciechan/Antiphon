using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.SessionRunner;

public enum RunnerIgnoredContentKind { Protected, Evidence, Disposable }

/// <summary>Runner-local policy witnesses: C1032_InspectionPolicyParity.</summary>
public sealed class RunnerWorkspaceIgnoredContent
{
    private readonly Regex _protected;
    private readonly Regex _evidence;
    private readonly Regex _disposable;
    private readonly Regex _antiphonDisposable;

    public RunnerWorkspaceIgnoredContent(IReadOnlyList<string>? disposablePatterns = null)
    {
        _protected = Compile([
            "**/.claude/**", "**/appsettings.*.json", "**/*.user", "**/*.local.json",
            "**/.antiphon/report.md", "**/.antiphon/deliverables/**", "**/.env*", "**/.env*/**",
            "logs/**", "backups/**", ".superpowers/**", ".memsearch/**", "tests/Antiphon.E2E/TestOutput/**",
        ]);
        _evidence = Compile([".antiphon/task-????????.md", ".antiphon/**/*.trx", ".antiphon/*checkpoints*/**"]);
        var patterns = disposablePatterns ?? [
            "**/bin/**", "**/obj/**", "**/bin-*/**", "**/node_modules/**", "**/dist/**",
            "**/storybook-static/**", "**/.tmp/**", "**/out/**", ".antiphon-cache/**",
            ".antiphon/inbox/*.md", ".antiphon/task-*-brief.md", ".antiphon/task-*-refinement-*.md",
        ];
        _disposable = Compile(patterns);
        _antiphonDisposable = Compile(patterns.Where(NamesAntiphon));
    }

    public RunnerIgnoredContentKind Classify(string raw)
    {
        var path = Normalize(raw);
        if (string.IsNullOrWhiteSpace(path) || Escapes(path) || _protected.IsMatch(path))
            return RunnerIgnoredContentKind.Protected;
        if (_evidence.IsMatch(path)) return RunnerIgnoredContentKind.Evidence;
        if ((NamesAntiphon(path) ? _antiphonDisposable : _disposable).IsMatch(path))
            return RunnerIgnoredContentKind.Disposable;
        return RunnerIgnoredContentKind.Protected;
    }

    private static bool NamesAntiphon(string path) => Normalize(path).Split('/').Any(
        p => p.Equals(".antiphon", StringComparison.OrdinalIgnoreCase));
    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        return normalized;
    }
    private static bool Escapes(string path) => path.StartsWith('/')
        || path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'
        || path.Split('/').Contains("..");

    private static Regex Compile(IEnumerable<string> patterns)
    {
        var alternatives = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Glob(Normalize(p.Trim()))).ToArray();
        return new Regex(alternatives.Length == 0 ? "(?!)" : "^(?:" + string.Join('|', alternatives) + ")$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
    private static string Glob(string pattern)
    {
        var result = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                if ((i == 0 || pattern[i - 1] == '/') && i + 2 < pattern.Length && pattern[i + 2] == '/')
                { result.Append("(?:[^/]*/)*"); i += 2; }
                else { result.Append(".*"); i++; }
            }
            else if (c == '*') result.Append("[^/]*");
            else if (c == '?') result.Append("[^/]");
            else result.Append(Regex.Escape(c.ToString()));
        }
        return result.ToString();
    }
}
