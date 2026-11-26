using System.Reflection.Metadata;
using System.Text.Json;
using Antiphon.ReleaseSelection;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseImpactRelationTests
{

    [Test]
    [Timeout(30_000)]
    public void UniqueBindingsPruneOnlyTheirOwnContributions(CancellationToken cancellationToken)
    {
        const string project = "tests/T/T.csproj";
        const string north = "tests/T/A.cs";
        const string south = "tests/T/B.cs";
        const string use = "tests/T/Use.cs";
        const string generatedPath = "tests/T/G.g.cs";
        const string part = "tests/T/Part.cs";
        const string args = "tests/T/Arg.cs";
        const string decoy = "tests/T/D.cs";
        const string receiver = "tests/T/R.cs";
        using var fixture = new ReleaseImpactTypeProofFixture();

        var q = Check("Q.unique", World("using North;", "_ = typeof(Probe);"), [north], [north, south]);
        var occurrence = q.Sites.Single(site => site.Origin.Source == use && site.Origin.Name == "Probe");
        occurrence.MetadataName.ShouldBe("North.Probe");
        occurrence.Winners.ShouldBe([north]);
        occurrence.Retained.ShouldBe([north]);
        occurrence.Origin.Start.ShouldBe(World("using North;", "_ = typeof(Probe);")[use].IndexOf("Probe", StringComparison.Ordinal));
        occurrence.Origin.Length.ShouldBe(5);
        occurrence.SourceSha256.Length.ShouldBe(64);
        occurrence.ContextDigest.Length.ShouldBe(64);
        Check("Q.qualified", World("", "_ = typeof(global::North.Probe);"), [north], [north, south]);
        var block = World("", "");
        block[use] = "namespace Cases { using North; public partial class SpecTests { public void Check() { _ = typeof(Probe); } } }\n";
        Check("Q.block", block, [north], [north, south]);
        Check("Q.ambiguous", World("using North; using South;", "_ = typeof(Probe);"), [north, south], refusal: "compiler-diagnostics");
        var mixed = Check("Q.mixed", World("", "_ = typeof(global::North.Probe); _ = typeof(South.Probe);"), [north, south]);
        mixed.Sites.Where(site => site.Origin.Name == "Probe").Select(site => site.Retained.Single()).ShouldBe([north, south]);
        var overlap = World("using North;", "_ = typeof(Probe); _ = \"tests/T/B.cs\";");
        overlap["Directory.Build.props"] = "<Project />\n";
        overlap["tests/T/Other.cs"] = "namespace Cases; public class OtherTests { public void Check() { _ = typeof(South.Probe); } }\n";
        var kinds = Check("Q.kinds", overlap, [north]);
        Consumer(kinds.Refined, south, "Cases.SpecTests").Kinds.ShouldBe(["path"]);
        Consumer(kinds.Refined, south, "Cases.OtherTests").Kinds.ShouldBe(["type"]);
        Consumer(kinds.Refined, use, "Cases.SpecTests").Kinds.ShouldBe(["declaration"]);
        Consumer(kinds.Refined, "Directory.Build.props", "Cases.SpecTests").Kinds.ShouldBe(["global"]);
        var limited = World("using North;", "_ = typeof(Probe);");
        limited[decoy] = "namespace Third; public class Probe {}\n";
        Check("Q.limit", limited, [north, south, decoy], maximumCandidates: 2)
            .Sites.Single(site => site.Origin.Name == "Probe").Rule.ShouldBe("proof-candidate-limit");

        var typeAlias = Check("AL.type", World("using P = North.Probe;", "_ = typeof(P);"), [north], [north, south]);
        Consumer(typeAlias.Refined, use, "Cases.SpecTests").Kinds.ShouldContain("directive", "AL.type directive");
        Check("AL.namespace", World("using N = North;", "_ = typeof(N.Probe);"), [north]);
        Check("AL.rebound", World("using P = South.Probe;", "_ = typeof(P);"), [south], [north, south]);
        Check("AL.duplicate", World("using P = North.Probe; using P = South.Probe;", "_ = typeof(P);"), [north, south], refusal: "compiler-diagnostics");
        Check("AL.failed", World("using P = Missing.Probe;", "_ = typeof(P);"), [north, south], refusal: "compiler-diagnostics");

        var partial = World("using North;", "_ = typeof(Probe);");
        partial[north] = "namespace North; public partial class Probe {}\n";
        var generated = new Dictionary<string, string> { [generatedPath] = "namespace North; public partial class Probe { public int Count; }\n" };
        Check("P.parts", partial, [north, generatedPath], [north, south, generatedPath], generated: generated);
        Check("P.unbound", partial, [north, south, generatedPath], generated: generated,
            refusal: "compiler-generated-source-unbound", unavailableGenerated: generatedPath);
        partial[part] = "namespace Cases; public partial class SpecTests { public void Second() { _ = typeof(South.Probe); } }\n";
        var members = Check("P.consumer", partial, [north, south, generatedPath], generated: generated);
        Consumer(members.Refined, part, "Cases.SpecTests").Kinds.ShouldBe(["declaration"]);

        var globals = World("", "_ = typeof(Probe);");
        globals["tests/T/One.cs"] = "global using North;\n";
        Check("GU.import", globals, [north]);
        globals["tests/T/Two.cs"] = "global using South;\n";
        Check("GU.ambiguous", globals, [north, south], refusal: "compiler-diagnostics");
        var globalAlias = World("", "_ = typeof(P);");
        globalAlias["tests/T/One.cs"] = "global using P = North.Probe;\n";
        Check("GU.alias", globalAlias, [north]);


        const string otherProject = "tests/U/U.csproj";
        var linked = World("", "_ = typeof(Probe);");
        linked[otherProject] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n";
        linked["tests/T/One.cs"] = "global using North;\n";
        linked["tests/U/One.cs"] = "global using South;\n";
        var linkedProof = Check("GU.owners", linked, [north], contexts:
        [
            new(project, "T", [north, south, use, "tests/T/One.cs"], [], [fixture.Core]),
            new(otherProject, "U", [north, south, use, "tests/U/One.cs"], [], [fixture.Core]),
        ]);
        linkedProof.Refined.Edges.Single(edge => edge.Path == south).Classes
            .Where(consumer => consumer.Kinds.Contains("type", StringComparer.Ordinal))
            .Select(consumer => consumer.Project).ShouldBe([otherProject], "GU.owners U");

        const string one = "src/One/One.csproj";
        const string two = "src/Two/Two.csproj";
        const string oneSource = "src/One/A.cs";
        const string twoSource = "src/Two/B.cs";
        const string externSource = "namespace Ident; public class Probe {}\n";
        var refOne = fixture.Emit("One", externSource, ["L"]) with { Project = one };
        var refTwo = fixture.Emit("Two", externSource, ["R"]) with { Project = two };
        var externs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [project] = "<Project />",
            [one] = "<Project />",
            [two] = "<Project />",
            [oneSource] = externSource,
            [twoSource] = externSource,
            [use] = ConsumerSource("extern alias L;", "_ = typeof(L::Ident.Probe);"),
        };
        ReleaseImpactTypeProofFixture.Context[] ExternContexts(ReleaseCompilerReference referenceOne) =>
        [
            new(project, "T", [use], [], [fixture.Core, referenceOne, refTwo]),
            new(one, "One", [oneSource], [], [fixture.Core]),
            new(two, "Two", [twoSource], [], [fixture.Core]),
        ];
        Check("EX.left", externs, [oneSource], contexts: ExternContexts(refOne)).Sites
            .Single(site => site.Origin.Name == "Probe").Assembly!.StartsWith("One,", StringComparison.Ordinal).ShouldBeTrue("EX.left assembly");
        Check("EX.unbound", externs, [oneSource, twoSource], contexts: ExternContexts(refOne with { Aliases = ["Other"] }),
            refusal: "compiler-diagnostics");
        externs[use] = ConsumerSource("extern alias R;", "_ = typeof(R::Ident.Probe);");
        Check("EX.right", externs, [twoSource], contexts: ExternContexts(refOne));

        var nested = World("using static North.Box;", "_ = typeof(Inner);");
        nested[north] = "namespace North; public class Box { public class Inner {} } public class Derived : Box {}\n";
        nested[south] = "namespace South; public class Box { public class Inner {} }\n";
        Check("N.static", nested, [north]);
        nested[use] = ConsumerSource("", "_ = typeof(North.Derived.Inner);");
        Check("N.inherited", nested, [north]).Sites.Single(site => site.Origin.Name == "Inner").MetadataName.ShouldBe("North.Box+Inner");
        nested[use] = ConsumerSource("using static North.Box; using static South.Box;", "_ = typeof(Inner);");
        Check("N.ambiguous", nested, [north, south], refusal: "compiler-diagnostics");
        nested[north] = nested[north].Replace("Derived : Box", "Derived : MissingBase", StringComparison.Ordinal);
        nested[use] = ConsumerSource("", "_ = typeof(North.Derived.Inner);");
        Check("N.unbound-base", nested, [north, south], refusal: "compiler-diagnostics");

        var generics = World("using North;", "_ = typeof(G<Arg>);");
        generics[north] = "namespace North; public class G {}\n";
        generics[south] = "namespace North; public class G<T> {}\n";
        generics[decoy] = "namespace South; public class G<T> {}\n";
        generics[args] = "namespace North; public class Arg {}\n";
        Check("AR.argument", generics, [args, south]).Sites.Single(site => site.Origin.Name == "G").MetadataName.ShouldBe("North.G\u00601");
        generics[use] = ConsumerSource("using North; using South;", "_ = typeof(G<Arg>);");
        Check("AR.ambiguous", generics, [north, args, south, decoy], refusal: "compiler-diagnostics");
        generics[use] = ConsumerSource("using North;", "_ = typeof(G);");
        Check("AR.zero", generics, [north]);
        generics[use] = ConsumerSource("using North;", "_ = typeof(G<MissingArg>);");
        Check("AR.failed-argument", generics, [north, south, decoy], refusal: "compiler-diagnostics");
        generics[use] = ConsumerSource("using North;", "_ = typeof(G<MissingArg>);").TrimEnd()[..^1]
            + " public void Constraint<X>() where X : North.Arg {} }\n";
        Check("AR.constraint", generics, [north, args, south, decoy], refusal: "compiler-diagnostics");

        var extension = World("using North;", "_ = new Model.R().Pick();");
        extension[north] = "namespace North; public static class Ext { public static int Pick(this Model.R r) => 1; }\n";
        extension[south] = "namespace South; public static class Ext { public static int Pick(this Model.R r) => 2; }\n";
        extension[receiver] = "namespace Model; public class R {}\n";
        Check("EXT.receiver", extension, [north, receiver]).Sites.Single(site => site.Origin.Name == "Pick").Retained.ShouldBe([north, receiver]);
        extension[use] = ConsumerSource("using North;", "_ = new Model.R().Pick(); _ = typeof(North.Ext);");
        Check("EXT.overlap", extension, [north, receiver]).Sites
            .Single(site => site.Origin.Name == "Ext").Retained.ShouldBe([north]);
        extension[use] = ConsumerSource("using North; using South;", "_ = new Model.R().Pick();");
        Check("EXT.ambiguous", extension, [north, south, receiver], refusal: "compiler-diagnostics");
        extension[use] = ConsumerSource("using North;", "_ = new Model.R().Pick();");
        extension[receiver] = "namespace Model; public class R { public int Pick() => 3; }\n";
        Check("EXT.instance", extension, [north, south, receiver]);
        extension[receiver] = "namespace Model; public class R {}\n";
        extension[use] = ConsumerSource("using North;", "dynamic r = new Model.R(); _ = r.Pick();");
        var runtimeBinder = fixture.PinAssembly(typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly);
        Check("EXT.dynamic", extension, [north, south, receiver], contexts:
        [
            new(project, "T", [north, south, receiver, use], [], [fixture.Core, runtimeBinder]),
        ]);

        const string facadeProject = "src/Facade/Facade.csproj";
        const string facadePath = "src/Facade/F.cs";
        const string libraryProject = "src/Library/Library.csproj";
        const string apiPath = "src/Library/API.cs";
        const string destinationProject = "src/Dest/Destination.csproj";
        const string destinationPath = "src/Dest/P.cs";
        const string definition = "namespace M; public class Probe {}\n";
        const string typeForwarder = "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(M.Probe))]\n";
        const string api = "public static class API { public static M.Probe Get() => null; }\n";
        var oldFacade = fixture.Emit("Facade", definition, []);
        var library = fixture.Emit("Library", api, [], fixture.Open(oldFacade)) with { Project = libraryProject };
        var destination = fixture.Emit("Destination", definition, []) with { Project = destinationProject };
        var facade = fixture.Emit("Facade", typeForwarder, [], fixture.Open(destination)) with { Project = facadeProject };
        fixture.InspectPe(library, metadata =>
        {
            var type = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
                .Single(row => metadata.GetString(row.Name) == "API");
            var method = type.GetMethods().Select(metadata.GetMethodDefinition)
                .Single(row => metadata.GetString(row.Name) == "Get");
            var signature = metadata.GetBlobReader(method.Signature);
            signature.ReadSignatureHeader().Kind.ShouldBe(SignatureKind.Method);
            signature.ReadCompressedInteger().ShouldBe(0);
            signature.ReadSignatureTypeCode().ShouldBe(SignatureTypeCode.TypeHandle);
            var reference = metadata.GetTypeReference((TypeReferenceHandle)signature.ReadTypeHandle());
            var owner = metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope);
            return (metadata.GetString(reference.Namespace), metadata.GetString(reference.Name), metadata.GetString(owner.Name));
        }, ("M", "Probe", "Facade"), "FW.chain independent Library signature");
        fixture.InspectPe(facade, metadata =>
        {
            var export = metadata.GetExportedType(metadata.ExportedTypes.Single());
            var owner = metadata.GetAssemblyReference((AssemblyReferenceHandle)export.Implementation);
            return (metadata.GetString(export.Namespace), metadata.GetString(export.Name), metadata.GetString(owner.Name), export.IsForwarder);
        }, ("M", "Probe", "Destination", true), "FW.chain independent Facade metadata");
        fixture.InspectPe(destination, metadata => metadata.TypeDefinitions.Select(metadata.GetTypeDefinition)
            .Count(row => metadata.GetString(row.Namespace) == "M" && metadata.GetString(row.Name) == "Probe"),
            1, "FW.chain independent Destination definition");
        var forwardSources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [project] = "<Project />",
            [facadeProject] = "<Project />",
            [libraryProject] = "<Project />",
            [destinationProject] = "<Project />",
            [facadePath] = typeForwarder,
            [apiPath] = api,
            [destinationPath] = definition,
            [south] = "namespace South; public class Probe {}\n",
            [use] = ConsumerSource("using M;", "Probe p = API.Get();"),
        };
        ReleaseImpactTypeProofFixture.Context[] ForwardContexts(ReleaseCompilerReference[] consumerReferences) =>
        [
            new(project, "T", [south, use], [], consumerReferences),
            new(facadeProject, "Facade", [facadePath], [], [fixture.Core, destination]),
            new(libraryProject, "Library", [apiPath], [], [fixture.Core, oldFacade]),
            new(destinationProject, "Destination", [destinationPath], [], [fixture.Core]),
        ];
        var chain = Check("FW.chain", forwardSources, [destinationPath, apiPath], [destinationPath, apiPath, south],
            contexts: ForwardContexts([fixture.Core, library, facade, destination]));
        Consumer(chain.Refined, facadePath, "Cases.SpecTests").Kinds.ShouldContain("compiler-forwarder", "FW.chain facade");
        using (var chainProof = JsonDocument.Parse(ReleaseImpactGraphBuilder.Serialize(
            chain.Sites.Single(site => site.Origin.Name == "Probe"))))
        {
            chainProof.RootElement.TryGetProperty("forwarding", out var hop).ShouldBeTrue("FW.chain PE evidence");
            hop.GetProperty("librarySha256").GetString().ShouldBe(library.Sha256, "FW.chain Library PE");
            hop.GetProperty("facadeSha256").GetString().ShouldBe(facade.Sha256, "FW.chain Facade PE");
            hop.GetProperty("destinationSha256").GetString().ShouldBe(destination.Sha256, "FW.chain Destination PE");
            hop.GetProperty("returnType").GetString().ShouldBe("M.Probe", "FW.chain Library return TypeRef");
        }
        var brokenChain = Check("FW.missing", forwardSources, [destinationPath, apiPath, south],
            contexts: ForwardContexts([fixture.Core, library, facade]), refusal: "compiler-diagnostics");
        Consumer(brokenChain.Refined, facadePath, "Cases.SpecTests").Kinds.ShouldContain("compiler-forwarder", "FW.missing facade");
        var unmapped = Check("FW.owner", forwardSources, [destinationPath, apiPath, south],
            contexts: ForwardContexts([fixture.Core, library, facade with { Project = libraryProject }, destination]));
        unmapped.Sites.Single(site => site.Origin.Name == "Probe").Rule.ShouldBe("compiler-forwarder-unbound", "FW.owner");

        var shadowMembers = new[]
        {
            "public void S() { int Probe = 1; _ = Probe; }",
            "int Probe => 1; public void S() { _ = Probe; }",
            "int Probe = 1; public void S() { _ = this.Probe; }",
            "public void S(int Probe) { _ = Probe; }",
            "public void S<Probe>() { _ = typeof(Probe); }",
        };
        foreach (var member in shadowMembers)
        {
            var shadow = World("using North;", "_ = typeof(global::North.Probe);");
            shadow[use] = shadow[use].TrimEnd()[..^1] + " " + member + " }\n";
            Check("SH.unsupported " + member, shadow, [north, south]).Sites
                .Select(site => site.Rule).ShouldContain("type-binding-unproven");
        }

        var conditional = World("", "_ = typeof(Pick);");
        conditional[use] = """
            #if FAST
            using Pick = North.Probe;
            #else
            using Pick = South.Probe;
            #endif
            namespace Cases;
            public partial class SpecTests { public void Check() { _ = typeof(Pick); } }
            """ + "\n";
        var fast = new ReleaseImpactTypeProofFixture.Context(project, "T", [north, south, use], ["FAST"], [fixture.Core]);
        var regular = fast with { Defines = [], Framework = "net8.0" };
        Check("CC.fast", conditional, [north], contexts: [fast]);
        Check("CC.regular", conditional, [south], contexts: [regular]);
        Check("CC.union", conditional, [north, south], contexts: [fast, regular]);
        Check("CC.missing", conditional, [north, south], contexts: [fast, regular],
            refusal: "compiler-manifest-digest-mismatch", proposal: manifest => manifest with { Contexts = [manifest.Contexts[0]] });

        var errors = World("using North;", "_ = typeof(Probe);");
        errors["tests/T/Broken.cs"] = "namespace Cases; public class Broken : MissingBase {}\n";
        Check("ER.compilation", errors, [north, south], refusal: "compiler-diagnostics");
        Check("ER.lookup", World("", "_ = typeof(Missing.Probe);"), [north, south], refusal: "compiler-diagnostics");
        Check("ER.parse", World("using North;", "_ = typeof(Probe ;"), [north, south], refusal: "compiler-diagnostics");
        Check("ER.unbounded", World("", "_ = typeof(Missing.NoSuchName);"), [], refusal: "compiler-diagnostics")
            .Refined.Fallback.ShouldBe("Full");

        foreach (var (kind, declaration, reference) in new[]
        {
            ("delegate", "public delegate T Tag<T>();", "Tag<int>"),
            ("interface", "public interface Tag {}", "Tag"),
            ("struct", "public struct Tag {}", "Tag"),
            ("enum", "public enum Tag { One }", "Tag"),
            ("nested-delegate", "public class Outer<T> { public delegate U Hook<U>(T t); }", "Outer<int>.Hook<string>"),
        })
        {
            var forms = World("using North;", "_ = typeof(" + reference + ");");
            forms[north] = "namespace North; " + declaration + "\n";
            forms[south] = "namespace South; " + declaration + "\n";
            Check("OT." + kind, forms, [north]);
            forms[use] = ConsumerSource("using North; using South;", "_ = typeof(" + reference + ");");
            Check("OT.ambiguous " + kind, forms, [north, south], refusal: "compiler-diagnostics");
        }
        var attributes = World("", "");
        attributes[north] = "namespace North; public class MarkAttribute : System.Attribute {}\n";
        attributes[south] = "namespace South; public class Mark : System.Attribute {}\n";
        attributes[use] = "using North; namespace Cases; public partial class SpecTests { [Mark] public void Check() {} }\n";
        Check("OT.winner", attributes, [north]).Sites.Single(site => site.Origin.Name == "Mark").Winners.ShouldBe([north]);
        attributes[use] = "using North; using South; namespace Cases; public partial class SpecTests { [Mark] public void Check() {} }\n";
        Check("OT.attribute", attributes, [north, south], refusal: "compiler-diagnostics");

        ReleaseImpactTypeProof Check(string label, Dictionary<string, string> files, string[] expected,
            string[]? before = null, Dictionary<string, string>? generated = null,
            ReleaseImpactTypeProofFixture.Context[]? contexts = null, string? refusal = null,
            Func<ReleaseCompilerManifest, ReleaseCompilerManifest>? proposal = null, int maximumCandidates = 4096,
            string? unavailableGenerated = null) => CheckTypeProof(fixture, cancellationToken, label, files, expected, before,
                generated, contexts, refusal, proposal, maximumCandidates, unavailableGenerated);
    }

    [Test, Timeout(30_000)]
    public async Task SdkGeneratedUsingsRetainOnlyPinnedBindings(CancellationToken cancellationToken)
    {
        const string project = "tests/T/T.csproj";
        const string north = "tests/T/A.cs";
        const string south = "tests/T/B.cs";
        const string use = "tests/T/Use.cs";
        using var fixture = new ReleaseImpactTypeProofFixture();

        const string implicitPath = "tests/T/T.GlobalUsings.g.cs";
        var implicitFiles = World("", "_ = typeof(Probe);");
        string[] sdkProjects =
        [
            SdkProject("""<Using Include="North" />"""),
            SdkProject("""<Using Include="North.Probe" Alias="P" />"""),
            SdkProject("""<Using Include="North" /><Using Include="South" />"""),
        ];
        var sdk = await fixture.GenerateGlobalUsingsAsync(sdkProjects, cancellationToken);
        string[] implicitNamespaces = ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http",
            "System.Threading", "System.Threading.Tasks"];
        var expectedImplicit = implicitNamespaces.Select(name => $"global using {name};").ToArray();
        sdk.Generated[0].ShouldBe(SdkOutput(["global using North;", ..expectedImplicit]), "IU actual SDK bytes");
        sdk.Generated[1].ShouldBe(SdkOutput([..expectedImplicit, "global using P = North.Probe;"]), "IU actual SDK alias bytes");
        sdk.Generated[2].ShouldBe(SdkOutput(["global using North;", "global using South;", ..expectedImplicit]), "IU actual SDK ambiguity bytes");
        ReleaseImpactTypeProofFixture.Context[] ImplicitContexts() =>
            [new(project, "T", [north, south, use, implicitPath], [], sdk.References)];
        implicitFiles[project] = sdkProjects[0];
        var imports = new Dictionary<string, string>
        {
            [implicitPath] = sdk.Generated[0],
        };
        var implicitGraph = CheckTypeProof(fixture, cancellationToken, "IU.implicit", implicitFiles, [north], [north, south],
            generated: imports, contexts: ImplicitContexts());
        Consumer(implicitGraph.Refined, implicitPath, "Cases.SpecTests").Kinds.ShouldBe(["directive"], "IU pinned generated directive");
        implicitFiles[project] = sdkProjects[1];
        implicitFiles[use] = ConsumerSource("", "_ = typeof(P);");
        imports[implicitPath] = sdk.Generated[1];
        CheckTypeProof(fixture, cancellationToken, "IU.alias", implicitFiles, [north], [north, south],
            generated: imports, contexts: ImplicitContexts());
        implicitFiles[project] = sdkProjects[2];
        implicitFiles[use] = ConsumerSource("", "_ = typeof(Probe);");
        imports[implicitPath] = sdk.Generated[2];
        CheckTypeProof(fixture, cancellationToken, "IU.ambiguous", implicitFiles, [north, south], generated: imports, contexts: ImplicitContexts(), refusal: "compiler-diagnostics");
        implicitFiles[project] = sdkProjects[0];
        imports[implicitPath] = sdk.Generated[0];
        CheckTypeProof(fixture, cancellationToken, "IU.missing", implicitFiles, [north, south], generated: imports, contexts: ImplicitContexts(),
            refusal: "compiler-manifest-digest-mismatch",
            proposal: manifest => manifest with { Contexts = [manifest.Contexts[0] with
            {
                Sources = manifest.Contexts[0].Sources.Where(source => source.Path != implicitPath).ToArray(),
            }] });

        static string SdkProject(string items) =>
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework>"
            + "<ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>" + items + "</ItemGroup></Project>\n";
        static string SdkOutput(string[] lines) => string.Join(Environment.NewLine, new[] { "// <auto-generated/>" }.Concat(lines)) + Environment.NewLine;
    }

    private static ReleaseImpactTypeProof CheckTypeProof(ReleaseImpactTypeProofFixture fixture,
        CancellationToken cancellationToken, string label, Dictionary<string, string> files, string[] expected,
        string[]? before = null, Dictionary<string, string>? generated = null,
        ReleaseImpactTypeProofFixture.Context[]? contexts = null, string? refusal = null,
        Func<ReleaseCompilerManifest, ReleaseCompilerManifest>? proposal = null, int maximumCandidates = 4096,
        string? unavailableGenerated = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const string project = "tests/T/T.csproj";
        const string use = "tests/T/Use.cs";
        generated ??= new(StringComparer.Ordinal);
        contexts ??= [new(project, "T", files.Keys.Concat(generated.Keys).Where(path => path.EndsWith(".cs", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray(), [], [fixture.Core])];
        var binding = fixture.Capture(files, generated, contexts, proposal, unavailableGenerated);
        binding.Eligible.ShouldBe(refusal is null, label + " fixture: " + binding.Reason);
        if (refusal is not null) binding.Reason.ShouldBe(refusal, label);
        var proof = ReleaseImpactGraphBuilder.BuildWithTypeProof(files, binding, maximumCandidates);
        TypePaths(proof.Refined).ShouldBe(expected.Order(StringComparer.Ordinal), label);
        if (refusal is not null) TypePaths(proof.Conservative).ShouldBe(expected.Order(StringComparer.Ordinal), label + " include all");
        if (before is not null) TypePaths(proof.Conservative).ShouldBe(before.Order(StringComparer.Ordinal), label + " before");
        Consumer(proof.Refined, use, "Cases.SpecTests").Kinds.ShouldContain("declaration", label + " declaration");

        var reversedFiles = files.Reverse().ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var reversedGenerated = generated.Reverse().ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var reversedContexts = contexts.Reverse().Select(context => context with
        {
            SourcePaths = context.SourcePaths.Reverse().ToArray(),
            References = context.References.Reverse().ToArray(),
        }).ToArray();
        var reversed = fixture.Capture(reversedFiles, reversedGenerated, reversedContexts, proposal, unavailableGenerated, permuted: true);
        reversed.Reason.ShouldBe(binding.Reason, label + " reordered input verdict");
        var reversedProof = ReleaseImpactGraphBuilder.BuildWithTypeProof(reversedFiles, reversed, maximumCandidates);
        // The raw authority pin binds the capture's exact bytes. The graph
        // and context identities have their own canonical ordering.
        ReleaseImpactGraphBuilder.Serialize(reversedProof with { ManifestDigest = null })
            .ShouldBe(ReleaseImpactGraphBuilder.Serialize(proof with { ManifestDigest = null }), label + " reordered graph bytes");
        var orderedSites = proof.Sites.Select(site => (site.Origin.Consumer.Project, site.Origin.Consumer.FullName,
            site.Origin.Source, site.Origin.Start, site.ContextDigest)).ToArray();
        orderedSites.SequenceEqual(orderedSites.Distinct().OrderBy(site => site.Project, StringComparer.Ordinal)
            .ThenBy(site => site.FullName, StringComparer.Ordinal).ThenBy(site => site.Source, StringComparer.Ordinal)
            .ThenBy(site => site.Start).ThenBy(site => site.ContextDigest, StringComparer.Ordinal))
            .ShouldBeTrue(label + " stored site order");
        foreach (var site in proof.Sites.Where(site => site.Rule == "unique-source-type"))
        {
            var source = generated.TryGetValue(site.Origin.Source, out var emitted) ? emitted : files[site.Origin.Source];
            var expectedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)));
            site.SourceSha256.ShouldBe(expectedHash, label + " use-site source hash");
            var span = source.AsSpan(site.Origin.Start, site.Origin.Length).ToString();
            (span == site.Origin.Name || span.StartsWith(site.Origin.Name + "<", StringComparison.Ordinal))
                .ShouldBeTrue(label + " source span " + span);
            site.Assembly.ShouldNotBeNullOrEmpty(label + " assembly");
            site.MetadataName.ShouldNotBeNullOrEmpty(label + " metadata name");
            site.Winners.ShouldNotBeEmpty(label + " known winner");
            site.Retained.ShouldBe(site.Winners, label + " retained winner");
            site.Winners.ShouldAllBe(path => site.Origin.Candidates.Contains(path, StringComparer.Ordinal)
                && (files.ContainsKey(path) || generated.ContainsKey(path)), label + " proved winner belongs to the frozen input");
        }
        return proof;
    }

    private static Dictionary<string, string> World(string imports, string body) => new(StringComparer.Ordinal)
    {
        ["tests/T/T.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework>"
            + "<AssemblyName>T</AssemblyName><LangVersion>12</LangVersion><ImplicitUsings>disable</ImplicitUsings>"
            + "<Deterministic>true</Deterministic></PropertyGroup></Project>\n",
        ["tests/T/A.cs"] = "namespace North; public class Probe {}\n",
        ["tests/T/B.cs"] = "namespace South; public class Probe {}\n",
        ["tests/T/Use.cs"] = ConsumerSource(imports, body),
    };

    private static string ConsumerSource(string imports, string body) => imports + "\n" +
        "namespace Cases; public partial class SpecTests { public void Check() { " + body + " } }\n";

    private static IEnumerable<string> TypePaths(ReleaseImpactGraph graph) => graph.Edges
        .Where(edge => edge.Classes.Any(consumer => consumer.Project == "tests/T/T.csproj" && consumer.FullName == "Cases.SpecTests"
            && consumer.Kinds.Contains("type", StringComparer.Ordinal)))
        .Select(edge => edge.Path);

    private static ReleaseImpactConsumer Consumer(ReleaseImpactGraph graph, string path, string name) =>
        graph.Edges.Single(edge => edge.Path == path).Classes.Single(consumer => consumer.Project == "tests/T/T.csproj" && consumer.FullName == name);

    [Test]
    public void AllTwentySixConsumersSurvive()
    {
        var sources = Sources();
        sources["shared/input.txt"] = "input";
        sources["tests/A/Consumers.cs"] = string.Join('\n', Enumerable.Range(0, 26).Select(i =>
            $"namespace Example {{ public class Consumer{i:00}Tests {{ [Test] public void Runs() {{ var path = \"shared/input.txt\"; }} }} }}"));
        var graph = Build(sources);
        Members(graph, "shared/input.txt").ShouldBe(Enumerable.Range(0, 26)
            .Select(i => $"tests/A/A.csproj::Example.Consumer{i:00}Tests"));
        AssertIncomplete(graph);
    }

    [Test]
    public void GlobalInputExpandsConsumers()
    {
        var sources = Sources();
        sources["tests/A/First.cs"] = "namespace A; public class FirstTests { [Test] public void Runs() {} }";
        sources["tests/B/B.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />";
        sources["tests/B/Second.cs"] = "namespace B; public class SecondTests { [Test] public void Runs() {} }";
        string[] expected = ["tests/A/A.csproj::A.FirstTests", "tests/B/B.csproj::B.SecondTests"];
        foreach (var path in new[] { "Antiphon.sln", "Directory.Build.props", "Directory.Build.targets", "global.json" })
            sources[path] = "global input";
        var graph = Build(sources);
        foreach (var path in new[] { "Antiphon.sln", "Directory.Build.props", "Directory.Build.targets", "global.json" })
            Members(graph, path).ShouldBe(expected, path);
        AssertIncomplete(graph);
    }

    [Test]
    public void QualifiedDuplicateNamesSurvive()
    {
        var sources = Sources();
        sources["shared/input.txt"] = "input";
        sources["tests/A/One.cs"] = "namespace One; public partial class ProbeTests { [Test] public void Runs() { var path = \"shared/input.txt\"; } }";
        sources["tests/A/Partial.cs"] = "namespace One; public partial class ProbeTests { }";
        sources["tests/A/Two.cs"] = "namespace Two; public class ProbeTests { [Test] public void Runs() { var path = \"shared/input.txt\"; } }";
        sources["tests/B/B.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />";
        sources["tests/B/One.cs"] = sources["tests/A/One.cs"];
        var graph = Build(sources);
        Members(graph, "shared/input.txt").ShouldBe(new[]
        {
            "tests/A/A.csproj::One.ProbeTests", "tests/A/A.csproj::Two.ProbeTests", "tests/B/B.csproj::One.ProbeTests",
        });
        var declarations = graph.GetProperty("universe").EnumerateArray()
            .Single(x => Identity(x) == "tests/A/A.csproj::One.ProbeTests").GetProperty("declarationPaths");
        declarations.EnumerateArray().Select(x => x.GetString()).ShouldBe(new[] { "tests/A/One.cs", "tests/A/Partial.cs" });
        AssertIncomplete(graph);
    }

    [Test]
    public void RawStringIsNotAClass()
    {
        var sources = Sources();
        sources["tests/A/Real.cs"] = """"
            namespace Census;
            internal partial class Container<T> {
                internal partial class Actual<U> {
                    [Test] public void Runs() { var text = """public class PhantomTests { [Test] public void Fake() {} }"""; }
                }
            }
            // public class CommentTests {}
            """";
        sources["tests/A/Other.cs"] = "namespace Census; internal partial class Container<T> { internal partial class Actual<U> {} }";
        var graph = Build(sources);
        graph.GetProperty("universe").EnumerateArray().Select(Identity)
            .ShouldBe(new[] { "tests/A/A.csproj::Census.Container`1+Actual`1" });
        Members(graph, "tests/A/Real.cs").ShouldBe(new[] { "tests/A/A.csproj::Census.Container`1+Actual`1" });
        AssertIncomplete(graph);
    }

    // CARD-1868 A-1. A delegate is not a BaseTypeDeclarationSyntax, so a file declaring only
    // delegates (top-level, generic or nested in a class no consumer names) got no type edge.
    [Test]
    public void DelegateOnlyFileGetsTypeEdge()
    {
        var sources = Sources();
        sources["shared/Callbacks.cs"] = "namespace Shared; public delegate void ProbeCallback(int value); public delegate T ProbeFactory<T>();";
        sources["shared/Holder.cs"] = "namespace Shared; public class ProbeHolder { public delegate void ProbeNested(); }";
        sources["tests/A/Consumer.cs"] = """
            namespace A;
            public class CallbackTests { [Test] public void Runs() { ProbeCallback callback = _ => { }; } }
            public class FactoryTests { [Test] public void Runs() { ProbeFactory<int> factory = () => 1; } }
            public class NestedTests : Shared.ProbeBase { [Test] public void Runs() { ProbeNested nested = () => { }; } }
            """;
        var graph = Build(sources);
        Members(graph, "shared/Callbacks.cs").ShouldBe(new[] { "tests/A/A.csproj::A.CallbackTests", "tests/A/A.csproj::A.FactoryTests" });
        Kinds(graph, "shared/Callbacks.cs").ShouldBe(new[] { "type" });
        Members(graph, "shared/Holder.cs").ShouldBe(new[] { "tests/A/A.csproj::A.NestedTests" });
        Kinds(graph, "shared/Holder.cs").ShouldBe(new[] { "type" });
        AssertIncomplete(graph);
    }

    private static string[] Kinds(JsonElement graph, string path) => graph.GetProperty("edges").EnumerateArray()
        .Where(x => x.GetProperty("path").GetString() == path).SelectMany(x => x.GetProperty("classes").EnumerateArray())
        .SelectMany(x => x.GetProperty("kinds").EnumerateArray()).Select(x => x.GetString()!).Distinct().Order(StringComparer.Ordinal).ToArray();

    private static Dictionary<string, string> Sources() => new(StringComparer.Ordinal)
    {
        ["tests/A/A.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />",
    };

    private static string Identity(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : value.GetProperty("project").GetString() + "::" + value.GetProperty("fullName").GetString();

    private static string[] Members(JsonElement graph, string path) => graph.GetProperty("edges").EnumerateArray()
        .Where(x => x.GetProperty("path").GetString() == path).SelectMany(x => x.GetProperty("classes").EnumerateArray())
        .Select(Identity).Order(StringComparer.Ordinal).ToArray();

    private static void AssertIncomplete(JsonElement graph)
    {
        graph.GetProperty("complete").GetBoolean().ShouldBeFalse();
        graph.GetProperty("fallback").GetString().ShouldBe("Full");
        graph.GetProperty("incompleteReasons").GetArrayLength().ShouldBeGreaterThan(0);
    }

    private static JsonElement Build(Dictionary<string, string> sources)
    {
        using var document = JsonDocument.Parse(ReleaseImpactGraphBuilder.Serialize(ReleaseImpactGraphBuilder.Build(sources)));
        return document.RootElement.Clone();
    }
}
