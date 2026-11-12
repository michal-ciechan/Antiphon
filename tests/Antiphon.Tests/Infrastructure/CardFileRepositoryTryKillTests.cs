using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-1859: a false <c>TerminateJobObject</c> result is logged and does not throw,
/// so <c>TryKill</c> still reaches <c>Kill(entireProcessTree: true)</c> while git is alive.</summary>
[Category("Unit")]
public sealed class CardFileRepositoryTryKillTests
{
    [Test]
    public void False_terminate_job_result_still_reaches_the_tree_kill()
    {
        var root = RepositoryRoot();
        var repository = Parse(Path.Combine(root, "server/Infrastructure/Git/CardFileRepository.cs"));
        var marker = Parse(Path.Combine(root, "server/Infrastructure/Git/OwnedChildMarker.cs"));

        var tryKill = Method(repository, "TryKill");
        var tries = tryKill.DescendantNodes().OfType<TryStatementSyntax>().ToList();
        tries.Count.ShouldBe(2, "TerminateMembers and the tree walk are separate tries");

        var terminateMembers = tries[0].Block.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(call => call.ToString().Contains("TerminateMembers", StringComparison.Ordinal));
        terminateMembers.ToString().ShouldContain("TerminateMembers");
        tries[0].Catches.Single().DescendantNodes().OfType<ReturnStatementSyntax>().ShouldBeEmpty();
        tries[0].Catches.Single().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Any(call => call.ToString().Contains("Kill", StringComparison.Ordinal)).ShouldBeFalse();

        var killIf = tries[1].Block.DescendantNodes().OfType<IfStatementSyntax>().Single();
        tries[0].Span.End.ShouldBeLessThan(tries[1].SpanStart);
        killIf.Condition.ToString().ShouldBe("!process.HasExited");
        var kill = killIf.Statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Single();
        kill.Expression.ToString().ShouldBe("process.Kill");
        var argument = kill.ArgumentList.Arguments.Single();
        argument.NameColon!.Name.Identifier.Text.ShouldBe("entireProcessTree");
        argument.Expression.ToString().ShouldBe("true");

        var terminateMembersMethod = Method(marker, "TerminateMembers");
        var terminateJob = terminateMembersMethod.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(call => call.Expression.ToString() == "TerminateJobObject");
        terminateJob.Ancestors().OfType<VariableDeclaratorSyntax>().Single().Identifier.Text.ShouldBe("terminated");
        terminateMembersMethod.DescendantNodes().OfType<ThrowStatementSyntax>().ShouldBeEmpty();
        terminateMembersMethod.DescendantNodes().OfType<ThrowExpressionSyntax>().ShouldBeEmpty();
        var guard = terminateMembersMethod.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
        guard.SpanStart.ShouldBeLessThan(terminateJob.SpanStart);
        guard.Ancestors().OfType<IfStatementSyntax>().Single().Condition.ToString().ShouldBe("_job == IntPtr.Zero");
        var uses = terminateMembersMethod.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.Text == "terminated").ToList();
        uses.Count.ShouldBe(1, "the false result is logged and does not gate a throw or a return");
        uses[0].Ancestors().OfType<InterpolationSyntax>().ShouldNotBeEmpty();
        uses[0].Ancestors().OfType<InvocationExpressionSyntax>().Single().Expression.ToString().ShouldBe("log");

        var comment = killIf.GetLeadingTrivia().ToFullString();
        comment.ShouldContain("terminated=False");
        comment.ShouldContain("does not throw");
        comment.ShouldContain("does not wait");
        comment.ShouldContain("this walk runs");
        comment.ShouldContain("ReleaseChildAsync");
        comment.ShouldContain("before TerminateMembers");
        comment.ShouldNotContain("threw");
        comment.ShouldNotContain("already exited");
        comment.ShouldNotContain("Same attach-after-start limit");
        comment.ShouldNotContain("does not collect");
    }

    private static CompilationUnitSyntax Parse(string path) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();

    private static MethodDeclarationSyntax Method(CompilationUnitSyntax unit, string name) =>
        unit.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.Text == name);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Antiphon.sln not found above " + AppContext.BaseDirectory);
    }
}
