using System.Security.Cryptography;
using System.Text;
using Antiphon.Messaging;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class ChannelReplyAttachmentReaderTests
{
    private const string Contents = "owned attachment: caf\u00e9\n";

    [Test]
    public async Task C1061_Byte_and_text_reads_reject_hard_links()
    {
        using var fixture = new LinkFixture();
        var reader = new ChannelReplyAttachmentReader();
        foreach (var arrangement in fixture.Arrangements())
        {
            fixture.CreateAndObserve(arrangement);
            await Should.ThrowAsync<InvalidDataException>(() => reader.ReadAttachmentAsync(arrangement.File,
                arrangement.Roots, 256, default), "Links.BytesRefused: " + arrangement.Name);
            await Should.ThrowAsync<InvalidDataException>(() => reader.ReadTextAsync(arrangement.File,
                arrangement.Roots, 256, default), "Links.TextRefused: " + arrangement.Name);
            File.Delete(arrangement.Alias);
            NativeHardLink.Observe(arrangement.File).Links.ShouldBe(1u);
            (await reader.ReadAttachmentAsync(arrangement.File, arrangement.Roots, 256, default)).ShouldBe(fixture.Bytes);
            (await reader.ReadTextAsync(arrangement.File, arrangement.Roots, 256, default)).ShouldBe(Contents);
        }
        foreach (var text in new[] { false, true })
        {
            var file = Path.Combine(fixture.Allowed, "cut-" + text);
            var alias = Path.Combine(fixture.Outside, "cut-" + text);
            await File.WriteAllBytesAsync(file, fixture.Bytes);
            NativeHardLink.Observe(file).Links.ShouldBe(1u);
            var hooks = 0;
            using var caller = new CancellationTokenSource();
            reader.BeforeOpenAsync = (path, ct) =>
            {
                hooks++;
                path.ShouldBe(file);
                ct.ShouldBe(caller.Token);
                NativeHardLink.Create(alias, path);
                fixture.ObservePair(file, alias);
                return Task.CompletedTask;
            };
            await Should.ThrowAsync<InvalidDataException>(async () =>
            {
                if (text) await reader.ReadTextAsync(file, [fixture.Allowed], 256, caller.Token);
                else await reader.ReadAttachmentAsync(file, [fixture.Allowed], 256, caller.Token);
            }, text ? "Links.TextRefused: after validation" : "Links.BytesRefused: after validation");
            hooks.ShouldBe(1);
        }
    }

    [Test]
    public async Task C1061_Hashes_reject_hard_links()
    {
        using var fixture = new LinkFixture();
        var expectedHash = Convert.ToHexString(SHA256.HashData(fixture.Bytes)).ToLowerInvariant();
        foreach (var arrangement in fixture.Arrangements())
        {
            fixture.CreateAndObserve(arrangement);
            await Should.ThrowAsync<InvalidDataException>(() => ChannelReplyAttachmentReader.HashFileAsync(
                arrangement.File, arrangement.Roots, 256, default), "Links.HashRefused: " + arrangement.Name);
            File.Delete(arrangement.Alias);
            NativeHardLink.Observe(arrangement.File).Links.ShouldBe(1u);
            var hash = await ChannelReplyAttachmentReader.HashFileAsync(arrangement.File, arrangement.Roots, 256, default);
            hash.Length.ShouldBe(fixture.Bytes.LongLength);
            hash.Sha256.ShouldBe(expectedHash);
        }
        var single = Path.Combine(fixture.Allowed, "single.txt");
        await File.WriteAllBytesAsync(single, fixture.Bytes);
        NativeHardLink.Observe(single).Links.ShouldBe(1u);
        var singleHash = await ChannelReplyAttachmentReader.HashFileAsync(single, [fixture.Allowed], 256, default);
        singleHash.Length.ShouldBe(fixture.Bytes.LongLength);
        singleHash.Sha256.ShouldBe(expectedHash);

        var store = new ChannelOutboundFileStore(Path.Combine(fixture.Root, "snapshots"));
        const string prompt = "Prepare the owned fixture attachment.";
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant();
        var reply = new ChannelReply
        {
            Channel = "fixture", ConversationId = "owned", Text = "fixture reply",
            Attachments = [new OutboundAttachment { Name = "source.txt", Mime = "text/plain",
                Kind = AttachmentKind.File, Content = fixture.Bytes }],
        };
        var prepared = new ChannelReplyPrepared(reply, prompt, revision, null);
        var id = Guid.NewGuid();
        const string capture = "{\"fixture\":\"C1061\"}";
        var staged = await store.StageCapturedAsync(id, capture, prepared, default);
        var adopted = await store.TryAdoptAsync(id, capture, default);
        adopted.ShouldNotBeNull();
        staged.PromptText.ShouldBe(prompt);
        staged.PromptRevision.ShouldBe(revision);
        adopted.PromptText.ShouldBe(prompt);
        adopted.PromptRevision.ShouldBe(revision);
        adopted.Snapshot.ShouldBe(staged.Snapshot);
        var storedReply = await store.ReadReplyAsync(adopted.Snapshot.ReplyPath, adopted.Snapshot.ReplySha256, default);
        storedReply.Attachments.Single().Content.ShouldBe(fixture.Bytes);
        var storedBytes = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(adopted.Snapshot.ReplyPath)!,
            "input", "attachment-001.txt"));
        storedBytes.ShouldBe(fixture.Bytes);
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(adopted.Snapshot.ReplyPath)))
            .ToLowerInvariant().ShouldBe(adopted.Snapshot.ReplySha256);
    }

    [Test]
    public void C1061_Linux_metadata_requires_type_and_link_count()
    {
        foreach (var (mask, label) in new[] { (0u, "Metadata.BothRequired"), (1u, "Metadata.LinksRequired"), (4u, "Metadata.TypeRequired") })
            Should.Throw<InvalidDataException>(() => ChannelReplyAttachmentReader.ValidateLinuxMetadata(mask, 0x8000, 1), label);
        foreach (var mask in new[] { 5u, 5u | 0x200 })
            ChannelReplyAttachmentReader.ValidateLinuxMetadata(mask, 0x8000, 1);
        foreach (var mode in new ushort[] { 0x4000, 0x1000, 0x2000 })
            Should.Throw<InvalidDataException>(() => ChannelReplyAttachmentReader.ValidateLinuxMetadata(5, mode, 1), "Metadata.RegularRequired");
    }

    [Test]
    public void C1061_Zero_link_count_is_rejected()
    {
        foreach (var count in new[] { 0u, 2u, uint.MaxValue })
        {
            var label = count == 0 ? "Count.ZeroRefused" : "Count.MultipleRefused";
            Should.Throw<InvalidDataException>(() => ChannelReplyAttachmentReader.RequireSingleLinkCount(count), label);
            Should.Throw<InvalidDataException>(() => ChannelReplyAttachmentReader.ValidateLinuxMetadata(5, 0x8000, count), label);
        }
        ChannelReplyAttachmentReader.RequireSingleLinkCount(1);
        ChannelReplyAttachmentReader.ValidateLinuxMetadata(5, 0x8000, 1);
    }

    private sealed record Arrangement(string Name, string File, string Alias, string[] Roots);

    private sealed class LinkFixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("c1061-links-").FullName;
        internal string Allowed { get; }
        internal string Outside { get; }
        private string Second { get; }
        internal byte[] Bytes { get; } = Encoding.UTF8.GetBytes(Contents);
        internal LinkFixture()
        {
            if (OperatingSystem.IsWindows()) NativeHardLink.RequireNtfs(Root);
            Allowed = Directory.CreateDirectory(Path.Combine(Root, "allowed")).FullName;
            Outside = Directory.CreateDirectory(Path.Combine(Root, "outside")).FullName;
            Second = Directory.CreateDirectory(Path.Combine(Root, "second")).FullName;
        }
        internal IEnumerable<Arrangement> Arrangements()
        {
            yield return new("outside", Path.Combine(Allowed, "one"), Path.Combine(Outside, "one"), [Allowed]);
            yield return new("same root", Path.Combine(Allowed, "two"), Path.Combine(Allowed, "two-alias"), [Allowed]);
            yield return new("two roots", Path.Combine(Allowed, "three"), Path.Combine(Second, "three"), [Allowed, Second]);
        }
        internal void CreateAndObserve(Arrangement arrangement)
        {
            File.WriteAllBytes(arrangement.File, Bytes);
            NativeHardLink.Observe(arrangement.File).Links.ShouldBe(1u);
            NativeHardLink.Create(arrangement.Alias, arrangement.File);
            ObservePair(arrangement.File, arrangement.Alias);
        }
        internal void ObservePair(string file, string alias)
        {
            var first = NativeHardLink.Observe(file);
            var second = NativeHardLink.Observe(alias);
            first.Links.ShouldBe(2u);
            second.ShouldBe(first, "Fixture must observe two names for the same native file identity.");
            File.ReadAllBytes(file).ShouldBe(Bytes);
            File.ReadAllBytes(alias).ShouldBe(Bytes);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
