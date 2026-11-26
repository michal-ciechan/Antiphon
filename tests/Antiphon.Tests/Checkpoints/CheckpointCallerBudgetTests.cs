using System.Reflection;
using Antiphon.Checkpoints;
using Antiphon.LiveLane;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class CheckpointCallerBudgetTests : CheckpointTestBase
{
    private const string IsolationCaller = "live-lane-isolation-rows";
    private const string EvidenceCaller = "live-lane-evidence-rows";

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

    // The unreferenced-script cleanup retired the startup and delivery checkpoint drivers. W2 slice S22 made the two live-lane
    // callers C# commands (live-lane-isolation-rows and live-lane-evidence-rows, Antiphon.LiveLane/LiveLaneCommission.cs):
    // their rows, totals and control roster are the commands' constants, linted here against the plans they import.
    [Test]
    [Arguments(IsolationCaller, "producerRows", false)]
    [Arguments(IsolationCaller, "producerRows", true)]
    [Arguments(IsolationCaller, "consumerRows", false)]
    [Arguments(IsolationCaller, "consumerRows", true)]
    [Arguments(EvidenceCaller, "CP-20,CP-26", false)]
    [Arguments(EvidenceCaller, "CP-29", false)]
    public void committed_live_lane_callers_pass_total_lint(string caller, string selection, bool controls)
    {
        var plan = caller == EvidenceCaller ? LiveLaneEvidenceRows.Plan : LiveLaneIsolationRows.Plan;
        var manifest = PlanTableImporter.ImportFile(Path.Combine(CheckpointFixtures.RepoRoot, plan)).Manifest.ShouldNotBeNull();
        string[] rows;
        int total;
        string? reason = null;
        if (caller == EvidenceCaller)
        {
            // Both real starts carry their own total.
            LiveLaneEvidenceRows.Groups.Count.ShouldBe(2);
            var group = LiveLaneEvidenceRows.Groups.Single(g => g.Rows == selection);
            total = group.TotalMinutes;
            rows = selection.Split(',');
        }
        else
        {
            total = LiveLaneIsolationRows.TotalMinutes;
            reason = LiveLaneIsolationRows.TotalOverrideReason;
            rows = (selection == "producerRows" ? LiveLaneIsolationRows.ProducerRows : LiveLaneIsolationRows.ConsumerRows).ToArray();
            rows.ShouldNotBeEmpty();
            if (rows.Contains("CP-24-PARITY"))
                AddRow(manifest, "CP-24-PARITY", "CP-24", 1);
            if (controls)
            {
                // The committed control roster and its estimate, including isolation's producer/consumer split at V-12.
                var variants = LiveLaneIsolationRows.Controls.Select(c => c.Variant).ToArray();
                variants.ShouldNotBeEmpty();
                variants = variants.Where(v => selection == "producerRows" ? v <= LiveLaneIsolationRows.LastProducerControl : v > LiveLaneIsolationRows.LastProducerControl).ToArray();
                foreach (var variant in variants)
                {
                    var id = $"PC-V{variant}";
                    AddRow(manifest, id, rows[0], LiveLaneIsolationRows.ControlEstimateMinutes);
                    rows = [.. rows, id];
                }
            }
        }
        Select(manifest, TempDir(), rows, total, reason).TotalTimeoutMinutes.ShouldBe(total, $"{caller}/{selection}/controls={controls}");
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
