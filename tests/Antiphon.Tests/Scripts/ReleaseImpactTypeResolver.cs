using System.Reflection.Metadata;
using System.Security.Cryptography;
using Antiphon.ReleaseSelection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Antiphon.Tests.Scripts;

internal static class ReleaseImpactTypeResolver
{
    internal static ReleaseImpactTypeProof Resolve(IReadOnlyDictionary<string, string> inputs, ReleaseImpactGraph graph,
        IReadOnlyList<ReleaseImpactTypeOrigin> legacyOrigins, ReleaseCompilerBinding binding, int maximumCandidates)
    {
        var lexicalTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var origin in legacyOrigins)
            Remember(lexicalTypes, origin.Name, origin.Candidates);
        var lexicalTrees = inputs.Where(entry => entry.Key.EndsWith(".cs", StringComparison.Ordinal))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(entry => entry.Key, entry => CSharpSyntaxTree.ParseText(entry.Value, path: entry.Key), StringComparer.Ordinal);
        var lexicalAliases = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var extensions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var potentialFacades = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in lexicalTrees.Values) ReadDeclarations(tree);
        foreach (var tree in binding.Contexts.SelectMany(bound => bound.Compilation.SyntaxTrees))
            ReadDeclarations(tree);

        var evaluators = binding.Contexts.Select(bound => new Evaluation(bound)).ToArray();
        var origins = legacyOrigins.ToDictionary(SiteKey);
        foreach (var member in graph.Universe)
        {
            var id = new ReleaseImpactIdentity(member.Project, member.FullName);
            foreach (var path in member.DeclarationPaths)
            {
                if (!lexicalTrees.TryGetValue(path, out var tree)) continue;
                foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .Where(declaration => ReleaseImpactGraphBuilder.FullName(declaration) == member.FullName))
                    foreach (var use in declaration.DescendantNodes().OfType<SimpleNameSyntax>())
                    {
                        var candidates = Candidates(use, includeLexical: false);
                        if (candidates.Count == 0) continue;
                        var origin = Origin(id, use, candidates);
                        var key = SiteKey(origin);
                        if (origins.TryGetValue(key, out var existing))
                            origin = origin with { Candidates = Ordered(existing.Candidates.Concat(origin.Candidates)) };
                        origins[key] = origin;
                    }
            }
        }

        var sites = new List<ReleaseImpactTypeSite>();
        var covered = new Dictionary<ReleaseImpactIdentity, HashSet<string>>();
        var independent = new HashSet<(ReleaseImpactIdentity Consumer, string Path, string Kind)>();
        foreach (var evaluation in evaluators)
        {
            foreach (var (tree, model) in evaluation.Models)
            {
                var parts = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
                    .Select(part => (Part: part, Symbol: model.GetDeclaredSymbol(part)))
                    .Where(entry => IsTest(entry.Symbol))
                    .Select(entry => (entry.Part, Id: new ReleaseImpactIdentity(evaluation.Project, Name(entry.Symbol!))));
                foreach (var (part, id) in parts)
                {
                    if (!covered.TryGetValue(id, out var paths)) covered.Add(id, paths = new(StringComparer.Ordinal));
                    paths.Add(tree.FilePath);
                    foreach (var global in DerivedImpactIndexBuilder.Allowlist.Where(inputs.ContainsKey))
                        independent.Add((id, global, "global"));
                    foreach (var path in evaluation.GlobalImports) independent.Add((id, path, "directive"));
                    foreach (var literal in part.DescendantNodes().OfType<LiteralExpressionSyntax>())
                    {
                        if (!literal.IsKind(SyntaxKind.StringLiteralExpression)) continue;
                        var path = literal.Token.ValueText.Replace('\\', '/');
                        if (inputs.ContainsKey(path)) independent.Add((id, path, "path"));
                    }
                    foreach (var use in part.DescendantNodes().OfType<SimpleNameSyntax>())
                        ResolveSite(evaluation, model, use, id);
                }
            }
        }

        var knownClasses = graph.Universe.ToDictionary(member => new ReleaseImpactIdentity(member.Project, member.FullName));
        var matched = sites.ToLookup(site => SiteKey(site.Origin));
        var retained = new Dictionary<(ReleaseImpactIdentity Consumer, string Path), HashSet<string>>();
        var conservative = new Dictionary<(ReleaseImpactIdentity Consumer, string Path), HashSet<string>>();
        foreach (var edge in graph.Edges)
            foreach (var consumer in edge.Classes)
                foreach (var kind in consumer.Kinds.Where(kind => kind != "type"))
                {
                    var id = new ReleaseImpactIdentity(consumer.Project, consumer.FullName);
                    Add(conservative, id, edge.Path, kind);
                    Add(retained, id, edge.Path, kind);
                }

        foreach (var rawOrigin in origins.Values)
        {
            var origin = binding.UnavailableGenerated is { } missing && rawOrigin.Consumer.Project == missing.Project
                ? rawOrigin with { Candidates = Ordered(rawOrigin.Candidates.Append(missing.Path)) }
                : rawOrigin;
            var yes = matched[SiteKey(origin)].ToArray();
            var contexts = evaluators.Where(evaluation => evaluation.Project == origin.Consumer.Project
                && evaluation.SourceHash.ContainsKey(origin.Source)).ToArray();
            var coveredEverywhere = contexts.Length > 0 && contexts.All(context =>
                yes.Any(site => site.ContextDigest == context.Digest))
                && knownClasses.TryGetValue(origin.Consumer, out var member)
                && covered.TryGetValue(origin.Consumer, out var coveredParts)
                && member.DeclarationPaths.All(coveredParts.Contains);
            foreach (var path in origin.Candidates)
            {
                Add(conservative, origin.Consumer, path, "type");
                if (!coveredEverywhere) Add(retained, origin.Consumer, path, "type");
            }
            if (!coveredEverywhere)
                sites.Add(new(origin, "", "", binding.Eligible ? "compiler-site-uncovered" : binding.Reason,
                    null, null, [], origin.Candidates));
        }

        foreach (var site in sites)
        {
            foreach (var path in site.Origin.Candidates) Add(conservative, site.Origin.Consumer, path, "type");
            foreach (var path in site.Retained) Add(retained, site.Origin.Consumer, path, "type");
        }

        // A facade's attribute is a separate obligation even when the final type
        // has a verified definition in another assembly or reference loading failed.
        foreach (var id in knownClasses.Keys.Concat(covered.Keys).Distinct())
            foreach (var path in potentialFacades)
                independent.Add((id, path, "compiler-forwarder"));

        foreach (var (id, path, kind) in independent)
        {
            Add(conservative, id, path, kind);
            Add(retained, id, path, kind);
        }

        foreach (var (id, paths) in covered)
        {
            foreach (var path in paths)
            {
                Add(conservative, id, path, "declaration");
                Add(retained, id, path, "declaration");
            }
            var all = knownClasses.TryGetValue(id, out var existing) ? paths.Concat(existing.DeclarationPaths) : paths;
            knownClasses[id] = new(id.Project, id.FullName, Ordered(all), ["linux", "windows"]);
        }

        var universe = knownClasses.Values.OrderBy(member => member.Project, StringComparer.Ordinal)
            .ThenBy(member => member.FullName, StringComparer.Ordinal).ToArray();
        return new(
            graph with { Edges = Edges(conservative), Universe = universe },
            graph with { Edges = Edges(retained), Universe = universe },
            sites.OrderBy(site => site.Origin.Consumer.Project, StringComparer.Ordinal)
                .ThenBy(site => site.Origin.Consumer.FullName, StringComparer.Ordinal)
                .ThenBy(site => site.Origin.Source, StringComparer.Ordinal)
                .ThenBy(site => site.Origin.Start)
                .ThenBy(site => site.ContextDigest, StringComparer.Ordinal).ToArray(),
            binding.ManifestDigest);

        void ReadDeclarations(SyntaxTree tree)
        {
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                Remember(lexicalTypes, declaration.Identifier.ValueText, [tree.FilePath]);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<DelegateDeclarationSyntax>())
                Remember(lexicalTypes, declaration.Identifier.ValueText, [tree.FilePath]);
            foreach (var import in tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
                if (import.Alias is { } alias && import.NamespaceOrType is { } target)
                    Remember(lexicalAliases, alias.Name.Identifier.ValueText,
                        target.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().Select(name => name.Identifier.ValueText));
            if (tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>().Any(attribute =>
                attribute.Name.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().LastOrDefault()?.Identifier.ValueText
                    is "TypeForwardedTo" or "TypeForwardedToAttribute"))
                potentialFacades.Add(tree.FilePath);
            foreach (var method in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                if (method.ParameterList.Parameters.FirstOrDefault()?.Modifiers.Any(SyntaxKind.ThisKeyword) == true)
                    Remember(extensions, method.Identifier.ValueText, [tree.FilePath]);
        }

        HashSet<string> Candidates(SimpleNameSyntax use, bool includeLexical)
        {
            var name = use.Identifier.ValueText;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            if (includeLexical && lexicalTypes.TryGetValue(name, out var initial)) paths.UnionWith(initial);
            if (lexicalAliases.TryGetValue(name, out var targets))
                foreach (var target in targets)
                    if (lexicalTypes.TryGetValue(target, out var aliasPaths)) paths.UnionWith(aliasPaths);
            if (Invocation(use) is not null && extensions.TryGetValue(name, out var providers)) paths.UnionWith(providers);
            if (Attribute(use) is not null && lexicalTypes.TryGetValue(name + "Attribute", out var suffixed)) paths.UnionWith(suffixed);
            return paths;
        }

        void ResolveSite(Evaluation evaluation, SemanticModel model, SimpleNameSyntax use, ReleaseImpactIdentity id)
        {
            var name = use.Identifier.ValueText;
            var candidates = Candidates(use, includeLexical: true);
            if (candidates.Count == 0 && !lexicalAliases.ContainsKey(name)) return;
            var info = model.GetSymbolInfo(use);
            var alias = use is IdentifierNameSyntax identifier ? model.GetAliasInfo(identifier) : null;
            var type = info.Symbol as INamedTypeSymbol;
            var receiver = default(INamedTypeSymbol);
            if (Attribute(use) is { } attribute)
            {
                info = model.GetSymbolInfo(attribute);
                type = (info.Symbol as IMethodSymbol)?.ContainingType;
            }
            else if (Invocation(use) is { } call && extensions.ContainsKey(name))
            {
                info = model.GetSymbolInfo(call);
                type = (info.Symbol as IMethodSymbol)?.ReducedFrom?.ContainingType;
                var access = (MemberAccessExpressionSyntax)call.Expression;
                receiver = model.GetTypeInfo(access.Expression).Type as INamedTypeSymbol;
                if (receiver is not null && lexicalTypes.TryGetValue(receiver.Name, out var receiverPaths))
                    candidates.UnionWith(receiverPaths);
                if (type is not null && receiver is null) type = null;
            }
            type = type?.OriginalDefinition;
            if (type is not null && lexicalTypes.TryGetValue(type.Name, out var byDefinitionName)) candidates.UnionWith(byDefinitionName);
            var winners = DeclarationPaths(evaluation, type, evaluators);
            var receiverWinners = receiver is null ? [] : DeclarationPaths(evaluation, receiver.OriginalDefinition, evaluators);
            var forwards = type is null ? [] : evaluation.Forwarders[Name(type)].ToArray();
            var forwarding = forwards.Length == 1
                ? ReadForwarding(evaluation, model, use, type!, evaluators, forwards[0].Facade, forwards[0].Input, forwards[0].Terminal)
                : null;
            var forwardersBound = forwards.Length == 0 || forwarding is not null;
            var proven = forwardersBound && winners.Length > 0 && (receiver is null || receiverWinners.Length > 0)
                && info.CandidateReason == CandidateReason.None && info.CandidateSymbols.IsEmpty;
            if (proven)
            {
                winners = Ordered(winners.Concat(receiverWinners));
                candidates.UnionWith(winners);
            }
            if (candidates.Count == 0) return;
            var origin = Origin(id, use, candidates);
            var limit = candidates.Count > maximumCandidates;
            proven &= !limit;
            var rule = limit ? "proof-candidate-limit"
                : !forwardersBound ? "compiler-forwarder-unbound"
                : proven ? "unique-source-type" : "type-binding-unproven";
            var directivePaths = alias?.DeclaringSyntaxReferences.Select(reference => reference.SyntaxTree.FilePath) ?? [];
            foreach (var directivePath in directivePaths)
                if (evaluation.SourceHash.ContainsKey(directivePath))
                    independent.Add((id, directivePath, "directive"));
            sites.Add(new(origin, evaluation.SourceHash[origin.Source], evaluation.Digest, rule,
                proven ? type!.ContainingAssembly.Identity.GetDisplayName() : null,
                proven ? Name(type!) : null, proven ? winners : [], proven ? winners : origin.Candidates,
                proven ? forwarding : null));
        }
    }

    private static ReleaseImpactTypeOrigin Origin(ReleaseImpactIdentity id, SimpleNameSyntax use, IEnumerable<string> paths) =>
        new(id, use.SyntaxTree.FilePath, use.SpanStart, use.Span.Length, use.Identifier.ValueText, Ordered(paths));

    private static InvocationExpressionSyntax? Invocation(SimpleNameSyntax use) =>
        use.Parent is MemberAccessExpressionSyntax access && access.Name == use ? access.Parent as InvocationExpressionSyntax : null;

    private static string[] DeclarationPaths(Evaluation evaluation, INamedTypeSymbol? type, Evaluation[] owners)
    {
        if (type is null || type.IsAnonymousType || type.HasUnsupportedMetadata ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate))
            return [];
        if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, evaluation.Compilation.Assembly))
            return OwnDeclarations(evaluation, type);

        var referenced = evaluation.References
            .Where(reference => SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, reference.Symbol))
            .Select(reference => reference.Input.Project).Distinct(StringComparer.Ordinal).ToArray();
        if (referenced.Length != 1 || referenced[0] is null) return [];
        var declarations = owners.Where(owner => owner.Project == referenced[0] &&
                owner.Compilation.Assembly.Identity.Equals(type.ContainingAssembly.Identity))
            .Select(owner => OwnDeclarations(owner, owner.Compilation.Assembly.GetTypeByMetadataName(Name(type))))
            .ToArray();
        return declarations.Any(paths => paths.Length == 0) ? [] : Ordered(declarations.SelectMany(paths => paths));

        static string[] OwnDeclarations(Evaluation owner, INamedTypeSymbol? symbol)
        {
            if (symbol is null || !symbol.DeclaringSyntaxReferences.All(reference => owner.SourceHash.ContainsKey(reference.SyntaxTree.FilePath)))
                return [];
            return Ordered(symbol.DeclaringSyntaxReferences.Select(reference => reference.SyntaxTree.FilePath));
        }
    }

    // Verify the Library return TypeRef and the facade's single ExportedType hop
    // over the exact compilation references. An unsupported signature or hop
    // retains the candidates even when Roslyn can bind the spelling directly.
    private static ReleaseImpactForwardingEvidence? ReadForwarding(Evaluation consumer, SemanticModel model,
        SimpleNameSyntax use, INamedTypeSymbol type, Evaluation[] owners, IAssemblySymbol facade,
        ReleaseCompilerReference facadeInput, INamedTypeSymbol terminal)
    {
        if (!SymbolEqualityComparer.Default.Equals(type, terminal.OriginalDefinition) ||
            facadeInput.Project is null ||
            !owners.Any(owner => owner.Project == facadeInput.Project && owner.Compilation.Assembly.Identity.Equals(facade.Identity))
            || use.Parent is not VariableDeclarationSyntax declaration || declaration.Variables.Count != 1 ||
            declaration.Variables[0].Initializer?.Value is not InvocationExpressionSyntax call ||
            model.GetSymbolInfo(call).Symbol is not IMethodSymbol method ||
            !SymbolEqualityComparer.Default.Equals(method.ReturnType, type) || method.IsGenericMethod ||
            method.ContainingType.ContainingType is not null || type.ContainingType is not null)
            return null;

        var libraries = consumer.References.Where(reference =>
            SymbolEqualityComparer.Default.Equals(reference.Symbol, method.ContainingAssembly)).ToArray();
        var facades = consumer.References.Where(reference =>
            SymbolEqualityComparer.Default.Equals(reference.Symbol, facade)).ToArray();
        var destinations = consumer.References.Where(reference =>
            SymbolEqualityComparer.Default.Equals(reference.Symbol, type.ContainingAssembly)).ToArray();
        if (libraries.Length != 1 || facades.Length != 1 || destinations.Length != 1 ||
            libraries[0].Input.Project is not { } libraryProject || destinations[0].Input.Project is null ||
            !owners.Any(owner => owner.Project == libraryProject && owner.Compilation.Assembly.Identity.Equals(method.ContainingAssembly.Identity)))
            return null;

        try
        {
            var source = Reader(libraries[0].Metadata);
            var bridge = Reader(facades[0].Metadata);
            var destination = Reader(destinations[0].Metadata);
            if (source is null || bridge is null || destination is null) return null;
            var libraryType = Definitions(source, method.ContainingType);
            if (libraryType.Length != 1 || Definitions(destination, type).Length != 1) return null;
            var methods = libraryType[0].GetMethods().Select(source.GetMethodDefinition)
                .Where(definition => source.GetString(definition.Name) == method.MetadataName).ToArray();
            if (methods.Length != 1) return null;
            var signature = source.GetBlobReader(methods[0].Signature);
            if (signature.ReadSignatureHeader().IsGeneric ||
                signature.ReadCompressedInteger() != method.Parameters.Length ||
                signature.ReadSignatureTypeCode() != SignatureTypeCode.TypeHandle)
                return null;
            var handle = signature.ReadTypeHandle();
            if (handle.Kind != HandleKind.TypeReference) return null;
            var typeReference = source.GetTypeReference((TypeReferenceHandle)handle);
            if (!MatchesType(source, typeReference.Namespace, typeReference.Name, type) ||
                !MatchesAssembly(source, typeReference.ResolutionScope, facade.Identity))
                return null;

            var exports = bridge.ExportedTypes.Select(bridge.GetExportedType).Where(export =>
                MatchesType(bridge, export.Namespace, export.Name, type)).ToArray();
            if (exports.Length != 1 || !exports[0].IsForwarder ||
                !MatchesAssembly(bridge, exports[0].Implementation, type.ContainingAssembly.Identity))
                return null;

            return new(libraries[0].Input.Sha256, facades[0].Input.Sha256, destinations[0].Input.Sha256,
                Name(method.ContainingType) + "." + method.MetadataName, Name(type),
                method.ContainingAssembly.Identity.GetDisplayName(), facade.Identity.GetDisplayName(),
                type.ContainingAssembly.Identity.GetDisplayName());
        }
        catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }

        static MetadataReader? Reader(PortableExecutableReference reference) =>
            reference.GetMetadata() is AssemblyMetadata assembly && assembly.GetModules().Length == 1
                ? assembly.GetModules()[0].GetMetadataReader() : null;

        static TypeDefinition[] Definitions(MetadataReader reader, INamedTypeSymbol symbol) => reader.TypeDefinitions
            .Select(reader.GetTypeDefinition).Where(row => MatchesType(reader, row.Namespace, row.Name, symbol)).ToArray();

        static bool MatchesType(MetadataReader reader, StringHandle space, StringHandle name, INamedTypeSymbol symbol) =>
            reader.GetString(name) == symbol.MetadataName &&
            reader.GetString(space) == (symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString());

        static bool MatchesAssembly(MetadataReader reader, EntityHandle handle, AssemblyIdentity expected)
        {
            if (handle.Kind != HandleKind.AssemblyReference) return false;
            var row = reader.GetAssemblyReference((AssemblyReferenceHandle)handle);
            var publicKey = System.Reflection.AssemblyFlags.PublicKey;
            if ((row.Flags & ~publicKey) != 0) return false;
            var key = (row.Flags & publicKey) != 0 ? expected.PublicKey : expected.PublicKeyToken;
            return reader.GetString(row.Name) == expected.Name && row.Version == expected.Version
                && reader.GetString(row.Culture) == (expected.CultureName ?? "")
                && (row.PublicKeyOrToken.IsNil ? key.IsDefaultOrEmpty : reader.GetBlobBytes(row.PublicKeyOrToken).SequenceEqual(key));
        }
    }

    private static AttributeSyntax? Attribute(SimpleNameSyntax name) => name.Parent switch
    {
        AttributeSyntax attribute => attribute,
        QualifiedNameSyntax qualified when qualified.Right == name => qualified.Parent as AttributeSyntax,
        AliasQualifiedNameSyntax qualified when qualified.Name == name => qualified.Parent as AttributeSyntax,
        _ => null,
    };

    private static bool IsTest(INamedTypeSymbol? symbol) => symbol?.TypeKind == TypeKind.Class &&
        (symbol.Name.EndsWith("Tests", StringComparison.Ordinal) ||
            symbol.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax type &&
                type.Members.OfType<MethodDeclarationSyntax>().Any(method =>
                    method.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
                        attribute.Name.ToString().Split('.').Last() is "Test" or "TestAttribute"))));

    private static string Name(INamedTypeSymbol type)
    {
        if (type.ContainingType is { } parent) return Name(parent) + "+" + type.MetadataName;
        var components = new Stack<string>();
        for (var space = type.ContainingNamespace; !space.IsGlobalNamespace; space = space.ContainingNamespace)
            components.Push(space.MetadataName);
        return components.Count == 0 ? type.MetadataName : string.Join('.', components) + "." + type.MetadataName;
    }

    private static (ReleaseImpactIdentity Id, string Source, int Start, int Length, string Name) SiteKey(ReleaseImpactTypeOrigin origin) =>
        (origin.Consumer, origin.Source, origin.Start, origin.Length, origin.Name);

    private static void Add(Dictionary<(ReleaseImpactIdentity Consumer, string Path), HashSet<string>> edges,
        ReleaseImpactIdentity consumer, string path, string kind) => Remember(edges, (consumer, path), [kind]);

    private static void Remember<TKey>(Dictionary<TKey, HashSet<string>> table, TKey key, IEnumerable<string> items) where TKey : notnull
    {
        if (!table.TryGetValue(key, out var existing)) table.Add(key, existing = new(StringComparer.Ordinal));
        existing.UnionWith(items);
    }

    private static ReleaseImpactEdge[] Edges(Dictionary<(ReleaseImpactIdentity Consumer, string Path), HashSet<string>> hits) =>
        hits.GroupBy(entry => entry.Key.Path, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ReleaseImpactEdge(group.Key, group.OrderBy(entry => entry.Key.Consumer.Project, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key.Consumer.FullName, StringComparer.Ordinal)
                .Select(entry => new ReleaseImpactConsumer(entry.Key.Consumer.Project, entry.Key.Consumer.FullName, Ordered(entry.Value)))
                .ToArray())).ToArray();

    private static string[] Ordered(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private sealed class Evaluation
    {
        internal Evaluation(BoundReleaseCompilation bound)
        {
            Project = bound.Input.Project;
            Digest = Convert.ToHexStringLower(SHA256.HashData(ReleaseImpactGraphBuilder.Serialize(bound.Input with
            {
                Sources = bound.Input.Sources.OrderBy(source => source.Path, StringComparer.Ordinal).ToArray(),
                References = bound.Input.References.OrderBy(reference => reference.Sha256, StringComparer.Ordinal)
                    .ThenBy(reference => reference.Project, StringComparer.Ordinal)
                    .ThenBy(reference => string.Join('\0', reference.Aliases), StringComparer.Ordinal)
                    .ThenBy(reference => reference.EmbedInteropTypes).ToArray(),
            })));
            Compilation = bound.Compilation;
            SourceHash = bound.Input.Sources.ToDictionary(source => source.Path, source => source.Sha256, StringComparer.Ordinal);
            Models = bound.Compilation.SyntaxTrees.ToDictionary(tree => tree, tree => bound.Compilation.GetSemanticModel(tree));
            References = bound.Compilation.References.Zip(bound.Input.References,
                (reference, input) => (bound.Compilation.GetAssemblyOrModuleSymbol(reference), input, (PortableExecutableReference)reference)).ToArray();
            Forwarders = References.Where(reference => reference.Symbol is IAssemblySymbol)
                .SelectMany(reference =>
                {
                    var assembly = (IAssemblySymbol)reference.Symbol!;
                    return assembly.GetForwardedTypes().Select(type => (Facade: assembly, reference.Input, Terminal: type));
                }).ToLookup(row => Name(row.Terminal), StringComparer.Ordinal);
            GlobalImports = Ordered(bound.Compilation.SyntaxTrees.Where(tree => tree.GetRoot().DescendantNodes()
                .OfType<UsingDirectiveSyntax>().Any(directive => directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)))
                .Select(tree => tree.FilePath));
        }

        internal string Project { get; }
        internal string Digest { get; }
        internal CSharpCompilation Compilation { get; }
        internal Dictionary<string, string> SourceHash { get; }
        internal Dictionary<SyntaxTree, SemanticModel> Models { get; }
        internal (ISymbol? Symbol, ReleaseCompilerReference Input, PortableExecutableReference Metadata)[] References { get; }
        internal ILookup<string, (IAssemblySymbol Facade, ReleaseCompilerReference Input, INamedTypeSymbol Terminal)> Forwarders { get; }
        internal string[] GlobalImports { get; }
    }
}
