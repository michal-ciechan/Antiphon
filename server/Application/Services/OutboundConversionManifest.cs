using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Messaging;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class OutboundConversionRequestV1
{
    public int Version { get; set; } = 1;
    public Guid DeliveryId { get; set; }
    public string? Text { get; set; }
    public string OutputDirectory { get; set; } = "";
    public List<OutboundConversionFileDescriptor> Attachments { get; set; } = [];
    public SourceBundleManifest? SourceManifest { get; set; }
    public Dictionary<string, string> RoutingReference { get; set; } = [];
}

public sealed class OutboundConversionOutputV1
{
    public int Version { get; set; } = 1;
    public Guid DeliveryId { get; set; }
    public string Disposition { get; set; } = "unchanged";
    public string? ReplacementText { get; set; }
    public List<OutboundConversionFileDescriptor> Files { get; set; } = [];
    public string? Channel { get; set; }
    public string? ConversationId { get; set; }
    public string? ReplyHandle { get; set; }
    public string? ReplyToMessageId { get; set; }
    public string? Kind { get; set; }
    public JsonElement? RawOverrides { get; set; }
    public Guid? SourceTaskId { get; set; }
    public string? Project { get; set; }
    public string? Policy { get; set; }
}

public sealed class OutboundConversionFileDescriptor
{
    public string Path { get; set; } = "";
    public string? Mime { get; set; }
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class OutboundConversionManifestValidator(IOptions<ChannelOutboundSettings> settings)
{
    public const string FallbackAnnotation = "Conversion unavailable; source files attached.";
    private static readonly string[] ProhibitedRouting =
    [
        nameof(OutboundConversionOutputV1.Channel),
        nameof(OutboundConversionOutputV1.ConversationId),
        nameof(OutboundConversionOutputV1.ReplyHandle),
        nameof(OutboundConversionOutputV1.ReplyToMessageId),
        nameof(OutboundConversionOutputV1.Kind),
        nameof(OutboundConversionOutputV1.RawOverrides),
        nameof(OutboundConversionOutputV1.SourceTaskId),
        nameof(OutboundConversionOutputV1.Project),
        nameof(OutboundConversionOutputV1.Policy),
    ];

    private readonly ChannelOutboundSettings _settings = settings.Value;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public OutboundConversionValidationResult ValidateOutput(
        Guid expectedDeliveryId,
        string outputDirectory,
        OutboundConversionOutputV1 output)
    {
        if (output.Version != 1)
            return Fail("version");
        if (output.DeliveryId != expectedDeliveryId)
            return Fail("delivery-id");
        if (output.Disposition is not ("unchanged" or "converted"))
            return Fail("disposition");
        foreach (var key in ProhibitedRouting)
        {
            if (HasProhibited(output, key))
                return Fail("routing:" + key);
        }

        if (output.Files.Count > _settings.MaxOutputDescriptors)
            return Fail("count");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accepted = new List<(OutboundConversionFileDescriptor Descriptor, byte[] Bytes)>();
        foreach (var file in output.Files)
        {
            if (!TryResolveContainedFile(outputDirectory, file.Path, out var full))
                return Fail("containment");
            if (!names.Add(Path.GetFileName(file.Path)))
                return Fail("duplicate-name");
            if (!File.Exists(full))
                return Fail("missing-file");
            if (IsReparse(full))
                return Fail("reparse");
            var bytes = File.ReadAllBytes(full);
            if (bytes.LongLength != file.Length)
                return Fail("length");
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                return Fail("hash");
            accepted.Add((file, bytes));
        }

        return new OutboundConversionValidationResult(true, null, accepted);
    }

    public static bool TryResolveContainedFile(string root, string relative, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(relative)
            || Path.IsPathRooted(relative)
            || relative.Contains("..", StringComparison.Ordinal)
            || relative.StartsWith("\\\\", StringComparison.Ordinal)
            || relative.Contains(':'))
        {
            return false;
        }

        var rootFull = Path.GetFullPath(root);
        full = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<OutboundConversionFileDescriptor> UnpackSourceZip(
        byte[] zipBytes,
        SourceBundleManifest manifest,
        string destDir,
        long expandedLimit)
    {
        Directory.CreateDirectory(destDir);
        using var stream = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var listed = manifest.Members
            .Where(m => m.Kind == SourceBundleMemberKinds.Zip || m.Kind == SourceBundleMemberKinds.Markdown)
            .Select(m => m.RelativePath.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        var files = new List<OutboundConversionFileDescriptor>();
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!listed.Contains(name))
                continue;
            if (name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
                throw new InvalidDataException("zip-traversal");
            var dest = Path.GetFullPath(Path.Combine(destDir, name.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!dest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("zip-containment");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            // The budget counts bytes as they LEAVE the archive. ZipArchiveEntry.Length is a
            // declaration the archive makes about itself, and an archive that under-declares is
            // exactly the archive this limit exists to stop.
            byte[] bytes;
            try
            {
                using (var src = entry.Open())
                using (var copy = File.Create(dest))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        expanded += read;
                        if (expanded > expandedLimit)
                            throw new InvalidDataException("zip-expanded-budget");
                        copy.Write(buffer, 0, read);
                    }
                }

                // Read back only AFTER the write handle is closed; Windows refuses the overlapping
                // read outright, so the previous shape threw IOException on every extraction.
                bytes = File.ReadAllBytes(dest);
            }
            catch
            {
                try { File.Delete(dest); } catch (IOException) { }
                throw;
            }

            files.Add(new OutboundConversionFileDescriptor
            {
                Path = name,
                Mime = "text/markdown",
                Length = bytes.LongLength,
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            });
        }

        return files;
    }

    private static bool HasProhibited(OutboundConversionOutputV1 output, string key) => key switch
    {
        nameof(OutboundConversionOutputV1.Channel) => output.Channel is not null,
        nameof(OutboundConversionOutputV1.ConversationId) => output.ConversationId is not null,
        nameof(OutboundConversionOutputV1.ReplyHandle) => output.ReplyHandle is not null,
        nameof(OutboundConversionOutputV1.ReplyToMessageId) => output.ReplyToMessageId is not null,
        nameof(OutboundConversionOutputV1.Kind) => output.Kind is not null,
        nameof(OutboundConversionOutputV1.RawOverrides) => output.RawOverrides is not null,
        nameof(OutboundConversionOutputV1.SourceTaskId) => output.SourceTaskId is not null,
        nameof(OutboundConversionOutputV1.Project) => output.Project is not null,
        nameof(OutboundConversionOutputV1.Policy) => output.Policy is not null,
        _ => false,
    };

    private static bool IsReparse(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static OutboundConversionValidationResult Fail(string reason) =>
        new(false, reason, []);
}

public sealed record OutboundConversionValidationResult(
    bool Ok,
    string? Reason,
    IReadOnlyList<(OutboundConversionFileDescriptor Descriptor, byte[] Bytes)> Files);
