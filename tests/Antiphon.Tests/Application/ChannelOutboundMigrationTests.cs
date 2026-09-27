using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundMigrationTests
{
    [Test]
    public async Task Upgrade_preserves_history_and_defaults()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var taskId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.AgentTasks.Add(new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = "legacy", Goal = "original goal",
            WorkingDirectory = Path.GetTempPath(), CreatedAt = now,
            Result = "historical report", Status = AgentTaskStatus.Succeeded,
        });
        await db.SaveChangesAsync();

        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var position = Array.FindIndex(migrations,
            m => m.EndsWith("_AddChannelOutboundProfile", StringComparison.Ordinal));
        position.ShouldBeGreaterThan(0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[position - 1]);
        var external = "legacy-" + channelId.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ChatChannels" ("Id", "Provider", "ExternalId", "Kind", "Enabled",
                "MessageCount", "CreatedAt", "UpdatedAt", "ReplyHandle")
            VALUES ({channelId}, 'slack', {external}, 0, true, 0, {now}, {now}, 'old-thread')
            """);
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var channel = await db.ChatChannels.SingleAsync(c => c.Id == channelId);
        channel.ExternalId.ShouldBe(external);
        channel.ReplyHandle.ShouldBe("old-thread");
        channel.OutboundAgentProfile.ShouldBeNull();
        var legacyTask = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        legacyTask.Result.ShouldBe("historical report");
        legacyTask.OutboundDeliveryId.ShouldBeNull();
        (await db.ChannelOutboundDeliveries.CountAsync()).ShouldBe(0);

        var deliveryId = Guid.NewGuid();
        db.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
        {
            Id = deliveryId, SourceKey = new string('a', 64), ChannelId = channelId,
            ProjectId = Guid.NewGuid(), InboundAgentId = Guid.NewGuid(),
            SourceSessionId = Guid.NewGuid(), SendKind = "main", ProfileName = "profile",
            ConverterAgentId = Guid.NewGuid(), PromptRevision = new string('b', 64),
            PromptText = "prompt", Trigger = "EveryAgentReply", InputPath = "frozen.json",
            InputSha256 = new string('c', 64), CreatedAt = now, DeadlineAt = now.AddMinutes(2),
        });
        await db.SaveChangesAsync();
        legacyTask.OutboundDeliveryId = deliveryId;
        await db.SaveChangesAsync();

        await using (var duplicate = new AppDbContext(options))
        {
            duplicate.ChannelOutboundDeliveries.Add(new ChannelOutboundDelivery
            {
                Id = Guid.NewGuid(), SourceKey = new string('a', 64), ChannelId = channelId,
                ProjectId = Guid.NewGuid(), InboundAgentId = Guid.NewGuid(),
                SourceSessionId = Guid.NewGuid(), SendKind = "main", ProfileName = "profile",
                ConverterAgentId = Guid.NewGuid(), PromptRevision = new string('b', 64),
                PromptText = "prompt", Trigger = "EveryAgentReply", InputPath = "other.json",
                InputSha256 = new string('c', 64), CreatedAt = now, DeadlineAt = now.AddMinutes(2),
            });
            await Should.ThrowAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }
        await using (var duplicateTask = new AppDbContext(options))
        {
            duplicateTask.AgentTasks.Add(new AgentTask
            {
                Id = Guid.NewGuid(), RootTaskId = Guid.NewGuid(), Title = "other", Goal = "other",
                WorkingDirectory = Path.GetTempPath(), CreatedAt = now,
                OutboundDeliveryId = deliveryId,
            });
            await Should.ThrowAsync<DbUpdateException>(() => duplicateTask.SaveChangesAsync());
        }
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == taskId))
            .OutboundDeliveryId.ShouldBe(deliveryId);
    }
}
