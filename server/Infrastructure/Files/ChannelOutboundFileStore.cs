using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Files;

/// <summary>Stages a complete reply before any database intent becomes visible.</summary>
public sealed class ChannelOutboundFileStore : IChannelOutboundFileStore
{
    private const long MaxRawAttachmentBytes = 14L * 1024 * 1024;
    private const int MaxRequestBytes = 20 * 1024 * 1024;
    private const long MaxExpandedSourceBytes = 64L * 1024 * 1024;
    private readonly string _root;

    internal Func<string, Guid, CancellationToken, Task>? ProbeBarrierAsync { get; set; }

    public ChannelOutboundFileStore(IHostEnvironment host)
        : this(Path.Combine(host.ContentRootPath, ".antiphon", "outbound")) { }

    public ChannelOutboundFileStore(string root) => _root = Path.GetFullPath(root);

    public async Task<ChannelOutboundSnapshot> StageAsync(Guid deliveryId, ChannelReply reply,
        CancellationToken ct, string? sourceManifestJson = null)
    {
        if (deliveryId == Guid.Empty)
            throw new ArgumentException("A delivery id is required.", nameof(deliveryId));
        if (reply.Attachments.Count > 64 || reply.Attachments.Sum(a => (long)(a.Content?.Length ?? 0)) > MaxRawAttachmentBytes)
            throw new InvalidDataException("Outbound attachments exceed the finite staging budget.");
        if (sourceManifestJson is { Length: > 256 * 1024 })
            throw new InvalidDataException("The source manifest exceeds the finite staging budget.");

        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(reply, MessagingJson.Options);
        if (replyBytes.Length > MaxRequestBytes)
            throw new InvalidDataException("The serialized reply exceeds the messaging budget.");
        var hash = Convert.ToHexString(SHA256.HashData(replyBytes)).ToLowerInvariant();
        var final = Path.Combine(_root, deliveryId.ToString("N"));
        if (Directory.Exists(final))
            throw new IOException("The outbound snapshot already exists.");
        Directory.CreateDirectory(_root);
        var temporary = Path.Combine(_root, ".stage-" + deliveryId.ToString("N") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var input = Path.Combine(temporary, "input");
            Directory.CreateDirectory(input);
            Directory.CreateDirectory(Path.Combine(temporary, "output"));
            var files = new List<object>();
            for (var i = 0; i < reply.Attachments.Count; i++)
            {
                var attachment = reply.Attachments[i];
                var extension = Path.GetExtension(attachment.Name ?? "").ToLowerInvariant() switch
                {
                    ".md" => ".md", ".zip" => ".zip", ".pdf" => ".pdf",
                    ".txt" => ".txt", ".json" => ".json", _ => ".bin",
                };
                string? localName = null;
                string? fileHash = null;
                if (attachment.Content is { } content)
                {
                    localName = $"attachment-{i + 1:D3}{extension}";
                    fileHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
                    await File.WriteAllBytesAsync(Path.Combine(input, localName), content, ct);
                    if (ProbeBarrierAsync is { } partialBarrier)
                        await partialBarrier("input-temporary-partial", deliveryId, ct);
                }
                files.Add(new { attachment.Name, attachment.Mime, attachment.Kind,
                    Length = attachment.Content?.Length, Sha256 = fileHash, LocalName = localName });
            }

            List<StagedSource> sourceFiles = sourceManifestJson is null
                ? [] : await MaterializeSourcesAsync(sourceManifestJson, reply.Attachments, input, ct);
            if (sourceManifestJson is not null)
                await File.WriteAllTextAsync(Path.Combine(input, "source-manifest.json"), sourceManifestJson, ct);

            var request = new
            {
                Version = 1,
                DeliveryId = deliveryId,
                Routing = new { reply.Channel, reply.ConversationId, reply.ReplyHandle,
                    reply.ReplyToMessageId, reply.Kind },
                reply.Text,
                Attachments = files,
                SourceManifest = sourceManifestJson is null ? null : "input/source-manifest.json",
                SourceFiles = sourceFiles,
                OutputDirectory = "output",
            };
            await File.WriteAllBytesAsync(Path.Combine(temporary, "reply.json"), replyBytes, ct);
            await File.WriteAllBytesAsync(Path.Combine(temporary, "request.json"),
                JsonSerializer.SerializeToUtf8Bytes(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ct);
            if (ProbeBarrierAsync is { } temporaryBarrier)
                await temporaryBarrier("input-temporary-complete", deliveryId, ct);
            Directory.Move(temporary, final);
            return new ChannelOutboundSnapshot(Path.Combine(final, "reply.json"), hash,
                Path.Combine(final, "request.json"), Path.Combine(final, "output"));
        }
        catch
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
            throw;
        }
    }

