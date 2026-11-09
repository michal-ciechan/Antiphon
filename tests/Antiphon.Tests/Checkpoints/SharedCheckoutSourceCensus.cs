using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CARD-1810 / CARD-1816. Names every class in the Checkpoints namespace whose source
/// assigns <c>CheckpointFixtures.RepoRoot</c> to a <c>ProcessStartInfo</c> working directory.
/// A text scan is the wrong tool: tool-lane classes read <c>RepoRoot</c> as a path and
/// assign it to other types' <c>WorkingDirectory</c> (a scheduler request, a ledger)
/// without starting a child in the shared checkout. The walk stays on the object
/// creation and on a simple assignment whose left side is <c>WorkingDirectory</c> on a
/// <c>ProcessStartInfo</c> (a local, field, property, or parameter declared or created
/// as that type) or on a <c>.StartInfo</c> member. A call to a method that does that
/// assignment counts the caller, and so does a call that passes <c>RepoRoot</c> into a
/// parameter which that method assigns to the working directory. Target-typed <c>new</c>
/// takes its type from the declared variable, property, method, or local function, still
/// without a compilation. A local or field whose initializer is exactly
/// <c>CheckpointFixtures.RepoRoot</c> counts; a further copy does not.
/// A <c>using static</c> bare name, a ternary, and <c>Path.GetFullPath</c> do not count.
/// The live census reads the Checkpoints folder and any sibling file under the test
/// project that declares this namespace, so a class outside the folder is still named.
/// </summary>
internal static class SharedCheckoutSourceCensus
{
    private const string CheckpointsNamespace = "Antiphon.Tests.Checkpoints";

    internal static IReadOnlyList<string> ClassesStartingInSharedCheckout(string directory)
    {
        var units = new List<CompilationUnitSyntax>();
        foreach (var path in CensusFiles(directory))
            units.Add(Parse(File.ReadAllText(path)).GetCompilationUnitRoot());

        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in ClassesInRoots(units))
        {
            if (IsCheckpointsCensusName(name))
                found.Add(name);
        }

