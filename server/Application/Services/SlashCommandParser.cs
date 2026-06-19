using YamlDotNet.RepresentationModel;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Pure helpers for reading a one-line description out of a Claude command/skill markdown file.
/// Files may have YAML frontmatter (<c>--- ... ---</c>) with a <c>description:</c>, or none at all
/// (some skills only have a markdown body). The fallback chain is: frontmatter <c>description</c> →
/// first non-empty body line (leading <c>#</c> stripped) → the supplied fallback (folder/file name).
/// All parsing is defensive: a malformed file degrades to the next fallback, never throws.
/// </summary>
public static class SlashCommandParser
{
    private const int MaxDescriptionChars = 200;

    public static string Describe(string? content, string fallback)
    {
        if (string.IsNullOrWhiteSpace(content))
            return fallback;

        var (frontmatter, body) = SplitFrontmatter(content);

        if (frontmatter is not null)
        {
            var description = TryGetYamlScalar(frontmatter, "description");
            if (!string.IsNullOrWhiteSpace(description))
                return Truncate(description.Trim());
        }

        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            // Skip a markdown heading marker but keep its text ("# Title" -> "Title").
            line = line.TrimStart('#').Trim();
            if (line.Length > 0)
                return Truncate(line);
        }

        return fallback;
    }

    /// <summary>
    /// Splits leading <c>--- ... ---</c> YAML frontmatter from the markdown body. Returns
    /// <c>(null, content)</c> when there is no frontmatter.
    /// </summary>
    public static (string? Frontmatter, string Body) SplitFrontmatter(string content)
    {
        // Normalize newlines so the delimiter scan is simple.
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal) && normalized != "---")
            return (null, content);

        var lines = normalized.Split('\n');
        // lines[0] == "---"; find the closing delimiter.
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                var frontmatter = string.Join('\n', lines[1..i]);
                var body = i + 1 < lines.Length ? string.Join('\n', lines[(i + 1)..]) : string.Empty;
                return (frontmatter, body);
            }
        }

        // Unterminated frontmatter — treat the whole thing as body.
        return (null, content);
    }

    private static string? TryGetYamlScalar(string yaml, string key)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.Count == 0)
                return null;
            if (stream.Documents[0].RootNode is not YamlMappingNode root)
                return null;
            if (root.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar)
                return scalar.Value;
            return null;
        }
        catch
        {
            // Malformed YAML — fall back to the body.
            return null;
        }
    }

    private static string Truncate(string value) =>
        value.Length <= MaxDescriptionChars ? value : value[..MaxDescriptionChars].TrimEnd() + "…";
}
