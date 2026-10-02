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
    }
}
