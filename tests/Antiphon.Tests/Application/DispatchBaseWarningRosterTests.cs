using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1155. The roster pin parses the E2E source with Roslyn and records each
/// public instance method of DispatchBaseWarningDeliveryE2ETests whose attributes
/// name Test or TestAttribute. A conditional compilation directive fails the pin
/// closed. Those names must equal the plan's CP-2..CP-14 filters. Queue insert,
/// Sent, or ack does not discharge an E2E row.
/// </summary>
[Category("Unit")]
public class DispatchBaseWarningRosterTests
{
    private const string PlanRelative =
        "docs/superpowers/plans/2026-10-11-card-1155-dispatch-warning-e2e-roster-plan.md";

    private const string BatchPlanRelative =
        "docs/superpowers/plans/2026-10-08-test-hardening-batch-1137-1134-1130-1126-1152-plan.md";

    private const string TestsRelative =
        "tests/Antiphon.E2E/DispatchBaseWarningDeliveryE2ETests.cs";

    private const string PinFilter =
        "/*/*/DispatchBaseWarningRosterTests/*";

    private const string CostCitation =
        "CARD-0550 CP-24 budgets one DispatchBaseWarningDeliveryE2ETests method at 4 minutes";

    private const string OneMethodBudget = "the CP-24 one-method budget";

