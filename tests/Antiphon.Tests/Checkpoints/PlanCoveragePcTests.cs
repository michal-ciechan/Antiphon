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

    [Test]
    public void rejects_ill_shaped_tokens_before_the_colon()
    {
        var plan = PlanCoverageFixture.Plan(row: "", pc:
            "| PC-1 | mutation | `Demo.Check`, `c1053-first` |\n"
            + "| PC-2 | mutation | `Demo.Check`, `c1053-second` |");
        var prosePlan = PlanCoverageFixture.Plan(row: "", pc: "| PC-1 | mutation | `Demo.Check`, `should` |");
        var upperPlan = PlanCoverageFixture.Plan(row: "", pc: "| PC-1 | mutation | `Demo.Check`, `SHOULD` |");

        PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"Expected should: detail\"); } }", prosePlan)
            .Pcs.Single().Status.ShouldBe("missing-target", "c1053-prose-before-colon");
        PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"should: detail\"); } }", prosePlan)
            .Pcs.Single().Status.ShouldBe("static-labeled", "c1053-single-lowercase");

        var word = PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"c1053-first should: detail\"); } }", plan);
        word.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-lowercase-word");
        word.Pcs.Single(p => p.Id == "PC-2").Status.ShouldBe("missing-target", "c1053-lowercase-word");

        foreach (var (source, label) in new[]
        {
            ("\"c1053-first, c1053-second: detail\"", "c1053-trailing-comma"),
            ("\"(c1053-first) c1053-second: detail\"", "c1053-parentheses"),
            ("\"c1053-first. c1053-second: detail\"", "c1053-dots"),
            ("\"\"", "c1053-empty-message"),
            ("\": detail\"", "c1053-empty-token"),
        })
        {
            var report = PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, " + source + "); } }", plan);
            report.Pcs.ShouldAllBe(p => p.Status == "missing-target", label);
            report.ExitCode.ShouldBe(1, label);
        }

        PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"c1053-first SHOULD: detail\"); } }", upperPlan)
            .Pcs.Single().Status.ShouldBe("missing-target", "c1053-uppercase-only");

        var prefix = PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"c1053-first-extra c1053-second: detail\"); } }", plan);
        prefix.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("missing-target", "c1053-prefix-only");
        prefix.Pcs.Single(p => p.Id == "PC-2").Status.ShouldBe("static-labeled", "c1053-prefix-only");

        var spaced = PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"c1053-first  c1053-second: detail\"); } }", plan);
        spaced.Pcs.ShouldAllBe(p => p.Status == "static-labeled", "c1053-blank-token-is-whitespace");
        spaced.ExitCode.ShouldBe(0, "c1053-blank-token-is-whitespace");
    }

    [Test]
    public void keeps_the_colon_boundary_across_interpolation_and_concatenation()
    {
        var plan = PlanCoverageFixture.Plan(row: "", pc:
            "| PC-1 | mutation | `Demo.Check`, `c1053-first` |\n"
            + "| PC-2 | mutation | `Demo.Check`, `c1053-second` |\n"
            + "| PC-3 | mutation | `Demo.Check`, `c1053-third` |");
        PlanCoverageReport Analyze(string message) => PlanCoverageFixture.Analyze(
            "class Demo { void Check() { int n = 1; x.ShouldBe(1, " + message + "); } }", plan);

        var afterHole = Analyze("$\"c1053-first: detail {n} c1053-second: prose\"");
        afterHole.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-hole-after-colon");
        afterHole.Pcs.Where(p => p.Id != "PC-1").ShouldAllBe(p => p.Status == "missing-target", "c1053-hole-after-colon");

        var formatColon = Analyze("$\"c1053-first {n:0} c1053-second\"");
        formatColon.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-colon-in-hole");
        formatColon.Pcs.Where(p => p.Id != "PC-1").ShouldAllBe(p => p.Status == "missing-target", "c1053-colon-in-hole");

        var ternary = Analyze("$\"c1053-first {(n > 0 ? 1 : 0)} c1053-second: detail\"");
        ternary.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-colon-in-expression");
        ternary.Pcs.Where(p => p.Id != "PC-1").ShouldAllBe(p => p.Status == "missing-target", "c1053-colon-in-expression");

        var verbatim = Analyze("$@\"c1053-first: detail {n} c1053-second: prose\"");
        verbatim.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-verbatim");
        verbatim.Pcs.Where(p => p.Id != "PC-1").ShouldAllBe(p => p.Status == "missing-target", "c1053-verbatim");

        var raw = Analyze("$\"\"\"c1053-first: detail {n} c1053-second: prose\"\"\"");
        raw.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-raw");
        raw.Pcs.Where(p => p.Id != "PC-1").ShouldAllBe(p => p.Status == "missing-target", "c1053-raw");

        var concat = Analyze("\"c1053-first \" + $\"c1053-second: detail {n} c1053-third: prose\"");
        concat.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-concatenation");
        concat.Pcs.Single(p => p.Id == "PC-2").Status.ShouldBe("static-labeled", "c1053-concatenation");
        concat.Pcs.Single(p => p.Id == "PC-3").Status.ShouldBe("missing-target", "c1053-concatenation");

        var packed = Analyze("$\"c1053-first c1053-second: detail {n}\"");
        packed.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-hole-after-packed-colon");
        packed.Pcs.Single(p => p.Id == "PC-2").Status.ShouldBe("static-labeled", "c1053-hole-after-packed-colon");
        packed.Pcs.Single(p => p.Id == "PC-3").Status.ShouldBe("missing-target", "c1053-hole-after-packed-colon");
        packed.ExitCode.ShouldBe(1, "c1053-hole-after-packed-colon");
    }

    [Test]
    public void unknown_concatenation_operand_ends_the_label_prefix()
    {
        var plan = PlanCoverageFixture.Plan(row: "", pc:
            "| PC-1 | mutation | `Demo.Check`, `c1053-first` |\n"
            + "| PC-2 | mutation | `Demo.Check`, `c1053-second` |");
        PlanCoverageReport Analyze(string message) => PlanCoverageFixture.Analyze(
            "class Demo { void Check() { int n = 1; x.ShouldBe(1, " + message + "); } }", plan);

        foreach (var (source, label) in new[]
        {
            ("\"c1053-first \" + 1 + \"c1053-second: detail\"", "c1053-numeric-literal"),
            ("\"c1053-first \" + n + \"c1053-second: detail\"", "c1053-identifier"),
            ("\"c1053-first \" + \"\".Length + \"c1053-second: detail\"", "c1053-member-access"),
            ("\"c1053-first \" + n.ToString() + \"c1053-second: detail\"", "c1053-invocation"),
            ("\"c1053-first \" + (object)n + \"c1053-second: detail\"", "c1053-cast"),
            ("\"c1053-first \" + (n > 0 ? \"c1053-second\" : \"c1053-second\") + \": detail\"", "c1053-conditional"),
            ("\"c1053-first \" + (n) + \"c1053-second: detail\"", "c1053-parentheses"),
            ("\"c1053-first \" + (missing ?? \"kept\") + \"c1053-second: detail\"", "c1053-null-coalescing"),
            ("\"c1053-first \" + (\"c1053-mid \" + n) + \"c1053-second: detail\"", "c1053-nested-concatenation"),
        })
        {
            var report = Analyze(source);
            report.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", label);
            report.Pcs.Single(p => p.Id == "PC-2").Status.ShouldBe("missing-target", label);
            report.ExitCode.ShouldBe(1, label);
        }
    }

    [Test]
    public void binds_packed_prefix_before_a_concatenation_hole()
    {
        var plan = PlanCoverageFixture.Plan(row: "", pc:
            "| PC-1 | mutation | `Demo.Check`, `c1053-first` |\n"
            + "| PC-2 | mutation | `Demo.Check`, `c1053-second` |\n"
            + "| PC-3 | mutation | `Demo.Check`, `c1053-third` |");
        PlanCoverageReport Analyze(string message) => PlanCoverageFixture.Analyze(
            "class Demo { void Check() { int n = 1; string packed = \"c1053-first c1053-second: detail\"; x.ShouldBe(1, " + message + "); } }", plan);

        foreach (var (source, label) in new[]
        {
            ("\"c1053-first c1053-second: detail\" + n", "c1053-prefix-before-numeric"),
            ("\"c1053-first \" + \"c1053-second: detail\" + n", "c1053-literal-concatenation-before-hole"),
            ("@\"c1053-first c1053-second: detail\" + n", "c1053-verbatim-before-hole"),
            ("\"\"\"c1053-first c1053-second: detail\"\"\" + n", "c1053-raw-before-hole"),
            ("(\"c1053-first \" + \"c1053-second: detail\") + n", "c1053-parenthesised-literals-before-hole"),
            ("packed + n", "c1053-literal-alias-before-hole"),
            ("\"c1053-first \" + (\"c1053-second: detail \" + n)", "c1053-nested-prefix-before-hole"),
        })
        {
            var report = Analyze(source);
            report.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", label);
            report.Pcs.Single(p => p.Id == "PC-2").Status.ShouldBe("static-labeled", label);
            report.Pcs.Single(p => p.Id == "PC-3").Status.ShouldBe("missing-target", label);
        }

        var after = Analyze("\"c1053-first c1053-second: detail\" + n + \" c1053-third\"");
        after.Pcs.Single(p => p.Id == "PC-1").Status.ShouldBe("static-labeled", "c1053-third-after-hole");
        after.Pcs.Single(p => p.Id == "PC-2").Status.ShouldBe("static-labeled", "c1053-third-after-hole");
        after.Pcs.Single(p => p.Id == "PC-3").Status.ShouldBe("missing-target", "c1053-third-after-hole");
        after.ExitCode.ShouldBe(1, "c1053-third-after-hole");
    }
}
