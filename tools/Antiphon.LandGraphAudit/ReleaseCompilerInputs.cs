#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Antiphon.ReleaseSelection;

// Each schema version fixes the supported compiler model. A capture with any other
// compiler options or additional inputs must use a different schema.
internal sealed record ReleaseCompilerManifest(string Schema, string SourceSha, string SdkSha256,
    string CompilerSha256, string CodeAnalysisSha256, ReleaseCompilerContext[] Contexts);

internal sealed record ReleaseCompilerContext(string Project, string Assembly, string OperatingSystem, string Architecture,
    string Framework, string RuntimeIdentifier, string Configuration, string Platform, string LanguageVersion,
    string[] Defines, string Nullable, bool AllowUnsafe, bool CheckOverflow, string Optimization, string OutputKind,
    ReleaseCompilerSource[] Sources, ReleaseCompilerReference[] References);

internal sealed record ReleaseCompilerSource(string Path, string Sha256, string? GitBlob);
internal sealed record ReleaseCompilerReference(string Sha256, string[] Aliases, bool EmbedInteropTypes);

internal sealed record BoundReleaseCompilation(ReleaseCompilerContext Input, CSharpCompilation Compilation);

internal sealed record ReleaseCompilerBinding(string Reason, string? ManifestDigest, BoundReleaseCompilation[] Contexts)
{
    internal bool Eligible => Contexts.Length > 0;
}

