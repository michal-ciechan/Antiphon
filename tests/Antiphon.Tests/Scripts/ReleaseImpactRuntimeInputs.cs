using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Source-only runtime positives over one immutable endpoint. Call separately for
/// old/new inventories and union the graphs. Declarations never grant completeness:
/// evaluated items, external identities and an independent reader census are still owed.
/// </summary>
internal static class ReleaseImpactRuntimeInputs
{
    internal const string ManifestPath = "tests/selector-runtime-inputs.json";

    internal static ReleaseImpactGraph Build(IReadOnlyDictionary<string, string> inputs)
    {
        var basis = ReleaseImpactProjectGraph.Build(inputs);
        var unknown = new SortedSet<string>(basis.IncompleteReasons, StringComparer.Ordinal)
        {
            "runtime-reader-census-unavailable",
        };
        var hits = basis.Edges.ToDictionary(e => e.Path, e => e.Classes.ToDictionary(
            c => new ReleaseImpactIdentity(c.Project, c.FullName), c => c.Kinds.ToHashSet(StringComparer.Ordinal)), StringComparer.Ordinal);
        var scans = inputs.Where(p => p.Key.EndsWith(".cs", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => CSharpSyntaxTree.ParseText(p.Value,
                new CSharpParseOptions(LanguageVersion.Preview), p.Key).GetRoot(), StringComparer.Ordinal);

        void Hit(string path, IEnumerable<ReleaseImpactClass> consumers, string kind)
        {
            if (!hits.TryGetValue(path, out var classes)) hits[path] = classes = [];
            foreach (var consumer in consumers)
            {
                var id = new ReleaseImpactIdentity(consumer.Project, consumer.FullName);
                if (!classes.TryGetValue(id, out var kinds)) classes[id] = kinds = new(StringComparer.Ordinal);
                kinds.Add(kind);
            }
        }
        var edgeLookup = basis.Edges.ToDictionary(e => e.Path, StringComparer.Ordinal);
        var classLookup = basis.Universe.ToDictionary(c => new ReleaseImpactIdentity(c.Project, c.FullName));
        ReleaseImpactClass[] Consumers(string path) => edgeLookup.TryGetValue(path, out var edge)
            ? edge.Classes.Select(c => classLookup[new ReleaseImpactIdentity(c.Project, c.FullName)]).ToArray() : [];

        var declarations = ReadDeclarations(inputs, unknown);
        foreach (var declaration in declarations)
        {
            var id = declaration.Id;
            var own = basis.Universe.Where(c => c.Project == declaration.ConsumerProject).ToArray();
            if (!inputs.ContainsKey(declaration.ConsumerProject) || own.Length == 0
                || (declaration.Scope == "class" && !own.Any(c => c.FullName == declaration.ConsumerClass)))
            {
                unknown.Add("runtime-declaration-consumer-missing:" + id);
                continue;
            }
            var consumers = declaration.Scope == "assembly" ? Consumers(declaration.ConsumerProject)
                : own.Where(c => c.FullName == declaration.ConsumerClass).ToArray();
            // A helper/hook may not be narrowed to one explicitly referencing class.
            if (declaration.SourcePaths.Any(path => !scans.ContainsKey(path)
                || !Consumers(path).Any(c => c.Project == declaration.ConsumerProject)
                || (declaration.Scope == "class" && !consumers.Any(c => c.DeclarationPaths.Contains(path, StringComparer.Ordinal)))))
            {
                unknown.Add("runtime-declaration-source-unresolved:" + id);
                continue;
            }
            if (!scans.TryGetValue(declaration.Witness.Path, out var witness)
                || !witness.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(m =>
                    m.Identifier.ValueText == declaration.Witness.Method && m.AttributeLists.SelectMany(a => a.Attributes)
                        .Any(a => a.Name.ToString().Split('.').Last() is "Test" or "TestAttribute")))
            {
                unknown.Add("runtime-declaration-witness-missing:" + id);
                continue;
            }
            Hit(ManifestPath, consumers, "runtimeInput:manifest");
            foreach (var pattern in declaration.Inputs)
            {
                var matcher = Pattern(pattern);
                var matches = inputs.Keys.Where(path => matcher.IsMatch(path)).Order(StringComparer.Ordinal).ToArray();
                if (matches.Length == 0) unknown.Add($"runtime-declaration-input-missing:{id}:{pattern}");
                foreach (var path in matches) Hit(path, consumers, "runtimeInput:" + id);
            }
            // Even an explicitly enumerated environment name is not a value/version identity.
            // Do not read environment values or place them in diagnostics.
            if (declaration.Kind == "environment")
                unknown.Add("runtime-environment-identity-unavailable:" + id);
        }

        foreach (var (path, root) in scans)
        {
            var consumers = Consumers(path);
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var name = call.Expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                    MemberBindingExpressionSyntax member => member.Name.Identifier.ValueText,
                    SimpleNameSyntax simple => simple.Identifier.ValueText,
                    _ => "",
                };
                var kind = ReaderKind(name);
                if (kind is null) continue;
                var value = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                ResolveRead(kind, value, call.SpanStart);
            }
            foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                var name = creation.Type.ToString().Split('.').Last();
                if (name is "FileStream" or "StreamReader" or "FileInfo" or "DirectoryInfo" or "ProcessStartInfo")
                    ResolveRead(name == "ProcessStartInfo" ? "host" : "file",
                        creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, creation.SpanStart);
            }

