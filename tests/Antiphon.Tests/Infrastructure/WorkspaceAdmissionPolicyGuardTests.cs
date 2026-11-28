using Antiphon.Tests.TestHelpers;
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
        var server = Path.Combine(DockerStackDocuments.RepoRoot, "server");
        var sources = Directory.EnumerateFiles(server, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: Path.GetRelativePath(server, path).Replace('\\', '/'), FullPath: path))
            .Where(file => !file.Path.Split('/').Any(part => part == "obj" || part == "bin"
                || part.StartsWith("bin-", StringComparison.Ordinal)))
            .Select(file => (file.Path, Root: CSharpSyntaxTree.ParseText(File.ReadAllText(file.FullPath)).GetRoot()))
            .ToArray();

        string[] entryPoints = ["TryAdmitConsumerAsync", "TryClaimRetirementAsync", "RequireConsumerAsync",
            "FenceCompletedAsync", "MeasureAdmissionLock", "AdmitWorkspaceAsync"];
        var calls = sources.SelectMany(file => file.Root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax member && entryPoints.Contains(member.Name.Identifier.ValueText)
                || call.Expression is IdentifierNameSyntax name && entryPoints.Contains(name.Identifier.ValueText))
            .Select(call => (file.Path, Call: call))).ToArray();
        calls.Length.ShouldBeGreaterThan(15, "the scan must cover journal, service and cleanup call sites");

        var interactiveSelections = sources.SelectMany(file => file.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(member => member.Expression.ToString() == "WorkspaceAdmissionWaitPolicy"
                && member.Name.Identifier.ValueText == "Interactive"
                && member.Parent is not BinaryExpressionSyntax)
            .Select(member => file.Path)).ToArray();
        interactiveSelections.ShouldNotBeEmpty("HTTP handlers must explicitly opt in to bounded admission");
        interactiveSelections.Where(path => !path.StartsWith("Api/Endpoints/", StringComparison.Ordinal))
            .ShouldBeEmpty("a background caller must never select the interactive policy");
    }
}
