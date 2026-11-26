#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;

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
    internal static ReleaseCompilerBinding Bind(string sourceSha, IReadOnlyDictionary<string, string> gitBlobs,
        string manifestPath, string expectedManifestDigest) =>
        new("compiler-input-binding-unavailable", null, []);
}
