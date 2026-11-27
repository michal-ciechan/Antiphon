using Antiphon.Server.Application.Review;
using Antiphon.Server.Application.Review.Workflows;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

/// <summary>The default pr-review workflow and the graph rules of design 3.2.</summary>
[Category("Unit")]
public class ReviewWorkflowValidatorTests
{
    private const string Header = "workflow: t\nversion: 1\n";

    [Test]
    public void C2109_S1_Unknown_step_is_reported_at_its_node()
    {
        var embedded = ReviewWorkflowYaml.LoadDefault();
        embedded.Node("reviewer")!.Run.ShouldBeNull();
        ReviewWorkflowValidator.Validate(embedded).Valid.ShouldBeTrue();

        var workflow = embedded with
        {
            Nodes = embedded.Nodes.Select(node => node.Id == "categorise" ? node with { Run = "NoSuchStep" } : node).ToArray(),
        };
        var matching = ReviewWorkflowValidator.Validate(workflow).Diagnostics.Where(diagnostic => diagnostic.Code == "unknown-step").ToArray();

        matching.Length.ShouldBe(1);
        matching[0].Severity.ShouldBe(WorkflowDiagnosticSeverity.Error);
        matching[0].NodeIds.ShouldBe(["categorise"]);
    }

    [Test]
    public void C2109_S1_Registered_step_requires_its_kind()
    {
        var graph = WithNode(ReviewWorkflowYaml.LoadDefault(), "categorise", node => node with { Kind = ReviewNodeKind.Code });
        AssertNodeError(graph, ReviewWorkflowValidator.Codes.KindMismatch, "categorise");
    }

    [Test]
    public void C2109_S1_Fan_out_child_cannot_be_a_frontier()
    {
        var embedded = ReviewWorkflowYaml.LoadDefault();
        ReviewWorkflowValidator.Validate(embedded).Valid.ShouldBeTrue();
        var graph = embedded with
        {
            Edges = embedded.Edges.Select(edge => edge.From == "review" ? edge with { To = "reviewer" } : edge).ToArray(),
        };
        var violation = ReviewWorkflowValidator.Validate(graph).Diagnostics.Single(d => d.Code == ReviewWorkflowValidator.Codes.ChildOnly);
        violation.Severity.ShouldBe(WorkflowDiagnosticSeverity.Error);
        violation.Edge!.To.ShouldBe("reviewer");
        violation.Edge.From.ShouldBe("review");
    }

    [Test]
    public void C2109_S1_Unsupported_declared_input_is_diagnosed()
    {
        var embedded = ReviewWorkflowYaml.LoadDefault();
        AssertNodeError(WithParam(embedded, "categorise", "inputs", new[] { "intake" }),
            ReviewWorkflowValidator.Codes.UnsupportedInput, "categorise");
        foreach (var value in new object[] { "intake", new object[] { "intake", 42 } })
            AssertNodeError(WithParam(embedded, "categorise", "inputs", value),
                ReviewWorkflowValidator.Codes.InvalidInput, "categorise");
    }

    [Test]
    public void C2109_S1_Missing_and_bypassed_producers_are_diagnosed()
    {
        var graph = ReviewWorkflowYaml.Parse(Header + """
            nodes:
              intake: { kind: code, run: Intake, description: d }
              route: { kind: code, run: RouteAfterRound, outputs: [findings, clean], onFailure: continue, fallback: clean, description: d }
              delta: { kind: code, run: Delta, description: d }
              packs: { kind: code, run: BuildPacks, description: d }
            edges:
              - intake -> route
              - route[findings] -> delta
              - route[clean] -> packs
              - delta -> packs
            """);
        ReviewStepContract? FindStep(string? name) => name == "BuildPacks"
            ? new ReviewStepContract(ReviewNodeKind.Code, [], AcceptsInputs: true)
            : ReviewWorkflowSteps.Find(name)?.Contract;

        foreach (var (producer, expected) in new[] { ("intake", 0), ("missing", 1), ("delta", 1) })
        {
            var result = ReviewWorkflowValidator.Validate(WithParam(graph, "packs", "inputs", new[] { producer }), FindStep);
            var errors = result.Diagnostics.Where(d => d.Code == ReviewWorkflowValidator.Codes.UnavailableInput).ToArray();
            errors.Length.ShouldBe(expected, producer);
            result.Diagnostics.ShouldNotContain(d => d.Code == ReviewWorkflowValidator.Codes.UnsupportedInput);
            if (expected > 0)
            {
                errors[0].NodeIds.ShouldBe(["packs"]);
                errors[0].Severity.ShouldBe(WorkflowDiagnosticSeverity.Error);
            }
        }
    }

