using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Server.Application.Review.Abf;

/// <summary>Names and the name fold (spec 3.3).</summary>
public static class AbfNames
{
    /// <summary>Deletes <c>-</c> and <c>_</c> and lower-cases ASCII letters.</summary>
    public static string Fold(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c is '-' or '_')
                continue;
            builder.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
        }

        return builder.ToString();
    }

    /// <summary>The fold used for the row-as-field repair: words separated by single spaces.</summary>
    internal static string FoldWords(string words) => Fold(words.Replace(" ", "", StringComparison.Ordinal));

    internal static bool IsName(string text) => AbfLines.NamePattern.IsMatch(text);
}

internal readonly record struct AbfFence(char Char, int Length);

/// <summary>Text preparation and line forms (spec 3.1, 3.2, 3.6).</summary>
internal static class AbfLines
{
    private const RegexOptions Options = RegexOptions.CultureInvariant;

    internal static readonly Regex NamePattern = new("^[A-Za-z][A-Za-z0-9_-]*$", Options);

    internal static readonly Regex TypeRefPattern = new(
        "^(?<type>[A-Za-z][A-Za-z0-9-]*)/(?<major>[0-9]+)(?:\\.(?<minor>[0-9]+))?$", Options);

    private static readonly Regex HeaderPattern = new(
        "^@abf\\s+(?<syntax>\\S+)\\s+(?<type>[A-Za-z][A-Za-z0-9-]*)/(?<major>[0-9]+)(?:\\.(?<minor>[0-9]+))?\\s*$",
        Options | RegexOptions.IgnoreCase);

    private static readonly Regex RecordPattern = new(
        "^===\\s+(?<type>[A-Za-z][A-Za-z0-9_-]*)(?:\\s+(?<id>\\S+)(?:\\s+(?<stray>.*?))?)?\\s*$", Options);

    private static readonly Regex LegacyHeadingPattern = new(
        "^--- (?<words>[A-Za-z][A-Za-z0-9_-]*(?: [A-Za-z0-9_-]+)*) ---\\s*$", Options);

    private static readonly Regex FieldHeadingPattern = new("^--- (?<name>[A-Za-z][A-Za-z0-9_-]*)\\s*$", Options);

    private static readonly Regex FieldPattern = new(
        "^(?<key>[A-Za-z][A-Za-z0-9_-]*)[ \\t]*:(?<rest>(?:\\s.*)?)$", Options);

    private static readonly Regex FenceOpenPattern = new("^ {0,3}(?<run>`{3,}|~{3,})(?<info>.*)$", Options);

    private static readonly Regex FenceClosePattern = new("^ {0,3}(?<run>`{3,}|~{3,})\\s*$", Options);

    private static readonly Regex RowAsFieldPattern = new(
        "^(?<words>[A-Za-z][A-Za-z0-9_-]*(?: [A-Za-z0-9_-]+)*)[ \\t]*:(?<value>.*)$", Options);

    private static readonly Regex BulletPattern = new("^(?:[-*+]|[0-9]+\\.) +", Options);

    /// <summary>
    /// Removes a leading BOM, splits on the .NET line-break set and drops the empty line after a
    /// final line break (spec 3.1). With <paramref name="reportEnvelope"/>, cuts at the last
    /// report-token-shaped line (spec 6.1).
    /// </summary>
    public static string[] Prepare(string? text, bool reportEnvelope)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        if (text[0] == '﻿')
            text = text[1..];
        var normalized = text.ReplaceLineEndings("\n");
        var lines = normalized.Split('\n');
        if (normalized.EndsWith('\n'))
            lines = lines[..^1];
        if (!reportEnvelope)
            return lines;

        var token = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[antiphon-report:", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(']'))
                token = i;
        }

