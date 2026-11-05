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
    [Arguments("var options = new PageScreenshotOptions { Path = output }; page.ScreenshotAsync(options)")]
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
                var scratch = Path.Combine(FindRepoRoot(), ".antiphon", "acceptance");
                File.WriteAllText(Path.Combine(scratch, "docs", "x"), text);
                File.WriteAllText(output, File.ReadAllText(input));
            } void Other() {
                var input = Path.Combine(PlaywrightFixture.ScreenshotRoot, "other.png");
                page.ScreenshotAsync(new PageScreenshotOptions { Path = input });
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
        var paths = new Dictionary<(int Scope, string Name), PathKind>();
        (int Scope, string Name) Key(SyntaxNode node, string name) =>
            (node.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault()?.SpanStart ?? -1, name);
        PathKind Kind(SyntaxNode node)
        {
            if (node is IdentifierNameSyntax identifier)
                return paths.GetValueOrDefault(Key(node, identifier.Identifier.ValueText),
                    paths.GetValueOrDefault((-1, identifier.Identifier.ValueText)));
            if (node is InvocationExpressionSyntax call)
            {
                var name = call.Expression.ToString().Split('.').Last();
                if (name is "FindRepoRoot" or "FindRepositoryRoot" or "RepositoryRoot")
                    return PathKind.Repository;
                if (name is "Combine" or "Join")
                {
                    var args = call.ArgumentList.Arguments;
                    if (args.Count > 1 && Kind(args[0].Expression) == PathKind.Repository)
                    {
                        var firstSegment = args.Skip(1).Select(a => a.Expression)
                            .OfType<LiteralExpressionSyntax>().SelectMany(n => n.Token.ValueText
                                .Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
                            .FirstOrDefault();
                        // A child such as .antiphon/acceptance is not the repository root;
                        // its own scratch docs tree must not be mistaken for checkout docs.
                        if (firstSegment is not null)
                            return firstSegment.Equals("docs", StringComparison.OrdinalIgnoreCase)
                                ? PathKind.Docs : PathKind.Other;
                    }
                }
            }
            var children = node.ChildNodes().Select(Kind).ToArray();
            if (children.Contains(PathKind.Docs)) return PathKind.Docs;
            return children.Contains(PathKind.Repository) ? PathKind.Repository : PathKind.Other;
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var variable in syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (variable.Initializer is null) continue;
                var kind = Kind(variable.Initializer.Value);
                var key = Key(variable, variable.Identifier.ValueText);
                if ((int)kind <= (int)paths.GetValueOrDefault(key)) continue;
                paths[key] = kind;
                changed = true;
            }
            foreach (var assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is not IdentifierNameSyntax name) continue;
                var kind = Kind(assignment.Right);
                var key = Key(name, name.Identifier.ValueText);
                if ((int)kind <= (int)paths.GetValueOrDefault(key)) continue;
                paths[key] = kind;
                changed = true;
            }
        } while (changed);
        return syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(n => n.Expression is MemberAccessExpressionSyntax member &&
                (member.Name.Identifier.ValueText is "ScreenshotAsync" or "CreateDirectory" ||
                 member.Name.Identifier.ValueText.StartsWith("WriteAll", StringComparison.Ordinal)))
            .Where(n => n.ArgumentList.Arguments.Count > 0 &&
                Kind(n.ArgumentList.Arguments[0].Expression) == PathKind.Docs)
            .Select(n => tree.GetLineSpan(n.Span).StartLinePosition.Line + 1).ToArray();
    }

    private enum PathKind { Other, Repository, Docs }
}
