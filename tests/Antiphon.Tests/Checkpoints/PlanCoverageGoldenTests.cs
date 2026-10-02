using System.Text.Json;
using Antiphon.Checkpoints.Coverage;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Checkpoints;
[Category("Unit")]
public sealed class PlanCoverageGoldenTests
{
    [Test]
    public void c780_class_only_regression_row_keeps_master_findings()
    {
        var path = Path.Combine(CheckpointFixtures.RepoRoot, "docs", "superpowers", "plans", "2026-09-30-card-0780-retire-temp-date-parse-plan.md");
        var row = File.ReadAllLines(path).Single(l => l.StartsWith("| R-1 |", StringComparison.Ordinal));
        row.ShouldContain("`ClaudeTokenRefreshOptInTests` (6 results)", Case.Sensitive, "coverage-c780-count-present");
        var report = new PlanCoverageAnalyzer().Analyze("c780-r1.md", PlanCoverageFixture.Plan(row),
            [new("source.cs", "class ClaudeTokenRefreshOptInTests { [Test] void Regression() {} }")]);
        report.Obligations.ShouldNotContain(o => o.Kind == "method", "coverage-c780-no-bound-methods");
        report.Diagnostics.ShouldBe([new CoverageDiagnostic("PROSE_UNMAPPED", 5, 54, "R-1", Name: "c590-real.ps1",
            Detail: "explicit member mapping required")], "coverage-c780-master-findings");
        report.ExitCode.ShouldBe(1, "coverage-c780-existing-finding-preserved");
    }

    [Test]
    public void c866_legacy_reports_v1_v4_v5()
    {
        var report = PlanCoverageFixture.C866(false, false);
        report.Diagnostics.ShouldContain(d => d.Code == "PROSE_UNMAPPED" && d.Name == "incarnation facts" && d.PlanLine == 206, "coverage-legacy-v-gaps");
        report.Diagnostics.ShouldContain(d => d.Name == "c866-user" && d.Id == "V-1" && d.PlanLine == 206, "coverage-legacy-canary");
        report.Diagnostics.ShouldContain(d => d.Name == "Foreground" && d.Code == "MISSING_EMPTY" && d.PlanLine == 210, "coverage-legacy-empty");
        using var provenance = JsonDocument.Parse(PlanCoverageFixture.Raw("provenance.json"));
        foreach (var blob in provenance.RootElement.GetProperty("blobs").EnumerateArray())
            PlanCoverageReport.Hash(PlanCoverageFixture.Raw(blob.GetProperty("fixture").GetString()!)).ShouldBe(blob.GetProperty("sha256").GetString(), "coverage-raw-provenance");
    }
    [Test]
    public void c866_fixed_checks_clear_same_obligations()
    {
        var original = PlanCoverageFixture.C866(false, true); var corrected = PlanCoverageFixture.C866(true, true);
        corrected.Diagnostics.ShouldBeEmpty("coverage-fixed-same-obligations");
        corrected.ExitCode.ShouldBe(0, "coverage-fixed-green");
        original.Obligations.Select(o => (o.Id, o.Test, o.Kind, o.Name)).ShouldBe(corrected.Obligations.Select(o => (o.Id, o.Test, o.Kind, o.Name)), "coverage-frozen-obligations");
        using var expected = JsonDocument.Parse(PlanCoverageFixture.Raw("c866-expected.json"));
        foreach (var row in expected.RootElement.EnumerateArray())
            original.Diagnostics.ShouldContain(d => d.Id == row[0].GetString() && d.Code == "MISSING_" + row[1].GetString()!.ToUpperInvariant() && d.Name == row[2].GetString() && d.PlanLine == row[3].GetInt32(), "coverage-historical-required-gap");
    }
    [Test]
    public void c835_eight_pc_risks_are_visible()
    {
        var report = new PlanCoverageAnalyzer().Analyze("c835-plan.md.txt", PlanCoverageFixture.Project("c835-plan.md.txt", "c835Projection"),
            [new("state.cs.txt", PlanCoverageFixture.Raw("c835-state.cs.txt")), new("script.cs.txt", PlanCoverageFixture.Raw("c835-script.cs.txt")), new("approval.cs.txt", PlanCoverageFixture.Raw("c835-approval.cs.txt"))]);
        using var expected = JsonDocument.Parse(PlanCoverageFixture.Raw("c835-expected.json"));
        report.Pcs.Select(p => p.Id).Order().ShouldBe(expected.RootElement.EnumerateObject().Select(p => p.Name).Order(), "coverage-eight-pc-risks");
        foreach (var p in expected.RootElement.EnumerateObject()) report.Pcs.Single(pc => pc.Id == p.Name).Status.ShouldBe(p.Value.GetString(), "coverage-eight-pc-status " + p.Name);
        report.Pcs.Single(p => p.Id == "PC-23").EarlierOtherLabels.ShouldNotBeEmpty("coverage-pc23-advisory");
        report.Pcs.ShouldAllBe(p => p.Reachability == "unproven", "coverage-historical-unproven");
    }
}
