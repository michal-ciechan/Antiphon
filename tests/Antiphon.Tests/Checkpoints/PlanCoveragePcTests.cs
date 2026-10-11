using Antiphon.Checkpoints.Coverage;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Checkpoints;
[Category("Unit")]
public sealed class PlanCoveragePcTests
{
    private static string PcPlan() => PlanCoverageFixture.Plan(pc: "| PC-1 | mutation | `Demo.Check`, `target-label` |");
    [Test]
    public void reports_unlabelled_predecessor_including_throw_async()
    {
        var report = PlanCoverageFixture.Analyze("class Demo { async Task Check() {\n await Should.ThrowAsync<Exception>(() => Act());\n x.ShouldBe(1, \"target-label\"); } }", PcPlan());
        report.Diagnostics.ShouldContain(d => d.Code == "PC_UNLABELED_PREDECESSOR" && d.TestLine == 2, "coverage-pc-predecessor");
        report.Pcs.Single().Status.ShouldBe("unlabeled-predecessor", "coverage-pc-status");
    }
    [Test]
    public void rejects_label_in_other_method()
    {
        var report = PlanCoverageFixture.Analyze("class Demo { void Check() {} void Other() { x.ShouldBe(1, \"target-label\"); } }", PcPlan());
        report.Diagnostics.ShouldContain(d => d.Code == "PC_LABEL_NOT_IN_METHOD", "coverage-pc-method");
        report.Pcs.Single().Status.ShouldBe("missing-target", "coverage-pc-missing");
    }
    [Test]
    public void reports_earlier_different_label_as_unproven()
    {
        var report = PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"other-label\"); x.ShouldBe(2, \"target-label\"); } }", PcPlan());
        report.Pcs.Single().EarlierOtherLabels.ShouldBe(["other-label"], "coverage-pc-other-label");
        report.Pcs.Single().Reachability.ShouldBe("unproven", "coverage-pc-advisory-unproven");
        report.ExitCode.ShouldBe(0, "coverage-pc-advisory-exit");
    }
    [Test]
    public void accepts_labeled_sequence_without_claiming_dynamic_proof()
    {
        var report = PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"target-label before\"); x.ShouldBe(2, \"target-label after\"); } }", PcPlan());
        report.Pcs.Single().Status.ShouldBe("static-labeled", "coverage-pc-unproven");
        report.Text().ShouldContain("reachability=unproven", Case.Sensitive, "coverage-pc-footer");
        report.Json().ShouldNotContain("reachable", Case.Sensitive, "coverage-pc-no-dynamic-certificate");
    }

    [Test]
    public void binds_every_pre_colon_label_in_one_message()
    {
        var plan = PlanCoverageFixture.Plan(row: "", pc:
            "| PC-1 | mutation | `Demo.Check`, `c1053-first` |\n"
            + "| PC-2 | mutation | `Demo.Check`, `c1053-second` |\n"
            + "| PC-3 | mutation | `Demo.Check`, `c1053-third` |");
        var packed = PlanCoverageFixture.Analyze(
            "class Demo { void Check() { Prove(\"c1053-first c1053-second c1053-third\"); } static void Prove(string label) { x.ShouldBe(1, label + \": detail\"); } }",
            plan);
        packed.ExitCode.ShouldBe(0, "c1053-multi-label");
        foreach (var id in new[] { "PC-1", "PC-2", "PC-3" })
        {
            var pc = packed.Pcs.Single(p => p.Id == id);
            pc.Status.ShouldBe("static-labeled", "c1053-multi-label");
            pc.Reachability.ShouldBe("unproven", "c1053-multi-label");
        }

        var missing = PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"c1053-first c1053-second: detail\"); } }", plan);
        missing.Pcs.Single(p => p.Id == "PC-3").Status.ShouldBe("missing-target", "c1053-missing-token");
        missing.ExitCode.ShouldBe(1, "c1053-missing-token");

        var prose = PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"c1053-first: c1053-second c1053-third\"); } }", plan);
        prose.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-prose");
        prose.Pcs.Where(p => p.Id != "PC-1").ShouldAllBe(p => p.Status == "missing-target", "c1053-prose");
        prose.ExitCode.ShouldBe(1, "c1053-prose");

        var unlabeled = PlanCoverageFixture.Analyze(
            "class Demo { void Check() { x.ShouldBe(1); x.ShouldBe(2, \"c1053-first c1053-second c1053-third: detail\"); } }", plan);
        unlabeled.Pcs.ShouldAllBe(p => p.Status == "unlabeled-predecessor", "c1053-unlabeled");
        unlabeled.ExitCode.ShouldBe(1, "c1053-unlabeled");

        var leading = PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"c1053-first before\"); } }", plan);
        leading.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-leading-only");
        leading.Pcs.Where(p => p.Id != "PC-1").ShouldAllBe(p => p.Status == "missing-target", "c1053-leading-only");
        leading.ExitCode.ShouldBe(1, "c1053-leading-only");

        var boundary = PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"c1053-first-extra: detail\"); } }", plan);
        boundary.Pcs.ShouldAllBe(p => p.Status == "missing-target", "c1053-label-boundary");
        boundary.ExitCode.ShouldBe(1, "c1053-label-boundary");
    }
}
