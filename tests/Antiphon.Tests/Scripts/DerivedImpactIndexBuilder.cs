using System.Text;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1820. Committed reverse-reference index for the impacted-classes selector.
/// The real walk is added once the witnesses have gone red against this empty result.
/// </summary>
internal static class DerivedImpactIndexBuilder
{
    internal const int Cap = 25;
    internal const int ByteLimit = 2_000_000;

    internal static readonly string[] Allowlist =
    [
        "Antiphon.sln",
        "Directory.Build.targets",
        "Directory.Build.props",
        "global.json",
    ];

    internal static DerivedImpactIndex Build(string repoRoot)
    {
        _ = repoRoot;
        var utf8 = Encoding.UTF8.GetBytes(
            "{\"schema\":1,\"cap\":25,\"byteLimit\":2000000,\"allowlist\":[],\"cappedPaths\":[],\"edges\":[]}\n");
        return new DerivedImpactIndex(1, Cap, Allowlist, [], [], utf8);
    }

    internal static void Write(string repoRoot, DerivedImpactIndex index)
    {
        var path = Path.Combine(repoRoot, "tests", "derived-impact-index.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, index.Utf8);
    }
}

internal readonly record struct CappedPath(string Path, int Classes, string Reason);

internal readonly record struct ImpactEdge(string Path, IReadOnlyList<string> Classes);

internal sealed record DerivedImpactIndex(
    int Schema,
    int Cap,
    IReadOnlyList<string> Allowlist,
    IReadOnlyList<CappedPath> CappedPaths,
    IReadOnlyList<ImpactEdge> Edges,
    byte[] Utf8);
