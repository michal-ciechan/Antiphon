using System.Text;
using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-1179. text=auto checks session-runner-grok shell and .mjs out as CRLF when
// core.autocrlf is true, and the session-testing image then fails sh -n on the copy.
[Category("Unit")]
public sealed class SessionRunnerGrokLineEndingContractTests
{
    private static readonly string[] RequiredRules =
    [
        "docker/session-runner-grok/**/*.sh text eol=lf",
        "docker/session-runner-grok/**/*.mjs text eol=lf",
    ];

    [Test]
    public void Session_runner_grok_shell_and_mjs_are_forced_lf()
    {
        var attributes = DockerStackDocuments.Read(".gitattributes");
        var problems = Problems(attributes, DockerStackDocuments.RepoRoot);
        problems.ShouldBeEmpty(string.Join("\n", problems));
    }

    private static List<string> Problems(string attributes, string repoRoot)
    {
        var problems = new List<string>();
        var active = ActiveLines(attributes);
        foreach (var rule in RequiredRules)
        {
            if (!active.Contains(rule, StringComparer.Ordinal))
                problems.Add("missing rule: " + rule);
        }

        var root = Path.Combine(repoRoot, "docker", "session-runner-grok");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .Where(path => path.EndsWith(".sh", StringComparison.Ordinal) || path.EndsWith(".mjs", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            problems.Add("no shell or .mjs files under docker/session-runner-grok");

        foreach (var file in files)
        {
            var eol = EffectiveEol(attributes, file);
            if (!string.Equals(eol, "lf", StringComparison.Ordinal))
                problems.Add(file + " eol=" + (eol ?? "unspecified"));
        }

        return problems;
    }

    private static IReadOnlyList<string> ActiveLines(string attributes) =>
        attributes.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line[0] != '#')
            .ToArray();

    // Last matching eol= wins, which is how git applies .gitattributes.
    private static string? EffectiveEol(string attributes, string path)
    {
        string? eol = null;
        foreach (var line in ActiveLines(attributes))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !GlobCovers(parts[0], path))
                continue;
            foreach (var attribute in parts.Skip(1))
            {
                if (attribute is "eol=lf" or "eol=crlf")
                    eol = attribute["eol=".Length..];
                else if (attribute == "-eol")
                    eol = null;
            }
        }

        return eol;
    }

    private static bool GlobCovers(string pattern, string path)
    {
        if (pattern.StartsWith('!') || pattern.EndsWith('/'))
            return false;
        if (!pattern.Contains('/'))
            pattern = "**/" + pattern;
        pattern = pattern.TrimStart('/');
        return Regex.IsMatch(path, "^" + ToRegex(pattern) + "$", RegexOptions.CultureInvariant);
    }

    private static string ToRegex(string pattern)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                i++;
                if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                {
                    i++;
                    builder.Append("(?:.*/)?");
                }
                else
                {
                    builder.Append(".*");
                }
            }
            else if (pattern[i] == '*')
            {
                builder.Append("[^/]*");
            }
            else if (pattern[i] == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(pattern[i].ToString()));
            }
        }

        return builder.ToString();
    }
}
