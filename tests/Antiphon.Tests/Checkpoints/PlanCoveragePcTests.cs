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
        report.Text().ShouldContain("reachability=unproven", "coverage-pc-footer");
        report.Json().ShouldNotContain("reachable", "coverage-pc-no-dynamic-certificate");
    }
}
