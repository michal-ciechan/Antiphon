using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.ReleaseSelection;
using Antiphon.Tests.TestHelpers;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;

namespace Antiphon.Tests.Scripts;

internal sealed class ReleaseImpactTypeProofFixture : IDisposable
{
    internal const string TUnitAttributes = """
        namespace TUnit.Core;
        public sealed class TestAttribute : System.Attribute {}
        [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
        public sealed class ArgumentsAttribute : System.Attribute
        {
            public ArgumentsAttribute(params object[] arguments) {}
        }
        """;

    private static readonly Signature Author = new("fixture", "fixture@antiphon.test", DateTimeOffset.UnixEpoch);
    private static readonly Lazy<MethodInfo> ReleaseGraphEntryPoint = new(() =>
        System.Reflection.Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "derived-impact-index", "Antiphon.DerivedImpactIndex.dll"))
            .EntryPoint ?? throw new InvalidOperationException("The staged release graph driver has no entry point."));
    private readonly ReportTempWorkspace _workspace = new();
    private readonly Repository _repository;
    private Repository? _permutedRepository;
    private readonly string _permutedRoot;
    private readonly string _dotnetRoot;
    private readonly string _sdkVersion;
    private readonly string _sdk;
    private readonly string _compiler;
    private readonly string _roslyn;
    private readonly PortableExecutableReference _core;

    internal ReleaseImpactTypeProofFixture()
    {
        using var timing = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "pin proof fixture compiler inputs");
        _repository = new(Repository.Init(Path.Combine(_workspace.Path, "git"), true));
        _permutedRoot = Path.Combine(_workspace.Path, "permuted");
        _dotnetRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
        var sdk = Directory.EnumerateDirectories(Path.Combine(_dotnetRoot, "sdk"))
            .Select(path => (Path: path, Version: System.Version.TryParse(Path.GetFileName(path), out var version) ? version : null))
            .Where(entry => entry.Version?.Major == 10).MaxBy(entry => entry.Version).Path;
        _sdkVersion = Path.GetFileName(sdk);
        _sdk = Store(File.ReadAllBytes(Path.Combine(sdk, "dotnet.dll")));
        _compiler = Hash(File.ReadAllBytes(typeof(CSharpCompilation).Assembly.Location));
        _roslyn = Hash(File.ReadAllBytes(typeof(Compilation).Assembly.Location));
        var coreBytes = File.ReadAllBytes(typeof(object).Assembly.Location);
        Core = new(Store(coreBytes), [], false);
        _core = MetadataReference.CreateFromImage(coreBytes);
    }

    internal ReleaseCompilerReference Core { get; }

    internal ReleaseCompilerReference PinAssembly(System.Reflection.Assembly assembly) =>
        new(Store(File.ReadAllBytes(assembly.Location)), [], false);

    internal ReleaseCompilerReference Emit(string assembly, string code, string[] aliases, params MetadataReference[] additional)
    {
        using var timing = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "emit proof fixture reference");
        var compilation = CSharpCompilation.Create(assembly,
            [CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.CSharp12))],
            [_core, ..additional], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        emitted.Success.ShouldBeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        return new(Store(output.ToArray()), aliases, false);
    }

    internal PortableExecutableReference Open(ReleaseCompilerReference reference) =>
        MetadataReference.CreateFromImage(File.ReadAllBytes(Path.Combine(_workspace.Path, reference.Sha256)),
            new MetadataReferenceProperties(aliases: [..reference.Aliases]));

    internal void InspectPe<T>(ReleaseCompilerReference reference, Func<MetadataReader, T> observe, T expected, string label)
    {
        using var stream = File.OpenRead(Path.Combine(_workspace.Path, reference.Sha256));
        using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        observe(pe.GetMetadataReader()).ShouldBe(expected, label);
    }

    internal async Task<(string[] Generated, ReleaseCompilerReference[] References)> GenerateGlobalUsingsAsync(
        string[] projects, CancellationToken cancellationToken)
    {
        var root = Path.Combine(_workspace.Path, "sdk-capture");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "global.json"), JsonSerializer.Serialize(new { sdk = new { version = _sdkVersion, rollForward = "disable" } }));
        var projectPaths = new string[projects.Length];
        for (var index = 0; index < projects.Length; index++)
        {
            var directory = Path.Combine(root, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            projectPaths[index] = Path.Combine(directory, "T.csproj");
            File.WriteAllText(projectPaths[index], projects[index]);
        }
        File.WriteAllText(Path.Combine(root, "capture.proj"), """
            <Project>
              <ItemGroup><Fixture Include="*/T.csproj" /></ItemGroup>
              <Target Name="Capture">
                <MSBuild Projects="@(Fixture)" Targets="GenerateGlobalUsings" Properties="GeneratedGlobalUsingsFile=%(Fixture.RootDir)%(Fixture.Directory)Generated.g.cs" />
              </Target>
            </Project>
            """);
        var start = new ProcessStartInfo("dotnet", ["msbuild", "capture.proj", "-nologo", "-v:quiet", "-t:Capture", "-nodeReuse:false"])
        {
            WorkingDirectory = root,
        };
        var result = await C1514Support.RunProcess(start, cancellationToken);
        result.ExitCode.ShouldBe(0, "IU SDK generation: " + result.Text);

        var pack = Directory.EnumerateDirectories(Path.Combine(_dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"))
            .Select(directory => (Directory: directory, Version: System.Version.TryParse(Path.GetFileName(directory), out var version) ? version : null))
            .Where(entry => entry.Version?.Major == 9).MaxBy(entry => entry.Version).Directory;
        var references = new[] { "System.Runtime", "System.Collections", "System.IO", "System.Linq", "System.Net.Http",
            "System.Threading", "System.Threading.Tasks" }.Select(name =>
            new ReleaseCompilerReference(Store(File.ReadAllBytes(Path.Combine(pack, "ref", "net9.0", name + ".dll"))), [], false)).ToArray();
        return (projectPaths.Select(path => new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(path)!, "Generated.g.cs")))).ToArray(), references);
    }

    internal sealed record Context(string Project, string Assembly, string[] SourcePaths, string[] Defines,
        ReleaseCompilerReference[] References, string Framework = "net9.0");

    internal ReleaseCompilerBinding Capture(IReadOnlyDictionary<string, string> files, IReadOnlyDictionary<string, string> generated,
        Context[] contexts, Func<ReleaseCompilerManifest, ReleaseCompilerManifest>? proposal = null, string? unavailableGenerated = null,
        bool permuted = false)
    {
        var root = permuted ? _permutedRoot : _workspace.Path;
        var repository = permuted
            ? _permutedRepository ??= new Repository(Repository.Init(Path.Combine(root, "git"), true))
            : _repository;
        var definition = new TreeDefinition();
        var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, text) in files.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var blob = repository.ObjectDatabase.CreateBlob(stream);
            blobs.Add(path, blob.Sha);
            definition.Add(path, blob, Mode.NonExecutableFile);
        }
        var commit = repository.ObjectDatabase.CreateCommit(Author, Author, "fixture", repository.ObjectDatabase.CreateTree(definition),
            [], false);
        var (manifestPath, expected) = WriteManifest(commit.Sha, files, generated, contexts, blobs, proposal, root);
        if (unavailableGenerated is null) return ReleaseCompilerInputs.Bind(commit.Sha, blobs, manifestPath, expected);
        var source = Encoding.UTF8.GetBytes(generated[unavailableGenerated]);
        var payload = Path.Combine(root, Hash(source));
        File.Delete(payload);
        try { return ReleaseCompilerInputs.Bind(commit.Sha, blobs, manifestPath, expected); }
        finally { File.WriteAllBytes(payload, source); }
    }

    // Reuse the staged driver in this host for multi-endpoint fixtures. S1
    // exercises its standalone process and command-line wiring separately.
    internal void WriteReleaseGraph(string repositoryRoot, string sha, string output, IReadOnlyDictionary<string, string> files,
        Context[] contexts, CancellationToken cancellationToken)
    {
        using var timing = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "write pinned release graph");
        var clock = Stopwatch.StartNew();
        var outputName = Path.GetFileName(output);
        CheckDeadline("before compiler manifest");
        using var repository = new Repository(repositoryRoot);
        var tree = repository.Lookup<Commit>(sha).Tree;
        var blobs = files.Keys.ToDictionary(path => path, path => tree[path].Target.Id.Sha, StringComparer.Ordinal);
        var (manifest, digest) = WriteManifest(sha, files, new Dictionary<string, string>(), contexts, blobs, null, _workspace.Path);
        var manifestMilliseconds = clock.ElapsedMilliseconds;
        CheckDeadline("after compiler manifest");
        string[] arguments = ["--release-graph", repositoryRoot, sha, output,
            "--proven-subtraction", "--compiler-manifest", manifest, "--expected-compiler-manifest-sha256", digest];
        var exit = (int)ReleaseGraphEntryPoint.Value.Invoke(null, [arguments])!;
        Console.WriteLine($"C2076 fixture phase=release-graph endpoint={outputName} manifestMs={manifestMilliseconds} elapsedMs={clock.ElapsedMilliseconds}");
        exit.ShouldBe(0, $"release graph fixture {outputName}");
        CheckDeadline($"after graph production (manifest {manifestMilliseconds} ms)");

        void CheckDeadline(string phase)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException($"Release graph {outputName} canceled {phase} at {clock.ElapsedMilliseconds} ms.", cancellationToken);
        }
    }

    private (string Path, string Digest) WriteManifest(string sha, IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, string> generated, Context[] contexts, IReadOnlyDictionary<string, string> blobs,
        Func<ReleaseCompilerManifest, ReleaseCompilerManifest>? proposal, string root)
    {
        var manifest = new ReleaseCompilerManifest("antiphon-release-compiler-inputs-v1", sha, _sdk, _compiler, _roslyn,
            contexts.Select(context => new ReleaseCompilerContext(context.Project, context.Assembly, "windows", "x64", context.Framework,
                "win-x64", "Release", "AnyCPU", "12", context.Defines, "disable", false, false, "release", "library",
                context.SourcePaths.Select(path => new ReleaseCompilerSource(path,
                    Store(Encoding.UTF8.GetBytes(generated.TryGetValue(path, out var text) ? text : files[path])),
                    blobs.GetValueOrDefault(path))).ToArray(), context.References)).ToArray());
        if (root != _workspace.Path)
            foreach (var payload in manifest.Contexts.SelectMany(context => context.Sources.Select(source => source.Sha256)
                         .Concat(context.References.Select(reference => reference.Sha256))).Append(_sdk).Distinct(StringComparer.Ordinal))
            {
                var destination = Path.Combine(root, payload);
                if (!File.Exists(destination)) File.Copy(Path.Combine(_workspace.Path, payload), destination);
            }
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var expected = Hash(JsonSerializer.SerializeToUtf8Bytes(manifest, options));
        var manifestPath = Path.Combine(root, "manifest.json");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(proposal?.Invoke(manifest) ?? manifest, options));
        return (manifestPath, expected);
    }

    private string Store(byte[] data)
    {
        var hash = Hash(data);
        var path = Path.Combine(_workspace.Path, hash);
        if (!File.Exists(path)) File.WriteAllBytes(path, data);
        return hash;
    }

    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public void Dispose()
    {
        _permutedRepository?.Dispose();
        _repository.Dispose();
        _workspace.Dispose();
    }
}