    private sealed record StagedSource(string OriginalRelativePath, string LocalName,
        long Length, string Sha256);

    private static async Task<List<StagedSource>> MaterializeSourcesAsync(string manifestJson,
        IReadOnlyList<OutboundAttachment> attachments, string input, CancellationToken ct)
    {
        var manifest = JsonSerializer.Deserialize<DeliverableBundleService.SourceManifest>(manifestJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The source manifest is empty.");
        if (manifest.Version != 1 || manifest.Sources is null || manifest.Sources.Count > 256)
            throw new InvalidDataException("The source manifest has an unsupported shape.");
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in manifest.Sources)
        {
            if (!SafeSourcePath(source.OriginalRelativePath)
                || !SafeStoredName(source.StoredFile)
                || source.Length < 0 || source.Sha256 is null || source.Sha256.Length != 64
                || !source.Sha256.All(Uri.IsHexDigit)
                || !sourcePaths.Add(source.OriginalRelativePath)
                || source.ZipEntry is not null
                    && (!SafeSourcePath(source.ZipEntry)
                        || !string.Equals(source.ZipEntry, source.OriginalRelativePath,
                            StringComparison.Ordinal)))
                throw new InvalidDataException("The source manifest has an unsafe or ambiguous member.");
        }
        var staged = new List<StagedSource>();
        long expanded = 0;
        foreach (var source in manifest.Sources.Where(s => s.ZipEntry is null))
        {
            var index = -1;
            for (var i = 0; i < attachments.Count; i++)
                if (attachments[i].Name == source.StoredFile && attachments[i].Content is not null)
                { index = i; break; }
            if (index < 0)
                continue; // The dispatcher already recorded the over-budget or missing attachment.
            var bytes = attachments[index].Content!;
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (bytes.LongLength != source.Length
                || !string.Equals(hash, source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An attached source no longer matches its manifest.");
            if (bytes.LongLength > MaxExpandedSourceBytes - expanded)
                throw new InvalidDataException("Manifested sources exceed the expanded budget.");
            expanded += bytes.LongLength;
            var extension = Path.GetExtension(attachments[index].Name ?? "").ToLowerInvariant();
            var localName = $"attachment-{index + 1:D3}{(extension == ".md" ? ".md" : ".bin")}";
            staged.Add(new StagedSource(source.OriginalRelativePath, localName, source.Length, hash));
        }
        foreach (var group in manifest.Sources.Where(s => s.ZipEntry is not null)
                     .GroupBy(s => s.StoredFile, StringComparer.Ordinal))
        {
            var attachment = attachments.SingleOrDefault(a => a.Name == group.Key
                && a.Content is not null);
            if (attachment is null)
                continue; // Preserve the visible source omission from attachment resolution.
            using var stream = new MemoryStream(attachment.Content!, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var source in group)
            {
                if (source.ZipEntry is null || source.Length < 0 || source.Sha256 is null
                    || source.Sha256.Length != 64
                    || source.Length > MaxExpandedSourceBytes - expanded)
                    throw new InvalidDataException("A manifested source exceeds the expanded budget.");
                var entry = zip.GetEntry(source.ZipEntry)
                    ?? throw new InvalidDataException("A manifested source is absent from its zip.");
                if (entry.Length != source.Length)
                    throw new InvalidDataException("A manifested source length changed.");
                var localName = $"source-{staged.Count + 1:D3}.md";
                var path = Path.Combine(input, localName);
                await using (var reader = entry.Open())
                await using (var writer = File.Create(path))
                {
                    var buffer = new byte[64 * 1024];
                    long copied = 0;
                    int read;
                    while ((read = await reader.ReadAsync(buffer, ct)) > 0)
                    {
                        copied += read;
                        if (copied > source.Length)
                            throw new InvalidDataException("A source zip entry expanded beyond its manifest.");
                        await writer.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                    if (copied != source.Length)
                        throw new InvalidDataException("A source zip entry was truncated.");
                }
                var bytes = await File.ReadAllBytesAsync(path, ct);
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(hash, source.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A manifested source hash changed.");
                expanded += source.Length;
                staged.Add(new StagedSource(source.OriginalRelativePath, localName, source.Length, hash));
            }
        }
        return staged;
    }

    private static bool SafeSourcePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && !path.Contains('\\') && !path.Contains(':')
        && path.Split('/').All(part => part.Length > 0 && part is not ("." or ".."));

    private static bool SafeStoredName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= 255
        && name is not ("." or "..")
        && !Path.IsPathRooted(name)
        && name == Path.GetFileName(name)
        && !name.Contains('\\') && !name.Contains(':');

    public async Task<ChannelReply> ReadReplyAsync(string path, string expectedSha256,
        CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(hash, expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The frozen outbound reply hash changed.");
        return JsonSerializer.Deserialize<ChannelReply>(bytes, MessagingJson.Options)
            ?? throw new InvalidDataException("The frozen outbound reply is invalid.");
    }

    private sealed class OutputManifest
    {
        public int Version { get; set; }
        public Guid DeliveryId { get; set; }
        public string? Disposition { get; set; }
        public string? ReplacementText { get; set; }
        public List<OutputFile>? Files { get; set; }
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? UnknownFields { get; set; }
    }

    private sealed class OutputFile
    {
        public string? Path { get; set; }
        public string? Name { get; set; }
        public string? Mime { get; set; }
        public long Length { get; set; }
        public string? Sha256 { get; set; }
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? UnknownFields { get; set; }
    }

    public async Task<ChannelOutboundSealed> ValidateAndSealAsync(Guid deliveryId,
        string replyPath, string replySha256, int maxMessageBytes, CancellationToken ct)
    {
        var original = await ReadReplyAsync(replyPath, replySha256, ct);
        var jobRoot = Path.GetDirectoryName(replyPath)
            ?? throw new InvalidDataException("The outbound snapshot path is invalid.");
        var sealedPath = Path.Combine(jobRoot, "sealed-reply.json");

        var outputRoot = Path.Combine(jobRoot, "output");
        var manifestPath = Path.Combine(outputRoot, "manifest.json");
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, ct);
        if (manifestBytes.Length > 64 * 1024)
            throw new InvalidDataException("The conversion manifest is too large.");
        var manifest = JsonSerializer.Deserialize<OutputManifest>(manifestBytes,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The conversion manifest is empty.");
        if (manifest.Version != 1 || manifest.DeliveryId != deliveryId
            || manifest.Disposition is not ("unchanged" or "converted")
            || manifest.Files is null || manifest.Files.Count > 16
            || manifest.UnknownFields is { Count: > 0 }
            || manifest.ReplacementText?.Length > 20_000)
            throw new InvalidDataException("The conversion manifest has invalid identity or fields.");
        if (manifest.Disposition == "unchanged"
            && (manifest.Files.Count != 0 || manifest.ReplacementText is not null))
            throw new InvalidDataException("An unchanged result cannot replace reply content.");
        if (manifest.Disposition == "converted" && manifest.Files.Count == 0
            && manifest.ReplacementText is null)
            throw new InvalidDataException("A converted result must supply content.");

        var attachments = new List<OutboundAttachment>(original.Attachments);
        long rawBytes = attachments.Sum(a => (long)(a.Content?.Length ?? 0));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var file in manifest.Files)
        {
            if (file.Path is null || file.Name is null || file.Mime is null || file.Sha256 is null
                || file.Name.Length is 0 or > 200 || file.Name != Path.GetFileName(file.Name)
                || file.Name is "." or ".." || file.Name.Contains('\\') || file.Name.Contains(':')
                || file.Mime.Length is 0 or > 100
                || file.Mime.Any(char.IsControl) || file.Length < 0
                || file.Sha256.Length != 64 || Path.IsPathRooted(file.Path)
                || file.Path.Contains('\\') || file.Path.Contains(':')
                || file.UnknownFields is { Count: > 0 }
                || !names.Add(file.Name) || !paths.Add(file.Path))
                throw new InvalidDataException("A converted file descriptor is invalid.");
            var path = Path.GetFullPath(Path.Combine(outputRoot, file.Path));
            if (!path.StartsWith(Path.GetFullPath(outputRoot) + Path.DirectorySeparatorChar,
                    pathComparison))
                throw new InvalidDataException("A converted file escapes its output directory.");
            for (var parent = Path.GetDirectoryName(path); parent is not null
                 && parent.Length >= outputRoot.Length; parent = Path.GetDirectoryName(parent))
            {
                if (Directory.Exists(parent) && File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("A converted file uses a linked directory.");
                if (string.Equals(parent, outputRoot, pathComparison)) break;
            }
            if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
                || new FileInfo(path).Length != file.Length || rawBytes + file.Length > MaxRawAttachmentBytes)
                throw new InvalidDataException("A converted file is missing, linked or oversized.");
            // Open the inspected file itself, without following a link introduced
            // between inspection and opening. Keep that handle through the copy:
            // a later rename cannot redirect the read to another file.
            await using var reader = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open, Access = FileAccess.Read,
                Share = FileShare.Read | FileShare.Delete,
                Options = FileOptions.OpenReparsePoint | FileOptions.Asynchronous,
            });
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("A converted file became linked before sealing.");
            if (ProbeBarrierAsync is { } copyBarrier)
                await copyBarrier("output-before-sealed-copy", deliveryId, ct);
            if (reader.Length != file.Length)
                throw new InvalidDataException("A converted file length changed before sealing.");
            var bytes = new byte[checked((int)file.Length)];
            await reader.ReadExactlyAsync(bytes, ct);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A converted file hash does not match its descriptor.");
            rawBytes += bytes.Length;
            attachments.Add(new OutboundAttachment
            {
                Kind = file.Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    ? AttachmentKind.Image : AttachmentKind.File,
                Name = file.Name, Mime = file.Mime, Content = bytes,
            });
        }

        var result = original with
        {
            Text = manifest.ReplacementText ?? original.Text,
            Attachments = attachments,
        };
        var replyBytes = JsonSerializer.SerializeToUtf8Bytes(result, MessagingJson.Options);
        if (replyBytes.Length > maxMessageBytes)
            throw new InvalidDataException("The converted reply exceeds the messaging budget.");
        var replyHash = Convert.ToHexString(SHA256.HashData(replyBytes)).ToLowerInvariant();
        if (File.Exists(sealedPath))
        {
            // A crash may leave our completed copy before the Ready commit. A worker
            // can also write beside output/, so an existing file is never authority.
            var priorBytes = await File.ReadAllBytesAsync(sealedPath, ct);
            if (!priorBytes.AsSpan().SequenceEqual(replyBytes))
                throw new InvalidDataException("The existing sealed reply differs from validated output.");
            return new ChannelOutboundSealed(sealedPath, replyHash,
                manifest.Disposition == "converted" ? "Converted" : "Unchanged");
        }
        var temporary = sealedPath + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllBytesAsync(temporary, replyBytes, ct);
        try { File.Move(temporary, sealedPath); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new ChannelOutboundSealed(sealedPath, replyHash,
            manifest.Disposition == "converted" ? "Converted" : "Unchanged");
    }
}