            void ResolveRead(string kind, ExpressionSyntax? expression, int position)
            {
                var reason = $"runtime-reader-unresolved:{path}:{position}:{kind}";
                if (kind is "environment" or "host" || consumers.Length == 0
                    || expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    unknown.Add(reason);
                    return;
                }
                var value = literal.Token.ValueText.Replace('\\', '/');
                if (!IsPath(value, allowGlob: false)) { unknown.Add(reason); return; }
                // Without an evaluated working directory every plausible local root is positive;
                // ambiguity remains unknown. Never guess from an arbitrary basename elsewhere.
                var candidates = new HashSet<string>(StringComparer.Ordinal);
                if (inputs.ContainsKey(value)) candidates.Add(value);
                foreach (var consumer in consumers)
                {
                    var projectDirectory = consumer.Project[..(consumer.Project.LastIndexOf('/') + 1)];
                    if (inputs.ContainsKey(projectDirectory + value)) candidates.Add(projectDirectory + value);
                }
                if (candidates.Count != 1 || kind == "directory") unknown.Add(reason);
                foreach (var target in candidates) Hit(target, consumers, "runtimeInput:literal");
            }
        }

        var edges = hits.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new ReleaseImpactEdge(p.Key,
            p.Value.OrderBy(c => c.Key.Project, StringComparer.Ordinal).ThenBy(c => c.Key.FullName, StringComparer.Ordinal)
                .Select(c => new ReleaseImpactConsumer(c.Key.Project, c.Key.FullName,
                    c.Value.Order(StringComparer.Ordinal).ToArray())).ToArray())).ToArray();
        return basis with { Complete = false, Fallback = "Full", IncompleteReasons = unknown.ToArray(), Edges = edges };
    }

    private static RuntimeDeclaration[] ReadDeclarations(IReadOnlyDictionary<string, string> inputs, SortedSet<string> unknown)
    {
        if (!inputs.TryGetValue(ManifestPath, out var text))
        {
            unknown.Add("runtime-declarations-missing");
            return [];
        }
        try
        {
            using var document = JsonDocument.Parse(text);
            RejectDuplicates(document.RootElement);
            var manifest = JsonSerializer.Deserialize<RuntimeManifest>(text, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            });
            if (manifest is null || manifest.Schema != 1 || manifest.Declarations is null) throw new JsonException();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in manifest.Declarations)
            {
                if (d is null || string.IsNullOrWhiteSpace(d.Id) || !ids.Add(d.Id)
                    || !Regex.IsMatch(d.Id, "^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)
                    || d.Kind is not ("config" or "content" or "fixture" or "host" or "environment")
                    || !IsPath(d.ConsumerProject, false) || !d.ConsumerProject.EndsWith(".csproj", StringComparison.Ordinal)
                    || d.Scope is not ("assembly" or "class")
                    || d.ConsumerClass is null || (d.Scope == "assembly" ? d.ConsumerClass.Length != 0 : string.IsNullOrWhiteSpace(d.ConsumerClass))
                    || d.SourcePaths is null || d.SourcePaths.Length == 0 || d.SourcePaths.Any(p => !IsPath(p, false))
                    || d.Inputs is null || d.Inputs.Length == 0 || d.Inputs.Any(p => !IsPath(p, true))
                    || d.EnvironmentNames is null || d.EnvironmentNames.Any(n => string.IsNullOrWhiteSpace(n) || n.Any(char.IsControl))
                    || (d.Kind == "environment" ? d.EnvironmentNames.Length == 0 : d.EnvironmentNames.Length != 0)
                    || string.IsNullOrWhiteSpace(d.Rationale) || d.Witness is null
                    || !IsPath(d.Witness.Path, false) || string.IsNullOrWhiteSpace(d.Witness.Method))
                    throw new JsonException();
            }
            return manifest.Declarations.OrderBy(d => d.Id, StringComparer.Ordinal).ToArray();
        }
        catch (JsonException)
        {
            unknown.Add("runtime-declarations-invalid");
            return [];
        }
    }

    private static void RejectDuplicates(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectDuplicates(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var value in node.EnumerateArray()) RejectDuplicates(value);
    }

    private static string? ReaderKind(string name) => name switch
    {
        "ReadAllText" or "ReadAllTextAsync" or "ReadAllBytes" or "ReadAllBytesAsync"
            or "ReadAllLines" or "ReadAllLinesAsync" or "ReadLines" or "ReadLinesAsync"
            or "Open" or "OpenRead" or "OpenText" or "AddJsonFile" or "AddXmlFile"
            or "AddIniFile" or "LoadFile" => "file",
        "GetFiles" or "EnumerateFiles" or "GetDirectories" or "EnumerateDirectories"
            or "GetFileSystemEntries" or "EnumerateFileSystemEntries" => "directory",
        "GetEnvironmentVariable" or "GetEnvironmentVariables" or "AddEnvironmentVariables" => "environment",
        "Start" or "StartAsync" => "host",
        _ => null,
    };

    private static bool IsPath(string? path, bool allowGlob) => !string.IsNullOrWhiteSpace(path)
        && !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':')
        && !path.Any(char.IsControl) && !path.Contains('$') && !path.Contains('[') && !path.Contains(']')
        && (allowGlob || path.IndexOfAny(['*', '?']) < 0)
        && path.Split('/').All(p => p.Length > 0 && p is not ("." or ".."));

    private static Regex Pattern(string pattern) => new("^" + Regex.Escape(pattern)
        .Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private sealed record RuntimeManifest(int Schema, RuntimeDeclaration[] Declarations);
    private sealed record RuntimeDeclaration(string Id, string Kind, string ConsumerProject, string Scope,
        string ConsumerClass, string[] SourcePaths, string[] Inputs, string[] EnvironmentNames, string Rationale, RuntimeWitness Witness);
    private sealed record RuntimeWitness(string Path, string Method);
}
