using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Conservative source inventory and reverse project closure over one immutable input tree.
/// Literal conditional alternatives are unioned, never evaluated on the analyst's host.
/// This is NOT an MSBuild evaluation receipt. SDK imports, generated artifacts, metadata,
/// target properties and runtime readers still require source-bound evaluation before S1R6.
/// </summary>
internal static class ReleaseImpactProjectGraph
{
    internal static ReleaseImpactGraph Build(IReadOnlyDictionary<string, string> inputs)
    {
        var relation = ReleaseImpactGraphBuilder.Build(inputs);
        var unknown = new SortedSet<string>(relation.IncompleteReasons, StringComparer.Ordinal)
        {
            "assembly-metadata-unavailable",
        };
        var projects = inputs.Keys.Where(p => p.EndsWith(".csproj", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        var reverse = projects.ToDictionary(p => p, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var assemblies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Own(string path, string project)
        {
            if (!owners.TryGetValue(path, out var set)) owners[path] = set = new(StringComparer.Ordinal);
            set.Add(project);
        }
        void Assembly(string name, string project)
        {
            if (!assemblies.TryGetValue(name, out var set)) assemblies[name] = set = new(StringComparer.Ordinal);
            set.Add(project);
        }

        foreach (var project in projects)
        {
            // Directory membership is deliberately a superset. Exclude/Remove and disabled
            // default items cannot narrow a source-only bound. Linked items are added below.
            foreach (var path in inputs.Keys.Where(p => p.StartsWith(DirectoryOf(project), StringComparison.Ordinal))) Own(path, project);
            Assembly(Path.GetFileNameWithoutExtension(project), project);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            Read(project);
            var directory = DirectoryOf(project);
            while (true)
            {
                foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
                    if (inputs.ContainsKey(directory + name)) Read(directory + name);
                foreach (var name in new[] { "global.json", "NuGet.Config", "nuget.config" })
                    if (inputs.ContainsKey(directory + name)) Own(directory + name, project);
                if (directory.Length == 0) break;
                directory = DirectoryOf(directory.TrimEnd('/'));
            }

            void Read(string path)
            {
                Own(path, project);
                if (!inputs.TryGetValue(path, out var text))
                {
                    unknown.Add($"project-input-missing:{project}:{path}");
                    return;
                }
                if (!visited.Add(path)) return;
                XDocument xml;
                try { xml = XDocument.Parse(text); }
                catch (XmlException) { unknown.Add("project-parse-unresolved:" + path); return; }
                if (xml.Root?.Name.LocalName != "Project")
                {
                    unknown.Add("project-parse-unresolved:" + path);
                    return;
                }
                if (xml.Descendants().Attributes("Condition").Any()) unknown.Add("project-condition-unevaluated:" + project);
                foreach (var element in xml.Descendants())
                {
                    var kind = element.Name.LocalName;
                    if (kind == "AssemblyName")
                    {
                        var assemblyName = element.Value.Trim();
                        if (HasExpression(assemblyName)) unknown.Add($"project-expression-unresolved:{project}:{assemblyName}");
                        else Assembly(assemblyName, project);
                    }
                    if (kind is "Target" or "UsingTask" or "Sdk") unknown.Add("project-build-logic-unevaluated:" + project);
                    if (kind == "PackageReference") unknown.Add("package-input-unresolved:" + project);
                    if (kind is "Analyzer" or "AdditionalFiles") unknown.Add("generated-provenance-unavailable:" + project);
                    var expression = kind == "Import" ? element.Attribute("Project")?.Value
                        : kind is "Compile" or "ProjectReference" or "Content" or "None" or "EmbeddedResource" or "AdditionalFiles" or "Analyzer" or "Reference"
                            ? element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value ?? element.Attribute("Remove")?.Value : null;
                    if (expression is null) continue;
                    foreach (var token in expression.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var resolved = HasExpression(token) ? null : Resolve(DirectoryOf(path), token);
                        if (resolved is null)
                        {
                            unknown.Add($"project-expression-unresolved:{project}:{token}");
                            continue;
                        }
                        var matches = resolved.IndexOfAny(['*', '?']) < 0 ? new[] { resolved }
                            : inputs.Keys.Where(p => Glob(resolved, p)).Order(StringComparer.Ordinal).ToArray();
                        if (matches.Length == 0) unknown.Add($"project-input-missing:{project}:{resolved}");
                        foreach (var input in matches)
                        {
                            Own(input, project);
                            if (!inputs.ContainsKey(input)) unknown.Add($"project-input-missing:{project}:{input}");
                            if (kind == "Import") Read(input);
                            if (kind == "ProjectReference")
                            {
                                if (reverse.TryGetValue(input, out var consumers)) consumers.Add(project);
                                else unknown.Add($"project-reference-unresolved:{project}:{input}");
                            }
                        }
                    }
                }
            }
        }

        // Constant reflection adds an assembly-wide runtime edge even without a project
        // reference. Computed/unresolved targets stay unknown; text is never negative proof.
        var scans = inputs.Where(p => p.Key.EndsWith(".cs", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => CSharpSyntaxTree.ParseText(p.Value).GetRoot(), StringComparer.Ordinal);
        var types = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (path, root) in scans)
            foreach (var declaration in root.DescendantNodes().Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                var typeName = TypeName(declaration);
                if (!types.TryGetValue(typeName, out var set)) types[typeName] = set = new(StringComparer.Ordinal);
                if (owners.TryGetValue(path, out var typeOwners)) set.UnionWith(typeOwners);
            }
        foreach (var (path, root) in scans)
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!TryReflectionCall(call, out var method, out var receiver)) continue;
                var typeLookup = method == "GetType" && receiver is not null && (receiver == "Type" || receiver.EndsWith(".Type", StringComparison.Ordinal));
                var assemblyLookup = method == "Load" && receiver is not null && (receiver == "Assembly" || receiver.EndsWith(".Assembly", StringComparison.Ordinal));
                if (!typeLookup && !assemblyLookup)
                {
                    if (IsUnrecognisedReflection(method, receiver, call.ArgumentList.Arguments.Count))
                        unknown.Add("reflection-target-unresolved:" + path);
                    continue;
                }
                var target = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                HashSet<string>? targets = null;
                if (target is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    var name = literal.Token.ValueText;
                    var parts = name.Split(',', StringSplitOptions.TrimEntries);
                    if (typeLookup && types.TryGetValue(parts[0], out var typeOwners))
                    {
                        targets = new(typeOwners, StringComparer.Ordinal);
                        if (parts.Length > 1)
                        {
                            if (assemblies.TryGetValue(parts[1], out var qualifiedAssemblies)) targets.IntersectWith(qualifiedAssemblies);
                            else targets.Clear();
                        }
                    }
                    else if (assemblyLookup && assemblies.TryGetValue(parts[0], out var loadedAssemblies)) targets = loadedAssemblies;
                }
                if (targets is null || targets.Count == 0 || !owners.TryGetValue(path, out var callers))
                {
                    unknown.Add("reflection-target-unresolved:" + path);
                    continue;
                }
                foreach (var owner in targets) reverse[owner].UnionWith(callers);
            }

        var hits = relation.Edges.ToDictionary(e => e.Path, e => e.Classes.ToDictionary(
            c => new ReleaseImpactIdentity(c.Project, c.FullName), c => c.Kinds.ToHashSet(StringComparer.Ordinal)), StringComparer.Ordinal);
        foreach (var (path, owningProjects) in owners)
        {
            var downstream = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(owningProjects);
            while (pending.TryPop(out var project))
                if (downstream.Add(project))
                    foreach (var consumer in reverse[project]) pending.Push(consumer);
            foreach (var test in relation.Universe.Where(c => downstream.Contains(c.Project)))
            {
                if (!hits.TryGetValue(path, out var classes)) hits[path] = classes = [];
                var id = new ReleaseImpactIdentity(test.Project, test.FullName);
                if (!classes.TryGetValue(id, out var kinds)) classes[id] = kinds = new(StringComparer.Ordinal);
                kinds.Add("projectClosure");
            }
        }
        var edges = hits.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new ReleaseImpactEdge(p.Key,
            p.Value.OrderBy(c => c.Key.Project, StringComparer.Ordinal).ThenBy(c => c.Key.FullName, StringComparer.Ordinal)
                .Select(c => new ReleaseImpactConsumer(c.Key.Project, c.Key.FullName, c.Value.Order(StringComparer.Ordinal).ToArray())).ToArray())).ToArray();
        return relation with { IncompleteReasons = unknown.ToArray(), Edges = edges };
    }

