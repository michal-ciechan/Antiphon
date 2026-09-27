using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Files;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class ChannelOutboundStorageTests
{
    [Test]
    public async Task Frozen_reply_and_input_bytes_survive_source_mutation()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-files-" + Guid.NewGuid().ToString("N"));
        var store = new ChannelOutboundFileStore(root);
        var id = Guid.NewGuid();
        var bytes = new byte[] { 0, 10, 255, 42 };
        var reply = new ChannelReply
        {
            Channel = "slack", ConversationId = "C1", ReplyHandle = "C1|thread-1", Text = "source",
            Attachments = [new OutboundAttachment { Kind = AttachmentKind.File, Name = "source.md",
                Mime = "text/markdown", Content = bytes }],
        };
        try
        {
            var snapshot = await store.StageAsync(id, reply, CancellationToken.None);
            bytes[0] = 99;
            var frozen = await store.ReadReplyAsync(snapshot.ReplyPath, snapshot.ReplySha256,
                CancellationToken.None);
            frozen.ReplyHandle.ShouldBe("C1|thread-1");
            frozen.Attachments.Single().Content.ShouldBe(new byte[] { 0, 10, 255, 42 });
            using var request = JsonDocument.Parse(await File.ReadAllTextAsync(snapshot.RequestPath));
            request.RootElement.GetProperty("deliveryId").GetGuid().ShouldBe(id);
            var input = Path.Combine(Path.GetDirectoryName(snapshot.RequestPath)!, "input", "attachment-001.md");
            (await File.ReadAllBytesAsync(input)).ShouldBe(new byte[] { 0, 10, 255, 42 });
            Directory.GetDirectories(root, ".stage-*").ShouldBeEmpty();

            await File.AppendAllTextAsync(snapshot.ReplyPath, "tampered");
            await Should.ThrowAsync<InvalidDataException>(() => store.ReadReplyAsync(
                snapshot.ReplyPath, snapshot.ReplySha256, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Oversized_reply_never_exposes_snapshot_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-files-" + Guid.NewGuid().ToString("N"));
        var store = new ChannelOutboundFileStore(root);
        var id = Guid.NewGuid();
        var reply = new ChannelReply
        {
            Channel = "fake", ConversationId = "c",
            Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                Content = new byte[14 * 1024 * 1024 + 1] }],
        };
        await Should.ThrowAsync<InvalidDataException>(() => store.StageAsync(id, reply, CancellationToken.None));
        Directory.Exists(Path.Combine(root, id.ToString("N"))).ShouldBeFalse();
    }

    [Test]
    public async Task Exactly_fourteen_mebibytes_of_original_bytes_can_be_staged()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-raw-cap-" + Guid.NewGuid().ToString("N"));
        var store = new ChannelOutboundFileStore(root);
        try
        {
            var bytes = new byte[14 * 1024 * 1024];
            Random.Shared.NextBytes(bytes);
            var reply = new ChannelReply
            {
                Channel = "slack", ConversationId = "C1",
                Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                    Name = "source.md", Mime = "text/markdown", Content = bytes }],
            };
            var snapshot = await store.StageAsync(Guid.NewGuid(), reply, CancellationToken.None);
            var restored = await store.ReadReplyAsync(snapshot.ReplyPath, snapshot.ReplySha256,
                CancellationToken.None);
            restored.Attachments.ShouldHaveSingleItem().Content.ShouldBe(bytes);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task Serialized_limit_counts_unicode_metadata_and_base64_at_exact_boundary()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-wire-cap-" + Guid.NewGuid().ToString("N"));
        var store = new ChannelOutboundFileStore(root);
        var id = Guid.NewGuid();
        using var raw = JsonDocument.Parse("""{"parse_mode":"MarkdownV2","note":"zażółć ✨"}""");
        var originalBytes = new byte[] { 0, 1, 2, 255 };
        var addition = new byte[] { 37, 80, 68, 70, 45, 49, 46, 55, 10 };
        try
        {
            var reply = new ChannelReply
            {
                Channel = "slack", ConversationId = "C1", ReplyHandle = "C1|wątek-✨",
                Text = "Polski tekst ✨", RawOverrides = raw.RootElement.Clone(),
                Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                    Name = "źródło.md", Mime = "text/markdown", Caption = "oryginał ✨",
                    Content = originalBytes, Source = "/never/read/from/source.md" }],
            };
            var snapshot = await store.StageAsync(id, reply, CancellationToken.None);
            await File.WriteAllBytesAsync(Path.Combine(snapshot.OutputDirectory, "combined.pdf"), addition);
            await File.WriteAllTextAsync(Path.Combine(snapshot.OutputDirectory, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    version = 1, deliveryId = id, disposition = "converted",
                    replacementText = "Gotowe ✨",
                    files = new[] { new { path = "combined.pdf", name = "combined.pdf",
                        mime = "application/pdf", length = addition.Length,
                        sha256 = Convert.ToHexString(SHA256.HashData(addition)).ToLowerInvariant() } },
                }));
            var expected = reply with { Text = "Gotowe ✨", Attachments =
                [.. reply.Attachments, new OutboundAttachment { Kind = AttachmentKind.File,
                    Name = "combined.pdf", Mime = "application/pdf", Content = addition }] };
            var wireLength = JsonSerializer.SerializeToUtf8Bytes(expected, MessagingJson.Options).Length;
            await Should.ThrowAsync<InvalidDataException>(() => store.ValidateAndSealAsync(id,
                snapshot.ReplyPath, snapshot.ReplySha256, wireLength - 1, CancellationToken.None));
            var sealedReply = await store.ValidateAndSealAsync(id, snapshot.ReplyPath,
                snapshot.ReplySha256, wireLength, CancellationToken.None);
            var actual = await store.ReadReplyAsync(sealedReply.ReplyPath,
                sealedReply.ReplySha256, CancellationToken.None);
            JsonSerializer.SerializeToUtf8Bytes(actual, MessagingJson.Options).Length.ShouldBe(wireLength);
            actual.Attachments[0].Content.ShouldBe(originalBytes);
            actual.Attachments[1].Content.ShouldBe(addition);
            actual.ReplyHandle.ShouldBe("C1|wątek-✨");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task Converted_files_are_sealed_with_original_sources_and_frozen_routing()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-seal-" + Guid.NewGuid().ToString("N"));
        var store = new ChannelOutboundFileStore(root);
        var id = Guid.NewGuid();
        var source = new byte[] { 1, 2, 3, 4 };
        var pdf = new byte[] { 37, 80, 68, 70, 45, 49, 46, 55, 10 };
        var reply = new ChannelReply
        {
            Channel = "slack", ConversationId = "C1", ReplyHandle = "C1|old-thread",
            Text = "source", Attachments = [new OutboundAttachment
            {
                Kind = AttachmentKind.File, Name = "source.md", Mime = "text/markdown", Content = source,
            }],
        };
        try
        {
            var snapshot = await store.StageAsync(id, reply, CancellationToken.None);
            var output = snapshot.OutputDirectory;
            var pdfPath = Path.Combine(output, "combined.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdf);
            var manifest = new
            {
                version = 1, deliveryId = id, disposition = "converted",
                replacementText = "converted",
                files = new[] { new { path = "combined.pdf", name = "combined.pdf",
                    mime = "application/pdf", length = pdf.Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant() } },
            };
            await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest));
            var sealedReply = await store.ValidateAndSealAsync(id, snapshot.ReplyPath,
                snapshot.ReplySha256, 20 * 1024 * 1024, CancellationToken.None);
            sealedReply.Outcome.ShouldBe("Converted");
            await File.WriteAllBytesAsync(pdfPath, [0]);
            var frozen = await store.ReadReplyAsync(sealedReply.ReplyPath, sealedReply.ReplySha256,
                CancellationToken.None);
            frozen.Channel.ShouldBe("slack");
            frozen.ReplyHandle.ShouldBe("C1|old-thread");
            frozen.Text.ShouldBe("converted");
            frozen.Attachments.Count.ShouldBe(2);
            frozen.Attachments[0].Content.ShouldBe(source);
            frozen.Attachments[1].Content.ShouldBe(pdf);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task Manifested_zip_sources_are_expanded_from_attached_bytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-zip-" + Guid.NewGuid().ToString("N"));
        var store = new ChannelOutboundFileStore(root);
        var id = Guid.NewGuid();
        var source = System.Text.Encoding.UTF8.GetBytes("# Original source\n");
        byte[] zipBytes;
        using (var zipStream = new MemoryStream())
        {
            using (var zip = new System.IO.Compression.ZipArchive(zipStream,
                       System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                await using var entry = zip.CreateEntry("docs/source.md").Open();
                await entry.WriteAsync(source);
            }
            zipBytes = zipStream.ToArray();
        }
        var manifest = new DeliverableBundleService.SourceManifest(1, true,
            [new("docs/source.md", "sources.zip", "docs/source.md", source.Length,
                Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant())], []);
        var reply = new ChannelReply
        {
            Channel = "slack", ConversationId = "C1",
            Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                Name = "sources.zip", Mime = "application/zip", Content = zipBytes }],
        };
        try
        {
            var snapshot = await store.StageAsync(id, reply, CancellationToken.None,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var input = Path.Combine(Path.GetDirectoryName(snapshot.RequestPath)!, "input");
            (await File.ReadAllBytesAsync(Path.Combine(input, "source-001.md"))).ShouldBe(source);
            using var request = JsonDocument.Parse(await File.ReadAllTextAsync(snapshot.RequestPath));
            request.RootElement.GetProperty("sourceFiles").GetArrayLength().ShouldBe(1);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    [Arguments("parent_path")]
    [Arguments("absolute_path")]
    [Arguments("backslash_path")]
    [Arguments("stored_file_traversal")]
    [Arguments("case_collision")]
    [Arguments("zip_entry_differs")]
    public async Task Source_zip_manifest_rejects_unsafe_or_ambiguous_members(string fault)
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-zip-path-" + Guid.NewGuid().ToString("N"));
        var store = new ChannelOutboundFileStore(root);
        var bytes = "# Valid source\n"u8.ToArray();
        var entries = new Dictionary<string, byte[]>
        {
            ["docs/source.md"] = bytes,
            ["docs/other.md"] = bytes,
            ["../escape.md"] = bytes,
            ["/absolute.md"] = bytes,
            ["..\\escape.md"] = bytes,
            ["docs/A.md"] = bytes,
            ["docs/a.md"] = bytes,
        };
        byte[] zipBytes;
        using (var zipStream = new MemoryStream())
        {
            using (var zip = new System.IO.Compression.ZipArchive(zipStream,
                       System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, content) in entries)
                {
                    await using var entry = zip.CreateEntry(path).Open();
                    await entry.WriteAsync(content);
                }
            }
            zipBytes = zipStream.ToArray();
        }
        var reply = new ChannelReply
        {
            Channel = "slack", ConversationId = "C1",
            Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                Name = "source.zip", Mime = "application/zip", Content = zipBytes }],
        };
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        DeliverableBundleService.SourceMember Member(string original, string? entry = null,
            string stored = "source.zip") => new(original, stored, entry ?? original, bytes.Length, hash);
        string Json(params DeliverableBundleService.SourceMember[] members) => JsonSerializer.Serialize(
            new DeliverableBundleService.SourceManifest(1, true, members, []),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        try
        {
            var valid = await store.StageAsync(Guid.NewGuid(), reply, CancellationToken.None,
                Json(Member("docs/source.md")));
            var staged = Path.Combine(Path.GetDirectoryName(valid.RequestPath)!, "input", "source-001.md");
            (await File.ReadAllBytesAsync(staged)).ShouldBe(bytes);

            var invalid = fault switch
            {
                "parent_path" => Json(Member("../escape.md")),
                "absolute_path" => Json(Member("/absolute.md")),
                "backslash_path" => Json(Member("..\\escape.md")),
                "stored_file_traversal" => Json(Member("docs/source.md", stored: "../source.zip")),
                "case_collision" => Json(Member("docs/A.md"), Member("docs/a.md")),
                "zip_entry_differs" => Json(Member("docs/source.md", "docs/other.md")),
                _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, null),
            };
            var id = Guid.NewGuid();
            await Should.ThrowAsync<InvalidDataException>(() => store.StageAsync(id, reply,
                CancellationToken.None, invalid));
            Directory.Exists(Path.Combine(root, id.ToString("N"))).ShouldBeFalse();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task Worker_created_sealed_reply_cannot_bypass_manifest_validation_on_recovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-forged-seal-" + Guid.NewGuid().ToString("N"));
        var store = new ChannelOutboundFileStore(root);
        var id = Guid.NewGuid();
        try
        {
            var snapshot = await store.StageAsync(id, new ChannelReply
            {
                Channel = "slack", ConversationId = "C1", ReplyHandle = "C1|original",
                Text = "source",
            }, CancellationToken.None);
            var manifest = new { version = 1, deliveryId = id, disposition = "converted",
                replacementText = "converted", files = Array.Empty<object>() };
            await File.WriteAllTextAsync(Path.Combine(snapshot.OutputDirectory, "manifest.json"),
                JsonSerializer.Serialize(manifest));
            var sealedPath = Path.Combine(Path.GetDirectoryName(snapshot.ReplyPath)!, "sealed-reply.json");
            var forged = new ChannelReply { Channel = "slack", ConversationId = "C1",
                ReplyHandle = "C1|attacker", Text = "converted" };
            await File.WriteAllBytesAsync(sealedPath,
                JsonSerializer.SerializeToUtf8Bytes(forged, MessagingJson.Options));

            await Should.ThrowAsync<InvalidDataException>(() => store.ValidateAndSealAsync(id,
                snapshot.ReplyPath, snapshot.ReplySha256, 20 * 1024 * 1024, CancellationToken.None));
            File.Delete(sealedPath);
            var sealedReply = await store.ValidateAndSealAsync(id, snapshot.ReplyPath,
                snapshot.ReplySha256, 20 * 1024 * 1024, CancellationToken.None);
            (await store.ReadReplyAsync(sealedReply.ReplyPath, sealedReply.ReplySha256,
                CancellationToken.None)).ReplyHandle.ShouldBe("C1|original");
            var recovered = await store.ValidateAndSealAsync(id, snapshot.ReplyPath,
                snapshot.ReplySha256, 20 * 1024 * 1024, CancellationToken.None);
            recovered.ReplySha256.ShouldBe(sealedReply.ReplySha256);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task Output_manifest_cannot_override_frozen_routing()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-route-" + Guid.NewGuid().ToString("N"));
        var store = new ChannelOutboundFileStore(root);
        var id = Guid.NewGuid();
        try
        {
            var snapshot = await store.StageAsync(id, new ChannelReply
            {
                Channel = "slack", ConversationId = "C1", ReplyHandle = "C1|old-thread", Text = "source",
            }, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(snapshot.OutputDirectory, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    version = 1, deliveryId = id, disposition = "converted", files = Array.Empty<object>(),
                    replyHandle = "C1|attacker-thread",
                }));
            await Should.ThrowAsync<InvalidDataException>(() => store.ValidateAndSealAsync(id,
                snapshot.ReplyPath, snapshot.ReplySha256, 20 * 1024 * 1024, CancellationToken.None));
            File.Exists(Path.Combine(root, id.ToString("N"), "sealed-reply.json")).ShouldBeFalse();

            if (!OperatingSystem.IsWindows())
            {
                var sibling = Path.Combine(Path.GetDirectoryName(snapshot.OutputDirectory)!, "OUTPUT");
                Directory.CreateDirectory(sibling);
                var foreign = new byte[] { 1, 2, 3 };
                await File.WriteAllBytesAsync(Path.Combine(sibling, "foreign.pdf"), foreign);
                await File.WriteAllTextAsync(Path.Combine(snapshot.OutputDirectory, "manifest.json"),
                    JsonSerializer.Serialize(new
                    {
                        version = 1, deliveryId = id, disposition = "converted",
                        files = new[] { new { path = "../OUTPUT/foreign.pdf", name = "foreign.pdf",
                            mime = "application/pdf", length = foreign.Length,
                            sha256 = Convert.ToHexString(SHA256.HashData(foreign)).ToLowerInvariant() } },
                    }));
                await Should.ThrowAsync<InvalidDataException>(() => store.ValidateAndSealAsync(id,
                    snapshot.ReplyPath, snapshot.ReplySha256, 20 * 1024 * 1024, CancellationToken.None));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task Complete_source_stamp_requires_every_manifested_attachment()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-stamp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "first.md");
            var second = Path.Combine(root, "second.md");
            await File.WriteAllTextAsync(first, "# First");
            await File.WriteAllTextAsync(second, "# Second");
            var manifest = new DeliverableBundleService.SourceManifest(1, true,
            [
                new("docs/first.md", "first.md", null, 7, new string('a', 64)),
                new("docs/second.md", "second.md", null, 8, new string('b', 64)),
            ], []);
            await File.WriteAllTextAsync(Path.Combine(root, DeliverableBundleService.SourceManifestName),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var task = new Antiphon.Server.Domain.Entities.AgentTask { DeliverableBundleDir = root };
            var both = new[]
            {
                new OutboundAttachment { Kind = AttachmentKind.File, Name = "first.md", Source = first, Content = "# First"u8.ToArray() },
                new OutboundAttachment { Kind = AttachmentKind.File, Name = "second.md", Source = second, Content = "# Second"u8.ToArray() },
            };
            ChannelOutboundDeliveryPump.HasCompleteSourceAttachments(task, manifest, both).ShouldBeTrue();
            ChannelOutboundDeliveryPump.HasCompleteSourceAttachments(task, manifest, both[..1]).ShouldBeFalse();

            File.Delete(second);
            ChannelOutboundDeliveryPump.HasCompleteSourceAttachments(task, manifest, both).ShouldBeFalse();
            File.Delete(first);
            ChannelOutboundDeliveryPump.HasCompleteSourceAttachments(task, manifest, both).ShouldBeFalse();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
