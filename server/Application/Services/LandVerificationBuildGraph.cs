using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1385: dotnet-build definition files only. The digest is the embedded manifest
/// written by scripts/audit-land-verification-graph.ps1. A missing or unreadable manifest
/// does not match, so the land builds. Loading it never throws from type initialization.
/// </summary>
internal sealed class LandVerificationBuildGraph
{
    private const string ManifestResource = "Antiphon.Server.LandVerificationBuildGraph.manifest.txt";

    private static readonly Lazy<Manifest> Loaded = new(ReadManifest);

    /// <summary>Manifest digest, or empty when the manifest is missing, short, or not uppercase hex.</summary>
    internal static string Fingerprint => Loaded.Value.Digest ?? "";

    /// <summary>Manifest count, or null when the manifest cannot be read.</summary>
    internal static int? AuditedCount => Loaded.Value.Count;

    internal bool Includes(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension.EndsWith("proj", StringComparison.Ordinal)
            || extension is ".props" or ".targets" or ".sln" or ".slnx" or ".slnf" or ".rsp"
            || name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase)
            || name.ToLowerInvariant() is "global.json" or "nuget.config" or "packages.lock.json"
                or "packages.config" or "directory.packages.props";
    }

    /// <summary>Ordinal-sorted, NUL-terminated SHA-256 of the selected ls-tree rows.</summary>
    internal static string FingerprintOf(IReadOnlyList<string> rows)
    {
        var ordered = rows.ToArray();
        Array.Sort(ordered, StringComparer.Ordinal);
        var payload = string.Join('\0', ordered) + '\0';
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static Manifest ReadManifest()
    {
        try
        {
            using var stream = typeof(LandVerificationBuildGraph).Assembly.GetManifestResourceStream(ManifestResource);
            if (stream is null) return new(null, null);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var digest = reader.ReadLine();
            var countLine = reader.ReadLine();
            if (reader.ReadLine() is not null) return new(null, null);
            if (digest is null || digest.Length != 64
                || !digest.All(static c => c is >= '0' and <= '9' or >= 'A' and <= 'F'))
                return new(null, null);
            if (countLine is null
                || !int.TryParse(countLine, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                || count < 0)
                return new(null, null);
            return new(digest, count);
        }
        catch (Exception)
        {
            return new(null, null);
        }
    }

    private readonly record struct Manifest(string? Digest, int? Count);
}
