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
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChannelOutboundRecoveryTests
{
    [Test]
    [Arguments("publishing-committed", ChannelOutboundDeliveryState.PublishUncertain, 0)]
    [Arguments("producer-accepted", ChannelOutboundDeliveryState.PublishUncertain, 1)]
    [Arguments("published-committed", ChannelOutboundDeliveryState.Published, 1)]
    public async Task Process_death_after_publication_commit_recovers_from_fresh_process(
        string barrier, ChannelOutboundDeliveryState expectedState, int expectedAcceptances)
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var storeRoot = Path.Combine(root, "store");
        var store = new ChannelOutboundFileStore(storeRoot);
        var frozen = await store.StageAsync(deliveryId, new ChannelReply
        {
            Channel = "fake", ConversationId = channelId.ToString("N"),
            ReplyHandle = "thread-1", Text = "crash-frozen-source",
        }, CancellationToken.None);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString)))
        {
            db.Projects.Add(new Project { Id = projectId, Name = "crash-" + projectId.ToString("N"),
                CreatedAt = now, UpdatedAt = now });
            db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "crash",
                CreatedAt = now, UpdatedAt = now });
            db.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "crash",
                Slug = "crash-" + agentId.ToString("N"), WorkingDirectory = root });
            db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
                ExternalId = channelId.ToString("N"), AgentId = agentId,
                CreatedAt = now, UpdatedAt = now });
            db.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
            {
                Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = channelId,
                ProjectId = projectId, InboundAgentId = agentId, SourceSessionId = Guid.NewGuid(),
                SendKind = "main", ProfileName = "", PromptRevision = new string('a', 64),
                InputPath = frozen.ReplyPath, InputSha256 = frozen.ReplySha256,
                Trigger = "Passthrough", State = ChannelOutboundDeliveryState.Ready,
                CreatedAt = now, DeadlineAt = now.AddMinutes(2),
            });
            await db.SaveChangesAsync();
        }

        var configPath = Path.Combine(root, "probe.json");
        var evidencePath = Path.Combine(root, "accepted.bin");
        var markerPath = Path.Combine(root, "barrier");
        var probeDll = Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe",
            "Antiphon.ChannelOutbound.Probe.dll");
        File.Exists(probeDll).ShouldBeTrue();
        Process? child = null;
        try
        {
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
            {
                ConnectionString = isolated.ConnectionString, StoreRoot = storeRoot,
                DeliveryId = deliveryId, EvidencePath = evidencePath,
                MarkerPath = markerPath, Barrier = barrier, ClockOffsetSeconds = 0,
            }));
            child = StartProbe(probeDll, configPath);
            var childPid = child.Id;
            var childStarted = child.StartTime;
            using (var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                while (!File.Exists(markerPath))
                {
                    child.HasExited.ShouldBeFalse("probe exited before its durable barrier");
                    await Task.Delay(25, watchdog.Token);
                }
            child.Id.ShouldBe(childPid);
            child.StartTime.ShouldBe(childStarted);
            child.Kill(entireProcessTree: true);
            using (var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                await child.WaitForExitAsync(watchdog.Token);
            child.Dispose();
            child = null;

            // The second launch receives only the original database and durable files.
            // It does not reconstruct or seed the expected state in memory.
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
            {
                ConnectionString = isolated.ConnectionString, StoreRoot = storeRoot,
                DeliveryId = deliveryId, EvidencePath = evidencePath,
                MarkerPath = markerPath, Barrier = (string?)null, ClockOffsetSeconds = 301,
            }));
            using var recovery = StartProbe(probeDll, configPath);
            using (var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                await recovery.WaitForExitAsync(watchdog.Token);
            recovery.ExitCode.ShouldBe(0);
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
            var row = await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);
            row.State.ShouldBe(expectedState);
            row.InputSha256.ShouldBe(frozen.ReplySha256);
            row.PublishedAt.HasValue.ShouldBe(expectedState == ChannelOutboundDeliveryState.Published);
            var accepted = File.Exists(evidencePath) ? await File.ReadAllBytesAsync(evidencePath) : [];
            (accepted.Length == 0 ? 0 : 1).ShouldBe(expectedAcceptances);
            if (expectedAcceptances == 1)
            {
                var count = BitConverter.ToInt32(accepted, 0);
                accepted.Length.ShouldBe(count + sizeof(int));
                var reply = JsonSerializer.Deserialize<ChannelReply>(accepted.AsSpan(sizeof(int)),
                    Antiphon.Messaging.MessagingJson.Options)!;
                reply.ReplyHandle.ShouldBe("thread-1");
                reply.Text.ShouldBe("crash-frozen-source");
            }
            (await verify.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == channelId))
                .LastReplyAt.HasValue.ShouldBe(expectedState == ChannelOutboundDeliveryState.Published);
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
                child.Dispose();
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static Process StartProbe(string dll, string config)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add(config);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start outbound probe.");
    }

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
