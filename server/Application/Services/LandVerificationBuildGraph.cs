using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Domain;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1385: dotnet-build definition files only. The digest is the embedded manifest
/// written by the land-graph-audit command (antiphon-scripts). A missing or unreadable manifest
/// does not match, so the land builds. Loading it never throws from type initialization.
/// </summary>
internal sealed class LandVerificationBuildGraph
{
    private const string ManifestResource = "Antiphon.Server.LandVerificationBuildGraph.manifest.txt";

    /// <summary>Committed manifest the land reads from the target tree. Not itself a build-graph file.</summary>
    internal const string ManifestPath = "server/Application/Services/LandVerificationBuildGraph.manifest.txt";

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
    // CARD-1409 (Review 31c77116): the audit script is default-deny inside the files this inventory selects
    // (allow-listed property names, item types, Project children, SDKs and .rsp switches). The inventory
    // itself is unchanged: .rsp was already selected, and EditorConfigFiles and GlobalAnalyzerConfigFiles
    // are item types inside project XML, not files.
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

    /// <summary>
    /// Reads an <c>ls-tree -r -z</c> listing. False only when the listing is malformed.
    /// <paramref name="digest"/> and <paramref name="count"/> are the selected build-graph rows.
    /// </summary>
    internal static bool TryReadGraph(string raw, out string digest, out int count)
    {
        digest = "";
        count = 0;
        if (!raw.EndsWith('\0') || raw.Contains('\uFFFD')) return false;
        var graph = new LandVerificationBuildGraph();
        var selected = new List<string>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in raw.Split('\0').SkipLast(1))
        {
            var tab = row.IndexOf('\t');
            if (tab < 0) return false;
            var metadata = row[..tab].Split(' ');
            var path = row[(tab + 1)..];
            if (metadata.Length != 3 || !GitObjectId.IsFull(metadata[2]) || !SafePath(path) || !paths.Add(path)) return false;
            if (!((metadata[0] is "100644" or "100755" or "120000") && metadata[1] == "blob"
                || metadata[0] == "160000" && metadata[1] == "commit")
                || metadata[2].All(c => c == '0')) return false;
            if (!graph.Includes(path)) continue;
            if (metadata[0] is not ("100644" or "100755") || metadata[1] != "blob") return false;
            selected.Add(row);
        }
        digest = FingerprintOf(selected);
        count = selected.Count;
        return true;
    }

    private static bool SafePath(string path) => path.Length > 0 && !path.Contains('\\')
        && !path.Any(char.IsControl) && !path.Contains('\uFFFD')
        && path.Split('/').All(component => component is not ("" or "." or ".."));

    private static Manifest ReadManifest()
    {
        try
        {
            using var stream = typeof(LandVerificationBuildGraph).Assembly.GetManifestResourceStream(ManifestResource);
            if (stream is null) return new(null, null);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var parsed = ParseManifest(reader.ReadToEnd());
            return new(parsed.Digest, parsed.Count);
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
        var parsed = ParseManifest(text);
        ScopedManifest.Value = new(parsed.Digest, parsed.Count);
        return new ManifestScope(previous);
    }

    /// <summary>Two lines: 64 uppercase hex, then a non-negative count. Malformed text is nulls, never a throw.</summary>
    internal static (string? Digest, int? Count) ParseManifest(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];
        if (lines.Length != 2) return (null, null);
        var digest = lines[0];
        if (digest.Length != 64 || !digest.All(static c => c is >= '0' and <= '9' or >= 'A' and <= 'F'))
            return (null, null);
        if (!int.TryParse(lines[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0)
            return (null, null);
        return (digest, count);
    }

    private sealed class ManifestScope : IDisposable
    {
        private readonly Manifest? _previous;

        internal ManifestScope(Manifest? previous) => _previous = previous;

        public void Dispose() => ScopedManifest.Value = _previous;
    }

    private readonly record struct Manifest(string? Digest, int? Count);
}
