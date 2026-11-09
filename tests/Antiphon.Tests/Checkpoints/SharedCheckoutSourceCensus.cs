using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CARD-1810. Names every class under the Checkpoints tree whose source assigns
/// <c>CheckpointFixtures.RepoRoot</c> to a <c>ProcessStartInfo</c> working directory.
/// A text scan is the wrong tool: tool-lane classes read <c>RepoRoot</c> as a path and
/// assign it to other types' <c>WorkingDirectory</c> (a scheduler request, a ledger)
/// without starting a child in the shared checkout. This walk stays on the object
/// creation. Target-typed <c>new</c> takes its type from the declared variable, property,
/// method, or local function, still without a compilation. A local or field whose
/// initializer is exactly <c>CheckpointFixtures.RepoRoot</c> counts; a further copy does not.
/// </summary>
internal static class SharedCheckoutSourceCensus
{
    internal static IReadOnlyList<string> ClassesStartingInSharedCheckout(string directory)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            foreach (var name in ClassesIn(File.ReadAllText(path)))
                found.Add(name);
        }

        return found.ToList();
    }

    internal static IReadOnlyList<string> ClassesIn(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in tree.GetCompilationUnitRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (!IsClassLike(type))
                continue;
            if (StartsSharedCheckoutChild(type))
                found.Add(ClrName(type));
        }

        return found.ToList();
    }

    private static bool IsClassLike(TypeDeclarationSyntax type)
    {
        if (type is RecordDeclarationSyntax record)
            return !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);
        return type is ClassDeclarationSyntax;
    }

    private static bool StartsSharedCheckoutChild(TypeDeclarationSyntax type)
    {
        var classAliases = ClassAliases(type);
        foreach (var creation in Creations(type))
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

        return false;
    }

    private static IEnumerable<BaseObjectCreationExpressionSyntax> Creations(TypeDeclarationSyntax type) =>
        type.DescendantNodes(node => node == type || node is not BaseTypeDeclarationSyntax)
            .OfType<BaseObjectCreationExpressionSyntax>();

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
