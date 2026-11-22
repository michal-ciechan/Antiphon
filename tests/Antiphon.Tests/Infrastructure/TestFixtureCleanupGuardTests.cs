using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class TestFixtureCleanupGuardTests
{
    private static readonly Regex GitFixture = new(
        """
        \b(?:git(?:\.exe)?\s+(?:-C\b[^\r\n]*?\s+)?(?:init|clone)\b|Repository\s*\.\s*(?:Init|Initialize|Clone)\s*\(|
        (?:\w*Git\w*|Run|Execute)\s*\([^;{}]{0,200}"(?:init|clone|worktree)(?:"|\s)|
        (?:GitFixture|ScratchGitRepo|GitTreeTemplate|DelegationScratchRepo|LandDeliveryFixture|CompletedCardRunnerFixture|LandingGitFixture)\b)
        """, RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Dictionary<(string Path, string Target), string> Allowed = new();

    [Test]
    public void Git_fixtures_use_read_only_safe_directory_cleanup()
    {
        var root = DockerStackDocuments.RepoRoot;
        var files = TestSources(Path.Combine(root, "tests")).Order(StringComparer.Ordinal).ToArray();
        files.ShouldNotBeEmpty();

        var violations = new List<string>();
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            if (!source.Contains("Delete", StringComparison.Ordinal) || !GitFixture.IsMatch(source))
                continue;

            var path = Path.GetRelativePath(root, file).Replace('\\', '/');
            var syntax = CSharpSyntaxTree.ParseText(source);
            foreach (var call in syntax.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var target = RawRecursiveDeleteTarget(call);
                if (target is not null && !Allowed.ContainsKey((path, target)))
                {
                    var line = syntax.GetLineSpan(call.Span).StartLinePosition.Line + 1;
                    violations.Add($"{path}:{line} ({target})");
                }
            }
        }

        violations.ShouldBeEmpty("Use ReadOnlyDirectory.Delete for Git fixture trees.\n" + string.Join('\n', violations));
    }

    private static string? RawRecursiveDeleteTarget(InvocationExpressionSyntax call)
    {
        if (call.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Delete" } member)
            return null;

        var args = call.ArgumentList.Arguments;
        var receiver = member.Expression.ToString();
        if (receiver == "Directory" || receiver.EndsWith(".Directory", StringComparison.Ordinal))
        {
            if (args.Count != 2)
                return null;
            var recursive = args.SingleOrDefault(arg => arg.NameColon?.Name.Identifier.ValueText == "recursive") ?? args[1];
            var path = args[0] == recursive ? args[1] : args[0];
            return recursive.Expression.IsKind(SyntaxKind.TrueLiteralExpression) ? path.Expression.ToString() : null;
        }

        // DirectoryInfo.Delete has a single recursive argument.
        return args.Count == 1 && args[0].Expression.IsKind(SyntaxKind.TrueLiteralExpression)
            ? receiver : null;
    }

    private static IEnumerable<string> TestSources(string directory)
    {
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", options))
            yield return file;

        foreach (var child in Directory.EnumerateDirectories(directory, "*", options))
        {
            var name = Path.GetFileName(child);
            if (name is "bin" or "obj" || name.StartsWith("bin-", StringComparison.Ordinal) || name.StartsWith('.'))
                continue;
            foreach (var file in TestSources(child))
                yield return file;
        }
    }
}