    [Test]
    public void C1155_RosterNamesEveryDispatchWarningE2EMethod()
    {
        var root = DelegateScriptRunner.RepoRoot;
        var planPath = Path.Combine(root, PlanRelative.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(planPath).ShouldBeTrue(planPath);
        var plan = File.ReadAllText(planPath).Replace("\r\n", "\n");
        var collapsed = Regex.Replace(plan.Replace("`", ""), @"\s+", " ");
        collapsed.ShouldContain("complete UserPrompt transcript from the real producer to the recipient");
        collapsed.ShouldContain("Queue insert, Sent, or ack alone does not discharge");
        collapsed.ShouldContain("LandDeliveryFixture.InitializeAsync requires Windows");
        collapsed.ShouldContain("CP-1 does not discharge");
        var cost = Collapse(HeadingSection(plan, "Cost basis", out var costStart, out var costEnd));
        cost.ShouldContain(CostCitation, Case.Sensitive);
        cost.ShouldContain(OneMethodBudget, Case.Sensitive);
        var outsideCost = Collapse(plan.Remove(costStart, costEnd - costStart));
        outsideCost.Contains(CostCitation, StringComparison.Ordinal).ShouldBeFalse(CostCitation);
        outsideCost.Contains(OneMethodBudget, StringComparison.Ordinal).ShouldBeFalse(OneMethodBudget);

        var batch = File.ReadAllText(Path.Combine(root, BatchPlanRelative.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n");
        Section(batch, "Checkpoints").ShouldNotContain("DispatchBaseWarningDeliveryE2ETests");

        var source = File.ReadAllText(Path.Combine(root, TestsRelative.Replace('/', Path.DirectorySeparatorChar)));
        var methods = TestMethods(source);
        methods.Count.ShouldBeGreaterThan(0);

        var rows = CheckpointRows(Section(plan, "Checkpoints"));
        var pin = rows.Single(row => row.Filter == PinFilter);
        pin.Build.ShouldContain("tests/Antiphon.Tests -> ");
        pin.Covers.ShouldContain("V-1");
        pin.Min.ShouldBe("11");
        int.TryParse(pin.EstimatedMinutes, out var pinMinutes).ShouldBeTrue(pin.Id);
        pinMinutes.ShouldBeGreaterThan(0);

        var byMethod = new Dictionary<string, Row>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var match = Regex.Match(
                row.Filter,
                @"^/\*/\*/DispatchBaseWarningDeliveryE2ETests/([A-Za-z_][A-Za-z0-9_]*)\*$",
                RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;
            byMethod.ContainsKey(match.Groups[1].Value).ShouldBeFalse(match.Groups[1].Value);
            byMethod.Add(match.Groups[1].Value, row);
        }

        SortedNames(byMethod.Keys).ShouldBe(SortedNames(methods));

        var builds = byMethod.Values.Where(row => row.Build.StartsWith("tests/Antiphon.E2E -> ", StringComparison.Ordinal)).ToArray();
        builds.Length.ShouldBe(1);
        var build = builds[0];
        foreach (var row in byMethod.Values)
        {
            row.Min.ShouldBe("1");
            row.Covers.ShouldContain("V-2");
            int.TryParse(row.EstimatedMinutes, out var minutes).ShouldBeTrue(row.Id);
            minutes.ShouldBeGreaterThan(0);
            if (row.Id == build.Id)
                continue;
            Regex.IsMatch(row.Build, @"^CP-\d+$").ShouldBeTrue(row.Id + " " + row.Build);
            rows.Single(prior => prior.Id == row.Build).Build.ShouldBe(build.Build);
        }
    }

    // The fixtures below declare `public partial class` so the line scanner in
    // TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration does not read
    // them as untagged test classes; TestMethods ignores class modifiers.
    [Test]
    public void C1155_ExtraAttributeAfterTestIsDiscovered()
    {
        const string source = """
            public partial class DispatchBaseWarningDeliveryE2ETests
            {
                [Test]
                [Category("E2E")]
                public async Task WithFollowingAttribute() { }

                [Test, Timeout(1)]
                public async Task WithCombinedAttribute() { }

                [Test][Category("E2E")]
                public async Task WithStackedAttribute() { }

                [Arguments(1)]
                [Test]
                public async Task WithEarlierAttribute() { }

                [TestAttribute]
                public async Task WithSpelledAttribute() { }

                [Latest]
                public async Task NamedLatest() { }
            }
            """;

        TestMethods(source).ShouldBe(new[]
        {
            "WithFollowingAttribute",
            "WithCombinedAttribute",
            "WithStackedAttribute",
            "WithEarlierAttribute",
            "WithSpelledAttribute",
        });
    }

    [Test]
    public void C1155_CommentedOutDeclarationIsNotCounted()
    {
        const string source = """
            public partial class DispatchBaseWarningDeliveryE2ETests
            {
                // [Test] public async Task CommentedOut() { }
                // [Test]
                public async Task AttributeOnlyCommented() { }
                public void HoldStrings()
                {
                    const string quoted = "[Test] public async Task Quoted() { }";
                    _ = @"[Test] public async Task Verbatim() { }";
                }
                [Test]
                public async Task Live() { }
            }
            """;

        TestMethods(source).ShouldBe(new[] { "Live" });
    }

    [Test]
    public void C1155_BlockCommentMethodIsNotCounted()
    {
        const string source = """
            public partial class DispatchBaseWarningDeliveryE2ETests
            {
                /*
                [Test]
                public async Task Hidden() { }
                */
                [Test]
                public async Task Live() { }
                /* [Test] public async Task HiddenInline() { } */
            }
            """;

        TestMethods(source).ShouldBe(new[] { "Live" });
    }

    [Test]
    public void C1155_RemovedAndRenamedMethodDiffersFromTheRoster()
    {
        const string source = """
            public partial class DispatchBaseWarningDeliveryE2ETests
            {
                [Test]
                [Category("E2E")]
                public async Task Renamed() { }

                [Test, Timeout(1)]
                public async Task Kept() { }

                // [Test] public async Task Removed() { }
            }
            """;

        var discovered = TestMethods(source);
        discovered.ShouldBe(new[] { "Renamed", "Kept" });
        var removedAndRenamed = new[] { "Kept", "OldName", "Removed" };
        SortedNames(discovered).SequenceEqual(SortedNames(removedAndRenamed)).ShouldBeFalse();
        SortedNames(discovered).ShouldBe(SortedNames(new[] { "Kept", "Renamed" }));
    }

    [Test]
    public void C1155_VerbatimEscapedQuoteDoesNotHideTheNextTest()
    {
        const string source = """"
            public partial class DispatchBaseWarningDeliveryE2ETests
            {
                const string escaped = @"""[Test] public async Task HiddenByVerbatim() { }";

                [Test]
                public async Task AfterVerbatimEscapedQuote() { }
            }
            """";

        TestMethods(source).ShouldBe(new[] { "AfterVerbatimEscapedQuote" });
    }

    [Test]
    public void C1155_CharLiteralQuoteDoesNotHideTheNextTest()
    {
        const string source = """
            public partial class DispatchBaseWarningDeliveryE2ETests
            {
                const char mark = '"';

                [Test]
                public async Task AfterCharLiteral() { }
            }
            """;

        TestMethods(source).ShouldBe(new[] { "AfterCharLiteral" });
    }

    [Test]
    public void C1155_RawStringDoesNotHideTheNextTest()
    {
        const string source = """"
            public partial class DispatchBaseWarningDeliveryE2ETests
            {
                const string hidden = """
                    [Test]
                    public async Task HiddenInRaw() { }
                    """;

                [Test]
                public async Task AfterRawString() { }
            }
            """";

        TestMethods(source).ShouldBe(new[] { "AfterRawString" });
    }

    [Test]
    public void C1155_InterpolatedStringDoesNotHideTheNextTest()
    {
        const string source = """
            public partial class DispatchBaseWarningDeliveryE2ETests
            {
                static readonly string Hidden = $"pre { '"' } [Test] public async Task HiddenInInterpolation() {{ }}";

                [Test]
                public async Task AfterInterpolated() { }
            }
            """;

        TestMethods(source).ShouldBe(new[] { "AfterInterpolated" });
    }

    [Test]
    public void C1155_ConditionalDirectiveFailsClosed()
    {
        const string source = """
            public partial class DispatchBaseWarningDeliveryE2ETests
            {
                #if NET9_0
                [Test]
                public async Task HiddenByDirective() { }
                #endif
            }
            """;

        var failure = Should.Throw<InvalidOperationException>(() => TestMethods(source));
        failure.Message.ShouldBe("Conditional compilation directive on line 3.");
    }

    [Test]
    public void C1155_SourceWithoutConditionalDirectiveIsDiscovered()
    {
        const string source = """
            public partial class DispatchBaseWarningDeliveryE2ETests
            {
                [Test]
                public async Task Visible() { }
            }
            """;

        TestMethods(source).ShouldBe(new[] { "Visible" });
    }

    private static string[] SortedNames(IEnumerable<string> names) =>
        names.OrderBy(name => name, StringComparer.Ordinal).ToArray();

    private static List<string> TestMethods(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var errors = tree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        errors.ShouldBeEmpty();
        RejectConditionalDirectives(tree);

        var methods = new List<string>();
        foreach (var type in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (type.Identifier.ValueText != "DispatchBaseWarningDeliveryE2ETests")
                continue;
            if (type.Parent is TypeDeclarationSyntax)
                continue;
            foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
            {
                if (!method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)))
                    continue;
                if (method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)))
                    continue;
                if (!method.AttributeLists.SelectMany(list => list.Attributes).Any(IsTestAttribute))
                    continue;
                var name = method.Identifier.ValueText;
                if (!methods.Contains(name))
                    methods.Add(name);
            }
        }

        return methods;
    }

    private static void RejectConditionalDirectives(SyntaxTree tree)
    {
        var first = tree.GetRoot()
            .DescendantTrivia(descendIntoTrivia: true)
            .Select(trivia => trivia.GetStructure())
            .OfType<DirectiveTriviaSyntax>()
            .Where(directive => directive
                is IfDirectiveTriviaSyntax
                or ElifDirectiveTriviaSyntax
                or ElseDirectiveTriviaSyntax
                or EndIfDirectiveTriviaSyntax)
            .OrderBy(directive => directive.SpanStart)
            .FirstOrDefault();
        if (first is null)
            return;

        var line = tree.GetLineSpan(first.Span).StartLinePosition.Line + 1;
        throw new InvalidOperationException($"Conditional compilation directive on line {line}.");
    }

    private static bool IsTestAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax alias => alias.Name switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                GenericNameSyntax generic => generic.Identifier.ValueText,
                _ => null,
            },
            _ => null,
        };
        return name is "Test" or "TestAttribute";
    }

    private static string Collapse(string text) =>
        Regex.Replace(text.Replace("`", ""), @"\s+", " ").Trim();

    private static string HeadingSection(string markdown, string heading, out int start, out int end)
    {
        var title = "## " + heading;
        var marker = "\n" + title + "\n";
        start = markdown.StartsWith(title + "\n", StringComparison.Ordinal)
            ? 0
            : markdown.IndexOf(marker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, title);
        if (start > 0)
            start++;
        end = markdown.IndexOf("\n## ", start + title.Length, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start, title);
        var section = markdown[start..end];
        section.StartsWith(title + "\n", StringComparison.Ordinal).ShouldBeTrue(title);
        return section;
    }

    private static string Section(string markdown, string heading)
    {
        var marker = "\n### " + heading + "\n";
        var start = markdown.StartsWith("### " + heading + "\n", StringComparison.Ordinal)
            ? 0
            : markdown.IndexOf(marker, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, marker.Trim());
        if (start > 0)
            start++;
        var next = markdown.IndexOf("\n##", start + marker.Trim().Length, StringComparison.Ordinal);
        return next < 0 ? markdown[start..] : markdown[start..next];
    }

    private static List<Row> CheckpointRows(string section)
    {
        List<string>? header = null;
        var rows = new List<Row>();
        foreach (var line in section.Split('\n'))
        {
            if (!line.TrimStart().StartsWith('|'))
                continue;
            var cells = SplitRow(line);
            if (cells.All(cell => cell.Length == 0 || cell.Trim('-', ':', ' ').Length == 0))
                continue;
            if (header is null)
            {
                header = cells;
                header.Count.ShouldBeGreaterThanOrEqualTo(9);
                header[4].ShouldBe("Filter");
                header[5].ShouldBe("Covers");
                header[7].ShouldBe("Min");
                header[8].ShouldBe("EstimatedMinutes");
                continue;
            }

            cells.Count.ShouldBe(header.Count, cells.FirstOrDefault());
            rows.Add(new Row(
                cells[0],
                StripBackticks(cells[2]),
                StripBackticks(cells[4]),
                cells[5],
                cells[7],
                cells[8]));
        }

        header.ShouldNotBeNull();
        return rows;
    }

    private static List<string> SplitRow(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] == '|')
            {
                current.Append('|');
                i++;
                continue;
            }

            if (line[i] == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(line[i]);
        }

        cells.Add(current.ToString().Trim());
        if (cells.Count > 0 && cells[0].Length == 0)
            cells.RemoveAt(0);
        if (cells.Count > 0 && cells[^1].Length == 0)
            cells.RemoveAt(cells.Count - 1);
        return cells;
    }

    private static string StripBackticks(string cell)
    {
        var text = cell.Trim();
        if (text.Length >= 2 && text.StartsWith('`') && text.EndsWith('`'))
            text = text[1..^1].Trim();
        return text;
    }

    private sealed record Row(
        string Id,
        string Build,
        string Filter,
        string Covers,
        string Min,
        string EstimatedMinutes);
}
