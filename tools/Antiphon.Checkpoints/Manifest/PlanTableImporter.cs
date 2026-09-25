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

    public static ImportResult ImportFile(string path, string? planProvenance = null) =>
        ImportMarkdown(File.ReadAllText(path), planProvenance ?? path);

    public static ImportResult ImportMarkdown(string markdown, string? planProvenance = null)
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

        var manifest = new CheckpointManifest { Plan = planProvenance };
        var rowBuilds = new Dictionary<string, (string BuildId, string AfterKey)>(StringComparer.Ordinal);
        var relax = false;

        foreach (var cells in data)
        {
            if (cells.Count < 9)
                return Fail("checkpoint row has fewer than 9 columns: " + string.Join(" | ", cells));

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
            if (int.TryParse(estimateCell, out var estimate))
            {
                row.EstimatedMinutes = estimate;
                row.TimeoutMinutes = RowTimeout.DeriveRowMinutes(estimate, null, 15);
                if (3 * estimate > RowTimeoutCeilingMinutes)
                    warnings.Add($"{id}: 3 x EstimatedMinutes ({3 * estimate}) exceeds the {RowTimeoutCeilingMinutes} minute row-timeout ceiling");
            }

            var payload = ExtractPayload(filterCell);
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
                if (!int.TryParse(minCell, out var min))
                    return Fail($"{id}: Min '{minCell}' is not an integer or n/a");
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
            else if (Regex.IsMatch(buildCell, @"^CP-\d+$", RegexOptions.IgnoreCase))
            {
                if (!rowBuilds.TryGetValue(buildCell, out var prior))
                    return Fail($"{id}: reuses {buildCell} which is not an earlier row");
                row.Build = prior.BuildId;
                var afterKey = string.Join(",", after);
                if (!string.Equals(prior.AfterKey, afterKey, StringComparison.Ordinal))
                {
                    relax = true;
                    warnings.Add($"{id}: reuses {buildCell} across a different After; one build is shared and the row runs --no-build");
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
                if (manifest.Builds.All(b => b.Id != buildId))
                {
                    manifest.Builds.Add(new BuildSpec { Id = buildId, Project = project, OutputPath = output });
                }

                row.Build = buildId;
            }

            if (!row.IsCommand && row.Build is not null)
                rowBuilds[id] = (row.Build, string.Join(",", after));
            manifest.Checkpoints.Add(row);
        }

        manifest.RelaxSharedBuildAfter = relax;
        return new ImportResult { Manifest = manifest, Warnings = warnings, ExitCode = ExitCodes.Green };
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
