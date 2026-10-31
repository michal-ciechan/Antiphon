using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.TestSupport.LiveClaude;

/// <summary>
/// Syntax walk for live-lane sinks. A value reaches a sink only through a projection call,
/// a pure literal, or a literal-only concatenation. One-hop locals and same-method skip forwarders count.
/// </summary>
internal static class LiveClaudeSinkAudit
{
    internal readonly record struct Counts(int Recognized, int Console, int Assertion, int Skip, int Actual, int File, int Process);

    internal static Counts Walk(IEnumerable<SyntaxNode> roots)
    {
        var recognized = 0;
        var console = 0;
        var assertion = 0;
        var skip = 0;
        var actual = 0;
        var file = 0;
        var process = 0;
        foreach (var root in roots)
        {
            var tainted = Tainted(root);
            var forwardSkips = ForwardSkips(root);
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (IsProjection(call))
                {
                    recognized++;
                    continue;
                }
                var name = call.Expression.ToString();
                if (IsConsole(name))
                {
                    if (call.ArgumentList.Arguments.Any(arg => !SafeEmit(arg.Expression))) console++;
                    continue;
                }
                if (IsFileSync(name))
                {
                    if (LaterArgument(call, expression => !SafeEmit(expression))) file++;
                    continue;
                }
                if (IsFileAsync(name))
                {
                    if (LaterArgument(call, expression => Carries(expression, tainted))) file++;
                    continue;
                }
                if (IsProcessStart(name))
                {
                    if (call.ArgumentList.Arguments.Any(arg => Carries(arg.Expression, tainted))) process++;
                    continue;
                }
                if (IsShould(name))
                {
                    if (call.Expression is MemberAccessExpressionSyntax member && DirectScreen(Peel(member.Expression), tainted))
                        actual++;
                    if (call.ArgumentList.Arguments.Any(arg => MessageCarries(arg.Expression, tainted)))
                        assertion++;
                    continue;
                }
                if (forwardSkips.Contains(SimpleName(call))
                    && call.ArgumentList.Arguments.Any(arg => Carries(arg.Expression, tainted)))
                    skip++;
            }
            foreach (var created in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var type = created.Type.ToString();
                if (type.EndsWith("SkipTestException", StringComparison.Ordinal)
                    && created.ArgumentList?.Arguments.Any(arg => MessageCarries(arg.Expression, tainted)) == true)
                    skip++;
                else if (type.EndsWith("ProcessStartInfo", StringComparison.Ordinal)
                    && created.ArgumentList?.Arguments.Any(arg => Carries(arg.Expression, tainted)) == true)
                    process++;
            }
        }
        return new Counts(recognized, console, assertion, skip, actual, file, process);
    }

    private static HashSet<string> Tainted(SyntaxNode root)
    {
        var tainted = new HashSet<string>(StringComparer.Ordinal);
        for (var pass = 0; pass < 4; pass++)
        {
            foreach (var local in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (local.Initializer is not null && Carries(local.Initializer.Value, tainted))
                    tainted.Add(local.Identifier.ValueText);
            }
            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is IdentifierNameSyntax name && Carries(assignment.Right, tainted))
                    tainted.Add(name.Identifier.ValueText);
            }
        }
        return tainted;
    }

    private static HashSet<string> ForwardSkips(SyntaxNode root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var local in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            var parameters = new HashSet<string>(
                local.ParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText),
                StringComparer.Ordinal);
            var forwards = local.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Any(created =>
                created.Type.ToString().EndsWith("SkipTestException", StringComparison.Ordinal)
                && created.ArgumentList?.Arguments.Any(arg =>
                    Peel(arg.Expression) is IdentifierNameSyntax identifier && parameters.Contains(identifier.Identifier.ValueText)) == true);
            if (forwards) names.Add(local.Identifier.ValueText);
        }
        return names;
    }

    private static bool LaterArgument(InvocationExpressionSyntax call, Func<ExpressionSyntax, bool> raw)
    {
        for (var index = 1; index < call.ArgumentList.Arguments.Count; index++)
        {
            if (raw(call.ArgumentList.Arguments[index].Expression)) return true;
        }
        return false;
    }

    private static bool MessageCarries(ExpressionSyntax expression, HashSet<string> tainted) =>
        !SafeEmit(expression) && (Carries(expression, tainted) || Constructed(expression));

    private static bool SafeEmit(ExpressionSyntax expression)
    {
        expression = Peel(expression);
        if (expression is LiteralExpressionSyntax) return true;
        if (expression is InvocationExpressionSyntax call && IsProjection(call)) return true;
        if (expression is BinaryExpressionSyntax binary && binary.OperatorToken.Text == "+")
            return SafeEmit(binary.Left) && SafeEmit(binary.Right);
        if (expression is InterpolatedStringExpressionSyntax interpolation)
            return interpolation.Contents.OfType<InterpolationSyntax>().All(hole => SafeEmit(hole.Expression));
        return false;
    }

    private static bool Constructed(ExpressionSyntax expression)
    {
        expression = Peel(expression);
        if (expression is BinaryExpressionSyntax binary && binary.OperatorToken.Text == "+") return true;
        if (expression is not InvocationExpressionSyntax call) return false;
        var name = call.Expression.ToString();
        var leaf = Leaf(name);
        if (leaf is not ("Format" or "Concat")) return false;
        if (!name.Contains("string", StringComparison.Ordinal)) return false;
        return call.ArgumentList.Arguments.Any(arg => Peel(arg.Expression) is not LiteralExpressionSyntax);
    }

    private static bool Carries(SyntaxNode node, HashSet<string> tainted) =>
        node.DescendantNodesAndSelf().Any(inner => IsDirect(inner, tainted));

    private static bool DirectScreen(ExpressionSyntax expression, HashSet<string> tainted) =>
        IsDirect(Peel(expression), tainted);

    private static bool IsDirect(SyntaxNode node, HashSet<string> tainted) =>
        node is InvocationExpressionSyntax call && IsScreenCall(call)
        || node is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "RenderedScreen"
        || node is IdentifierNameSyntax identifier && tainted.Contains(identifier.Identifier.ValueText);

    private static bool IsScreenCall(InvocationExpressionSyntax call) =>
        call.Expression.ToString().EndsWith("SnapshotScreen", StringComparison.Ordinal) || IsSecretEnv(call);

    private static bool IsSecretEnv(InvocationExpressionSyntax call)
    {
        if (!call.Expression.ToString().EndsWith("GetEnvironmentVariable", StringComparison.Ordinal)) return false;
        if (call.ArgumentList.Arguments.Count < 1) return false;
        return Peel(call.ArgumentList.Arguments[0].Expression) is LiteralExpressionSyntax literal
            && literal.Token.ValueText is "HOME" or "USERPROFILE" or "CLAUDE";
    }

    private static bool IsProjection(InvocationExpressionSyntax call) =>
        call.Expression.ToString().StartsWith("LiveClaudeEvidence.", StringComparison.Ordinal);

    private static bool IsConsole(string name)
    {
        var leaf = Leaf(name);
        if (leaf is not ("Write" or "WriteLine" or "WriteAsync" or "WriteLineAsync")) return false;
        return name.StartsWith("Console", StringComparison.Ordinal)
            || name.StartsWith("TestContext", StringComparison.Ordinal)
            || name.Contains(".Console.", StringComparison.Ordinal)
            || name.Contains(".TestContext.", StringComparison.Ordinal);
    }

    private static bool IsFileSync(string name)
    {
        var leaf = Leaf(name);
        return (leaf is "WriteAllText" or "AppendAllText" or "WriteAllBytes" or "AppendAllLines")
            && name.Contains("File", StringComparison.Ordinal);
    }

    private static bool IsFileAsync(string name)
    {
        var leaf = Leaf(name);
        return (leaf is "WriteAllTextAsync" or "AppendAllTextAsync" or "WriteAllBytesAsync" or "AppendAllLinesAsync")
            && name.Contains("File", StringComparison.Ordinal);
    }

    private static bool IsProcessStart(string name) =>
        name is "Process.Start" || name.EndsWith(".Process.Start", StringComparison.Ordinal);

    private static bool IsShould(string name) => Leaf(name).Contains("Should", StringComparison.Ordinal);

    private static string SimpleName(InvocationExpressionSyntax call) => call.Expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => call.Expression.ToString()
    };

    private static string Leaf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    private static ExpressionSyntax Peel(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
        return expression;
    }
}