    [Test]
    public void C2109_S1_Chain_must_exist_in_the_handlers_registry()
    {
        var embedded = ReviewWorkflowYaml.LoadDefault();
        var decision = new HashSet<string> { ReviewCategorisation.DefaultChain, "only-decision" };
        var completion = new HashSet<string> { "completions-cheap", "only-completion" };
        WorkflowValidation Validate(WorkflowDefinition graph) => ReviewWorkflowValidator.Validate(graph,
            name => ReviewWorkflowSteps.Find(name)?.Contract,
            new ReviewWorkflowChainNames(decision, completion));

        foreach (var (id, chain) in new[] { ("categorise", "only-completion"), ("verify", "only-decision") })
        {
            var error = Validate(WithParam(embedded, id, "chain", chain)).Diagnostics
                .Single(d => d.Code == ReviewWorkflowValidator.Codes.UnknownChain);
            error.NodeIds.ShouldBe([id]);
            error.Severity.ShouldBe(WorkflowDiagnosticSeverity.Error);
            Validate(WithParam(embedded, id, "chain", null)).Valid.ShouldBeTrue();
        }
    }

    [Test]
    public void C2109_S1_Authored_output_must_be_emittable()
    {
        var graph = WithParam(ReviewWorkflowYaml.LoadDefault(), "route", "outputs",
            new[] { "bot-round-limit", "findings", "clean", "made-up" });
        AssertNodeError(graph, ReviewWorkflowValidator.Codes.UnsupportedOutput, "route");
    }

    [Test]
    public void C2109_S1_Duplicate_route_has_a_located_error()
    {
        var graph = ReviewWorkflowYaml.LoadDefault();
        var duplicate = new WorkflowEdge("route", "human-review", "findings", null, null, null);
        var result = ReviewWorkflowValidator.Validate(graph with { Edges = [.. graph.Edges, duplicate] });
        var error = result.Diagnostics.Single(d => d.Code == ReviewWorkflowValidator.Codes.AmbiguousRoute);
        error.Edge.ShouldBeSameAs(duplicate);
        error.Severity.ShouldBe(WorkflowDiagnosticSeverity.Error);
    }

    [Test]
    public void C2109_S1_Wait_bounds_a_cycle_but_plain_cycle_fails()
    {
        var graph = ReviewWorkflowYaml.Parse(Header + """
            nodes:
              intake: { kind: code, run: Intake, description: d }
              park: { kind: wait, on: [push], description: d }
            edges:
              - intake -> park
              - park -> intake
            """);
        ReviewWorkflowValidator.Validate(graph).Valid.ShouldBeTrue();
        var plain = WithNode(graph, "park", node => node with { Kind = ReviewNodeKind.Code, Run = "RecordBaseDrift" });
        var cycle = ReviewWorkflowValidator.Validate(plain).Diagnostics.ShouldHaveSingleItem();
        cycle.Code.ShouldBe(ReviewWorkflowValidator.Codes.UnboundedCycle);
        cycle.Severity.ShouldBe(WorkflowDiagnosticSeverity.Error);
        cycle.NodeIds.ShouldBe(["intake", "park"], ignoreOrder: true);
        cycle.Edge.ShouldNotBeNull();
    }

    private static WorkflowDefinition WithNode(WorkflowDefinition graph, string id, Func<WorkflowNode, WorkflowNode> edit) =>
        graph with { Nodes = graph.Nodes.Select(node => node.Id == id ? edit(node) : node).ToArray() };

    private static WorkflowDefinition WithParam(WorkflowDefinition graph, string id, string key, object? value) =>
        WithNode(graph, id, node => node with { Params = new Dictionary<string, object?>(node.Params) { [key] = value } });

    private static void AssertNodeError(WorkflowDefinition graph, string code, string id)
    {
        var error = ReviewWorkflowValidator.Validate(graph).Diagnostics.Single(d => d.Code == code);
        error.Severity.ShouldBe(WorkflowDiagnosticSeverity.Error);
        error.NodeIds.ShouldBe([id]);
    }

    [Test]
    public async Task The_default_pr_review_workflow_loads_and_is_valid()
    {
        var workflow = ReviewWorkflowYaml.LoadDefault();

        workflow.Name.ShouldBe("pr-review");
        workflow.Nodes.Count.ShouldBe(24);
        workflow.Edges.ShouldContain(e => e.From == "merge-gate" && e.To == "base-moved" && e.Label == "approve");
        workflow.Node("merge-gate")!.StringParam("optional").ShouldBe("True", StringCompareShould.IgnoreCase);
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

        var validation = ReviewWorkflowValidator.ValidateStructure(workflow);

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

        var validation = ReviewWorkflowValidator.ValidateStructure(workflow);

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

        var codes = ReviewWorkflowValidator.ValidateStructure(workflow).Diagnostics.Select(d => d.Code).ToList();

        codes.ShouldContain(ReviewWorkflowValidator.Codes.OverflowNotGate);
        codes.ShouldContain(ReviewWorkflowValidator.Codes.UnknownNode);
        codes.ShouldContain(ReviewWorkflowValidator.Codes.UnknownOutput);
        codes.ShouldContain(ReviewWorkflowValidator.Codes.UnboundedCycle, "the plain unaffected edge closes a second loop");
        await Task.CompletedTask;
    }
}
