using System.Security.Cryptography;
using System.Text;
using Antiphon.Checkpoints.Coverage;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Checkpoints;
[Category("Unit")]
public sealed class PlanCoverageParserTests : CheckpointTestBase
{
    [Test]
    public void extracts_verification_obligations_with_original_lines()
    {
        var plan = PlanCoverageFixture.Plan("| V-1 | `Demo.Check()` | label `first-label`; check canary `secret` absent |");
        var report = new PlanCoverageReader().Read("plan.md", plan);
        report.Obligations.Select(o => $"{o.Kind}:{o.Name}:{o.PlanLine}:{o.PlanColumn}").ShouldBe(["method:Demo.Check:5:10", "label:first-label:5:33", "canary:secret:5:61"], "coverage-parser-items");
        new PlanCoverageReader().Read("plan.md", plan.Replace("\n", "\r\n")).Obligations.Select(o => (o.Kind, o.Name, o.PlanLine, o.PlanColumn)).ShouldBe(report.Obligations.Select(o => (o.Kind, o.Name, o.PlanLine, o.PlanColumn)), "coverage-parser-crlf");
        new PlanCoverageReader().Read("plan.md", PlanCoverageFixture.Plan("| V-1 | `Demo.Check` | label `broken |" )).Invalid.ShouldBeTrue("coverage-parser-unclosed");
    }
    [Test]
    public void classifies_tokens_without_promoting_example_values()
    {
        var plan = PlanCoverageFixture.Plan("| V-1 | `Demo.Check` | label `plain` |") .Replace("### Checkpoints", "| Input | Expected display |\n| `secret-home` | `[redacted]` |\n```text\nV-2 `Not.AMethod`\n```\n### Checkpoints");
        var report = new PlanCoverageReader().Read("p", plan);
        report.Obligations.Select(o => o.Name).ShouldBe(["Demo.Check", "plain"], "coverage-parser-roles");
        report.Exclusions.ShouldContain(d => d.Name == "secret-home", "coverage-parser-exclusions");
    }
    [Test]
    public void resolves_closed_test_scope_and_rejects_ambiguity()
    {
        var root = TempDir(); var world = PlanCoverageFixture.WriteWorld(root);
        var output = new StringWriter(); var command = new CoverageCommand();
        command.Run(root, world.Plan, output: output).ShouldBe(0, "coverage-scope-closed");
        output.ToString().ShouldContain("files=1", Case.Sensitive, "coverage-scope-files");
        command.Run(root, world.Plan, [world.Source, world.Source], output: new StringWriter()).ShouldBe(2, "coverage-scope-duplicate");
        command.Run(root, "../outside.md", output: new StringWriter()).ShouldBe(2, "coverage-scope-escape");
        File.WriteAllText(world.Plan, PlanCoverageFixture.Plan().Replace("Demo*", "Missing*"));
        command.Run(root, world.Plan, [world.Source], output: new StringWriter()).ShouldBe(2, "coverage-scope-missing-selected");
        File.WriteAllText(world.Plan, PlanCoverageFixture.Plan().Replace("`Demo.Check`", "`Check`"));
        var unique = new StringWriter();
        command.Run(root, world.Plan, output: unique).ShouldBe(0, "coverage-unqualified-unique");
        File.WriteAllText(world.Source, "partial class Demo { void Check() { x.ShouldBe(1, \"target-label\"); } } partial class Other { void Check() {} }");
        var ambiguous = new StringWriter();
        command.Run(root, world.Plan, [world.Source], output: ambiguous).ShouldBe(1, "coverage-method-ambiguous");
        ambiguous.ToString().ShouldContain("METHOD_UNMAPPED", Case.Sensitive, "coverage-method-ambiguity-visible");
    }
    [Test]
    public void maps_checklist_without_erasing_legacy_requirements()
    {
        var plan = PlanCoverageFixture.Plan("| V-1 | `Demo.Check` | label `old-label`; Verify incarnation facts. |");
        var checklist = PlanCoverageFixture.Checklist(PlanCoverageFixture.Item("member", "InstanceId", maps: "incarnation facts"));
        var report = new PlanCoverageReader().Read("p", plan, checklist);
        report.Obligations.Select(o => o.Name).ShouldBe(["Demo.Check", "old-label", "InstanceId"], "coverage-checklist-additive");
        report.Diagnostics.ShouldNotContain(d => d.Name == "incarnation facts", "coverage-checklist-mapped");
        new PlanCoverageReader().Read("p", plan, checklist.Replace("\"version\":1", "\"version\":2")).Invalid.ShouldBeTrue("coverage-checklist-version");
        new PlanCoverageReader().Read("p", plan, checklist.Replace("incarnation facts", "stale clause")).Invalid.ShouldBeTrue("coverage-checklist-stale");

        const string json = "{\n  \"version\": 1,\n  \"items\": []\n}\n";
        const string lfHash = "da6fa0fb6dc64a3faf1f81f830b8696ccf02b9c654135111429090fa240870f8";
        const string crlfHash = "79f2f76348e92b2903f7a7e371d28a1e951cf465a1ec68a1f1ee2221a871a200";
        string RawHash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        foreach (var (payload, expectedHash, label) in new[]
        {
            (json, lfHash, "c1013-inline-lf"),
            (json + "\n", "3ba91a5d353e5eaab71a23740fd938f42e9cd1996fe809aded37f4b2951bcc33", "c1013-inline-blank-line"),
            (json.Replace("  \"version\"", "    \"version\""), "85c491e24a155122596d00155b48f01c095a8b8968b71985736a69155256feb1", "c1013-inline-whitespace"),
        })
        {
            var lfPlan = PlanCoverageFixture.Plan() + "\n```plan-coverage-v1\n" + payload + "```\n";
            var lf = new PlanCoverageReader().Read("plan.md", lfPlan);
            var crlfPlan = lfPlan.Replace("\n", "\r\n");
            var crlf = new PlanCoverageReader().Read("plan.md", crlfPlan);
            foreach (var parsed in new[] { lf, crlf })
            {
                parsed.Invalid.ShouldBeFalse("c1013-inline-valid");
                parsed.ChecklistSha256.ShouldBe(expectedHash, label);
                parsed.Obligations.Select(o => (o.Id, o.Test, o.Kind, o.Name, o.PlanLine, o.PlanColumn))
                    .ShouldBe([("V-1", "Demo.Check", "method", "Demo.Check", 5, 10),
                        ("V-1", "Demo.Check", "label", "target-label", 5, 33)], "c1013-inline-coordinates");
            }
            lf.PlanSha256.ShouldBe(RawHash(lfPlan), "c1013-raw-plan");
            crlf.PlanSha256.ShouldBe(RawHash(crlfPlan), "c1013-raw-plan");
            crlf.PlanSha256.ShouldNotBe(lf.PlanSha256, "c1013-raw-plan");
        }
        foreach (var rawPlan in new[] { PlanCoverageFixture.Plan(), PlanCoverageFixture.Plan().Replace("\n", "\r\n") })
        {
            var externalLf = new PlanCoverageReader().Read("plan.md", rawPlan, json);
            var externalCrlf = new PlanCoverageReader().Read("plan.md", rawPlan, json.Replace("\n", "\r\n"));
            externalLf.Invalid.ShouldBeFalse("c1013-external-valid");
            externalCrlf.Invalid.ShouldBeFalse("c1013-external-valid");
            externalLf.ChecklistSha256.ShouldBe(lfHash, "c1013-external-lf");
            externalCrlf.ChecklistSha256.ShouldBe(crlfHash, "c1013-external-crlf");
        }
    }

