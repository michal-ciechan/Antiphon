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
    public void binds_packed_prefixes_from_known_leading_text()
    {
        foreach (var (statement, witness) in new[]
        {
            ("x.ShouldBe(1, \"c1053-first \" + \"c1053-second: detail\");", "c1053-literal-chain"),
            ("x.ShouldBe(1, \"c1053-first \" + \"c1053-second\" + \": detail\");", "c1053-three-literal-chain"),
            ("x.ShouldBe(1, ((\"c1053-first \") + (\"c1053-second: detail\")));", "c1053-nested-parentheses"),
            ("x.ShouldBe(1, @\"c1053-first c1053-second: detail\");", "c1053-verbatim"),
            ("x.ShouldBe(1, \"\"\"c1053-first c1053-second: detail\"\"\");", "c1053-raw"),
            ("x.ShouldBe(1, $\"c1053-first c1053-second: {n} detail\");", "c1053-hole-after-colon"),
            ("x.ShouldBe(1, $\"c1053-first c1053-second: detail\");", "c1053-interpolation-without-hole"),
            ("x.ShouldBe(1, \"c1053-first c1053-second: detail\" + n);", "c1053-literal-before-identifier"),
            ("x.ShouldBe(1, \"c1053-first c1053-second: detail\" + 1);", "c1053-literal-before-numeric"),
            ("x.ShouldBe(1, \"c1053-first c1053-second: detail\" + n.ToString());", "c1053-literal-before-call"),
            ("x.ShouldBe(1, flag ? \"c1053-left c1053-left-b: one\" : \"c1053-right c1053-right-b: two\");", "c1053-both-alternatives"),
        })
        {
            var report = Coverage(statement, "c1053-first", "c1053-second", "c1053-left", "c1053-left-b", "c1053-right", "c1053-right-b");
            if (witness == "c1053-both-alternatives")
            {
                Status(report, "c1053-left").ShouldBe("static-labeled", witness);
                Status(report, "c1053-left-b").ShouldBe("static-labeled", witness);
                Status(report, "c1053-right").ShouldBe("static-labeled", witness);
                Status(report, "c1053-right-b").ShouldBe("static-labeled", witness);
            }
            else
            {
                Status(report, "c1053-first").ShouldBe("static-labeled", witness);
                Status(report, "c1053-second").ShouldBe("static-labeled", witness);
            }
        }
    }

    [Test]
    public void unknown_operands_do_not_extend_a_packed_prefix()
    {
        foreach (var (statement, witness) in new[]
        {
            ("x.ShouldBe(1, \"c1053-first \" + new[] { \"c1053-second: detail\" });", "c1053-array"),
            ("x.ShouldBe(1, \"c1053-first \" + new List<string> { \"c1053-second: detail\" });", "c1053-list"),
            ("x.ShouldBe(1, string.Join(\" \", new[] { \"c1053-first\", \"c1053-second: detail\" }));", "c1053-join"),
            ("x.ShouldBe(1, \"c1053-first \" + 1 + \"c1053-second: detail\");", "c1053-numeric-middle"),
            ("x.ShouldBe(1, \"c1053-first \" + n + \"c1053-second: detail\");", "c1053-identifier-middle"),
            ("x.ShouldBe(1, \"c1053-first \" + n.ToString() + \"c1053-second: detail\");", "c1053-call-middle"),
            ("x.ShouldBe(1, 1 + \"c1053-first c1053-second: detail\");", "c1053-numeric-left"),
            ("x.ShouldBe(1, n + \"c1053-first c1053-second: detail\");", "c1053-identifier-left"),
            ("x.ShouldBe(1, n.ToString() + \"c1053-first c1053-second: detail\");", "c1053-call-left"),
            ("x.ShouldBe(1, \"c1053-first \" + (object)n + \"c1053-second: detail\");", "c1053-cast"),
            ("x.ShouldBe(1, \"c1053-first \" + (missing ?? \"c1053-second\") + \": detail\");", "c1053-null-coalescing"),
            ("x.ShouldBe(1, \"read this first: c1053-second c1053-third\");", "c1053-prose-before-colon"),
            ("x.ShouldBe(1, $\"c1053-first {n} c1053-second: detail\");", "c1053-hole-before-colon"),
            ("x.ShouldBe(1, $\"{n}c1053-first c1053-second: detail\");", "c1053-hole-resets-colon"),
            ("x.ShouldBe(1, flag ? \"c1053-left c1053-left-b: one\" : \"c1053-solo extra-token\");", "c1053-one-unlabelled-alternative"),
        })
        {
            var report = Coverage(statement, "c1053-first", "c1053-second", "c1053-third", "c1053-left", "c1053-left-b", "c1053-solo", "extra-token");
            if (witness is "c1053-join")
            {
                Status(report, "c1053-first").ShouldBe("missing-target", witness);
                Status(report, "c1053-second").ShouldBe("missing-target", witness);
            }
            else if (witness is "c1053-numeric-left" or "c1053-identifier-left" or "c1053-call-left" or "c1053-hole-resets-colon")
            {
                Status(report, "c1053-first").ShouldBe("static-labeled", witness);
                Status(report, "c1053-second").ShouldBe("missing-target", witness);
            }
            else if (witness is "c1053-prose-before-colon")
            {
                Status(report, "c1053-second").ShouldBe("missing-target", witness);
                Status(report, "c1053-third").ShouldBe("missing-target", witness);
            }
            else if (witness is "c1053-one-unlabelled-alternative")
            {
                Status(report, "c1053-left").ShouldBe("static-labeled", witness);
                Status(report, "c1053-left-b").ShouldBe("static-labeled", witness);
                Status(report, "c1053-solo").ShouldBe("static-labeled", witness);
                Status(report, "extra-token").ShouldBe("missing-target", witness);
            }
            else if (witness is "c1053-null-coalescing")
            {
                Status(report, "c1053-first").ShouldBe("static-labeled", witness);
                Status(report, "c1053-second").ShouldBe("missing-target", witness);
            }
            else
            {
                Status(report, "c1053-first").ShouldBe("static-labeled", witness);
                Status(report, "c1053-second").ShouldBe("missing-target", witness);
            }
        }
    }

    [Test]
    public void single_label_messages_follow_the_literal_segment_reading()
    {
        var spaced = Coverage("x.ShouldBe(1, \"target-label before\");", "target-label", "before-token");
        Status(spaced, "target-label").ShouldBe("static-labeled", "c1053-single-label-prefix");
        Status(spaced, "before-token").ShouldBe("missing-target", "c1053-trailing-word-stays-prose");
        var exact = Coverage("x.ShouldBe(1, \"target-label\");", "target-label", "target-label-extra");
        Status(exact, "target-label").ShouldBe("static-labeled", "c1053-single-label-exact");
        Status(exact, "target-label-extra").ShouldBe("missing-target", "c1053-single-label-boundary");
        var aliased = Coverage("const string label = \"target-label\"; x.ShouldBe(1, label);", "target-label");
        Status(aliased, "target-label").ShouldBe("static-labeled", "c1053-literal-alias");
    }

    [Test]
    public void c983_driver_aliases_bind_on_the_exit_assertion()
    {
        var path = Path.Combine(CheckpointFixtures.RepoRoot, "tests", "Antiphon.Tests", "Scripts", "RemoteScriptContractTests.cs");
        var source = File.ReadAllText(path);
        var exitLine = source.Split('\n').Select((line, index) => (line, number: index + 1))
            .Single(row => row.line.Contains("label + \": \" + run.Output", StringComparison.Ordinal)).number;
        var plan = PlanCoverageFixture.Plan(row: "", pc:
            "| PC-6 | mutation | `RemoteScriptContractTests.C983_Required_jq_driver_rejects_successful_false_probe`, `c983-optional-driver` |\n"
            + "| PC-8 | mutation | `RemoteScriptContractTests.C983_Required_jq_driver_rejects_successful_false_probe`, `c983-available-driver` |");
        var report = new PlanCoverageAnalyzer().Analyze("c983.md", plan, [new("RemoteScriptContractTests.cs", source)]);
        foreach (var id in new[] { "PC-6", "PC-8" })
        {
            var pc = report.Pcs.Single(p => p.Id == id);
            pc.Status.ShouldBe("static-labeled", id);
            pc.TargetLine.ShouldBe(exitLine, id);
        }
        report.Diagnostics.ShouldNotContain(d => d.Code == "PC_UNLABELED_PREDECESSOR", "c983-later-matches-stay-labeled");
    }

    private static PlanCoverageReport Coverage(string statement, params string[] labels)
    {
        var rows = string.Join('\n', labels.Select((label, index) =>
            $"| PC-{index + 1} | mutation | `Demo.Check`, `{label}` |"));
        return PlanCoverageFixture.Analyze(
            "class Demo { void Check() { int n = 1; bool flag = true; string missing = null; " + statement + " } }",
            PlanCoverageFixture.Plan(row: "", pc: rows));
    }

    private static string Status(PlanCoverageReport report, string label) =>
        report.Pcs.Single(p => p.Target == label).Status;
}
