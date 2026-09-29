using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class ChannelOutboundDeliveryTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Source_publication_requires_all_four_members_or_a_complete_zip(
        bool zip, bool complete)
    {
        var root = Directory.CreateTempSubdirectory("c0418-source-publication-").FullName;
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var now = DateTime.UtcNow;
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var files = new ChannelOutboundFileStore(Path.Combine(root, "store"));
        var producer = new FakeAntiphonMessagingClient();
        try
        {
            var bundle = Path.Combine(root, "bundle");
            Directory.CreateDirectory(bundle);
            var names = new[] { "requirements.md", "design.md", "external-api.md", "snapshots.md" };
            var bytes = names.Select((name, i) => System.Text.Encoding.UTF8.GetBytes(
                $"# {name}\nunique final sentinel {i} żółw ✨\n")).ToArray();
            var members = new List<DeliverableBundleService.SourceMember>();
            var attachments = new List<OutboundAttachment>();
            if (zip)
            {
                var zipPath = Path.Combine(bundle, "sources.zip");
                await using (var stream = File.Create(zipPath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                    for (var i = 0; i < names.Length; i++)
                    {
                        if (!complete && i == names.Length - 1) continue;
                        await using var entry = archive.CreateEntry(names[i]).Open();
                        await entry.WriteAsync(bytes[i]);
                    }
                attachments.Add(new OutboundAttachment
                {
                    Kind = AttachmentKind.File, Name = "sources.zip", Mime = "application/zip",
                    Source = zipPath, Content = await File.ReadAllBytesAsync(zipPath),
                });
                for (var i = 0; i < names.Length; i++)
                    members.Add(new DeliverableBundleService.SourceMember(
                        "docs/" + names[i], "sources.zip", names[i], bytes[i].Length,
                        Convert.ToHexStringLower(SHA256.HashData(bytes[i]))));
            }
            else
            {
                for (var i = 0; i < names.Length; i++)
                {
                    var path = Path.Combine(bundle, names[i]);
                    await File.WriteAllBytesAsync(path, bytes[i]);
                    if (complete || i == 0)
                        attachments.Add(new OutboundAttachment
                        {
                            Kind = AttachmentKind.File, Name = names[i], Mime = "text/markdown",
                            Source = path, Content = bytes[i],
                        });
                    members.Add(new DeliverableBundleService.SourceMember(
                        "docs/" + names[i], names[i], null, bytes[i].Length,
                        Convert.ToHexStringLower(SHA256.HashData(bytes[i]))));
                }
            }
            await File.WriteAllTextAsync(Path.Combine(bundle, DeliverableBundleService.SourceManifestName),
                JsonSerializer.Serialize(new DeliverableBundleService.SourceManifest(1, true, members, []),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var original = new ChannelReply
            {
                Channel = "fake", ConversationId = channelId.ToString("N"),
                ReplyHandle = "T1", Text = "Four sources", Attachments = attachments,
            };
            var expectedWire = JsonSerializer.SerializeToUtf8Bytes(original,
                Antiphon.Messaging.MessagingJson.Options);
            var frozen = await files.StageAsync(deliveryId, original, CancellationToken.None);
            await using (var seed = new AppDbContext(options))
            {
                seed.Projects.Add(new Project { Id = projectId, Name = "members-" + projectId.ToString("N"),
                    CreatedAt = now, UpdatedAt = now });
                seed.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "members",
                    CreatedAt = now, UpdatedAt = now });
                seed.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "members",
                    Slug = "members-" + agentId.ToString("N"), WorkingDirectory = root });
                seed.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
                    ExternalId = channelId.ToString("N"), AgentId = agentId,
                    CreatedAt = now, UpdatedAt = now });
                seed.AgentTasks.Add(new AgentTask
                {
                    Id = taskId, RootTaskId = taskId, ProjectId = projectId, AgentId = agentId,
                    Title = "Four sources", Goal = "Write sources", WorkingDirectory = root,
                    RepoPath = root, Status = AgentTaskStatus.Succeeded,
                    DeliverableBundleDir = bundle, CreatedAt = now, CompletedAt = now,
                });
                seed.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
                {
                    Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"),
                    ChannelId = channelId, ProjectId = projectId, InboundAgentId = agentId,
                    SourceSessionId = Guid.NewGuid(), SourceTaskId = taskId, SendKind = "main",
                    ProfileName = "", PromptRevision = new string('a', 64),
                    InputPath = frozen.ReplyPath, InputSha256 = frozen.ReplySha256,
                    Trigger = "Passthrough", State = ChannelOutboundDeliveryState.Ready,
                    CreatedAt = now, DeadlineAt = now.AddMinutes(10),
                });
                await seed.SaveChangesAsync();
            }
            await using (var db = new AppDbContext(options))
            {
                var pump = new ChannelOutboundDeliveryPump(db, null!, files, producer,
                    Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                    NullLogger<ChannelOutboundDeliveryPump>.Instance);
                (await pump.TickAsync(CancellationToken.None)).ShouldBe(1);
            }
            var accepted = producer.SentReplies.ShouldHaveSingleItem();
            JsonSerializer.SerializeToUtf8Bytes(accepted, Antiphon.Messaging.MessagingJson.Options)
                .ShouldBe(expectedWire);
            accepted.Attachments.Count.ShouldBe(zip ? 1 : complete ? 4 : 1);
            await using var verify = new AppDbContext(options);
            (await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId))
                .State.ShouldBe(ChannelOutboundDeliveryState.Published);
            (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId))
                .DeliverableDeliveredAt.HasValue.ShouldBe(complete);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [Arguments("blocked-then-accepted")]
    [Arguments("two-refusals-then-accepted")]
    [Arguments("three-refusals")]
    [Arguments("ambiguous")]
    public async Task Only_acceptance_stamps_complete_actual_payload(string outcome)
    {
        var root = Directory.CreateTempSubdirectory("c0418-publication-").FullName;
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var now = DateTime.UtcNow;
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var files = new ChannelOutboundFileStore(Path.Combine(root, "store"));
        var producer = new PublicationOutcomeProducer(outcome);
        try
        {
            var sourceDir = Path.Combine(root, "bundle");
            Directory.CreateDirectory(sourceDir);
            var sourcePath = Path.Combine(sourceDir, "source.md");
            var sourceBytes = "# Frozen source\nżółw ✨\n"u8.ToArray();
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            await File.WriteAllTextAsync(Path.Combine(sourceDir,
                DeliverableBundleService.SourceManifestName), JsonSerializer.Serialize(
                    new DeliverableBundleService.SourceManifest(1, true,
                        [new DeliverableBundleService.SourceMember("docs/source.md", "source.md", null,
                            sourceBytes.Length, Convert.ToHexStringLower(SHA256.HashData(sourceBytes)))], []),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var original = new ChannelReply
            {
                Channel = "fake", ConversationId = channelId.ToString("N"),
                ReplyHandle = "frozen-T1", Text = "Original with escaped ✨ text",
                Attachments = [new OutboundAttachment
                {
                    Kind = AttachmentKind.File, Name = "source.md", Mime = "text/markdown",
                    Source = sourcePath, Content = sourceBytes,
                }],
            };
            var expectedWire = JsonSerializer.SerializeToUtf8Bytes(original, Antiphon.Messaging.MessagingJson.Options);
            var snapshot = await files.StageAsync(deliveryId, original, CancellationToken.None);
            await using (var seed = new AppDbContext(options))
            {
                seed.Projects.Add(new Project { Id = projectId, Name = "publish-" + projectId.ToString("N"),
                    CreatedAt = now, UpdatedAt = now });
                seed.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "publish",
                    CreatedAt = now, UpdatedAt = now });
                seed.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "publish",
                    Slug = "publish-" + agentId.ToString("N"), WorkingDirectory = root });
                seed.ChatChannels.Add(new ChatChannel
                {
                    Id = channelId, Provider = "fake", ExternalId = channelId.ToString("N"),
                    AgentId = agentId, ReplyHandle = "newer-T2", LastAuthor = "human",
                    LastChannelMessageId = "inbound-17", LastMessageAt = now.AddSeconds(-3),
                    CreatedAt = now, UpdatedAt = now,
                });
                seed.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "publish",
                    Cwd = root, CreatedAt = now, StartedAt = now, LastSeenAt = now });
                seed.AgentTasks.Add(new AgentTask
                {
                    Id = taskId, RootTaskId = taskId, ProjectId = projectId,
                    AgentId = agentId, Title = "Source", Goal = "Write source",
                    WorkingDirectory = root, RepoPath = root,
                    Status = AgentTaskStatus.Succeeded, DeliverableBundleDir = sourceDir,
                    CreatedAt = now, CompletedAt = now,
                });
                seed.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
                {
                    Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"),
                    ChannelId = channelId, ProjectId = projectId, InboundAgentId = agentId,
                    SourceSessionId = sessionId, SourceTaskId = taskId, SendKind = "main",
                    ProfileName = "", PromptRevision = new string('a', 64),
                    InputPath = snapshot.ReplyPath, InputSha256 = snapshot.ReplySha256,
                    Trigger = "Passthrough", State = ChannelOutboundDeliveryState.Ready,
                    CreatedAt = now, DeadlineAt = now.AddMinutes(10),
                });
                await seed.SaveChangesAsync();
                seed.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = correlationId, AgentSessionId = sessionId, Body = "asked in T1",
                    Sequence = 1, Origin = QueuedMessageOrigin.Channel,
                    Status = QueuedMessageStatus.Sent,
                    ConversationKey = "fake:" + channelId.ToString("N"),
                    ChannelOutboundDeliveryId = deliveryId, CreatedAt = now,
                });
                await seed.SaveChangesAsync();
            }

            await using var db = new AppDbContext(options);
            var pump = new ChannelOutboundDeliveryPump(db, null!, files, producer,
                Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                NullLogger<ChannelOutboundDeliveryPump>.Instance);
            Task<int>? running = null;
            try
            {
                if (outcome == "blocked-then-accepted")
                {
                    running = pump.TickAsync(CancellationToken.None);
                    await producer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    await AssertStampsAsync(options, deliveryId, channelId, correlationId, taskId,
                        ChannelOutboundDeliveryState.Publishing, false, 1, now);
                    producer.Accepted.ShouldBeEmpty();
                    producer.Release.TrySetResult();
                    (await running.WaitAsync(TimeSpan.FromSeconds(15))).ShouldBe(1);
                }
                else
                    (await pump.TickAsync(CancellationToken.None)).ShouldBe(1);
            }
            finally
            {
                producer.Release.TrySetResult();
                if (running is not null)
                    try { await running.WaitAsync(TimeSpan.FromSeconds(15)); }
                    catch { /* Keep the primary assertion, after releasing the producer. */ }
            }

            var accepted = outcome is "blocked-then-accepted" or "two-refusals-then-accepted";
            var expectedState = accepted ? ChannelOutboundDeliveryState.Published
                : outcome == "three-refusals" ? ChannelOutboundDeliveryState.Failed
                : ChannelOutboundDeliveryState.PublishUncertain;
            var attempts = outcome is "two-refusals-then-accepted" or "three-refusals" ? 3 : 1;
            producer.Attempts.ShouldBe(attempts);
            producer.Accepted.Count.ShouldBe(accepted ? 1 : 0);
            if (accepted)
                producer.Accepted.Single().ShouldBe(expectedWire);
            await AssertStampsAsync(options, deliveryId, channelId, correlationId, taskId,
                expectedState, accepted, attempts, now);
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(0);
            producer.Attempts.ShouldBe(attempts);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task AssertStampsAsync(DbContextOptions<AppDbContext> options,
        Guid deliveryId, Guid channelId, Guid correlationId, Guid taskId,
        ChannelOutboundDeliveryState state, bool accepted, int attempts, DateTime inboundAt)
    {
        await using var read = new AppDbContext(options);
        var delivery = await read.ChannelOutboundDeliveries.AsNoTracking()
            .SingleAsync(d => d.Id == deliveryId);
        delivery.State.ShouldBe(state);
        delivery.PublicationAttempts.ShouldBe(attempts);
        delivery.PublishedAt.HasValue.ShouldBe(accepted);
        (await read.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
            .ChannelReplySettledAt.HasValue.ShouldBe(accepted);
        var channel = await read.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == channelId);
        channel.LastReplyAt.HasValue.ShouldBe(accepted);
        channel.LastAuthor.ShouldBe("human");
        channel.LastChannelMessageId.ShouldBe("inbound-17");
        channel.LastMessageAt.ShouldNotBeNull();
        Math.Abs((channel.LastMessageAt.Value - inboundAt.AddSeconds(-3)).Ticks)
            .ShouldBeLessThan(10);
        channel.ReplyHandle.ShouldBe("newer-T2");
        (await read.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId))
            .DeliverableDeliveredAt.HasValue.ShouldBe(accepted);
    }

    private sealed class PublicationOutcomeProducer(string outcome) : IAntiphonMessagingProducer
    {
        public int Attempts { get; private set; }
        public List<byte[]> Accepted { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            Attempts++;
            Entered.TrySetResult();
            if (outcome == "blocked-then-accepted")
                await Release.Task.WaitAsync(cancellationToken);
            if ((outcome is "three-refusals" or "two-refusals-then-accepted") &&
                (outcome == "three-refusals" || Attempts <= 2))
                throw new ProduceException<string, string>(new Error(ErrorCode.Local_QueueFull,
                    "fixture queue refused before acceptance"), new DeliveryResult<string, string>());
            if (outcome == "ambiguous")
                throw new IOException("fixture producer gave no acceptance verdict");
            Accepted.Add(JsonSerializer.SerializeToUtf8Bytes(reply, Antiphon.Messaging.MessagingJson.Options));
        }
    }
}
