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

    // AsyncLocal, not a process-wide override: the registry test reads the embedded manifest
    // in the same process, and a static swap would race it.
    private static readonly AsyncLocal<Manifest?> ScopedManifest = new();

    private static Manifest Current => ScopedManifest.Value ?? Loaded.Value;

    /// <summary>Manifest digest, or empty when the manifest is missing, short, or not uppercase hex.</summary>
    internal static string Fingerprint => Current.Digest ?? "";

    /// <summary>Manifest count, or null when the manifest cannot be read.</summary>
    internal static int? AuditedCount => Current.Count;

    // CARD-1409 (Review 8989ece8 F1): a tracked obj/ .props or .targets, which Microsoft.Common.props/.targets
    // import by wildcard, is included by its extension; the audit script refuses it, so no manifest ever
    // covers such a tree and a docs-only land builds.
    internal bool Includes(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension.EndsWith("proj", StringComparison.Ordinal)
            || extension is ".props" or ".targets" or ".sln" or ".slnx" or ".slnf" or ".rsp"
            || name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase)
            // CARD-1409: Microsoft.Common.targets imports a tracked <project>.user beside its project.
            || name.EndsWith("proj.user", StringComparison.OrdinalIgnoreCase)
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
            return ParseManifest(reader.ReadToEnd());
        }
        catch (Exception)
        {
            return new(null, null);
        }
    }

    /// <summary>
    /// CARD-1390 test seam. Malformed text is an empty manifest on this async flow, so it
    /// shadows the embedded digest and a docs-only land has to build.
    /// </summary>
    internal static IDisposable UseManifestText(string text)
    {
        var previous = ScopedManifest.Value;
        ScopedManifest.Value = ParseManifest(text);
        return new ManifestScope(previous);
    }

    private static Manifest ParseManifest(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];
        if (lines.Length != 2) return new(null, null);
        var digest = lines[0];
        if (digest.Length != 64 || !digest.All(static c => c is >= '0' and <= '9' or >= 'A' and <= 'F'))
            return new(null, null);
        if (!int.TryParse(lines[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0)
            return new(null, null);
        return new(digest, count);
    }

    private sealed class ManifestScope : IDisposable
    {
        private readonly Manifest? _previous;

        internal ManifestScope(Manifest? previous) => _previous = previous;

        public void Dispose() => ScopedManifest.Value = _previous;
    }

    private readonly record struct Manifest(string? Digest, int? Count);
}