    internal static ReleaseImpactGraph Union(ReleaseImpactGraph oldGraph, ReleaseImpactGraph newGraph)
    {
        var reasons = oldGraph.IncompleteReasons.Intersect(newGraph.IncompleteReasons, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var rightIds = newGraph.Universe.Select(c => new ReleaseImpactIdentity(c.Project, c.FullName)).ToHashSet();
        var universe = oldGraph.Universe.Where(c => rightIds.Contains(new ReleaseImpactIdentity(c.Project, c.FullName))).ToArray();
        var rightEdges = newGraph.Edges.ToDictionary(e => e.Path, e => e.Classes.ToDictionary(
            c => new ReleaseImpactIdentity(c.Project, c.FullName), c => c.Kinds.ToHashSet(StringComparer.Ordinal)));
        var edges = new List<ReleaseImpactEdge>();
        foreach (var edge in oldGraph.Edges)
        {
            if (!rightEdges.TryGetValue(edge.Path, out var rightClasses)) continue;
            var classes = new List<ReleaseImpactConsumer>();
            foreach (var consumer in edge.Classes)
            {
                var id = new ReleaseImpactIdentity(consumer.Project, consumer.FullName);
                if (!rightClasses.TryGetValue(id, out var rightKinds)) continue;
                var kinds = consumer.Kinds.Where(rightKinds.Contains).Order(StringComparer.Ordinal).ToArray();
                if (kinds.Length == 0) continue;
                classes.Add(consumer with { Kinds = kinds });
            }
            if (classes.Count > 0) edges.Add(edge with { Classes = classes.ToArray() });
        }
        return oldGraph with { IncompleteReasons = reasons, Universe = universe, Edges = edges.ToArray() };
    }

    private static bool TryReflectionCall(InvocationExpressionSyntax call, out string method, out string? receiver)
    {
        switch (call.Expression)
        {
            case MemberAccessExpressionSyntax member:
                method = member.Name.Identifier.ValueText;
                receiver = member.Expression.ToString();
                return true;
            case IdentifierNameSyntax name:
                method = name.Identifier.ValueText;
                receiver = null;
                return true;
            default:
                method = "";
                receiver = null;
                return false;
        }
    }

    // Recognised Type.GetType and Assembly.Load are bound above. Every other reflective
    // load adds an incomplete reason. Parameterless GetType is Object.GetType(), not a name load.
    private static bool IsUnrecognisedReflection(string method, string? receiver, int arguments)
    {
        if (method is "LoadFrom" or "LoadFile" or "GetTypes" or "GetExportedTypes"
            or "CreateInstance" or "LoadFromAssemblyName" or "LoadFromAssemblyPath")
            return true;
        if (method == "GetType" && arguments > 0) return true;
        if (method == "Load" && arguments > 0)
            return receiver is null
                || receiver.Contains("AppDomain", StringComparison.Ordinal)
                || receiver.EndsWith(".CurrentDomain", StringComparison.Ordinal);
        return false;
    }

    private static bool HasExpression(string value) => value.Contains("$(", StringComparison.Ordinal)
        || value.Contains("@(", StringComparison.Ordinal) || value.Contains("%(", StringComparison.Ordinal);
    private static string DirectoryOf(string path) => path.Contains('/') ? path[..(path.LastIndexOf('/') + 1)] : "";
    private static string? Resolve(string directory, string value)
    {
        value = value.Replace('\\', '/');
        if (value.StartsWith('/') || value.Contains(':')) return null;
        var parts = new List<string>();
        foreach (var piece in (directory + value).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (piece == ".") continue;
            if (piece == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
            else parts.Add(piece);
        }
        return string.Join('/', parts);
    }
    private static bool Glob(string pattern, string path) => Regex.IsMatch(path, "^" + Regex.Escape(pattern)
        .Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static string TypeName(SyntaxNode declaration)
    {
        var spaces = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString());
        var names = declaration.AncestorsAndSelf().Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax).Reverse().Select(n =>
        {
            var name = n is BaseTypeDeclarationSyntax type ? type.Identifier.ValueText : ((DelegateDeclarationSyntax)n).Identifier.ValueText;
            var arity = n is TypeDeclarationSyntax generic ? generic.TypeParameterList?.Parameters.Count ?? 0
                : n is DelegateDeclarationSyntax callback ? callback.TypeParameterList?.Parameters.Count ?? 0 : 0;
            return name + (arity == 0 ? "" : "`" + arity);
        });
        var prefix = string.Join('.', spaces);
        return (prefix.Length == 0 ? "" : prefix + ".") + string.Join('+', names);
    }
}
