using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Tests.Infrastructure;

internal sealed record DockerStage(string Name, string From, string Body);

internal static class DockerStackDocuments
{
    public static string RepoRoot { get; } = FindRoot();

    public static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    public static bool Excluded(string ignoreText, string relativePath)
    {
        var path = relativePath.Replace('\\', '/').TrimStart('/');
        var excluded = false;
        foreach (var raw in ignoreText.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
                continue;
            var negate = line[0] == '!';
            if (negate)
                line = line[1..];
            if (Matches(line, path))
                excluded = !negate;
        }

        return excluded;
    }

    public static IReadOnlyList<DockerStage> Stages(string dockerfile)
    {
        var lines = dockerfile.Replace("\r\n", "\n").Split('\n');
        var stages = new List<DockerStage>();
        string? name = null;
        string? from = null;
        var body = new StringBuilder();
        void Flush()
        {
            if (name is null || from is null)
                return;
            stages.Add(new DockerStage(name, from, body.ToString()));
            body.Clear();
        }

        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^\s*FROM\s+(\S+)(?:\s+AS\s+(\S+))?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                Flush();
                from = match.Groups[1].Value;
                name = match.Groups[2].Success ? match.Groups[2].Value : from;
                continue;
            }

            body.AppendLine(line);
        }

        Flush();
        return stages;
    }

    public static string Closure(IReadOnlyList<DockerStage> stages, string name)
    {
        var map = stages.GroupBy(stage => stage.Name).ToDictionary(group => group.Key, group => group.Last());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var text = new StringBuilder();
        var stack = new Stack<string>();
        stack.Push(name);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current) || !map.TryGetValue(current, out var stage))
                continue;
            text.AppendLine(stage.From);
            text.AppendLine(stage.Body);
            if (map.ContainsKey(stage.From))
                stack.Push(stage.From);
        }

        return text.ToString();
    }

    public static string Service(string yaml, string service)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => line == "  " + service + ":");
        if (start < 0)
            throw new InvalidOperationException("missing service " + service);
        var block = new StringBuilder();
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].Length > 0 && lines[i][0] != ' ' && lines[i][0] != '\t')
                break;
            if (lines[i].StartsWith("  ", StringComparison.Ordinal) && !lines[i].StartsWith("   ", StringComparison.Ordinal))
                break;
            block.AppendLine(lines[i]);
        }

        return block.ToString();
    }

    public static string Env(string block, string key)
    {
        var prefix = key + ":";
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            return line[prefix.Length..].Trim().Trim('"');
        }

        throw new InvalidOperationException("missing env " + key);
    }

    public static bool EnvFalse(string block, string key) =>
        string.Equals(Env(block, key), "false", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> List(string block, string key)
    {
        var lines = block.Replace("\r\n", "\n").Split('\n');
        var values = new List<string>();
        var capture = false;
        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (!capture)
            {
                if (trimmed == key + ":")
                    capture = true;
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                values.Add(trimmed[2..].Trim().Trim('"'));
                continue;
            }

            // A YAML comment between items is not the end of the list.
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            break;
        }

        return values;
    }

    public static string VolumeDestination(string spec)
    {
        var parts = spec.Split(':');
        return parts.Length > 1 ? parts[1] : spec;
    }

    private static bool Matches(string pattern, string path)
    {
        var directory = pattern.EndsWith('/');
        pattern = pattern.TrimEnd('/');
        if (!pattern.Contains('/'))
            pattern = "**/" + pattern;
        pattern = pattern.TrimStart('/');
        var regex = "^" + ToRegex(pattern) + (directory ? "(?:/.*)?$" : "(?:/.*)?$");
        return Regex.IsMatch(path, regex, RegexOptions.CultureInvariant);
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

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root was not found");
    }
}
