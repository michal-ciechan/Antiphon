using Antiphon.Messaging;
using Antiphon.Messaging.Client;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Messaging.Slack;
using Antiphon.Messaging.Tests.FakeSlack;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
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
using Testcontainers.Redpanda;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChannelOutboundRecoveryTests
{
    [Test]
    public async Task Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed()
    {
        var root = Directory.CreateTempSubdirectory("c0418-held-order-").FullName;
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var dbOptions = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var store = new ChannelOutboundFileStore(Path.Combine(root, "store"));
        var producer = new FakeAntiphonMessagingClient();
        try
        {
            var first = await store.StageAsync(firstId, new ChannelReply
            {
                Channel = "fake", ConversationId = channelId.ToString("N"),
                ReplyHandle = "T1", Text = "first held answer",
            }, CancellationToken.None);
            var second = await store.StageAsync(secondId, new ChannelReply
            {
                Channel = "fake", ConversationId = channelId.ToString("N"),
                ReplyHandle = "T2", Text = "later answer",
            }, CancellationToken.None);
            await using var db = new AppDbContext(dbOptions);
            db.Projects.Add(new Project { Id = projectId, Name = "held-" + projectId.ToString("N"),
                CreatedAt = now, UpdatedAt = now });
            db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "held",
                CreatedAt = now, UpdatedAt = now });
            db.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "held",
                Slug = "held-" + agentId.ToString("N"), WorkingDirectory = root });
            db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
                ExternalId = channelId.ToString("N"), AgentId = agentId,
                Enabled = false, ReplyHandle = "T2", CreatedAt = now, UpdatedAt = now });
            db.ChannelOutboundDeliveries.AddRange(
                new ChannelOutboundDelivery { Id = firstId, SourceKey = Guid.NewGuid().ToString("N"),
                    ChannelId = channelId, ProjectId = projectId, InboundAgentId = agentId,
                    SourceSessionId = Guid.NewGuid(), SendKind = "main", ProfileName = "",
                    PromptRevision = new string('a', 64), InputPath = first.ReplyPath,
                    InputSha256 = first.ReplySha256, Trigger = "Passthrough",
                    State = ChannelOutboundDeliveryState.Ready, CreatedAt = now,
                    DeadlineAt = now.AddMinutes(10) },
                new ChannelOutboundDelivery { Id = secondId, SourceKey = Guid.NewGuid().ToString("N"),
                    ChannelId = channelId, ProjectId = projectId, InboundAgentId = agentId,
                    SourceSessionId = Guid.NewGuid(), SendKind = "trailing", ProfileName = "",
                    PromptRevision = new string('a', 64), InputPath = second.ReplyPath,
                    InputSha256 = second.ReplySha256, Trigger = "Passthrough",
                    State = ChannelOutboundDeliveryState.Ready, CreatedAt = now.AddMilliseconds(1),
                    DeadlineAt = now.AddMinutes(10) });
            await db.SaveChangesAsync();
            var pump = new ChannelOutboundDeliveryPump(db, null!, store, producer,
                Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                NullLogger<ChannelOutboundDeliveryPump>.Instance);
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(2);
            producer.SentReplies.ShouldBeEmpty();
            (await db.ChannelOutboundDeliveries.AsNoTracking().CountAsync(d => d.ChannelId == channelId
                && d.State == ChannelOutboundDeliveryState.Held)).ShouldBe(2);
            var service = new ChannelOutboundService(db, store, producer,
                Options.Create(new ChannelOutboundSettings()), TimeProvider.System);
            var refusal = await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ConflictException>(
                () => service.ResumeHeldAsync(secondId, CancellationToken.None));
            refusal.Code.ShouldBe("channel_outbound_binding_held");
            await db.ChatChannels.Where(c => c.Id == channelId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Enabled, true));
            await service.ResumeHeldAsync(secondId, CancellationToken.None);
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(1);
            producer.SentReplies.ShouldBeEmpty();
            (await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == secondId))
                .State.ShouldBe(ChannelOutboundDeliveryState.Ready);
            await service.ResumeHeldAsync(firstId, CancellationToken.None);
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(2);
            producer.SentReplies.Select(r => (r.Text, r.ReplyHandle))
                .ShouldBe([("first held answer", "T1"), ("later answer", "T2")]);
            (await db.ChannelOutboundDeliveries.AsNoTracking().CountAsync(d => d.ChannelId == channelId
                && d.State == ChannelOutboundDeliveryState.Published)).ShouldBe(2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head()
    {
        var root = Directory.CreateTempSubdirectory("c0418-route-restart-").FullName;
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var dbOptions = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var store = new ChannelOutboundFileStore(Path.Combine(root, "store"));
        var evidencePath = Path.Combine(root, "sent.bin");
        var markerPath = Path.Combine(root, "barrier");
        var configPath = Path.Combine(root, "probe.json");
        var probeDll = Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe",
            "Antiphon.ChannelOutbound.Probe.dll");
        File.Exists(probeDll).ShouldBeTrue();
        Process? child = null;
        try
        {
            await using var fakeSlack = new FakeSlackServer();
            await fakeSlack.StartAsync();
            using var slackHttp = new HttpClient();
            var slack = new SlackChannelAdapter(slackHttp, new SlackSettings
            {
                ApiBaseUrl = fakeSlack.ApiBaseUrl, BotToken = fakeSlack.BotToken,
                AppToken = fakeSlack.AppToken, ErrorBackoffSeconds = 0,
            }, NullLogger<SlackChannelAdapter>.Instance);
            using var inboundTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await using var inbound = slack.ReceiveAsync(inboundTimeout.Token).GetAsyncEnumerator();
            fakeSlack.EnqueueMessage(channelId.ToString("N"), "U1", "prompt T1",
                threadTs: "1700000000.000100", ts: "1700000001.000200");
            (await inbound.MoveNextAsync()).ShouldBeTrue();
            var t1 = inbound.Current;
            fakeSlack.EnqueueMessage(channelId.ToString("N"), "U1", "prompt T2",
                threadTs: "1700001000.000300", ts: "1700001001.000400");
            (await inbound.MoveNextAsync()).ShouldBeTrue();
            var t2 = inbound.Current;
            var source = "# Frozen source\n"u8.ToArray();
            using var metadata = JsonDocument.Parse("""{"thread_marker":"T1","parse_mode":"MarkdownV2"}""");
            var firstReply = new ChannelReply
            {
                Channel = "slack", ConversationId = channelId.ToString("N"),
                ReplyHandle = t1.ReplyHandle, ReplyToMessageId = t1.ChannelMessageId,
                Kind = ChannelReplyKind.Question, RawOverrides = metadata.RootElement.Clone(),
                Text = "first frozen answer",
                Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                    Name = "source.md", Mime = "text/markdown", Caption = "original source",
                    Source = "fixture://source.md", Content = source }],
            };
            var secondReply = firstReply with { ReplyHandle = t2.ReplyHandle,
                ReplyToMessageId = t2.ChannelMessageId, Text = "second frozen answer",
                Attachments = [] };
            var first = await store.StageAsync(firstId, firstReply, CancellationToken.None);
            var second = await store.StageAsync(secondId, secondReply, CancellationToken.None);
            await using (var db = new AppDbContext(dbOptions))
            {
                db.Projects.Add(new Project { Id = projectId, Name = "route-" + projectId.ToString("N"),
                    CreatedAt = now, UpdatedAt = now });
                db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "route",
                    CreatedAt = now, UpdatedAt = now });
                db.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "route",
                    Slug = "route-" + agentId.ToString("N"), WorkingDirectory = root });
                db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "slack",
                    ExternalId = channelId.ToString("N"), AgentId = agentId,
                    ReplyHandle = t2.ReplyHandle, CreatedAt = now, UpdatedAt = now });
                db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "route",
                    Cwd = root, CreatedAt = now, StartedAt = now, LastSeenAt = now });
                db.ChannelOutboundDeliveries.AddRange(
                    new ChannelOutboundDelivery { Id = firstId, SourceKey = Guid.NewGuid().ToString("N"),
                        ChannelId = channelId, ProjectId = projectId, InboundAgentId = agentId,
                        SourceSessionId = sessionId, SendKind = "main", ProfileName = "",
                        PromptRevision = new string('a', 64), InputPath = first.ReplyPath,
                        InputSha256 = first.ReplySha256, Trigger = "Passthrough",
                        State = ChannelOutboundDeliveryState.Ready, CreatedAt = now,
                        DeadlineAt = now.AddMinutes(20) },
                    new ChannelOutboundDelivery { Id = secondId, SourceKey = Guid.NewGuid().ToString("N"),
                        ChannelId = channelId, ProjectId = projectId, InboundAgentId = agentId,
                        SourceSessionId = sessionId, SendKind = "trailing", ProfileName = "",
                        PromptRevision = new string('a', 64), InputPath = second.ReplyPath,
                        InputSha256 = second.ReplySha256, Trigger = "Passthrough",
                        State = ChannelOutboundDeliveryState.Ready, CreatedAt = now.AddMilliseconds(1),
                        DeadlineAt = now.AddMinutes(20) });
                await db.SaveChangesAsync();
                db.SessionQueuedMessages.Add(new SessionQueuedMessage
                {
                    Id = correlationId, AgentSessionId = sessionId, Body = "asked in T1",
                    Sequence = 1, Origin = QueuedMessageOrigin.Channel,
                    Status = QueuedMessageStatus.Sent, ConversationKey = "slack:" + channelId.ToString("N"),
                    ChannelOutboundDeliveryId = firstId, CreatedAt = now,
                });
                await db.SaveChangesAsync();
            }

            async Task WriteConfigAsync(string? barrier) =>
                await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
                {
                    ConnectionString = isolated.ConnectionString, StoreRoot = Path.Combine(root, "store"),
                    DeliveryId = firstId, EvidencePath = evidencePath, MarkerPath = markerPath,
                    Barrier = barrier, ClockOffsetSeconds = barrier is null ? 301 : 0,
                }));
            await WriteConfigAsync("publishing-committed");
            child = StartProbe(probeDll, configPath);
            using (var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                while (!File.Exists(markerPath))
                {
                    child.HasExited.ShouldBeFalse("publisher exited before the durable Publishing cut");
                    await Task.Delay(25, watchdog.Token);
                }
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            child.Dispose();
            child = null;

            await WriteConfigAsync(null);
            using (var recovery = StartProbe(probeDll, configPath))
            {
                using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await recovery.WaitForExitAsync(watchdog.Token);
                recovery.ExitCode.ShouldBe(0);
            }
            File.Exists(evidencePath).ShouldBeFalse();
            await using (var verify = new AppDbContext(dbOptions))
            {
                var head = await verify.ChannelOutboundDeliveries.AsNoTracking()
                    .SingleAsync(d => d.Id == firstId);
                head.State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
                head.PublishedAt.ShouldBeNull();
                (await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == secondId))
                    .State.ShouldBe(ChannelOutboundDeliveryState.Ready);
                (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
                    .ChannelReplySettledAt.ShouldBeNull();
                var service = new ChannelOutboundService(verify, store, new FakeAntiphonMessagingClient(),
                    Options.Create(new ChannelOutboundSettings()), TimeProvider.System);
                await service.RetryUncertainAsync(firstId, true, CancellationToken.None);
            }
            using (var resumed = StartProbe(probeDll, configPath))
            {
                using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await resumed.WaitForExitAsync(watchdog.Token);
                resumed.ExitCode.ShouldBe(0);
            }
            var bytes = await File.ReadAllBytesAsync(evidencePath);
            var replies = new List<ChannelReply>();
            for (var offset = 0; offset < bytes.Length;)
            {
                var length = BitConverter.ToInt32(bytes, offset);
                offset += sizeof(int);
                replies.Add(JsonSerializer.Deserialize<ChannelReply>(bytes.AsSpan(offset, length),
                    Antiphon.Messaging.MessagingJson.Options)!);
                offset += length;
            }
            replies.Count.ShouldBe(2);
            replies[0].Channel.ShouldBe("slack");
            replies[0].ConversationId.ShouldBe(channelId.ToString("N"));
            replies[0].ReplyHandle.ShouldBe(t1.ReplyHandle);
            replies[0].ReplyToMessageId.ShouldBe(t1.ChannelMessageId);
            replies[0].Kind.ShouldBe(ChannelReplyKind.Question);
            replies[0].RawOverrides!.Value.GetProperty("thread_marker").GetString().ShouldBe("T1");
            replies[0].Text.ShouldBe("first frozen answer");
            replies[0].Attachments.ShouldHaveSingleItem().Content.ShouldBe(source);
            replies[0].Attachments[0].Source.ShouldBe("fixture://source.md");
            replies[0].Attachments[0].Caption.ShouldBe("original source");
            replies[1].ReplyHandle.ShouldBe(t2.ReplyHandle);
            replies[1].ReplyToMessageId.ShouldBe(t2.ChannelMessageId);
            replies[1].Text.ShouldBe("second frozen answer");
            await using var final = new AppDbContext(dbOptions);
            (await final.ChannelOutboundDeliveries.AsNoTracking().CountAsync(d => d.ChannelId == channelId
                && d.State == ChannelOutboundDeliveryState.Published)).ShouldBe(2);
            (await final.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
                .ChannelReplySettledAt.ShouldNotBeNull();
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

    [Test]
    public async Task Broker_ack_before_process_death_remains_uncertain_without_replay()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-broker-crash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").Build();
        await broker.StartAsync();
        var address = broker.GetBootstrapAddress();
        var topic = "c0418-crash-" + Guid.NewGuid().ToString("N");
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = address }).Build())
            await admin.CreateTopicsAsync([new TopicSpecification
                { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var store = new ChannelOutboundFileStore(Path.Combine(root, "store"));
        var source = "# accepted before crash\n"u8.ToArray();
        var frozen = await store.StageAsync(deliveryId, new ChannelReply
        {
            Channel = "slack", ConversationId = "C0418",
            ReplyHandle = "C0418|1700000000.000100", Text = "broker crash source",
            Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                Name = "source.md", Mime = "text/markdown", Content = source }],
        }, CancellationToken.None);
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString)))
        {
            db.Projects.Add(new Project { Id = projectId, Name = "broker-crash-" + projectId.ToString("N"),
                CreatedAt = now, UpdatedAt = now });
            db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "broker-crash",
                CreatedAt = now, UpdatedAt = now });
            db.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "broker-crash",
                Slug = "broker-crash-" + agentId.ToString("N"), WorkingDirectory = root });
            db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "slack",
                ExternalId = "C0418", AgentId = agentId, CreatedAt = now, UpdatedAt = now });
            db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "broker-crash",
                Cwd = root, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            db.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
            {
                Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = channelId,
                ProjectId = projectId, InboundAgentId = agentId, SourceSessionId = sessionId,
                SendKind = "main", ProfileName = "", PromptRevision = new string('a', 64),
                InputPath = frozen.ReplyPath, InputSha256 = frozen.ReplySha256,
                Trigger = "Passthrough", State = ChannelOutboundDeliveryState.Ready,
                CreatedAt = now, DeadlineAt = now.AddMinutes(2),
            });
            await db.SaveChangesAsync();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = correlationId, AgentSessionId = sessionId, Body = "broker crash source",
                Sequence = 1, Origin = QueuedMessageOrigin.Channel,
                Status = QueuedMessageStatus.Sent, ConversationKey = "slack:C0418",
                ChannelOutboundDeliveryId = deliveryId, CreatedAt = now,
            });
            await db.SaveChangesAsync();
        }
        var configPath = Path.Combine(root, "probe.json");
        var markerPath = Path.Combine(root, "barrier");
        var probeDll = Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe",
            "Antiphon.ChannelOutbound.Probe.dll");
        File.Exists(probeDll).ShouldBeTrue();
        Process? child = null;
        try
        {
            async Task WriteConfigAsync(string? barrier, int offset) =>
                await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
                {
                    ConnectionString = isolated.ConnectionString, StoreRoot = Path.Combine(root, "store"),
                    DeliveryId = deliveryId, EvidencePath = Path.Combine(root, "unused.bin"),
                    MarkerPath = markerPath, Barrier = barrier, ClockOffsetSeconds = offset,
                    Mode = "broker", BootstrapServers = address, Topic = topic,
                }));
            await WriteConfigAsync("producer-accepted", 0);
            child = StartProbe(probeDll, configPath);
            var childPid = child.Id;
            var childStarted = child.StartTime;
            using (var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                while (!File.Exists(markerPath))
                {
                    child.HasExited.ShouldBeFalse("broker probe exited before the acceptance barrier");
                    await Task.Delay(25, watchdog.Token);
                }
            child.Id.ShouldBe(childPid);
            child.StartTime.ShouldBe(childStarted);
            child.Kill(entireProcessTree: true);
            using (var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                await child.WaitForExitAsync(watchdog.Token);
            child.Dispose();
            child = null;

            using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = address,
                GroupId = "c0418-crash-group-" + Guid.NewGuid().ToString("N"),
                AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false,
            }).Build();
            consumer.Subscribe(topic);
            var accepted = consumer.Consume(TimeSpan.FromSeconds(30));
            accepted.ShouldNotBeNull();
            accepted.Topic.ShouldBe(topic);
            accepted.Message.Key.ShouldBe("C0418");
            var reply = JsonSerializer.Deserialize<ChannelReply>(accepted.Message.Value,
                Antiphon.Messaging.MessagingJson.Options)!;
            reply.ReplyHandle.ShouldBe("C0418|1700000000.000100");
            reply.Attachments.ShouldHaveSingleItem().Content.ShouldBe(source);

            await WriteConfigAsync(null, 301);
            for (var i = 0; i < 2; i++)
            {
                using var recovery = StartProbe(probeDll, configPath);
                using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await recovery.WaitForExitAsync(watchdog.Token);
                recovery.ExitCode.ShouldBe(0);
            }
            consumer.Consume(TimeSpan.FromSeconds(2)).ShouldBeNull();
            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
            var delivery = await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);
            delivery.State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
            delivery.PublicationAttempts.ShouldBe(1);
            delivery.PublishedAt.ShouldBeNull();
            delivery.FailureReason.ShouldContain("unknown");
            (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
                .ChannelReplySettledAt.ShouldBeNull();
            (await verify.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == channelId))
                .LastReplyAt.ShouldBeNull();
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

    [Test]
    [Arguments("input-temporary-partial")]
    [Arguments("input-temporary-complete")]
    [Arguments("admission-committed")]
    [Arguments("conversion-task-committed")]
    [Arguments("conversion-dispatched")]
    public async Task Process_death_preserves_admission_and_one_owned_conversion_task(string cut)
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert the frozen Markdown source.");
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var storeRoot = Path.Combine(root, "store");
        var evidencePath = Path.Combine(root, "accepted.bin");
        var markerPath = Path.Combine(root, "barrier");
        var configPath = Path.Combine(root, "probe.json");
        var probeDll = Path.Combine(AppContext.BaseDirectory, "channel-outbound-probe",
            "Antiphon.ChannelOutbound.Probe.dll");
        File.Exists(probeDll).ShouldBeTrue();
        await using (var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString)))
        {
            db.Projects.Add(new Project { Id = projectId, Name = "admission-" + projectId.ToString("N"),
                CreatedAt = now, UpdatedAt = now });
            db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "admission",
                CreatedAt = now, UpdatedAt = now });
            db.Agents.AddRange(
                new Agent { Id = inboundId, BoardId = boardId, Name = "inbound",
                    Slug = "inbound-" + inboundId.ToString("N"), WorkingDirectory = root },
                new Agent { Id = converterId, BoardId = boardId, Name = "converter",
                    Slug = "converter-" + converterId.ToString("N"), WorkingDirectory = root });
            db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
                ExternalId = channelId.ToString("N"), AgentId = inboundId,
                OutboundAgentProfile = "crash-pdf", CreatedAt = now, UpdatedAt = now });
            db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "admission",
                Cwd = root, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = correlationId, AgentSessionId = sessionId, Body = "send sources",
                Sequence = 1, Origin = QueuedMessageOrigin.Channel,
                Status = QueuedMessageStatus.Sent, ConversationKey = "fake:" + channelId.ToString("N"),
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        async Task WriteConfigAsync(string mode, string? barrier, Guid deliveryId = default,
            int clockOffsetSeconds = 0, bool allowPublication = false)
        {
            await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
            {
                ConnectionString = isolated.ConnectionString, StoreRoot = storeRoot,
                DeliveryId = deliveryId, EvidencePath = evidencePath, MarkerPath = markerPath,
                Barrier = barrier, ClockOffsetSeconds = clockOffsetSeconds, Mode = mode,
                WorkspaceRoot = root, ProjectId = projectId, ConverterAgentId = converterId,
                ChannelId = channelId, SessionId = sessionId, CorrelationId = correlationId,
                AllowPublication = allowPublication,
            }));
        }

        async Task RunToExitAsync(string mode, Guid deliveryId = default, int offset = 0)
        {
            await WriteConfigAsync(mode, null, deliveryId, offset,
                allowPublication: mode == "prepare" && offset > 0);
            using var probe = StartProbe(probeDll, configPath);
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await probe.WaitForExitAsync(watchdog.Token);
            probe.ExitCode.ShouldBe(0);
        }

        async Task<Guid> ReadDeliveryIdAsync()
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
            return (await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync()).Id;
        }

        Process? child = null;
        Guid? dispatchedSessionId = null;
        try
        {
            var afterTaskCreation = cut is "conversion-task-committed" or "conversion-dispatched";
            if (afterTaskCreation)
                await RunToExitAsync("admit");
            var acceptedId = afterTaskCreation ? await ReadDeliveryIdAsync() : Guid.Empty;
            if (cut == "conversion-dispatched")
                await RunToExitAsync("prepare", acceptedId);
            await WriteConfigAsync(cut switch
                {
                    "conversion-task-committed" => "prepare",
                    "conversion-dispatched" => "dispatch",
                    _ => "admit",
                }, cut, acceptedId);
            child = StartProbe(probeDll, configPath);
            var childPid = child.Id;
            var childStarted = child.StartTime;
            using (var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                while (!File.Exists(markerPath))
                {
                    child.HasExited.ShouldBeFalse("probe exited before its admission/task barrier");
                    await Task.Delay(25, watchdog.Token);
                }
            if (cut == "conversion-dispatched")
                (await File.ReadAllTextAsync(markerPath)).ShouldStartWith(
                    $"dispatch-warning-claim-committed|{acceptedId:N}|");
            child.Id.ShouldBe(childPid);
            child.StartTime.ShouldBe(childStarted);
            child.Kill(entireProcessTree: true);
            using (var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                await child.WaitForExitAsync(watchdog.Token);
            child.Dispose();
            child = null;

            await using (var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString)))
            {
                var count = await verify.ChannelOutboundDeliveries.CountAsync();
                count.ShouldBe(cut.StartsWith("input-temporary-", StringComparison.Ordinal) ? 0 : 1);
                (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
                    .ChannelOutboundDeliveryId.HasValue.ShouldBe(count == 1);
                (await verify.AgentTasks.CountAsync(t => t.OutboundDeliveryId != null))
                    .ShouldBe(afterTaskCreation ? 1 : 0);
                if (cut == "conversion-dispatched")
                {
                    var dispatched = await verify.AgentTasks.AsNoTracking()
                        .SingleAsync(t => t.OutboundDeliveryId == acceptedId);
                    dispatched.Status.ShouldBe(AgentTaskStatus.Dispatched);
                    dispatched.AgentSessionId.ShouldNotBeNull();
                    dispatchedSessionId = dispatched.AgentSessionId;
                    dispatched.DispatchedAt.ShouldNotBeNull();
                    (await verify.AgentSessions.AsNoTracking()
                        .SingleAsync(s => s.Id == dispatched.AgentSessionId)).Status
                        .ShouldBe(SessionStatus.Starting);
                    (await verify.AgentTaskEvents.AsNoTracking().CountAsync(e =>
                        e.AgentTaskId == dispatched.Id && e.Type == AgentTaskEventType.Dispatched))
                        .ShouldBe(1);
                }
            }
            if (cut.StartsWith("input-temporary-", StringComparison.Ordinal))
                Directory.GetDirectories(storeRoot, ".stage-*", SearchOption.TopDirectoryOnly)
                    .Length.ShouldBe(1);
            if (cut == "input-temporary-partial")
            {
                var staged = Directory.GetDirectories(storeRoot, ".stage-*", SearchOption.TopDirectoryOnly).Single();
                File.Exists(Path.Combine(staged, "input", "attachment-001.md")).ShouldBeTrue();
                File.Exists(Path.Combine(staged, "request.json")).ShouldBeFalse();
                File.Exists(Path.Combine(staged, "reply.json")).ShouldBeFalse();
            }
            File.Exists(evidencePath).ShouldBeFalse();

            if (cut != "conversion-task-committed")
                await RunToExitAsync("admit");
            acceptedId = await ReadDeliveryIdAsync();
            await RunToExitAsync("prepare", acceptedId,
                afterTaskCreation ? 301 : 0);
            await using var final = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
            var intent = await final.ChannelOutboundDeliveries.AsNoTracking().SingleAsync();
            intent.Id.ShouldBe(acceptedId);
            intent.State.ShouldBe(afterTaskCreation
                ? ChannelOutboundDeliveryState.Published : ChannelOutboundDeliveryState.Converting);
            (await final.AgentTasks.AsNoTracking().CountAsync(t => t.OutboundDeliveryId == acceptedId))
                .ShouldBe(1);
            (await final.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
                .ChannelOutboundDeliveryId.ShouldBe(acceptedId);
            var frozen = await new ChannelOutboundFileStore(storeRoot).ReadReplyAsync(
                intent.InputPath, intent.InputSha256, CancellationToken.None);
            frozen.ReplyHandle.ShouldBe("thread-1");
            frozen.Attachments.ShouldHaveSingleItem().Content.ShouldBe("# crash source"u8.ToArray());
            File.Exists(evidencePath).ShouldBe(afterTaskCreation);
            if (cut == "conversion-dispatched")
            {
                var retained = await final.AgentTasks.AsNoTracking()
                    .SingleAsync(t => t.OutboundDeliveryId == acceptedId);
                retained.Status.ShouldBe(AgentTaskStatus.Dispatched);
                retained.AgentSessionId.ShouldBe(dispatchedSessionId);
                (await final.AgentSessions.AsNoTracking().CountAsync(s => s.Id == dispatchedSessionId))
                    .ShouldBe(1);
            }
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

    [Test]
    [Arguments("before-conversion-observation", ChannelOutboundDeliveryState.Published, 1)]
    [Arguments("ready-committed", ChannelOutboundDeliveryState.Published, 1)]
    [Arguments("publishing-committed", ChannelOutboundDeliveryState.PublishUncertain, 0)]
    [Arguments("producer-accepted", ChannelOutboundDeliveryState.PublishUncertain, 1)]
    [Arguments("published-committed", ChannelOutboundDeliveryState.Published, 1)]
    [Arguments("publication-commit-failed", ChannelOutboundDeliveryState.PublishUncertain, 1)]
    public async Task Recovery_preserves_settled_worker_and_publication_boundaries(
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
        var sessionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var storeRoot = Path.Combine(root, "store");
        var store = new ChannelOutboundFileStore(storeRoot);
        var hasSource = barrier is "published-committed" or "publication-commit-failed";
        var sourceTaskId = Guid.NewGuid();
        var sourceBytes = "# crash source\n"u8.ToArray();
        var bundleDir = Path.Combine(root, "bundle");
        var sourcePath = Path.Combine(bundleDir, "source.md");
        if (hasSource)
        {
            Directory.CreateDirectory(bundleDir);
            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            var sourceManifest = new DeliverableBundleService.SourceManifest(1, true,
                [new DeliverableBundleService.SourceMember("docs/source.md", "source.md", null,
                    sourceBytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sourceBytes))
                        .ToLowerInvariant())], []);
            await File.WriteAllTextAsync(Path.Combine(bundleDir, DeliverableBundleService.SourceManifestName),
                JsonSerializer.Serialize(sourceManifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        var frozen = await store.StageAsync(deliveryId, new ChannelReply
        {
            Channel = "fake", ConversationId = channelId.ToString("N"),
            ReplyHandle = "thread-1", Text = "crash-frozen-source",
            Attachments = hasSource ? [new OutboundAttachment
            {
                Kind = AttachmentKind.File, Name = "source.md", Mime = "text/markdown",
                Source = sourcePath, Content = sourceBytes,
            }] : [],
        }, CancellationToken.None);
        var hasSettledWorker = barrier is "before-conversion-observation" or "ready-committed";
        var workerTaskId = Guid.NewGuid();
        if (hasSettledWorker)
        {
            var pdf = "%PDF-1.4 crash-boundary"u8.ToArray();
            var output = frozen.OutputDirectory;
            await File.WriteAllBytesAsync(Path.Combine(output, "combined.pdf"), pdf);
            await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(new
            {
                version = 1, deliveryId, disposition = "converted",
                files = new[] { new { path = "combined.pdf", name = "combined.pdf",
                    mime = "application/pdf", length = pdf.Length,
                    sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pdf)).ToLowerInvariant() } },
            }));
        }
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
            db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "crash",
                Cwd = root, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            if (hasSource)
                db.AgentTasks.Add(new AgentTask
                {
                    Id = sourceTaskId, RootTaskId = sourceTaskId, ProjectId = projectId,
                    AgentId = agentId, Title = "Crash source", Goal = "Write source",
                    WorkingDirectory = root, RepoPath = root, Status = AgentTaskStatus.Succeeded,
                    DeliverableBundleDir = bundleDir, CreatedAt = now, CompletedAt = now,
                });
            var delivery = new ChannelOutboundDelivery
            {
                Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = channelId,
                ProjectId = projectId, InboundAgentId = agentId, SourceSessionId = sessionId,
                SendKind = "main", ProfileName = "", PromptRevision = new string('a', 64),
                InputPath = frozen.ReplyPath, InputSha256 = frozen.ReplySha256,
                Trigger = "Passthrough", State = hasSettledWorker
                    ? ChannelOutboundDeliveryState.Converting : ChannelOutboundDeliveryState.Ready,
                SourceTaskId = hasSource ? sourceTaskId : null,
                ConversionTaskId = null,
                CreatedAt = now, DeadlineAt = now.AddMinutes(hasSettledWorker ? 10 : 2),
            };
            db.ChannelOutboundDeliveries.Add(delivery);
            await db.SaveChangesAsync();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = correlationId, AgentSessionId = sessionId, Body = "crash source",
                Sequence = 1, Origin = QueuedMessageOrigin.Channel,
                Status = QueuedMessageStatus.Sent, ConversationKey = "fake:" + channelId.ToString("N"),
                ChannelOutboundDeliveryId = deliveryId, CreatedAt = now,
            });
            await db.SaveChangesAsync();
            if (hasSettledWorker)
            {
                db.AgentTasks.Add(new AgentTask
                {
                    Id = workerTaskId, RootTaskId = workerTaskId, ProjectId = projectId,
                    AgentId = agentId, Title = "Crash probe worker", Goal = "Write output manifest",
                    WorkingDirectory = root, RepoPath = root, Status = AgentTaskStatus.Succeeded,
                    OutboundDeliveryId = deliveryId, CreatedAt = now, CompletedAt = now,
                });
                await db.SaveChangesAsync();
                delivery.ConversionTaskId = workerTaskId;
                await db.SaveChangesAsync();
            }
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
                MarkerPath = markerPath,
                Barrier = barrier == "publication-commit-failed" ? "commit-failed" : barrier,
                FailPublishCommit = barrier == "publication-commit-failed",
                ClockOffsetSeconds = 0,
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
            if (barrier == "publication-commit-failed")
                File.Exists(Path.Combine(root, "commit-injected")).ShouldBeTrue(
                    "the probe must reach the database commit interceptor after acceptance");
            child.Id.ShouldBe(childPid);
            child.StartTime.ShouldBe(childStarted);
            child.Kill(entireProcessTree: true);
            using (var exitWatchdog = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                await child.WaitForExitAsync(exitWatchdog.Token);
            child.Dispose();
            child = null;

            if (barrier == "publication-commit-failed")
            {
                await using var rolledBack = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));
                var pending = await rolledBack.ChannelOutboundDeliveries.AsNoTracking()
                    .SingleAsync(d => d.Id == deliveryId);
                pending.State.ShouldBe(ChannelOutboundDeliveryState.Publishing);
                pending.PublishedAt.ShouldBeNull();
                (await rolledBack.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
                    .ChannelReplySettledAt.ShouldBeNull();
                (await rolledBack.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == channelId))
                    .LastReplyAt.ShouldBeNull();
                (await rolledBack.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == sourceTaskId))
                    .DeliverableDeliveredAt.ShouldBeNull();
            }

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
                reply.Attachments.Count.ShouldBe((hasSettledWorker ? 1 : 0) + (hasSource ? 1 : 0));
                if (hasSource)
                    reply.Attachments[0].Content.ShouldBe(sourceBytes);
                if (hasSettledWorker)
                    reply.Attachments[0].Content.ShouldBe("%PDF-1.4 crash-boundary"u8.ToArray());
            }
            (await verify.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == channelId))
                .LastReplyAt.HasValue.ShouldBe(expectedState == ChannelOutboundDeliveryState.Published);
            (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
                .ChannelReplySettledAt.HasValue.ShouldBe(expectedState == ChannelOutboundDeliveryState.Published);
            if (hasSource)
                (await verify.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == sourceTaskId))
                    .DeliverableDeliveredAt.HasValue.ShouldBe(expectedState == ChannelOutboundDeliveryState.Published);
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
