using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Gateway;
using Antiphon.Messaging.Slack;
using Antiphon.Messaging.Tests.FakeSlack;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Redpanda;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChannelOutboundComposedTransportTests
{
    [Test]
    public async Task Sealed_four_source_pdf_crosses_pump_broker_gateway_and_fake_slack()
    {
        var specimen = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "Fixtures", "Card0418"));
        var names = new[] { "requirements.md", "design.md", "external-api.md", "current-snapshots.md" };
        var sources = new List<(string Name, byte[] Bytes)>();
        foreach (var name in names)
            sources.Add((name, await File.ReadAllBytesAsync(Path.Combine(specimen, name))));
        var pdf = await File.ReadAllBytesAsync(Path.Combine(specimen, "combined.pdf"));
        Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant().ShouldBe(
            "5e6aaaf18d0e9b5f057ea351b517fd243ce15520495c1ebdfb88e9cbdf27a806");

        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-composed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        await broker.StartAsync();
        var topic = "c0418-composed-" + Guid.NewGuid().ToString("N");
        using (var admin = new AdminClientBuilder(new AdminClientConfig
               { BootstrapServers = broker.GetBootstrapAddress() }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification
                { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        await using var fake = new FakeSlackServer();
        await fake.StartAsync();
        using var http = new HttpClient();
        var slack = new SlackChannelAdapter(http, new SlackSettings
        {
            ApiBaseUrl = fake.ApiBaseUrl, BotToken = fake.BotToken,
            AppToken = fake.AppToken, ErrorBackoffSeconds = 0,
        }, NullLogger<SlackChannelAdapter>.Instance);
        var gateway = new GatewayOutboundService([slack], Options.Create(new AntiphonGatewayOptions
        {
            BootstrapServers = broker.GetBootstrapAddress(), OutboundTopic = topic,
            ConsumerGroup = "c0418-composed-group-" + Guid.NewGuid().ToString("N"),
            AutoOffsetReset = "Earliest",
        }), NullLogger<GatewayOutboundService>.Instance);
        await gateway.StartAsync(CancellationToken.None);
        try
        {
            var projectId = Guid.NewGuid();
            var boardId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var channelId = Guid.NewGuid();
            var deliveryId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var reply = new ChannelReply
            {
                Channel = "slack", ConversationId = "C0418",
                ReplyHandle = "C0418|1700000000.000100", Text = "Four verified sources",
                Attachments = sources.Select(source => new OutboundAttachment
                {
                    Kind = AttachmentKind.File, Name = source.Name,
                    Mime = "text/markdown", Content = source.Bytes,
                }).ToArray(),
            };
            var manifest = new DeliverableBundleService.SourceManifest(1, true,
                sources.Select(source => new DeliverableBundleService.SourceMember(
                    "docs/" + source.Name, source.Name, null, source.Bytes.Length,
                    Convert.ToHexString(SHA256.HashData(source.Bytes)).ToLowerInvariant())).ToArray(), []);
            var store = new ChannelOutboundFileStore(Path.Combine(root, "store"));
            var snapshot = await store.StageAsync(deliveryId, reply, CancellationToken.None,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var output = snapshot.OutputDirectory;
            await File.WriteAllBytesAsync(Path.Combine(output, "combined.pdf"), pdf);
            await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
            {
                version = 1, deliveryId, disposition = "converted",
                files = new[] { new { path = "combined.pdf", name = "combined.pdf",
                    mime = "application/pdf", length = pdf.Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant() } },
            }));
            var sealedReply = await store.ValidateAndSealAsync(deliveryId, snapshot.ReplyPath,
                snapshot.ReplySha256, 20 * 1024 * 1024, CancellationToken.None);
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString)))
            {
                db.Projects.Add(new Project { Id = projectId,
                    Name = "composed-" + projectId.ToString("N"), CreatedAt = now, UpdatedAt = now });
                db.Boards.Add(new Board { Id = boardId, ProjectId = projectId,
                    Name = "composed", CreatedAt = now, UpdatedAt = now });
                db.Agents.Add(new Agent { Id = agentId, BoardId = boardId,
                    Name = "composed", Slug = "composed-" + agentId.ToString("N"), WorkingDirectory = root });
                db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "slack",
                    ExternalId = "C0418", AgentId = agentId, CreatedAt = now, UpdatedAt = now });
                db.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
                {
                    Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"),
                    ChannelId = channelId, ProjectId = projectId, InboundAgentId = agentId,
                    SourceSessionId = Guid.NewGuid(), SendKind = "main", ProfileName = "",
                    PromptRevision = new string('a', 64), InputPath = snapshot.ReplyPath,
                    InputSha256 = snapshot.ReplySha256, OutputPath = sealedReply.ReplyPath,
                    OutputSha256 = sealedReply.ReplySha256, ConversionOutcome = sealedReply.Outcome,
                    Trigger = "Passthrough", State = ChannelOutboundDeliveryState.Ready,
                    CreatedAt = now, DeadlineAt = now.AddMinutes(2),
                });
                await db.SaveChangesAsync();
            }
            using (var producer = new KafkaAntiphonMessagingProducer(Options.Create(
                       new AntiphonMessagingOptions
                       { BootstrapServers = broker.GetBootstrapAddress(), OutboundTopic = topic })))
            await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString)))
            {
                var pump = new ChannelOutboundDeliveryPump(db, null!, store, producer,
                    Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                    NullLogger<ChannelOutboundDeliveryPump>.Instance);
                (await pump.TickAsync(CancellationToken.None)).ShouldBe(1);
                (await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId))
                    .State.ShouldBe(ChannelOutboundDeliveryState.Published);
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (fake.UploadedFiles.Count < 5)
            {
                deadline.Token.ThrowIfCancellationRequested();
                await Task.Delay(50, deadline.Token);
            }
            var sent = fake.SentMessages.ShouldHaveSingleItem();
            sent.Channel.ShouldBe("C0418");
            sent.ThreadTs.ShouldBe("1700000000.000100");
            var uploaded = fake.UploadedFiles;
            uploaded.Count.ShouldBe(5);
            for (var i = 0; i < 4; i++)
            {
                uploaded[i].Title.ShouldBe(sources[i].Name);
                uploaded[i].Bytes.ShouldBe(sources[i].Bytes);
                uploaded[i].ThreadTs.ShouldBe("1700000000.000100");
            }
            uploaded[4].Title.ShouldBe("combined.pdf");
            uploaded[4].Bytes.ShouldBe(pdf);
            uploaded[4].ThreadTs.ShouldBe("1700000000.000100");
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
            (await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId))
                .PublicationAttempts.ShouldBe(1);
        }
        finally
        {
            await gateway.StopAsync(CancellationToken.None);
            Directory.Delete(root, recursive: true);
        }
    }
}
