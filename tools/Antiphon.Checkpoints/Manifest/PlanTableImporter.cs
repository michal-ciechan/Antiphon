using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public sealed class ImportResult
{
    public CheckpointManifest? Manifest { get; init; }
    public List<string> Warnings { get; init; } = [];
    public int ExitCode { get; init; }
    public string? Error { get; init; }
}

public static class PlanTableImporter
{
    private static readonly string[] Header =
        ["CP", "After", "Build", "Group", "Filter", "Covers", "Expect", "Min", "EstimatedMinutes"];

    private const int RowTimeoutCeilingMinutes = 45;

    public static ImportResult ImportFile(string path, string? planProvenance = null, bool? isWindows = null) =>
        ImportMarkdown(File.ReadAllText(path), planProvenance ?? path, isWindows);

    public static ImportResult ImportMarkdown(string markdown, string? planProvenance = null, bool? isWindows = null)
    {
        var warnings = new List<string>();
        var section = ExtractSection(markdown);
        if (section.Count == 0)
            return Fail("no ### Checkpoints section");

        List<string>? header = null;
        var data = new List<List<string>>();
        foreach (var line in section)
        {
            if (!line.TrimStart().StartsWith('|'))
            {
                if (header is not null)
                    break;
                continue;
            }

            var cells = SplitRow(line);
            if (cells.All(c => c.Length == 0 || c.Trim('-', ':', ' ').Length == 0))
                continue;
            if (header is null)
            {
                header = cells;
                continue;
            }

            data.Add(cells);
        }

        if (header is null)
            return Fail("### Checkpoints has no table");

        if (header.Count == 8 && header.Take(8).SequenceEqual(Header.Take(8)))
        {
            return Fail("legacy eight-column checkpoint table: rename the time column to EstimatedMinutes (CARD-0617) and derive Min from the roster");
        }

        if (header.Count < 9 || !header.Take(9).SequenceEqual(Header))
            return Fail("checkpoint table header must be CP, After, Build, Group, Filter, Covers, Expect, Min, EstimatedMinutes");

        var optional = new HashSet<string>(["EstimatedMinutesWindows", "Serial", "Environment"], StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in header.Skip(9))
        {
            if (!optional.Contains(name))
                return Fail($"unknown checkpoint column '{name}' (escape a literal filter pipe as \\|)");
            if (!seen.Add(name))
                return Fail($"duplicate checkpoint column '{name}'");
        }
        var windowsColumn = header.IndexOf("EstimatedMinutesWindows");
        var serialColumn = header.IndexOf("Serial");
        var environmentColumn = header.IndexOf("Environment");

        var manifest = new CheckpointManifest { Plan = planProvenance };
        var rowBuilds = new Dictionary<string, (string BuildId, string AfterKey)>(StringComparer.Ordinal);
        foreach (var cells in data)
        {
            if (cells.Count != header.Count)
                return Fail($"checkpoint row {cells.FirstOrDefault() ?? "?"} has {cells.Count} columns, expected {header.Count}; escape a literal Filter pipe as \\|");

            var id = cells[0].Trim();
            var after = AfterSelector.Expand(cells[1]).ToList();
            var buildCell = StripWrappingBackticks(cells[2].Trim());
            var group = cells[3].Trim();
            var filterCell = cells[4].Trim();
            var expectText = StripWrappingBackticks(cells[6].Trim());
            var minCell = cells[7].Trim();
            var estimateCell = cells[8].Trim();

            var row = new CheckpointSpec
            {
                Id = id,
                After = after,
                Group = group,
                ExpectText = expectText,
            };
            if (serialColumn >= 0)
            {
                var serial = cells[serialColumn].Trim();
                var parsedSerial = false;
                if (serial.Length > 0 && !bool.TryParse(serial, out parsedSerial))
                    return Fail($"{id}: Serial must be true, false, or blank");
                row.Serial = serial.Length > 0 && parsedSerial;
            }
            if (environmentColumn >= 0)
            {
                var environmentText = StripWrappingBackticks(cells[environmentColumn].Trim());
                if (environmentText.Length > 0 && !environmentText.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var entry in environmentText.Split(';'))
                    {
                        var trimmedEntry = entry.Trim();
                        var equals = trimmedEntry.IndexOf('=');
                        if (equals < 0)
                            return Fail($"{id}: Environment entry '{entry.Trim()}' must be NAME=value");
                        var name = trimmedEntry[..equals].Trim();
                        var value = trimmedEntry[(equals + 1)..];
                        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)
                            || name.IndexOfAny(['\0', '\n', '\r']) >= 0
                            || value.IndexOfAny(['\0', '\n', '\r']) >= 0)
                            return Fail($"{id}: Environment has invalid NAME=value entry '{entry.Trim()}'");
                        if (!row.Environment.TryAdd(name, value))
                            return Fail($"{id}: Environment duplicates '{name}'");
                    }
                }
            }
            if (!TryPositiveEstimate(estimateCell, out var estimate))
                return Fail($"{id}: EstimatedMinutes must be a positive integer with safe derived deadlines");
            row.EstimatedMinutes = estimate;
            if (windowsColumn >= 0 && windowsColumn < cells.Count)
            {
                var windowsCell = cells[windowsColumn].Trim();
                if (windowsCell.Length > 0 && !windowsCell.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryPositiveEstimate(windowsCell, out var windows))
                        return Fail($"{id}: EstimatedMinutesWindows must be a positive integer with safe derived deadlines");
                    row.EstimatedMinutesWindows = windows;
                    if (isWindows ?? OperatingSystem.IsWindows())
                        row.EstimatedMinutes = windows;
                }
            }
            row.TimeoutMinutes = RowTimeout.DeriveRowMinutes(row.EstimatedMinutes, null, 15);
            if (3L * row.EstimatedMinutes > RowTimeoutCeilingMinutes)
                warnings.Add($"{id}: 3 x EstimatedMinutes ({3L * row.EstimatedMinutes}) exceeds the {RowTimeoutCeilingMinutes} minute row-timeout ceiling");

            var payload = ExtractPayload(filterCell);
            if (Regex.IsMatch(payload, @"^same\s+as\s+CP-\d+$", RegexOptions.IgnoreCase))
                return Fail($"{id}: Filter must repeat the exact filter, not 'same as CP-n'");
            if (filterCell.StartsWith('`') && payload.StartsWith('/')
                && !filterCell.Equals("`" + payload + "`", StringComparison.Ordinal))
                return Fail($"{id}: Filter has trailing text; put NAME=value in Environment and keep the exact filter");
            if (payload.StartsWith('/'))
            {
                row.Filter = payload;
                row.Expect = RosterTokens(payload).ToList();
                if (row.Expect.Count(name => name.Contains("Land", StringComparison.Ordinal)) > 4)
                    warnings.Add($"{id}: names more than four land classes; split the row (about 120 tests)");
            }
            else
            {
                row.Command = payload;
            }

            if (!minCell.Equals("n/a", StringComparison.OrdinalIgnoreCase) && minCell.Length > 0)
            {
                if (!TryParseMin(minCell, isWindows ?? OperatingSystem.IsWindows(), out var min, out var minError))
                    return Fail($"{id}: {minError}");
                if (row.IsCommand)
                    return Fail($"{id}: command row Min must be n/a");
                row.MinExecuted = min;
            }
            else if (!row.IsCommand)
            {
                var floor = Regex.Match(expectText, @">=\s*(\d+)\s*executed");
                if (floor.Success)
                    row.MinExecuted = int.Parse(floor.Groups[1].Value);
            }

            if (row.IsCommand)
            {
                if (!StartsWithNa(buildCell) && buildCell.Length > 0 && !buildCell.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"{id}: command row build cell '{buildCell}' ignored");
            }
            else if (Regex.IsMatch(buildCell, @"^CP-\d+(?: \(-NoBuild\))?$", RegexOptions.IgnoreCase))
            {
                var priorId = Regex.Match(buildCell, @"^CP-\d+", RegexOptions.IgnoreCase).Value;
                if (!rowBuilds.TryGetValue(priorId, out var prior))
                    return Fail($"{id}: reuses {buildCell} which is not an earlier row");
                row.Build = prior.BuildId;
                var afterKey = string.Join(",", after);
                if (!string.Equals(prior.AfterKey, afterKey, StringComparison.Ordinal))
                {
                    return Fail($"{id}: After differs from {priorId}; a reused build requires the same After group");
                }
            }
            else
            {
                var match = Regex.Match(buildCell, @"^(?<project>.+?)\s*->\s*(?<output>bin-[A-Za-z0-9._-]+/)\s*$");
                if (!match.Success)
                    return Fail($"{id}: Build '{buildCell}' is not '<project> -> <bin-x/>', CP-n, or n/a");
                var output = match.Groups["output"].Value;
                var buildId = output.TrimEnd('/');
                var project = match.Groups["project"].Value.Trim().Trim('`');
                var existingBuild = manifest.Builds.FirstOrDefault(b => b.Id == buildId);
                if (existingBuild is not null && existingBuild.Project != project)
                    return Fail($"{id}: Build '{buildId}' conflicts with project '{existingBuild.Project}' versus '{project}'");
                if (existingBuild is null)
                {
                    manifest.Builds.Add(new BuildSpec { Id = buildId, Project = project, OutputPath = output });
                }

                row.Build = buildId;
            }

            if (!row.IsCommand && row.Build is not null)
            {
                var priorBuild = rowBuilds.Values.FirstOrDefault(value => value.BuildId == row.Build);
                if (priorBuild.BuildId is not null && priorBuild.AfterKey != string.Join(",", after))
                    return Fail($"{id}: After differs for build '{row.Build}'; use a fresh build output");
                rowBuilds[id] = (row.Build, string.Join(",", after));
            }
            manifest.Checkpoints.Add(row);
        }

        if (2L * manifest.Checkpoints.Sum(row => (long)(row.EstimatedMinutes ?? 0)) + 10 > int.MaxValue)
            return Fail("EstimatedMinutes: derived total deadline overflows");
        return new ImportResult { Manifest = manifest, Warnings = warnings, ExitCode = ExitCodes.Green };
    }

    private static bool TryPositiveEstimate(string cell, out int estimate) =>
        int.TryParse(cell, out estimate) && estimate > 0 && 3L * estimate <= int.MaxValue;

    public static bool TryParseMin(string cell, bool isWindows, out int? min, out string? error)
    {
        error = null;
        min = null;
        var text = cell.Trim();
        if (text.Length == 0 || text.Equals("n/a", StringComparison.OrdinalIgnoreCase))
            return true;
        if (int.TryParse(text, out var single))
        {
            min = single;
            return true;
        }

        var match = Regex.Match(text, @"^(?<linux>\d+)\s+linux\s*/\s*(?<windows>\d+)\s+windows$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            error = $"Min '{cell}' is not an integer, n/a, or '<n> linux / <n> windows'";
            return false;
        }

        min = int.Parse(isWindows ? match.Groups["windows"].Value : match.Groups["linux"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    public static IReadOnlyList<string> RosterTokens(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return [];
        if (filter.Contains("[Category=", StringComparison.Ordinal))
            return [];

        var parens = Regex.Matches(filter, @"\(([A-Za-z_][A-Za-z0-9_]*)\*\)");
        if (parens.Count > 0)
        {
            var names = new List<string>();
            foreach (Match match in parens)
            {
                var name = match.Groups[1].Value;
                if (!names.Contains(name))
                    names.Add(name);
            }

            return names;
        }

        foreach (var part in filter.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part is "*" || part.Contains('[') || part.Contains('|') || part.Contains('('))
                continue;
            if (Regex.IsMatch(part, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                return [part];
        }

        return [];
    }

    private static ImportResult Fail(string message) =>
        new() { ExitCode = ExitCodes.Invalid, Error = message };

    private static List<string> ExtractSection(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inside = false;
        var section = new List<string>();
        foreach (var line in lines)
        {
            if (!inside)
            {
                if (line.Trim().Equals("### Checkpoints", StringComparison.Ordinal))
                    inside = true;
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal) || line.StartsWith("## ", StringComparison.Ordinal))
                break;
            section.Add(line);
        }

        return section;
    }

    public static List<string> SplitRow(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\' && i + 1 < line.Length && line[i + 1] == '|')
            {
                sb.Append('|');
                i++;
                continue;
            }

            if (c == '|')
            {
                cells.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        cells.Add(sb.ToString().Trim());
        if (cells.Count > 0 && cells[0].Length == 0)
            cells.RemoveAt(0);
        if (cells.Count > 0 && cells[^1].Length == 0)
            cells.RemoveAt(cells.Count - 1);
        return cells;
    }

    private static string ExtractPayload(string cell)
    {
        var trimmed = cell.Trim();
        var spans = Regex.Matches(trimmed, "`([^`]*)`");
        if (spans.Count >= 1)
        {
            var first = spans[0].Groups[1].Value.Trim();
            if (first.StartsWith('/'))
                return first;
        }

        return StripWrappingBackticks(trimmed);
    }

    private static string StripWrappingBackticks(string cell)
    {
        var text = cell.Trim();
        if (text.Length >= 2 && text.StartsWith('`') && text.EndsWith('`'))
            text = text[1..^1].Trim();
        return text;
    }

    private static bool StartsWithNa(string cell) =>
        cell.Equals("n/a", StringComparison.OrdinalIgnoreCase)
        || cell.StartsWith("n/a", StringComparison.OrdinalIgnoreCase)
        || cell.StartsWith("N/A", StringComparison.Ordinal);
}