internal static class ReleaseCompilerInputs
{
    private const string Schema = "antiphon-release-compiler-inputs-v1";
    private const int MiB = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    // The caller supplies the digest from its separately frozen capture and the
    // committed regular-file inventory. Pack members are named by their content hash.
    internal static ReleaseCompilerBinding Bind(string sourceSha, IReadOnlyDictionary<string, string> gitBlobs,
        string manifestPath, string expectedManifestDigest)
    {
        if (!IsHex(sourceSha, 40) || !IsHex(expectedManifestDigest, 64))
            return new("compiler-input-authority-required", null, []);

        string? digest = null;
        var reason = "compiler-manifest-unreadable";
        var watch = Stopwatch.StartNew();
        try
        {
            var manifestBytes = ReadBytes(manifestPath, 8 * MiB);
            if (Digest(manifestBytes) != expectedManifestDigest)
                return new("compiler-manifest-digest-mismatch", null, []);
            digest = expectedManifestDigest;
            reason = "compiler-manifest-invalid";
            var manifest = JsonSerializer.Deserialize<ReleaseCompilerManifest>(manifestBytes, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectRequiredConstructorParameters = true,
                RespectNullableAnnotations = true,
            });
            if (manifest?.Schema != Schema) return new("compiler-manifest-unsupported", digest, []);
            if (manifest.SourceSha != sourceSha) return new("compiler-source-mismatch", digest, []);
            if (manifest.Contexts.Length == 0 || manifest.Contexts.Length > 2048 || manifest.Contexts.Any(context => context is null)
                || manifest.Contexts.GroupBy(ContextKey).Any(group => group.Count() != 1))
                return new("compiler-context-coverage-unbound", digest, []);

            var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
            var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var totalBytes = 0L;
            reason = "compiler-sdk-unbound";
            _ = Payload(manifest.SdkSha256, 64 * MiB);
            reason = "compiler-binary-unbound";
            if (!MatchesBinary(typeof(CSharpCompilation).Assembly.Location, manifest.CompilerSha256)
                || !MatchesBinary(typeof(Compilation).Assembly.Location, manifest.CodeAnalysisSha256))
                return new(reason, digest, []);

            var contexts = new List<BoundReleaseCompilation>(manifest.Contexts.Length);
            var contextItems = 0L;
            foreach (var context in manifest.Contexts.OrderBy(ContextKey, StringComparer.Ordinal))
            {
                reason = "compiler-context-unbound";
                if (!IsRelativePath(context.Project) || !context.Project.EndsWith(".csproj", StringComparison.Ordinal)
                    || !gitBlobs.ContainsKey(context.Project) || string.IsNullOrWhiteSpace(context.Assembly)
                    || context.OperatingSystem is not ("windows" or "linux") || context.Architecture is not ("x64" or "arm64" or "x86" or "arm")
                    || string.IsNullOrWhiteSpace(context.Framework) || string.IsNullOrWhiteSpace(context.Configuration)
                    || context.Sources.Length == 0 || context.Sources.Length > 16384 || context.Sources.Any(source => source is null)
                    || context.References.Length == 0 || context.References.Length > 2048 || context.References.Any(reference => reference is null))
                    return new(reason, digest, []);
                contextItems += context.Sources.Length + context.References.Length;
                if (contextItems > 128_000) return new("compiler-input-resource-limit", digest, []);
                reason = "compiler-options-unsupported";
                if (!LanguageVersionFacts.TryParse(context.LanguageVersion, out var languageVersion)
                    || languageVersion is LanguageVersion.Default or LanguageVersion.Latest or LanguageVersion.LatestMajor or LanguageVersion.Preview
                    || !Enum.TryParse<Platform>(context.Platform, ignoreCase: true, out var platform) || !Enum.IsDefined(platform)
                    || !Enum.TryParse<NullableContextOptions>(context.Nullable, ignoreCase: true, out var nullable) || !Enum.IsDefined(nullable)
                    || context.Optimization is not ("debug" or "release") || context.OutputKind != "library"
                    || context.Defines.Any(symbol => !SyntaxFacts.IsValidIdentifier(symbol)))
                    return new(reason, digest, []);

                var parse = new CSharpParseOptions(languageVersion, preprocessorSymbols: context.Defines);
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var trees = new List<SyntaxTree>(context.Sources.Length);
                foreach (var source in context.Sources.OrderBy(source => source.Path, StringComparer.Ordinal))
                {
                    reason = source.GitBlob is null ? "compiler-generated-source-unbound" : "compiler-tracked-source-unbound";
                    if (!IsRelativePath(source.Path) || !paths.Add(source.Path)) return new(reason, digest, []);
                    var bytes = Payload(source.Sha256, 8 * MiB);
                    if (source.GitBlob is null ? gitBlobs.ContainsKey(source.Path)
                        : !gitBlobs.TryGetValue(source.Path, out var blob) || source.GitBlob != blob || GitBlob(bytes) != blob)
                        return new(reason, digest, []);
                    using var sourceStream = new MemoryStream(bytes, writable: false);
                    var text = SourceText.From(sourceStream, Utf8, SourceHashAlgorithm.Sha256, throwIfBinaryDetected: true);
                    trees.Add(CSharpSyntaxTree.ParseText(text, parse, source.Path));
                }

                reason = "compiler-reference-unbound";
                var references = new List<PortableExecutableReference>(context.References.Length);
                foreach (var reference in context.References)
                {
                    if (reference.Aliases.Any(alias => !SyntaxFacts.IsValidIdentifier(alias))
                        || reference.Aliases.Distinct(StringComparer.Ordinal).Count() != reference.Aliases.Length)
                        return new(reason, digest, []);
                    var properties = new MetadataReferenceProperties(aliases: ImmutableArray.CreateRange(reference.Aliases),
                        embedInteropTypes: reference.EmbedInteropTypes);
                    references.Add(MetadataReference.CreateFromImage(Payload(reference.Sha256, 64 * MiB), properties));
                }

                reason = "compiler-diagnostics";
                var compilation = CSharpCompilation.Create(context.Assembly, trees, references, new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary, optimizationLevel: context.Optimization == "release" ? OptimizationLevel.Release : OptimizationLevel.Debug,
                    checkOverflow: context.CheckOverflow, allowUnsafe: context.AllowUnsafe, platform: platform, deterministic: true,
                    nullableContextOptions: nullable));
                if (compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
                    return new(reason, digest, []);
                contexts.Add(new(context, compilation));
            }
            return new("compiler-inputs-verified", digest, contexts.ToArray());

            byte[] Payload(string sha, int limit)
            {
                if (!IsHex(sha, 64)) throw new InvalidDataException();
                if (payloads.TryGetValue(sha, out var existing))
                    return existing.Length <= limit ? existing : throw new InvalidDataException();
                var bytes = ReadBytes(Path.Combine(root, sha), limit);
                totalBytes += bytes.Length;
                if (totalBytes > 256L * MiB || Digest(bytes) != sha) throw new InvalidDataException();
                payloads.Add(sha, bytes);
                return bytes;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or BadImageFormatException)
        {
            return new(reason, digest, []);
        }
        finally
        {
            Console.Error.WriteLine($"release-input phase=compiler-binding elapsedMs={watch.ElapsedMilliseconds}");
        }
    }

    private static string ContextKey(ReleaseCompilerContext context) => string.Join('\0',
        context.Project, context.OperatingSystem, context.Architecture, context.Framework,
        context.RuntimeIdentifier, context.Configuration, context.Platform);

    private static bool IsRelativePath(string? path) => !string.IsNullOrEmpty(path)
        && !path.Contains('\\') && !path.Contains(':') && !path.Any(char.IsControl)
        && path.Split('/').All(part => part is not ("" or "." or ".."));

    private static bool IsHex(string? value, int length) => value?.Length == length
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Digest(byte[] input) => Convert.ToHexStringLower(SHA256.HashData(input));

    private static bool MatchesBinary(string path, string sha)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream)) == sha;
    }

    private static string GitBlob(byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0"));
        hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static byte[] ReadBytes(string path, int limit)
    {
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException();
        using var stream = File.OpenRead(path);
        if (stream.Length < 0 || stream.Length > limit) throw new InvalidDataException();
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException();
        return bytes;
    }
}
