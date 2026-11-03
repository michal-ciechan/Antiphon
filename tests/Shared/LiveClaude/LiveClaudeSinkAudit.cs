using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.TestSupport.LiveClaude;

/// <summary>
/// Syntax walk for live-lane sinks. A value reaches a sink only through a projection call,
/// a pure literal, or a literal-only concatenation. One-hop locals, console aliases,
/// using-static Console, method groups, conditional writes, and same-method skip forwarders count.
/// CARD-1645: outside those categories the walk is a closed allow-list. A tainted value that
/// reaches any other invocation, creation, or non-local store counts as Escape unless it is a
/// LiveClaudeEvidence projection, a named bool predicate, a reviewed product screen reader,
/// a pure text transform (its result stays tainted), a mutator of an in-memory buffer local,
/// or a walked helper (whose parameters, out arguments and return values carry the taint).
/// </summary>
internal static class LiveClaudeSinkAudit
{
    internal readonly record struct Counts(int Recognized, int Console, int Assertion, int Skip, int Actual, int File, int Process, int Escape = 0);

    internal static Counts Walk(IEnumerable<SyntaxNode> roots)
    {
        var recognized = 0;
        var console = 0;
        var assertion = 0;
        var skip = 0;
        var actual = 0;
        var file = 0;
        var process = 0;
        var escape = 0;
        var list = roots.ToList();
        var callees = Callees(list);
        var tainted = Tainted(list, callees);
        foreach (var root in list)
        {
            var aliases = Aliases(root);
            var locals = Locals(root);
            var buffers = Buffers(root);
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
                {
                    skip++;
                    continue;
                }
                if (Escapes(call, tainted, callees, buffers)) escape++;
            }
            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!Carries(assignment.Right, tainted)) continue;
                if (!StaysLocal(assignment.Left, locals)) escape++;
            }
            foreach (var created in root.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
            {
                if (created.ArgumentList.Arguments.Any(arg => ArgumentCarries(arg, tainted))) escape++;
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
                if (type.EndsWith("Exception", StringComparison.Ordinal))
                {
                    if (created.ArgumentList?.Arguments.Any(arg => MessageCarries(arg.Expression, tainted)) == true)
                        skip++;
                }
                else if (type.EndsWith("ProcessStartInfo", StringComparison.Ordinal)
                    && created.ArgumentList?.Arguments.Any(arg => Carries(arg.Expression, tainted)) == true)
                    process++;
                else if (IsWriterType(type)
                    && created.ArgumentList?.Arguments.Any(arg => Carries(arg.Expression, tainted)) == true)
                    file++;
                else if (!IsBufferType(type)
                    && created.ArgumentList?.Arguments.Any(arg => ArgumentCarries(arg, tainted)) == true)
                    escape++;
            }
            foreach (var thrown in root.DescendantNodes().OfType<ThrowStatementSyntax>())
                if (ThrownCarries(thrown.Expression, tainted)) skip++;
            foreach (var thrown in root.DescendantNodes().OfType<ThrowExpressionSyntax>())
                if (ThrownCarries(thrown.Expression, tainted)) skip++;
        }
        return new Counts(recognized, console, assertion, skip, actual, file, process, escape);
    }

    // Methods and local functions the walk reads, by simple name, with their parameter names.
    private static Dictionary<string, List<string[]>> Callees(List<SyntaxNode> roots)
    {
        var callees = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        void Take(string name, ParameterListSyntax parameters)
        {
            if (!callees.TryGetValue(name, out var list))
            {
                list = new List<string[]>();
                callees[name] = list;
            }
            list.Add(parameters.Parameters.Select(parameter => parameter.Identifier.ValueText).ToArray());
        }
        foreach (var root in roots)
        {
            foreach (var method in root.DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>())
                Take(method.Identifier.ValueText, method.ParameterList);
            foreach (var local in root.DescendantNodesAndSelf().OfType<LocalFunctionStatementSyntax>())
                Take(local.Identifier.ValueText, local.ParameterList);
        }
        return callees;
    }

    private static bool IsCallee(InvocationExpressionSyntax call, Dictionary<string, List<string[]>> callees) =>
        call.Expression switch
        {
            IdentifierNameSyntax identifier => callees.ContainsKey(identifier.Identifier.ValueText),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } member => callees.ContainsKey(member.Name.Identifier.ValueText),
            // An instance helper of a walked file (debug.Draft). A Type.Name call is never a callee.
            MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } member =>
                receiver.Identifier.ValueText is [var first, ..] && char.IsLower(first)
                && callees.ContainsKey(member.Name.Identifier.ValueText),
            _ => false
        };

    // Product screen readers whose result carries no screen text; the walk does not descend into them.
    private static bool IsSanitizing(InvocationExpressionSyntax call) =>
        IsProjection(call)
        || call.Expression.ToString() is "ClaudeDialogObservation.Observe" or "ClaudeScreen.IsSettled"
            or "ClaudeScreen.IsWorking" or "ClaudeScreen.ComposerIsLive" or "ClaudeScreen.TryReadComposer"
            or "ClaudeScreen.ComposerContentIsEmpty" or "ComposerDeliveryEvidence.FragmentIsVisible"
            or "ComposerDeliveryEvidence.HeadFragmentIsVisible" or "ComposerDeliveryEvidence.HeadFragmentIsVisibleWhole"
            or "PromptSubmissionMatch.IsCompleteIn";

    // Calls that may take tainted text without emitting it. Their result stays tainted.
    private static bool IsReviewedCall(InvocationExpressionSyntax call) =>
        call.Expression.ToString() is "string.Join" or "string.Concat" or "string.Format"
            or "JsonSerializer.Serialize" or "Task.FromResult" or "Path.Combine" or "Path.GetFullPath"
            or "ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync"
            // Reviewed verifier: literal-only refusals over bridge observations.
            or "LiveClaudeStartupEvidence.RequireSlash";

    private static bool Escapes(InvocationExpressionSyntax call, HashSet<string> tainted, Dictionary<string, List<string[]>> callees, HashSet<string> buffers)
    {
        if (!call.ArgumentList.Arguments.Any(arg => ArgumentCarries(arg, tainted))) return false;
        var leaf = SimpleName(call);
        if (IsPredicate(leaf) || IsSanitizing(call) || IsReviewedCall(call)) return false;
        if (IsCallee(call, callees)) return false;
        return !(call.Expression is MemberAccessExpressionSyntax member
            && member.Expression is IdentifierNameSyntax receiver
            && buffers.Contains(receiver.Identifier.ValueText)
            && IsBufferMutator(leaf));
    }

    // A lambda argument escapes only when it returns tainted text; its body is walked in place.
    private static bool ArgumentCarries(ArgumentSyntax argument, HashSet<string> tainted)
    {
        var expression = Peel(argument.Expression);
        if (expression is AnonymousFunctionExpressionSyntax function)
        {
            if (function.ExpressionBody is { } body) return ReturnCarries(body, tainted);
            return function.Block?.DescendantNodes().OfType<ReturnStatementSyntax>()
                .Any(statement => statement.Expression is { } value && ReturnCarries(value, tainted)) == true;
        }
        return Carries(expression, tainted);
    }

    private static bool ReturnCarries(ExpressionSyntax expression, HashSet<string> tainted) =>
        !PredicateShaped(expression) && Carries(expression, tainted);

    private static bool PredicateShaped(ExpressionSyntax expression)
    {
        expression = Peel(expression);
        if (expression is LiteralExpressionSyntax literal)
            return literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.TrueLiteralExpression)
                || literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.FalseLiteralExpression);
        if (expression is BinaryExpressionSyntax binary)
            return binary.OperatorToken.Text is "==" or "!=" or "<" or ">" or "<=" or ">=" or "&&" or "||";
        if (expression is PrefixUnaryExpressionSyntax unary) return unary.OperatorToken.Text == "!";
        if (expression is IsPatternExpressionSyntax) return true;
        return expression is InvocationExpressionSyntax call && IsPredicate(SimpleName(call));
    }

    private static bool IsPredicate(string leaf) =>
        leaf is "Contains" or "StartsWith" or "EndsWith" or "Equals" or "IsMatch" or "SequenceEqual"
            or "ContainsKey" or "ContainsValue" or "Any" or "All" or "Exists" or "IsNullOrEmpty"
            or "IsNullOrWhiteSpace" or "SetEquals" or "Overlaps" or "IsSubsetOf" or "IsSupersetOf";

    private static bool IsBufferType(string type)
    {
        var angle = type.IndexOf('<');
        var name = Leaf(angle < 0 ? type : type[..angle]);
        return name is "List" or "StringBuilder" or "HashSet" or "Dictionary" or "Queue" or "Stack"
            or "SortedSet" or "SortedDictionary" or "LinkedList";
    }

    private static bool IsBufferMutator(string leaf) =>
        leaf is "Add" or "AddRange" or "Append" or "AppendLine" or "AppendFormat" or "AppendJoin"
            or "AppendFormatted" or "AppendLiteral" or "Insert" or "InsertRange" or "Push" or "Enqueue"
            or "TryAdd" or "Remove" or "Clear";

    // In-memory buffer locals: an allow-listed collection or builder created in place.
    private static HashSet<string> Buffers(SyntaxNode root)
    {
        var buffers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var local in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (IsField(local)) continue;
            var value = local.Initializer?.Value is { } initializer ? Peel(initializer) : null;
            var declared = (local.Parent as VariableDeclarationSyntax)?.Type.ToString() ?? "";
            if (value is ObjectCreationExpressionSyntax created && IsBufferType(created.Type.ToString()))
                buffers.Add(local.Identifier.ValueText);
            else if (value is ImplicitObjectCreationExpressionSyntax && IsBufferType(declared))
                buffers.Add(local.Identifier.ValueText);
            else if (value is CollectionExpressionSyntax && IsBufferType(declared))
                buffers.Add(local.Identifier.ValueText);
        }
        return buffers;
    }

    private static bool IsField(VariableDeclaratorSyntax declarator) =>
        declarator.Parent?.Parent is BaseFieldDeclarationSyntax;

    // Names a store may write without leaving the walk: locals, parameters, and pattern or loop variables.
    private static HashSet<string> Locals(SyntaxNode root)
    {
        var locals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var local in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            if (!IsField(local)) locals.Add(local.Identifier.ValueText);
        foreach (var parameter in root.DescendantNodesAndSelf().OfType<ParameterSyntax>())
            locals.Add(parameter.Identifier.ValueText);
        foreach (var loop in root.DescendantNodes().OfType<ForEachStatementSyntax>())
            locals.Add(loop.Identifier.ValueText);
        foreach (var single in root.DescendantNodes().OfType<SingleVariableDesignationSyntax>())
            locals.Add(single.Identifier.ValueText);
        foreach (var caught in root.DescendantNodes().OfType<CatchDeclarationSyntax>())
            locals.Add(caught.Identifier.ValueText);
        return locals;
    }

    private static bool StaysLocal(ExpressionSyntax left, HashSet<string> locals)
    {
        left = Peel(left);
        if (left is DeclarationExpressionSyntax or TupleExpressionSyntax) return true;
        if (left is IdentifierNameSyntax name) return locals.Contains(name.Identifier.ValueText);
        if (left is ElementAccessExpressionSyntax or MemberAccessExpressionSyntax)
            return RootIdentifier(left) is { } root && locals.Contains(root);
        return false;
    }

    private static string? RootIdentifier(ExpressionSyntax expression)
    {
        while (true)
        {
            expression = Peel(expression);
            switch (expression)
            {
                case IdentifierNameSyntax identifier:
                    return identifier.Identifier.ValueText;
                case MemberAccessExpressionSyntax member:
                    expression = member.Expression;
                    break;
                case ElementAccessExpressionSyntax element:
                    expression = element.Expression;
                    break;
                case ConditionalAccessExpressionSyntax conditional:
                    expression = conditional.Expression;
                    break;
                default:
                    return null;
            }
        }
    }

    // One name set for every walked root. A callee whose return carries taint is entered as
    // "()Name" so its call sites carry it; a tainted argument taints the callee parameter.
    private static HashSet<string> Tainted(List<SyntaxNode> roots, Dictionary<string, List<string[]>> callees)
    {
        var tainted = new HashSet<string>(StringComparer.Ordinal);
        for (var pass = 0; pass < 16; pass++)
        {
            var before = tainted.Count;
            foreach (var root in roots)
                TaintPass(root, tainted, callees);
            if (tainted.Count == before) break;
        }
        return tainted;
    }

    private static void TaintPass(SyntaxNode root, HashSet<string> tainted, Dictionary<string, List<string[]>> callees)
    {
        var buffers = Buffers(root);
        foreach (var local in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (local.Initializer is not null && Carries(local.Initializer.Value, tainted))
                tainted.Add(local.Identifier.ValueText);
        }
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (Carries(assignment.Right, tainted) && RootIdentifier(assignment.Left) is { } target)
                tainted.Add(target);
            TaintDeconstruction(assignment, tainted);
        }
        foreach (var loop in root.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            if (Carries(loop.Expression, tainted)) tainted.Add(loop.Identifier.ValueText);
        }
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var carried = call.ArgumentList.Arguments.Select(arg => ArgumentCarries(arg, tainted)).ToArray();
            if (!carried.Any(item => item)) continue;
            if (call.Expression is MemberAccessExpressionSyntax member
                && member.Expression is IdentifierNameSyntax receiver
                && buffers.Contains(receiver.Identifier.ValueText)
                && IsBufferMutator(member.Name.Identifier.ValueText))
                tainted.Add(receiver.Identifier.ValueText);
            foreach (var argument in call.ArgumentList.Arguments)
            {
                if (argument.RefKindKeyword.ValueText is not ("out" or "ref")) continue;
                foreach (var name in Designations(argument.Expression)) tainted.Add(name);
                if (Peel(argument.Expression) is IdentifierNameSyntax target) tainted.Add(target.Identifier.ValueText);
            }
            if (!IsCallee(call, callees)) continue;
            foreach (var parameters in callees[SimpleName(call)])
            {
                for (var index = 0; index < carried.Length; index++)
                {
                    if (!carried[index]) continue;
                    var named = call.ArgumentList.Arguments[index].NameColon?.Name.Identifier.ValueText;
                    if (named is not null) tainted.Add(named);
                    else if (index < parameters.Length) tainted.Add(parameters[index]);
                    else if (parameters.Length > 0) tainted.Add(parameters[^1]);
                }
            }
        }
        foreach (var method in root.DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>())
        {
            if (ReturnsTaint(method.ExpressionBody?.Expression, method.Body, tainted))
                tainted.Add("()" + method.Identifier.ValueText);
        }
        foreach (var local in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            if (ReturnsTaint(local.ExpressionBody?.Expression, local.Body, tainted))
                tainted.Add("()" + local.Identifier.ValueText);
        }
    }

    private static bool ReturnsTaint(ExpressionSyntax? expression, BlockSyntax? body, HashSet<string> tainted)
    {
        if (expression is not null) return ReturnCarries(expression, tainted);
        if (body is null) return false;
        return body.DescendantNodes().Any(node =>
            node is ReturnStatementSyntax { Expression: { } value } && ReturnCarries(value, tainted)
            || node is YieldStatementSyntax { Expression: { } yielded } && Carries(yielded, tainted));
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
        node.DescendantNodesAndSelf(inner => inner is not InvocationExpressionSyntax call || !IsSanitizing(call))
            .Any(inner => IsDirect(inner, tainted));

    private static bool DirectScreen(ExpressionSyntax expression, HashSet<string> tainted) =>
        IsDirect(Peel(expression), tainted);

    private static bool IsDirect(SyntaxNode node, HashSet<string> tainted) =>
        node is InvocationExpressionSyntax call && (IsScreenCall(call) || IsFolderPath(call) || ReturnsTainted(call, tainted))
        || node is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText is "RenderedScreen" or "UserProfile"
        || node is IdentifierNameSyntax identifier && (tainted.Contains(identifier.Identifier.ValueText)
            || identifier.Identifier.ValueText == "UserProfile");

    private static bool ReturnsTainted(InvocationExpressionSyntax call, HashSet<string> tainted) =>
        call.Expression is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax }
        && tainted.Contains("()" + SimpleName(call));

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