    [Test]
    public void declared_total_still_rejects_zero_bound_methods()
    {
        var plan = PlanCoverageFixture.Plan("| R-1 | Regression | All 1 class-qualified methods in the checklist are required. |");
        var report = new PlanCoverageAnalyzer().Analyze("p", plan, [new("s.cs", "class DemoTests { void Check() {} }")]);
        report.Diagnostics.ShouldContain(d => d.Code == "CHECKLIST_COUNT_MISMATCH" && d.Id == "R-1" && d.Test == "" && d.Detail == "methods expected=1 actual=0", "coverage-total-zero-still-checked");
        report.ExitCode.ShouldBe(1, "coverage-total-zero-still-fails");
    }

    [Test]
    public void declared_row_counts_reject_missing_and_extra_bound_methods()
    {
        var plan = PlanCoverageFixture.Plan("| V-1 | `DemoTests` (2 results) | `DemoTests.First`; label `target-label` |");
        var source = "class DemoTests { [Test] void First() { x.ShouldBe(1, \"target-label\"); } [Test] void Second() {} [Test] void Third() {} }";
        var analyzer = new PlanCoverageAnalyzer();
        var second = PlanCoverageFixture.Item("method", "DemoTests.Second", test: "DemoTests.Second");
        var third = PlanCoverageFixture.Item("method", "DemoTests.Third", test: "DemoTests.Third");
        var missing = analyzer.Analyze("p", plan, [new("s.cs", source)]);
        missing.Diagnostics.ShouldContain(d => d.Code == "CHECKLIST_COUNT_MISMATCH" && d.Test == "DemoTests" && d.PlanLine == 5 && d.Detail == "results expected=2 actual=1", "coverage-row-count-missing");
        missing.ExitCode.ShouldBe(1, "coverage-row-count-missing");
        var extra = analyzer.Analyze("p", plan, [new("s.cs", source)], PlanCoverageFixture.Checklist(second, third));
        extra.Diagnostics.ShouldContain(d => d.Code == "CHECKLIST_COUNT_MISMATCH" && d.Detail == "results expected=2 actual=3", "coverage-row-count-extra");
        extra.ExitCode.ShouldBe(1, "coverage-row-count-extra");
        analyzer.Analyze("p", plan, [new("s.cs", source)], PlanCoverageFixture.Checklist(second)).ExitCode.ShouldBe(0, "coverage-row-count-exact");
    }

