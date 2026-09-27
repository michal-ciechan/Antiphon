using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Confluent.Kafka;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundRecoveryTests
{
    [Test]
    public async Task Definite_queue_refusal_retries_sealed_payload_but_ambiguous_failure_does_not()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-retries-" + Guid.NewGuid().ToString("N"));
        var files = new ChannelOutboundFileStore(root);
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var producer = new ScriptedProducer();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        db.Projects.Add(new Project { Id = projectId, Name = "retries-" + projectId.ToString("N"),
            CreatedAt = now, UpdatedAt = now });
        db.Boards.Add(new Board { Id = boardId, Name = "retries", ProjectId = projectId,
            CreatedAt = now, UpdatedAt = now });
        db.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "retries",
            Slug = "retries-" + agentId.ToString("N"), WorkingDirectory = root });
        db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
            ExternalId = channelId.ToString("N"), AgentId = agentId,
            CreatedAt = now, UpdatedAt = now });
        var deliveries = new List<ChannelOutboundDelivery>();
        foreach (var (index, body) in new[] { "retry-success", "retry-exhaust", "ambiguous" }
                     .Select((body, index) => (index, body)))
        {
            var id = Guid.NewGuid();
            var snapshot = await files.StageAsync(id, new ChannelReply
            {
                Channel = "fake", ConversationId = channelId.ToString("N"), Text = body,
            }, CancellationToken.None);
            deliveries.Add(new ChannelOutboundDelivery
            {
                Id = id, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = channelId,
                ProjectId = projectId, InboundAgentId = agentId, SourceSessionId = Guid.NewGuid(),
                SendKind = "main", ProfileName = "", PromptRevision = new string('a', 64),
                InputPath = snapshot.ReplyPath, InputSha256 = snapshot.ReplySha256,
                Trigger = "Passthrough", State = ChannelOutboundDeliveryState.Ready,
                CreatedAt = now.AddMilliseconds(index), DeadlineAt = now.AddMinutes(2),
            });
        }
        db.ChannelOutboundDeliveries.AddRange(deliveries);
        await db.SaveChangesAsync();
        try
        {
            var pump = new ChannelOutboundDeliveryPump(db, null!, files, producer,
                Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                NullLogger<ChannelOutboundDeliveryPump>.Instance);
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(3);
            var rows = await db.ChannelOutboundDeliveries.AsNoTracking()
                .Where(d => d.ChannelId == channelId).ToListAsync();
            rows.Single(d => d.Id == deliveries[0].Id).State.ShouldBe(ChannelOutboundDeliveryState.Published);
            rows.Single(d => d.Id == deliveries[1].Id).State.ShouldBe(ChannelOutboundDeliveryState.Failed);
            rows.Single(d => d.Id == deliveries[2].Id).State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
            rows.Select(d => d.PublicationAttempts).ShouldBe(new[] { 3, 3, 1 }, ignoreOrder: true);
            producer.Attempts["retry-success"].ShouldBe(3);
            producer.Attempts["retry-exhaust"].ShouldBe(3);
            producer.Attempts["ambiguous"].ShouldBe(1);
            producer.Accepted.ShouldHaveSingleItem().Text.ShouldBe("retry-success");
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(0);
        }
        finally
        {
            await db.ChannelOutboundDeliveries.Where(d => d.ChannelId == channelId).ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => c.Id == channelId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == agentId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
            Directory.Delete(root, true);
        }
    }

    private sealed class ScriptedProducer : IAntiphonMessagingProducer
    {
        public Dictionary<string, int> Attempts { get; } = new(StringComparer.Ordinal);
        public List<ChannelReply> Accepted { get; } = [];

        public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default)
        {
            var body = reply.Text!;
            var attempt = Attempts.TryGetValue(body, out var previous) ? previous + 1 : 1;
            Attempts[body] = attempt;
            if (body == "retry-exhaust" || body == "retry-success" && attempt < 3)
                throw new ProduceException<string, string>(new Error(ErrorCode.Local_QueueFull,
                    "synthetic pre-acceptance queue refusal"), new DeliveryResult<string, string>());
            if (body == "ambiguous")
                throw new IOException("synthetic ambiguous broker result");
            Accepted.Add(reply);
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task Expired_publishing_lease_is_uncertain_until_explicit_retry()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-recovery-" + Guid.NewGuid().ToString("N"));
        var files = new ChannelOutboundFileStore(root);
        var deliveryId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var frozen = await files.StageAsync(deliveryId, new ChannelReply
        {
            Channel = "fake", ConversationId = channelId.ToString("N"), Text = "frozen answer",
        }, CancellationToken.None);
        var producer = new FakeAntiphonMessagingClient();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        db.Projects.Add(new Project { Id = projectId, Name = "recovery-" + projectId.ToString("N"),
            CreatedAt = now, UpdatedAt = now });
        db.Boards.Add(new Board { Id = boardId, Name = "recovery", ProjectId = projectId,
            CreatedAt = now, UpdatedAt = now });
        db.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "recovery",
            Slug = "recovery-" + agentId.ToString("N"), WorkingDirectory = root });
        db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
            ExternalId = channelId.ToString("N"), AgentId = agentId,
            CreatedAt = now, UpdatedAt = now });
        db.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
        {
            Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"),
            ChannelId = channelId, ProjectId = projectId, InboundAgentId = agentId,
            SourceSessionId = Guid.NewGuid(), SendKind = "main", ProfileName = "",
            PromptRevision = new string('a', 64), InputPath = frozen.ReplyPath,
            InputSha256 = frozen.ReplySha256, Trigger = "Passthrough",
            State = ChannelOutboundDeliveryState.Publishing,
            LeaseOwner = Guid.NewGuid(), LeaseUntil = now.AddSeconds(-1),
            CreatedAt = now, DeadlineAt = now.AddMinutes(2), PublicationAttempts = 1,
        });
        await db.SaveChangesAsync();
        try
        {
            var pump = new ChannelOutboundDeliveryPump(db, null!, files, producer,
                Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                NullLogger<ChannelOutboundDeliveryPump>.Instance);
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(1);
            var uncertain = await db.ChannelOutboundDeliveries.AsNoTracking()
                .SingleAsync(d => d.Id == deliveryId);
            uncertain.State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
            uncertain.PublishedAt.ShouldBeNull();
            producer.SentReplies.ShouldBeEmpty();
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(0);

            var service = new ChannelOutboundService(db, files, producer,
                Options.Create(new ChannelOutboundSettings()), TimeProvider.System);
            await service.RetryUncertainAsync(deliveryId, true, CancellationToken.None);
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(1);
            producer.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("frozen answer");
            var published = await db.ChannelOutboundDeliveries.AsNoTracking()
                .SingleAsync(d => d.Id == deliveryId);
            published.State.ShouldBe(ChannelOutboundDeliveryState.Published);
            published.PublicationAttempts.ShouldBe(2);
        }
        finally
        {
            await db.ChannelOutboundDeliveries.Where(d => d.Id == deliveryId).ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => c.Id == channelId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == agentId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
            Directory.Delete(root, true);
        }
    }
}
