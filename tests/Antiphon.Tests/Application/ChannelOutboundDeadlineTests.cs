using Antiphon.Messaging;
using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Services;
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
public sealed class ChannelOutboundDeadlineTests
{
    [Test]
    public async Task Deadline_equality_cancels_only_queued_worker_and_ignores_late_success()
    {
        var now = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        var root = Directory.CreateTempSubdirectory("c0418-deadline-").FullName;
        var files = new ChannelOutboundFileStore(Path.Combine(root, "outbound"));
        var producer = new FakeAntiphonMessagingClient();
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var ownerSessionId = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        db.Projects.Add(new Project { Id = projectId, Name = "deadline-" + projectId.ToString("N"),
            CreatedAt = now, UpdatedAt = now });
        db.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "deadline",
            CreatedAt = now, UpdatedAt = now });
        db.Agents.Add(new Agent { Id = agentId, BoardId = boardId, Name = "deadline",
            Slug = "deadline-" + agentId.ToString("N"), WorkingDirectory = root });
        db.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
            ExternalId = channelId.ToString("N"), AgentId = agentId,
            CreatedAt = now, UpdatedAt = now });
        db.AgentSessions.Add(new AgentSession { Id = ownerSessionId, DefinitionName = "converter",
            Cwd = root, Status = SessionStatus.Running, CreatedAt = now, StartedAt = now,
            LastSeenAt = now });
        await db.SaveChangesAsync();
        var cases = new[] { (Status: AgentTaskStatus.Queued, Body: "queued", Session: (Guid?)null),
            (Status: AgentTaskStatus.Working, Body: "working", Session: (Guid?)ownerSessionId) };
        var deliveries = new List<Guid>();
        var tasks = new List<Guid>();
        try
        {
            foreach (var (index, item) in cases.Select((item, index) => (index, item)))
            {
                var deliveryId = Guid.NewGuid();
                var taskId = Guid.NewGuid();
                var source = new byte[] { (byte)(index + 1), 10, 255 };
                var snapshot = await files.StageAsync(deliveryId, new ChannelReply
                {
                    Channel = "fake", ConversationId = channelId.ToString("N"), Text = item.Body,
                    Attachments = [new OutboundAttachment { Kind = AttachmentKind.File,
                        Name = item.Body + ".md", Mime = "text/markdown", Content = source }],
                }, CancellationToken.None);
                db.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
                {
                    Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = channelId,
                    ProjectId = projectId, InboundAgentId = agentId, SourceSessionId = Guid.NewGuid(),
                    SendKind = "main", ProfileName = "", PromptRevision = new string('a', 64),
                    ConverterAgentId = agentId, Trigger = "EveryAgentReply",
                    InputPath = snapshot.ReplyPath, InputSha256 = snapshot.ReplySha256,
                    ConversionTaskId = taskId, State = ChannelOutboundDeliveryState.Converting,
                    CreatedAt = now.AddMilliseconds(index), DeadlineAt = now,
                });
                await db.SaveChangesAsync();
                db.AgentTasks.Add(new AgentTask
                {
                    Id = taskId, RootTaskId = taskId, OutboundDeliveryId = deliveryId,
                    Title = "conversion", Goal = "convert", Role = AgentTaskRole.Custom,
                    Status = item.Status, AgentId = agentId, AgentSessionId = item.Session,
                    ProjectId = projectId, WorkingDirectory = root, CreatedAt = now,
                });
                await db.SaveChangesAsync();
                deliveries.Add(deliveryId);
                tasks.Add(taskId);
            }

            var pump = new ChannelOutboundDeliveryPump(db, null!, files, producer,
                Options.Create(new Antiphon.Messaging.Client.AntiphonMessagingOptions()),
                clock, NullLogger<ChannelOutboundDeliveryPump>.Instance);
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(2);
            var prepared = await db.ChannelOutboundDeliveries.AsNoTracking()
                .Where(d => deliveries.Contains(d.Id)).ToListAsync();
            prepared.All(d => d.State == ChannelOutboundDeliveryState.Published).ShouldBeTrue();
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == tasks[0]))
                .Status.ShouldBe(AgentTaskStatus.Canceled);
            (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == tasks[1]))
                .Status.ShouldBe(AgentTaskStatus.Working);
            (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == ownerSessionId))
                .Status.ShouldBe(SessionStatus.Running);

            producer.SentReplies.Count.ShouldBe(2);
            producer.SentReplies.Select(r => r.Text!.StartsWith("queued") ? "queued" : "working")
                .ShouldBe(new[] { "queued", "working" });
            producer.SentReplies[0].Attachments.Single().Content.ShouldBe(new byte[] { 1, 10, 255 });
            producer.SentReplies[1].Attachments.Single().Content.ShouldBe(new byte[] { 2, 10, 255 });
            producer.SentReplies.All(r => r.Text!.Contains("original attachments retained")).ShouldBeTrue();

            await db.AgentTasks.Where(t => t.Id == tasks[1]).ExecuteUpdateAsync(s =>
                s.SetProperty(t => t.Status, AgentTaskStatus.Succeeded));
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(0);
            producer.SentReplies.Count.ShouldBe(2);
            (await db.ChannelOutboundDeliveries.AsNoTracking()
                .Where(d => deliveries.Contains(d.Id)).ToListAsync())
                .All(d => d.State == ChannelOutboundDeliveryState.Published).ShouldBeTrue();
        }
        finally
        {
            await db.AgentTasks.Where(t => tasks.Contains(t.Id)).ExecuteDeleteAsync();
            await db.ChannelOutboundDeliveries.Where(d => deliveries.Contains(d.Id)).ExecuteDeleteAsync();
            await db.AgentSessions.Where(s => s.Id == ownerSessionId).ExecuteDeleteAsync();
            await db.ChatChannels.Where(c => c.Id == channelId).ExecuteDeleteAsync();
            await db.Agents.Where(a => a.Id == agentId).ExecuteDeleteAsync();
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync();
            Directory.Delete(root, recursive: true);
        }
    }
}
