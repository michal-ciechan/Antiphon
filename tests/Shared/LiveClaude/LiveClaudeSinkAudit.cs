using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.TestSupport.LiveClaude;

/// <summary>
/// Syntax walk for live-lane sinks. A value reaches a sink only through a projection call,
/// a pure literal, or a literal-only concatenation. One-hop locals, console aliases,
/// using-static Console, method groups, conditional writes, and same-method skip forwarders count.
/// Reflection, dynamic, and mutation through an unlisted helper in another file stay invisible.
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
            var aliases = Aliases(root);
            var usings = UsingConsole(root);
            var forwardSkips = ForwardSkips(root);
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (IsProjection(call))
                {
                    recognized++;
                    continue;
                }
                var name = call.Expression.ToString();
                if (IsConsole(name) || IsAliasConsole(call, aliases) || IsUsingConsole(call, usings))
                {
                    if (call.ArgumentList.Arguments.Any(arg => !SafeEmit(arg.Expression))) console++;
                    continue;
                }
                if (call.ArgumentList.Arguments.Any(arg => IsConsoleMethodGroup(arg.Expression)))
                {
                    console++;
                    continue;
                }
                if (IsFileSync(name))
                {
                    if (ArgumentCarries(call, 0, tainted) || LaterArgument(call, expression => !SafeEmit(expression))) file++;
                    continue;
                }
                if (IsFileCarry(name) || IsWriterCall(name) || IsAliasWriter(call, aliases))
                {
                    if (call.ArgumentList.Arguments.Any(arg => Carries(arg.Expression, tainted))) file++;
                    continue;
                }
                if (IsFileAsync(name))
                {
                    if (ArgumentCarries(call, 0, tainted) || LaterArgument(call, expression => Carries(expression, tainted))) file++;
                    continue;
                }
                if (IsProcessStart(name))
                {
                    if (call.ArgumentList.Arguments.Any(arg => Carries(arg.Expression, tainted))) process++;
                    continue;
                }
                if (IsSkipDotTest(name))
                {
                    if (call.ArgumentList.Arguments.Any(arg => MessageCarries(arg.Expression, tainted))) skip++;
                    continue;
                }
                if (IsAssertMessage(name))
                {
                    if (call.ArgumentList.Arguments.Any(arg => MessageCarries(arg.Expression, tainted))) assertion++;
                    continue;
                }
                if (IsShould(name))
                {
                    if (call.Expression is MemberAccessExpressionSyntax member && ShouldSubject(member.Expression, tainted))
                        actual++;
                    if (call.ArgumentList.Arguments.Any(arg => MessageCarries(arg.Expression, tainted)))
                        assertion++;
                    continue;
                }
                if (forwardSkips.Contains(SimpleName(call))
                    && call.ArgumentList.Arguments.Any(arg => Carries(arg.Expression, tainted)))
                    skip++;
            }
            foreach (var access in root.DescendantNodes().OfType<ConditionalAccessExpressionSyntax>())
            {
                if (access.WhenNotNull is not InvocationExpressionSyntax call) continue;
                if (!IsWriteLeaf(Leaf(call.Expression.ToString()))) continue;
                if (!ConditionalConsole(access)) continue;
                if (call.ArgumentList.Arguments.Any(arg => !SafeEmit(arg.Expression))) console++;
            }
            foreach (var created in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var type = created.Type.ToString();
                if (type.EndsWith("Exception", StringComparison.Ordinal)
                    && created.ArgumentList?.Arguments.Any(arg => MessageCarries(arg.Expression, tainted)) == true)
                    skip++;
                else if (type.EndsWith("ProcessStartInfo", StringComparison.Ordinal)
                    && created.ArgumentList?.Arguments.Any(arg => Carries(arg.Expression, tainted)) == true)
                    process++;
                else if (IsWriterType(type)
                    && created.ArgumentList?.Arguments.Any(arg => Carries(arg.Expression, tainted)) == true)
                    file++;
            }
            foreach (var thrown in root.DescendantNodes().OfType<ThrowStatementSyntax>())
                if (ThrownCarries(thrown.Expression, tainted)) skip++;
            foreach (var thrown in root.DescendantNodes().OfType<ThrowExpressionSyntax>())
                if (ThrownCarries(thrown.Expression, tainted)) skip++;
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
                if (assignment.Left is ElementAccessExpressionSyntax element
                    && element.Expression is IdentifierNameSyntax array
                    && Carries(assignment.Right, tainted))
                    tainted.Add(array.Identifier.ValueText);
                TaintDeconstruction(assignment, tainted);
            }
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression is not MemberAccessExpressionSyntax member) continue;
                if (member.Expression is not IdentifierNameSyntax receiver) continue;
                var leaf = member.Name.Identifier.ValueText;
                if (leaf is not ("Append" or "AppendLine" or "AppendFormatted" or "AppendLiteral")) continue;
                if (call.ArgumentList.Arguments.Any(arg => Carries(arg.Expression, tainted)))
                    tainted.Add(receiver.Identifier.ValueText);
            }
        }
        return tainted;
    }

    private readonly record struct AliasSets(HashSet<string> Console, HashSet<string> Writer);

    private readonly record struct UsingConsoleSet(bool StaticConsole, Dictionary<string, string> Aliases);

    private static AliasSets Aliases(SyntaxNode root)
    {
        var console = new HashSet<string>(StringComparer.Ordinal);
        var writer = new HashSet<string>(StringComparer.Ordinal);
        void Take(string name, ExpressionSyntax? value)
        {
            if (value is null || string.IsNullOrEmpty(name)) return;
            if (IsConsoleWriter(value) || IsConsoleMethodGroup(value)) console.Add(name);
            if (IsWriterCreation(value)) writer.Add(name);
        }
        foreach (var local in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            Take(local.Identifier.ValueText, local.Initializer?.Value);
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is IdentifierNameSyntax name)
                Take(name.Identifier.ValueText, assignment.Right);
        }
        return new AliasSets(console, writer);
    }

    private static UsingConsoleSet UsingConsole(SyntaxNode root)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var unit = root as CompilationUnitSyntax ?? root.SyntaxTree?.GetRoot() as CompilationUnitSyntax;
        var staticConsole = false;
        if (unit is null) return new UsingConsoleSet(false, aliases);
        foreach (var directive in unit.Usings)
        {
            var target = directive.Name?.ToString() ?? "";
            if (directive.Alias is null
                && directive.StaticKeyword != default
                && target is "System.Console" or "Console")
                staticConsole = true;
            if (directive.Alias is not null)
                aliases[directive.Alias.Name.Identifier.ValueText] = target;
        }
        return new UsingConsoleSet(staticConsole, aliases);
    }

    private static void TaintDeconstruction(AssignmentExpressionSyntax assignment, HashSet<string> tainted)
    {
        var names = Designations(assignment.Left).ToArray();
        if (names.Length == 0 || assignment.Right is not TupleExpressionSyntax tuple) return;
        if (tuple.Arguments.Count != names.Length) return;
        for (var i = 0; i < names.Length; i++)
        {
            if (Carries(tuple.Arguments[i].Expression, tainted))
                tainted.Add(names[i]);
        }
    }

    private static IEnumerable<string> Designations(ExpressionSyntax expression)
    {
        expression = Peel(expression);
        if (expression is DeclarationExpressionSyntax declaration)
            return Names(declaration.Designation);
        if (expression is TupleExpressionSyntax tuple)
            return tuple.Arguments.SelectMany(argument => Designations(argument.Expression));
        return [];
    }

    private static IEnumerable<string> Names(VariableDesignationSyntax designation)
    {
        if (designation is SingleVariableDesignationSyntax single && single.Identifier.ValueText is { Length: > 0 } singleName && singleName != "_")
            yield return singleName;
        if (designation is not ParenthesizedVariableDesignationSyntax parenthesized) yield break;
        foreach (var child in parenthesized.Variables)
        foreach (var nested in Names(child))
            yield return nested;
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

    internal static IEnumerable<SyntaxNode> WithHelpers(MethodDeclarationSyntax method)
    {
        var type = method.Parent as TypeDeclarationSyntax;
        var methods = type?.Members.OfType<MethodDeclarationSyntax>().ToArray() ?? [];
        var byName = new Dictionary<string, List<MethodDeclarationSyntax>>(StringComparer.Ordinal);
        foreach (var candidate in methods)
        {
            if (!byName.TryGetValue(candidate.Identifier.ValueText, out var list))
            {
                list = new List<MethodDeclarationSyntax>();
                byName[candidate.Identifier.ValueText] = list;
            }
            list.Add(candidate);
        }
        var seen = new HashSet<MethodDeclarationSyntax>();
        var queue = new Queue<MethodDeclarationSyntax>();
        queue.Enqueue(method);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current)) continue;
            yield return current;
            foreach (var call in current.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!byName.TryGetValue(SimpleName(call), out var targets)) continue;
                foreach (var target in targets)
                    if (!seen.Contains(target)) queue.Enqueue(target);
            }
        }
    }

    private static bool ThrownCarries(ExpressionSyntax? expression, HashSet<string> tainted)
    {
        if (expression is null) return false;
        expression = Peel(expression);
        if (expression is ObjectCreationExpressionSyntax) return false;
        return Carries(expression, tainted);
    }

    private static bool ArgumentCarries(InvocationExpressionSyntax call, int index, HashSet<string> tainted) =>
        call.ArgumentList.Arguments.Count > index && Carries(call.ArgumentList.Arguments[index].Expression, tainted);

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
        node is InvocationExpressionSyntax call && (IsScreenCall(call) || IsFolderPath(call))
        || node is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText is "RenderedScreen" or "UserProfile"
        || node is IdentifierNameSyntax identifier && (tainted.Contains(identifier.Identifier.ValueText)
            || identifier.Identifier.ValueText == "UserProfile");

    private static bool IsScreenCall(InvocationExpressionSyntax call) =>
        call.Expression.ToString().EndsWith("SnapshotScreen", StringComparison.Ordinal) || IsSecretEnv(call);

    private static bool IsFolderPath(InvocationExpressionSyntax call) =>
        call.Expression.ToString().EndsWith("GetFolderPath", StringComparison.Ordinal);

    private static bool IsSecretEnv(InvocationExpressionSyntax call)
    {
        if (!call.Expression.ToString().EndsWith("GetEnvironmentVariable", StringComparison.Ordinal)) return false;
        if (call.ArgumentList.Arguments.Count < 1) return false;
        return Peel(call.ArgumentList.Arguments[0].Expression) is LiteralExpressionSyntax literal
            && literal.Token.ValueText is "HOME" or "USERPROFILE" or "CLAUDE";
    }

    private static bool IsProjection(InvocationExpressionSyntax call) =>
        call.Expression.ToString().StartsWith("LiveClaudeEvidence.", StringComparison.Ordinal);

    private static bool IsWriteLeaf(string leaf) =>
        leaf is "Write" or "WriteLine" or "WriteAsync" or "WriteLineAsync";

    private static bool IsConsole(string name)
    {
        if (!IsWriteLeaf(Leaf(name))) return false;
        return IsWriteTarget(name, "Console")
            || IsWriteTarget(name, "TestContext")
            || IsWriteTarget(name, "Trace")
            || IsWriteTarget(name, "Debug");
    }

    private static bool IsAliasConsole(InvocationExpressionSyntax call, AliasSets aliases)
    {
        if (call.Expression is IdentifierNameSyntax identifier)
            return aliases.Console.Contains(identifier.Identifier.ValueText);
        if (call.Expression is not MemberAccessExpressionSyntax member) return false;
        if (!IsWriteLeaf(member.Name.Identifier.ValueText)) return false;
        return member.Expression is IdentifierNameSyntax receiver && aliases.Console.Contains(receiver.Identifier.ValueText);
    }

    private static bool IsUsingConsole(InvocationExpressionSyntax call, UsingConsoleSet usings)
    {
        if (usings.StaticConsole && call.Expression is IdentifierNameSyntax && IsWriteLeaf(Leaf(call.Expression.ToString())))
            return true;
        if (call.Expression is not MemberAccessExpressionSyntax member) return false;
        if (!IsWriteLeaf(member.Name.Identifier.ValueText)) return false;
        if (member.Expression is not IdentifierNameSyntax receiver) return false;
        return usings.Aliases.TryGetValue(receiver.Identifier.ValueText, out var target)
            && target is "System.Console" or "Console";
    }

    private static bool IsConsoleWriter(ExpressionSyntax expression)
    {
        var text = Peel(expression).ToString();
        return text is "Console.Out" or "Console.Error" or "System.Console.Out" or "System.Console.Error";
    }

    private static bool IsConsoleMethodGroup(ExpressionSyntax expression)
    {
        var text = Peel(expression).ToString();
        return text is "Console.WriteLine" or "Console.Write" or "Console.WriteLineAsync" or "Console.WriteAsync"
            or "Console.Out.WriteLine" or "Console.Out.Write" or "Console.Error.WriteLine" or "Console.Error.Write"
            or "System.Console.WriteLine" or "System.Console.Write";
    }

    private static bool ConditionalConsole(ConditionalAccessExpressionSyntax access)
    {
        var text = access.ToString();
        var paren = text.IndexOf('(');
        if (paren >= 0) text = text[..paren];
        return text.Contains("Console", StringComparison.Ordinal)
            || text.Contains("OutputWriter", StringComparison.Ordinal)
            || text.Contains("TestContext", StringComparison.Ordinal);
    }

    private static bool IsWriteTarget(string name, string type) =>
        name.StartsWith(type + ".", StringComparison.Ordinal)
        || name.Contains("." + type + ".", StringComparison.Ordinal);

    private static bool IsSkipDotTest(string name)
    {
        var leaf = Leaf(name);
        if (leaf is not ("Test" or "When" or "Unless" or "If")) return false;
        return name == "Skip." + leaf || name.EndsWith(".Skip." + leaf, StringComparison.Ordinal);
    }

    private static bool IsAssertMessage(string name) =>
        name.StartsWith("Assert.", StringComparison.Ordinal)
        || name.Contains(".Assert.", StringComparison.Ordinal)
        || name is "Debug.Assert" or "Environment.FailFast"
        || name.EndsWith(".Debug.Assert", StringComparison.Ordinal)
        || name.EndsWith(".FailFast", StringComparison.Ordinal);

    private static bool IsFileSync(string name)
    {
        var leaf = Leaf(name);
        return (leaf is "WriteAllText" or "AppendAllText" or "WriteAllBytes" or "AppendAllLines" or "WriteAllLines")
            && name.Contains("File", StringComparison.Ordinal);
    }

    private static bool IsFileCarry(string name)
    {
        var leaf = Leaf(name);
        if (leaf is "Create" or "Move" or "CreateText" or "AppendText")
            return name.Contains("File", StringComparison.Ordinal);
        return leaf == "CreateDirectory" && name.Contains("Directory", StringComparison.Ordinal);
    }

    private static bool IsWriterType(string type) =>
        type.EndsWith("StreamWriter", StringComparison.Ordinal) || type.EndsWith("FileStream", StringComparison.Ordinal);

    private static bool IsWriterCreation(ExpressionSyntax expression) =>
        Peel(expression) is ObjectCreationExpressionSyntax created && IsWriterType(created.Type.ToString());

    private static bool IsWriterCall(string name)
    {
        if (!IsWriteLeaf(Leaf(name))) return false;
        return name.Contains("StreamWriter", StringComparison.Ordinal) || name.Contains("FileStream", StringComparison.Ordinal);
    }

    private static bool IsAliasWriter(InvocationExpressionSyntax call, AliasSets aliases)
    {
        if (!IsWriteLeaf(Leaf(call.Expression.ToString()))) return false;
        if (call.Expression is IdentifierNameSyntax identifier)
            return aliases.Writer.Contains(identifier.Identifier.ValueText);
        return call.Expression is MemberAccessExpressionSyntax member
            && member.Expression is IdentifierNameSyntax receiver
            && aliases.Writer.Contains(receiver.Identifier.ValueText);
    }

    // Contains/StartsWith and other predicates return a bool. Shouldly does not print the
    // receiver, and the canaries assert those bools. Split and the other leaves still yield text.
    private static bool PreservesText(string leaf) =>
        leaf is "Split" or "Substring" or "Trim" or "TrimStart" or "TrimEnd"
            or "ToUpper" or "ToLower" or "ToUpperInvariant" or "ToLowerInvariant"
            or "Replace" or "Remove" or "Insert" or "PadLeft" or "PadRight"
            or "Normalize" or "ToCharArray" or "ToArray";

    private static bool ShouldSubject(ExpressionSyntax expression, HashSet<string> tainted)
    {
        expression = Peel(expression);
        if (DirectScreen(expression, tainted)) return true;
        if (expression is InvocationExpressionSyntax call
            && call.Expression is MemberAccessExpressionSyntax member
            && Carries(member.Expression, tainted)
            && PreservesText(member.Name.Identifier.ValueText))
            return true;
        if (expression is ImplicitArrayCreationExpressionSyntax implicitArray
            && implicitArray.Initializer.Expressions.Any(item => Carries(item, tainted)))
            return true;
        if (expression is ArrayCreationExpressionSyntax array
            && array.Initializer?.Expressions.Any(item => Carries(item, tainted)) == true)
            return true;
        if (expression is CollectionExpressionSyntax collection
            && collection.Elements.Any(element => element is ExpressionElementSyntax item && Carries(item.Expression, tainted)))
            return true;
        return expression is ElementAccessExpressionSyntax access && Carries(access.Expression, tainted);
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
