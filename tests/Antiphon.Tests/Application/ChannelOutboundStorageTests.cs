using System.IO.Compression;
using System.Text;
using Antiphon.Messaging;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0418 V-9 / V-13 (input half): the identity of an outbound intent, and the boundary around
/// what a conversion worker is allowed to be handed.
///
/// <para>Identity is enforced by PostgreSQL, not by a read-then-write in application code, so these
/// tests race two independent services against the real unique index rather than asserting a
/// check that a concurrent caller could walk straight past.</para>
/// </summary>
[Category("Integration")]
public class ChannelOutboundStorageTests
{
    // ---- V-9: one intent per source/destination -----------------------------------------------

    /// <summary>
    /// Two triggers for the same source window, synchronized so that both have decided to insert
    /// before either commits. Exactly one intent may exist afterwards.
    ///
    /// <para>FINDING: the loser does not recover. <c>AdmitDeferredAsync</c> checks for an existing
    /// SourceKey and then calls <c>SaveChangesAsync</c> with no handler for the unique-index
    /// violation it is racing, so the losing caller sees a <c>DbUpdateException</c> instead of the
    /// winner's <c>Deferred</c> result. The database keeps the invariant — one intent, and no
    /// partially admitted second one — but the caller is handed an exception where the design says
    /// it should join the existing intent. That is the assertion below.</para>
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Concurrent_triggers_claim_one_intent_and_one_task(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var reply = ChannelOutboundWorld.MarkdownReply();
        var request = world.Request(reply);

        var atCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var firstDb = world.NewContext();
        var first = world.NewService(firstDb);
        first.TestPauseBeforeCommit = true;
        var hits = 0;
        first.TestBarrier = async (name, _, token) =>
        {
            if (name != "IntentCommit" || Interlocked.Increment(ref hits) != 1)
                return;
            atCommit.SetResult();
            await released.Task.WaitAsync(token);
        };

        var firstSend = first.SendAsync(request, ct);
        await atCommit.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);