        return token < 0 ? lines : lines[..token];
    }

    public static bool IsBlank(string line) => string.IsNullOrWhiteSpace(line);

    public readonly record struct Header(string Syntax, string Type, int Major, int Minor);

    public static bool TryHeader(string line, out Header header)
    {
        header = default;
        var match = HeaderPattern.Match(line);
        if (!match.Success || !int.TryParse(match.Groups["major"].Value, out var major))
            return false;
        var minor = 0;
        if (match.Groups["minor"].Success && !int.TryParse(match.Groups["minor"].Value, out minor))
            return false;
        header = new Header(match.Groups["syntax"].Value, match.Groups["type"].Value.ToLowerInvariant(), major, minor);
        return true;
    }

    public readonly record struct RecordLine(string Type, string? Id, string? Stray, bool IsEnd);

    public static bool TryRecord(string line, out RecordLine record)
    {
        record = default;
        var match = RecordPattern.Match(line);
        if (!match.Success)
            return false;
        var type = match.Groups["type"].Value;
        var id = match.Groups["id"].Success ? match.Groups["id"].Value : null;
        var stray = match.Groups["stray"].Success && match.Groups["stray"].Value.Length > 0 ? match.Groups["stray"].Value : null;
        if (AbfNames.Fold(type) == "end")
        {
            // Anything after `end` is stray text.
            var after = id is null ? null : stray is null ? id : id + " " + stray;
            record = new RecordLine(type, null, after, true);
            return true;
        }

        record = new RecordLine(type, id, stray, false);
        return true;
    }

    public static bool IsEnd(string line) => TryRecord(line, out var record) && record.IsEnd;

    /// <summary>A <c>--- words ---</c> line. Returns the words.</summary>
    public static bool TryLegacyHeading(string line, out string words)
    {
        var match = LegacyHeadingPattern.Match(line);
        words = match.Success ? match.Groups["words"].Value : "";
        return match.Success;
    }

    public static bool TryFieldHeading(string line, out string name)
    {
        var match = FieldHeadingPattern.Match(line);
        name = match.Success ? match.Groups["name"].Value : "";
        return match.Success;
    }

    public static bool TryField(string line, out string key, out string value)
    {
        var match = FieldPattern.Match(line);
        key = match.Success ? match.Groups["key"].Value : "";
        value = match.Success ? match.Groups["rest"].Value.Trim() : "";
        return match.Success;
    }

    /// <summary>Header, record, end, field heading, legacy heading or field line (the escape set, 3.6).</summary>
    public static bool IsEscapable(string line) =>
        TryHeader(line, out _) || TryRecord(line, out _) || TryLegacyHeading(line, out _)
        || TryFieldHeading(line, out _) || TryField(line, out _, out _);

    /// <summary>A line that would be structural inside a multi-line field (3.4), other than <c>=== end</c>.</summary>
    public static bool IsStructuralInField(string line) =>
        TryHeader(line, out _) || TryRecord(line, out _) || TryLegacyHeading(line, out _) || TryFieldHeading(line, out _);

    /// <summary>
    /// Backslash stuffing: a line of one or more <c>\</c> followed by a structural line loses exactly
    /// one leading <c>\</c>.
    /// </summary>
    public static bool TryUnescape(string line, out string unescaped)
    {
        unescaped = line;
        if (line.Length < 2 || line[0] != '\\')
            return false;
        var stripped = line[1..];
        if (!IsEscapable(stripped.TrimStart('\\')))
            return false;
        unescaped = stripped;
        return true;
    }

    public static bool TryFenceOpen(string line, out AbfFence fence)
    {
        fence = default;
        var match = FenceOpenPattern.Match(line);
        if (!match.Success)
            return false;
        var run = match.Groups["run"].Value;
        if (run[0] == '`' && match.Groups["info"].Value.Contains('`'))
            return false;
        fence = new AbfFence(run[0], run.Length);
        return true;
    }

    public static bool IsFenceClose(string line, AbfFence fence)
    {
        var match = FenceClosePattern.Match(line);
        if (!match.Success)
            return false;
        var run = match.Groups["run"].Value;
        return run[0] == fence.Char && run.Length >= fence.Length;
    }

    /// <summary>Row-as-field (3.5): words before the first colon, then the value.</summary>
    public static bool TryRowAsField(string row, out string words, out string value)
    {
        var match = RowAsFieldPattern.Match(row);
        words = match.Success ? match.Groups["words"].Value : "";
        value = match.Success ? match.Groups["value"].Value.Trim() : "";
        return match.Success;
    }

    public static bool TryRemoveBullet(string row, out string rest)
    {
        var match = BulletPattern.Match(row);
        rest = match.Success ? row[match.Length..].TrimStart() : row;
        return match.Success;
    }

    /// <summary>
    /// Splits a row into columns (3.5). Each column but the last takes the next word or quoted
    /// string; the last takes the rest, unquoted only when it is wholly one quoted string.
    /// Returns false for an unclosed quote (<c>bad-row</c>).
    /// </summary>
    public static bool TrySplitColumns(string row, int columnCount, out List<string> columns)
    {
        columns = [];
        var position = 0;
        for (var c = 0; c < columnCount; c++)
        {
            while (position < row.Length && char.IsWhiteSpace(row[position]))
                position++;
            if (position >= row.Length)
                return true;
            var last = c == columnCount - 1;
            if (last)
            {
                var rest = row[position..].Trim();
                columns.Add(TryUnquoteWhole(rest, out var unquoted) ? unquoted : rest);
                return true;
            }

            if (row[position] == '"')
            {
                if (!TryReadQuoted(row, position, out var value, out var end))
                    return false;
                columns.Add(value);
                position = end;
                continue;
            }

            var start = position;
            while (position < row.Length && !char.IsWhiteSpace(row[position]))
                position++;
            columns.Add(row[start..position]);
        }

        return true;
    }

    private static bool TryUnquoteWhole(string text, out string value)
    {
        value = text;
        if (text.Length < 2 || text[0] != '"')
            return false;
        if (!TryReadQuoted(text, 0, out var inner, out var end) || end != text.Length)
            return false;
        value = inner;
        return true;
    }

    private static bool TryReadQuoted(string text, int start, out string value, out int end)
    {
        var builder = new StringBuilder();
        var i = start + 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
            {
                builder.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (c == '"')
            {
                value = builder.ToString();
                end = i + 1;
                return true;
            }

            builder.Append(c);
            i++;
        }

        value = "";
        end = text.Length;
        return false;
    }
}
