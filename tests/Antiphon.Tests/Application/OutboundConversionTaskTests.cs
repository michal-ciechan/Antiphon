using Antiphon.Messaging;
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
public sealed class OutboundConversionTaskTests
{
    [Test]
    public async Task Ordinary_creation_links_one_pinned_internal_worker_without_source_inheritance()
    {
        var root = Directory.CreateTempSubdirectory("c0418-purpose-").FullName;
        var files = new ChannelOutboundFileStore(Path.Combine(root, "outbound"));
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var now = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
        var deadline = now.AddMinutes(2);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        db.Projects.Add(new Project { Id = projectId, Name = "purpose-" + projectId.ToString("N"),
            CreatedAt = now, UpdatedAt = now });
        db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "purpose",
            CreatedAt = now, UpdatedAt = now });
        db.Agents.AddRange(
            new Agent { Id = inboundId, BoardId = boardId, Name = "inbound",
                Slug = "inbound-" + inboundId.ToString("N"), WorkingDirectory = root },
            new Agent { Id = converterId, BoardId = boardId, Name = "converter",
                Slug = "converter-" + converterId.ToString("N"), WorkingDirectory = root });
        db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
            ExternalId = channelId.ToString("N"), AgentId = inboundId,
            CreatedAt = now, UpdatedAt = now });
        var snapshot = await files.StageAsync(deliveryId, new ChannelReply
        {
            Channel = "fake", ConversationId = channelId.ToString("N"), Text = "source text",
        }, CancellationToken.None);
        var delivery = new ChannelOutboundDelivery
        {
            Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = channelId,
            ProjectId = projectId, InboundAgentId = inboundId, ConverterAgentId = converterId,
            SourceSessionId = Guid.NewGuid(), SourceTaskId = Guid.NewGuid(),
            SendKind = "main", ProfileName = "test", PromptRevision = new string('b', 64),
            PromptText = "Use the local converter tool.", Trigger = "MarkdownSources",
            InputPath = snapshot.ReplyPath, InputSha256 = snapshot.ReplySha256,
            State = ChannelOutboundDeliveryState.Pending, CreatedAt = now, DeadlineAt = deadline,
        };
        db.ChannelOutboundDeliveries.Add(delivery);
        await db.SaveChangesAsync();
        try
        {
            var tasks = new AgentTaskService(db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new DelegationSettings { AllowedRoots = [root] }),
                new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
                NullLogger<AgentTaskService>.Instance);
            var runner = new OutboundConversionTaskRunner(db, tasks);
            var taskId = await runner.CreateAsync(delivery, CancellationToken.None);
            (await runner.CreateAsync(delivery, CancellationToken.None)).ShouldBe(taskId);
            var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            task.OutboundDeliveryId.ShouldBe(deliveryId);
            task.ProjectId.ShouldBe(projectId);
            task.AgentId.ShouldBe(converterId);
            task.Kind.ShouldBe(AgentTaskKind.Worker);
            task.Role.ShouldBe(AgentTaskRole.Custom);
            task.Workspace.ShouldBe(WorkspaceMode.Shared);
            task.ReplyTo.ShouldBe(AgentTaskReplyTo.None);
            task.ParentSessionId.ShouldBeNull();
            task.ParentTaskId.ShouldBeNull();
            task.CardId.ShouldBeNull();
            task.ExecutionDeadlineAt.ShouldBe(deadline);
            task.MaxAttempts.ShouldBe(1);
            task.CommitOnSettle.ShouldBe(CommitOnSettlePolicy.Never);
            task.LaunchEnvOverrideJson.ShouldBe("{}");
            task.InheritedLaunchEnvJson.ShouldBe("{}");
            task.Goal.ShouldContain(snapshot.RequestPath);
            task.Goal.ShouldContain("Do not dispatch child tasks or send to a channel");
            task.Goal.ShouldContain("Use the local converter tool.");
            task.Goal.ShouldNotContain(delivery.SourceTaskId!.Value.ToString("D"));
            (await db.AgentTasks.AsNoTracking().CountAsync(t => t.OutboundDeliveryId == deliveryId))
                .ShouldBe(1);
            var stored = await db.ChannelOutboundDeliveries.AsNoTracking()
                .SingleAsync(d => d.Id == deliveryId);
            stored.ConversionTaskId.ShouldBe(taskId);
            stored.State.ShouldBe(ChannelOutboundDeliveryState.Converting);
        }
        finally
        {
            await db.AgentTasks.Where(t => t.OutboundDeliveryId == deliveryId).ExecuteDeleteAsync();
            await db.ChannelOutboundDeliveries.Where(d => d.Id == deliveryId).ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => c.Id == channelId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == inboundId || a.Id == converterId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
