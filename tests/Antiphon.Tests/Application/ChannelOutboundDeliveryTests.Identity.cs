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

public sealed partial class ChannelOutboundDeliveryTests
{
    [Test]
    public async Task Two_dispatchers_preserve_source_window_and_destination_identity_under_admission_race()
    {
        var root = Directory.CreateTempSubdirectory("c0418-identity-").FullName;
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var xId = Guid.NewGuid();
        var yId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var store = new ChannelOutboundFileStore(Path.Combine(root, "store"));
        var producer = new FakeAntiphonMessagingClient();
        var settings = Options.Create(new ChannelOutboundSettings
        {
            Profiles = new Dictionary<string, ChannelOutboundProfile>
            {
                ["convert"] = new() { ProjectId = projectId, AgentId = converterId,
                    PromptFile = "convert.md", Trigger = ChannelOutboundTrigger.EveryAgentReply },
            },
        });
        await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert the frozen reply.");
        await using var firstDb = new AppDbContext(options);
        await using var secondDb = new AppDbContext(options);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ChannelOutboundSendOutcome>? firstCall = null;
        Task<ChannelOutboundSendOutcome>? secondCall = null;
        try
        {
            firstDb.Projects.Add(new Project { Id = projectId, Name = "identity-" + projectId.ToString("N"),
                CreatedAt = now, UpdatedAt = now });
            firstDb.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "identity",
                CreatedAt = now, UpdatedAt = now });
            firstDb.Agents.AddRange(
                new Agent { Id = inboundId, BoardId = boardId, Name = "inbound",
                    Slug = "inbound-" + inboundId.ToString("N"), WorkingDirectory = root },
                new Agent { Id = converterId, BoardId = boardId, Name = "converter",
                    Slug = "converter-" + converterId.ToString("N"), WorkingDirectory = root });
            foreach (var id in new[] { xId, yId })
                firstDb.ChatChannels.Add(new ChatChannel { Id = id, Provider = "fake",
                    ExternalId = id.ToString("N"), AgentId = inboundId,
                    OutboundAgentProfile = "convert", CreatedAt = now, UpdatedAt = now });
            firstDb.AgentSessions.Add(new AgentSession { Id = sessionId, DefinitionName = "identity",
                Cwd = root, CreatedAt = now, StartedAt = now, LastSeenAt = now });
            await firstDb.SaveChangesAsync();

