using Antiphon.Tests.Application;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class ProcessSpawnTimeoutPolicyTests
{
    private const int CeilingMilliseconds = 600_000;

    private static (string Path, string Class)[] Portfolio =>
    [
        ("Scripts/HostJqPrerequisiteScriptTests.cs", "HostJqPrerequisiteScriptTests"),
        ("Infrastructure/JqRunnerImageContractTests.cs", "JqRunnerImageContractTests"),
        ("Infrastructure/GrokSha256PinTests.cs", "GrokSha256PinTests"),
        ("Infrastructure/GitMinimumVersionGuardTests.cs", "GitMinimumVersionGuardTests"),
        ("Infrastructure/CodexRootExecContractTests.cs", "CodexRootExecContractTests"),
        ("Infrastructure/CodexRunnerImageContractTests.cs", "CodexRunnerImageContractTests"),
        ("Scripts/RollingVolumeRecycleScriptTests.cs", "RollingVolumeRecycleScriptTests"),
        ("Scripts/C1008PlatformContractTests.cs", "C1008PlatformContractTests"),
        ("Scripts/WindowsUnitPlatformAdmissionTests.cs", "WindowsUnitPlatformAdmissionTests"),
        ("Infrastructure/ShellFixtureLineEndingContractTests.cs", "ShellFixtureLineEndingContractTests"),
        ("Application/ChannelOutboundStorageTests.cs", "ChannelOutboundStorageTests"),
        ("Scripts/RemoteScriptContractTests.cs", "RemoteScriptContractTests")
    ];

    [Test]
    public void Spawning_classes_carry_a_class_timeout()
    {
        var vectors = new[]
        {
            ("absent", "", "", false),
            ("method-only", "", "[Timeout(100)]", false),
            ("class", "[Timeout(600_000)]", "", true),
            ("shorter-override", "[Timeout(600_000)]", "[Timeout(100)]", true),
            ("duplicate", "[Timeout(100), Timeout(200)]", "", false),
            ("qualified", "[TUnit.Core.TimeoutAttribute(600_000)]", "", true)
        };
        foreach (var (name, classAttributes, methodAttributes, expected) in vectors)
        {
            var (declaration, model) = Probe(classAttributes, methodAttributes);
            (Timeouts(declaration.AttributeLists, model).Length == 1)
                .ShouldBe(expected, "c1210-timeout-presence-vector: " + name);
        }

        var sources = ReadPortfolio();
        foreach (var (declaration, model) in sources)
        {
            var attributes = declaration.AttributeLists.SelectMany(list => list.Attributes)
                .Concat(declaration.Members.OfType<MethodDeclarationSyntax>()
                    .SelectMany(method => method.AttributeLists.SelectMany(list => list.Attributes)));
            attributes.Any(attribute => attribute.Name.ToString().Contains("ParallelLimiter", StringComparison.Ordinal)
                    && attribute.Name.ToString().Contains("ProcessSpawnLimit", StringComparison.Ordinal))
                .ShouldBeTrue("c1210-spawning-portfolio: " + declaration.Identifier.ValueText);
            Timeouts(declaration.AttributeLists, model).Length.ShouldBe(1,
                "c1210-timeout-present: " + declaration.Identifier.ValueText);
        }
    }

    [Test]
    public void Spawn_timeouts_do_not_exceed_ten_minutes()
    {
        var vectors = new[]
        {
            ("absent", "", "", "c1210-timeout-present"),
            ("method-only", "", "[Timeout(100)]", "c1210-timeout-present"),
            ("ceiling", "[Timeout(600_000)]", "", ""),
            ("constant-expression", "[Timeout(10 * Minute)]", "", ""),
            ("shorter-override", "[Timeout(600_000)]", "[Timeout(2_000)]", ""),
            ("zero", "[Timeout(0)]", "", "c1210-timeout-ceiling"),
            ("negative", "[Timeout(-1)]", "", "c1210-timeout-ceiling"),
            ("above-class-ceiling", "[Timeout(1_200_000)]", "", "c1210-timeout-ceiling"),
            ("above-method-ceiling", "[Timeout(600_000)]", "[Timeout(1_200_000)]", "c1210-timeout-ceiling"),
            ("unresolved-class", "[Timeout(Unknown)]", "", "c1210-timeout-unresolved"),
            ("unresolved-method", "[Timeout(600_000)]", "[Timeout(Unknown)]", "c1210-timeout-unresolved"),
            ("duplicate-class", "[Timeout(100), Timeout(200)]", "", "c1210-timeout-present"),
            ("duplicate-method", "[Timeout(600_000)]", "[Timeout(100), Timeout(200)]", "c1210-timeout-present")
        };
        foreach (var (name, classAttributes, methodAttributes, expected) in vectors)
        {
            var (declaration, model) = Probe(classAttributes, methodAttributes);
            Validate(declaration, model).ShouldBe(expected, "c1210-timeout-value-vector: " + name);
        }

        foreach (var (declaration, model) in ReadPortfolio())
            Validate(declaration, model).ShouldBe("", "c1210-timeout-ceiling: " + declaration.Identifier.ValueText);
    }

    private static string Validate(ClassDeclarationSyntax declaration, SemanticModel model)
    {
        var defaults = Timeouts(declaration.AttributeLists, model);
        if (defaults.Length != 1) return "c1210-timeout-present";
        var result = ValidateValue(defaults[0], model);
        if (result.Length != 0) return result;
        foreach (var method in declaration.Members.OfType<MethodDeclarationSyntax>())
        {
            var overrides = Timeouts(method.AttributeLists, model);
            if (overrides.Length > 1) return "c1210-timeout-present";
            if (overrides.Length == 0) continue;
            result = ValidateValue(overrides[0], model);
            if (result.Length != 0) return result;
        }
        return "";
    }

    private static string ValidateValue(AttributeSyntax attribute, SemanticModel model)
    {
        if (model.GetSymbolInfo(attribute).Symbol is not IMethodSymbol constructor
            || constructor.ContainingType.ToDisplayString() != "TUnit.Core.TimeoutAttribute"
            || attribute.ArgumentList?.Arguments.Count != 1)
            return "c1210-timeout-unresolved";
        var constant = model.GetConstantValue(attribute.ArgumentList.Arguments[0].Expression);
        if (!constant.HasValue || constant.Value is not int value) return "c1210-timeout-unresolved";
        return value is > 0 and <= CeilingMilliseconds ? "" : "c1210-timeout-ceiling";
    }

    private static AttributeSyntax[] Timeouts(SyntaxList<AttributeListSyntax> lists, SemanticModel model) =>
        lists.SelectMany(list => list.Attributes).Where(attribute =>
            (model.GetSymbolInfo(attribute).Symbol as IMethodSymbol)?.ContainingType.ToDisplayString()
                == "TUnit.Core.TimeoutAttribute"
            || attribute.Name.ToString().Split('.').Last() is "Timeout" or "TimeoutAttribute").ToArray();

    private static (ClassDeclarationSyntax Declaration, SemanticModel Model) Probe(string classAttributes, string methodAttributes)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using TUnit.Core;
            {{classAttributes}}
            public class Probe
            {
                private const int Minute = 60_000;
                {{methodAttributes}}
                public void Test() { }
            }
            """);
        var compilation = Compile([tree]);
        return (tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single(), compilation.GetSemanticModel(tree));
    }

    private static (ClassDeclarationSyntax Declaration, SemanticModel Model)[] ReadPortfolio()
    {
        var portfolio = Portfolio;
        portfolio.Length.ShouldBe(12, "c1210-timeout-scope");
        var trees = portfolio.Select(entry => CSharpSyntaxTree.ParseText(File.ReadAllText(
            Path.Combine(DelegateScriptRunner.RepoRoot, "tests/Antiphon.Tests", entry.Path)))).ToArray();
        var compilation = Compile(trees);
        return trees.Select((tree, index) =>
        {
            var declarations = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(declaration => declaration.Identifier.ValueText == portfolio[index].Class).ToArray();
            declarations.Length.ShouldBe(1, "c1210-timeout-class: " + portfolio[index].Class);
            return (declarations[0], compilation.GetSemanticModel(tree));
        }).ToArray();
    }

    private static CSharpCompilation Compile(SyntaxTree[] trees)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(TimeoutAttribute).Assembly.Location).Distinct(StringComparer.Ordinal);
        return CSharpCompilation.Create("TimeoutPolicy", trees,
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
