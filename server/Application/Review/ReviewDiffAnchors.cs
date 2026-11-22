using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Review;

/// <summary>Where an anchor sits at the reviewed head: its line in the new file, and the diff hunk around it when it is in the diff.</summary>
public sealed record ReviewAnchorLocation(int Line, bool InDiff, string? Hunk);

/// <summary>
/// Resolves a finding's anchor (one verbatim code line, ABF 7.1) at the reviewed head (design 4.8):
/// first among the new-side lines of the unified diff, then in the whole file when the host can
/// read it. Trimmed equality wins over a trimmed substring match. Also splits a diff by file. Pure.
/// </summary>
public static partial class ReviewDiffAnchors
{
    public const int HunkContextLines = 3;

    [GeneratedRegex("^@@ -\\d+(?:,\\d+)? \\+(?<start>\\d+)(?:,\\d+)? @@", RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeader();

    /// <summary>The diff text per new path (<c>+++ b/&lt;path&gt;</c>, or the <c>diff --git</c> line for a deletion).</summary>
    public static IReadOnlyDictionary<string, string> Sections(string? diff)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(diff))
            return sections;
        string? path = null;
        var text = new StringBuilder();
        foreach (var line in Lines(diff))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                if (path is not null)
                    sections[path] = text.ToString();
                text.Clear();
                var b = line.LastIndexOf(" b/", StringComparison.Ordinal);
                path = b >= 0 ? line[(b + 3)..] : null;
            }
            else if (line.StartsWith("+++ ", StringComparison.Ordinal) && line.Length > 4 && line[4..] != "/dev/null")
            {
                path = line[4..].StartsWith("b/", StringComparison.Ordinal) ? line[6..] : line[4..];
            }

            text.Append(line).Append('\n');
        }

        if (path is not null)
            sections[path] = text.ToString();
        return sections;
    }

    public static ReviewAnchorLocation? Locate(string? diff, string path, string anchor, string? fileText = null)
    {
        var wanted = anchor.Trim();
        if (wanted.Length == 0)
            return null;
        if (Sections(diff).TryGetValue(path, out var section) && InDiff(section, wanted) is { } inDiff)
            return inDiff;
        if (fileText is null)
            return null;
        var lines = Lines(fileText).ToList();
        var index = lines.FindIndex(l => l.Trim() == wanted);
        if (index < 0)
            index = lines.FindIndex(l => l.Contains(wanted, StringComparison.Ordinal));
        return index < 0 ? null : new ReviewAnchorLocation(index + 1, false, null);
    }

    private static ReviewAnchorLocation? InDiff(string section, string wanted)
    {
        var lines = Lines(section).ToList();
        ReviewAnchorLocation? contained = null;
        var newLine = 0;
        var hunkStart = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (HunkHeader().Match(line) is { Success: true } header)
            {
                newLine = int.Parse(header.Groups["start"].Value);
                hunkStart = i;
                continue;
            }

            if (hunkStart < 0 || line.Length == 0 && i == lines.Count - 1)
                continue;
            if (line.StartsWith('-') || line.StartsWith('\\'))
                continue;
            if (!(line.StartsWith('+') || line.StartsWith(' ') || line.Length == 0))
                continue;

            var content = line.Length == 0 ? "" : line[1..];
            if (content.Trim() == wanted)
                return new ReviewAnchorLocation(newLine, true, Hunk(lines, hunkStart, i));
            if (contained is null && content.Contains(wanted, StringComparison.Ordinal))
                contained = new ReviewAnchorLocation(newLine, true, Hunk(lines, hunkStart, i));
            newLine++;
        }

        return contained;
    }

    /// <summary>The hunk header, then up to <see cref="HunkContextLines"/> lines either side of the anchor.</summary>
    private static string Hunk(List<string> lines, int header, int at)
    {
        var from = Math.Max(header + 1, at - HunkContextLines);
        var end = at;
        for (var i = at + 1; i < lines.Count && i <= at + HunkContextLines; i++)
        {
            if (HunkHeader().IsMatch(lines[i]) || lines[i].StartsWith("diff --git ", StringComparison.Ordinal))
                break;
            end = i;
        }

        var text = new StringBuilder(lines[header]).Append('\n');
        for (var i = from; i <= end; i++)
            text.Append(lines[i]).Append('\n');
        return text.ToString().TrimEnd('\n');
    }

    private static IEnumerable<string> Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}
