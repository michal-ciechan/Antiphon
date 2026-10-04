using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Checkpoints.Coverage;

public sealed record IndexedAssertion(string Path, int Line, string Name, IReadOnlyList<string> Members,
    IReadOnlyList<string> Expected, IReadOnlyList<string> Messages, IReadOnlyList<string> Canaries, bool Empty, bool Null);
public sealed record IndexedMethod(string Class, string Name, string Path, MethodDeclarationSyntax Syntax)
{
    public string Qualified => Class + "." + Name;
}
internal sealed record IndexedClass(string Class, string Namespace, string Path, ClassDeclarationSyntax Syntax);

/// <summary>Bounded syntax evidence. No semantic model, loading or execution of selected code.</summary>
public sealed class TestAssertionIndex
{
    private sealed record Callable(string Class, string Path, SyntaxNode Syntax, ParameterListSyntax Parameters, SyntaxNode? Body)
    {
        public static Callable From(IndexedMethod method) => new(method.Class, method.Path, method.Syntax,
            method.Syntax.ParameterList, (SyntaxNode?)method.Syntax.Body ?? method.Syntax.ExpressionBody);
        public Callable Local(LocalFunctionStatementSyntax local) => new(Class, Path, local,
            local.ParameterList, (SyntaxNode?)local.Body ?? local.ExpressionBody);
    }
    private readonly List<(string Path, CompilationUnitSyntax Root)> _trees = [];
    public List<IndexedMethod> Methods { get; } = [];
    internal List<IndexedClass> Declarations { get; } = [];
    public List<CoverageDiagnostic> Diagnostics { get; } = [];
    public TestAssertionIndex(IReadOnlyList<CoverageSource> sources)
    {
        foreach (var source in sources)
        {
            var tree = CSharpSyntaxTree.ParseText(source.Text);
            if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
            { Diagnostics.Add(new("SOURCE_INVALID", TestPath: source.Path, Detail: "invalid C# syntax")); continue; }
            var root = tree.GetCompilationUnitRoot(); _trees.Add((source.Path, root));
            foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                Declarations.Add(new(ClassName(declaration), string.Join('.', declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString())), source.Path, declaration));
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                Methods.Add(new(ClassName(method), method.Identifier.ValueText, source.Path, method));
        }
    }
    public IReadOnlyList<string> Classes(string path) => _trees.Where(t => t.Path == path).SelectMany(t => t.Root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        .Select(c => ClassName(c)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static string ClassName(SyntaxNode node)
    {
        var classes = node.AncestorsAndSelf().OfType<ClassDeclarationSyntax>().Reverse().Select(c => c.Identifier.ValueText);
        var ns = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString());
        return string.Join('.', ns.Concat(classes));
    }
    public IReadOnlyList<IndexedMethod> Resolve(string name)
    {
        return Methods.Where(m => m.Qualified == name || m.Qualified.EndsWith("." + name, StringComparison.Ordinal) || !name.Contains('.') && m.Name == name).ToArray();
    }
    public List<IndexedAssertion> Assertions(IndexedMethod method, List<CoverageDiagnostic> unknown)
    {
        var result = new List<IndexedAssertion>();
        Expand(Callable.From(method), new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal), new HashSet<SyntaxNode>(), result, unknown, 0);
        return result;
    }
    private void Expand(Callable method, Dictionary<string, ExpressionSyntax> parameters, HashSet<SyntaxNode> stack,
        List<IndexedAssertion> assertions, List<CoverageDiagnostic> unknown, int depth)
    {
        if (depth > 12 || !stack.Add(method.Syntax)) { unknown.Add(new("HELPER_UNMAPPED", TestPath: method.Path, TestLine: Line(method.Syntax), Detail: "cyclic or deep helper")); return; }
        try
        {
            var nodes = method.Body?.DescendantNodesAndSelf(n => n is not LocalFunctionStatementSyntax) ?? [];
            foreach (var call in nodes.OfType<InvocationExpressionSyntax>().OrderBy(c => c.Span.End))
            {
                var name = CallName(call); var arguments = call.ArgumentList.Arguments;
                var receiver = (call.Expression as MemberAccessExpressionSyntax)?.Expression;
                var isThrow = name is "Throw" or "ThrowAsync" && receiver?.ToString() == "Should";
                var isAssertion = name.StartsWith("Should", StringComparison.Ordinal) || isThrow;
                if (isAssertion)
                {
                    var expectedPosition = name is "ShouldBe" or "ShouldNotBe" or "ShouldContain" or "ShouldNotContain" or "ShouldBeGreaterThan" or "ShouldBeLessThan" or "ShouldBeGreaterThanOrEqualTo" or "ShouldBeLessThanOrEqualTo" or "ShouldStartWith" or "ShouldEndWith" or "ShouldAllBe";
                    var supported = expectedPosition || isThrow || name is "ShouldBeTrue" or "ShouldBeFalse" or "ShouldBeNull" or "ShouldNotBeNull" or "ShouldBeEmpty" or "ShouldNotBeEmpty" or "ShouldHaveSingleItem" or "ShouldBeOfType" or "ShouldBeAssignableTo";
                    if (!supported) { unknown.Add(new("ASSERTION_UNMAPPED", TestPath: method.Path, TestLine: Line(call), Name: name, Detail: "unsupported assertion signature")); continue; }
                    var actual = receiver;
                    var expected = expectedPosition && arguments.Count > 0 ? arguments[0].Expression : null;
                    var messageIndex = isThrow || expectedPosition ? 1 : 0;
                    if (arguments.Count > messageIndex && arguments[messageIndex].Expression.ToString() is "Case.Sensitive" or "Case.Insensitive") messageIndex++;
                    var message = arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText is "customMessage" or "message")?.Expression
                        ?? (arguments.Count > messageIndex ? arguments[messageIndex].Expression : null);
                    var members = actual is null ? [] : Members(actual, method.Syntax, call, parameters, new HashSet<string>(), 0);
                    var values = expected is null ? [] : Values(expected, method.Syntax, call, parameters, new HashSet<string>(), 0);
                    var messages = message is null ? [] : Literals(message, method.Syntax, call, parameters, new HashSet<string>(), 0);
                    var canaries = name == "ShouldNotContain" && expected is not null ? Literals(expected, method.Syntax, call, parameters, new HashSet<string>(), 0) : [];
                    var empty = name == "ShouldBeEmpty" || name == "ShouldBe" && expected is not null && IsEmpty(expected)
                        || name == "ShouldBe" && values.Contains("0") && actual is MemberAccessExpressionSyntax a && a.Name.Identifier.ValueText is "Count" or "Length";
                    var isNull = name == "ShouldBeNull" || name == "ShouldBe" && expected is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.NullLiteralExpression);
                    assertions.Add(new(method.Path, Line((call.Expression as MemberAccessExpressionSyntax)?.Name ?? call.Expression), name, members, values, messages, canaries, empty, isNull));
                    continue;
                }
                // Local names shadow class methods, including when their argument shape is unsupported.
                var locals = call.Expression is SimpleNameSyntax ? LocalCandidates(call, name) : [];
                var candidates = locals.Count > 0 ? locals.Select(method.Local)
                    : Methods.Where(m => m.Name == name && OuterClass(m.Syntax) == OuterClass(method.Syntax)).Select(Callable.From);
                var helpers = candidates.Where(h => Bind(h, method, call, parameters) is not null).ToArray();
                if (helpers.Length == 1 && (locals.Count > 0 || HasAssertions(helpers[0]) || name.StartsWith("Assert", StringComparison.Ordinal)))
                {
                    var helper = helpers[0]; var bindings = Bind(helper, method, call, parameters)!;
                    Expand(helper, bindings, stack, assertions, unknown, depth + 1);
                }
                else if (name.StartsWith("Assert", StringComparison.Ordinal)) unknown.Add(new("HELPER_UNMAPPED", TestPath: method.Path, TestLine: Line(call), Name: name, Detail: "unresolved or ambiguous assertion helper"));
            }
        }
        finally { stack.Remove(method.Syntax); }
    }
    private static string OuterClass(SyntaxNode node) => node.Ancestors().OfType<ClassDeclarationSyntax>().LastOrDefault() is ClassDeclarationSyntax outer ? ClassName(outer) : "";
    private static List<LocalFunctionStatementSyntax> LocalCandidates(SyntaxNode site, string name)
    {
        foreach (var scope in site.Ancestors())
        {
            if (scope is BlockSyntax block)
            {
                var found = block.Statements.OfType<LocalFunctionStatementSyntax>().Where(l => l.Identifier.ValueText == name).ToList();
                if (found.Count > 0) return found;
            }
            if (scope is MethodDeclarationSyntax) break;
        }
        return [];
    }
    private static IEnumerable<VariableDeclaratorSyntax> VisibleVariables(SyntaxNode site)
    {
        foreach (var scope in site.Ancestors())
        {
            if (scope is BlockSyntax block)
                foreach (var variable in block.Statements.OfType<LocalDeclarationStatementSyntax>()
                    .SelectMany(s => s.Declaration.Variables).Where(v => v.SpanStart < site.SpanStart).Reverse())
                    yield return variable;
            if (scope is MethodDeclarationSyntax or LocalFunctionStatementSyntax) yield break;
        }
    }
    private Dictionary<string, ExpressionSyntax>? Bind(Callable helper, Callable caller, InvocationExpressionSyntax call,
        Dictionary<string, ExpressionSyntax> inherited)
    {
        var parameters = helper.Parameters.Parameters;
        var assigned = new Dictionary<string, List<ExpressionSyntax>>(StringComparer.Ordinal);
        var position = 0;
        foreach (var arg in call.ArgumentList.Arguments)
        {
            if (arg.RefKindKeyword.RawKind != 0) return null;
            var index = arg.NameColon is null ? position : parameters.IndexOf(parameters.FirstOrDefault(p => p.Identifier.ValueText == arg.NameColon.Name.Identifier.ValueText)!);
            if (index < 0 || index >= parameters.Count) return null;
            var parameter = parameters[index]; var name = parameter.Identifier.ValueText;
            var isParams = parameter.Modifiers.Any(SyntaxKind.ParamsKeyword) && index == parameters.Count - 1;
            if (assigned.ContainsKey(name) && (!isParams || arg.NameColon is not null)) return null;
            if (!assigned.TryGetValue(name, out var values)) assigned[name] = values = [];
            values.Add(arg.Expression);
            if (arg.NameColon is null && !isParams) position++;
            else if (arg.NameColon is not null && index == position) position++;
        }
        var bindings = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        if (helper.Syntax is LocalFunctionStatementSyntax)
        {
            foreach (var pair in inherited) bindings[pair.Key] = pair.Value;
            foreach (var variable in VisibleVariables(call).DistinctBy(v => v.Identifier.ValueText))
            {
                var literals = variable.Initializer is null ? [] : Literals(variable.Initializer.Value, caller.Syntax, call, inherited, [], 0);
                bindings[variable.Identifier.ValueText] = literals.Count > 0 ? SyntheticLiterals(literals) : SyntaxFactory.IdentifierName("__unknown_capture");
            }
        }
        foreach (var parameter in parameters)
        {
            var name = parameter.Identifier.ValueText;
            var isParams = parameter.Modifiers.Any(SyntaxKind.ParamsKeyword) && parameter == parameters.Last();
            if (!assigned.TryGetValue(name, out var args))
            {
                if (parameter.Default is not null) args = [parameter.Default.Value];
                else if (isParams) args = [];
                else return null;
            }
            var literals = args.SelectMany(a => Literals(a, caller.Syntax, call, inherited, [], 0)).ToList();
            bindings[name] = literals.Count > 0 || isParams ? SyntheticLiterals(literals)
                : args.Count == 1 ? args[0] : SyntaxFactory.IdentifierName("__unknown_argument");
        }
        return bindings;
    }
    private static bool HasAssertions(Callable method) => (method.Body?.DescendantNodesAndSelf(n => n is not LocalFunctionStatementSyntax) ?? []).OfType<InvocationExpressionSyntax>()
        .Any(c => CallName(c).StartsWith("Should", StringComparison.Ordinal) || CallName(c) is "Throw" or "ThrowAsync" || CallName(c).StartsWith("Assert", StringComparison.Ordinal));
    private static string CallName(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        SimpleNameSyntax n => n.Identifier.ValueText,
        _ => ""
    };
    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    private static ExpressionSyntax SyntheticLiterals(IReadOnlyList<string> literals) => SyntaxFactory.ParseExpression("new[] { " + string.Join(", ", literals.Select(s => System.Text.Json.JsonSerializer.Serialize(s))) + " }");
    private ExpressionSyntax? Alias(string name, SyntaxNode method, SyntaxNode site, Dictionary<string, ExpressionSyntax> parameters)
    {
        var loop = site.Ancestors().OfType<ForEachStatementSyntax>().FirstOrDefault(f => f.Identifier.ValueText == name);
        if (loop is not null) return loop.Expression;
        var local = VisibleVariables(site).FirstOrDefault(v => v.Identifier.ValueText == name);
        if (local is not null) return local.Initializer?.Value;
        if (parameters.TryGetValue(name, out var bound)) return bound;
        var declared = method switch { MethodDeclarationSyntax m => m.ParameterList, LocalFunctionStatementSyntax l => l.ParameterList, _ => null };
        if (declared?.Parameters.Any(p => p.Identifier.ValueText == name) == true) return null;
        // Only literal const fields in the enclosing/partial class are traced.
        return _trees.SelectMany(t => t.Root.DescendantNodes().OfType<FieldDeclarationSyntax>()).Where(f => f.Modifiers.Any(SyntaxKind.ConstKeyword)
            && OuterClass(f) == OuterClass(method)).SelectMany(f => f.Declaration.Variables).FirstOrDefault(v => v.Identifier.ValueText == name)?.Initializer?.Value;
    }
    private List<string> Members(ExpressionSyntax expression, SyntaxNode method, SyntaxNode site, Dictionary<string, ExpressionSyntax> parameters, HashSet<string> seen, int depth)
    {
        var result = expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Select(m => m.Name.Identifier.ValueText).ToList();
        if (depth > 8) return result;
        foreach (var id in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Where(i => i.Parent is not MemberAccessExpressionSyntax m || m.Expression == i))
        {
            if (!seen.Add(id.Identifier.ValueText)) continue;
            var alias = Alias(id.Identifier.ValueText, method, site, parameters);
            if (alias is not null) result.AddRange(Members(alias, method, site, parameters, seen, depth + 1));
        }
        return result.Distinct(StringComparer.Ordinal).ToList();
    }
    private List<string> Values(ExpressionSyntax expression, SyntaxNode method, SyntaxNode site, Dictionary<string, ExpressionSyntax> parameters, HashSet<string> seen, int depth)
    {
        var result = Literals(expression, method, site, parameters, seen, depth);
        result.AddRange(expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>().Select(m => m.Name.Identifier.ValueText));
        result.AddRange(expression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>().Where(l => !l.IsKind(SyntaxKind.StringLiteralExpression)).Select(l => l.Token.ValueText));
        return result.Distinct(StringComparer.Ordinal).ToList();
    }
    private List<string> Literals(ExpressionSyntax expression, SyntaxNode method, SyntaxNode site, Dictionary<string, ExpressionSyntax> parameters, HashSet<string> seen, int depth)
    {
        if (depth > 8) return [];
        switch (expression)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression): return [literal.Token.ValueText];
            case IdentifierNameSyntax id:
                if (!seen.Add(id.Identifier.ValueText)) return [];
                var alias = Alias(id.Identifier.ValueText, method, site, parameters);
                return alias is null ? [] : Literals(alias, method, site, parameters, seen, depth + 1);
            case InterpolatedStringExpressionSyntax interpolation:
                // A dynamic interpolation breaks the string. Preserve each literal segment only.
                return interpolation.Contents.OfType<InterpolatedStringTextSyntax>().Select(t => t.TextToken.ValueText).Where(t => t.Length > 0).ToList();
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                var left = Literals(binary.Left, method, site, parameters, new(seen), depth + 1);
                var right = Literals(binary.Right, method, site, parameters, new(seen), depth + 1);
                return left.Count > 0 && right.Count > 0 ? left.SelectMany(l => right.Select(r => l + r)).ToList() : left.Concat(right).ToList();
            case ConditionalExpressionSyntax conditional:
                return Literals(conditional.WhenTrue, method, site, parameters, new(seen), depth + 1).Concat(Literals(conditional.WhenFalse, method, site, parameters, new(seen), depth + 1)).ToList();
            case ParenthesizedExpressionSyntax parenthesized: return Literals(parenthesized.Expression, method, site, parameters, seen, depth + 1);
            case ArrayCreationExpressionSyntax array when array.Initializer is not null:
                return array.Initializer.Expressions.SelectMany(e => Literals(e, method, site, parameters, new(seen), depth + 1)).ToList();
            case ImplicitArrayCreationExpressionSyntax array:
                return array.Initializer.Expressions.SelectMany(e => Literals(e, method, site, parameters, new(seen), depth + 1)).ToList();
            case CollectionExpressionSyntax collection:
                return collection.Elements.OfType<ExpressionElementSyntax>().SelectMany(e => Literals(e.Expression, method, site, parameters, new(seen), depth + 1)).ToList();
            default: return [];
        }
    }
    private static bool IsEmpty(ExpressionSyntax expression) => expression is CollectionExpressionSyntax c && c.Elements.Count == 0
        || expression is ArrayCreationExpressionSyntax a && a.Initializer?.Expressions.Count == 0
        || expression is ImplicitArrayCreationExpressionSyntax i && i.Initializer.Expressions.Count == 0
        || expression is InvocationExpressionSyntax call && call.Expression is MemberAccessExpressionSyntax m && m.Expression.ToString() is "Array" or "Enumerable" && m.Name.Identifier.ValueText == "Empty";
    public static bool HasLabel(IndexedAssertion assertion, string label) => assertion.Messages.Any(message => message == label
        || message.StartsWith(label + " ", StringComparison.Ordinal) || message.StartsWith(label + ":", StringComparison.Ordinal));
    public static IReadOnlyList<string> Labels(IndexedAssertion assertion) => assertion.Messages.Select(m => m.Trim()).Where(m => m.Length > 0)
        .Select(m => m.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0]).Distinct(StringComparer.Ordinal).ToArray();
}
