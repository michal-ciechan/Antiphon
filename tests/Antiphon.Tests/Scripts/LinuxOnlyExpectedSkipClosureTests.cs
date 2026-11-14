using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1907. These four classes are the Windows Full run's unclassified Linux-only
/// skips. Each method that skips on Windows because it is Linux-only must be covered
/// by a windows exclusion whose reason starts with "Linux-only by construction:", or
/// by a windows row in tests/expected-platform-skips.json, and nothing else in those
/// classes may be covered. The sets are equal: no extra, none missing.
/// </summary>
[Category("Unit")]
public sealed class LinuxOnlyExpectedSkipClosureTests
{
    private const string LinuxOnlyPrefix = "Linux-only by construction:";

    private static readonly (string ClassName, string RelativePath)[] Subjects =
    [
        ("RcBridgeProbePlatformTests", "tests/Antiphon.Tests/Infrastructure/RcBridgeProbePlatformTests.cs"),
        ("ZombieCensusPlatformTests", "tests/Antiphon.Tests/Infrastructure/ZombieCensusPlatformTests.cs"),
        ("PwshPrivateCacheTests", "tests/Antiphon.Tests/Scripts/PwshPrivateCacheTests.cs"),
        ("UnixPtyDeliveryEnvelopeTests", "tests/Antiphon.Agents.Pty.Tests/UnixPtyDeliveryEnvelopeTests.cs"),
    ];

    [Test]
    public void Linux_only_source_methods_match_the_classified_windows_set()
    {
        var root = RepoRoot();
        var testsByClass = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var source = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var subject in Subjects)
        {
            var parsed = Parse(Path.Combine(root, subject.RelativePath), subject.ClassName);
            parsed.Tests.Count.ShouldBeGreaterThan(0, subject.ClassName);
            parsed.LinuxOnly.Count.ShouldBeGreaterThan(0, subject.ClassName);
            testsByClass.Add(subject.ClassName, parsed.Tests);
            foreach (var method in parsed.LinuxOnly)
                source.Add(subject.ClassName + "." + method);
        }

