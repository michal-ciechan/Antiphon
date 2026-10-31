using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.TestSupport.LiveClaude;

/// <summary>Bounded syntax contract for the simple launch statements used by these callers.</summary>
internal static class LiveClaudeExecutableAudit
{
    public static string Root()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("C1545 source root missing");
    }
    public static string Read(string relative) => File.ReadAllText(Path.Combine(Root(), relative));
    public static MethodDeclarationSyntax Method(string source, string name)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)) Fail(name, "parse");
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText == name).ToArray();
        if (methods.Length != 1 || methods[0].Body is null) Fail(name, "method-count/body");
        return methods[0];
    }
    public static void Prefix(BlockSyntax? body, string expected, string name)
    {
        var statement = SyntaxFactory.ParseStatement(expected);
        if (body?.Statements.FirstOrDefault() is not LocalDeclarationStatementSyntax actual
            || !SyntaxFactory.AreEquivalent(actual, statement)) Fail(name, "required first admission");
    }
    public static void Call(BlockSyntax? body, string expression, string firstArgument, string name, int argumentIndex = 0)
    {
        var calls = body?.Statements.Where(s => s is LocalDeclarationStatementSyntax or ExpressionStatementSyntax)
            .SelectMany(s => s.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(c => c.Expression.ToString() == expression
                && c.ArgumentList.Arguments.ElementAtOrDefault(argumentIndex)?.Expression.ToString() == firstArgument)
            .ToArray() ?? [];
        if (calls.Length != 1 || calls[0].Ancestors().TakeWhile(a => a != body)
            .Any(a => a is AnonymousFunctionExpressionSyntax or ConditionalExpressionSyntax))
            Fail(name, "required consumed launch delegation");
    }
    public static void Core(string source, string method, string constant, string consume, string argument = "executable", int argumentIndex = 0)
    {
        var body = Method(source, method).Body;
        Prefix(body, $"var executable = ClSession.RequireCore(LiveClaudeCore.{constant});", method);
        Call(body, consume, argument, method, argumentIndex);
        if (body!.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(c =>
                c.Expression.ToString() is "ClSession.ResolveOrThrow" or "ClSession.ResolveCl" or "ClSession.ResolveClaude"))
            Fail(method, "legacy resolver in core");
    }
    public static void Harness(string source)
    {
        var body = Method(source, "LaunchReadyAsync").Body!;
        var branch = body.Statements.OfType<IfStatementSyntax>().SingleOrDefault();
        if (branch?.Condition.ToString() != "backend == \"fakeclaude\"" || branch.Else?.Statement is not BlockSyntax live)
            Fail("harness", "unknown branch");
        else
        {
            Prefix(live, "var executable = coreExecutable ?? ClSession.RequireCore(LiveClaudeCore.Submit);", "harness");
            Call(live, "ClSession.BuildCoreLaunch", "executable", "harness");
            var method = Method(source, "LaunchReadyAsync");
            Launch("class Audited { " + method.WithBody(live).ToFullString() + " }", "LaunchReadyAsync");
        }
    }
    public static void C680(string source)
    {
        var body = Method(source, "C680_LiveNudgeRefusesPermissionAndQuestionDialogs").Body;
        Prefix(body, "var executable = RequireExecutable();", "C680");
        // These named builder methods are also called by the portable capture fixture.
        Call(body, "CreateVersionProbe", "executable", "C680-probe");
        var launches = body!.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(c => c.Expression.ToString() == "runner.StartAsync").ToArray();
        if (launches.Length != 1 || launches[0].ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression
                is not InvocationExpressionSyntax build || build.Expression.ToString() is not ("CreateLaunchSpec" or "CreateProfileLaunch")
            || build.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() != "executable")
            Fail("C680", "launch must consume admitted spec");
        // Observation refusal must be an unconditional statement immediately after the actual start.
        var statement = launches[0].Ancestors().OfType<ExpressionStatementSyntax>().First();
        var siblings = ((BlockSyntax)statement.Parent!).Statements;
        var next = siblings.ElementAtOrDefault(siblings.IndexOf(statement) + 1);
        if (!SyntaxFactory.AreEquivalent(next, SyntaxFactory.ParseStatement("executable.RequireNativeObservation();")))
            Fail("C680", "post-launch observation required");
    }
    public static void Launch(string source, string method)
    {
        var body = Method(source, method).Body!;
        var statements = body.Statements;
        var builder = statements.SelectMany(s => s.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(c => c.Expression.ToString() == "ClSession.BuildCoreLaunch").ToArray();
        if (builder.Length != 1 || builder[0].ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() != "executable")
            Fail(method, "exact builder count/path");
        var statement = builder[0].Ancestors().OfType<StatementSyntax>().First();
        var normalized = statement.ReplaceNode(builder[0], builder[0].WithArgumentList(SyntaxFactory.ParseArgumentList("(executable)")));
        var expected = SyntaxFactory.ParseStatement("var (app, args) = ClSession.BuildCoreLaunch(executable);");
        var expectedStartup = SyntaxFactory.ParseStatement("var (app, launchArgs) = ClSession.BuildCoreLaunch(executable);");
        var isStartup = SyntaxFactory.AreEquivalent(normalized, expectedStartup);
        if (!isStartup && !SyntaxFactory.AreEquivalent(normalized, expected)) Fail(method, "builder result must be consumed");
        if (statement.Parent != body) Fail(method, "builder must be unconditional");
        var next = statements.ElementAtOrDefault(statements.IndexOf(statement) + 1);
        var start = next?.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .SingleOrDefault(c => c.Expression.ToString() == "runner.StartAsync");
        if (start is null || start.ArgumentList.Arguments.ElementAtOrDefault(0)?.Expression.ToString() != "app"
            || start.ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression.ToString() != (isStartup ? "launchArgs" : "args"))
            Fail(method, "actual start must use builder result");
        var after = statements.ElementAtOrDefault(statements.IndexOf(statement) + 2);
        if (!SyntaxFactory.AreEquivalent(after, SyntaxFactory.ParseStatement("executable.RequireNativeObservation();")))
            Fail(method, "actual start requires observation");
    }
    public static void Builder(string source, string method, int pathArgument)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var declarations = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText == method).ToArray();
        if (declarations.Length != 1 || declarations[0].ExpressionBody?.Expression is not BaseObjectCreationExpressionSyntax creation
            || creation.ArgumentList?.Arguments.ElementAtOrDefault(pathArgument)?.Expression.ToString() != "executable.RequirePath()")
            Fail(method, "builder must use admitted path directly");
    }
    public static void Reject(Action<string> audit, string healthy, string from, string to, string pc)
    {
        audit(healthy);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("C1545_EXECUTABLE_CONTROLS"))) return;
        if (!healthy.Contains(from, StringComparison.Ordinal)) Fail(pc, "mutation site missing");
        var changed = healthy.Replace(from, to, StringComparison.Ordinal);
        var refused = false;
        try { audit(changed); } catch (InvalidOperationException e) when (e.Message.StartsWith("C1545 audit/")) { refused = true; }
        if (!refused) Fail(pc, "mutant survived");
        audit(healthy);
        Console.WriteLine($"C1545 {pc} assertion-red; restored-green (owned syntax copy)");
    }
    private static void Fail(string name, string predicate) => throw new InvalidOperationException($"C1545 audit/{name} {predicate}");
}
