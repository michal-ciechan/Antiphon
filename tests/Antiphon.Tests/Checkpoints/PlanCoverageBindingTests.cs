using System.Text.Json;
using Antiphon.Checkpoints.Coverage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
public sealed class PlanCoverageBindingTests : CheckpointTestBase
{
    [Test]
    public void called_local_functions_and_params_bind_at_call_site()
    {
        const string source = """
            class Demo {
                async Task Check() {
                    const string captured = "target-label";
                    void AssertChildren(int count, params string[] phases) {
                        count.ShouldBe(2, captured);
                        phases.ShouldContain("run", "phase-label");
                    }
                    AssertChildren(2, "build", "run");
                    AssertChildren(3, "build", "run", "run");
                    void AssertDefault(string label = "target-label") => x.ShouldBe(1, label);
                    AssertDefault();
                    AssertDefault(label: "target-label");
                    async Task AssertAsync(string label) { x.ShouldBe(1, label); }
                    await AssertAsync("target-label");
                    void AssertChain(string label) { AssertClass(label); }
                    AssertChain("target-label");
                }
                void AssertClass(string label) { x.ShouldBe(1, label); }
            }
            """;
        var report = PlanCoverageFixture.Analyze(source);
        report.ExitCode.ShouldBe(0, "c1046-local-bind");
        report.Obligations.Single(o => o.Kind == "label").Matches
            .ShouldBe(new[] { 5, 5, 10, 10, 13, 18 }.Select(line => new CoverageMatch("Demo.cs", line)), "c1046-local-bind");
        foreach (var args in new[] { "", "\"target-label\"", "\"other\", \"target-label\"", "new string[] { \"target-label\" }", "labels: new[] { \"target-label\" }" })
        {
            var label = args.Length == 0 ? "\"target-label\"" : "labels";
            var input = "class Demo { void Check() { AssertParams(" + args + "); void AssertParams(params string[] labels) { x.ShouldBe(1, " + label + "); } } }";
            PlanCoverageFixture.Analyze(input).ExitCode.ShouldBe(0, "c1046-local-bind");
        }
        PlanCoverageFixture.Analyze("class Demo { void Check() { AssertNamed(label: \"target-label\", n: 1); void AssertNamed(int n, string label) => x.ShouldBe(n, label); } }")
            .ExitCode.ShouldBe(0, "c1046-local-bind");
        var capturedParameter = "class Demo { void Check() { AssertOuter(\"target-label\"); void AssertOuter(string label) { AssertInner(); void AssertInner() => x.ShouldBe(1, label); } } }";
        PlanCoverageFixture.Analyze(capturedParameter).ExitCode.ShouldBe(0, "c1046-local-bind");
        PlanCoverageFixture.Analyze("class Demo { void Check() { void Helper() => x.ShouldBe(1, label); const string label = \"target-label\"; Helper(); } }")
            .ExitCode.ShouldBe(0, "c1046-local-bind");
    }

