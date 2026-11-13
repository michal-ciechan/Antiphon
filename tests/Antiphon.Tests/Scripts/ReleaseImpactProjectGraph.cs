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
        {
            var bindings = ReflectionBindings.Read(root);
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (!TryReflectionCall(call, out var method, out var receiverExpression, out var receiver)) continue;
                var arguments = call.ArgumentList.Arguments.Count;
                var expanded = ExpandReceiver(receiverExpression, receiver, bindings);
                var typeLookup = method == "GetType" && receiver is not null
                    && (receiver == "Type" || receiver.EndsWith(".Type", StringComparison.Ordinal) || expanded == "System.Type");
                var assemblyLookup = method == "Load" && receiver is not null
                    && (receiver == "Assembly" || receiver.EndsWith(".Assembly", StringComparison.Ordinal) || expanded == "System.Reflection.Assembly");
                if (!typeLookup && !assemblyLookup)
                {
                    if (IsUnrecognisedReflection(method, arguments, expanded, receiverExpression is null && bindings.HasReflectiveStaticUsing))
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
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var reason in oldGraph.IncompleteReasons) reasons.Add(reason);
        foreach (var reason in newGraph.IncompleteReasons) reasons.Add(reason);
        var classes = new Dictionary<ReleaseImpactIdentity, (SortedSet<string> Paths, SortedSet<string> Platforms)>();
        void AddClass(ReleaseImpactClass type)
        {
            var id = new ReleaseImpactIdentity(type.Project, type.FullName);
            if (!classes.TryGetValue(id, out var sets))
            {
                sets = (new SortedSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal));
                classes[id] = sets;
            }
            foreach (var path in type.DeclarationPaths) sets.Paths.Add(path);
            foreach (var platform in type.Platforms) sets.Platforms.Add(platform);
        }
        foreach (var type in oldGraph.Universe) AddClass(type);
        foreach (var type in newGraph.Universe) AddClass(type);
        var universe = classes.OrderBy(p => p.Key.Project, StringComparer.Ordinal).ThenBy(p => p.Key.FullName, StringComparer.Ordinal)
            .Select(p => new ReleaseImpactClass(p.Key.Project, p.Key.FullName, p.Value.Paths.ToArray(), p.Value.Platforms.ToArray())).ToArray();
        var hits = new Dictionary<string, Dictionary<ReleaseImpactIdentity, SortedSet<string>>>(StringComparer.Ordinal);
        void AddEdge(ReleaseImpactEdge edge)
        {
            if (!hits.TryGetValue(edge.Path, out var consumers))
                hits[edge.Path] = consumers = new Dictionary<ReleaseImpactIdentity, SortedSet<string>>();
            foreach (var consumer in edge.Classes)
            {
                var id = new ReleaseImpactIdentity(consumer.Project, consumer.FullName);
                if (!consumers.TryGetValue(id, out var kinds))
                    consumers[id] = kinds = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var kind in consumer.Kinds) kinds.Add(kind);
            }
        }
        foreach (var edge in oldGraph.Edges) AddEdge(edge);
        foreach (var edge in newGraph.Edges) AddEdge(edge);
        var edges = hits.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new ReleaseImpactEdge(p.Key,
            p.Value.OrderBy(c => c.Key.Project, StringComparer.Ordinal).ThenBy(c => c.Key.FullName, StringComparer.Ordinal)
                .Select(c => new ReleaseImpactConsumer(c.Key.Project, c.Key.FullName, c.Value.ToArray())).ToArray())).ToArray();
        var fallback = oldGraph.Fallback == "Full" || newGraph.Fallback == "Full" ? "Full" : oldGraph.Fallback;
        return new ReleaseImpactGraph(oldGraph.Schema, oldGraph.Complete && newGraph.Complete, fallback, reasons.ToArray(), universe, edges);
    }

    private static bool TryReflectionCall(InvocationExpressionSyntax call, out string method, out ExpressionSyntax? receiverExpression, out string? receiver)
    {
        switch (call.Expression)
        {
            case MemberAccessExpressionSyntax member:
                method = member.Name.Identifier.ValueText;
                receiverExpression = Peel(member.Expression);
                receiver = receiverExpression.ToString();
                return true;
            case MemberBindingExpressionSyntax binding:
                method = binding.Name.Identifier.ValueText;
                receiverExpression = call.Parent is ConditionalAccessExpressionSyntax access ? Peel(access.Expression) : null;
                receiver = receiverExpression?.ToString();
                return true;
            case IdentifierNameSyntax name:
                method = name.Identifier.ValueText;
                receiverExpression = null;
                receiver = null;
                return true;
            default:
                method = "";
                receiverExpression = null;
                receiver = null;
                return false;
        }
    }

    // Constant Type.GetType and Assembly.Load bind above, including aliases of those types.
    // Any other call whose receiver is a reflection/loader/native type, or whose member is a
    // known load/invoke name, is incomplete. Parameterless GetType is Object.GetType().
    private static bool IsUnrecognisedReflection(string method, int arguments, string? expandedReceiver, bool bareStaticReflective)
    {
        if (method == "GetType" && arguments == 0) return false;
        if (bareStaticReflective || (expandedReceiver is not null && IsReflectiveType(expandedReceiver))) return true;
        return method is "LoadFrom" or "LoadFile" or "LoadFromStream" or "LoadFromAssemblyName" or "LoadFromAssemblyPath"
            or "GetTypes" or "GetExportedTypes" or "GetTypeFromProgID" or "GetTypeFromCLSID"
            or "CreateInstance" or "CreateInstanceFrom" or "CreateInstanceAndUnwrap"
            or "Invoke" or "InvokeMember" or "GetMethod" or "GetMethods" or "GetField" or "GetFields"
            or "GetProperty" or "GetProperties" or "GetMember" or "GetMembers" or "GetConstructor"
            or "GetConstructors" or "GetInterface" or "GetNestedType" or "GetNestedTypes"
            or "GetDelegateForFunctionPointer"
            || method == "GetType" && arguments > 0
            || method == "Load" && arguments > 0;
    }

    private static string? ExpandReceiver(ExpressionSyntax? expression, string? receiver, ReflectionBindings bindings)
    {
        if (expression is null) return null;
        if (expression is IdentifierNameSyntax identifier)
        {
            var name = identifier.Identifier.ValueText;
            if (bindings.Aliases.TryGetValue(name, out var aliased)) return bindings.Expand(aliased);
            if (bindings.Locals.TryGetValue(name, out var types))
            {
                string? first = null;
                foreach (var type in types)
                {
                    var expanded = bindings.Expand(type);
                    first ??= expanded;
                    if (IsReflectiveType(expanded)) return expanded;
                }
                return first;
            }
            return bindings.Expand(name);
        }
        return bindings.Expand(receiver ?? expression.ToString());
    }

    private static ExpressionSyntax Peel(ExpressionSyntax expression)
    {
        while (true)
        {
            if (expression is ParenthesizedExpressionSyntax paren) { expression = paren.Expression; continue; }
            if (expression.IsKind(SyntaxKind.SuppressNullableWarningExpression) && expression is PostfixUnaryExpressionSyntax postfix)
            { expression = postfix.Operand; continue; }
            return expression;
        }
    }

    private static bool IsReflectiveType(string name)
    {
        if (name.Length == 0) return false;
        if (IsReflectiveQualified(name)) return true;
        var built = "";
        foreach (var part in name.Split('.'))
        {
            built = built.Length == 0 ? part : built + "." + part;
            if (IsReflectiveQualified(built)) return true;
        }
        return false;
    }

    private static bool IsReflectiveQualified(string name) =>
        name is "System.Type" or "System.Activator" or "System.AppDomain" or "System.Reflection" or "System.Runtime.Loader"
            or "System.Runtime.InteropServices.NativeLibrary"
        || name.StartsWith("System.Type.", StringComparison.Ordinal)
        || name.StartsWith("System.Activator.", StringComparison.Ordinal)
        || name.StartsWith("System.AppDomain.", StringComparison.Ordinal)
        || name.StartsWith("System.Reflection.", StringComparison.Ordinal)
        || name.StartsWith("System.Runtime.Loader.", StringComparison.Ordinal)
        || name.StartsWith("System.Runtime.InteropServices.NativeLibrary.", StringComparison.Ordinal);

    private static string NormalizeType(string raw)
    {
        raw = raw.Replace(" ", "").Replace("global::", "");
        while (raw.EndsWith('?') || raw.EndsWith("[]", StringComparison.Ordinal))
            raw = raw.EndsWith('?') ? raw[..^1] : raw[..^2];
        var generic = raw.IndexOf('<');
        return generic < 0 ? raw : raw[..generic];
    }

    private sealed class ReflectionBindings
    {
        internal Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);
        internal List<string> Namespaces { get; } = new();
        internal List<string> StaticTypes { get; } = new();
        internal Dictionary<string, List<string>> Locals { get; } = new(StringComparer.Ordinal);
        internal bool HasReflectiveStaticUsing { get; private set; }

        internal static ReflectionBindings Read(SyntaxNode root)
        {
            var bindings = new ReflectionBindings();
            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    case UsingDirectiveSyntax directive:
                        var name = NormalizeType(directive.Name?.ToString() ?? "");
                        if (name.Length == 0) break;
                        if (directive.Alias is { } alias) bindings.Aliases[alias.Name.Identifier.ValueText] = name;
                        else if (directive.StaticKeyword.RawKind != 0) bindings.StaticTypes.Add(name);
                        else bindings.Namespaces.Add(name);
                        break;
                    case ParameterSyntax parameter when parameter.Type is not null:
                        bindings.AddLocal(parameter.Identifier.ValueText, parameter.Type.ToString());
                        break;
                    case VariableDeclarationSyntax declaration:
                        var typeName = declaration.Type.ToString();
                        if (typeName == "var") break;
                        foreach (var variable in declaration.Variables)
                            bindings.AddLocal(variable.Identifier.ValueText, typeName);
                        break;
                    case ForEachStatementSyntax loop when loop.Type.ToString() != "var":
                        bindings.AddLocal(loop.Identifier.ValueText, loop.Type.ToString());
                        break;
                }
            }
            foreach (var type in bindings.StaticTypes)
                if (IsReflectiveType(bindings.Expand(type))) bindings.HasReflectiveStaticUsing = true;
            return bindings;
        }

        internal string Expand(string raw)
        {
            raw = NormalizeType(raw);
            if (raw.Length == 0) return raw;
            var head = raw.Split('.')[0];
            if (Aliases.TryGetValue(head, out var aliased)) raw = NormalizeType(aliased) + raw[head.Length..];
            if (!raw.Contains('.'))
            {
                foreach (var space in Namespaces)
                {
                    var candidate = space + "." + raw;
                    if (IsReflectiveType(candidate)) return candidate;
                }
            }
            return raw;
        }

        private void AddLocal(string identifier, string type)
        {
            if (identifier.Length == 0 || type.Length == 0) return;
            if (!Locals.TryGetValue(identifier, out var types)) Locals[identifier] = types = new();
            types.Add(type);
        }
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
