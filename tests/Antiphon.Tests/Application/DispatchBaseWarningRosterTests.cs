using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1155. The later roster must name every current dispatch-warning E2E method
/// and keep queue insert, Sent, or ack from discharging that row.
/// </summary>
[Category("Unit")]
public class DispatchBaseWarningRosterTests
{
    private const string PlanRelative =
        "docs/superpowers/plans/2026-10-11-card-1155-dispatch-warning-e2e-roster-plan.md";

    private const string BatchPlanRelative =
        "docs/superpowers/plans/2026-10-08-test-hardening-batch-1137-1134-1130-1126-1152-plan.md";

    private const string TestsRelative =
        "tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs";

    private const string PinFilter =
        "/*/*/DispatchBaseWarningRosterTests/C1155_RosterNamesEveryDispatchWarningE2EMethod*";

    [Test]
    public void C1155_RosterNamesEveryDispatchWarningE2EMethod()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var planPath = Path.Combine(root, PlanRelative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(planPath).ShouldBeTrue(planPath);
        var plan = File.ReadAllText(planPath).Replace("\r\n", "\n");
        plan.ShouldContain("complete UserPrompt transcript from the real producer to the recipient");
        plan.ShouldContain("Queue insert, Sent, or ack alone does not discharge");
        plan.ShouldContain("LandDeliveryFixture.InitializeAsync requires Windows");
        plan.ShouldContain("CP-1 does not discharge");
        plan.ShouldContain("CARD-0550");
        plan.ShouldContain("CP-24");

        var batch = File.ReadAllText(Path.Combine(root, BatchPlanRelative.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n");
        Section(batch, "Checkpoints").ShouldNotContain("DispatchBaseWarningDeliveryE2ETests");

        var source = File.ReadAllText(Path.Combine(root, TestsRelative.Replace('/', Path.DirectorySeparatorChar)));
        var methods = TestMethods(source);
        methods.Count.ShouldBeGreaterThan(0);

        var rows = CheckpointRows(Section(plan, "Checkpoints"));
        var pin = rows.Single(row => row.Filter == PinFilter);
        pin.Build.ShouldContain("tests/Antiphon.Tests -> ");
        pin.Covers.ShouldContain("V-1");
        int.TryParse(pin.EstimatedMinutes, out var pinMinutes).ShouldBeTrue(pin.Id);
        pinMinutes.ShouldBeGreaterThan(0);

        var byMethod = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var match = Regex.Match(
                row.Filter,
                @"^/\*/\*/DispatchBaseWarningDeliveryE2ETests/([A-Za-z_][A-Za-z0-9_]*)\*$",
                RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;
            byMethod.ContainsKey(match.Groups[1].Value).ShouldBeFalse(match.Groups[1].Value);
            byMethod.Add(match.Groups[1].Value, row);
        }

        byMethod.Keys.OrderBy(name => name, StringComparer.Ordinal).ShouldBe(
            methods.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        var builds = byMethod.Values.Where(row => row.Build.StartsWith("tests/Antiphon.E2E -> ", StringComparison.Ordinal)).ToArray();
        builds.Length.ShouldBe(1);
        var build = builds[0];
        foreach (var row in byMethod.Values)
        {
            row.Min.ShouldBe("1");
            row.Covers.ShouldContain("V-2");
            int.TryParse(row.EstimatedMinutes, out var minutes).ShouldBeTrue(row.Id);
            minutes.ShouldBeGreaterThan(0);
            if (row.Id == build.Id)
                continue;
            Regex.IsMatch(row.Build, @"^CP-\d+$").ShouldBeTrue(row.Id + " " + row.Build);
            rows.Single(prior => prior.Id == row.Build).Build.ShouldBe(build.Build);
        }
    }

    private static List<string> TestMethods(string source)
    {
        var methods = new List<string>();
        foreach (Match match in Regex.Matches(
                     source,
                     @"\[Test\]\s+public\s+(?:async\s+)?Task\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(",
                     RegexOptions.CultureInvariant))
            methods.Add(match.Groups[1].Value);
        return methods;
    }

    private static string Section(string markdown, string heading)
    {
        var marker = "### " + heading;
        var start = markdown.IndexOf(marker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, marker);
        var next = markdown.IndexOf("\n##", start + marker.Length, StringComparison.Ordinal);
        return next < 0 ? markdown[start..] : markdown[start..next];
    }

    private static List<Row> CheckpointRows(string section)
    {
        List<string>? header = null;
        var rows = new List<Row>();
        foreach (var line in section.Split('\n'))
        {
            if (!line.TrimStart().StartsWith('|'))
                continue;
            var cells = SplitRow(line);
            if (cells.All(cell => cell.Length == 0 || cell.Trim('-', ':', ' ').Length == 0))
                continue;
            if (header is null)
            {
                header = cells;
                header.Count.ShouldBeGreaterThanOrEqualTo(9);
                header[4].ShouldBe("Filter");
                header[5].ShouldBe("Covers");
                header[7].ShouldBe("Min");
                header[8].ShouldBe("EstimatedMinutes");
                continue;
            }

            cells.Count.ShouldBe(header.Count, cells.FirstOrDefault());
            rows.Add(new Row(
                cells[0],
                StripBackticks(cells[2]),
                StripBackticks(cells[4]),
                cells[5],
                cells[7],
                cells[8]));
        }

        header.ShouldNotBeNull();
        return rows;
    }

    private static List<string> SplitRow(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] == '|')
            {
                current.Append('|');
                i++;
                continue;
            }

            if (line[i] == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(line[i]);
        }

        cells.Add(current.ToString().Trim());
        if (cells.Count > 0 && cells[0].Length == 0)
            cells.RemoveAt(0);
        if (cells.Count > 0 && cells[^1].Length == 0)
            cells.RemoveAt(cells.Count - 1);
        return cells;
    }

    private static string StripBackticks(string cell)
    {
        var text = cell.Trim();
        if (text.Length >= 2 && text.StartsWith('`') && text.EndsWith('`'))
            text = text[1..^1].Trim();
        return text;
    }

    private sealed record Row(
        string Id,
        string Build,
        string Filter,
        string Covers,
        string Min,
        string EstimatedMinutes);
}