    [Test]
    public void local_helper_scope_does_not_invent_evidence()
    {
        var worlds = new[]
        {
            "class Demo { void Check() { void AssertDecoy() => x.ShouldBe(1, \"target-label\"); } }",
            "class Demo { void Check() { AssertDecoy(); } void Other() { void AssertDecoy() => x.ShouldBe(1, \"target-label\"); } }",
            "class Demo { void Check() { { void AssertDecoy() => x.ShouldBe(1, \"target-label\"); } { AssertDecoy(); } } }",
            "class Demo { void Check() { void AssertDecoy() => x.ShouldBe(1, \"other-label\"); AssertDecoy(); } void AssertDecoy() => x.ShouldBe(1, \"target-label\"); }",
            "class Demo { void Check() { void AssertDecoy() => x.ShouldBe(1, \"target-label\"); opaque.AssertDecoy(); } }",
            "class Demo { void Check() { void AssertDecoy(int n) => x.ShouldBe(n, \"target-label\"); void AssertDecoy(string n) => x.ShouldBe(n, \"target-label\"); AssertDecoy(1); } }",
            "class Demo { void Check() { void AssertDecoy() { AssertDecoy(); } AssertDecoy(); } }",
            "class Demo { void Check() { { const string label = \"target-label\"; } void AssertDecoy() => x.ShouldBe(1, label); AssertDecoy(); } }",
            "class Demo { void Check() { const string label = \"target-label\"; void AssertDecoy(string label) => x.ShouldBe(1, label); AssertDecoy(BuildLabel()); } }",
            "class Demo { void Check() { const string label = \"target-label\"; void AssertDecoy() { string label = BuildLabel(); x.ShouldBe(1, label); } AssertDecoy(); } }",
            "class Demo { void Check() { void AssertDecoy() => x.ShouldBe(1, label); { const string label = \"target-label\"; AssertDecoy(); } } }",
            "class Demo { void Check() { const string label = \"other-label\"; void AssertDecoy() => x.ShouldBe(1, label); { const string label = \"target-label\"; AssertDecoy(); } } }",
            "class Demo { void Check() { void AssertDecoy(string label) => x.ShouldBe(1, label); AssertDecoy(wrong: \"target-label\"); } }",
            "class Demo { const string label = \"target-label\"; void Check() { var label = BuildLabel(); void AssertDecoy(string value) => x.ShouldBe(1, value); AssertDecoy(label); } }",
            "class Demo { void Check() { const string label = \"target-label\"; void AssertOuter() { var label = BuildLabel(); void AssertInner() => x.ShouldBe(1, label); AssertInner(); } AssertOuter(); } }",
        };
        foreach (var source in worlds)
        {
            var report = PlanCoverageFixture.Analyze(source);
            report.ExitCode.ShouldBe(1, "c1046-local-scope");
            report.Obligations.Single(o => o.Kind == "label").Matches.ShouldBeEmpty("c1046-local-scope");
            report.Diagnostics.ShouldContain(d => d.Code == "MISSING_LABEL" || d.Code == "HELPER_UNMAPPED", "c1046-local-scope");
        }
        var deep = "class Demo { void Check() { Assert0(); " + string.Join(" ", Enumerable.Range(0, 14).Select(i =>
            "void Assert" + i + "() { " + (i == 13 ? "x.ShouldBe(1, \"target-label\");" : "Assert" + (i + 1) + "();") + " }")) + " } }";
        var overflow = PlanCoverageFixture.Analyze(deep);
        overflow.ExitCode.ShouldBe(1, "c1046-local-scope");
        overflow.Diagnostics.ShouldContain(d => d.Code == "HELPER_UNMAPPED", "c1046-local-scope");
        var nearest = PlanCoverageFixture.Analyze("class Demo { void Check() { void AssertSame() => x.ShouldBe(1, \"decoy-label\"); { AssertSame(); void AssertSame() => x.ShouldBe(1, \"target-label\"); } } }");
        nearest.ExitCode.ShouldBe(0, "c1046-local-scope");
        nearest.Obligations.Single(o => o.Kind == "label").Matches.Count.ShouldBe(1, "c1046-local-scope");
    }

