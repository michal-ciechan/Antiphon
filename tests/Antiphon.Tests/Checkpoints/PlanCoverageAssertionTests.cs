using Antiphon.Checkpoints.Coverage;
using Shouldly;
using TUnit.Core;
namespace Antiphon.Tests.Checkpoints;
[Category("Unit")]
public sealed class PlanCoverageAssertionTests
{
    [Test]
    public void requires_label_at_bound_assertion()
    {
        var wrong = PlanCoverageFixture.Analyze("class Demo { void Check() { var x = \"target-label\"; x.ShouldBe(\"target-label\"); } void Other() { x.ShouldBe(1, \"target-label\"); } }");
        wrong.Diagnostics.ShouldContain(d => d.Code == "MISSING_LABEL", "coverage-label-bound");
        PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, $\"target-label case {n}\"); } }").ExitCode.ShouldBe(0, "coverage-label-interpolation");
        PlanCoverageFixture.Analyze("class Demo { void Check() { x.ShouldBe(1, \"target-label-extra\"); } }").ExitCode.ShouldBe(1, "coverage-label-boundary");
    }
    [Test]
    public void requires_canary_in_assertion_not_setup()
    {
        var plan = PlanCoverageFixture.Plan("| V-1 | `Demo.Check` | check canary `secret` absent |");
        var wrong = PlanCoverageFixture.Analyze("class Demo { void Check() { var x = \"secret\"; x.ShouldBe(\"secret\"); } }", plan);
        wrong.Diagnostics.ShouldContain(d => d.Code == "MISSING_CANARY" && d.Name == "secret", "coverage-canary-asserted");
        PlanCoverageFixture.Analyze("class Demo { void Check() { json.ShouldNotContain(\"secret\", \"absent\"); } }", plan).ExitCode.ShouldBe(0, "coverage-canary-exclusion");
        var namespacedPlan = PlanCoverageFixture.Plan("| V-1 | `One.Demo.Check` | check canary `secret` absent |");
        var decoy = PlanCoverageFixture.Analyze("namespace One { class Demo { void Check() { json.ShouldNotContain(Canary); } } } namespace Two { class Demo { const string Canary = \"secret\"; } }", namespacedPlan);
        decoy.Diagnostics.ShouldContain(d => d.Code == "MISSING_CANARY", "coverage-canary-namespace");
    }
    [Test]
    public void requires_members_and_collection_predicates()
    {
        var plan = PlanCoverageFixture.Plan("| V-1 | `Demo.Check` | check `Foreground` |" );
        var checklist = PlanCoverageFixture.Checklist(PlanCoverageFixture.Item("empty", "Foreground"), PlanCoverageFixture.Item("null", "Foreground"), PlanCoverageFixture.Item("value", "IdentityUnproven"));
        var wrong = PlanCoverageFixture.Analyze("class Demo { void Check() { dto.Foreground.ShouldBeNull(); dto.Eligible.ShouldBeFalse(); } }", plan, checklist);
        wrong.Diagnostics.ShouldContain(d => d.Code == "MISSING_EMPTY" && d.Name == "Foreground", "coverage-empty-distinct");
        wrong.Diagnostics.ShouldContain(d => d.Code == "MISSING_VALUE", "coverage-value-expected");
        PlanCoverageFixture.Analyze("class Demo { void Check() { var f = dto.Foreground; f.ShouldBeEmpty(); dto.Foreground.ShouldBeNull(); dto.Code.ShouldBe(Codes.IdentityUnproven); } }", plan, checklist).ExitCode.ShouldBe(0, "coverage-predicates-complete");
    }
    [Test]
    public void reports_unmapped_prose_and_opaque_helpers()
    {
        var plan = PlanCoverageFixture.Plan("| V-1 | `Demo.Check` | Verify incarnation facts; label `target-label` |" );
        var report = PlanCoverageFixture.Analyze("class Demo { void Check() { External.Assert(x); x.ShouldSomething(\"target-label\"); } }", plan);
        report.Diagnostics.ShouldContain(d => d.Code == "PROSE_UNMAPPED" && d.Name == "incarnation facts", "coverage-unmapped-visible");
        report.Diagnostics.ShouldContain(d => d.Code == "ASSERTION_UNMAPPED", "coverage-opaque-visible");
        PlanCoverageFixture.Analyze("class Demo { void Check() { Assert(); } void Assert() { Assert(); } }").Diagnostics.ShouldContain(d => d.Code == "HELPER_UNMAPPED", "coverage-helper-cycle");
    }
    [Test]
    public void tracks_literal_helper_arguments_without_executing_source()
    {
        var plan = PlanCoverageFixture.Plan("| V-1 | `Demo.Check` | label `target-label`; check canary `secret` absent |" );
        var source = "class Demo { void Check() { AssertNoPath(json, \"target-label\"); } static void AssertNoPath(string json, string witness) { foreach (var canary in new[] { \"secret\", \"another\" }) json.ShouldNotContain(canary, witness + \" case\"); } }";
        PlanCoverageFixture.Analyze(source, plan).ExitCode.ShouldBe(0, "coverage-helper-literals");
        var named = "class Demo { void Check() { AssertNoPath(witness: \"target-label\", json: payload); } static void AssertNoPath(string json, string witness) { json.ShouldNotContain(\"secret\", Case.Sensitive, witness); } }";
        PlanCoverageFixture.Analyze(named, plan).ExitCode.ShouldBe(0, "coverage-helper-named-arguments");
        PlanCoverageFixture.Analyze(source.Replace("\"target-label\");", "BuildLabel());"), plan).Diagnostics.ShouldContain(d => d.Code == "MISSING_LABEL" || d.Code == "HELPER_UNMAPPED", "coverage-helper-return-opaque");
    }
}
