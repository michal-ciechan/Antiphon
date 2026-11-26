using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Tests.Infrastructure;

internal sealed class RepositoryInvariantAudit
{
    private readonly HashSet<string> _tracked;
    private readonly ILookup<string, TypeDeclarationSyntax> _declarations;
    private readonly ILookup<(string Project, string Name), TypeDeclarationSyntax> _shortNames;

    private RepositoryInvariantAudit(string root, string[] tracked)
    {
        _tracked = tracked.ToHashSet(StringComparer.Ordinal);
        if (!_tracked.Contains("Antiphon.sln"))
            throw new InvalidDataException("The tracked checkout has no Antiphon.sln: " + root);

        _declarations = tracked
            .Where(path => path.StartsWith("tests/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal))
            .SelectMany(path =>
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root, path)),
                    new CSharpParseOptions(LanguageVersion.Preview), path);
                var error = tree.GetDiagnostics().FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                if (error is not null)
                    throw new InvalidDataException($"Cannot examine {path}: {error}");
                return tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>();
            })
            .ToLookup(TypeKey, StringComparer.Ordinal);
        if (_declarations.Count == 0)
            throw new InvalidDataException("The tracked checkout has no test source declarations: " + root);
        _shortNames = _declarations.SelectMany(type => type)
            .ToLookup(type => (Project(type), type.Identifier.ValueText));
    }

    internal static IReadOnlyList<Reader> Scan(string root)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("Cannot examine checkout " + root);
        using var checkout = new LibGit2Sharp.Repository(root);
        return new RepositoryInvariantAudit(root, checkout.Index.Select(entry => entry.Path).ToArray()).Readers();
    }

    internal sealed record Reader(string Path, int Line, string Type, string Method, string Input,
        bool InNamespace, bool HasCategory)
    {
        public bool Designated => InNamespace && HasCategory;

        public override string ToString() =>
            $"{Path}:{Line} {Type}.{Method} -> {Input}; required .Invariants namespace and RepositoryInvariant category";
    }

    private IReadOnlyList<Reader> Readers()
    {
        var readers = new List<Reader>();
        foreach (var type in _declarations)
        {
            var ns = Namespace(type.First());
            var designatedNamespace = ns.Split('.').Last() == "Invariants";
            var category = type.SelectMany(part => part.AttributeLists).SelectMany(list => list.Attributes)
                .Any(attribute => attribute.Name.ToString() is "Category" or "CategoryAttribute"
                    && attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
                    && literal.Token.ValueText == "RepositoryInvariant");
            foreach (var method in type.SelectMany(part => part.Members).OfType<MethodDeclarationSyntax>()
                         .Where(method => method.AttributeLists.SelectMany(list => list.Attributes)
                             .Any(attribute => attribute.Name.ToString() is "Test" or "TestAttribute")))
            {
                var assertions = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Where(call => call.Expression is MemberAccessExpressionSyntax member
                        && member.Name.Identifier.ValueText.StartsWith("Should", StringComparison.Ordinal));
                var scope = Scope(method);
                foreach (var assertion in assertions)
                {
                    var member = (MemberAccessExpressionSyntax)assertion.Expression;
                    var inputs = member.Expression is IdentifierNameSyntax { Identifier.ValueText: "Should" }
                        ? assertion.ArgumentList.Arguments.Select(argument => argument.Expression)
                        : new[] { member.Expression }.Concat(assertion.ArgumentList.Arguments.Select(argument => argument.Expression));
                    foreach (var input in inputs.SelectMany(expression => expression.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
                                 .Select(expression => Resolve(expression, scope, [])).OfType<Input>()
                                 .Where(origin => origin.Read))
                    {
                        readers.Add(new Reader(method.SyntaxTree.FilePath, method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            type.Key, method.Identifier.ValueText, input.Trace, designatedNamespace, category));
                    }
                }
            }
        }

        return readers.Distinct().OrderBy(reader => reader.Path, StringComparer.Ordinal).ThenBy(reader => reader.Line).ToArray();
    }

    private sealed record Input(string Path, string Trace, bool Read = false, bool Checkout = true);

    private static Dictionary<string, ExpressionSyntax> Scope(MethodDeclarationSyntax method) =>
        method.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Initializer is not null)
            .GroupBy(variable => variable.Identifier.ValueText)
            .Where(variables => variables.Count() == 1)
            .ToDictionary(variables => variables.Key, variables => variables.Single().Initializer!.Value, StringComparer.Ordinal);

    private Input? Resolve(ExpressionSyntax expression, IReadOnlyDictionary<string, ExpressionSyntax> scope, HashSet<SyntaxNode> visiting)
    {
        if (!visiting.Add(expression))
            return null;
        try
        {
            return ResolveExpression(expression, scope, visiting);
        }
        finally
        {
            visiting.Remove(expression);
        }
    }

    private Input? ResolveExpression(ExpressionSyntax expression, IReadOnlyDictionary<string, ExpressionSyntax> scope, HashSet<SyntaxNode> visiting)
    {
        if (expression is IdentifierNameSyntax id && scope.TryGetValue(id.Identifier.ValueText, out var initializer))
            return Resolve(initializer, scope, visiting);
        if (expression is MemberAccessExpressionSyntax property)
        {
            if (property.ToString() == "DockerStackDocuments.RepoRoot")
                return new Input("", property.ToString());
            return Resolve(property.Expression, scope, visiting);
        }
        if (expression is not InvocationExpressionSyntax call)
            return null;

        var target = call.Expression.ToString();
        var args = call.ArgumentList.Arguments.Select(argument => argument.Expression).ToArray();
        if (target == "LiveLaneProcess.FindRepo")
            return new Input("", target + "()");
        if (target == "OwnedTempRoot.CreateOwned")
            return new Input("", target + "()", Checkout: false);
        if (target is "LiveLaneGateScan.Matrix" or "Antiphon.Nightly.LiveLaneGateScan.Matrix"
            or "LiveLaneGateScan.Scan" or "Antiphon.Nightly.LiveLaneGateScan.Scan"
            or "LiveLaneGateScan.RcExclusions" or "Antiphon.Nightly.LiveLaneGateScan.RcExclusions")
            return args.Length != 0 && Resolve(args[0], scope, visiting) is { Path: "", Checkout: true } liveRoot
                ? new Input("tests/", $"{liveRoot.Trace} -> {target} (tracked tests and live-lane declarations)", Read: true)
                : null;
        if (target == "Path.Combine" && args.Length > 1 && Resolve(args[0], scope, visiting) is { Read: false } root
            && args.Skip(1).All(arg => arg is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
                && !Path.IsPathRooted(literal.Token.ValueText)))
        {
            var parts = args.Skip(1).Cast<LiteralExpressionSyntax>().Select(literal => literal.Token.ValueText.Replace('\\', '/'));
            return root with
            {
                Path = string.Join('/', parts.Prepend(root.Path).Where(part => part.Length != 0)),
                Trace = root.Trace + " -> " + call,
            };
        }
        if (target is "File.ReadAllText" or "File.ReadAllBytes" or "File.ReadAllLines" or "PlanTableImporter.ImportFile"
            && args.Length != 0 && Resolve(args[0], scope, visiting) is { Read: false, Checkout: true } path && _tracked.Contains(path.Path))
            return path with { Trace = $"{path.Trace} -> {target} [{path.Path}]", Read = true };

        if (call.Expression is MemberAccessExpressionSyntax access && Resolve(access.Expression, scope, visiting) is { Read: true } read)
            return read;

        var containing = call.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (containing is null)
            return null;
        var declaring = call.Expression is IdentifierNameSyntax
            ? _declarations[TypeKey(containing)]
            : _shortNames[(Project(containing), target[..Math.Max(0, target.LastIndexOf('.'))])];
        var methodName = target.Split('.').Last();
        var helpers = declaring.SelectMany(type => type.Members).OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == methodName && method.ParameterList.Parameters.Count == args.Length)
            .Take(2).ToArray();
        if (helpers.Length != 1)
            return null;
        var helper = helpers[0];
        ExpressionSyntax?[] returns = helper.ExpressionBody is { } body ? [body.Expression]
            : helper.Body?.Statements.OfType<ReturnStatementSyntax>().Select(statement => statement.Expression).ToArray() ?? [];
        if (returns.Length != 1 || returns[0] is not { } result)
            return null;
        var helperScope = Scope(helper);
        for (var i = 0; i < args.Length; i++)
        {
            var value = args[i];
            if (value is IdentifierNameSyntax name && scope.TryGetValue(name.Identifier.ValueText, out var local))
                value = local;
            helperScope[helper.ParameterList.Parameters[i].Identifier.ValueText] = value;
        }
        return Resolve(result, helperScope, visiting) is { } origin
            ? origin with { Trace = helper.Identifier.ValueText + " -> " + origin.Trace }
            : null;
    }

    private static string Project(TypeDeclarationSyntax type) => type.SyntaxTree.FilePath.Split('/')[1];

    private static string Namespace(TypeDeclarationSyntax type) => string.Join('.',
        type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(ns => ns.Name.ToString()));

    private static string TypeKey(TypeDeclarationSyntax type) => Project(type) + "/" + Namespace(type) + "." + string.Join('.',
        type.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().Reverse().Select(part => part.Identifier.ValueText));
}