    [Test]
    public void literal_pc_filter_binds_method_without_reading_mutation_cell()
    {
        const string source = "namespace Exact.Namespace { class Demo { void Check() { x.ShouldBe(1, \"target-label\"); } } }";
        foreach (var (filter, identity) in new[] { ("/*/*/Demo/Check", "Demo.Check"), ("/*/Exact.Namespace/Demo/Check", "Exact.Namespace.Demo.Check"), ("/*/Exact.Namespace/Exact.Namespace.Demo/Check", "Exact.Namespace.Demo.Check") })
        {
            var plan = PlanCoverageFixture.Plan(row: "", pc: "| PC-1 | Remove `Decoy.Other` and `decoy-label` | `" + filter + "` | fails at `target-label` |");
            var report = PlanCoverageFixture.Analyze(source, plan);
            report.ExitCode.ShouldBe(0, "c1046-pc-filter");
            var obligation = report.Obligations.Single();
            obligation.Test.ShouldBe(identity, "c1046-pc-filter");
            obligation.Kind.ShouldBe("label", "c1046-pc-filter");
            obligation.PlanLine.ShouldBe(6, "c1046-pc-filter");
            obligation.PlanColumn.ShouldBe(plan.Split('\n')[5].IndexOf("target-label", StringComparison.Ordinal) + 1, "c1046-pc-filter");
            obligation.Matches.ShouldBe([new CoverageMatch("Demo.cs", 1)], "c1046-pc-filter");
            report.Pcs.Single().Reachability.ShouldBe("unproven", "c1046-pc-filter");
        }
        foreach (var cell in new[] { "`/*/*/Demo/*`", "`/*/*/Demo/Check*`", "`/*/*/Demo/(Check)\\|(Other)`", "`/*/*/Demo/Check[Category=Unit]`", "`dotnet run --treenode-filter /*/*/Demo/Check`", "`/*/*/Demo/Check` `/*/*/Demo/Other`" })
        {
            var report = PlanCoverageFixture.Analyze(source, PlanCoverageFixture.Plan(row: "", pc: "| PC-1 | mutation | " + cell + " | `target-label` |"));
            report.ExitCode.ShouldBe(1, "c1046-pc-filter");
            report.Diagnostics.ShouldContain(d => d.Code == "METHOD_UNMAPPED", "c1046-pc-filter");
        }
        var conflict = PlanCoverageFixture.Analyze(source, PlanCoverageFixture.Plan(row: "", pc: "| PC-1 | mutation | `/*/*/Demo/Check` | `Other.Check`, `target-label` |"));
        conflict.ExitCode.ShouldBe(1, "c1046-pc-filter");
        conflict.Diagnostics.ShouldContain(d => d.Code == "METHOD_UNMAPPED", "c1046-pc-filter");
        foreach (var pc in new[] { "| PC-1 | mutation | `Demo.Check`, `target-label` |", "| PC-1 | mutation | V-1 fails at `target-label` |" })
            PlanCoverageFixture.Analyze(source, PlanCoverageFixture.Plan(pc: pc)).ExitCode.ShouldBe(0, "c1046-pc-filter");
        PlanCoverageFixture.Analyze(source, PlanCoverageFixture.Plan(pc: "| PC-1 | mutation | `/*/*/Demo/Check` | V-1 fails at `target-label` |"))
            .ExitCode.ShouldBe(0, "c1046-pc-filter");
        var referenceConflict = PlanCoverageFixture.Analyze(source, PlanCoverageFixture.Plan(pc: "| PC-1 | mutation | `/*/*/Other/Check` | V-1 fails at `target-label` |"));
        referenceConflict.ExitCode.ShouldBe(1, "c1046-pc-filter");
        referenceConflict.Diagnostics.ShouldContain(d => d.Code == "METHOD_UNMAPPED", "c1046-pc-filter");
    }

    [Test]
    public void local_helper_predecessors_keep_order_and_reachability()
    {
        const string source = """
            class Demo {
                void Check() {
                    void AssertLater() {
                        x.ShouldBe(1);
                        x.ShouldBe(2, "target-label");
                    }
                    x.ShouldBe(0, "caller-label");
                    AssertLater();
                }
            }
            """;
        var plan = PlanCoverageFixture.Plan(pc: "| PC-1 | mutation | `Demo.Check`, `target-label` |");
        var red = PlanCoverageFixture.Analyze(source, plan);
        red.ExitCode.ShouldBe(1, "c1046-pc-order");
        red.Pcs.Single().Status.ShouldBe("unlabeled-predecessor", "c1046-pc-order");
        red.Pcs.Single().TargetLine.ShouldBe(5, "c1046-pc-order");
        red.Pcs.Single().PredecessorLine.ShouldBe(4, "c1046-pc-order");
        red.Diagnostics.ShouldContain(d => d.Code == "PC_UNLABELED_PREDECESSOR" && d.TestPath == "Demo.cs" && d.TestLine == 4, "c1046-pc-order");
        red.Pcs.Single().EarlierOtherLabels.ShouldBe(["caller-label"], "c1046-pc-order");
        var labeled = PlanCoverageFixture.Analyze(source.Replace("x.ShouldBe(1);", "x.ShouldBe(1, \"helper-label\");"), plan);
        labeled.ExitCode.ShouldBe(0, "c1046-pc-order");
        labeled.Pcs.Single().Status.ShouldBe("static-labeled", "c1046-pc-order");
        labeled.Pcs.Single().EarlierOtherLabels.ShouldBe(["caller-label", "helper-label"], "c1046-pc-order");
        labeled.Diagnostics.ShouldAllBe(d => d.Code == "PC_EARLIER_OTHER_LABEL", "c1046-pc-order");
        var missing = PlanCoverageFixture.Analyze(source.Replace("x.ShouldBe(2, \"target-label\");", ""), plan);
        missing.ExitCode.ShouldBe(1, "c1046-pc-order");
        missing.Pcs.Single().Status.ShouldBe("missing-target", "c1046-pc-order");
        foreach (var report in new[] { red, labeled, missing })
            report.Pcs.Single().Reachability.ShouldBe("unproven", "c1046-pc-order");
    }

