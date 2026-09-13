using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0418: versioned inventory of a settlement source bundle. New bundles list only these
/// members as implied attachments; a corrupt file must not fall back to directory enumeration.
/// </summary>
public sealed class SourceBundleManifest
{
    public const string FileName = "source-manifest.json";
    public const int CurrentVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int Version { get; set; } = CurrentVersion;

    public List<SourceBundleMember> Members { get; set; } = [];

    public List<SourceBundleOmission> Omissions { get; set; } = [];

    public bool Incomplete { get; set; }

    public string? Error { get; set; }

    public static SourceBundleManifest Read(string path)
    {
        using var stream = File.OpenRead(path);
        var manifest = JsonSerializer.Deserialize<SourceBundleManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException("source-manifest.json deserialized to null");
        if (manifest.Version != CurrentVersion)
            throw new InvalidDataException($"unsupported source-manifest version {manifest.Version}");
        return manifest;
    }

    public static async Task WriteAsync(string path, SourceBundleManifest manifest, CancellationToken ct)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, ct);
    }

    public IReadOnlyList<string> ListExistingFiles(string bundleDir)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in Members)
        {
            if (member.Kind is not (SourceBundleMemberKinds.Markdown or SourceBundleMemberKinds.Zip))
                continue;
            if (string.IsNullOrWhiteSpace(member.StoredName))
                continue;
            if (member.StoredName.Contains("..", StringComparison.Ordinal)
                || Path.IsPathRooted(member.StoredName)
                || member.StoredName.Contains('/')
                || member.StoredName.Contains('\\'))
                continue;
            var full = Path.Combine(bundleDir, member.StoredName);
            if (!File.Exists(full))
                continue;
            if (seen.Add(full))
                files.Add(full);
        }

        return files;
    }

    public static string Sha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));
}

public static class SourceBundleMemberKinds
{
    public const string Markdown = "markdown";
    public const string Zip = "zip";
}

public sealed class SourceBundleMember
{
    public string RelativePath { get; set; } = "";
    public string StoredName { get; set; } = "";
    public string Kind { get; set; } = SourceBundleMemberKinds.Markdown;
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class SourceBundleOmission
{
    public string RelativePath { get; set; } = "";
    public long Length { get; set; }
    public string Reason { get; set; } = "";
}