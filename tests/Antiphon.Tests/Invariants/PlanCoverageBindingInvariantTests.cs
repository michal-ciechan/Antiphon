using System.Text.Json;
using System.Xml.Linq;
using Antiphon.Checkpoints.Coverage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Checkpoints;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
public sealed class PlanCoverageBindingInvariantTests : CheckpointTestBase
{
    [Test]
    public void historical_plans_bind_without_suppression()
    {
        foreach (var (path, targets) in new[]
        {
            ("docs/superpowers/plans/2026-10-04-card-1035-checkpoint-child-cwd-plan.md", new[] { "child-cwd-matches-certified-root" }),
            ("docs/superpowers/plans/2026-10-04-card-1037-remote-follow-up-launch-guard-plan.md", new[] { "remote-pool-refused-before-insert", "local-pool-follow-up-admitted", "remote-standing-follow-up-admitted" }),
        })
        {
            var first = new StringWriter(); var second = new StringWriter();
            var command = new CoverageCommand();
            var firstExit = command.Run(CheckpointFixtures.RepoRoot, path, format: "json", output: first);
            CheckpointNativeDiagnostics.Coverage(firstExit, CheckpointFixtures.RepoRoot, first).ShouldBe(0, "c1046-real-plans");
            var secondExit = command.Run(CheckpointFixtures.RepoRoot, path, format: "json", output: second);
            CheckpointNativeDiagnostics.Coverage(secondExit, CheckpointFixtures.RepoRoot, second).ShouldBe(0, "c1046-real-plans");
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

    [Test]
    [Timeout(240_000)]
    public Task historical_row_timeout_checklist_binds_the_advisory_method(CancellationToken cancellationToken)
    {
        const string retired = "warns_when_estimate_exceeds_row_timeout";
        const string current = "warns_when_estimate_exceeds_advisory_threshold";
        const string qualified = "CheckpointImportTests." + current;
        const string censusPlan = "docs/superpowers/plans/2026-10-03-card-1005-opt-in-class-census-plan.md";
        var root = CheckpointFixtures.RepoRoot;
        foreach (var relative in new[]
        {
            "docs/superpowers/plans/2026-10-01-card-0891-plan-to-test-coverage-check-plan.md",
            censusPlan,
            "docs/superpowers/plans/2026-09-25-card-0723-checkpoint-runner-plan.md",
            "docs/superpowers/plans/2026-09-26-checkpoint-tool-hardening-plan.md",
        })
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            text.ShouldNotContain(retired, Case.Sensitive, "c1055-retired-name");
            text.ShouldContain(current, Case.Sensitive, "c1055-advisory-name");
        }

        var rename = File.ReadAllText(Path.Combine(root, "docs/superpowers/plans/2026-10-04-card-1041-row-timeout-advisory-plan.md"));
        rename.ShouldContain(retired, Case.Sensitive, "c1055-rename-record");
        rename.ShouldContain(current, Case.Sensitive, "c1055-rename-record");

        var command = new CoverageCommand();
        Bind(root, "docs/superpowers/plans/2026-10-01-card-0891-plan-to-test-coverage-check-plan.md", null, "R-1", 72, "c1055-0891");
        string[] censusSources =
        [
            "tests/Antiphon.Tests/Checkpoints/PlanCoverageCensusTests.cs",
            "tests/Antiphon.Tests/Checkpoints/PlanCoverageParserTests.cs",
            "tests/Antiphon.Tests/Checkpoints/PlanCoverageAssertionTests.cs",
            "tests/Antiphon.Tests/Checkpoints/PlanCoveragePcTests.cs",
            "tests/Antiphon.Tests/Checkpoints/PlanCoverageGoldenTests.cs",
            "tests/Antiphon.Tests/Checkpoints/PlanCoverageCommandTests.cs",
            "tests/Antiphon.Tests/Checkpoints/PlanCoverageHandleTests.cs",
            "tests/Antiphon.Tests/Checkpoints/PlanCoverageFixture.cs",
            "tests/Antiphon.Tests/Checkpoints/CheckpointImportTests.cs",
            "tests/Antiphon.Tests/Checkpoints/CheckpointManifestTests.cs",
            "tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs",
            "tests/Antiphon.Tests/Invariants/TestLaneCategoryGuardTests.cs",
            "tests/Shared/TestClassificationGuardTests.cs",
        ];

        // CARD-1005 also binds a control against the retired PowerShell namespace census.
        // Use its original source for that control and current source for the live obligations.
        var censusRoot = TempDir();
        foreach (var relative in censusSources.Prepend(censusPlan))
        {
            var destination = Path.Combine(censusRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(root, relative), destination);
        }
        File.Copy(
            Path.Combine(root, "tests/Antiphon.Tests/Checkpoints/Fixtures/PlanCoverage/CheckpointNamespaceCensusUsageTests.cs.txt"),
            Path.Combine(censusRoot, "tests/Antiphon.Tests/Checkpoints/CheckpointNamespaceCensusUsageTests.cs"));
        Bind(censusRoot, censusPlan, censusSources, "R-4", 177, "c1055-1005");

        void Bind(string sourceRoot, string plan, string[]? tests, string id, int obligations, string label)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = new StringWriter();
            var exit = command.Run(sourceRoot, plan, tests, "json", output: output);
            CheckpointNativeDiagnostics.Coverage(exit, sourceRoot, output).ShouldBe(0, label);
            using var document = JsonDocument.Parse(output.ToString());
            var report = document.RootElement;
            report.GetProperty("invalid").GetBoolean().ShouldBeFalse(label);
            var summary = report.GetProperty("summary");
            summary.GetProperty("result").GetString().ShouldBe("clean", label);
            summary.GetProperty("obligations").GetInt32().ShouldBe(obligations, label);
            summary.GetProperty("matched").GetInt32().ShouldBe(obligations, label);
            summary.GetProperty("missing").GetInt32().ShouldBe(0, label);
            foreach (var diagnostic in report.GetProperty("diagnostics").EnumerateArray())
                (diagnostic.GetProperty("test").GetString() ?? "").Contains(retired, StringComparison.Ordinal).ShouldBeFalse(label);
            var obligation = report.GetProperty("obligations").EnumerateArray().Single(item =>
                item.GetProperty("id").GetString() == id
                && item.GetProperty("kind").GetString() == "method"
                && item.GetProperty("test").GetString() == qualified);
            var match = obligation.GetProperty("matches").EnumerateArray().Single();
            match.GetProperty("path").GetString().ShouldBe("tests/Antiphon.Tests/Checkpoints/CheckpointImportTests.cs", label);
            match.GetProperty("line").GetInt32().ShouldBeGreaterThan(0, label);
        }

        return Task.CompletedTask;
    }

