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
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class ChannelOutboundDeliveryTests
{
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
            intent.State = ChannelOutboundDeliveryState.Ready;
            intent.ConversionOutcome = "Fallback";
            later.State = ChannelOutboundDeliveryState.Ready;
            later.ConversionOutcome = "Passthrough";
            await verify.SaveChangesAsync();

            var pump = new ChannelOutboundDeliveryPump(verify, null!, files, producer,
                Options.Create(new AntiphonMessagingOptions()), TimeProvider.System,
                NullLogger<ChannelOutboundDeliveryPump>.Instance);
            (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            producer.SentReplies.Count.ShouldBe(2);
            var sent = producer.SentReplies[0];
            sent.ReplyHandle.ShouldBe("thread-1");
            sent.Text.ShouldContain("Conversion unavailable; source files attached.");
            producer.SentReplies[1].ReplyHandle.ShouldBe("thread-2");
            producer.SentReplies[1].Text.ShouldBe("second answer");
            (await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == intent.Id))
                .State.ShouldBe(ChannelOutboundDeliveryState.Published);
            (await verify.SessionQueuedMessages.AsNoTracking().SingleAsync(m => m.Id == correlationId))
                .ChannelReplySettledAt.ShouldNotBeNull();

            var revokedReply = reply with { Text = "sources after revocation" };
            (await outbound.SendAsync(revokedReply, ChannelOutboundOrigin.AgentReply,
                new ChannelOutboundSource(sessionId, 3, 7, 8, "main", []), CancellationToken.None))
                .ShouldBe(ChannelOutboundSendOutcome.Deferred);
            var revoked = await verify.ChannelOutboundDeliveries
                .SingleAsync(d => d.ChannelId == channelId && d.PromptSequence == 3);
            channel.OutboundAgentProfile = null;
            revoked.State = ChannelOutboundDeliveryState.Ready;
            revoked.ConversionOutcome = "Converted";
            revoked.OutputPath = "obsolete-worker-output";
            revoked.OutputSha256 = new string('a', 64);
            await verify.SaveChangesAsync();
            (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            producer.SentReplies.Count.ShouldBe(2);
            (await pump.TickAsync(CancellationToken.None)).ShouldBeGreaterThan(0);
            producer.SentReplies[2].Text.ShouldBe("sources after revocation");
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
