using Antiphon.Messaging;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using System.Collections;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0044 slices 1-4: transcript deletion is per-session all-or-nothing, settled queue rows
/// are pruned independently of session liveness, terminal AgentSession rows past 90d are
/// deleted when nothing still names them, AgentTask trees past 180d are deleted whole or
/// not at all, and audit FullContent is archived past <c>AuditSettings.RetentionDays</c>. The
/// sweep is global, so this class is
/// <see cref="NotInParallelAttribute"/> with no group key (serialise against everything, not just
/// itself) and every assertion is scoped to ids this test created.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class DataRetentionServiceTests
{
    [Test]
    [Arguments(24)]
    [Arguments(2)]
    public async Task C768_ConfirmedPayloadExpiresAfterReceiptGrace(int graceHours)
    {
        await using var f = await C768Fixture.CreateAsync();
        var h = f.Harness;
        var chat = await h.BindChannelAsync();
        f.InstallTranscriptReceipt();
        var bytes = new byte[] { 0, 31, 127, 255 };
        var message = new ChannelMessage
        {
            Id = Guid.NewGuid().ToString("N"), Channel = "telegram",
            ChannelMessageId = Guid.NewGuid().ToString("N"),
            Conversation = new Conversation { Id = chat, Kind = ConversationKind.Group, Title = "Family" },
            Author = new Participant { Id = "sender", DisplayName = "sender" },
            Timestamp = DateTimeOffset.UtcNow,
            Text = "attachment proof with distinctive text and recognizable tail",
            ReplyHandle = "original reply handle",
            Attachments = [new Attachment
            {
                Kind = AttachmentKind.File, ChannelRef = "proof-file", Name = "proof.bin",
                Content = bytes,
            }],
            Raw = JsonDocument.Parse("{\"native\":\"metadata\"}").RootElement.Clone(),
        };
        await f.Bridge().HandleInboundAsync(message, CancellationToken.None);
        await f.WaitForAsync(async () =>
        {
            await using var observed = f.Db();
            var row = await observed.ChannelInbounds.AsNoTracking()
                .SingleAsync(i => i.NativeMessageId == message.ChannelMessageId);
            return row.QueueMessageId is Guid ownerId
                && await observed.SessionQueuedMessages.AnyAsync(q => q.Id == ownerId
                    && q.Status == QueuedMessageStatus.Sent)
                && await observed.TranscriptEntries.AnyAsync(t => t.AgentSessionId == h.SessionId
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text != null
                    && t.Text.Contains("recognizable tail"));
        });
        ChannelInbound before;
        SessionQueuedMessage owner;
        await using (var seed = f.Db())
        {
            before = await seed.ChannelInbounds.AsNoTracking()
                .SingleAsync(i => i.NativeMessageId == message.ChannelMessageId);
            owner = await seed.SessionQueuedMessages.AsNoTracking()
                .SingleAsync(q => q.Id == before.QueueMessageId);
            var restored = JsonSerializer.Deserialize<ChannelMessage>(before.EnvelopeJson!,
                Antiphon.Messaging.MessagingJson.Options)!;
            restored.Attachments.Single().Content.ShouldBe(bytes);
            restored.Text.ShouldBe(message.Text);
            restored.ReplyHandle.ShouldBe(message.ReplyHandle);
            restored.Raw.GetProperty("native").GetString().ShouldBe("metadata");
        }
        var receiptAt = f.Clock.GetUtcNow().UtcDateTime.AddHours(-graceHours).AddMilliseconds(1);
        await using (var age = f.Db())
        {
            await age.ChannelInbounds.Where(i => i.Id == before.Id).ExecuteUpdateAsync(u => u
                .SetProperty(i => i.AcceptedAt, receiptAt.AddDays(-3))
                .SetProperty(i => i.TransferredAt, receiptAt.AddDays(-2)));
            await age.SessionQueuedMessages.Where(q => q.Id == owner.Id).ExecuteUpdateAsync(u => u
                .SetProperty(q => q.CreatedAt, receiptAt.AddDays(-2))
                .SetProperty(q => q.SentAt, receiptAt.AddDays(-2)));
            await age.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text == owner.Body)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.CreatedAt, receiptAt)
                    .SetProperty(t => t.Text,
                        graceHours == 2 ? owner.Body.ReplaceLineEndings(" ") : owner.Body));
        }
        var inbox = Path.Combine(h.TempRoot, "workspace", ".antiphon", "inbox");
        var savedFile = Directory.GetFiles(inbox).Single();
        File.ReadAllBytes(savedFile).ShouldBe(bytes);
        var settings = new RetentionSettings
        {
            ChannelInboundPayloadGraceHours = graceHours,
            SessionRetentionDays = 0, TranscriptRetentionDays = 0,
            QueuedMessageRetentionDays = 0, TaskRetentionDays = 0,
        };
        await using var db = f.Db();
        var service = new DataRetentionService(db, Options.Create(settings), Options.Create(new AuditSettings()),
            f.Clock, NullLogger<DataRetentionService>.Instance,
            new AuditService(db, Options.Create(new AuditSettings())));
        await service.RunOnceAsync(CancellationToken.None);
        (await db.ChannelInbounds.AsNoTracking().Where(i => i.Id == before.Id)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBe(before.EnvelopeJson);
        f.Clock.Advance(TimeSpan.FromMilliseconds(1));
        (await service.PruneChannelInboundPayloadsAsync(CancellationToken.None)).ShouldBe(1);
        var retained = await db.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == before.Id);
        retained.EnvelopeJson.ShouldBeNull();
        retained.Id.ShouldBe(before.Id);
        retained.Provider.ShouldBe(before.Provider);
        retained.ConversationId.ShouldBe(before.ConversationId);
        retained.NativeMessageId.ShouldBe(before.NativeMessageId);
        retained.AgentId.ShouldBe(before.AgentId);
        retained.ChatChannelId.ShouldBe(before.ChatChannelId);
        retained.AcceptanceSequence.ShouldBe(before.AcceptanceSequence);
        retained.AcceptedAt.ShouldBe(receiptAt.AddDays(-3));
        retained.TransferredAt.ShouldBe(receiptAt.AddDays(-2));
        retained.QueueMessageId.ShouldBe(before.QueueMessageId);
        retained.WakeTimeoutIncidentAt.ShouldBe(before.WakeTimeoutIncidentAt);
        retained.ContinuityHoldIncidentAt.ShouldBe(before.ContinuityHoldIncidentAt);
        retained.SuspensionHoldIncidentAt.ShouldBe(before.SuspensionHoldIncidentAt);
        retained.LivenessHoldIncidentAt.ShouldBe(before.LivenessHoldIncidentAt);
        await using var restarted = f.Db();
        var restartedService = new DataRetentionService(restarted, Options.Create(settings),
            Options.Create(new AuditSettings()), f.Clock, NullLogger<DataRetentionService>.Instance,
            new AuditService(restarted, Options.Create(new AuditSettings())));
        (await restartedService.PruneChannelInboundPayloadsAsync(CancellationToken.None)).ShouldBe(0);
        await restartedService.RunOnceAsync(CancellationToken.None);
        File.ReadAllBytes(savedFile).ShouldBe(bytes);
    }

    [Test]
    [Arguments("none")]
    [Arguments("queued-user-prompt")]
    [Arguments("assistant")]
    [Arguments("tool-result")]
    [Arguments("clipped-tail")]
    [Arguments("different-tail")]
    [Arguments("wrong-marker")]
    [Arguments("partial-marker")]
    public async Task C768_NonReceiptEvidenceRetainsPayload(string evidence)
    {
        await using var f = await C768Fixture.CreateAsync();
        var guarded = await f.SeedAsync(receipt: false);
        var eligible = await f.SeedAsync();
        if (evidence == "none")
        {
            await using var db = f.Db();
            await db.SessionQueuedMessages.Where(q => q.Id == guarded.OwnerId)
                .ExecuteUpdateAsync(u => u.SetProperty(q => q.DeliveryVerdict, DeliveryVerdict.Delivered));
        }
        else
        {
            var kind = evidence switch
            {
                "queued-user-prompt" => TranscriptKinds.QueuedUserPrompt,
                "assistant" => TranscriptKinds.AssistantText,
                "tool-result" => TranscriptKinds.ToolResult,
                _ => TranscriptKinds.UserPrompt,
            };
            var text = evidence switch
            {
                "clipped-tail" => guarded.Body[..^12],
                "different-tail" => guarded.Body.Replace("distinctive tail", "different tail"),
                "wrong-marker" => guarded.Body.Replace(guarded.OwnerId.ToString("N"), Guid.NewGuid().ToString("N")),
                "partial-marker" => guarded.Body.Replace("[antiphon-channel:", "[antiphon-chann"),
                _ => guarded.Body,
            };
            await f.AddReceiptAsync(text, kind);
        }
        (await f.PruneAsync()).ShouldBe(1);
        await using var verify = f.Db();
        (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == guarded.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBe(guarded.Json);
        (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == eligible.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBeNull();
    }

    [Test]
    [Arguments("unowned")]
    [Arguments("pending")]
    [Arguments("parked")]
    [Arguments("canceled")]
    [Arguments("non-channel")]
    public async Task C768_UnownedOrIneligibleQueueRetainsPayload(string state)
    {
        await using var f = await C768Fixture.CreateAsync();
        var guarded = await f.SeedAsync(
            mapped: state != "unowned",
            status: state == "canceled" ? QueuedMessageStatus.Canceled
                : state is "pending" or "parked" ? QueuedMessageStatus.Pending : QueuedMessageStatus.Sent,
            origin: state == "non-channel" ? QueuedMessageOrigin.Ui : QueuedMessageOrigin.Channel);
        if (state == "parked")
        {
            await using var db = f.Db();
            await db.SessionQueuedMessages.Where(q => q.Id == guarded.OwnerId)
                .ExecuteUpdateAsync(u => u.SetProperty(q => q.DeliveryAttempts, 3));
        }
        var eligible = await f.SeedAsync();
        (await f.PruneAsync()).ShouldBe(1);
        await using var verify = f.Db();
        (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == guarded.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBe(guarded.Json);
        (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == eligible.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBeNull();
        if (state != "unowned")
            (await verify.SessionQueuedMessages.AsNoTracking().Where(q => q.Id == guarded.OwnerId)
                .Select(q => q.Status).SingleAsync())
                .ShouldBe(state == "canceled" ? QueuedMessageStatus.Canceled
                    : state is "pending" or "parked" ? QueuedMessageStatus.Pending : QueuedMessageStatus.Sent);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public async Task C768_DisabledPayloadRetentionKeepsEnvelopeAndOtherPassesRun(int graceHours)
    {
        await using var f = await C768Fixture.CreateAsync();
        var guarded = await f.SeedAsync();
        var unrelatedId = Guid.NewGuid();
        await using (var seed = f.Db())
        {
            seed.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = unrelatedId, AgentSessionId = f.Harness.SessionId,
                Body = "unrelated settled queue row", Origin = QueuedMessageOrigin.Ui,
                Status = QueuedMessageStatus.Sent, CreatedAt = f.Clock.GetUtcNow().UtcDateTime.AddDays(-3),
                SentAt = f.Clock.GetUtcNow().UtcDateTime.AddDays(-3),
            });
            await seed.SaveChangesAsync();
        }
        var settings = new RetentionSettings
        {
            ChannelInboundPayloadGraceHours = graceHours, SessionRetentionDays = 0,
            TranscriptRetentionDays = 0, QueuedMessageRetentionDays = 1, TaskRetentionDays = 0,
        };
        await using var db = f.Db();
        var audit = new AuditSettings { RetentionDays = 0 };
        var service = new DataRetentionService(db, Options.Create(settings), Options.Create(audit),
            f.Clock, NullLogger<DataRetentionService>.Instance, new AuditService(db, Options.Create(audit)));
        await service.RunOnceAsync(CancellationToken.None);
        await using var verify = f.Db();
        (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == guarded.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBe(guarded.Json);
        (await verify.SessionQueuedMessages.AnyAsync(q => q.Id == guarded.OwnerId)).ShouldBeTrue();
        (await verify.SessionQueuedMessages.AnyAsync(q => q.Id == unrelatedId)).ShouldBeFalse();
    }

    [Test]
    [Arguments("wrong-session")]
    [Arguments("sequence-equal")]
    [Arguments("sequence-before")]
    [Arguments("native-null")]
    [Arguments("native-before-floor")]
    public async Task C768_ReceiptMustBelongToRecipientAndOriginalAttempt(string condition)
    {
        await using var f = await C768Fixture.CreateAsync();
        var noSequence = condition.StartsWith("native", StringComparison.Ordinal);
        var guarded = await f.SeedAsync(receipt: false,
            baseline: noSequence ? null : condition == "sequence-before" ? 2 : condition == "sequence-equal" ? 1 : 0);
        if (condition == "wrong-session")
        {
            var otherSession = await f.AddOtherSessionAsync();
            await f.AddReceiptAsync(guarded.Body, sessionId: otherSession);
        }
        else if (noSequence)
        {
            await using var read = f.Db();
            var started = (await read.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == guarded.OwnerId))
                .LastDeliveryStartedAt!.Value;
            await f.AddReceiptAsync(guarded.Body,
                nativeAt: condition == "native-null" ? null : started.AddSeconds(-30).AddMilliseconds(-1),
                nullNative: condition == "native-null");
        }
        else
            await f.AddReceiptAsync(guarded.Body);
        (await f.PruneAsync()).ShouldBe(0);
        await using (var read = f.Db())
            (await read.ChannelInbounds.AsNoTracking().Where(i => i.Id == guarded.InboundId)
                .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBe(guarded.Json);

        if (condition == "sequence-before")
            await f.AddReceiptAsync("unrelated assistant", TranscriptKinds.AssistantText);
        if (noSequence)
        {
            await using var read = f.Db();
            var started = (await read.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == guarded.OwnerId))
                .LastDeliveryStartedAt!.Value;
            await f.AddReceiptAsync(guarded.Body, nativeAt: started.AddSeconds(-30));
        }
        else
            await f.AddReceiptAsync(guarded.Body);
        (await f.PruneAsync()).ShouldBe(1);
        await using var verify = f.Db();
        (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == guarded.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBeNull();
    }

    [Test]
    [Arguments("uncertain")]
    [Arguments("screen-only")]
    public async Task C768_LateCompleteReceiptStartsItsOwnRetentionGrace(string verdict)
    {
        await using var f = await C768Fixture.CreateAsync();
        var retained = await f.SeedAsync(receipt: false,
            status: verdict == "uncertain" ? QueuedMessageStatus.Pending : QueuedMessageStatus.Sent);
        await using (var set = f.Db())
            await set.SessionQueuedMessages.Where(q => q.Id == retained.OwnerId)
                .ExecuteUpdateAsync(u => u.SetProperty(q => q.DeliveryVerdict,
                    verdict == "uncertain" ? DeliveryVerdict.NoTranscriptRecord : DeliveryVerdict.Delivered));
        (await f.PruneAsync()).ShouldBe(0);
        var receiptAt = f.Clock.GetUtcNow().UtcDateTime.AddHours(-24).AddMilliseconds(1);
        await f.AddReceiptAsync(retained.Body, createdAt: receiptAt);
        await f.AddReceiptAsync(string.Empty, TranscriptKinds.TurnEnd, createdAt: receiptAt);
        var sentBefore = f.Harness.Adapter.SubmittedBodies.Count;
        if (verdict == "uncertain")
        {
            await f.Harness.Queue.OnTurnEndAsync(f.Harness.SessionId, CancellationToken.None);
            await using var check = f.Db();
            var owner = await check.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == retained.OwnerId);
            owner.Status.ShouldBe(QueuedMessageStatus.Sent);
            owner.DeliveryVerdict.ShouldBe(DeliveryVerdict.LateConfirmed);
        }
        (await f.PruneAsync()).ShouldBe(0);
        await using (var before = f.Db())
            (await before.ChannelInbounds.AsNoTracking().Where(i => i.Id == retained.InboundId)
                .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBe(retained.Json);
        f.Clock.Advance(TimeSpan.FromMilliseconds(1));
        (await f.PruneAsync()).ShouldBe(1);
        f.Harness.Adapter.SubmittedBodies.Count.ShouldBe(sentBefore);
    }

    [Test]
    [Arguments("missing-owner")]
    [Arguments("missing-transcript")]
    public async Task C768_UnprovableHistoryRetainsBytesAndReportsOnlyCounts(string missing)
    {
        await using var f = await C768Fixture.CreateAsync();
        var orphan = await f.SeedAsync(receipt: false);
        if (missing == "missing-owner")
        {
            await using var db = f.Db();
            await db.SessionQueuedMessages.Where(q => q.Id == orphan.OwnerId).ExecuteDeleteAsync();
        }
        var eligible = await f.SeedAsync();
        var logs = new C768CaptureLogger();
        (await f.PruneAsync(logger: logs)).ShouldBe(1);
        (await f.PruneAsync()).ShouldBe(0);
        await using var verify = f.Db();
        var remaining = await verify.ChannelInbounds.AsNoTracking().SingleAsync(i => i.Id == orphan.InboundId);
        remaining.EnvelopeJson.ShouldBe(orphan.Json);
        remaining.QueueMessageId.ShouldBe(orphan.OwnerId);
        (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == eligible.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBeNull();
        logs.Messages.ShouldContain(message => message.Contains(missing == "missing-owner"
            ? "1 mapped envelope(s) lacked an owner" : "1 lacked a complete recipient UserPrompt"));
        logs.Messages.ShouldNotContain(message => message.Contains(orphan.InboundId.ToString("N"), StringComparison.Ordinal)
            || message.Contains(orphan.Json, StringComparison.Ordinal));
    }

    [Test]
    [Arguments("inline")]
    [Arguments("spill")]
    public async Task C768_CompleteBatchReceiptPurgesEveryMappedMember(string transport)
    {
        await using var f = await C768Fixture.CreateAsync(debounceMs: 100_000);
        var h = f.Harness;
        var chat = await h.BindChannelAsync();
        f.InstallTranscriptReceipt();
        var nativeIds = new List<string>();
        var bytesByNative = new Dictionary<string, byte[]>();
        var bridge = f.Bridge();
        for (var n = 0; n < 3; n++)
        {
            var native = Guid.NewGuid().ToString("N");
            nativeIds.Add(native);
            var bytes = new byte[] { (byte)n, 31, 127, 255 };
            bytesByNative[native] = bytes;
            var message = new ChannelMessage
            {
                Id = Guid.NewGuid().ToString("N"), Channel = "telegram", ChannelMessageId = native,
                Conversation = new Conversation { Id = chat, Kind = ConversationKind.Group, Title = "Family" },
                Author = new Participant { Id = "batch-sender", DisplayName = "batch-sender" },
                Timestamp = DateTimeOffset.UtcNow,
                Text = $"member {n} complete text "
                    + (transport == "spill" ? new string('y', 3_000) : "short")
                    + $" DISTINCT TAIL {n}",
                ReplyHandle = "batch-reply-handle",
                Raw = JsonDocument.Parse("{\"batch\":true}").RootElement.Clone(),
                Attachments = [new Attachment
                {
                    Kind = AttachmentKind.File, ChannelRef = $"batch-file-{n}",
                    Name = $"batch-{n}.bin", Content = bytes,
                }],
            };
            await bridge.HandleInboundAsync(message, CancellationToken.None);
        }
        await h.Provider.GetRequiredService<ChannelInboundDebouncer>().FlushAllAsync();
        await f.WaitForAsync(async () =>
        {
            await using var db = f.Db();
            var mappings = await db.ChannelInbounds.AsNoTracking()
                .Where(i => nativeIds.Contains(i.NativeMessageId) && i.QueueMessageId != null)
                .Select(i => i.QueueMessageId).ToListAsync();
            return mappings.Count == 3 && mappings.Distinct().Count() == 1
                && await db.SessionQueuedMessages.AnyAsync(q => q.Id == mappings[0]
                    && q.Status == QueuedMessageStatus.Sent)
                && await db.TranscriptEntries.AnyAsync(t => t.AgentSessionId == h.SessionId
                    && t.Kind == TranscriptKinds.UserPrompt);
        });
        Guid ownerId;
        Guid memberToRemap;
        string memberEnvelope;
        string body;
        string? spillPath = null;
        await using (var db = f.Db())
        {
            var members = await db.ChannelInbounds.AsNoTracking()
                .Where(i => nativeIds.Contains(i.NativeMessageId)).ToListAsync();
            members.Count.ShouldBe(3);
            ownerId = members[0].QueueMessageId!.Value;
            memberToRemap = members[^1].Id;
            memberEnvelope = members[^1].EnvelopeJson!;
            members.ShouldAllBe(i => i.QueueMessageId == ownerId);
            foreach (var member in members)
            {
                var restored = JsonSerializer.Deserialize<ChannelMessage>(member.EnvelopeJson!,
                    Antiphon.Messaging.MessagingJson.Options)!;
                restored.Attachments.Single().Content.ShouldBe(bytesByNative[member.NativeMessageId]);
            }
            var owner = await db.SessionQueuedMessages.AsNoTracking().SingleAsync(q => q.Id == ownerId);
            owner.SourceChannelInboundId.ShouldNotBeNull();
            body = owner.Body;
            if (transport == "spill")
            {
                ChannelPromptCorrelation.IsSpillPointer(body).ShouldBeTrue();
                spillPath = TypedBodySpill.InboxAbsolutePath(Path.Combine(h.TempRoot, "workspace"),
                    ownerId.ToString("D"));
                File.Exists(spillPath).ShouldBeTrue();
            }
            else
                foreach (var n in Enumerable.Range(0, 3)) body.ShouldContain($"DISTINCT TAIL {n}");
            var receiptAt = f.Clock.GetUtcNow().UtcDateTime.AddHours(-24).AddMilliseconds(1);
            await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text == body)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.CreatedAt, receiptAt));
        }
        (await f.PruneAsync()).ShouldBe(0);
        f.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var clippedBody = body[..^13];
        await using (var clip = f.Db())
            await clip.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text == body)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.Text, clippedBody));
        (await f.PruneAsync()).ShouldBe(0);
        await using (var restore = f.Db())
            await restore.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId
                    && t.Kind == TranscriptKinds.UserPrompt && t.Text == clippedBody)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.Text, body));
        var otherOwner = Guid.NewGuid();
        await using (var remap = f.Db())
        {
            remap.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = otherOwner, AgentSessionId = h.SessionId,
                Body = ChannelPromptCorrelation.Mark(otherOwner, "unconfirmed alternate owner tail"),
                Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Sent,
                DeliveryAttempts = 1, LastDeliveryBaselineSequence = 0,
                CreatedAt = f.Clock.GetUtcNow().UtcDateTime.AddDays(-2),
                SentAt = f.Clock.GetUtcNow().UtcDateTime.AddDays(-2),
            });
            await remap.SaveChangesAsync();
            await remap.ChannelInbounds.Where(i => i.Id == memberToRemap)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.QueueMessageId, otherOwner));
        }
        (await f.PruneAsync()).ShouldBe(2);
        await using (var remapped = f.Db())
            (await remapped.ChannelInbounds.AsNoTracking().Where(i => i.Id == memberToRemap)
                .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBe(memberEnvelope);
        await using (var restoreMapping = f.Db())
            await restoreMapping.ChannelInbounds.Where(i => i.Id == memberToRemap)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.QueueMessageId, ownerId));
        (await f.PruneAsync()).ShouldBe(1);
        await using var verify = f.Db();
        (await verify.ChannelInbounds.AsNoTracking().CountAsync(i => nativeIds.Contains(i.NativeMessageId)
            && i.EnvelopeJson == null && i.QueueMessageId == ownerId)).ShouldBe(3);
        (await verify.SessionQueuedMessages.AsNoTracking().CountAsync(q => q.Id == ownerId)).ShouldBe(1);
        if (spillPath is not null) File.Exists(spillPath).ShouldBeTrue();
    }

    [Test]
    [Arguments("session")]
    [Arguments("transcript")]
    [Arguments("queue")]
    public async Task C768_PayloadProofSurvivesOrdinaryRetention(string pass)
    {
        await using var f = await C768Fixture.CreateAsync();
        var confirmedSession = await f.AddOtherSessionAsync();
        var insideSession = await f.AddOtherSessionAsync();
        var unconfirmedSession = await f.AddOtherSessionAsync();
        var unrelatedSession = await f.AddOtherSessionAsync();
        var confirmed = await f.SeedAsync(ownerSession: confirmedSession);
        var inside = await f.SeedAsync(receipt: false, ownerSession: insideSession);
        await f.AddReceiptAsync(inside.Body, sessionId: insideSession,
            createdAt: f.Clock.GetUtcNow().UtcDateTime.AddHours(-36));
        var unconfirmed = await f.SeedAsync(receipt: false, ownerSession: unconfirmedSession);
        await f.AddReceiptAsync("unrelated incomplete history", sessionId: unconfirmedSession);
        await f.AddReceiptAsync("unrelated ordinary transcript", sessionId: unrelatedSession);
        var unrelatedQueue = Guid.NewGuid();
        await using (var seed = f.Db())
        {
            seed.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = unrelatedQueue, AgentSessionId = unrelatedSession,
                Body = "unrelated ordinary queue", Origin = QueuedMessageOrigin.Ui,
                Status = QueuedMessageStatus.Sent,
                CreatedAt = f.Clock.GetUtcNow().UtcDateTime.AddDays(-3),
                SentAt = f.Clock.GetUtcNow().UtcDateTime.AddDays(-3),
            });
            await seed.SaveChangesAsync();
        }
        var settings = new RetentionSettings
        {
            ChannelInboundPayloadGraceHours = 48,
            SessionRetentionDays = pass == "session" ? 1 : 0,
            TranscriptRetentionDays = pass == "transcript" ? 1 : 0,
            QueuedMessageRetentionDays = pass == "queue" ? 1 : 0,
            TaskRetentionDays = 0,
        };
        await using var db = f.Db();
        var audit = new AuditSettings { RetentionDays = 0 };
        var service = new DataRetentionService(db, Options.Create(settings), Options.Create(audit),
            f.Clock, NullLogger<DataRetentionService>.Instance, new AuditService(db, Options.Create(audit)));
        if (pass == "session") await service.PruneSessionsAsync(CancellationToken.None);
        if (pass == "transcript") await service.PruneTranscriptsAsync(CancellationToken.None);
        if (pass == "queue") await service.PruneQueuedMessagesAsync(CancellationToken.None);
        await using (var protectedDb = f.Db())
        {
            (await protectedDb.SessionQueuedMessages.AnyAsync(q => q.Id == inside.OwnerId)).ShouldBeTrue();
            (await protectedDb.SessionQueuedMessages.AnyAsync(q => q.Id == unconfirmed.OwnerId)).ShouldBeTrue();
            if (pass == "transcript")
                (await protectedDb.TranscriptEntries.AnyAsync(t => t.AgentSessionId == insideSession)).ShouldBeTrue();
        }
        await service.RunOnceAsync(CancellationToken.None);
        await using var verify = f.Db();
        (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == confirmed.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBeNull();
        (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == inside.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBe(inside.Json);
        (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == unconfirmed.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBe(unconfirmed.Json);
        (await verify.SessionQueuedMessages.AnyAsync(q => q.Id == inside.OwnerId)).ShouldBeTrue();
        (await verify.SessionQueuedMessages.AnyAsync(q => q.Id == unconfirmed.OwnerId)).ShouldBeTrue();
        if (pass == "session")
        {
            (await verify.AgentSessions.AnyAsync(s => s.Id == confirmedSession)).ShouldBeFalse();
            (await verify.AgentSessions.AnyAsync(s => s.Id == unrelatedSession)).ShouldBeFalse();
            (await verify.AgentSessions.AnyAsync(s => s.Id == insideSession)).ShouldBeTrue();
        }
        if (pass == "transcript")
        {
            (await verify.TranscriptEntries.AnyAsync(t => t.AgentSessionId == confirmedSession)).ShouldBeFalse();
            (await verify.TranscriptEntries.AnyAsync(t => t.AgentSessionId == insideSession)).ShouldBeTrue();
            (await verify.TranscriptEntries.AnyAsync(t => t.AgentSessionId == unrelatedSession)).ShouldBeFalse();
        }
        if (pass == "queue")
        {
            (await verify.SessionQueuedMessages.AnyAsync(q => q.Id == confirmed.OwnerId)).ShouldBeFalse();
            (await verify.SessionQueuedMessages.AnyAsync(q => q.Id == unrelatedQueue)).ShouldBeFalse();
        }
        f.Clock.Advance(TimeSpan.FromHours(12));
        await using var laterDb = f.Db();
        var laterService = new DataRetentionService(laterDb, Options.Create(settings), Options.Create(audit),
            f.Clock, NullLogger<DataRetentionService>.Instance, new AuditService(laterDb, Options.Create(audit)));
        await laterService.RunOnceAsync(CancellationToken.None);
        await using var later = f.Db();
        (await later.ChannelInbounds.AsNoTracking().Where(i => i.Id == inside.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBeNull();
        (await later.ChannelInbounds.AsNoTracking().Where(i => i.Id == unconfirmed.InboundId)
            .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBe(unconfirmed.Json);
        if (pass == "session") (await later.AgentSessions.AnyAsync(s => s.Id == insideSession)).ShouldBeFalse();
        if (pass == "transcript") (await later.TranscriptEntries.AnyAsync(t => t.AgentSessionId == insideSession)).ShouldBeFalse();
        if (pass == "queue") (await later.SessionQueuedMessages.AnyAsync(q => q.Id == inside.OwnerId)).ShouldBeFalse();
    }

    [Test]
    public async Task C768_PayloadScanIsBoundedAndDoesNotStarveLaterCandidates()
    {
        await using var f = await C768Fixture.CreateAsync();
        var pending = new List<Guid>();
        var eligible = new List<Guid>();
        var old = f.Clock.GetUtcNow().UtcDateTime.AddDays(-3);
        await using (var seed = f.Db())
        {
            for (var n = 0; n < 129; n++)
            {
                var id = Guid.NewGuid();
                var ownerId = Guid.NewGuid();
                var canPurge = n >= 64;
                (canPurge ? eligible : pending).Add(id);
                seed.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = id, Provider = "telegram", ConversationId = id.ToString("N"),
                    NativeMessageId = id.ToString("N"), AgentId = f.Harness.AgentId,
                    EnvelopeJson = canPurge ? $"{{\"attachment\":\"{new string('A', 8192)}\"}}" : "{malformed legacy payload",
                    QueueMessageId = canPurge ? ownerId : null, AcceptedAt = old,
                });
                if (!canPurge) continue;
                var body = ChannelPromptCorrelation.Mark(ownerId, $"complete body {id:N} with distinctive tail");
                seed.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = ownerId, SourceChannelInboundId = id, AgentSessionId = f.Harness.SessionId,
                    Body = body, Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Sent,
                    DeliveryAttempts = 1, LastDeliveryBaselineSequence = 0,
                    CreatedAt = old, SentAt = old.AddHours(1),
                });
                seed.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = f.Harness.SessionId,
                    Sequence = n - 63, Kind = TranscriptKinds.UserPrompt,
                    Text = body, Timestamp = old.AddHours(1), CreatedAt = old.AddHours(1),
                });
            }
            await seed.SaveChangesAsync();
        }
        var probe = new C768ScanReaderProbe();
        (await f.PruneAsync(interceptor: probe)).ShouldBe(65);
        probe.CandidatePages.Count.ShouldBeGreaterThanOrEqualTo(3);
        probe.CandidatePages.ShouldAllBe(page => page.Rows <= 64
            && !page.Fields.Contains(nameof(ChannelInbound.EnvelopeJson)));
        probe.ProofPages.ShouldAllBe(page => page.Rows <= 64);
        (await f.PruneAsync()).ShouldBe(0);
        await using var verify = f.Db();
        (await verify.ChannelInbounds.AsNoTracking().CountAsync(i => eligible.Contains(i.Id)
            && i.EnvelopeJson == null)).ShouldBe(65);
        (await verify.ChannelInbounds.AsNoTracking().CountAsync(i => pending.Contains(i.Id)
            && i.EnvelopeJson != null)).ShouldBe(64);
    }

    [Test]
    [Arguments("same-mapping")]
    [Arguments("mapping-changed")]
    public async Task C768_PayloadPurgeIsConditionalAndAtMostOnce(string race)
    {
        await using var f = await C768Fixture.CreateAsync();
        var retained = await f.SeedAsync();
        var pause = new C768UpdatePause();
        var first = f.PruneAsync(interceptor: pause);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = 0;
        Guid? newOwnerId = null;
        if (race == "same-mapping")
            second = await f.PruneAsync();
        else
        {
            newOwnerId = Guid.NewGuid();
            await using var remap = f.Db();
            remap.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = newOwnerId.Value, AgentSessionId = f.Harness.SessionId,
                Body = ChannelPromptCorrelation.Mark(newOwnerId.Value, "new unconfirmed owner complete tail"),
                Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Sent,
                DeliveryAttempts = 1, LastDeliveryBaselineSequence = 0,
                CreatedAt = f.Clock.GetUtcNow().UtcDateTime.AddDays(-3),
                SentAt = f.Clock.GetUtcNow().UtcDateTime.AddDays(-3),
            });
            await remap.SaveChangesAsync();
            await remap.ChannelInbounds.Where(i => i.Id == retained.InboundId)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.QueueMessageId, newOwnerId));
        }
        pause.Release.TrySetResult(true);
        var firstCount = await first;
        (firstCount + second).ShouldBe(race == "same-mapping" ? 1 : 0);
        (await f.PruneAsync()).ShouldBe(0);
        await using (var verify = f.Db())
        {
            (await verify.ChannelInbounds.AsNoTracking().Where(i => i.Id == retained.InboundId)
                .Select(i => i.EnvelopeJson).SingleAsync())
                .ShouldBe(race == "same-mapping" ? null : retained.Json);
        }
        if (newOwnerId is Guid id)
        {
            await using (var read = f.Db())
            {
                var body = await read.SessionQueuedMessages.Where(q => q.Id == id).Select(q => q.Body).SingleAsync();
                await f.AddReceiptAsync(body);
            }
            (await f.PruneAsync()).ShouldBe(1);
        }
    }
    [Test]
    public async Task A_running_sessions_rows_survive_at_any_age()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(40);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Running, stale);
            var oldId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            var olderId = await SeedTranscriptAsync(sessionId, 2, TranscriptKinds.TurnEnd, stale.AddDays(-5));

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(oldId)).ShouldBeTrue();
            (await ExistsAsync(olderId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_terminal_session_whose_newest_row_is_recent_keeps_every_row()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(1));
            var ancientId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, DaysAgo(90));
            var oldId = await SeedTranscriptAsync(sessionId, 2, TranscriptKinds.AssistantText, DaysAgo(40));
            var recentId = await SeedTranscriptAsync(sessionId, 3, TranscriptKinds.TurnEnd, DaysAgo(1));

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(ancientId)).ShouldBeTrue("a partial trim of old rows is forbidden");
            (await ExistsAsync(oldId)).ShouldBeTrue("a partial trim of old rows is forbidden");
            (await ExistsAsync(recentId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_terminal_session_with_recent_LastSeenAt_keeps_every_row_even_when_all_transcripts_are_stale()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, lastSeenAt: DaysAgo(1));
            var a = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, DaysAgo(40));
            var b = await SeedTranscriptAsync(sessionId, 2, TranscriptKinds.TurnEnd, DaysAgo(40));

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(a)).ShouldBeTrue();
            (await ExistsAsync(b)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_fully_stale_terminal_session_loses_all_transcript_rows_and_reads_idle()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(40);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Failed, stale);
            // Activity without an end marker: before the prune this session reads working.
            var activityId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            var moreId = await SeedTranscriptAsync(sessionId, 2, TranscriptKinds.AssistantText, stale.AddHours(1));

            await using (var before = CreateContext())
            {
                (await SessionMessageQueueService.IsWorkingAsync(before, sessionId, CancellationToken.None))
                    .ShouldBeTrue("fixture: leftover activity with no end marker is working");
            }

            await using var db = CreateContext();
            var removed = await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);
            removed.ShouldBeGreaterThanOrEqualTo(2);

            (await ExistsAsync(activityId)).ShouldBeFalse();
            (await ExistsAsync(moreId)).ShouldBeFalse();

            await using var after = CreateContext();
            (await after.TranscriptEntries.CountAsync(t => t.AgentSessionId == sessionId)).ShouldBe(0);
            (await SessionMessageQueueService.IsWorkingAsync(after, sessionId, CancellationToken.None))
                .ShouldBeFalse("zero rows is idle — the launch invariant");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Default_transcript_window_prunes_after_seven_days_but_keeps_six_day_history()
    {
        var marker = NewMarker();
        try
        {
            var staleAt = DaysAgo(8);
            var recentAt = DaysAgo(6);
            var staleSession = await SeedSessionAsync(marker, SessionStatus.Stopped, staleAt);
            var recentSession = await SeedSessionAsync(marker, SessionStatus.Stopped, recentAt);
            var staleTranscript = await SeedTranscriptAsync(staleSession, 1, TranscriptKinds.UserPrompt, staleAt);
            var recentTranscript = await SeedTranscriptAsync(recentSession, 1, TranscriptKinds.UserPrompt, recentAt);

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(staleTranscript)).ShouldBeFalse();
            (await ExistsAsync(recentTranscript)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_stale_stopped_session_that_is_an_agents_PersistentSessionId_is_excluded()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(40);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, stale);
            var rowId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            await SeedAgentPointingAtAsync(marker, sessionId);

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(rowId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Queue_keeps_Pending_and_unsettled_channel_rows_and_deletes_settled_old_ones()
    {
        var marker = NewMarker();
        try
        {
            // Queue prune is independent of session liveness — a live always-on session is the
            // interesting case (this is what keeps its queue bounded).
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Running, DateTime.UtcNow);
            var pendingOld = await SeedQueuedAsync(
                sessionId, 1, QueuedMessageStatus.Pending, QueuedMessageOrigin.Ui, DaysAgo(40));
            var sentOld = await SeedQueuedAsync(
                sessionId, 2, QueuedMessageStatus.Sent, QueuedMessageOrigin.Ui, DaysAgo(40));
            var sentRecent = await SeedQueuedAsync(
                sessionId, 3, QueuedMessageStatus.Sent, QueuedMessageOrigin.Ui, DaysAgo(1));
            var canceledOld = await SeedQueuedAsync(
                sessionId, 4, QueuedMessageStatus.Canceled, QueuedMessageOrigin.System, DaysAgo(40));
            var channelUnsettled = await SeedQueuedAsync(
                sessionId, 5, QueuedMessageStatus.Sent, QueuedMessageOrigin.Channel, DaysAgo(40),
                settledAt: null);
            var channelSettled = await SeedQueuedAsync(
                sessionId, 6, QueuedMessageStatus.Sent, QueuedMessageOrigin.Channel, DaysAgo(40),
                settledAt: DaysAgo(39));
            var protectedInbound = Guid.NewGuid();
            var protectedOwner = Guid.NewGuid();
            var protectedBody = ChannelPromptCorrelation.Mark(protectedOwner,
                "separate stale settled Channel owner with a complete receipt tail");
            await using (var seed = CreateContext())
            {
                seed.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = protectedInbound, Provider = "telegram", ConversationId = marker,
                    NativeMessageId = protectedInbound.ToString("N"),
                    QueueMessageId = protectedOwner, EnvelopeJson = "{\"attachment\":\"AAEf/251\"}",
                    AcceptedAt = DaysAgo(40),
                });
                seed.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = protectedOwner, SourceChannelInboundId = protectedInbound,
                    AgentSessionId = sessionId, Body = protectedBody,
                    Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Sent,
                    DeliveryAttempts = 1, LastDeliveryBaselineSequence = 0,
                    CreatedAt = DaysAgo(40), SentAt = DaysAgo(40),
                    ChannelReplySettledAt = DaysAgo(39),
                });
                seed.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 1,
                    Kind = TranscriptKinds.UserPrompt, Text = protectedBody,
                    Timestamp = DaysAgo(39), CreatedAt = DaysAgo(39),
                });
                await seed.SaveChangesAsync();
            }

            await using var db = CreateContext();
            await CreateService(db).PruneQueuedMessagesAsync(CancellationToken.None);

            (await QueueExistsAsync(pendingOld)).ShouldBeTrue("Pending survives at any age");
            (await QueueExistsAsync(sentOld)).ShouldBeFalse();
            (await QueueExistsAsync(sentRecent)).ShouldBeTrue();
            (await QueueExistsAsync(canceledOld)).ShouldBeFalse();
            (await QueueExistsAsync(channelUnsettled)).ShouldBeTrue("owed channel reply is never deleted");
            (await QueueExistsAsync(channelSettled)).ShouldBeFalse();
            (await QueueExistsAsync(protectedOwner)).ShouldBeTrue("an unpurged journal payload protects its owner");
            (await CreateService(db).PruneChannelInboundPayloadsAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            (await CreateService(db).PruneQueuedMessagesAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            (await QueueExistsAsync(protectedOwner)).ShouldBeFalse();
            (await db.ChannelInbounds.AsNoTracking().Where(i => i.Id == protectedInbound)
                .Select(i => i.EnvelopeJson).SingleAsync()).ShouldBeNull();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_swept_parked_row_is_pruned_after_the_queued_message_retention_window()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Failed, DaysAgo(40));
            var swept = await SeedQueuedAsync(
                sessionId, 1, QueuedMessageStatus.Canceled, QueuedMessageOrigin.Delegation, DaysAgo(40),
                deliveryAttempts: 3);

            await using var db = CreateContext();
            (await CreateService(db).PruneQueuedMessagesAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);

            (await QueueExistsAsync(swept)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_zero_transcript_window_skips_transcripts_and_still_prunes_queued_messages()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(40);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, stale);
            var transcriptId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            var sentOld = await SeedQueuedAsync(
                sessionId, 1, QueuedMessageStatus.Sent, QueuedMessageOrigin.Ui, stale);

            await using var db = CreateContext();
            var result = await CreateService(db, new RetentionSettings
            {
                TranscriptRetentionDays = 0,
                QueuedMessageRetentionDays = 30,
            }).RunOnceAsync(CancellationToken.None);

            result.Transcripts.ShouldBe(0);
            (await ExistsAsync(transcriptId)).ShouldBeTrue();
            (await QueueExistsAsync(sentOld)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_zero_queued_window_skips_queued_messages_and_still_prunes_transcripts()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(40);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, stale);
            var transcriptId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            var sentOld = await SeedQueuedAsync(
                sessionId, 1, QueuedMessageStatus.Sent, QueuedMessageOrigin.Ui, stale);

            await using var db = CreateContext();
            var result = await CreateService(db, new RetentionSettings
            {
                TranscriptRetentionDays = 7,
                QueuedMessageRetentionDays = 0,
            }).RunOnceAsync(CancellationToken.None);

            result.QueuedMessages.ShouldBe(0);
            (await ExistsAsync(transcriptId)).ShouldBeFalse();
            (await QueueExistsAsync(sentOld)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_terminal_session_past_the_window_with_no_referencers_is_deleted()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));

            await using var db = CreateContext();
            var removed = await CreateService(db).PruneSessionsAsync(CancellationToken.None);
            removed.ShouldBeGreaterThanOrEqualTo(1);

            (await SessionExistsAsync(sessionId)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_session_referenced_via_AgentTask_AgentSessionId_survives_past_the_window()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Failed, DaysAgo(100));
            await SeedTaskAsync(marker, agentSessionId: sessionId, parentSessionId: null);

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_session_referenced_via_AgentTask_ParentSessionId_survives_past_the_window()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            await SeedTaskAsync(marker, agentSessionId: null, parentSessionId: sessionId);

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_session_that_is_an_agents_PersistentSessionId_survives_session_prune()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            await SeedAgentPointingAtAsync(marker, sessionId);

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_non_terminal_session_survives_session_prune_regardless_of_age()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Running, DaysAgo(100));

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task Deleting_a_session_cascades_its_transcripts_and_queued_messages()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(100);
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Failed, stale);
            var transcriptId = await SeedTranscriptAsync(sessionId, 1, TranscriptKinds.UserPrompt, stale);
            var queuedId = await SeedQueuedAsync(
                sessionId, 1, QueuedMessageStatus.Sent, QueuedMessageOrigin.Ui, stale);
            var protectedSession = await SeedSessionAsync(marker, SessionStatus.Stopped, stale);
            var protectedInbound = Guid.NewGuid();
            var protectedOwner = Guid.NewGuid();
            var protectedBody = ChannelPromptCorrelation.Mark(protectedOwner,
                "separate stopped session complete channel receipt tail");
            await using (var seed = CreateContext())
            {
                seed.ChannelInbounds.Add(new ChannelInbound
                {
                    Id = protectedInbound, Provider = "telegram", ConversationId = marker,
                    NativeMessageId = protectedInbound.ToString("N"),
                    QueueMessageId = protectedOwner, EnvelopeJson = "{\"attachment\":\"AAEf/251\"}",
                    AcceptedAt = stale,
                });
                seed.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = protectedOwner, SourceChannelInboundId = protectedInbound,
                    AgentSessionId = protectedSession, Body = protectedBody,
                    Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Sent,
                    DeliveryAttempts = 1, LastDeliveryBaselineSequence = 0,
                    CreatedAt = stale, SentAt = stale, ChannelReplySettledAt = stale,
                });
                seed.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = protectedSession,
                    Sequence = 1, Kind = TranscriptKinds.UserPrompt, Text = protectedBody,
                    Timestamp = stale, CreatedAt = stale,
                });
                await seed.SaveChangesAsync();
            }

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeFalse();
            (await ExistsAsync(transcriptId)).ShouldBeFalse();
            (await QueueExistsAsync(queuedId)).ShouldBeFalse();
            (await SessionExistsAsync(protectedSession)).ShouldBeTrue();
            (await QueueExistsAsync(protectedOwner)).ShouldBeTrue();
            (await CreateService(db).PruneChannelInboundPayloadsAsync(CancellationToken.None))
                .ShouldBeGreaterThan(0);
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);
            (await SessionExistsAsync(protectedSession)).ShouldBeFalse();
            (await QueueExistsAsync(protectedOwner)).ShouldBeFalse();
            var tombstone = await db.ChannelInbounds.AsNoTracking()
                .SingleAsync(i => i.Id == protectedInbound);
            tombstone.NativeMessageId.ShouldBe(protectedInbound.ToString("N"));
            tombstone.EnvelopeJson.ShouldBeNull();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_zero_session_window_skips_sessions()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));

            await using var db = CreateContext();
            var result = await CreateService(db, new RetentionSettings
            {
                SessionRetentionDays = 0,
            }).RunOnceAsync(CancellationToken.None);

            result.Sessions.ShouldBe(0);
            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_fully_terminal_stale_tree_loses_every_row_and_its_events()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var rootId = Guid.NewGuid();
            var parentId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await SeedTaskRowAsync(marker, parentId, rootId, rootId, depth: 1,
                AgentTaskStatus.Failed, stale, stale.AddHours(2));
            await SeedTaskRowAsync(marker, childId, rootId, parentId, depth: 2,
                AgentTaskStatus.Canceled, stale, stale.AddHours(3));
            var rootEvent = await SeedTaskEventAsync(rootId, stale.AddHours(1));
            var parentEvent = await SeedTaskEventAsync(parentId, stale.AddHours(2));
            var childEvent = await SeedTaskEventAsync(childId, stale.AddHours(3));

            await using var db = CreateContext();
            var removed = await CreateService(db).PruneTasksAsync(CancellationToken.None);
            removed.ShouldBeGreaterThanOrEqualTo(3);

            (await TaskExistsAsync(rootId)).ShouldBeFalse();
            (await TaskExistsAsync(parentId)).ShouldBeFalse();
            (await TaskExistsAsync(childId)).ShouldBeFalse();
            (await TaskEventExistsAsync(rootEvent)).ShouldBeFalse();
            (await TaskEventExistsAsync(parentEvent)).ShouldBeFalse();
            (await TaskEventExistsAsync(childEvent)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_tree_with_one_live_member_survives_entirely()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await SeedTaskRowAsync(marker, childId, rootId, rootId, depth: 1,
                AgentTaskStatus.Working, stale, completedAt: null);

            await using var db = CreateContext();
            await CreateService(db).PruneTasksAsync(CancellationToken.None);

            (await TaskExistsAsync(rootId)).ShouldBeTrue("a partial tree delete is forbidden");
            (await TaskExistsAsync(childId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_tree_whose_newest_row_is_within_the_window_survives_entirely()
    {
        var marker = NewMarker();
        try
        {
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, DaysAgo(200), DaysAgo(199));
            await SeedTaskRowAsync(marker, childId, rootId, rootId, depth: 1,
                AgentTaskStatus.Succeeded, DaysAgo(2), DaysAgo(1));

            await using var db = CreateContext();
            await CreateService(db).PruneTasksAsync(CancellationToken.None);

            (await TaskExistsAsync(rootId)).ShouldBeTrue("a stale leaf of a fresh tree survives");
            (await TaskExistsAsync(childId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_zero_task_window_skips_tasks()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var rootId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale);

            await using var db = CreateContext();
            var result = await CreateService(db, new RetentionSettings
            {
                TaskRetentionDays = 0,
            }).RunOnceAsync(CancellationToken.None);

            result.Tasks.ShouldBe(0);
            (await TaskExistsAsync(rootId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task RunOnce_archives_old_audit_FullContent_using_the_configured_window()
    {
        var marker = NewMarker();
        try
        {
            // 20d is younger than the hardcoded 90 the endpoint used to default to, so this
            // only goes if RunOnceAsync actually reads AuditSettings.RetentionDays = 14.
            var oldId = await SeedAuditAsync(marker, DaysAgo(20), """{"prompt":"old"}""", " old");
            var youngId = await SeedAuditAsync(marker, DaysAgo(7), """{"prompt":"young"}""", " young");

            await using var db = CreateContext();
            var result = await CreateService(db, auditSettings: new AuditSettings { RetentionDays = 14 })
                .RunOnceAsync(CancellationToken.None);

            result.AuditRecords.ShouldBeGreaterThanOrEqualTo(1);

            var old = await GetAuditAsync(oldId);
            old.ShouldNotBeNull();
            old.FullContent.ShouldBeNull();
            old.Summary.ShouldBe($"{marker} old");
            old.ModelName.ShouldBe("test-model");
            old.TokensIn.ShouldBe(10);
            old.TokensOut.ShouldBe(20);
            old.CostUsd.ShouldBe(0.001m);

            var young = await GetAuditAsync(youngId);
            young.ShouldNotBeNull();
            young.FullContent.ShouldNotBeNull("a 7-day-old record is inside the 14-day window");
            young.FullContent.ShouldContain("young");
            young.Summary.ShouldBe($"{marker} young");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task A_zero_audit_window_skips_the_archive_pass()
    {
        var marker = NewMarker();
        try
        {
            var oldId = await SeedAuditAsync(marker, DaysAgo(200), """{"prompt":"keep"}""");

            await using var db = CreateContext();
            var result = await CreateService(db, auditSettings: new AuditSettings { RetentionDays = 0 })
                .RunOnceAsync(CancellationToken.None);

            result.AuditRecords.ShouldBe(0);
            var kept = await GetAuditAsync(oldId);
            kept.ShouldNotBeNull();
            kept.FullContent.ShouldNotBeNull("RetentionDays <= 0 must skip the archive pass");
            kept.FullContent.ShouldContain("keep");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    // ---------- helpers ----------

    [Test]
    public async Task C514_Retention_keeps_ambiguous_arm_veto()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Running, DateTime.UtcNow);
            var id = Guid.NewGuid();
            await using (var db = CreateContext())
            {
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = id,
                    AgentSessionId = sessionId,
                    Body = "/remote-control",
                    Status = QueuedMessageStatus.Sent,
                    Sequence = 1,
                    Origin = QueuedMessageOrigin.Supervision,
                    CreatedAt = DaysAgo(40),
                    SentAt = DaysAgo(40),
                    MaintenanceKind = RemoteControlMaintenanceKind.AutomaticArm,
                    MaintenanceResult = RemoteControlArmResult.ArmUnconfirmed,
                    MaintenanceAcceptedStartedAt = SessionGeneration.Normalize(DateTime.UtcNow),
                });
                await db.SaveChangesAsync();
            }

            await using var prune = CreateContext();
            await CreateService(prune).PruneQueuedMessagesAsync(CancellationToken.None);
            (await QueueExistsAsync(id)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task C514_Open_modal_survives_episode_retention()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Running, DateTime.UtcNow);
            var openId = Guid.NewGuid();
            var resolvedId = Guid.NewGuid();
            var generation = SessionGeneration.Normalize(DateTime.UtcNow);
            await using (var db = CreateContext())
            {
                db.RemoteControlModalEpisodes.Add(new RemoteControlModalEpisode
                {
                    Id = openId,
                    SessionId = sessionId,
                    AcceptedStartedAt = generation,
                    FirstObservedAt = DaysAgo(40),
                    LastObservedAt = DaysAgo(40),
                });
                db.RemoteControlModalEpisodes.Add(new RemoteControlModalEpisode
                {
                    Id = resolvedId,
                    SessionId = sessionId,
                    AcceptedStartedAt = SessionGeneration.Next(generation, DateTime.UtcNow.AddMinutes(-1)),
                    FirstObservedAt = DaysAgo(40),
                    LastObservedAt = DaysAgo(40),
                    ResolvedAt = DaysAgo(40),
                    Resolution = RemoteControlEpisodeResolution.ObservedClear,
                });
                await db.SaveChangesAsync();
            }

            await using var prune = CreateContext();
            await CreateService(prune).PruneResolvedModalEpisodesAsync(CancellationToken.None);
            await using var verify = CreateContext();
            (await verify.RemoteControlModalEpisodes.AnyAsync(e => e.Id == openId)).ShouldBeTrue();
            (await verify.RemoteControlModalEpisodes.AnyAsync(e => e.Id == resolvedId)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task C514_Deferred_work_retains_its_attempt_and_session()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            await using (var db = CreateContext())
            {
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = Guid.NewGuid(),
                    AgentSessionId = sessionId,
                    Body = "deferred original work body c514",
                    Status = QueuedMessageStatus.Pending,
                    Sequence = 1,
                    Origin = QueuedMessageOrigin.System,
                    CreatedAt = DaysAgo(100),
                    DeferredFromRunAttemptId = Guid.NewGuid(),
                    MaintenanceAcceptedStartedAt = SessionGeneration.Normalize(DaysAgo(100)),
                });
                await db.SaveChangesAsync();
            }

            await using var prune = CreateContext();
            await CreateService(prune).PruneSessionsAsync(CancellationToken.None);
            (await SessionExistsAsync(sessionId)).ShouldBeTrue();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// V-29 / G-91: a pending dispatch-warning intent is the ONLY thing naming its original
    /// destination — no agent PersistentSessionId, no task AgentSessionId/ParentSessionId, no
    /// queue row and no note — and the session survives the sweep anyway. Materializing it, or
    /// capturing it with ReplyTo None, releases the session.
    /// </summary>
    [Test]
    public async Task C508_IntentSessionRetention()
    {
        var marker = NewMarker();
        try
        {
            var sessionId = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            var loose = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            var notRequired = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            var taskId = await SeedDetachedTaskAsync(marker, DaysAgo(200));
            await SeedIntentAsync(taskId, sessionId, AgentTaskReplyTo.Session, DaysAgo(100), materialized: false);
            await SeedIntentAsync(taskId, loose, AgentTaskReplyTo.Session, DaysAgo(100), materialized: true);
            await SeedIntentAsync(taskId, notRequired, AgentTaskReplyTo.None, DaysAgo(100), materialized: false);

            await using var db = CreateContext();
            await CreateService(db).PruneSessionsAsync(CancellationToken.None);

            (await SessionExistsAsync(sessionId)).ShouldBeTrue(
                "a pending dispatch-warning intent still owes this destination a prompt");
            (await SessionExistsAsync(loose)).ShouldBeFalse("a materialized intent owes the session nothing");
            (await SessionExistsAsync(notRequired)).ShouldBeFalse("ReplyTo None never owed a prompt");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// V-29 / G-92: transcript pruning is per-session all-or-nothing, and the protected
    /// destination keeps EVERY row — including rows far older than the window — while an
    /// otherwise identical unprotected session in the same sweep loses all of its.
    /// </summary>
    [Test]
    public async Task C508_IntentTranscriptRetention()
    {
        var marker = NewMarker();
        try
        {
            var protectedSession = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            var control = await SeedSessionAsync(marker, SessionStatus.Stopped, DaysAgo(100));
            var taskId = await SeedDetachedTaskAsync(marker, DaysAgo(200));
            await SeedIntentAsync(taskId, protectedSession, AgentTaskReplyTo.Session, DaysAgo(100), materialized: false);

            var oldest = await SeedTranscriptAsync(protectedSession, 1, TranscriptKinds.UserPrompt, DaysAgo(120));
            var middle = await SeedTranscriptAsync(protectedSession, 2, TranscriptKinds.AssistantText, DaysAgo(110));
            var newest = await SeedTranscriptAsync(protectedSession, 3, TranscriptKinds.TurnEnd, DaysAgo(100));
            var controlRow = await SeedTranscriptAsync(control, 1, TranscriptKinds.UserPrompt, DaysAgo(120));

            await using var db = CreateContext();
            await CreateService(db).PruneTranscriptsAsync(CancellationToken.None);

            (await ExistsAsync(oldest)).ShouldBeTrue();
            (await ExistsAsync(middle)).ShouldBeTrue();
            (await ExistsAsync(newest)).ShouldBeTrue();
            (await ExistsAsync(controlRow)).ShouldBeFalse(
                "the unprotected session in the same sweep must still make progress");
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// V-29 / G-93: any intent on ANY member of a stale terminal tree — pending or materialized —
    /// protects the whole tree, and an unrelated eligible tree still prunes in the same sweep.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C508_IntentTaskTreeRetention(bool materialized)
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await SeedTaskRowAsync(marker, childId, rootId, rootId, depth: 1,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(2));
            // The intent hangs off the CHILD; the whole tree must be retained.
            await SeedIntentAsync(childId, parentSessionId: null, AgentTaskReplyTo.Session,
                stale.AddHours(2), materialized);

            var otherRoot = Guid.NewGuid();
            await SeedTaskRowAsync(marker, otherRoot, otherRoot, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            var otherEvent = await SeedTaskEventAsync(otherRoot, stale.AddHours(1));

            await using var db = CreateContext();
            Exception? caught = null;
            try { await CreateService(db).PruneTasksAsync(CancellationToken.None); }
            catch (Exception ex) { caught = ex; }

            caught.ShouldBeNull();
            (await TaskExistsAsync(rootId)).ShouldBeTrue("a partial tree delete is forbidden");
            (await TaskExistsAsync(childId)).ShouldBeTrue();
            (await TaskExistsAsync(otherRoot)).ShouldBeFalse(
                "an unrelated eligible tree still prunes in the same sweep");
            (await TaskEventExistsAsync(otherEvent)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// V-29 / G-94: the same protection for a notification on any member, including a CONFIRMED
    /// note and a NotRequired one — the row is history the tree delete would silently destroy.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task C508_NotificationTaskTreeRetention(bool confirmed)
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, rootId, rootId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await SeedTaskRowAsync(marker, childId, rootId, rootId, depth: 1,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(2));
            await SeedDispatchNoteAsync(childId, stale.AddHours(2), confirmed);

            var otherRoot = Guid.NewGuid();
            await SeedTaskRowAsync(marker, otherRoot, otherRoot, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));

            await using var db = CreateContext();
            Exception? caught = null;
            try { await CreateService(db).PruneTasksAsync(CancellationToken.None); }
            catch (Exception ex) { caught = ex; }

            caught.ShouldBeNull();
            (await TaskExistsAsync(rootId)).ShouldBeTrue();
            (await TaskExistsAsync(childId)).ShouldBeTrue();
            (await TaskExistsAsync(otherRoot)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    /// <summary>
    /// A task that names no session at all, so only the intent under test can protect anything.
    /// </summary>
    private static async Task<Guid> SeedDetachedTaskAsync(string marker, DateTime createdAt)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = marker,
            Goal = "retention intent custody",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            AgentSessionId = null,
            ParentSessionId = null,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = createdAt,
            CompletedAt = createdAt.AddHours(1),
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// CARD-0544 PC-118 / R-11: a stale keyed row whose Completion obligation is unconfirmed is receipt
    /// evidence still owed and survives the queue window; a confirmed, channel-classified row prunes while the
    /// notification keeps ConfirmedAt/ConfirmingPromptSequence, and the next reconcile never re-enqueues.
    /// </summary>
    [Test]
    public async Task C544_CompletionObligationRetention()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var caller = await BridgeQueueHarness.CreateAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var old = DaysAgo(40);
        (Guid NoteId, Guid RowId) confirmed, owed, confirmedOpen;
        await using (var db = new AppDbContext(options))
        {
            confirmed = SeedCompletionObligation(db, caller.SessionId, old, confirmed: true, sequence: 1,
                discoveryClosed: true);
            owed = SeedCompletionObligation(db, caller.SessionId, old, confirmed: false, sequence: 2);
            confirmedOpen = SeedCompletionObligation(db, caller.SessionId, old, confirmed: true, sequence: 3);
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
            await CreateService(db).PruneQueuedMessagesAsync(CancellationToken.None);

        await using (var db = new AppDbContext(options))
        {
            (await db.SessionQueuedMessages.AnyAsync(m => m.Id == owed.RowId)).ShouldBeTrue("unconfirmed Completion evidence survives");
            (await db.SessionQueuedMessages.AnyAsync(m => m.Id == confirmedOpen.RowId)).ShouldBeTrue(
                "a completion receipt does not classify its possible channel reply");
            (await db.SessionQueuedMessages.AnyAsync(m => m.Id == confirmed.RowId)).ShouldBeFalse("confirmed obligation row prunes");
            var kept = await db.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == confirmed.NoteId);
            kept.State.ShouldBe(LandNotificationState.Confirmed);
            kept.ConfirmedAt.ShouldNotBeNull("receipt identity retained");
            kept.ConfirmingPromptSequence.ShouldBe(41);

            await new AgentTaskLandNotificationService(db, caller.Queue, new CompletionNoteFlushQueue(), caller.Runtime, TimeProvider.System)
                .ReconcileAsync(confirmed.NoteId, CancellationToken.None);
        }

        await using (var verify = new AppDbContext(options))
        {
            (await verify.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId == confirmed.NoteId))
                .ShouldBe(0, "a confirmed obligation is never re-enqueued");
            var after = await verify.AgentTaskLandNotifications.AsNoTracking().SingleAsync(n => n.Id == confirmed.NoteId);
            after.State.ShouldBe(LandNotificationState.Confirmed);
            after.ConfirmingPromptSequence.ShouldBe(41);
            caller.Adapter.SubmittedBodies.ShouldBeEmpty();
        }
    }

    private static (Guid NoteId, Guid RowId) SeedCompletionObligation(AppDbContext db, Guid session, DateTime at, bool confirmed,
        long sequence, bool discoveryClosed = false)
    {
        var taskId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        const string body = "[task retention] verification=Final; scope=Full; final-review=none\nreport";
        var digest = DelegationNoteDigest.Compute(body);
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "C544 retention", Goal = "retention", Role = AgentTaskRole.Review,
            WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, ReplyTo = AgentTaskReplyTo.Session,
            ParentSessionId = session, VerificationProfileVersion = 1, VerificationRound = VerificationRound.Final,
            CreatedAt = at, CompletedAt = at,
        });
        db.AgentTaskEvents.Add(new AgentTaskEvent { Id = eventId, AgentTaskId = taskId, Type = AgentTaskEventType.Completed, Detail = "settled", At = at });
        db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
        {
            Id = noteId, TaskId = taskId, SourceEventId = eventId, Kind = LandNotificationKind.TaskCompletion,
            ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = session, Body = body, ContentDigest = digest,
            CreatedAt = at, NextAttemptAt = at, EnqueuedAt = at, QueueMessageId = rowId,
            State = confirmed ? LandNotificationState.Confirmed : LandNotificationState.AwaitingReceipt,
            ConfirmedAt = confirmed ? at : null, ConfirmingPromptSequence = confirmed ? 41 : null,
        });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = rowId, AgentSessionId = session, Sequence = sequence, Body = body,
            Status = QueuedMessageStatus.Sent, Origin = QueuedMessageOrigin.Delegation, SourceTaskId = taskId,
            ContentDigest = digest, SourceLandNotificationId = noteId, ConversationKey = $"task:{taskId:N}",
            DeliveryAttempts = 1, DeliveryVerdict = confirmed ? DeliveryVerdict.Delivered : null,
            ChannelReplyDiscoveryClosedAt = discoveryClosed ? at : null,
            CreatedAt = at, SentAt = at,
        });
        return (noteId, rowId);
    }

    private static async Task<Guid> SeedIntentAsync(
        Guid taskId, Guid? parentSessionId, AgentTaskReplyTo replyTo, DateTime createdAt, bool materialized)
    {
        var intentId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await using var db = CreateContext();
        var dispatchEventId = Guid.NewGuid();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = dispatchEventId,
            AgentTaskId = taskId,
            Type = AgentTaskEventType.Dispatched,
            Detail = "retention dispatch",
            At = createdAt,
        });
        var payload = DispatchBaseNotificationPayload.Capture(
            intentId, notificationId, taskId, dispatchEventId, 0,
            DispatchBaseNotificationPayload.MismatchKey, replyTo, parentSessionId,
            "retention dispatch-base warning", createdAt);
        db.AgentTaskDispatchWarningIntents.Add(new AgentTaskDispatchWarningIntent
        {
            Id = payload.WarningEventId,
            DispatchEventId = payload.DispatchEventId,
            TaskId = payload.TaskId,
            Attempt = payload.Attempt,
            WarningKey = payload.WarningKey,
            NotificationId = payload.NotificationId,
            ReplyTo = payload.ReplyTo,
            ParentSessionId = payload.ParentSessionId,
            Detail = payload.Detail,
            Body = payload.Body,
            ContentDigest = payload.ContentDigest,
            CreatedAt = payload.CreatedAt,
            InitialState = payload.InitialState,
            NextAttemptAt = payload.CreatedAt,
            MaterializedAt = materialized ? createdAt : null,
        });
        await db.SaveChangesAsync();
        return intentId;
    }

    private static async Task<Guid> SeedDispatchNoteAsync(Guid taskId, DateTime createdAt, bool confirmed)
    {
        var noteId = Guid.NewGuid();
        var warningId = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = warningId,
            AgentTaskId = taskId,
            Type = AgentTaskEventType.Warning,
            Detail = "retention dispatch-base warning",
            At = createdAt,
        });
        db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
        {
            Id = noteId,
            RequestId = null,
            TaskId = taskId,
            SourceEventId = warningId,
            Kind = LandNotificationKind.DispatchBase,
            ReplyTo = confirmed ? AgentTaskReplyTo.Session : AgentTaskReplyTo.None,
            ParentSessionId = null,
            Body = "retention dispatch-base note",
            ContentDigest = "digest",
            CreatedAt = createdAt,
            NextAttemptAt = createdAt,
            State = confirmed ? LandNotificationState.Confirmed : LandNotificationState.NotRequired,
            ConfirmedAt = confirmed ? createdAt : null,
        });
        await db.SaveChangesAsync();
        return noteId;
    }

    private sealed record RetentionObservation(string? ErrorType, string Retained, string Unrelated)
    {
        public static RetentionObservation Of(Exception? error, IEnumerable<Guid> retained, IEnumerable<Guid> unrelated) =>
            new(error?.GetType().Name, Join(retained), Join(unrelated));

        private static string Join(IEnumerable<Guid> ids) =>
            string.Join(",", ids.OrderBy(id => id).Select(id => id.ToString("N")));
    }

    /// <summary>
    /// CARD-0079 PC-209. An unresolved publication protects its interpreter task tree
    /// even when that tree has no notification, session, or recovery pointer of its own.
    /// </summary>
    [Test]
    public async Task Legacy_note_retention_preserves_interpreter_tasks()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var stale = DaysAgo(200);
        var interpreterId = Guid.NewGuid();
        var unrelatedId = Guid.NewGuid();
        await SeedInterpreterPublicationAsync(options, interpreterId, sessionId: null, stale, confirmed: false);
        await using (var db = new AppDbContext(options))
        {
            db.AgentTasks.Add(TerminalTask(unrelatedId, "unrelated retention", stale));
            await db.SaveChangesAsync();
        }

        Exception? caught = null;
        await using (var db = new AppDbContext(options))
        {
            try { await CreateService(db).PruneTasksAsync(CancellationToken.None); }
            catch (Exception ex) { caught = ex; }
        }

        await using var verify = new AppDbContext(options);
        var retained = await verify.AgentTasks.AnyAsync(t => t.Id == interpreterId) ? new[] { interpreterId } : Array.Empty<Guid>();
        var unrelated = await verify.AgentTasks.AnyAsync(t => t.Id == unrelatedId) ? new[] { unrelatedId } : Array.Empty<Guid>();
        var observedRetention = RetentionObservation.Of(caught, retained, unrelated);
        var expectedRetention = RetentionObservation.Of(null, new[] { interpreterId }, Array.Empty<Guid>());
        observedRetention.ShouldBe(expectedRetention);
    }

    /// <summary>
    /// CARD-0079 PC-210. The interpreter session survives when only the publication names it.
    /// </summary>
    [Test]
    public async Task Legacy_note_retention_preserves_interpreter_sessions()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var interpreterSession = Guid.NewGuid();
        var unrelatedSession = Guid.NewGuid();
        var stale = DaysAgo(100);
        await SeedStoppedSessionAsync(options, interpreterSession, stale);
        await SeedStoppedSessionAsync(options, unrelatedSession, stale);
        await SeedInterpreterPublicationAsync(options, Guid.NewGuid(), interpreterSession, DaysAgo(200), confirmed: false, capturedOnly: true);

        Exception? caught = null;
        await using (var db = new AppDbContext(options))
        {
            try { await CreateService(db).PruneSessionsAsync(CancellationToken.None); }
            catch (Exception ex) { caught = ex; }
        }

        await using var verify = new AppDbContext(options);
        var retained = await verify.AgentSessions.AnyAsync(s => s.Id == interpreterSession) ? new[] { interpreterSession } : Array.Empty<Guid>();
        var unrelated = await verify.AgentSessions.AnyAsync(s => s.Id == unrelatedSession) ? new[] { unrelatedSession } : Array.Empty<Guid>();
        var observedRetention = RetentionObservation.Of(caught, retained, unrelated);
        var expectedRetention = RetentionObservation.Of(null, new[] { interpreterSession }, Array.Empty<Guid>());
        observedRetention.ShouldBe(expectedRetention);
    }

    /// <summary>
    /// CARD-0079 PC-211. Transcript rows of that interpreter session stay with it.
    /// </summary>
    [Test]
    public async Task Legacy_note_retention_preserves_original_receipt_evidence()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var interpreterSession = Guid.NewGuid();
        var unrelatedSession = Guid.NewGuid();
        var stale = DaysAgo(40);
        await SeedStoppedSessionAsync(options, interpreterSession, stale);
        await SeedStoppedSessionAsync(options, unrelatedSession, stale);
        var originalTranscript = await SeedSchemaTranscriptAsync(options, interpreterSession, stale);
        var unrelatedTranscript = await SeedSchemaTranscriptAsync(options, unrelatedSession, stale);
        await SeedInterpreterPublicationAsync(options, Guid.NewGuid(), interpreterSession, DaysAgo(200), confirmed: false, capturedOnly: true);

        Exception? caught = null;
        await using (var db = new AppDbContext(options))
        {
            try { await CreateService(db).PruneTranscriptsAsync(CancellationToken.None); }
            catch (Exception ex) { caught = ex; }
        }

        await using var verify = new AppDbContext(options);
        var retained = await verify.TranscriptEntries.AnyAsync(t => t.Id == originalTranscript) ? new[] { originalTranscript } : Array.Empty<Guid>();
        var unrelated = await verify.TranscriptEntries.AnyAsync(t => t.Id == unrelatedTranscript) ? new[] { unrelatedTranscript } : Array.Empty<Guid>();
        var observedRetention = RetentionObservation.Of(caught, retained, unrelated);
        var expectedRetention = RetentionObservation.Of(null, new[] { originalTranscript }, Array.Empty<Guid>());
        observedRetention.ShouldBe(expectedRetention);
    }

    /// <summary>
    /// CARD-0079 PC-212. Confirmed publication identity survives retention and blocks a second note.
    /// </summary>
    [Test]
    public async Task Legacy_publication_tombstone_prevents_reminting()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var sessionId = Guid.NewGuid();
        await SeedStoppedSessionAsync(options, sessionId, DateTime.UtcNow);
        await using (var db = new AppDbContext(options))
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == sessionId);
            session.Status = SessionStatus.Running;
            session.EndedAt = null;
            session.ExitCode = null;
            await db.SaveChangesAsync();
        }
        var seeded = await SeedInterpreterPublicationAsync(
            options, Guid.NewGuid(), sessionId, DateTime.UtcNow, confirmed: true, attachTasksToSession: true);
        var unrelatedId = Guid.NewGuid();
        await using (var db = new AppDbContext(options))
        {
            db.AgentTasks.Add(TerminalTask(unrelatedId, "unrelated tombstone", DaysAgo(200)));
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
            await CreateService(db).RunOnceAsync(CancellationToken.None);

        await using var replay = new AppDbContext(options);
        var subject = await replay.AgentTasks.SingleAsync(t => t.Id == seeded.CheckedTaskId);
        var publisher = new LegacyCheckNotePublicationService(replay, TimeProvider.System);
        await publisher.TryPublishAsync(subject, seeded.CheckNumber, "replayed body", "event", seeded.RunId, false, null, CancellationToken.None);

        await using var verify = new AppDbContext(options);
        var after = await verify.LegacyCheckNotePublications.SingleAsync(p => p.CheckedTaskId == seeded.CheckedTaskId);
        var publicationIdentity = (after.Id, after.CheckedTaskId, after.CheckedTaskAttempt, after.CheckedTaskDispatchedAt, after.CheckNumber);
        var originalIdentity = (seeded.PublicationId, seeded.CheckedTaskId, seeded.Attempt, seeded.DispatchedAt, seeded.CheckNumber);
        publicationIdentity.ShouldBe(originalIdentity);
        var newNotes = await verify.AgentTaskLandNotifications
            .Where(n => n.Kind == LandNotificationKind.LegacyCheckNote && n.Id != seeded.NotificationId)
            .ToListAsync();
        newNotes.Count.ShouldBe(0);
        (await verify.AgentTasks.AnyAsync(t => t.Id == unrelatedId)).ShouldBeFalse();
    }

    private readonly record struct PublicationSeed(
        Guid PublicationId, Guid CheckedTaskId, Guid RunId, Guid NotificationId, int Attempt, DateTime DispatchedAt, int CheckNumber);

    private static async Task<PublicationSeed> SeedInterpreterPublicationAsync(
        DbContextOptions<AppDbContext> options,
        Guid interpreterTaskId,
        Guid? sessionId,
        DateTime stale,
        bool confirmed,
        bool capturedOnly = false,
        bool attachTasksToSession = false)
    {
        var now = DateTime.UtcNow;
        var dispatched = SessionGeneration.Normalize(now);
        var agentId = Guid.NewGuid();
        var checkedId = Guid.NewGuid();
        var runId = interpreterTaskId;
        var episodeId = Guid.NewGuid();
        var publicationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var generation = dispatched;
        await using var db = new AppDbContext(options);
        if (sessionId is Guid existing)
        {
            var session = await db.AgentSessions.SingleAsync(s => s.Id == existing);
            generation = SessionGeneration.Normalize(session.StartedAt);
        }
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "c79-retention-" + agentId.ToString("N")[..8],
            Slug = "c79-" + agentId.ToString("N")[..12],
            WorkingDirectory = Path.GetTempPath(),
            Status = AgentStatus.Idle,
            CreatedAt = stale,
            UpdatedAt = stale,
        });
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
        {
            Id = episodeId,
            PhysicalAgentId = agentId,
            SessionId = Guid.NewGuid(),
            AcceptedStartedAt = generation,
            BoundaryIdentity = "boundary-retention",
            BoundaryCreatedAt = stale,
            ContinuationCreatedAt = stale,
            ConfiguredThresholdMinutes = 10,
            DetectedAt = stale,
            State = CheckCompactionRecoveryState.AwaitingCheck,
            ResumeSessionId = attachTasksToSession ? sessionId : null,
            ResumeAcceptedStartedAt = attachTasksToSession ? generation : null,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = checkedId, RootTaskId = checkedId, Title = "checked retention", Goal = "subject",
            Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Succeeded,
            WorkingDirectory = Path.GetTempPath(), ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = now, CompletedAt = now, DispatchedAt = dispatched, Attempt = 1,
            AgentId = agentId, AgentSessionId = attachTasksToSession ? sessionId : null,
        });
        db.AgentTasks.Add(TerminalTask(runId, "interpreter retention", stale));
        db.AgentTasks.Local.Single(t => t.Id == runId).AgentId = agentId;
        db.AgentTasks.Local.Single(t => t.Id == runId).AgentSessionId = attachTasksToSession ? sessionId : null;
        if (!capturedOnly)
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = eventId, AgentTaskId = checkedId, Type = AgentTaskEventType.Check, Detail = "produced", At = now,
            });
            db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
            {
                Id = noteId, TaskId = checkedId, SourceEventId = eventId, Kind = LandNotificationKind.LegacyCheckNote,
                ReplyTo = AgentTaskReplyTo.None, Body = "original note", ContentDigest = "digest",
                CreatedAt = now, NextAttemptAt = now,
                State = confirmed ? LandNotificationState.Confirmed : LandNotificationState.Queued,
                ConfirmedAt = confirmed ? now : null,
            });
        }
        db.LegacyCheckNotePublications.Add(new LegacyCheckNotePublication
        {
            Id = publicationId,
            CheckedTaskId = checkedId,
            CheckedTaskAttempt = 1,
            CheckedTaskDispatchedAt = dispatched,
            CheckNumber = 1,
            RecoveryId = episodeId,
            PhysicalAgentId = agentId,
            InterpreterSessionId = sessionId ?? Guid.NewGuid(),
            InterpreterAcceptedStartedAt = generation,
            ParentSessionId = Guid.NewGuid(),
            CapturedAt = stale,
            FactsSnapshotJson = "{\"check\":1}",
            RenderContextJson = "{}",
            InterpretationTaskId = runId,
            InterpretationDeadlineAt = now,
            State = capturedOnly ? LegacyCheckNoteState.Captured : LegacyCheckNoteState.Produced,
            SourceEventId = capturedOnly ? Guid.NewGuid() : eventId,
            NotificationId = capturedOnly ? Guid.NewGuid() : noteId,
            ProducedAt = capturedOnly ? null : now,
            Body = capturedOnly ? null : "original note",
            ContentDigest = capturedOnly ? null : "digest",
            NextAttemptAt = now,
        });
        await db.SaveChangesAsync();
        return new PublicationSeed(publicationId, checkedId, runId, noteId, 1, dispatched, 1);
    }

    private static AgentTask TerminalTask(Guid id, string title, DateTime stale) => new()
    {
        Id = id, RootTaskId = id, Title = title, Goal = "retention",
        Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Succeeded,
        WorkingDirectory = Path.GetTempPath(), ReplyTo = AgentTaskReplyTo.None,
        CreatedAt = stale, CompletedAt = stale.AddHours(1),
    };

    private static async Task SeedStoppedSessionAsync(DbContextOptions<AppDbContext> options, Guid sessionId, DateTime lastSeenAt)
    {
        await using var db = new AppDbContext(options);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Stopped,
            Cwd = Path.GetTempPath(),
            Cols = 80,
            Rows = 24,
            CreatedAt = lastSeenAt.AddDays(-1),
            StartedAt = lastSeenAt.AddDays(-1),
            LastSeenAt = lastSeenAt,
            EndedAt = lastSeenAt,
            ExitCode = 0,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedSchemaTranscriptAsync(DbContextOptions<AppDbContext> options, Guid sessionId, DateTime at)
    {
        var id = Guid.NewGuid();
        await using var db = new AppDbContext(options);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = id, AgentSessionId = sessionId, Sequence = 1, Kind = TranscriptKinds.UserPrompt,
            Uuid = id.ToString("N"), Text = "interpreter prompt", CreatedAt = at, Timestamp = at,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class C768Fixture : IAsyncDisposable
    {
        private long _sequence;
        public required IsolatedTestSchema Schema { get; init; }
        public required BridgeQueueHarness Harness { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public string ConnectionString => Schema.ConnectionString;

        public static async Task<C768Fixture> CreateAsync(int debounceMs = 0)
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var harness = await BridgeQueueHarness.CreateAsync(new()
            {
                ConnectionString = schema.ConnectionString, PreserveDatabaseOnDispose = true,
                Bridge = new ChannelBridgeSettings
                {
                    Enabled = true, BatchingEnabled = true,
                    DebounceWindowMs = debounceMs,
                    DebounceMaxMs = Math.Max(2000, debounceMs * 2),
                    AgentReadyDelaySeconds = 0,
                },
            });
            return new C768Fixture
            {
                Schema = schema, Harness = harness,
                Clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero)),
            };
        }

        public AppDbContext Db(IInterceptor? interceptor = null)
        {
            var options = TestDbFixture.CreateDbContextOptions(ConnectionString);
            return interceptor is null ? new AppDbContext(options)
                : new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(options)
                    .AddInterceptors(interceptor).Options);
        }

        public ChannelBridgeService Bridge() => new(
            Harness.Messaging, Harness.Queue,
            Harness.Provider.GetRequiredService<ChannelInboundDebouncer>(),
            Harness.EventBus, Harness.Provider.GetRequiredService<IServiceScopeFactory>(),
            Harness.Provider.GetRequiredService<IOptions<ChannelBridgeSettings>>(),
            Harness.Clock, NullLogger<ChannelBridgeService>.Instance,
            Harness.Provider.GetRequiredService<ChannelInboundWakeSignal>());

        public void InstallTranscriptReceipt()
        {
            var gate = new SemaphoreSlim(1, 1);
            Harness.Adapter.OnSubmitted = async submitted =>
            {
                await gate.WaitAsync();
                try
                {
                    await using var db = Db();
                    var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == Harness.SessionId)
                        .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
                    var record = new SessionRunnerTranscriptEvent(Harness.SessionId, sequence,
                        TranscriptKinds.UserPrompt, Guid.NewGuid().ToString("N"), null,
                        DateTimeOffset.UtcNow, "user", submitted,
                        null, null, null, null, null);
                    Harness.Runner.SetTranscript(new SessionRunnerTranscriptDto(Harness.SessionId, [record], sequence));
                    await Harness.Runtime.SyncTranscriptAsync(Harness.SessionId, CancellationToken.None);
                }
                finally { gate.Release(); }
            };
        }

        public async Task WaitForAsync(Func<Task<bool>> predicate)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                if (await predicate()) return;
                await Task.Delay(25);
            }
            (await predicate()).ShouldBeTrue("the real channel delivery did not commit a complete prompt");
        }

        public async Task<(Guid InboundId, Guid OwnerId, string Body, string Json)> SeedAsync(
            bool receipt = true, QueuedMessageStatus status = QueuedMessageStatus.Sent,
            QueuedMessageOrigin origin = QueuedMessageOrigin.Channel, string? receiptKind = null,
            string? receiptText = null, Guid? receiptSession = null,
            DateTime? receiptTimestamp = null, long? baseline = 0, bool mapped = true,
            Guid? ownerSession = null)
        {
            var id = Guid.NewGuid();
            var owner = Guid.NewGuid();
            var body = ChannelPromptCorrelation.Mark(owner, $"payload {id:N} complete distinctive tail");
            var json = $"{{\"attachment\":\"AAEf/251\",\"sentinel\":\"{id:N}\"}}";
            var old = Clock.GetUtcNow().UtcDateTime.AddDays(-3);
            await using var db = Db();
            db.ChannelInbounds.Add(new ChannelInbound
            {
                Id = id, Provider = "telegram", ConversationId = id.ToString("N"),
                NativeMessageId = id.ToString("N"), AgentId = Harness.AgentId,
                EnvelopeJson = json, QueueMessageId = mapped ? owner : null,
                AcceptedAt = old, TransferredAt = mapped ? old.AddHours(1) : null,
            });
            if (mapped)
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = owner, SourceChannelInboundId = id, AgentSessionId = ownerSession ?? Harness.SessionId,
                    Body = body, Origin = origin, Status = status,
                    DeliveryAttempts = 1, LastDeliveryBaselineSequence = baseline,
                    LastDeliveryStartedAt = old.AddHours(2), CreatedAt = old,
                    SentAt = status == QueuedMessageStatus.Sent ? old.AddHours(3) : null,
                    ChannelReplySettledAt = old.AddHours(4),
                });
            if (receipt)
                db.TranscriptEntries.Add(new TranscriptEntry
                {
                    Id = Guid.NewGuid(), AgentSessionId = receiptSession ?? ownerSession ?? Harness.SessionId,
                    Sequence = Interlocked.Increment(ref _sequence),
                    Kind = receiptKind ?? TranscriptKinds.UserPrompt,
                    Text = receiptText ?? body, Timestamp = receiptTimestamp ?? old.AddHours(3),
                    CreatedAt = Clock.GetUtcNow().UtcDateTime.AddDays(-2),
                });
            await db.SaveChangesAsync();
            return (id, owner, body, json);
        }

        public async Task AddReceiptAsync(string text, string kind = TranscriptKinds.UserPrompt,
            Guid? sessionId = null, DateTime? nativeAt = null, DateTime? createdAt = null,
            bool nullNative = false)
        {
            await using var db = Db();
            var target = sessionId ?? Harness.SessionId;
            var sequence = (await db.TranscriptEntries.Where(t => t.AgentSessionId == target)
                .MaxAsync(t => (long?)t.Sequence) ?? 0) + 1;
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = target, Sequence = sequence,
                Kind = kind, Text = text,
                Timestamp = nullNative ? null : nativeAt ?? Clock.GetUtcNow().UtcDateTime.AddDays(-2),
                CreatedAt = createdAt ?? Clock.GetUtcNow().UtcDateTime.AddDays(-2),
            });
            await db.SaveChangesAsync();
        }

        public async Task<Guid> AddOtherSessionAsync()
        {
            var id = Guid.NewGuid();
            var old = Clock.GetUtcNow().UtcDateTime.AddDays(-3);
            await using var db = Db();
            db.AgentSessions.Add(new AgentSession
            {
                Id = id, DefinitionName = "claude", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Stopped, Cwd = Path.Combine(Harness.TempRoot, id.ToString("N")),
                Cols = 120, Rows = 30, CreatedAt = old, StartedAt = old,
                LastSeenAt = old, EndedAt = old, ExitCode = 0,
            });
            await db.SaveChangesAsync();
            return id;
        }

        public async Task<int> PruneAsync(int graceHours = 24, IInterceptor? interceptor = null,
            ILogger<DataRetentionService>? logger = null)
        {
            await using var db = Db(interceptor);
            var settings = new RetentionSettings
            {
                ChannelInboundPayloadGraceHours = graceHours,
                SessionRetentionDays = 0, TranscriptRetentionDays = 0,
                QueuedMessageRetentionDays = 0, TaskRetentionDays = 0,
            };
            var audit = new AuditSettings { RetentionDays = 0 };
            var service = new DataRetentionService(db, Options.Create(settings), Options.Create(audit),
                Clock, logger ?? NullLogger<DataRetentionService>.Instance,
                new AuditService(db, Options.Create(audit)));
            return await service.PruneChannelInboundPayloadsAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            await Schema.DisposeAsync();
        }
    }

    private sealed class C768UpdatePause : DbCommandInterceptor
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE \"ChannelInbounds\"", StringComparison.Ordinal))
            {
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class C768CaptureLogger : ILogger<DataRetentionService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state)!;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    private sealed class C768ScanReaderProbe : DbCommandInterceptor
    {
        internal sealed class Page(string[] fields)
        {
            public string[] Fields { get; } = fields;
            public int Rows { get; set; }
        }

        public List<Page> CandidatePages { get; } = [];
        public List<Page> ProofPages { get; } = [];

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            List<Page>? pages = command.CommandText.Contains("FROM \"ChannelInbounds\"", StringComparison.Ordinal)
                && command.CommandText.Contains("ORDER BY", StringComparison.Ordinal)
                ? CandidatePages
                : command.CommandText.Contains("FROM \"TranscriptEntries\"", StringComparison.Ordinal)
                    && command.CommandText.Contains("ORDER BY", StringComparison.Ordinal)
                    ? ProofPages : null;
            if (pages is null) return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
            var fields = Enumerable.Range(0, result.FieldCount).Select(result.GetName).ToArray();
            var page = new Page(fields);
            pages.Add(page);
            return ValueTask.FromResult<DbDataReader>(new C768CountingReader(result, page));
        }
    }

    private sealed class C768CountingReader(DbDataReader inner, C768ScanReaderProbe.Page page) : DbDataReader
    {
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
            inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
            inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
        public override bool NextResult() => inner.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) =>
            inner.NextResultAsync(cancellationToken);
        public override bool Read()
        {
            var found = inner.Read();
            if (found) page.Rows++;
            return found;
        }
        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            var found = await inner.ReadAsync(cancellationToken);
            if (found) page.Rows++;
            return found;
        }
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) =>
            inner.IsDBNullAsync(ordinal, cancellationToken);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) =>
            inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override void Close() => inner.Close();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private static DataRetentionService CreateService(
        AppDbContext db,
        RetentionSettings? settings = null,
        AuditSettings? auditSettings = null)
    {
        var audit = auditSettings ?? new AuditSettings();
        return new DataRetentionService(
            db,
            Options.Create(settings ?? new RetentionSettings()),
            Options.Create(audit),
            TimeProvider.System,
            NullLogger<DataRetentionService>.Instance,
            new AuditService(db, Options.Create(audit)));
    }

    private static string NewMarker() => $"ret-{Guid.NewGuid():N}";

    private static DateTime DaysAgo(int days) => DateTime.UtcNow.AddDays(-days);

    private static async Task<Guid> SeedSessionAsync(string marker, SessionStatus status, DateTime lastSeenAt)
    {
        var sessionId = Guid.NewGuid();
        var startedAt = lastSeenAt.AddDays(-1);
        await using var db = CreateContext();
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            CardId = null,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = status,
            Cwd = Path.Combine(Path.GetTempPath(), marker),
            Cols = 120,
            Rows = 30,
            CreatedAt = startedAt,
            StartedAt = startedAt,
            LastSeenAt = lastSeenAt,
            EndedAt = status is SessionStatus.Stopped or SessionStatus.Failed ? lastSeenAt : null,
            ExitCode = status is SessionStatus.Failed ? 1 : status is SessionStatus.Stopped ? 0 : null,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private static async Task SeedAgentPointingAtAsync(string marker, Guid sessionId)
    {
        var now = DateTime.UtcNow;
        await using var db = CreateContext();
        db.Agents.Add(new Agent
        {
            Id = Guid.NewGuid(),
            Name = marker,
            Slug = marker,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            Status = AgentStatus.Idle,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now.AddDays(-40),
            UpdatedAt = now.AddDays(-40),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedTaskAsync(string marker, Guid? agentSessionId, Guid? parentSessionId)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = marker,
            Goal = "retention session-ref",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            AgentSessionId = agentSessionId,
            ParentSessionId = parentSessionId,
            Status = AgentTaskStatus.Succeeded,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = DaysAgo(100),
            CompletedAt = DaysAgo(99),
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Test]
    public async Task C459_IncompleteRetirementRetainsEvidence()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var taskId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, taskId, taskId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            var other = Guid.NewGuid();
            await SeedTaskRowAsync(marker, other, other, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await using (var db = CreateContext())
            {
                db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
                {
                    Id = Guid.NewGuid(),
                    TaskId = taskId,
                    TaskAttempt = 1,
                    TerminalStatus = AgentTaskStatus.Succeeded,
                    TaskCompletedAt = stale.AddHours(1),
                    ReleasedTaskRevision = Guid.NewGuid(),
                    ReleasedAt = stale.AddHours(2),
                    State = WorktreeRetirementState.Claimed,
                    Active = true,
                    UpdatedAt = stale.AddHours(2),
                    SourceSha = new string('a', 40),
                    SourceFullRef = "refs/heads/feat/card-task-aaaaaaaa",
                    WorktreePath = Path.Combine(Path.GetTempPath(), marker, "tree"),
                    RepositoryPath = Path.Combine(Path.GetTempPath(), marker),
                    CommonDirectory = Path.Combine(Path.GetTempPath(), marker),
                    GitDirectory = Path.Combine(Path.GetTempPath(), marker, ".git"),
                });
                await db.SaveChangesAsync();
            }

            await using var prune = CreateContext();
            await CreateService(prune).PruneTasksAsync(CancellationToken.None);
            var retainedTask = await TaskExistsAsync(taskId);
            retainedTask.ShouldBeTrue();
            (await TaskExistsAsync(other)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    [Test]
    public async Task C459_CompletedRetirementExpiresInDependencyOrder()
    {
        var marker = NewMarker();
        try
        {
            var stale = DaysAgo(200);
            var taskId = Guid.NewGuid();
            await SeedTaskRowAsync(marker, taskId, taskId, parentTaskId: null, depth: 0,
                AgentTaskStatus.Succeeded, stale, stale.AddHours(1));
            await using (var db = CreateContext())
            {
                db.TaskWorktreeRetirements.Add(new TaskWorktreeRetirement
                {
                    Id = Guid.NewGuid(),
                    TaskId = taskId,
                    TaskAttempt = 1,
                    TerminalStatus = AgentTaskStatus.Succeeded,
                    TaskCompletedAt = stale.AddHours(1),
                    ReleasedTaskRevision = Guid.NewGuid(),
                    ReleasedAt = stale.AddHours(2),
                    State = WorktreeRetirementState.Complete,
                    Active = false,
                    UpdatedAt = stale.AddHours(3),
                    SourceSha = new string('a', 40),
                    SourceFullRef = "refs/heads/feat/card-task-aaaaaaaa",
                    WorktreePath = Path.Combine(Path.GetTempPath(), marker, "tree"),
                    RepositoryPath = Path.Combine(Path.GetTempPath(), marker),
                    CommonDirectory = Path.Combine(Path.GetTempPath(), marker),
                    GitDirectory = Path.Combine(Path.GetTempPath(), marker, ".git"),
                });
                await db.SaveChangesAsync();
            }

            await using var prune = CreateContext();
            await CreateService(prune).PruneTasksAsync(CancellationToken.None);
            (await TaskExistsAsync(taskId)).ShouldBeFalse();
        }
        finally
        {
            await CleanupAsync(marker);
        }
    }

    private static async Task<Guid> SeedTaskRowAsync(
        string marker,
        Guid id,
        Guid rootTaskId,
        Guid? parentTaskId,
        int depth,
        AgentTaskStatus status,
        DateTime createdAt,
        DateTime? completedAt)
    {
        await using var db = CreateContext();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = rootTaskId,
            ParentTaskId = parentTaskId,
            Depth = depth,
            Title = marker,
            Goal = "retention task-tree",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Code,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            Status = status,
            ReplyTo = AgentTaskReplyTo.None,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedTaskEventAsync(Guid taskId, DateTime at)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = id,
            AgentTaskId = taskId,
            Type = AgentTaskEventType.Completed,
            Detail = "retention event",
            At = at,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedTranscriptAsync(
        Guid sessionId, long sequence, string kind, DateTime createdAt)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = id,
            AgentSessionId = sessionId,
            Sequence = sequence,
            Kind = kind,
            Uuid = id.ToString("N"),
            Text = kind == TranscriptKinds.UserPrompt ? $"prompt {sequence}" : $"body {sequence}",
            CreatedAt = createdAt,
            Timestamp = createdAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedQueuedAsync(
        Guid sessionId,
        long sequence,
        QueuedMessageStatus status,
        QueuedMessageOrigin origin,
        DateTime createdAt,
        DateTime? settledAt = null,
        int deliveryAttempts = 0)
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = id,
            AgentSessionId = sessionId,
            Body = $"msg {sequence}",
            Status = status,
            Sequence = sequence,
            Origin = origin,
            ConversationKey = origin == QueuedMessageOrigin.Channel ? "telegram:-1001" : null,
            CreatedAt = createdAt,
            SentAt = status == QueuedMessageStatus.Sent ? createdAt : null,
            CanceledAt = status == QueuedMessageStatus.Canceled ? createdAt : null,
            ChannelReplySettledAt = settledAt,
            DeliveryAttempts = deliveryAttempts,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<bool> SessionExistsAsync(Guid sessionId)
    {
        await using var db = CreateContext();
        return await db.AgentSessions.AnyAsync(s => s.Id == sessionId);
    }

    private static async Task<bool> ExistsAsync(Guid transcriptId)
    {
        await using var db = CreateContext();
        return await db.TranscriptEntries.AnyAsync(t => t.Id == transcriptId);
    }

    private static async Task<bool> QueueExistsAsync(Guid messageId)
    {
        await using var db = CreateContext();
        return await db.SessionQueuedMessages.AnyAsync(m => m.Id == messageId);
    }

    private static async Task<bool> TaskExistsAsync(Guid taskId)
    {
        await using var db = CreateContext();
        return await db.AgentTasks.AnyAsync(t => t.Id == taskId);
    }

    private static async Task<bool> TaskEventExistsAsync(Guid eventId)
    {
        await using var db = CreateContext();
        return await db.AgentTaskEvents.AnyAsync(e => e.Id == eventId);
    }

    private static async Task CleanupAsync(string marker)
    {
        await using var db = CreateContext();
        await db.ChannelInbounds.Where(i => i.ConversationId == marker).ExecuteDeleteAsync();
        var sessionIds = await db.AgentSessions
            .Where(s => s.Cwd.EndsWith(marker))
            .Select(s => s.Id)
            .ToListAsync();
        if (sessionIds.Count > 0)
        {
            await db.TranscriptEntries.Where(t => sessionIds.Contains(t.AgentSessionId)).ExecuteDeleteAsync();
            await db.SessionQueuedMessages.Where(m => sessionIds.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
            await db.RemoteControlModalEpisodes.Where(e => sessionIds.Contains(e.SessionId)).ExecuteDeleteAsync();
        }

        var taskIds = await db.AgentTasks.Where(t => t.Title == marker).Select(t => t.Id).ToListAsync();
        if (taskIds.Count > 0)
        {
            // CARD-0508: intents and notifications hold Restrict FKs to the events and tasks
            // below, so they have to go first or the whole cleanup fails.
            var retirementIds = await db.TaskWorktreeRetirements.Where(r => taskIds.Contains(r.TaskId)).Select(r => r.Id).ToListAsync();
            if (retirementIds.Count > 0)
            {
                await db.TaskWorktreeRetirementAttempts.Where(a => retirementIds.Contains(a.RetirementId)).ExecuteDeleteAsync();
                await db.TaskWorktreeRetirements.Where(r => retirementIds.Contains(r.Id)).ExecuteDeleteAsync();
            }
            await db.AgentTaskDispatchWarningIntents.Where(i => taskIds.Contains(i.TaskId)).ExecuteDeleteAsync();
            await db.AgentTaskLandNotifications.Where(n => taskIds.Contains(n.TaskId)).ExecuteDeleteAsync();
            await db.AgentTaskEvents.Where(e => taskIds.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
            var depths = await db.AgentTasks
                .Where(t => t.Title == marker)
                .Select(t => t.Depth)
                .Distinct()
                .OrderByDescending(d => d)
                .ToListAsync();
            foreach (var depth in depths)
                await db.AgentTasks.Where(t => t.Title == marker && t.Depth == depth).ExecuteDeleteAsync();
        }

        await db.Agents.Where(a => a.Name == marker).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => s.Cwd.EndsWith(marker)).ExecuteDeleteAsync();
        await db.AuditRecords.Where(a => a.Summary.StartsWith(marker)).ExecuteDeleteAsync();
    }

    private static async Task<Guid> SeedAuditAsync(
        string marker, DateTime createdAt, string? fullContent, string summarySuffix = "")
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        db.AuditRecords.Add(new AuditRecord
        {
            Id = id,
            EventType = AuditEventType.LlmCall,
            ModelName = "test-model",
            TokensIn = 10,
            TokensOut = 20,
            CostUsd = 0.001m,
            DurationMs = 100,
            Summary = $"{marker}{summarySuffix}",
            FullContent = fullContent,
            CreatedAt = createdAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<AuditRecord?> GetAuditAsync(Guid id)
    {
        await using var db = CreateContext();
        return await db.AuditRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
    }
}