    [Test]
    public void linked_shared_sources_are_literal_compile_items()
    {
        var root = CheckpointFixtures.RepoRoot;
        var directory = Path.Combine(root, "tests", "Shared", "LiveClaude");
        var files = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        files.ShouldNotBeEmpty();
        foreach (var project in new[] { "tests/Antiphon.Tests/Antiphon.Tests.csproj", "tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj" })
        {
            var includes = XDocument.Load(Path.Combine(root, project)).Descendants("Compile").Attributes("Include")
                .Select(attribute => attribute.Value.Replace('\\', '/')).ToArray();
            includes.Any(WildcardSharedInclude).ShouldBeFalse(project + " wildcard linked Shared source");
            var live = includes.Where(include => include.StartsWith("../Shared/LiveClaude/", StringComparison.Ordinal)
                && include.EndsWith(".cs", StringComparison.Ordinal)).ToArray();
            foreach (var file in files)
                live.ShouldContain("../Shared/LiveClaude/" + file, project);
            foreach (var include in live)
            {
                var name = Path.GetFileName(include.Replace('\\', '/'));
                files.ShouldContain(name, project + " lists " + include);
                File.Exists(Path.Combine(directory, name!)).ShouldBeTrue(include);
            }
        }
        WildcardSharedInclude("../Shared/LiveClaude/*.cs").ShouldBeTrue("a glob include is the refused shape");
        WildcardSharedInclude("../Shared/LiveClaude/LiveClaudeOutcome.cs").ShouldBeFalse("a literal include is admitted");
    }

    private static bool WildcardSharedInclude(string include)
    {
        var value = include.Replace('\\', '/');
        return value.Contains("Shared", StringComparison.Ordinal) && (value.Contains('*') || value.Contains('?'));
    }
}
