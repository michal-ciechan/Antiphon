using System.Security.Cryptography;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundDeliveryTests
{
    [Test]
    public async Task Expired_lease_takeover_fences_the_old_owner_before_producer_invocation()
    {
        var root = Directory.CreateTempSubdirectory("c0418-stale-producer-").FullName;
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        await using var firstDb = new AppDbContext(options);
        await using var secondDb = new AppDbContext(options);
        var now = DateTime.UtcNow;
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var files = new ChannelOutboundFileStore(Path.Combine(root, "store"));
        var producer = new FakeAntiphonMessagingClient();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? firstTick = null;
        try
        {
            var snapshot = await files.StageAsync(deliveryId, new ChannelReply
            {
                Channel = "fake", ConversationId = channelId.ToString("N"), Text = "frozen source",
            }, CancellationToken.None);
            firstDb.Projects.Add(new Project { Id = projectId, Name = "stale-" + projectId.ToString("N"),
                CreatedAt = now, UpdatedAt = now });
            firstDb.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "stale",
                CreatedAt = now, UpdatedAt = now });
            firstDb.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "stale",
                Slug = "stale-" + agentId.ToString("N"), WorkingDirectory = root });
            firstDb.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
                ExternalId = channelId.ToString("N"), AgentId = agentId,
                CreatedAt = now, UpdatedAt = now });
            firstDb.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
            {
                Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"),
                ChannelId = channelId, ProjectId = projectId, InboundAgentId = agentId,
                SourceSessionId = Guid.NewGuid(), SendKind = "main", ProfileName = "",
                PromptRevision = new string('a', 64), InputPath = snapshot.ReplyPath,
                InputSha256 = snapshot.ReplySha256, Trigger = "Passthrough",
                State = ChannelOutboundDeliveryState.Ready, CreatedAt = now,
                DeadlineAt = now.AddHours(1),
            });
            await firstDb.SaveChangesAsync();

            var first = new ChannelOutboundDeliveryPump(firstDb, null!, files, producer,
                Options.Create(new AntiphonMessagingOptions()), clock,
                NullLogger<ChannelOutboundDeliveryPump>.Instance);
            first.ProbeBarrierAsync = async (name, id, ct) =>
            {
                if (name != "before-producer-call" || id != deliveryId) return;
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            };
            var second = new ChannelOutboundDeliveryPump(secondDb, null!, files, producer,
                Options.Create(new AntiphonMessagingOptions()), clock,
                NullLogger<ChannelOutboundDeliveryPump>.Instance);
            firstTick = first.TickAsync(CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            producer.SentReplies.ShouldBeEmpty();
            (await second.TickAsync(CancellationToken.None)).ShouldBe(0);
            (await secondDb.ChannelOutboundDeliveries.AsNoTracking()
                .SingleAsync(d => d.Id == deliveryId)).State.ShouldBe(ChannelOutboundDeliveryState.Publishing);
            clock.Advance(TimeSpan.FromMinutes(6));
            (await second.TickAsync(CancellationToken.None)).ShouldBe(1);
            var taken = await secondDb.ChannelOutboundDeliveries.AsNoTracking()
                .SingleAsync(d => d.Id == deliveryId);
            taken.State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
            taken.PublicationAttempts.ShouldBe(1);
            taken.PublishedAt.ShouldBeNull();
            release.TrySetResult();
            await firstTick.WaitAsync(TimeSpan.FromSeconds(5));
            producer.SentReplies.ShouldBeEmpty();
            (await secondDb.ChannelOutboundDeliveries.AsNoTracking()
                .SingleAsync(d => d.Id == deliveryId)).State.ShouldBe(ChannelOutboundDeliveryState.PublishUncertain);
        }
        finally
        {
            release.TrySetResult();
            if (firstTick is not null)
                try { await firstTick.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception) { /* Preserve the assertion failure after releasing the fixture barrier. */ }
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Deferred_is_returned_only_after_intent_and_correlation_commit()
    {
        var root = Directory.CreateTempSubdirectory("c0418-admission-boundary-").FullName;
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var entered = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sendCancellation = new CancellationTokenSource();
        var producer = new FakeAntiphonMessagingClient();
        await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert sources.");
        var settings = Options.Create(new ChannelOutboundSettings
        {
            Profiles = new Dictionary<string, ChannelOutboundProfile>
            {
                ["convert"] = new() { ProjectId = projectId, AgentId = converterId,
                    PromptFile = "convert.md", Trigger = ChannelOutboundTrigger.EveryAgentReply },
            },
        });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        db.Projects.Add(new Project { Id = projectId, Name = "admission-" + projectId.ToString("N"),
            CreatedAt = now, UpdatedAt = now });
        db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "admission",
            CreatedAt = now, UpdatedAt = now });
        db.Agents.AddRange(
            new Agent { Id = inboundId, Name = "inbound", Slug = "inbound-" + inboundId.ToString("N"),
                BoardId = boardId, WorkingDirectory = root },
            new Agent { Id = converterId, Name = "converter", Slug = "converter-" + converterId.ToString("N"),
                BoardId = boardId, WorkingDirectory = root });
        db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "slack",
            ExternalId = channelId.ToString("N"), AgentId = inboundId,
            OutboundAgentProfile = "convert", CreatedAt = now, UpdatedAt = now });
        db.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "inbound", Cwd = root,
            CreatedAt = now, StartedAt = now, LastSeenAt = now });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = correlationId, AgentSessionId = sessionId, Body = "asked", Sequence = 1,
            Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Sent,
            ConversationKey = "slack:" + channelId.ToString("N"), CreatedAt = now,
        });
        await db.SaveChangesAsync();
        Task<ChannelOutboundSendOutcome>? pendingSend = null;
        try
        {
            var service = new ChannelOutboundService(db,
                new ChannelOutboundFileStore(Path.Combine(root, "outbound")), producer,
                settings, TimeProvider.System);
            service.ProbeBarrierAsync = async (boundary, id, ct) =>
            {
                if (boundary != "admission-before-commit") return;
                entered.TrySetResult(id);
                await release.Task.WaitAsync(ct);
            };
            pendingSend = service.SendAsync(new ChannelReply
            {
                Channel = "slack", ConversationId = channelId.ToString("N"), Text = "source answer",
            }, ChannelOutboundOrigin.AgentReply,
                new ChannelOutboundSource(sessionId, 1, 2, 3, "main", [correlationId]),
                sendCancellation.Token);
            var deliveryId = await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            pendingSend.IsCompleted.ShouldBeFalse();
            await using (var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                (await observer.ChannelOutboundDeliveries.AsNoTracking()
                    .CountAsync(d => d.Id == deliveryId)).ShouldBe(0);
                (await observer.SessionQueuedMessages.AsNoTracking()
                    .SingleAsync(m => m.Id == correlationId)).ChannelOutboundDeliveryId.ShouldBeNull();
            }
            producer.SentReplies.ShouldBeEmpty();
            release.TrySetResult();
            (await pendingSend.WaitAsync(TimeSpan.FromSeconds(15))).ShouldBe(ChannelOutboundSendOutcome.Deferred);
            await using (var observer = new AppDbContext(TestDbFixture.CreateDbContextOptions()))
            {
                var intent = await observer.ChannelOutboundDeliveries.AsNoTracking()
                    .SingleAsync(d => d.Id == deliveryId);
                intent.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
                intent.InputSha256.Length.ShouldBe(64);
                (await observer.SessionQueuedMessages.AsNoTracking()
                    .SingleAsync(m => m.Id == correlationId)).ChannelOutboundDeliveryId.ShouldBe(deliveryId);
            }
            producer.SentReplies.ShouldBeEmpty();
        }
        finally
        {
            release.TrySetResult();
            sendCancellation.Cancel();
            if (pendingSend is not null)
            {
                try { await pendingSend.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (Exception) { /* Preserve the original test failure and release fixture ownership. */ }
            }
            await db.SessionQueuedMessages.Where(m => m.Id == correlationId).ExecuteDeleteAsync();
            await db.ChannelOutboundDeliveries.Where(d => d.ChannelId == channelId).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => c.Id == channelId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == inboundId || a.Id == converterId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Concurrent_admission_respects_max_pending_per_channel()
    {
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var otherChannelId = Guid.NewGuid();
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert sources.");
        var options = Options.Create(new ChannelOutboundSettings
        {
            Profiles = new Dictionary<string, ChannelOutboundProfile>
            {
                ["limited"] = new()
                {
                    ProjectId = projectId, AgentId = converterId, PromptFile = "convert.md",
                    Trigger = ChannelOutboundTrigger.EveryAgentReply, MaxPending = 1,
                },
            },
        });
        await using var seed = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var now = DateTime.UtcNow;
        seed.Projects.Add(new Project { Id = projectId, Name = "outbound-" + projectId.ToString("N"),
            CreatedAt = now, UpdatedAt = now });
        seed.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "limit",
            CreatedAt = now, UpdatedAt = now });
        seed.Agents.AddRange(
            new Agent { Id = inboundId, Name = "inbound", Slug = "inbound-" + inboundId.ToString("N"),
                BoardId = boardId, WorkingDirectory = root },
            new Agent { Id = converterId, Name = "converter", Slug = "converter-" + converterId.ToString("N"),
                BoardId = boardId, WorkingDirectory = root });
        seed.ChatChannels.AddRange(
            new ChatChannel { Id = channelId, Provider = "slack", ExternalId = channelId.ToString("N"),
                AgentId = inboundId, OutboundAgentProfile = "limited", CreatedAt = now, UpdatedAt = now },
            new ChatChannel { Id = otherChannelId, Provider = "slack",
                ExternalId = otherChannelId.ToString("N"), AgentId = inboundId,
                OutboundAgentProfile = "limited", CreatedAt = now, UpdatedAt = now });
        await seed.SaveChangesAsync();
        try
        {
            var sourceSessionId = Guid.NewGuid();
            var sourceTaskId = Guid.NewGuid();
            async Task SendOneAsync(int sequence, Guid target, string kind = "main")
            {
                await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
                var service = new ChannelOutboundService(db, new ChannelOutboundFileStore(
                    Path.Combine(root, "outbound")), new FakeAntiphonMessagingClient(), options,
                    TimeProvider.System);
                var reply = new ChannelReply { Channel = "slack",
                    ConversationId = target.ToString("N"), Text = "reply " + sequence };
                (await service.SendAsync(reply, ChannelOutboundOrigin.AgentReply,
                    new ChannelOutboundSource(sourceSessionId, sequence, 1, 2, kind, [], sourceTaskId),
                    CancellationToken.None)).ShouldBe(ChannelOutboundSendOutcome.Deferred);
            }
            await Task.WhenAll(SendOneAsync(1, channelId), SendOneAsync(1, channelId));
            (await seed.ChannelOutboundDeliveries.AsNoTracking()
                .CountAsync(d => d.ChannelId == channelId)).ShouldBe(1);
            await SendOneAsync(2, channelId);
            await SendOneAsync(1, channelId, "trailing");
            await SendOneAsync(1, otherChannelId);
            var deliveries = await seed.ChannelOutboundDeliveries.AsNoTracking()
                .Where(d => d.ChannelId == channelId || d.ChannelId == otherChannelId).ToListAsync();
            deliveries.Count.ShouldBe(4);
            deliveries.Select(d => d.SourceKey).Distinct().Count().ShouldBe(4);
            deliveries.All(d => d.SourceTaskId == sourceTaskId).ShouldBeTrue();
            deliveries.Count(d => d.State == ChannelOutboundDeliveryState.Pending).ShouldBe(2);
            deliveries.Count(d => d.ConversionOutcome == "QueueOverflow").ShouldBe(2);
            deliveries.Single(d => d.SendKind == "trailing").ChannelId.ShouldBe(channelId);
        }
        finally
        {
            await seed.ChannelOutboundDeliveries.Where(d => d.ChannelId == channelId
                || d.ChannelId == otherChannelId).ExecuteDeleteAsync();
            await seed.ChatChannels.Where(c => c.Id == channelId || c.Id == otherChannelId)
                .ExecuteDeleteAsync();
            await seed.Agents.Where(a => a.Id == inboundId || a.Id == converterId).ExecuteDeleteAsync();
            await seed.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await seed.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Deferred_intent_freezes_route_and_only_publication_stamps_correlation()
    {
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundAgentId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var root = Path.Combine(Path.GetTempPath(), "antiphon-outbound-delivery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert the supplied sources.");
        var options = Options.Create(new ChannelOutboundSettings
        {
            Profiles = new Dictionary<string, ChannelOutboundProfile>
            {
                ["conversion"] = new()
                {
                    ProjectId = projectId, AgentId = converterId, PromptFile = "convert.md",
                    Trigger = ChannelOutboundTrigger.EveryAgentReply,
                },
            },
        });
        var producer = new FakeAntiphonMessagingClient();
        var files = new ChannelOutboundFileStore(Path.Combine(root, "outbound"));
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        var now = DateTime.UtcNow;
        db.Projects.Add(new Project { Id = projectId, Name = "outbound-" + projectId.ToString("N"),
            CreatedAt = now, UpdatedAt = now });
        db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "delivery",
            CreatedAt = now, UpdatedAt = now });
        db.Agents.AddRange(
            new Agent { Id = inboundAgentId, Name = "inbound", Slug = "inbound-" + inboundAgentId.ToString("N"),
                BoardId = boardId, WorkingDirectory = root },
            new Agent { Id = converterId, Name = "converter", Slug = "converter-" + converterId.ToString("N"),
                BoardId = boardId, WorkingDirectory = root });
        db.ChatChannels.Add(new ChatChannel
        {
            Id = channelId, Provider = "slack", ExternalId = channelId.ToString("N"),
            AgentId = inboundAgentId, ReplyHandle = "thread-2", OutboundAgentProfile = "conversion",
            CreatedAt = now, UpdatedAt = now,
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId, DefinitionName = "test", Cwd = root,
            CreatedAt = now, StartedAt = now, LastSeenAt = now,
        });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = correlationId, AgentSessionId = sessionId, Body = "asked", Sequence = 1,
            Origin = QueuedMessageOrigin.Channel, Status = QueuedMessageStatus.Sent,
            ConversationKey = "slack:" + channelId.ToString("N"), CreatedAt = now,
        });
        await db.SaveChangesAsync();

        try
        {
            var outbound = new ChannelOutboundService(db, files, producer, options, TimeProvider.System);
            var reply = new ChannelReply
            {
                Channel = "slack", ConversationId = channelId.ToString("N"),
                ReplyHandle = "thread-1", Text = "Here are the sources.",
            };
            var source = new ChannelOutboundSource(sessionId, 2, 3, 4, "main", [correlationId]);
            (await outbound.SendAsync(reply, ChannelOutboundOrigin.AgentReply, source,
                CancellationToken.None)).ShouldBe(ChannelOutboundSendOutcome.Deferred);
            (await outbound.SendAsync(reply, ChannelOutboundOrigin.AgentReply, source,
                CancellationToken.None)).ShouldBe(ChannelOutboundSendOutcome.Deferred);
            var second = reply with { ReplyHandle = "thread-2", Text = "second answer" };
            (await outbound.SendAsync(second, ChannelOutboundOrigin.AgentReply,
                new ChannelOutboundSource(sessionId, 2, 5, 6, "trailing", []),
                CancellationToken.None)).ShouldBe(ChannelOutboundSendOutcome.Deferred);
            producer.SentReplies.ShouldBeEmpty();

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var intents = await verify.ChannelOutboundDeliveries.Where(d => d.ChannelId == channelId)
                .OrderBy(d => d.CreatedAt).ToListAsync();
            intents.Count.ShouldBe(2);
            var intent = intents[0];
            var later = intents[1];
            intent.State.ShouldBe(ChannelOutboundDeliveryState.Pending);
            intent.PromptText.ShouldBe("Convert the supplied sources.");
            var row = await verify.SessionQueuedMessages.SingleAsync(m => m.Id == correlationId);
            row.ChannelOutboundDeliveryId.ShouldBe(intent.Id);
            row.ChannelReplySettledAt.ShouldBeNull();
            var channel = await verify.ChatChannels.SingleAsync(c => c.Id == channelId);
            channel.ReplyHandle = "thread-2";
            var tasks = new AgentTaskService(verify,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new DelegationSettings { AllowedRoots = [root] }),
                new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
                NullLogger<AgentTaskService>.Instance);
            var runner = new OutboundConversionTaskRunner(verify, tasks);
            var taskId = await runner.CreateAsync(intent, CancellationToken.None);
            (await runner.CreateAsync(intent, CancellationToken.None)).ShouldBe(taskId);
            var conversionTask = await verify.AgentTasks.SingleAsync(t => t.Id == taskId);
            conversionTask.OutboundDeliveryId.ShouldBe(intent.Id);
            conversionTask.Workspace.ShouldBe(WorkspaceMode.Shared);
            conversionTask.ReplyTo.ShouldBe(AgentTaskReplyTo.None);
            conversionTask.MaxAttempts.ShouldBe(1);
            conversionTask.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
            var outputDir = Path.Combine(Path.GetDirectoryName(intent.InputPath)!, "output");
            var pdf = new byte[] { 37, 80, 68, 70, 45, 49, 46, 55, 10 };
            await File.WriteAllBytesAsync(Path.Combine(outputDir, "combined.pdf"), pdf);
            await File.WriteAllTextAsync(Path.Combine(outputDir, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    version = 1, deliveryId = intent.Id, disposition = "converted",
                    replacementText = "converted answer",
                    files = new[] { new { path = "combined.pdf", name = "combined.pdf",
                        mime = "application/pdf", length = pdf.Length,
                        sha256 = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant() } },
                }));
            conversionTask.Status = AgentTaskStatus.Succeeded;
            conversionTask.CompletedAt = DateTime.UtcNow;
            later.State = ChannelOutboundDeliveryState.Ready;
            later.ConversionOutcome = "Passthrough";
            await verify.SaveChangesAsync();

            var pump = new ChannelOutboundDeliveryPump(verify, null!, files, producer,
                Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                NullLogger<ChannelOutboundDeliveryPump>.Instance, options);
            (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            producer.SentReplies.Count.ShouldBe(2);
            var sent = producer.SentReplies[0];
            sent.ReplyHandle.ShouldBe("thread-1");
            sent.Text.ShouldBe("converted answer");
            sent.Attachments.ShouldHaveSingleItem().Content.ShouldBe(pdf);
            producer.SentReplies[1].ReplyHandle.ShouldBe("thread-2");
            producer.SentReplies[1].Text.ShouldBe("second answer");
            (await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == intent.Id))
                .State.ShouldBe(ChannelOutboundDeliveryState.Published);
            (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
                .ChannelReplySettledAt.ShouldNotBeNull();

            var control = reply with { Text = "# Control notice", ReplyHandle = "control-thread" };
            (await outbound.SendAsync(control, ChannelOutboundOrigin.Control, source,
                CancellationToken.None)).ShouldBe(ChannelOutboundSendOutcome.Published);
            producer.SentReplies[2].ReplyHandle.ShouldBe("control-thread");
            (await verify.ChannelOutboundDeliveries.AsNoTracking()
                .CountAsync(d => d.ChannelId == channelId)).ShouldBe(2);

            var machine = reply with { Text = "# Machine completion", ReplyHandle = "machine-thread" };
            (await outbound.SendAsync(machine, ChannelOutboundOrigin.AgentReply,
                new ChannelOutboundSource(sessionId, 4, 9, 10, "machine", []),
                CancellationToken.None)).ShouldBe(ChannelOutboundSendOutcome.Deferred);
            var machineIntent = await verify.ChannelOutboundDeliveries
                .SingleAsync(d => d.ChannelId == channelId && d.PromptSequence == 4);
            machineIntent.SendKind.ShouldBe("machine");
            machineIntent.State = ChannelOutboundDeliveryState.Ready;
            machineIntent.ConversionOutcome = "Passthrough";
            await verify.SaveChangesAsync();
            (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            producer.SentReplies[3].ReplyHandle.ShouldBe("machine-thread");
            producer.SentReplies[3].Text.ShouldBe("# Machine completion");
            (await verify.ChannelOutboundDeliveries.AsNoTracking()
                .SingleAsync(d => d.Id == machineIntent.Id)).State.ShouldBe(ChannelOutboundDeliveryState.Published);

            var revokedReply = reply with { Text = "sources after revocation" };
            (await outbound.SendAsync(revokedReply, ChannelOutboundOrigin.AgentReply,
                new ChannelOutboundSource(sessionId, 3, 7, 8, "main", []), CancellationToken.None))
                .ShouldBe(ChannelOutboundSendOutcome.Deferred);
            var revoked = await verify.ChannelOutboundDeliveries
                .SingleAsync(d => d.ChannelId == channelId && d.PromptSequence == 3);
            (await verify.ChatChannels.SingleAsync(c => c.Id == channelId))
                .OutboundAgentProfile = null;
            revoked.State = ChannelOutboundDeliveryState.Ready;
            revoked.ConversionOutcome = "Converted";
            revoked.OutputPath = "obsolete-worker-output";
            revoked.OutputSha256 = new string('a', 64);
            await verify.SaveChangesAsync();
            (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            producer.SentReplies.Count.ShouldBe(4);
            (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            producer.SentReplies[4].Text.ShouldBe("sources after revocation");
            (await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == revoked.Id))
                .ConversionOutcome.ShouldBe("Revoked");

            var uncertain = await verify.ChannelOutboundDeliveries.SingleAsync(d => d.Id == later.Id);
            uncertain.State = ChannelOutboundDeliveryState.PublishUncertain;
            await verify.SaveChangesAsync();
            var retry = new ChannelOutboundService(verify, files, producer, options, TimeProvider.System);
            await Should.ThrowAsync<Antiphon.Server.Application.Exceptions.ValidationException>(() =>
                retry.RetryUncertainAsync(later.Id, false, CancellationToken.None));
            await retry.RetryUncertainAsync(later.Id, true, CancellationToken.None);
            (await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == later.Id))
                .State.ShouldBe(ChannelOutboundDeliveryState.Ready);
        }
        finally
        {
            await db.SessionQueuedMessages.Where(m => m.Id == correlationId).ExecuteDeleteAsync();
            await db.AgentTasks.Where(t => t.OutboundDeliveryId != null
                && db.ChannelOutboundDeliveries.Where(d => d.ChannelId == channelId)
                    .Select(d => d.Id).Contains(t.OutboundDeliveryId.Value)).ExecuteDeleteAsync();
            await db.ChannelOutboundDeliveries.Where(d => d.ChannelId == channelId).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => c.Id == channelId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == inboundAgentId || a.Id == converterId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
