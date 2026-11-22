using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Server.Domain.Enums;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Antiphon.Server.Application.Review.Checks;

/// <summary>
/// Parses one check file (design 9): YAML front matter between <c>---</c> lines, then sections that
/// start only at a column-0 line <c># @&lt;audience&gt;: &lt;name&gt;</c> outside a code fence. Every
/// other heading is content. Text before the first section is ignored with a warning. Pure.
/// </summary>
public static partial class ReviewCheckParser
{
    public static readonly IReadOnlyList<string> Statuses = ["candidate", "shadow", "active", "retired"];

    [GeneratedRegex("^# @(?<audience>[a-z][a-z0-9-]*): (?<name>\\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionMarker();

    /// <param name="text">The file.</param>
    /// <param name="source">Where it came from, for messages: a path such as <c>.antiphon/review/checks/concurrency/CHK-0142.md</c>.</param>
    /// <param name="category">The category when the front matter has none: the folder the file sits in.</param>
    public static ReviewCheckParseResult Parse(string text, string source, string? category = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length == 0 || lines[0].TrimEnd() != "---")
            return Fail($"{source}: a check starts with YAML front matter between '---' lines.");
        var close = Array.FindIndex(lines, 1, l => l.TrimEnd() == "---");
        if (close < 0)
            return Fail($"{source}: the front matter is not closed with '---'.");

        YamlMappingNode front;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(string.Join('\n', lines[1..close])));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
                return Fail($"{source}: the front matter must be a mapping.");
            front = mapping;
        }
        catch (YamlException ex)
        {
            return Fail($"{source}: the front matter is not valid YAML: {ex.Message}");
        }

        var id = Scalar(front, "id");
        var title = Scalar(front, "title");
        var severityText = Scalar(front, "severity");
        if (string.IsNullOrWhiteSpace(id))
            errors.Add($"{source}: 'id' is required.");
        if (string.IsNullOrWhiteSpace(title))
            errors.Add($"{source}: 'title' is required.");
        if (!Enum.TryParse<ReviewSeverity>(severityText, ignoreCase: true, out var severity) || !Enum.IsDefined(severity))
            errors.Add($"{source}: 'severity' must be nit, minor, major or blocker, not '{severityText}'.");
        var status = (Scalar(front, "status") ?? "active").Trim().ToLowerInvariant();
        if (!Statuses.Contains(status))
            errors.Add($"{source}: 'status' must be one of {string.Join(", ", Statuses)}, not '{status}'.");
        var requiresHumanText = Scalar(front, "requiresHuman");
        var requiresHuman = false;
        if (requiresHumanText is not null && !bool.TryParse(requiresHumanText, out requiresHuman))
            errors.Add($"{source}: 'requiresHuman' must be true or false.");

        var triggers = ReviewCheckTriggers.None;
        if (Child(front, "triggers") is { } triggerNode)
        {
            if (triggerNode is YamlMappingNode t)
                triggers = new ReviewCheckTriggers(List(t, "globs"), List(t, "languages"), List(t, "tags"), List(t, "symbols"));
            else
                errors.Add($"{source}: 'triggers' must be a mapping of globs, languages, tags and symbols.");
        }

        var sections = new List<ReviewCheckSection>();
        string? audience = null;
        string? name = null;
        var body = new StringBuilder();
        var fence = "";
        var preamble = false;

        void Flush()
        {
            if (audience is not null)
                sections.Add(new ReviewCheckSection(audience, name!, body.ToString().Trim('\n').TrimEnd()));
            body.Clear();
        }

        foreach (var line in lines[(close + 1)..])
        {
            if (fence.Length == 0 && SectionMarker().Match(line.TrimEnd()) is { Success: true } marker)
            {
                Flush();
                audience = marker.Groups["audience"].Value;
                name = marker.Groups["name"].Value.Trim();
                continue;
            }

            fence = FenceAfter(line, fence);
            if (audience is null)
            {
                preamble |= line.Trim().Length > 0;
                continue;
            }

            body.Append(line).Append('\n');
        }

        Flush();
        if (preamble)
            warnings.Add($"{source}: text before the first '# @audience: name' section is ignored.");
        if (!sections.Any(s => s.Audience == ReviewCheck.ModelAudience))
            warnings.Add($"{source}: no '# @model:' section; a reviewer gets only the title.");
        foreach (var duplicate in sections.GroupBy(s => (s.Audience, s.Name.ToLowerInvariant())).Where(g => g.Count() > 1))
            warnings.Add($"{source}: section '@{duplicate.Key.Audience}: {duplicate.First().Name}' appears {duplicate.Count()} times.");

        if (errors.Count > 0)
            return new ReviewCheckParseResult(null, errors, warnings);

        var check = new ReviewCheck(
            id!.Trim(),
            title!.Trim(),
            (Scalar(front, "category") ?? category ?? "general").Trim(),
            severity,
            List(front, "flags").Select(f => f.ToLowerInvariant()).ToList(),
            status,
            requiresHuman,
            triggers,
            List(front, "relatedFiles"),
            sections,
            PromptHash(sections),
            source);
        return new ReviewCheckParseResult(check, errors, warnings);

        ReviewCheckParseResult Fail(string error) => new(null, [error], warnings);
    }

    /// <summary>SHA-256 over the <c>@model</c> sections in order (name and body), lower-case hex.</summary>
    public static string PromptHash(IEnumerable<ReviewCheckSection> sections)
    {
        var canonical = new StringBuilder();
        foreach (var section in sections.Where(s => s.Audience == ReviewCheck.ModelAudience))
            canonical.Append("# @model: ").Append(section.Name).Append('\n').Append(section.Body).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    /// <summary>
    /// The fence open after <paramref name="line"/>: a run of three or more backticks or tildes, indented
    /// at most three spaces, opens one; a run of the same character at least as long closes it.
    /// </summary>
    private static string FenceAfter(string line, string open)
    {
        var trimmed = line.TrimStart(' ');
        if (line.Length - trimmed.Length > 3 || trimmed.Length < 3 || trimmed[0] is not ('`' or '~'))
            return open;
        var run = trimmed.TakeWhile(c => c == trimmed[0]).Count();
        if (run < 3)
            return open;
        var marker = new string(trimmed[0], run);
        if (open.Length == 0)
            return marker;
        return trimmed[0] == open[0] && run >= open.Length && trimmed[run..].Trim().Length == 0 ? "" : open;
    }

    private static YamlNode? Child(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? Scalar(YamlMappingNode node, string key) => Child(node, key) is YamlScalarNode scalar ? scalar.Value : null;

    /// <summary>A sequence of scalars, or one scalar as a one-item list.</summary>
    private static IReadOnlyList<string> List(YamlMappingNode node, string key) => Child(node, key) switch
    {
        YamlSequenceNode sequence => sequence.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").Where(v => v.Length > 0).ToList(),
        YamlScalarNode { Value: { Length: > 0 } single } => [single],
        _ => [],
    };
}