        return found.ToList();
    }

    internal static IReadOnlyList<string> ClassesIn(string source) =>
        ClassesInRoots([Parse(source).GetCompilationUnitRoot()]);

    private static SyntaxTree Parse(string source) =>
        CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));

    private static IReadOnlyList<string> ClassesInRoots(IReadOnlyList<CompilationUnitSyntax> units)
    {
        var types = units
            .SelectMany(unit => unit.DescendantNodes().OfType<TypeDeclarationSyntax>())
            .Where(IsClassLike)
            .ToList();
        var helpers = new Dictionary<(string Type, string Method), List<HelperShape>>();
        foreach (var type in types)
            CollectHelperShapes(type, helpers);

        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (StartsSharedCheckoutChild(type) || CallsHelper(type, helpers))
                found.Add(ClrName(type));
        }

        return found.ToList();
    }

    private static IEnumerable<string> CensusFiles(string directory)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in EnumerateCs(directory))
        {
            if (seen.Add(path))
                yield return path;
        }

        var trimmed = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(trimmed), "Checkpoints", StringComparison.Ordinal))
            yield break;
        var project = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(project))
            yield break;

        foreach (var path in EnumerateCs(project))
        {
            if (seen.Contains(path))
                continue;
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (text.Contains("namespace Antiphon.Tests.Checkpoints", StringComparison.Ordinal) && seen.Add(path))
                yield return path;
        }
    }

    private static IEnumerable<string> EnumerateCs(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (name is "bin" or "obj")
                    continue;
                pending.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
                yield return file;
        }
    }

    private static bool IsCheckpointsCensusName(string clrName)
    {
        var plus = clrName.IndexOf('+', StringComparison.Ordinal);
        var typePath = plus >= 0 ? clrName[..plus] : clrName;
        var dot = typePath.LastIndexOf('.');
        if (dot <= 0)
            return false;
        var ns = typePath[..dot];
        return ns == CheckpointsNamespace
            || ns.StartsWith(CheckpointsNamespace + ".", StringComparison.Ordinal);
    }

    private static bool IsClassLike(TypeDeclarationSyntax type)
    {
        if (type is RecordDeclarationSyntax record)
            return !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);
        return type is ClassDeclarationSyntax;
    }

    private static bool StartsSharedCheckoutChild(TypeDeclarationSyntax type) =>
        ScopeStartsSharedCheckout(type, type);

    private static bool ScopeStartsSharedCheckout(SyntaxNode scope, TypeDeclarationSyntax type)
    {
        var classAliases = ClassAliases(type);
        foreach (var creation in CreationsIn(scope))
        {
            if (!IsProcessStartInfo(creation) || creation.Initializer is null)
                continue;
            foreach (var expression in creation.Initializer.Expressions)
            {
                if (expression is AssignmentExpressionSyntax assignment
                    && assignment.Left is IdentifierNameSyntax { Identifier.Text: "WorkingDirectory" }
                    && ReferencesSharedRoot(assignment.Right, classAliases, creation, type))
                    return true;
            }
        }

        var classPsi = ClassProcessStartInfoNames(type);
        foreach (var assignment in AssignmentsIn(scope))
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                continue;
            if (assignment.Left is not MemberAccessExpressionSyntax { Name.Identifier.Text: "WorkingDirectory" } left)
                continue;
            if (!ReceiverIsProcessStartInfo(left.Expression, classPsi, assignment, type))
                continue;
            if (ReferencesSharedRoot(assignment.Right, classAliases, assignment, type))
                return true;
        }

        return false;
    }

    private sealed class HelperShape
    {
        internal bool AnyCall { get; init; }
        internal string[] ParameterNames { get; init; } = [];
        internal HashSet<int> ThreadedIndexes { get; init; } = new();
    }

    private static void CollectHelperShapes(
        TypeDeclarationSyntax type,
        Dictionary<(string Type, string Method), List<HelperShape>> helpers)
    {
        var typeName = type.Identifier.Text;
        foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
        {
            HelperShape? shape = null;
            if (ScopeStartsSharedCheckout(method, type))
            {
                shape = new HelperShape { AnyCall = true };
            }
            else
            {
                var threaded = ThreadedParameterIndexes(method, type);
                if (threaded.Count > 0)
                {
                    var names = new string[method.ParameterList.Parameters.Count];
                    for (var i = 0; i < names.Length; i++)
                        names[i] = method.ParameterList.Parameters[i].Identifier.Text;
                    shape = new HelperShape { ParameterNames = names, ThreadedIndexes = threaded };
                }
            }

            if (shape is null)
                continue;
            var key = (typeName, method.Identifier.Text);
            if (!helpers.TryGetValue(key, out var list))
            {
                list = [];
                helpers.Add(key, list);
            }

            list.Add(shape);
        }
    }

    private static HashSet<int> ThreadedParameterIndexes(MethodDeclarationSyntax method, TypeDeclarationSyntax type)
    {
        var indexes = new HashSet<int>();
        var parameters = method.ParameterList.Parameters;
        if (parameters.Count == 0)
            return indexes;
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
            byName[parameters[i].Identifier.Text] = i;

        var classPsi = ClassProcessStartInfoNames(type);
        foreach (var assignment in AssignmentsIn(method))
        {
            if (!WorkingDirectoryOfProcessStart(assignment, classPsi, type, out var right))
                continue;
            if (Strip(right) is IdentifierNameSyntax identifier && byName.TryGetValue(identifier.Identifier.Text, out var index))
                indexes.Add(index);
        }

        return indexes;
    }

    private static bool CallsHelper(
        TypeDeclarationSyntax type,
        Dictionary<(string Type, string Method), List<HelperShape>> helpers)
    {
        foreach (var invocation in type.DescendantNodes(node => node == type || node is not BaseTypeDeclarationSyntax)
                     .OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access)
                continue;
            var typeName = SimpleReceiverName(access.Expression);
            if (typeName is null)
                continue;
            if (!helpers.TryGetValue((typeName, access.Name.Identifier.Text), out var shapes))
                continue;
            foreach (var shape in shapes)
            {
                if (shape.AnyCall || ArgumentsThreadRoot(invocation, shape, type))
                    return true;
            }
        }

        return false;
    }

    private static bool ArgumentsThreadRoot(InvocationExpressionSyntax invocation, HelperShape shape, TypeDeclarationSyntax caller)
    {
        var args = invocation.ArgumentList.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            var argument = args[i];
            var index = i;
            if (argument.NameColon is { } colon)
            {
                index = Array.IndexOf(shape.ParameterNames, colon.Name.Identifier.Text);
                if (index < 0)
                    continue;
            }

            if (!shape.ThreadedIndexes.Contains(index))
                continue;
            if (ReferencesSharedRoot(argument.Expression, ClassAliases(caller), invocation, caller))
                return true;
        }

        return false;
    }

    private static string? SimpleReceiverName(ExpressionSyntax expression)
    {
        expression = Strip(expression);
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            _ => null,
        };
    }

    private static bool WorkingDirectoryOfProcessStart(
        AssignmentExpressionSyntax assignment,
        HashSet<string> classPsi,
        TypeDeclarationSyntax type,
        out ExpressionSyntax right)
    {
        right = assignment.Right;
        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return false;
        if (assignment.Left is IdentifierNameSyntax { Identifier.Text: "WorkingDirectory" }
            && assignment.Parent is InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax creation })
            return IsProcessStartInfo(creation);
        if (assignment.Left is MemberAccessExpressionSyntax { Name.Identifier.Text: "WorkingDirectory" } left)
            return ReceiverIsProcessStartInfo(left.Expression, classPsi, assignment, type);
        return false;
    }

    private static bool ReceiverIsProcessStartInfo(
        ExpressionSyntax receiver, HashSet<string> classPsi, SyntaxNode site, TypeDeclarationSyntax type)
    {
        receiver = Strip(receiver);
        if (receiver is MemberAccessExpressionSyntax { Name.Identifier.Text: "StartInfo" })
            return true;
        if (receiver is not IdentifierNameSyntax identifier)
            return false;
        var name = identifier.Identifier.Text;
        return classPsi.Contains(name) || MemberProcessStartInfoNames(site, type).Contains(name);
    }

    private static HashSet<string> ClassProcessStartInfoNames(TypeDeclarationSyntax type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
            CollectDeclared(field.Declaration, names);
        foreach (var property in type.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (TypeNameIs(property.Type, "ProcessStartInfo"))
                names.Add(property.Identifier.Text);
        }

        return names;
    }

    private static HashSet<string> MemberProcessStartInfoNames(SyntaxNode site, TypeDeclarationSyntax type)
    {
        SyntaxNode? member = site;
        while (member is not null && member.Parent != type)
            member = member.Parent;
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (member is null)
            return names;
        foreach (var declaration in member.DescendantNodes(node => node == member || node is not BaseTypeDeclarationSyntax)
                     .OfType<VariableDeclarationSyntax>())
            CollectDeclared(declaration, names);
        foreach (var parameter in member.DescendantNodes(node => node == member || node is not BaseTypeDeclarationSyntax)
                     .OfType<ParameterSyntax>())
        {
            if (parameter.Type is { } parameterType && TypeNameIs(parameterType, "ProcessStartInfo"))
                names.Add(parameter.Identifier.Text);
        }

        return names;
    }

    private static void CollectDeclared(VariableDeclarationSyntax declaration, HashSet<string> names)
    {
        var typed = TypeNameIs(declaration.Type, "ProcessStartInfo");
        var isVar = declaration.Type is IdentifierNameSyntax { Identifier.Text: "var" };
        foreach (var variable in declaration.Variables)
        {
            if (typed || (isVar && CreationIsProcessStartInfo(variable.Initializer?.Value)))
                names.Add(variable.Identifier.Text);
        }
    }

    private static bool CreationIsProcessStartInfo(ExpressionSyntax? expression) =>
        expression is not null && Strip(expression) is BaseObjectCreationExpressionSyntax creation && IsProcessStartInfo(creation);

    private static IEnumerable<BaseObjectCreationExpressionSyntax> CreationsIn(SyntaxNode scope) =>
        scope.DescendantNodes(node => node == scope || node is not BaseTypeDeclarationSyntax)
            .OfType<BaseObjectCreationExpressionSyntax>();

    private static IEnumerable<AssignmentExpressionSyntax> AssignmentsIn(SyntaxNode scope) =>
        scope.DescendantNodes(node => node == scope || node is not BaseTypeDeclarationSyntax)
            .OfType<AssignmentExpressionSyntax>();

    private static bool IsProcessStartInfo(BaseObjectCreationExpressionSyntax creation)
    {
        if (creation is ObjectCreationExpressionSyntax named && TypeNameIs(named.Type, "ProcessStartInfo"))
            return true;
        if (creation is not ImplicitObjectCreationExpressionSyntax)
            return false;
        return creation.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax variable } }
                => TypeNameIs(variable.Type, "ProcessStartInfo"),
            ArrowExpressionClauseSyntax arrow => ArrowTypeIsProcessStartInfo(arrow),
            ReturnStatementSyntax statement => EnclosingReturnIsProcessStartInfo(statement),
            _ => false,
        };
    }

    private static bool ArrowTypeIsProcessStartInfo(ArrowExpressionClauseSyntax arrow) => arrow.Parent switch
    {
        MethodDeclarationSyntax method => TypeNameIs(method.ReturnType, "ProcessStartInfo"),
        PropertyDeclarationSyntax property => TypeNameIs(property.Type, "ProcessStartInfo"),
        LocalFunctionStatementSyntax local => TypeNameIs(local.ReturnType, "ProcessStartInfo"),
        _ => false,
    };

    private static bool EnclosingReturnIsProcessStartInfo(ReturnStatementSyntax statement)
    {
        for (SyntaxNode? current = statement.Parent; current is not null; current = current.Parent)
        {
            if (current is BaseTypeDeclarationSyntax)
                return false;
            if (current is MethodDeclarationSyntax method)
                return TypeNameIs(method.ReturnType, "ProcessStartInfo");
            if (current is LocalFunctionStatementSyntax local)
                return TypeNameIs(local.ReturnType, "ProcessStartInfo");
        }

        return false;
    }

    private static bool TypeNameIs(TypeSyntax type, string name) => type switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text == name,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text == name,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text == name,
        _ => false,
    };

    private static bool ReferencesSharedRoot(
        ExpressionSyntax expression, HashSet<string> classAliases, SyntaxNode site, TypeDeclarationSyntax type)
    {
        expression = Strip(expression);
        if (IsCheckpointFixturesRepoRoot(expression))
            return true;
        if (expression is not IdentifierNameSyntax identifier)
            return false;
        var name = identifier.Identifier.Text;
        return classAliases.Contains(name) || MemberAliases(site, type).Contains(name);
    }

    private static HashSet<string> ClassAliases(TypeDeclarationSyntax type)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
            CollectInitialized(field.Declaration.Variables, aliases);
        foreach (var property in type.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (property.Initializer is { Value: { } value } && IsCheckpointFixturesRepoRoot(value))
                aliases.Add(property.Identifier.Text);
        }

        return aliases;
    }

    private static HashSet<string> MemberAliases(SyntaxNode site, TypeDeclarationSyntax type)
    {
        SyntaxNode? member = site;
        while (member is not null && member.Parent != type)
            member = member.Parent;
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        if (member is null)
            return aliases;
        foreach (var declarator in member.DescendantNodes(node => node == member || node is not BaseTypeDeclarationSyntax)
                     .OfType<VariableDeclaratorSyntax>())
        {
            if (declarator.Initializer is { Value: { } value } && IsCheckpointFixturesRepoRoot(value))
                aliases.Add(declarator.Identifier.Text);
        }

        foreach (var assignment in member.DescendantNodes(node => node == member || node is not BaseTypeDeclarationSyntax)
                     .OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is IdentifierNameSyntax identifier && IsCheckpointFixturesRepoRoot(assignment.Right))
                aliases.Add(identifier.Identifier.Text);
        }

        return aliases;
    }

    private static void CollectInitialized(SeparatedSyntaxList<VariableDeclaratorSyntax> variables, HashSet<string> aliases)
    {
        foreach (var variable in variables)
        {
            if (variable.Initializer is { Value: { } value } && IsCheckpointFixturesRepoRoot(value))
                aliases.Add(variable.Identifier.Text);
        }
    }

    private static bool IsCheckpointFixturesRepoRoot(ExpressionSyntax expression)
    {
        expression = Strip(expression);
        return expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "RepoRoot" } access
            && IsCheckpointFixtures(access.Expression);
    }

    private static bool IsCheckpointFixtures(ExpressionSyntax expression)
    {
        expression = Strip(expression);
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text == "CheckpointFixtures",
            MemberAccessExpressionSyntax access => access.Name.Identifier.Text == "CheckpointFixtures",
            _ => false,
        };
    }

    private static ExpressionSyntax Strip(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        return expression;
    }

    private static string ClrName(TypeDeclarationSyntax type)
    {
        var names = new List<string>();
        for (SyntaxNode? node = type; node is not null; node = node.Parent)
        {
            if (node is TypeDeclarationSyntax declaration)
            {
                var ident = declaration.Identifier.Text;
                if (declaration.TypeParameterList is { Parameters.Count: > 0 } parameters)
                    ident += "`" + parameters.Parameters.Count;
                names.Insert(0, ident);
            }
            else if (node is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                names.Insert(0, namespaceDeclaration.Name.ToString());
                break;
            }
        }

        if (names.Count == 1)
            return names[0];
        return names[0] + "." + string.Join("+", names.Skip(1));
    }
}
