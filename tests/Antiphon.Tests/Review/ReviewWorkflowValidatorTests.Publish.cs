using Antiphon.Server.Application.Review.Workflows;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Review;

public partial class ReviewWorkflowValidatorTests
{
    [Test]
    public void C2110_S1_unit_Declared_publish_roles_and_input_shape_are_validated()
    {
        var graph = ReviewWorkflowYaml.LoadDefault();
        graph = graph with { Nodes = [.. graph.Nodes, graph.Node("verify")! with { Id = "verify-alternate" }] };
        var invalid = new (string Label, object? Inputs)[]
        {
            ("scalar", "collate"),
            ("null", null),
            ("number", new object[] { "collate", 42 }),
            ("blank", new[] { "collate", "verify", " " }),
            ("duplicate", new[] { "collate", "collate", "verify" }),
            ("case", new[] { "Collate", "verify" }),
            ("self", new[] { "collate", "verify", "publish" }),
            ("unknown", new[] { "collate", "verify", "missing" }),
            ("fan-out child", new[] { "collate", "verify", "reviewer" }),
            ("wrong contract", new[] { "collate", "verify", "group" }),
            ("too many", Enumerable.Repeat("verify", 9).ToArray()),
            ("missing verify", new[] { "collate", "categorise" }),
            ("ambiguous verify", new[] { "collate", "verify", "verify-alternate" }),
        };

        ReviewWorkflowValidator.Validate(WithParams(graph, "inputs", new[] { "select", "verify", "categorise", "collate" }))
            .Valid.ShouldBeTrue();
        var observations = invalid.Select(arm => (arm.Label,
            Validation: ReviewWorkflowValidator.Validate(WithParams(graph, "inputs", arm.Inputs)))).ToList();
        foreach (var (label, validation) in observations)
            AssertField(validation, "invalid-input", "inputs", label);
    }

    [Test]
    public void C2110_S1_unit_Required_inputs_dominate_publish_on_admitted_paths()
    {
        var graph = WithParams(ReviewWorkflowYaml.LoadDefault(), "inputs", new[] { "collate", "verify", "categorise" });
        var defaultBypass = ChangeEdge(graph, "review", "collate", "verify");
        var labelledBypass = graph with { Edges = [.. graph.Edges, new("delta", "publish", "full", null, null, null)] };
        var overflowBypass = ChangeEdge(graph, "human-review", "merge-gate", "publish") with
        {
            Edges = ChangeEdge(graph, "human-review", "merge-gate", "publish").Edges
                .Select(e => e.From == "intake" ? e with { MaxIterations = 1, Overflow = "human-review" } : e).ToList(),
        };
        var cycleOnly = graph with
        {
            Edges = graph.Edges.Select(e => (e.From, e.To) switch
            {
                ("collate", "verify") => e with { To = "publish" },
                ("await-fixes", "intake") => e with { To = "verify" },
                _ => e,
            }).ToList(),
        };
        var optionalBypass = graph with { Edges = [.. graph.Edges, new("delta", "select", "full", null, null, null)] };
        var anotherCollate = ChangeEdge(graph, "collate", "verify", "other-collate");
        anotherCollate = anotherCollate with
        {
            Nodes = [.. anotherCollate.Nodes, graph.Node("collate")! with { Id = "other-collate" }],
            Edges = [.. anotherCollate.Edges, new("other-collate", "verify", null, null, null, null)],
        };

        ReviewWorkflowValidator.Validate(graph).Valid.ShouldBeTrue("a return to intake starts a new iteration");
        ReviewWorkflowValidator.Validate(optionalBypass).Valid.ShouldBeTrue("optional context may be bypassed");
        var cases = new[] { defaultBypass, labelledBypass, overflowBypass, cycleOnly, anotherCollate };
        foreach (var (definition, index) in cases.Select((value, index) => (value, index)))
        {
            var validation = ReviewWorkflowValidator.Validate(definition);
            validation.Diagnostics.Where(d => d.Severity == WorkflowDiagnosticSeverity.Error).Select(d => d.Code)
                .ShouldBe(["unavailable-input"], $"path case {index}");
            AssertField(validation, "unavailable-input", "inputs", $"path case {index}");
        }
    }

    [Test]
    public void C2110_S1_unit_Publish_template_requires_one_valid_coverage_slot()
    {
        var graph = ReviewWorkflowYaml.LoadDefault();
        var invalid = new (string Label, object? Template)[]
        {
            ("zero coverage", "{{verification}}"),
            ("two coverage slots", "{{defaultBody}}\n{{findings}}"),
            ("section inline", "Findings: {{findings}}"),
            ("unknown", "{{findings}}\n{{privateBody}}"),
            ("malformed", "{{findings}}\n{{round"),
            ("unbound", "{{findings}}\n{{selectedChecks}}"),
            ("wrong type", new[] { "{{findings}}" }),
            ("oversized UTF-8", "{{findings}}\n" + new string('é', 4096)),
        };

        ReviewWorkflowValidator.Validate(WithParams(graph, "summaryTemplate", "# Round {{round}}\n\n{{findings}}"))
            .Valid.ShouldBeTrue();
        var bound = WithParams(graph, "inputs", new[] { "collate", "verify", "select" });
        ReviewWorkflowValidator.Validate(WithParams(bound, "summaryTemplate", "{{defaultBody}}\n\n{{selectedChecks}}"))
            .Valid.ShouldBeTrue();
        var observations = invalid.Select(arm => (arm.Label,
            Validation: ReviewWorkflowValidator.Validate(WithParams(graph, "summaryTemplate", arm.Template)))).ToList();
        foreach (var (label, validation) in observations)
            AssertField(validation, "invalid-summary-template", "summaryTemplate", label);
    }

    private static WorkflowDefinition WithParams(WorkflowDefinition graph, string name, object? value) => graph with
    {
        Nodes = graph.Nodes.Select(n => n.Id == "publish"
            ? n with { Params = new Dictionary<string, object?>(n.Params) { [name] = value } }
            : n).ToList(),
    };

    private static WorkflowDefinition ChangeEdge(WorkflowDefinition graph, string from, string to, string target) => graph with
    {
        Edges = graph.Edges.Select(e => (e.From, e.To) == (from, to) ? e with { To = target } : e).ToList(),
    };

    private static void AssertField(WorkflowValidation validation, string code, string field, string arm)
    {
        validation.Valid.ShouldBeFalse(arm);
        var error = validation.Diagnostics.SingleOrDefault(d => d.Code == code);
        error.ShouldNotBeNull(arm);
        error.Severity.ShouldBe(WorkflowDiagnosticSeverity.Error, arm);
        error.NodeIds.ShouldBe(["publish"], arm);
        error.Field.ShouldBe(field, arm);
    }
}