    [Test]
    public void declared_counts_keep_class_and_requirement_bindings()
    {
        var plan = PlanCoverageFixture.Plan("| V-1 | `DemoTests` (2 results) | `DemoTests.First`; label `target-label` |\n| V-2 | `OtherTests` (1 results) | `OtherTests.First` |");
        var source = "namespace Example; class DemoTests { void First() { x.ShouldBe(1, \"target-label\"); } void Second() {} } class OtherTests { void First() {} }";
        var checklist = PlanCoverageFixture.Checklist(
            PlanCoverageFixture.Item("method", "DemoTests.First", test: "Example.DemoTests.First"),
            PlanCoverageFixture.Item("method", "DemoTests.Second", id: "V-2", test: "DemoTests.Second", line: 6),
            PlanCoverageFixture.Item("method", "OtherTests.First", test: "OtherTests.First"));
        var report = new PlanCoverageAnalyzer().Analyze("p", plan, [new("s.cs", source)], checklist);
        report.Diagnostics.Where(d => d.Code == "CHECKLIST_COUNT_MISMATCH").Select(d => (d.Id, d.Test, d.Detail))
            .ShouldBe([("V-1", "DemoTests", "results expected=2 actual=1")], "coverage-count-binding-scoped");
    }

    [Test]
    public void declared_results_expand_arguments_without_inventing_dynamic_counts()
    {
        var plan = PlanCoverageFixture.Plan("| V-1 | `DemoTests` (2 results) | `DemoTests.Check`; label `target-label` |");
        var source = "class DemoTests { [Test, Arguments(1), Arguments(2)] void Check(int x) { x.ShouldBe(1, \"target-label\"); } }";
        var analyzer = new PlanCoverageAnalyzer();
        analyzer.Analyze("p", plan, [new("s.cs", source)]).ExitCode.ShouldBe(0, "coverage-count-arguments");
        var dynamic = source.Replace("Arguments(1), Arguments(2)", "MethodDataSource(nameof(Cases))", StringComparison.Ordinal);
        var report = analyzer.Analyze("p", plan, [new("s.cs", dynamic)]);
        report.Diagnostics.ShouldContain(d => d.Code == "CHECKLIST_COUNT_UNMAPPED", "coverage-count-dynamic-unmapped");
        report.ExitCode.ShouldBe(1, "coverage-count-dynamic-unmapped");
    }
}
