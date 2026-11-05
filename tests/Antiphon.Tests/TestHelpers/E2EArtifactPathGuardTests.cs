using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class E2EArtifactPathGuardTests
{
    [Test]
    public void E2E_writers_do_not_target_repository_docs()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Antiphon.sln")))
            root = root.Parent;
        root.ShouldNotBeNull();
        var directory = Path.Combine(root!.FullName, "tests", "Antiphon.E2E");
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(directory, p).Split(Path.DirectorySeparatorChar)
                .Any(s => s is "bin" or "obj" || s.StartsWith("bin-", StringComparison.Ordinal)))
            .ToArray();
        files.Length.ShouldBeGreaterThan(0);
        var violations = files.SelectMany(path => FindDocsWrites(File.ReadAllText(path))
            .Select(line => $"{Path.GetRelativePath(root.FullName, path)}:{line}"));
        violations.ShouldBeEmpty("E2E output belongs in ignored TestOutput, not repository docs");
    }

    [Test]
    [Arguments("page.ScreenshotAsync(new PageScreenshotOptions { Path = output })")]
    [Arguments("File.WriteAllTextAsync(output, text)")]
    [Arguments("Directory.CreateDirectory(Path.GetDirectoryName(output))")]
    public void Scanner_rejects_docs_paths_reaching_writers(string writer)
    {
        var source = "class C { void M() { var repo = FindRepoRoot(); "
            + "var dir = Path.Combine(repo, \"docs\", \"screenshots\"); "
            + "var output = Path.Combine(dir, \"image.png\"); " + writer + "; } }";
        FindDocsWrites(source).Count.ShouldBe(1);
    }

    [Test]
    public void Scanner_distinguishes_reads_comments_and_artifacts_from_docs_writes()
    {
        const string source = """
            class C { void M() {
                // File.WriteAllText(Path.Combine(FindRepoRoot(), "docs", "x"), text);
                var input = Path.Combine(FindRepoRoot(), "docs", "x");
                File.ReadAllText(input);
                var output = Path.Combine(PlaywrightFixture.ScreenshotRoot, "x.png");
                page.ScreenshotAsync(new PageScreenshotOptions { Path = output });
                File.WriteAllText(Path.Combine(tempRoot, "docs", "x"), text);
            } }
            """;
        FindDocsWrites(source).ShouldBeEmpty();
    }

    // Syntax nodes exclude comments/string examples. Follow local/field aliases and wrappers
    // to sinks, so splitting Path.Combine across lines or variables cannot hide a docs write.
    private static IReadOnlyList<int> FindDocsWrites(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var syntax = tree.GetRoot();
        var paths = new Dictionary<string, PathKind>(StringComparer.Ordinal);
        PathKind Kind(SyntaxNode node)
        {
            var nodes = node.DescendantNodesAndSelf().ToArray();
            var aliases = nodes.OfType<IdentifierNameSyntax>()
                .Select(n => paths.GetValueOrDefault(n.Identifier.ValueText)).ToArray();
            if (aliases.Contains(PathKind.Docs)) return PathKind.Docs;
            var repo = aliases.Contains(PathKind.Repository) || nodes.OfType<InvocationExpressionSyntax>()
                .Any(n => n.Expression.ToString().Split('.').Last() is
                    "FindRepoRoot" or "FindRepositoryRoot" or "RepositoryRoot");
            if (!repo) return PathKind.Other;
            return nodes.OfType<LiteralExpressionSyntax>().Any(n => n.Token.ValueText
                .Replace('\\', '/').Split('/').Contains("docs", StringComparer.OrdinalIgnoreCase))
                ? PathKind.Docs : PathKind.Repository;
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var variable in syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (variable.Initializer is null) continue;
                var kind = Kind(variable.Initializer.Value);
                if ((int)kind <= (int)paths.GetValueOrDefault(variable.Identifier.ValueText)) continue;
                paths[variable.Identifier.ValueText] = kind;
                changed = true;
            }
            foreach (var assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is not IdentifierNameSyntax name) continue;
                var kind = Kind(assignment.Right);
                if ((int)kind <= (int)paths.GetValueOrDefault(name.Identifier.ValueText)) continue;
                paths[name.Identifier.ValueText] = kind;
                changed = true;
            }
        } while (changed);
        return syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(n => n.Expression is MemberAccessExpressionSyntax member &&
                (member.Name.Identifier.ValueText is "ScreenshotAsync" or "CreateDirectory" ||
                 member.Name.Identifier.ValueText.StartsWith("WriteAll", StringComparison.Ordinal)))
            .Where(n => Kind(n.ArgumentList) == PathKind.Docs)
            .Select(n => tree.GetLineSpan(n.Span).StartLinePosition.Line + 1).ToArray();
    }

    private enum PathKind { Other, Repository, Docs }
}
