using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class WorkspaceAdmissionPolicyGuardTests
{
    [Test]
    public void OnlyHttpHandlersSelectInteractiveAdmission()
    {
        const string policy = "WorkspaceAdmissionWaitPolicy";
        string[] entryPoints = ["TryAdmitConsumerAsync", "TryClaimRetirementAsync", "RequireConsumerAsync",
            "FenceCompletedAsync", "MeasureAdmissionLock", "AdmitWorkspaceAsync", "AcquireAsync"];
        var server = Path.Combine(DockerStackDocuments.RepoRoot, "server");
        var sources = Directory.EnumerateFiles(server, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: Path.GetRelativePath(server, path).Replace('\\', '/'), FullPath: path))
            .Where(file => !file.Path.Split('/').Any(part => part == "obj" || part == "bin"
                || part.StartsWith("bin-", StringComparison.Ordinal)))
            .Select(file => (file.Path, Source: File.ReadAllText(file.FullPath)))
            .Where(file => file.Source.Contains(policy, StringComparison.Ordinal)
                || entryPoints.Any(name => file.Source.Contains(name, StringComparison.Ordinal)))
            .Select(file => (file.Path, Root: CSharpSyntaxTree.ParseText(file.Source).GetRoot()))
            .ToArray();

        var calls = sources.SelectMany(file => file.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax member && entryPoints.Contains(member.Name.Identifier.ValueText)
                || call.Expression is IdentifierNameSyntax name && entryPoints.Contains(name.Identifier.ValueText))
            .Select(call => (file.Path, Call: call))).ToArray();
        calls.Length.ShouldBeGreaterThan(15, "the scan must cover journal, service and cleanup call sites");

        const string journal = "Infrastructure/Data/WorkspaceReservationJournal.cs";
        var interactiveSelections = sources.SelectMany(file => file.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(member => member.Expression.ToString().Split('.').Last() == policy
                && member.Name.Identifier.ValueText == "Interactive"
                && !(file.Path == journal && member.Parent is BinaryExpressionSyntax comparison
                    && comparison.RawKind == (int)SyntaxKind.EqualsExpression))
            .Select(member => file.Path)).ToArray();
        interactiveSelections.ShouldNotBeEmpty("HTTP handlers must explicitly opt in to bounded admission");
        interactiveSelections.Where(path => !path.StartsWith("Api/Endpoints/", StringComparison.Ordinal))
            .ShouldBeEmpty("a background caller must never select the interactive policy");

        var policyImports = sources.SelectMany(file => file.Root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(directive => (directive.Alias is not null || directive.StaticKeyword.RawKind != 0)
                && directive.Name?.ToString().EndsWith(policy, StringComparison.Ordinal) == true)
            .Select(_ => file.Path));
        policyImports.ShouldBeEmpty("make admission policy selections explicit to the source census");

        var defaultedEntryPoints = sources.Where(file => file.Path == journal
                || file.Path == "Application/Interfaces/IWorkspaceReservationJournal.cs"
                || file.Path == "Application/Services/WorkspaceUseAdmission.cs")
            .SelectMany(file => file.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(method => entryPoints.Contains(method.Identifier.ValueText))
            .SelectMany(method => method.ParameterList.Parameters)
            .Where(parameter => parameter.Type?.ToString() == policy)
            .ToArray();
        defaultedEntryPoints.Length.ShouldBe(5);
        defaultedEntryPoints.ShouldAllBe(parameter => parameter.Default != null
            && parameter.Default.Value.ToString() == policy + ".Background");
    }
}
