using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Release-only positive relation. Never deserialize the capped ordinary index here.
/// S1R2 supplies declaration identities and uncapped syntactic edges, not evaluated
/// project/runtime closure or an authoritative discovery universe (S1R3-S1R6).
/// </summary>
internal static class ReleaseImpactGraphBuilder
{
    internal static ReleaseImpactGraph Build(IReadOnlyDictionary<string, string> inputs)
    {
        var reasons = new SortedSet<string>(StringComparer.Ordinal)
        {
            "project-evaluation-unavailable", "runtime-inputs-unavailable",
            "script-closure-unavailable", "platform-evaluation-unavailable",
        };
        var projects = inputs.Keys.Where(p => p.EndsWith(".csproj", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        var scans = new List<Scan>();
        foreach (var path in inputs.Keys.Where(p => p.EndsWith(".cs", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var root = CSharpSyntaxTree.ParseText(inputs[path], new CSharpParseOptions(LanguageVersion.Preview), path).GetRoot();
            if (root.ContainsDiagnostics)
                reasons.Add("source-parse-unresolved:" + path);
            // Without evaluated preprocessor symbols this census cannot certify either target.
            if (root.DescendantTrivia(descendIntoTrivia: true).Any(t => t.IsDirective))
                reasons.Add("conditional-source-unresolved:" + path);
            var owners = projects.Where(p => path.StartsWith(DirectoryOf(p), StringComparison.Ordinal)).ToArray();
            if (owners.Length > 0)
            {
                var depth = owners.Max(p => DirectoryOf(p).Length);
                owners = owners.Where(p => DirectoryOf(p).Length == depth).ToArray();
            }
            if (owners.Length != 1)
                reasons.Add("project-ownership-unresolved:" + path);
            scans.Add(new Scan(path, root, owners));
        }

        var typeFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var pieces = new Dictionary<ReleaseImpactIdentity, List<(string Path, TypeDeclarationSyntax Type)>>();
        foreach (var scan in scans)
        {
            foreach (var type in scan.Root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (!typeFiles.TryGetValue(type.Identifier.ValueText, out var paths))
                    typeFiles[type.Identifier.ValueText] = paths = new HashSet<string>(StringComparer.Ordinal);
                paths.Add(scan.Path);
                if (type is not TypeDeclarationSyntax declaration || !IsClass(declaration))
                    continue;
                foreach (var project in scan.Projects.Where(p => p.StartsWith("tests/", StringComparison.Ordinal)))
                {
                    var id = new ReleaseImpactIdentity(project, FullName(type));
                    if (!pieces.TryGetValue(id, out var parts))
                        pieces[id] = parts = [];
                    parts.Add((scan.Path, declaration));
                }
            }
        }

        var tests = pieces.Where(pair => pair.Value.Any(part =>
            part.Type.Identifier.ValueText.EndsWith("Tests", StringComparison.Ordinal)
            || part.Type.Members.OfType<MethodDeclarationSyntax>().Any(method => method.AttributeLists
                .SelectMany(list => list.Attributes).Any(IsTestAttribute))))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var universe = tests.OrderBy(p => p.Key.Project, StringComparer.Ordinal).ThenBy(p => p.Key.FullName, StringComparer.Ordinal)
            .Select(p => new ReleaseImpactClass(p.Key.Project, p.Key.FullName,
                p.Value.Select(part => part.Path).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                // Potential membership only. No native execution or evaluated platform is credited.
                ["linux", "windows"])).ToArray();

        var hits = new Dictionary<string, Dictionary<ReleaseImpactIdentity, HashSet<string>>>(StringComparer.Ordinal);
        void Hit(string path, ReleaseImpactIdentity id, string kind)
        {
            if (!hits.TryGetValue(path, out var consumers)) hits[path] = consumers = [];
            if (!consumers.TryGetValue(id, out var kinds)) consumers[id] = kinds = new HashSet<string>(StringComparer.Ordinal);
            kinds.Add(kind);
        }

        foreach (var (id, parts) in tests)
        {
            foreach (var (path, type) in parts)
            {
                Hit(path, id, "declaration");
                // All ambiguous simple-name targets are positives. A syntactic edge never
                // subtracts a conservative project edge supplied by later composition.
                foreach (var name in type.DescendantNodes().OfType<SimpleNameSyntax>())
                    if (typeFiles.TryGetValue(name.Identifier.ValueText, out var targets))
                        foreach (var target in targets) Hit(target, id, "type");
                foreach (var literal in type.DescendantNodes().OfType<LiteralExpressionSyntax>())
                {
                    if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) continue;
                    var target = literal.Token.ValueText.Replace('\\', '/');
                    if (inputs.ContainsKey(target)) Hit(target, id, "path");
                }
            }
        }

        foreach (var global in DerivedImpactIndexBuilder.Allowlist.Where(inputs.ContainsKey))
            foreach (var id in tests.Keys) Hit(global, id, "global");

        var edges = hits.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new ReleaseImpactEdge(p.Key,
            p.Value.OrderBy(c => c.Key.Project, StringComparer.Ordinal).ThenBy(c => c.Key.FullName, StringComparer.Ordinal)
                .Select(c => new ReleaseImpactConsumer(c.Key.Project, c.Key.FullName, c.Value.Order(StringComparer.Ordinal).ToArray())).ToArray())).ToArray();
        return new ReleaseImpactGraph("antiphon-release-relation-v1", false, "Full", reasons.ToArray(), universe, edges);
    }

    internal static byte[] Serialize(object value) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n",
        }) + "\n");

    private static bool IsClass(TypeDeclarationSyntax type) => type is ClassDeclarationSyntax
        || type is RecordDeclarationSyntax record && !record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword);

    private static bool IsTestAttribute(AttributeSyntax attribute) => attribute.Name.ToString().Split('.').Last()
        is "Test" or "TestAttribute";

    private static string DirectoryOf(string path) => path.Contains('/') ? path[..(path.LastIndexOf('/') + 1)] : "";

    private static string FullName(BaseTypeDeclarationSyntax type)
    {
        var spaces = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse()
            .Select(ns => string.Join('.', ns.Name.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.ValueText)));
        var types = type.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(t =>
        {
            var arity = (t as TypeDeclarationSyntax)?.TypeParameterList?.Parameters.Count ?? 0;
            return t.Identifier.ValueText + (arity == 0 ? "" : "`" + arity);
        });
        var prefix = string.Join('.', spaces);
        return (prefix.Length == 0 ? "" : prefix + ".") + string.Join('+', types);
    }

    private sealed record Scan(string Path, SyntaxNode Root, string[] Projects);
}

internal sealed record ReleaseImpactIdentity(string Project, string FullName);
internal sealed record ReleaseImpactClass(string Project, string FullName, string[] DeclarationPaths, string[] Platforms);
internal sealed record ReleaseImpactConsumer(string Project, string FullName, string[] Kinds);
internal sealed record ReleaseImpactEdge(string Path, ReleaseImpactConsumer[] Classes);
internal sealed record ReleaseImpactGraph(string Schema, bool Complete, string Fallback, string[] IncompleteReasons,
    ReleaseImpactClass[] Universe, ReleaseImpactEdge[] Edges);