        var accounted = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var id in PolicyCoverage(root, testsByClass))
            accounted.Add(id);
        foreach (var id in RosterCoverage(root))
            accounted.Add(id);

        var missing = source.Except(accounted, StringComparer.Ordinal).ToArray();
        var extra = accounted.Except(source, StringComparer.Ordinal).ToArray();
        (missing.Length == 0 && extra.Length == 0).ShouldBeTrue(
            "missing=[" + string.Join(", ", missing) + "] extra=[" + string.Join(", ", extra) + "]");
    }

    private static (SortedSet<string> Tests, SortedSet<string> LinuxOnly) Parse(string path, string className)
    {
        var text = File.ReadAllText(path);
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview), path);
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray();
        errors.ShouldBeEmpty(path);
        var classDecl = tree.GetCompilationUnitRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == className);
        var methods = classDecl.Members.OfType<MethodDeclarationSyntax>().ToArray();
        var helpers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            if (HasTestAttribute(method))
                continue;
            if (SkipsOnWindowsBecauseLinuxOnly(Body(method)))
                helpers.Add(method.Identifier.Text);
        }

        var tests = new SortedSet<string>(StringComparer.Ordinal);
        var linuxOnly = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            if (!HasTestAttribute(method))
                continue;
            tests.Add(method.Identifier.Text);
            if (SkipsOnWindowsBecauseLinuxOnly(Body(method)) || Invokes(method, helpers))
                linuxOnly.Add(method.Identifier.Text);
        }

        return (tests, linuxOnly);
    }

    private static SyntaxNode? Body(MethodDeclarationSyntax method) =>
        method.Body ?? (SyntaxNode?)method.ExpressionBody;

    private static bool HasTestAttribute(MethodDeclarationSyntax method) =>
        method.AttributeLists.SelectMany(list => list.Attributes).Any(IsTestAttribute);

    private static bool IsTestAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            _ => attribute.Name.ToString(),
        };
        return name is "Test" or "TestAttribute";
    }

    private static bool SkipsOnWindowsBecauseLinuxOnly(SyntaxNode? body)
    {
        if (body is null)
            return false;
        foreach (var statement in body.DescendantNodes().OfType<IfStatementSyntax>())
        {
            if (IsPositiveCall(statement.Condition, "IsWindows") && ThenSkips(statement.Statement))
                return true;
            if (IsNegatedCall(statement.Condition, "IsLinux") && ThenSkips(statement.Statement))
                return true;
        }

        return false;
    }

    private static bool Invokes(MethodDeclarationSyntax method, HashSet<string> helpers)
    {
        var body = Body(method);
        if (body is null || helpers.Count == 0)
            return false;
        return body.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(invocation =>
        {
            var name = invocation.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                _ => null,
            };
            return name is not null && helpers.Contains(name);
        });
    }

    private static bool IsPositiveCall(ExpressionSyntax expression, string method)
    {
        expression = Unwrap(expression);
        return expression is not PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression }
            && IsOperatingSystemCall(expression, method);
    }

    private static bool IsNegatedCall(ExpressionSyntax expression, string method)
    {
        expression = Unwrap(expression);
        return expression is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } not
            && IsOperatingSystemCall(Unwrap(not.Operand), method);
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        return expression;
    }

    private static bool IsOperatingSystemCall(ExpressionSyntax expression, string method) =>
        expression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.Text: "OperatingSystem" },
                Name.Identifier.Text: var called,
            },
        } && called == method;

    private static bool ThenSkips(StatementSyntax statement)
    {
        foreach (var node in statement.DescendantNodesAndSelf())
        {
            if (node is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax
                {
                    Name.Identifier.Text: "Test",
                    Expression: IdentifierNameSyntax { Identifier.Text: "Skip" },
                })
                return true;
            if (node is ObjectCreationExpressionSyntax created && IsSkipException(created.Type))
                return true;
        }

        return false;
    }

    private static bool IsSkipException(TypeSyntax type)
    {
        var text = type.ToString();
        return text == "SkipTestException" || text.EndsWith(".SkipTestException", StringComparison.Ordinal);
    }

    private static IEnumerable<string> PolicyCoverage(
        string root, IReadOnlyDictionary<string, SortedSet<string>> testsByClass)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "test-execution-policy.json")));
        foreach (var row in doc.RootElement.GetProperty("profiles").GetProperty("rc").GetProperty("exclusions").EnumerateArray())
        {
            if (!row.TryGetProperty("platform", out var platform) || platform.GetString() != "windows")
                continue;
            if (!row.TryGetProperty("reason", out var reason)
                || reason.GetString() is not { } text
                || !text.StartsWith(LinuxOnlyPrefix, StringComparison.Ordinal))
                continue;
            if (!row.TryGetProperty("class", out var className) || className.GetString() is not { } cls)
                continue;
            if (!testsByClass.TryGetValue(cls, out var tests))
                continue;
            if (row.TryGetProperty("methods", out var methods)
                && methods.ValueKind == JsonValueKind.Array
                && methods.GetArrayLength() > 0)
            {
                foreach (var method in methods.EnumerateArray())
                {
                    var name = method.GetString();
                    if (!string.IsNullOrEmpty(name))
                        yield return cls + "." + name;
                }
            }
            else
            {
                foreach (var name in tests)
                    yield return cls + "." + name;
            }
        }
    }

    private static IEnumerable<string> RosterCoverage(string root)
    {
        var classes = new HashSet<string>(Subjects.Select(subject => subject.ClassName), StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "expected-platform-skips.json")));
        foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            if (!entry.TryGetProperty("platform", out var platform) || platform.GetString() != "windows")
                continue;
            var test = entry.GetProperty("test").GetString();
            if (string.IsNullOrEmpty(test))
                continue;
            var dot = test.LastIndexOf('.');
            if (dot <= 0 || dot >= test.Length - 1)
                continue;
            var method = test[(dot + 1)..];
            var head = test[..dot];
            var classDot = head.LastIndexOf('.');
            var cls = classDot < 0 ? head : head[(classDot + 1)..];
            if (classes.Contains(cls))
                yield return cls + "." + method;
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }
}