    [Test]
    public void historical_plans_bind_without_suppression()
    {
        foreach (var (name, targets) in new[]
        {
            ("2026-10-04-card-1035-checkpoint-child-cwd-plan.md", new[] { "child-cwd-matches-certified-root" }),
            ("2026-10-04-card-1037-remote-follow-up-launch-guard-plan.md", new[] { "remote-pool-refused-before-insert", "local-pool-follow-up-admitted", "remote-standing-follow-up-admitted" }),
        })
        {
            var path = "docs/superpowers/plans/" + name;
            var first = new StringWriter(); var second = new StringWriter();
            var command = new CoverageCommand();
            command.Run(CheckpointFixtures.RepoRoot, path, format: "json", output: first).ShouldBe(0, "c1046-real-plans");
            command.Run(CheckpointFixtures.RepoRoot, path, format: "json", output: second).ShouldBe(0, "c1046-real-plans");
            second.ToString().ShouldBe(first.ToString(), "c1046-real-plans-deterministic");
            using var document = JsonDocument.Parse(first.ToString());
            var root = document.RootElement;
            foreach (var key in new[] { "missing", "unmapped", "pcIssues" })
                root.GetProperty("summary").GetProperty(key).GetInt32().ShouldBe(0, "c1046-real-plans");
            var pcs = root.GetProperty("pcs").EnumerateArray().ToArray();
            pcs.Select(p => p.GetProperty("target").GetString()).Order().ShouldBe(targets.Order(), "c1046-real-plans");
            foreach (var pc in pcs)
            {
                pc.GetProperty("status").GetString().ShouldBe("static-labeled", "c1046-real-plans");
                pc.GetProperty("reachability").GetString().ShouldBe("unproven", "c1046-real-plans");
                pc.GetProperty("targetLine").GetInt32().ShouldBeGreaterThan(0, "c1046-real-plans");
                var target = pc.GetProperty("target").GetString();
                var identity = pc.GetProperty("test").GetString();
                var obligation = root.GetProperty("obligations").EnumerateArray().Single(o => o.GetProperty("kind").GetString() == "label"
                    && o.GetProperty("name").GetString() == target && o.GetProperty("test").GetString() == identity && o.GetProperty("id").GetString()!.StartsWith("PC-", StringComparison.Ordinal));
                var match = obligation.GetProperty("matches").EnumerateArray().First();
                var source = File.ReadAllText(Path.Combine(CheckpointFixtures.RepoRoot, match.GetProperty("path").GetString()!));
                var invocations = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Where(call => call.Expression is MemberAccessExpressionSyntax access
                        && access.Name.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == match.GetProperty("line").GetInt32());
                var literalMessages = invocations.SelectMany(call => call.ArgumentList.Arguments.SelectMany(argument => argument.Expression.DescendantNodesAndSelf()
                    .OfType<LiteralExpressionSyntax>().Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression)).Select(literal => literal.Token.ValueText))).ToArray();
                literalMessages.ShouldContain(message => message == target || message.StartsWith(target + ":", StringComparison.Ordinal)
                    || message.StartsWith(target + " ", StringComparison.Ordinal), "c1046-real-plans");
            }
        }
    }
}
