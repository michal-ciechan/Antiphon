using Antiphon.Server.Application.Review.Workflows;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>The default pr-review workflow and the graph rules of design 3.2.</summary>
[Category("Unit")]
public class ReviewWorkflowValidatorTests
{
    private const string Header = "workflow: t\nversion: 1\n";

    [Test]
    public async Task The_default_pr_review_workflow_loads_and_is_valid()
    {
        var workflow = ReviewWorkflowYaml.LoadDefault();

        workflow.Name.ShouldBe("pr-review");
        workflow.Nodes.Count.ShouldBe(22);
        workflow.Sections.Select(s => s.Id).ShouldBe(["intake", "review", "decide", "branch"]);
        workflow.Edges.Count(e => e.MaxIterations is not null).ShouldBe(3);
        workflow.Edges.ShouldContain(e => e.From == "base-moved" && e.To == "base-drift" && e.Label == "moved|behind");
        var validation = ReviewWorkflowValidator.Validate(workflow);
        validation.Diagnostics.ShouldBeEmpty(string.Join("; ", validation.Diagnostics.Select(d => d.Message)));
        validation.Valid.ShouldBeTrue();
        workflow.BackEdges().Select(e => (e.From, e.To)).ShouldContain(("base-drift", "intake"));
        await Task.CompletedTask;
    }

    [Test]
    public async Task A_loop_with_no_gate_wait_or_limit_fails_the_cycle_rule()
    {
        var workflow = ReviewWorkflowYaml.Parse(Header + """
            nodes:
              packs:  { kind: code, run: BuildPacks, description: d }
              verify: { kind: completion, run: VerifyFindings, outputs: [disputed, kept], description: d }
            edges:
              - packs -> verify
              - verify[disputed] -> packs
            """);

        var validation = ReviewWorkflowValidator.Validate(workflow);

        validation.Valid.ShouldBeFalse();
        var cycle = validation.Diagnostics.ShouldHaveSingleItem();
        cycle.Code.ShouldBe(ReviewWorkflowValidator.Codes.UnboundedCycle);
        cycle.NodeIds.ShouldBe(["packs", "verify"], ignoreOrder: true);
        cycle.Edge.ShouldNotBeNull();
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("1", true)]
    [Arguments("0", true)]
    [Arguments("4", false)]
    [Arguments("1.5", false)]
    [Arguments("forever", false)]
    public async Task A_gate_breaks_a_cycle_only_with_autoRetries_from_0_to_3(string retries, bool valid)
    {
        var workflow = ReviewWorkflowYaml.Parse(Header + $$"""
            nodes:
              packs:  { kind: code, run: BuildPacks, description: d }
              verify: { kind: completion, run: VerifyFindings, outputs: [disputed], description: d }
              disputed-findings: { kind: gate, decisions: [re-review, drop, keep], autoRetries: {{retries}}, autoDecision: re-review, description: d }
            edges:
              - packs -> verify
              - verify[disputed] -> disputed-findings
              - disputed-findings[re-review] -> packs
            """);

        var validation = ReviewWorkflowValidator.Validate(workflow);

        validation.Valid.ShouldBe(valid);
        if (!valid)
        {
            validation.Diagnostics.Select(d => d.Code).ShouldBe(
                [ReviewWorkflowValidator.Codes.GateAutoRetries, ReviewWorkflowValidator.Codes.UnboundedCycle], ignoreOrder: true);
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task An_iteration_limit_must_overflow_to_a_gate_and_references_must_exist()
    {
        var workflow = ReviewWorkflowYaml.Parse(Header + """
            nodes:
              intake: { kind: code, run: Intake, description: d }
              drift:  { kind: decision, run: BaseDriftCheck, outputs: [affected], description: d }
            edges:
              - intake -> drift
              - { edge: "drift[affected] -> intake", maxIterations: 10, overflow: intake }
              - drift[affected] -> ghost
              - drift[unaffected] -> intake
            """);

        var codes = ReviewWorkflowValidator.Validate(workflow).Diagnostics.Select(d => d.Code).ToList();

        codes.ShouldContain(ReviewWorkflowValidator.Codes.OverflowNotGate);
        codes.ShouldContain(ReviewWorkflowValidator.Codes.UnknownNode);
        codes.ShouldContain(ReviewWorkflowValidator.Codes.UnknownOutput);
        codes.ShouldContain(ReviewWorkflowValidator.Codes.UnboundedCycle, "the plain unaffected edge closes a second loop");
        await Task.CompletedTask;
    }
}
