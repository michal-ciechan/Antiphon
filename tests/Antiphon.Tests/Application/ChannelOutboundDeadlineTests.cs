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
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class ChannelOutboundDeadlineTests
{
    [Test]
    public async Task Deadline_crossing_the_final_creation_barrier_never_launches_a_worker()
    {
        var now = DateTime.UtcNow;
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        var root = Directory.CreateTempSubdirectory("c0418-precreate-deadline-").FullName;
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var files = new ChannelOutboundFileStore(Path.Combine(root, "outbound"));
        var producer = new FakeAntiphonMessagingClient();
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? tick = null;
        try
        {
            var snapshot = await files.StageAsync(deliveryId, new ChannelReply
            {
                Channel = "fake", ConversationId = channelId.ToString("N"), Text = "frozen source",
            }, CancellationToken.None);
            await using (var seed = new AppDbContext(options))
            {
                seed.Projects.Add(new Project { Id = projectId, Name = "deadline-" + projectId.ToString("N"),
                    CreatedAt = now, UpdatedAt = now });
                seed.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "deadline",
                    CreatedAt = now, UpdatedAt = now });
                seed.Agents.AddRange(
                    new Agent { Id = inboundId, BoardId = boardId, Name = "inbound",
                        Slug = "inbound-" + inboundId.ToString("N"), WorkingDirectory = root },
                    new Agent { Id = converterId, BoardId = boardId, Name = "converter",
                        Slug = "converter-" + converterId.ToString("N"), WorkingDirectory = root });
                seed.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
                    ExternalId = channelId.ToString("N"), AgentId = inboundId,
                    CreatedAt = now, UpdatedAt = now });
                seed.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
                {
                    Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = channelId,
                    ProjectId = projectId, InboundAgentId = inboundId, ConverterAgentId = converterId,
                    SourceSessionId = Guid.NewGuid(), SendKind = "main", ProfileName = "",
                    PromptRevision = new string('a', 64), PromptText = "Convert.",
                    Trigger = "EveryAgentReply", InputPath = snapshot.ReplyPath,
                    InputSha256 = snapshot.ReplySha256, State = ChannelOutboundDeliveryState.Pending,
                    CreatedAt = now, DeadlineAt = now.AddSeconds(30),
                });
                await seed.SaveChangesAsync();
            }

            await using var db = new AppDbContext(options);
            var tasks = new AgentTaskService(db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                Options.Create(new Antiphon.Server.Application.Settings.DelegationSettings { AllowedRoots = [root] }),
                new MockEventBus(), new RecordingSessionStopper(), clock,
                NullLogger<AgentTaskService>.Instance);
            var pump = new ChannelOutboundDeliveryPump(db, new OutboundConversionTaskRunner(db, tasks),
                files, producer, Options.Create(new Antiphon.Messaging.Client.AntiphonMessagingOptions()),
                clock, NullLogger<ChannelOutboundDeliveryPump>.Instance);
            pump.ProbeBarrierAsync = async (boundary, _, ct) =>
            {
                if (boundary != "before-conversion-create") return;
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            };
            tick = pump.TickAsync(CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            clock.Advance(TimeSpan.FromMinutes(1));
            release.TrySetResult();
            (await tick.WaitAsync(TimeSpan.FromSeconds(15))).ShouldBe(1);
            await using (var verify = new AppDbContext(options))
            {
                var delivery = await verify.ChannelOutboundDeliveries.AsNoTracking()
                    .SingleAsync(d => d.Id == deliveryId);
                delivery.State.ShouldBe(ChannelOutboundDeliveryState.Ready);
                delivery.ConversionTaskId.ShouldBeNull();
                delivery.FailureReason.ShouldContain("deadline elapsed before worker creation");
                (await verify.AgentTasks.AsNoTracking()
                    .CountAsync(t => t.OutboundDeliveryId == deliveryId)).ShouldBe(0);
            }
            (await pump.TickAsync(CancellationToken.None)).ShouldBe(1);
            producer.SentReplies.ShouldHaveSingleItem().Text.ShouldContain("frozen source");
            await using var published = new AppDbContext(options);
            (await published.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId))
                .State.ShouldBe(ChannelOutboundDeliveryState.Published);
        }
        finally
        {
            release.TrySetResult();
            if (tick is not null) try { await tick.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
            Directory.Delete(root, recursive: true);
        }
    }

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
