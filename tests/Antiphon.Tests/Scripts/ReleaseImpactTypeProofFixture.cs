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
    private static readonly Signature Author = new("fixture", "fixture@antiphon.test", DateTimeOffset.UnixEpoch);
    private readonly ReportTempWorkspace _workspace = new();
    private readonly Repository _repository;
    private readonly string _sdk;
    private readonly string _compiler;
    private readonly string _roslyn;
    private readonly PortableExecutableReference _core;

    internal ReleaseImpactTypeProofFixture()
    {
        _repository = new(Repository.Init(Path.Combine(_workspace.Path, "git"), true));
        var dotnetRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
        var sdk = Directory.EnumerateDirectories(Path.Combine(dotnetRoot, "sdk"))
            .Select(path => (Path: path, Version: System.Version.TryParse(Path.GetFileName(path), out var version) ? version : null))
            .Where(entry => entry.Version?.Major == 10).MaxBy(entry => entry.Version).Path;
        _sdk = Store(File.ReadAllBytes(Path.Combine(sdk, "dotnet.dll")));
        _compiler = Hash(File.ReadAllBytes(typeof(CSharpCompilation).Assembly.Location));
        _roslyn = Hash(File.ReadAllBytes(typeof(Compilation).Assembly.Location));
        var coreBytes = File.ReadAllBytes(typeof(object).Assembly.Location);
        Core = new(Store(coreBytes), [], false);
        _core = MetadataReference.CreateFromImage(coreBytes);
    }

    internal ReleaseCompilerReference Core { get; }

    internal ReleaseCompilerReference Emit(string assembly, string code, string[] aliases, params MetadataReference[] additional)
    {
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

    internal sealed record Context(string Project, string Assembly, string[] SourcePaths, string[] Defines,
        ReleaseCompilerReference[] References, string Framework = "net9.0");

    internal ReleaseCompilerBinding Capture(IReadOnlyDictionary<string, string> files, IReadOnlyDictionary<string, string> generated,
        Context[] contexts, Func<ReleaseCompilerManifest, ReleaseCompilerManifest>? proposal = null)
    {
        var definition = new TreeDefinition();
        var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, text) in files.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var blob = _repository.ObjectDatabase.CreateBlob(stream);
            blobs.Add(path, blob.Sha);
            definition.Add(path, blob, Mode.NonExecutableFile);
        }
        var commit = _repository.ObjectDatabase.CreateCommit(Author, Author, "fixture", _repository.ObjectDatabase.CreateTree(definition),
            [], false);
        var manifest = new ReleaseCompilerManifest("antiphon-release-compiler-inputs-v1", commit.Sha, _sdk, _compiler, _roslyn,
            contexts.Select(context => new ReleaseCompilerContext(context.Project, context.Assembly, "windows", "x64", context.Framework,
                "win-x64", "Release", "AnyCPU", "12", context.Defines, "disable", false, false, "release", "library",
                context.SourcePaths.Select(path => new ReleaseCompilerSource(path,
                    Store(Encoding.UTF8.GetBytes(generated.TryGetValue(path, out var text) ? text : files[path])),
                    blobs.GetValueOrDefault(path))).ToArray(), context.References)).ToArray());
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var expected = Hash(JsonSerializer.SerializeToUtf8Bytes(manifest, options));
        var manifestPath = Path.Combine(_workspace.Path, "manifest.json");
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(proposal?.Invoke(manifest) ?? manifest, options));
        return ReleaseCompilerInputs.Bind(commit.Sha, blobs, manifestPath, expected);
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
        _repository.Dispose();
        _workspace.Dispose();
    }
}