        // The second trigger runs to completion while the first is parked on its own commit.
        ChannelOutboundPublishResult secondResult;
        await using (var secondDb = world.NewContext())
            secondResult = await world.NewService(secondDb).SendAsync(request, ct);
        secondResult.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);

        released.SetResult();
        var loser = await Should.ThrowAsync<DbUpdateException>(async () => await firstSend);
        loser.ShouldNotBeNull();

        // The invariant that matters holds: one intent, one destination, nothing sent.
        (await world.DeliveryCountAsync()).ShouldBe(1);
        var delivery = await world.ReadDeliveryAsync(secondResult.DeliveryId!.Value);
        delivery!.SourceKey.ShouldBe(ChannelOutboundService.SourceKey(request));
        world.Producer.MethodEntries.ShouldBe(0);

        // A THIRD trigger after the race joins the surviving intent rather than making another.
        await using (var thirdDb = world.NewContext())
        {
            var third = await world.NewService(thirdDb).SendAsync(request, ct);
            third.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
            third.DeliveryId.ShouldBe(secondResult.DeliveryId);
        }

        (await world.DeliveryCountAsync()).ShouldBe(1);
    }

    /// <summary>
    /// Everything the source key is made of is part of the identity. Changing the prompt sequence,
    /// the text window, the send kind or the destination conversation produces a DIFFERENT, equally
    /// valid intent — one reply per window per destination, never a collapse into one.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Every_component_of_the_source_key_is_part_of_the_identity(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var sessionId = Guid.NewGuid();
        var baseline = world.Request(ChannelOutboundWorld.MarkdownReply(), sessionId: sessionId);

        var variants = new[]
        {
            baseline,
            baseline with { PromptSequence = 99 },
            baseline with { TextWindowStart = 50, TextWindowEnd = 60 },
            baseline with { SendKind = ChannelOutboundSendKind.Trailing },
            baseline with { SendKind = ChannelOutboundSendKind.Machine },
        };

        var ids = new List<Guid>();
        foreach (var variant in variants)
        {
            await using var db = world.NewContext();
            var result = await world.NewService(db).SendAsync(variant, ct);
            result.Status.ShouldBe(ChannelOutboundPublishStatus.Deferred);
            ids.Add(result.DeliveryId!.Value);
        }

        ids.Distinct().Count().ShouldBe(variants.Length);
        (await world.DeliveryCountAsync()).ShouldBe(variants.Length);

        // Re-sending the very first variant joins its own intent, proving the five above are
        // distinct BECAUSE of their components and not because every call makes a new row.
        await using (var db = world.NewContext())
        {
            var repeat = await world.NewService(db).SendAsync(variants[0], ct);
            repeat.DeliveryId.ShouldBe(ids[0]);
        }

        (await world.DeliveryCountAsync()).ShouldBe(variants.Length);
    }

    /// <summary>
    /// The same settled source answered into two different conversations is two independent
    /// outcomes with their own artifacts — and a source task id alone never merges them, because a
    /// task id says where the content came from, not where it is going.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Same_source_to_two_destinations_stays_independent(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync(s =>
        {
            // Y gets its own profile so both destinations are opted in and comparable.
            s.Profiles[ChannelOutboundWorld.AltProfileName] = new Antiphon.Server.Application.Settings.ChannelOutboundProfileSettings
            {
                ProjectId = s.Profiles[ChannelOutboundWorld.ProfileName].ProjectId,
                AgentId = s.Profiles[ChannelOutboundWorld.ProfileName].AgentId,
                PromptFile = "prompts/pdf.md",
            };
        });

        await using (var db = world.NewContext())
        {
            await db.ChatChannels.Where(c => c.Id == world.ChannelY)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.OutboundAgentProfile, ChannelOutboundWorld.AltProfileName), ct);
        }

        var sourceTaskId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var toX = world.Request(
            ChannelOutboundWorld.MarkdownReply(conversationId: "X-conversation"),
            world.ChannelX, sessionId: sessionId, sourceTaskIds: [sourceTaskId]);
        var toY = world.Request(
            ChannelOutboundWorld.MarkdownReply(conversationId: "Y-conversation"),
            world.ChannelY, sessionId: sessionId, sourceTaskIds: [sourceTaskId]);

        Guid xId, yId;
        await using (var db = world.NewContext())
            xId = (await world.NewService(db).SendAsync(toX, ct)).DeliveryId!.Value;
        await using (var db = world.NewContext())
            yId = (await world.NewService(db).SendAsync(toY, ct)).DeliveryId!.Value;

        xId.ShouldNotBe(yId);
        (await world.DeliveryCountAsync()).ShouldBe(2);
        (await world.ReadDeliveryAsync(xId))!.ConversationId.ShouldBe("X-conversation");
        (await world.ReadDeliveryAsync(yId))!.ConversationId.ShouldBe("Y-conversation");
        (await world.ReadDeliveryAsync(xId))!.ProfileName.ShouldBe(ChannelOutboundWorld.ProfileName);
        (await world.ReadDeliveryAsync(yId))!.ProfileName.ShouldBe(ChannelOutboundWorld.AltProfileName);

        // Their staged inputs are separate directories with separate bytes.
        world.Store.InputDirectory(xId).ShouldNotBe(world.Store.InputDirectory(yId));
        File.Exists(Path.Combine(world.Store.InputDirectory(xId), "reply.json")).ShouldBeTrue();
        File.Exists(Path.Combine(world.Store.InputDirectory(yId), "reply.json")).ShouldBeTrue();
        (await File.ReadAllTextAsync(Path.Combine(world.Store.InputDirectory(xId), "reply.json"), ct))
            .ShouldContain("X-conversation");
        (await File.ReadAllTextAsync(Path.Combine(world.Store.InputDirectory(yId), "reply.json"), ct))
            .ShouldContain("Y-conversation");
    }

    /// <summary>
    /// A second pump cannot take a row another owner holds. Once the lease expires it may, and when
    /// it does it must continue the SAME work — the same conversion task, the same sealed output —
    /// not start a second one.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task A_second_pump_waits_for_the_lease_then_resumes_the_same_work(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        ChannelOutboundPublishResult admitted;
        await using (var db = world.NewContext())
            admitted = await world.NewService(db).SendAsync(world.Request(ChannelOutboundWorld.MarkdownReply()), ct);
        var deliveryId = admitted.DeliveryId!.Value;

        var conversionTaskId = Guid.NewGuid();
        await using (var db = world.NewContext())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = conversionTaskId,
                RootTaskId = conversionTaskId,
                Title = "Outbound conversion",
                Goal = "Convert.",
                Kind = AgentTaskKind.Worker,
                Role = AgentTaskRole.Custom,
                ModelLevel = AgentModelLevel.Medium,
                Workspace = WorkspaceMode.Shared,
                WorkingDirectory = world.ConverterWorkspace,
                RepoPath = world.ConverterWorkspace,
                Status = AgentTaskStatus.Succeeded,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            await db.ChannelOutboundDeliveries.Where(d => d.Id == deliveryId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(d => d.State, ChannelOutboundDeliveryState.Converting)
                    .SetProperty(d => d.ConversionTaskId, conversionTaskId), ct);
        }

        await world.WriteWorkerOutputAsync(deliveryId, "unchanged");

        // First pump claims and advances to Ready, and holds the lease.
        await using (var db = world.NewContext())
            (await world.NewService(db).PumpOnceAsync(ct)).ShouldBe(1);
        var afterFirst = await world.ReadDeliveryAsync(deliveryId);
        afterFirst!.State.ShouldBe(ChannelOutboundDeliveryState.Ready);
        var versionAfterFirst = afterFirst.Version;

        // A second pump inside the lease window handles nothing and changes nothing.
        await using (var db = world.NewContext())
            (await world.NewService(db).PumpOnceAsync(ct)).ShouldBe(0);
        (await world.ReadDeliveryAsync(deliveryId))!.Version.ShouldBe(versionAfterFirst);
        world.Producer.MethodEntries.ShouldBe(0);

        // Past the lease, the row is claimable again — and the work continues, it does not restart.
        world.AdvancePastLease();
        await using (var db = world.NewContext())
            (await world.NewService(db).PumpOnceAsync(ct)).ShouldBe(1);

        var published = await world.ReadDeliveryAsync(deliveryId);
        published!.State.ShouldBe(ChannelOutboundDeliveryState.Published);
        published.ConversionTaskId.ShouldBe(conversionTaskId);
        published.Version.ShouldBeGreaterThan(versionAfterFirst);
        world.Producer.AcceptedCount.ShouldBe(1);
        await using var fresh = world.NewContext();
        (await fresh.AgentTasks.CountAsync(t => t.Role == AgentTaskRole.Custom, ct)).ShouldBe(1);
    }

    // ---- V-13: input staging is bounded and authorized ----------------------------------------

    /// <summary>
    /// Zip staging extracts what the MANIFEST lists and nothing else, refuses entries that try to
    /// leave the destination, and stops on the expanded-byte budget rather than on the compressed
    /// size.
    ///
    /// <para>Two defects in <c>UnpackSourceZip</c> were repaired to get here, both found by this
    /// test. It read the extracted file back while its own write handle was still open, so on
    /// Windows every SUCCESSFUL extraction threw <c>IOException</c> — the happy path could not run
    /// at all. And the budget accumulated <c>ZipArchiveEntry.Length</c>, the size the archive
    /// DECLARES about itself, rather than the bytes actually streamed out; an archive that
    /// under-declares is precisely what the limit exists to stop. Both are fixed; this test pins
    /// both.</para>
    ///
    /// <para>Separately, and NOT fixed here: nothing in the server calls this method.
    /// <c>BuildStagedInputAsync</c> stages inline attachment bytes only, so a settlement source zip
    /// triggers conversion without its Markdown members ever being expanded for the worker. Wiring
    /// it up is a Code-stage decision; see the report.</para>
    /// </summary>
    [Test]
    [Arguments("listed-only")]
    [Arguments("traversal-entry")]
    [Arguments("absolute-entry")]
    [Arguments("expanded-budget")]
    [Arguments("forged-manifest-hash")]
    public void Input_staging_is_bounded_and_authorized(string shape)
    {
        var dest = Path.Combine(Path.GetTempPath(), "antiphon-zipstage-" + Guid.NewGuid().ToString("N"));
        var manifest = new SourceBundleManifest();

        byte[] zip;
        switch (shape)
        {
            case "listed-only":
                zip = BuildZip(
                    ("docs/01-requirements.md", "# listed\n"),
                    ("docs/02-design.md", "# listed too\n"),
                    ("docs/secret-notes.md", "# NOT listed\n"),
                    ("build/output.log", "noise\n"));
                manifest.Members =
                [
                    Member("docs/01-requirements.md", "# listed\n"),
                    Member("docs/02-design.md", "# listed too\n"),
                ];
                var listed = OutboundConversionManifestValidator.UnpackSourceZip(zip, manifest, dest, 1 << 20);
                listed.Select(f => f.Path).ShouldBe(["docs/01-requirements.md", "docs/02-design.md"], ignoreOrder: true);
                File.Exists(Path.Combine(dest, "docs", "secret-notes.md")).ShouldBeFalse();
                File.Exists(Path.Combine(dest, "build", "output.log")).ShouldBeFalse();
                // Descriptors carry the bytes that were actually written, hashed here independently.
                foreach (var file in listed)
                {
                    var bytes = File.ReadAllBytes(Path.Combine(dest, file.Path.Replace('/', Path.DirectorySeparatorChar)));
                    file.Length.ShouldBe(bytes.LongLength);
                    file.Sha256.ShouldBe(SourceBundleManifest.Sha256Hex(bytes));
                }

                return;

            case "traversal-entry":
                zip = BuildZip(("../escaped.md", "# escaped\n"));
                manifest.Members = [Member("../escaped.md", "# escaped\n")];
                Should.Throw<InvalidDataException>(() =>
                        OutboundConversionManifestValidator.UnpackSourceZip(zip, manifest, dest, 1 << 20))
                    .Message.ShouldBe("zip-traversal");
                Directory.Exists(dest).ShouldBeTrue();
                Directory.GetFiles(dest, "*", SearchOption.AllDirectories).ShouldBeEmpty();
                return;

            case "absolute-entry":
                zip = BuildZip(("/etc/passwd", "root\n"));
                manifest.Members = [Member("/etc/passwd", "root\n")];
                Should.Throw<InvalidDataException>(() =>
                        OutboundConversionManifestValidator.UnpackSourceZip(zip, manifest, dest, 1 << 20))
                    .Message.ShouldBe("zip-traversal");
                return;

            case "expanded-budget":
                // Compresses to almost nothing; expands to 512 KiB. A check on archive size would
                // wave this through.
                var compressible = new string('a', 512 * 1024);
                zip = BuildZip(("docs/bomb.md", compressible));
                manifest.Members = [Member("docs/bomb.md", compressible)];
                zip.LongLength.ShouldBeLessThan(64 * 1024, "the archive itself is tiny");
                Should.Throw<InvalidDataException>(() =>
                        OutboundConversionManifestValidator.UnpackSourceZip(zip, manifest, dest, 64 * 1024))
                    .Message.ShouldBe("zip-expanded-budget");

                // The same archive under a budget that fits does extract, so the refusal above is
                // the budget and not the shape of the data.
                var roomy = Path.Combine(Path.GetTempPath(), "antiphon-zipstage-" + Guid.NewGuid().ToString("N"));
                OutboundConversionManifestValidator.UnpackSourceZip(zip, manifest, roomy, 1 << 20)
                    .Count.ShouldBe(1);
                return;

            case "forged-manifest-hash":
                zip = BuildZip(("docs/01-requirements.md", "# real content\n"));
                manifest.Members =
                [
                    new SourceBundleMember
                    {
                        RelativePath = "docs/01-requirements.md",
                        StoredName = "01-requirements.md",
                        Kind = SourceBundleMemberKinds.Markdown,
                        Length = 999_999,
                        Sha256 = new string('f', 64),
                    },
                ];
                var staged = OutboundConversionManifestValidator.UnpackSourceZip(zip, manifest, dest, 1 << 20);
                staged.Count.ShouldBe(1);
                // The descriptor handed on is the TRUTH about the staged bytes, not the claim the
                // manifest made about them.
                staged[0].Length.ShouldBe(Encoding.UTF8.GetByteCount("# real content\n"));
                staged[0].Sha256.ShouldBe(SourceBundleManifest.Sha256Hex("# real content\n"u8));
                staged[0].Sha256.ShouldNotBe(new string('f', 64));
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    /// <summary>
    /// An attachment's own bytes are the only bytes staged. A <c>Source</c> naming a URL or an
    /// unrelated local file is provenance: the server does not fetch it, does not read it, and does
    /// not hand the worker anything derived from it.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Explicit_content_is_used_and_source_is_never_fetched(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var sentinelPath = Path.Combine(world.Root, "unrelated-sentinel.md");
        await File.WriteAllTextAsync(sentinelPath, "SENTINEL-MUST-NOT-BE-STAGED\n", ct);

        var reply = new ChannelReply
        {
            Channel = "slack",
            ConversationId = "X-conversation",
            ReplyHandle = "handle-T1",
            Text = "Sources attached.",
            Attachments =
            [
                new OutboundAttachment
                {
                    Kind = AttachmentKind.File,
                    Name = "01-requirements.md",
                    Mime = "text/markdown",
                    Source = "https://example.invalid/not-fetched.md",
                    Content = Encoding.UTF8.GetBytes("# authorized bytes\n"),
                },
                new OutboundAttachment
                {
                    Kind = AttachmentKind.File,
                    Name = "02-design.md",
                    Mime = "text/markdown",
                    Source = sentinelPath,
                    Content = Encoding.UTF8.GetBytes("# also authorized\n"),
                },
                new OutboundAttachment
                {
                    // No authorized bytes at all: nothing may be staged for this one.
                    Kind = AttachmentKind.File,
                    Name = "03-no-bytes.md",
                    Mime = "text/markdown",
                    Source = sentinelPath,
                },
            ],
        };

        ChannelOutboundPublishResult admitted;
        await using (var db = world.NewContext())
            admitted = await world.NewService(db).SendAsync(world.Request(reply), ct);
        var deliveryId = admitted.DeliveryId!.Value;

        var filesDir = Path.Combine(world.Store.InputDirectory(deliveryId), "files");
        var staged = Directory.GetFiles(filesDir).Select(Path.GetFileName).ToList();
        staged.ShouldBe(["01-requirements.md", "02-design.md"], ignoreOrder: true);
        (await File.ReadAllTextAsync(Path.Combine(filesDir, "01-requirements.md"), ct))
            .ShouldBe("# authorized bytes\n");

        foreach (var path in Directory.GetFiles(world.Store.InputDirectory(deliveryId), "*", SearchOption.AllDirectories))
            (await File.ReadAllTextAsync(path, ct)).ShouldNotContain("SENTINEL-MUST-NOT-BE-STAGED");

        // The worker request names only the staged, authorized descriptors.
        var requestJson = await File.ReadAllTextAsync(
            Path.Combine(world.Store.InputDirectory(deliveryId), "request.json"), ct);
        requestJson.ShouldContain("01-requirements.md");
        requestJson.ShouldContain("02-design.md");
        requestJson.ShouldNotContain("03-no-bytes.md");
        requestJson.ShouldNotContain("example.invalid");
    }

    /// <summary>
    /// A path-shaped attachment name cannot place a file outside the delivery's own input directory.
    /// Names are reduced to their file component before staging, and the store refuses anything that
    /// still looks like a path.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Attachment_names_cannot_escape_the_input_directory(CancellationToken ct)
    {
        await using var world = await ChannelOutboundWorld.CreateAsync();
        var reply = ChannelOutboundWorld.MarkdownReply() with
        {
            Attachments =
            [
                new OutboundAttachment
                {
                    Kind = AttachmentKind.File,
                    Name = @"..\..\escaped.md",
                    Mime = "text/markdown",
                    Content = Encoding.UTF8.GetBytes("# escape attempt\n"),
                },
                new OutboundAttachment
                {
                    Kind = AttachmentKind.File,
                    Name = "sub/dir/nested.md",
                    Mime = "text/markdown",
                    Content = Encoding.UTF8.GetBytes("# nested attempt\n"),
                },
            ],
        };

        ChannelOutboundPublishResult admitted;
        await using (var db = world.NewContext())
            admitted = await world.NewService(db).SendAsync(world.Request(reply), ct);
        var deliveryId = admitted.DeliveryId!.Value;

        var inputDir = world.Store.InputDirectory(deliveryId);
        var filesDir = Path.Combine(inputDir, "files");
        Directory.GetFiles(filesDir).Select(Path.GetFileName)
            .ShouldBe(["escaped.md", "nested.md"], ignoreOrder: true);
        File.Exists(Path.Combine(world.StoreRoot, "escaped.md")).ShouldBeFalse();
        File.Exists(Path.Combine(world.Root, "escaped.md")).ShouldBeFalse();
        foreach (var path in Directory.GetFiles(world.StoreRoot, "*", SearchOption.AllDirectories))
            Path.GetFullPath(path).ShouldStartWith(Path.GetFullPath(world.StoreRoot));
    }

    // ---- fixture helpers -----------------------------------------------------------------------

    private static SourceBundleMember Member(string relativePath, string content) => new()
    {
        RelativePath = relativePath,
        StoredName = Path.GetFileName(relativePath),
        Kind = SourceBundleMemberKinds.Markdown,
        Length = Encoding.UTF8.GetByteCount(content),
        Sha256 = SourceBundleManifest.Sha256Hex(Encoding.UTF8.GetBytes(content)),
    };

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        return buffer.ToArray();
    }
}