            var first = new ChannelOutboundService(firstDb, store, producer, settings, TimeProvider.System);
            var second = new ChannelOutboundService(secondDb, store, producer, settings, TimeProvider.System);
            first.ProbeBarrierAsync = async (boundary, _, ct) =>
            {
                if (boundary != "admission-before-commit") return;
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            };
            var x = new ChannelReply { Channel = "fake", ConversationId = xId.ToString("N"),
                Text = "same frozen answer" };
            var source = new ChannelOutboundSource(sessionId, 10, 11, 12, "trailing", []);
            firstCall = first.SendAsync(x, ChannelOutboundOrigin.AgentReply, source, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            secondCall = second.SendAsync(x, ChannelOutboundOrigin.AgentReply, source, CancellationToken.None);
            await Task.Delay(100);
            secondCall.IsCompleted.ShouldBeFalse();
            await using (var observer = new AppDbContext(options))
                (await observer.ChannelOutboundDeliveries.CountAsync()).ShouldBe(0);
            release.TrySetResult();
            (await firstCall.WaitAsync(TimeSpan.FromSeconds(15))).ShouldBe(ChannelOutboundSendOutcome.Deferred);
            (await secondCall.WaitAsync(TimeSpan.FromSeconds(15))).ShouldBe(ChannelOutboundSendOutcome.Deferred);

            // The same source window can answer another destination, and a later
            // trailing window for X is a distinct reply even with identical bytes.
            (await second.SendAsync(x with { ConversationId = yId.ToString("N") },
                ChannelOutboundOrigin.AgentReply, source, CancellationToken.None))
                .ShouldBe(ChannelOutboundSendOutcome.Deferred);
            (await second.SendAsync(x, ChannelOutboundOrigin.AgentReply,
                source with { LastTextSequence = 13 }, CancellationToken.None))
                .ShouldBe(ChannelOutboundSendOutcome.Deferred);
            await using var verify = new AppDbContext(options);
            var deliveries = await verify.ChannelOutboundDeliveries.AsNoTracking().ToListAsync();
            deliveries.Count.ShouldBe(3);
            deliveries.Select(d => d.SourceKey).Distinct().Count().ShouldBe(3);
            deliveries.Count(d => d.ChannelId == xId).ShouldBe(2);
            deliveries.Count(d => d.ChannelId == yId).ShouldBe(1);
            deliveries.Select(d => d.InputSha256).Distinct().Count().ShouldBe(2);
            deliveries.ShouldAllBe(d => d.State == ChannelOutboundDeliveryState.Pending
                && d.PublishedAt == null && d.ConversionTaskId == null);
            producer.SentReplies.ShouldBeEmpty();
        }
        finally
        {
            release.TrySetResult();
            if (firstCall is not null) try { await firstCall.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
            if (secondCall is not null) try { await secondCall.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Two_pumps_create_one_linked_task_and_expired_lease_takeover_fences_old_owner()
    {
        var root = Directory.CreateTempSubdirectory("c0418-pump-race-").FullName;
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(isolated.ConnectionString);
        var now = DateTime.UtcNow;
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        var projectId = Guid.NewGuid();
        var boardId = Guid.NewGuid();
        var inboundId = Guid.NewGuid();
        var converterId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var files = new ChannelOutboundFileStore(Path.Combine(root, "store"));
        var producer = new FakeAntiphonMessagingClient();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? firstTick = null;
        try
        {
            var frozen = await files.StageAsync(deliveryId, new ChannelReply
            {
                Channel = "fake", ConversationId = channelId.ToString("N"), Text = "frozen",
            }, CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(root, "convert.md"), "Convert.");
            await using (var seed = new AppDbContext(options))
            {
                seed.Projects.Add(new Project { Id = projectId, Name = "pump-" + projectId.ToString("N"),
                    CreatedAt = now, UpdatedAt = now });
                seed.Boards.Add(new Board { Id = boardId, ProjectId = projectId, Name = "pump",
                    CreatedAt = now, UpdatedAt = now });
                seed.Agents.AddRange(
                    new Agent { Id = inboundId, BoardId = boardId, Name = "inbound",
                        Slug = "inbound-" + inboundId.ToString("N"), WorkingDirectory = root },
                    new Agent { Id = converterId, BoardId = boardId, Name = "converter",
                        Slug = "converter-" + converterId.ToString("N"), WorkingDirectory = root });
                seed.ChatChannels.Add(new ChatChannel { Id = channelId, Provider = "fake",
                    ExternalId = channelId.ToString("N"), AgentId = inboundId,
                    OutboundAgentProfile = "convert", CreatedAt = now, UpdatedAt = now });
                seed.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
                {
                    Id = deliveryId, SourceKey = Guid.NewGuid().ToString("N"), ChannelId = channelId,
                    ProjectId = projectId, InboundAgentId = inboundId, SourceSessionId = Guid.NewGuid(),
                    SendKind = "main", ProfileName = "convert", ConverterAgentId = converterId,
                    PromptRevision = new string('a', 64), PromptText = "Convert.",
                    Trigger = ChannelOutboundTrigger.EveryAgentReply.ToString(),
                    InputPath = frozen.ReplyPath, InputSha256 = frozen.ReplySha256,
                    State = ChannelOutboundDeliveryState.Pending, CreatedAt = now,
                    DeadlineAt = now.AddHours(1),
                });
                await seed.SaveChangesAsync();
            }
            await using var firstDb = new AppDbContext(options);
            await using var secondDb = new AppDbContext(options);
            OutboundConversionTaskRunner Runner(AppDbContext db)
            {
                var tasks = new AgentTaskService(db,
                    new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                    Options.Create(new DelegationSettings { AllowedRoots = [root] }),
                    new MockEventBus(), new RecordingSessionStopper(), clock,
                    NullLogger<AgentTaskService>.Instance);
                return new OutboundConversionTaskRunner(db, tasks);
            }
            var firstRunner = Runner(firstDb);
            firstRunner.ProbeBarrierAsync = async (boundary, _, ct) =>
            {
                if (boundary != "conversion-task-committed") return;
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            };
            var first = new ChannelOutboundDeliveryPump(firstDb, firstRunner, files, producer,
                Options.Create(new AntiphonMessagingOptions()), clock,
                NullLogger<ChannelOutboundDeliveryPump>.Instance);
            var second = new ChannelOutboundDeliveryPump(secondDb, Runner(secondDb), files, producer,
                Options.Create(new AntiphonMessagingOptions()), clock,
                NullLogger<ChannelOutboundDeliveryPump>.Instance);
            firstTick = first.TickAsync(CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            (await second.TickAsync(CancellationToken.None)).ShouldBe(0);
            await using (var verify = new AppDbContext(options))
            {
                var delivery = await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync();
                delivery.State.ShouldBe(ChannelOutboundDeliveryState.Converting);
                delivery.ConversionTaskId.ShouldNotBeNull();
                (await verify.AgentTasks.CountAsync(t => t.OutboundDeliveryId == deliveryId)).ShouldBe(1);
            }
            clock.Advance(TimeSpan.FromMinutes(6));
            (await second.TickAsync(CancellationToken.None)).ShouldBe(1);
            release.TrySetResult();
            (await firstTick.WaitAsync(TimeSpan.FromSeconds(15))).ShouldBe(1);
            await using (var verify = new AppDbContext(options))
            {
                var delivery = await verify.ChannelOutboundDeliveries.AsNoTracking().SingleAsync();
                delivery.State.ShouldBe(ChannelOutboundDeliveryState.Converting);
                delivery.ConversionTaskId.ShouldNotBeNull();
                delivery.LeaseOwner.ShouldBeNull();
                delivery.PublishedAt.ShouldBeNull();
                var tasks = await verify.AgentTasks.AsNoTracking()
                    .Where(t => t.OutboundDeliveryId == deliveryId).ToListAsync();
                tasks.ShouldHaveSingleItem().Id.ShouldBe(delivery.ConversionTaskId.Value);
            }
            (await first.TickAsync(CancellationToken.None)).ShouldBe(1);
            await using (var verify = new AppDbContext(options))
                (await verify.AgentTasks.CountAsync(t => t.OutboundDeliveryId == deliveryId)).ShouldBe(1);
            producer.SentReplies.ShouldBeEmpty();
        }
        finally
        {
            release.TrySetResult();
            if (firstTick is not null) try { await firstTick.WaitAsync(TimeSpan.FromSeconds(15)); } catch { }
            Directory.Delete(root, recursive: true);
        }
    }
}
