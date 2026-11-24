using System.Reflection;
using System.Text.RegularExpressions;
using Antiphon.Checkpoints;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointCallerBudgetTests : CheckpointTestBase
{
    private const string IsolationCaller = "scripts/test-live-lane-isolation.ps1";
    private const string EvidenceCaller = "scripts/test-live-lane-evidence.ps1";

    [Test]
    [Arguments("plan")]
    [Arguments("yaml")]
    [Arguments("cli")]
    public void explicit_totals_use_estimates_without_the_default_floor(string entry)
    {
        const string table = """
            ### Checkpoints

            | CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
            |---|---|---|---|---|---|---|---|---|
            | CP-1 | S1 | tests/Antiphon.Tests -> bin-floor/ | floor | /*/*/BudgetTests/* | V-1 | all listed | 1 | 1 |
            """;
        var root = TempDir();
        RowTimeout.DeriveTotalMinutes([1], null).ShouldBe(30);
        if (entry == "plan")
        {
            var imported = PlanTableImporter.ImportMarkdown(table.Replace("### Checkpoints", "### Checkpoints\n\ntotalTimeoutMinutes: 12"));
            imported.ExitCode.ShouldBe(0, imported.Error);
            imported.Manifest.ShouldNotBeNull().Timeouts.ExplicitTotalMinutes.ShouldBe(12);
            imported.Warnings.ShouldBeEmpty();
            return;
        }

        var manifest = PlanTableImporter.ImportMarkdown(table).Manifest.ShouldNotBeNull();
        if (entry == "yaml")
        {
            manifest.Timeouts.ExplicitTotalMinutes = 12;
            ManifestLoader.LoadYaml(ManifestLoader.ToYaml(manifest), root).Timeouts.ExplicitTotalMinutes.ShouldBe(12);
            return;
        }
        Select(manifest, root, ["CP-1"], 12).TotalTimeoutMinutes.ShouldBe(12);
    }

    // The unreferenced-script cleanup retired the startup and delivery checkpoint drivers.
    [Test]
    [Arguments(IsolationCaller, "producerRows", false)]
    [Arguments(IsolationCaller, "producerRows", true)]
    [Arguments(IsolationCaller, "consumerRows", false)]
    [Arguments(IsolationCaller, "consumerRows", true)]
    [Arguments(EvidenceCaller, "CP-20,CP-26", false)]
    [Arguments(EvidenceCaller, "CP-29", false)]
    public void committed_live_lane_callers_pass_total_lint(string caller, string selection, bool controls)
    {
        var source = File.ReadAllText(Path.Combine(CheckpointFixtures.RepoRoot, caller));
        var plan = Match(source, @"import --plan \(Join-Path \$repo '(?<value>[^']+)'\)");
        var manifest = PlanTableImporter.ImportFile(Path.Combine(CheckpointFixtures.RepoRoot, plan)).Manifest.ShouldNotBeNull();
        string[] rows;
        int total;
        if (caller == EvidenceCaller)
        {
            // Both real calls to the parameterized start wrapper carry their own total.
            Regex.Matches(source, @"Invoke-EvidenceCheckpoint -Rows '[^']+' -TotalMinutes \d+").Count.ShouldBe(2);
            source.ShouldContain("'--total-timeout', ($TotalMinutes.ToString() + 'm')");
            total = int.Parse(Match(source, $@"Invoke-EvidenceCheckpoint -Rows '{selection}' -TotalMinutes (?<value>\d+)"));
            rows = selection.Split(',');
        }
        else
        {
            Regex.Matches(source, @"--total-timeout \d+m").Count.ShouldBe(1);
            total = int.Parse(Match(source, @"--total-timeout (?<value>\d+)m"));
            var assigned = Match(source, $@"\${selection} = @\((?<value>[^\r\n]+)\)");
            rows = Regex.Matches(assigned, @"'([^']+)'").Select(match => match.Groups[1].Value).ToArray();
            rows.ShouldNotBeEmpty();
            if (rows.Contains("CP-24-PARITY"))
                AddRow(manifest, "CP-24-PARITY", "CP-24", 1);
            if (controls)
            {
                // Read the committed control roster and its actual estimate, including
                // isolation's producer/consumer split at V-12.
                var estimate = int.Parse(Match(source, @"- id: PC-V\$v[\s\S]*?estimatedMinutes: (?<value>\d+)"));
                var variants = Regex.Matches(source, @"@\((\d+),'[^']+'\)").Select(match => int.Parse(match.Groups[1].Value)).ToArray();
                variants.ShouldNotBeEmpty();
                source.ShouldContain("$isPty = $v -le 12");
                variants = variants.Where(v => selection == "producerRows" ? v <= 12 : v > 12).ToArray();
                foreach (var variant in variants)
                {
                    var id = $"PC-V{variant}";
                    AddRow(manifest, id, rows[0], estimate);
                    rows = [.. rows, id];
                }
            }
        }
        var reason = Regex.Match(source, @"--total-timeout-override-reason '(?<value>[^']+)'");
        Select(manifest, TempDir(), rows, total, reason.Success ? reason.Groups["value"].Value : null)
            .TotalTimeoutMinutes.ShouldBe(total, $"{caller}/{selection}/controls={controls}");
    }

    private static string Match(string source, string pattern)
    {
        var match = Regex.Match(source, pattern);
        match.Success.ShouldBeTrue($"Unrecognized committed caller syntax: {pattern}");
        return match.Groups["value"].Value;
    }

    private static void AddRow(CheckpointManifest manifest, string id, string parent, int estimate)
    {
        var row = manifest.Checkpoints.Single(row => row.Id == parent);
        manifest.Checkpoints.Add(new CheckpointSpec
        {
            Id = id, After = row.After, Build = row.Build, Filter = row.Filter,
            MinExecuted = 1, EstimatedMinutes = estimate
        });
    }

    private static RunRequest Select(CheckpointManifest manifest, string root, string[] rows, int total, string? reason = null)
    {
        var path = Path.Combine(root, "callers.yaml");
        File.WriteAllText(path, ManifestLoader.ToYaml(manifest));
        var options = new Antiphon.Checkpoints.Program.ArgSet();
        options.Positionals.Add(path);
        options.Add("rows", string.Join(',', rows));
        options.Add("total-timeout", $"{total}m");
        if (reason is not null)
            options.Add("total-timeout-override-reason", reason);
        var method = typeof(Antiphon.Checkpoints.Program).GetMethod("LoadSelection", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = ((CheckpointManifest Manifest, RunRequest Request))method.Invoke(null, [options, root, null, CancellationToken.None])!;
        return result.Request;
    }
}
