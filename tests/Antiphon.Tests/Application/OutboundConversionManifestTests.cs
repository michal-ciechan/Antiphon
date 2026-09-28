using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Messaging;
using Antiphon.Server.Infrastructure.Files;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class OutboundConversionManifestTests
{
    [Test]
    public async Task Valid_results_add_files_and_seal_bytes()
    {
        var root = Directory.CreateTempSubdirectory("c0418-output-positive-").FullName;
        var store = new ChannelOutboundFileStore(root);
        var source = new byte[] { 0, 10, 255, 42 };
        var output = new Dictionary<string, (string Mime, byte[] Bytes)>
        {
            ["combined.pdf"] = ("application/pdf", [37, 80, 68, 70, 45, 49, 46, 55, 10]),
            ["chart.png"] = ("image/png", [137, 80, 78, 71, 0, 255]),
            ["notes.txt"] = ("text/plain", [1, 2, 3, 4]),
        };
        try
        {
            var id = Guid.NewGuid();
            var original = new ChannelReply
            {
                Channel = "slack", ConversationId = "C1", ReplyHandle = "C1|T1",
                ReplyToMessageId = "T1", Text = "source text",
                Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                    Name = "source.md", Mime = "text/markdown", Content = source }],
            };
            var snapshot = await store.StageAsync(id, original, CancellationToken.None);
            foreach (var (name, file) in output)
                await File.WriteAllBytesAsync(Path.Combine(snapshot.OutputDirectory, name), file.Bytes);
            await File.WriteAllTextAsync(Path.Combine(snapshot.OutputDirectory, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    version = 1, deliveryId = id, disposition = "converted",
                    replacementText = "converted text",
                    files = output.Select(f => new
                    {
                        path = f.Key, name = f.Key, mime = f.Value.Mime,
                        length = f.Value.Bytes.Length,
                        sha256 = Convert.ToHexString(SHA256.HashData(f.Value.Bytes)).ToLowerInvariant(),
                    }).ToArray(),
                }));
            var sealedOutput = await store.ValidateAndSealAsync(id, snapshot.ReplyPath,
                snapshot.ReplySha256, 20 * 1024 * 1024, CancellationToken.None);
            sealedOutput.Outcome.ShouldBe("Converted");
            foreach (var name in output.Keys)
                await File.WriteAllBytesAsync(Path.Combine(snapshot.OutputDirectory, name), [99]);
            File.Delete(snapshot.ReplyPath);
            var frozen = await store.ReadReplyAsync(sealedOutput.ReplyPath,
                sealedOutput.ReplySha256, CancellationToken.None);
            frozen.Channel.ShouldBe("slack");
            frozen.ConversationId.ShouldBe("C1");
            frozen.ReplyHandle.ShouldBe("C1|T1");
            frozen.ReplyToMessageId.ShouldBe("T1");
            frozen.Text.ShouldBe("converted text");
            frozen.Attachments.Count.ShouldBe(4);
            frozen.Attachments[0].Name.ShouldBe("source.md");
            frozen.Attachments[0].Content.ShouldBe(source);
            foreach (var attachment in frozen.Attachments.Skip(1))
                attachment.Content.ShouldBe(output[attachment.Name!].Bytes);
            frozen.Attachments.Single(a => a.Name == "chart.png").Kind.ShouldBe(AttachmentKind.Image);

            var unchangedId = Guid.NewGuid();
            var unchanged = await store.StageAsync(unchangedId, original with
                { ReplyHandle = "C2|T2", ConversationId = "C2" }, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(unchanged.OutputDirectory, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    version = 1, deliveryId = unchangedId, disposition = "unchanged",
                    files = Array.Empty<object>(),
                }));
            var unchangedSeal = await store.ValidateAndSealAsync(unchangedId,
                unchanged.ReplyPath, unchanged.ReplySha256, 20 * 1024 * 1024, CancellationToken.None);
            unchangedSeal.Outcome.ShouldBe("Unchanged");
            var unchangedReply = await store.ReadReplyAsync(unchangedSeal.ReplyPath,
                unchangedSeal.ReplySha256, CancellationToken.None);
            unchangedReply.Text.ShouldBe("source text");
            unchangedReply.ReplyHandle.ShouldBe("C2|T2");
            unchangedReply.Attachments.ShouldHaveSingleItem().Content.ShouldBe(source);
            unchangedReply.Attachments.ShouldNotContain(a => a.Name == "combined.pdf");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [Arguments("missing_json")]
    [Arguments("malformed_json")]
    [Arguments("wrong_version")]
    [Arguments("wrong_delivery_id")]
    [Arguments("missing_disposition")]
    [Arguments("invalid_disposition")]
    [Arguments("missing_files")]
    [Arguments("too_many_descriptors")]
    [Arguments("duplicate_name")]
    [Arguments("case_colliding_name")]
    [Arguments("missing_file")]
    [Arguments("wrong_length")]
    [Arguments("wrong_hash")]
    [Arguments("absolute_path")]
    [Arguments("parent_path")]
    [Arguments("backslash_path")]
    [Arguments("sibling_prefix_path")]
    [Arguments("linked_file")]
    [Arguments("linked_directory")]
    [Arguments("unc_path")]
    [Arguments("empty_name")]
    [Arguments("empty_mime")]
    [Arguments("unknown_manifest_field")]
    [Arguments("unknown_file_field")]
    [Arguments("long_replacement_text")]
    [Arguments("route_channel")]
    [Arguments("route_conversation_id")]
    [Arguments("route_reply_handle")]
    [Arguments("route_reply_to_message_id")]
    [Arguments("route_kind")]
    [Arguments("route_raw_overrides")]
    [Arguments("route_source_task_id")]
    [Arguments("route_project")]
    [Arguments("route_policy")]
    public async Task Invalid_output_is_rejected_with_a_valid_adjacent_control(string fault)
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-manifest-" + Guid.NewGuid().ToString("N"));
        var store = new ChannelOutboundFileStore(root);
        var id = Guid.NewGuid();
        try
        {
            var snapshot = await store.StageAsync(id, new ChannelReply
            {
                Channel = "slack", ConversationId = "C1", ReplyHandle = "C1|thread-1",
                Text = "original",
            }, CancellationToken.None);
            var pdf = new byte[] { 37, 80, 68, 70, 45, 49, 46, 55, 10 };
            await File.WriteAllBytesAsync(Path.Combine(snapshot.OutputDirectory, "combined.pdf"), pdf);
            var manifestPath = Path.Combine(snapshot.OutputDirectory, "manifest.json");
            var file = new JsonObject
            {
                ["path"] = "combined.pdf", ["name"] = "combined.pdf",
                ["mime"] = "application/pdf", ["length"] = pdf.Length,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant(),
            };
            var manifest = new JsonObject
            {
                ["version"] = 1, ["deliveryId"] = id.ToString(),
                ["disposition"] = "converted", ["replacementText"] = "converted",
                ["files"] = new JsonArray(file),
            };
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
            var valid = await store.ValidateAndSealAsync(id, snapshot.ReplyPath,
                snapshot.ReplySha256, 20 * 1024 * 1024, CancellationToken.None);
            var original = await store.ReadReplyAsync(valid.ReplyPath, valid.ReplySha256,
                CancellationToken.None);
            original.ReplyHandle.ShouldBe("C1|thread-1");
            original.Attachments.ShouldHaveSingleItem().Content.ShouldBe(pdf);
            // The positive control has proven the route. Rejection must now come from
            // the changed field, not from a mismatch with an existing sealed copy.
            File.Delete(valid.ReplyPath);

            switch (fault)
            {
                case "missing_json": File.Delete(manifestPath); break;
                case "malformed_json": await File.WriteAllTextAsync(manifestPath, "{"); break;
                case "wrong_version": manifest["version"] = 2; break;
                case "wrong_delivery_id": manifest["deliveryId"] = Guid.NewGuid().ToString(); break;
                case "missing_disposition": manifest.Remove("disposition"); break;
                case "invalid_disposition": manifest["disposition"] = "sent"; break;
                case "missing_files": manifest.Remove("files"); break;
                case "too_many_descriptors":
                    for (var i = 0; i < 16; i++)
                    {
                        var name = $"extra-{i:D2}.pdf";
                        await File.WriteAllBytesAsync(Path.Combine(snapshot.OutputDirectory, name), pdf);
                        var extra = (JsonObject)file.DeepClone();
                        extra["path"] = name;
                        extra["name"] = name;
                        manifest["files"]!.AsArray().Add(extra);
                    }
                    break;
                case "duplicate_name":
                    await File.WriteAllBytesAsync(Path.Combine(snapshot.OutputDirectory, "second.pdf"), pdf);
                    var duplicate = (JsonObject)file.DeepClone();
                    duplicate["path"] = "second.pdf";
                    manifest["files"]!.AsArray().Add(duplicate);
                    break;
                case "case_colliding_name":
                    await File.WriteAllBytesAsync(Path.Combine(snapshot.OutputDirectory, "second.pdf"), pdf);
                    var caseCollision = (JsonObject)file.DeepClone();
                    caseCollision["path"] = "second.pdf";
                    caseCollision["name"] = "COMBINED.PDF";
                    manifest["files"]!.AsArray().Add(caseCollision);
                    break;
                case "missing_file": file["path"] = "absent.pdf"; break;
                case "wrong_length": file["length"] = pdf.Length + 1; break;
                case "wrong_hash": file["sha256"] = new string('0', 64); break;
                case "absolute_path":
                    await File.WriteAllBytesAsync(Path.Combine(root, "elsewhere.pdf"), pdf);
                    file["path"] = Path.GetFullPath(Path.Combine(root, "elsewhere.pdf"));
                    break;
                case "parent_path":
                    await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(snapshot.OutputDirectory)!,
                        "elsewhere.pdf"), pdf);
                    file["path"] = "../elsewhere.pdf";
                    break;
                case "backslash_path":
                    await File.WriteAllBytesAsync(Path.Combine(snapshot.OutputDirectory,
                        "..\\elsewhere.pdf"), pdf);
                    file["path"] = "..\\elsewhere.pdf";
                    break;
                case "sibling_prefix_path":
                    var sibling = Path.Combine(Path.GetDirectoryName(snapshot.OutputDirectory)!,
                        "output-elsewhere");
                    Directory.CreateDirectory(sibling);
                    await File.WriteAllBytesAsync(Path.Combine(sibling, "file.pdf"), pdf);
                    file["path"] = "../output-elsewhere/file.pdf";
                    break;
                case "linked_file":
                    var outsideFile = Path.Combine(root, "outside-file.pdf");
                    await File.WriteAllBytesAsync(outsideFile, pdf);
                    try { File.CreateSymbolicLink(Path.Combine(snapshot.OutputDirectory, "linked.pdf"), outsideFile); }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException)
                    { throw new SkipTestException("A fixture-owned file link could not be created: " + ex.Message); }
                    file["path"] = "linked.pdf";
                    break;
                case "linked_directory":
                    var outsideDirectory = Path.Combine(root, "outside-directory");
                    Directory.CreateDirectory(outsideDirectory);
                    await File.WriteAllBytesAsync(Path.Combine(outsideDirectory, "combined.pdf"), pdf);
                    try { Directory.CreateSymbolicLink(Path.Combine(snapshot.OutputDirectory, "linked"), outsideDirectory); }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException)
                    { throw new SkipTestException("A fixture-owned directory link could not be created: " + ex.Message); }
                    file["path"] = "linked/combined.pdf";
                    break;
                case "unc_path": file["path"] = @"\\server\share\combined.pdf"; break;
                case "empty_name": file["name"] = ""; break;
                case "empty_mime": file["mime"] = ""; break;
                case "unknown_manifest_field": manifest["unrecognized"] = "x"; break;
                case "unknown_file_field": file["unrecognized"] = "x"; break;
                case "long_replacement_text": manifest["replacementText"] = new string('x', 20_001); break;
                case "route_channel": manifest["channel"] = "attacker"; break;
                case "route_conversation_id": manifest["conversationId"] = "C2"; break;
                case "route_reply_handle": manifest["replyHandle"] = "C2|thread-2"; break;
                case "route_reply_to_message_id": manifest["replyToMessageId"] = "root"; break;
                case "route_kind": manifest["kind"] = "control"; break;
                case "route_raw_overrides": manifest["rawOverrides"] = new JsonObject(); break;
                case "route_source_task_id": manifest["sourceTaskId"] = Guid.NewGuid().ToString(); break;
                case "route_project": manifest["projectId"] = Guid.NewGuid().ToString(); break;
                case "route_policy": manifest["policy"] = "bypass"; break;
                default: throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
            }
            if (fault is not ("missing_json" or "malformed_json"))
                await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
            var invalid = () => store.ValidateAndSealAsync(id, snapshot.ReplyPath,
                snapshot.ReplySha256, 20 * 1024 * 1024, CancellationToken.None);
            if (fault == "missing_json") await Should.ThrowAsync<FileNotFoundException>(invalid);
            else if (fault == "malformed_json") await Should.ThrowAsync<JsonException>(invalid);
            else await Should.ThrowAsync<InvalidDataException>(invalid);
            File.Exists(valid.ReplyPath).ShouldBeFalse();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
