using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class ChannelOutboundDurabilitySchemaTests
{
    [Test]
    public async Task C519_Upgrade_preserves_ordinals_and_history()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var position = Array.FindIndex(migrations,
            m => m.EndsWith("_ExtendChannelOutboundRecovery", StringComparison.Ordinal));
        position.ShouldBe(migrations.Length - 1);
        position.ShouldBeGreaterThan(0);
        migrations[position - 1].ShouldBe("20261004011911_CompletedCardWorktreeCleanup");
        var migrator = db.GetService<IMigrator>();

        // Exercise the entire empty-database migration chain, independently of the template.
        await migrator.MigrateAsync("0");
        await migrator.MigrateAsync();
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        var now = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var channel = NewChannel(now);
        channel.LastReplyAt = now.AddMinutes(-1);
        channel.LastReplyPreview = "historical accepted preview";
        db.ChatChannels.Add(channel);
        var session = new AgentSession { Id = Guid.NewGuid(), CreatedAt = now,
            StartedAt = now, LastSeenAt = now, Cwd = Path.GetTempPath() };
        db.AgentSessions.Add(session);
        var legacy = Enumerable.Range(0, 8).Select(ordinal => NewDelivery(channel.Id, session.Id,
            10 + ordinal, (ChannelOutboundDeliveryState)ordinal, capture: null)).ToArray();
        foreach (var delivery in legacy)
        {
            delivery.CreatedAt = now;
            delivery.DeadlineAt = now.AddMinutes(2);
            delivery.InputPath = "historical/frozen.json";
            delivery.InputSha256 = new string('a', 64);
            delivery.PublicationAttempts = 2;
            delivery.Version = 7;
        }
        legacy[4].PublishedAt = channel.LastReplyAt;
        // Historical Published without a timestamp must also stay out of new repair work.
        var publishedWithoutTimestamp = NewDelivery(channel.Id, session.Id, 99,
            ChannelOutboundDeliveryState.Published, capture: null);
        publishedWithoutTimestamp.CreatedAt = now;
        db.ChannelOutboundDeliveries.AddRange(legacy);
        db.ChannelOutboundDeliveries.Add(publishedWithoutTimestamp);
        var settled = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = session.Id,
            Body = "old settled channel", Sequence = 1, Status = QueuedMessageStatus.Sent,
            Origin = QueuedMessageOrigin.Channel, CreatedAt = now, SentAt = now,
            ChannelReplySettledAt = channel.LastReplyAt, ChannelOutboundDeliveryId = legacy[4].Id };
        var open = new SessionQueuedMessage { Id = Guid.NewGuid(), AgentSessionId = session.Id,
            Body = "old open machine", Sequence = 2, Status = QueuedMessageStatus.Sent,
            Origin = QueuedMessageOrigin.Delegation, CreatedAt = now, SentAt = now };
        db.SessionQueuedMessages.AddRange(settled, open);
        var task = new AgentTask { Id = Guid.NewGuid(), Title = "historical bundle",
            Goal = "original goal", WorkingDirectory = Path.GetTempPath(), CreatedAt = now,
            DeliverableDeliveredAt = channel.LastReplyAt };
        task.RootTaskId = task.Id;
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync(migrations[position - 1]);
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync();

        ChannelOutboundDeliveryState[] expected = [ChannelOutboundDeliveryState.Pending,
            ChannelOutboundDeliveryState.Converting, ChannelOutboundDeliveryState.Ready,
            ChannelOutboundDeliveryState.Publishing, ChannelOutboundDeliveryState.Published,
            ChannelOutboundDeliveryState.Held, ChannelOutboundDeliveryState.PublishUncertain,
            ChannelOutboundDeliveryState.Failed];
        for (var ordinal = 0; ordinal < expected.Length; ordinal++)
        {
            ((int)expected[ordinal]).ShouldBe(ordinal);
            var stored = await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == legacy[ordinal].Id);
            stored.State.ShouldBe(expected[ordinal]);
            stored.InputPath.ShouldBe("historical/frozen.json");
            stored.InputSha256.ShouldBe(new string('a', 64));
            stored.PublicationAttempts.ShouldBe(2);
            stored.Version.ShouldBe(7);
            stored.CaptureJson.ShouldBeNull();
            stored.RootDeliveryId.ShouldBeNull();
            stored.ReservedThroughSequence.ShouldBeNull();
            stored.TailClosedAt.ShouldBeNull();
            stored.NextAttemptAt.ShouldBeNull();
            stored.PreparationDeadlineAt.ShouldBeNull();
            stored.PreparationAttempts.ShouldBe(0);
            stored.PublicationAttemptBudgetBase.ShouldBe(0);
            stored.FailureEpisode.ShouldBe(0);
            stored.FailureReportedEpisode.ShouldBe(0);
            stored.MetadataAppliedAt.ShouldBe(ordinal == 4 ? channel.LastReplyAt : null);
        }
        ((int)ChannelOutboundDeliveryState.Captured).ShouldBe(8);
        ((int)ChannelOutboundDeliveryState.Suppressed).ShouldBe(9);
        (await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == publishedWithoutTimestamp.Id))
            .MetadataAppliedAt.ShouldBe(now);
        var storedChannel = await db.ChatChannels.SingleAsync(c => c.Id == channel.Id);
        storedChannel.LastReplyAt.ShouldBe(channel.LastReplyAt);
        storedChannel.LastReplyPreview.ShouldBe("historical accepted preview");
        (await db.AgentTasks.SingleAsync(t => t.Id == task.Id)).DeliverableDeliveredAt.ShouldBe(task.DeliverableDeliveredAt);
        var storedSettled = await db.SessionQueuedMessages.SingleAsync(m => m.Id == settled.Id);
        storedSettled.ChannelReplySettledAt.ShouldBe(settled.ChannelReplySettledAt);
        storedSettled.ChannelOutboundDeliveryId.ShouldBe(legacy[4].Id);
        storedSettled.ChannelReplyDiscoveryClosedAt.ShouldBeNull();
        var storedOpen = await db.SessionQueuedMessages.SingleAsync(m => m.Id == open.Id);
        storedOpen.ChannelReplySettledAt.ShouldBeNull();
        storedOpen.ChannelOutboundDeliveryId.ShouldBeNull();
        storedOpen.ChannelReplyDiscoveryClosedAt.ShouldBeNull();

        var captured = NewDelivery(channel.Id, session.Id, 200, ChannelOutboundDeliveryState.Captured);
        captured.ReservedThroughSequence = 212;
        captured.PreparationAttempts = 1;
        captured.PreparationDeadlineAt = now.AddMinutes(5);
        captured.NextAttemptAt = now.AddSeconds(30);
        captured.PublicationAttemptBudgetBase = 2;
        captured.FailureEpisode = 3;
        captured.FailureReportedEpisode = 2;
        captured.TailClosedAt = now.AddMinutes(6);
        var suppressed = NewDelivery(channel.Id, session.Id, 300, ChannelOutboundDeliveryState.Suppressed);
        db.ChannelOutboundDeliveries.AddRange(captured, suppressed);
        storedOpen.ChannelReplyDiscoveryClosedAt = now;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var reloaded = await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == captured.Id);
        reloaded.State.ShouldBe(ChannelOutboundDeliveryState.Captured);
        reloaded.CaptureJson.ShouldBe("{\"version\":1}");
        reloaded.InputPath.ShouldBeEmpty();
        reloaded.InputSha256.ShouldBeEmpty();
        reloaded.ReservedThroughSequence.ShouldBe(212);
        reloaded.PreparationAttempts.ShouldBe(1);
        reloaded.PreparationDeadlineAt.ShouldBe(now.AddMinutes(5));
        reloaded.NextAttemptAt.ShouldBe(now.AddSeconds(30));
        reloaded.PublicationAttemptBudgetBase.ShouldBe(2);
        reloaded.FailureEpisode.ShouldBe(3);
        reloaded.FailureReportedEpisode.ShouldBe(2);
        reloaded.TailClosedAt.ShouldBe(now.AddMinutes(6));
        reloaded.MetadataAppliedAt.ShouldBeNull();
        var storedSuppressed = await db.ChannelOutboundDeliveries.SingleAsync(d => d.Id == suppressed.Id);
        storedSuppressed.State.ShouldBe(ChannelOutboundDeliveryState.Suppressed);
        storedSuppressed.PublishedAt.ShouldBeNull();
        (await db.SessionQueuedMessages.SingleAsync(m => m.Id == open.Id)).ChannelReplyDiscoveryClosedAt.ShouldBe(now);
    }

    [Test]
    public async Task C519_Database_rejects_duplicate_roots()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var channel = NewChannel(DateTime.UtcNow);
        var otherChannel = NewChannel(DateTime.UtcNow);
        db.ChatChannels.AddRange(channel, otherChannel);
        var sessionId = Guid.NewGuid();
        var root = NewDelivery(channel.Id, sessionId, 10, ChannelOutboundDeliveryState.Captured);
        db.ChannelOutboundDeliveries.Add(root);
        await db.SaveChangesAsync();
        var duplicate = NewDelivery(channel.Id, sessionId, 10, ChannelOutboundDeliveryState.Captured);
        duplicate.SendKind = "machine"; // Main/machine precedence shares the same identity.
        duplicate.LastTextSequence = 50;
        await RejectInsertAsync(schema, duplicate, "IX_ChannelOutboundDeliveries_RootIdentity");
        db.ChannelOutboundDeliveries.AddRange(
            NewDelivery(otherChannel.Id, sessionId, 10, ChannelOutboundDeliveryState.Captured),
            NewDelivery(channel.Id, sessionId, 11, ChannelOutboundDeliveryState.Captured),
            NewDelivery(channel.Id, Guid.NewGuid(), 10, ChannelOutboundDeliveryState.Captured),
            NewDelivery(channel.Id, sessionId, 10, ChannelOutboundDeliveryState.Ready, capture: null),
            NewDelivery(channel.Id, sessionId, 10, ChannelOutboundDeliveryState.Ready, capture: null));
        await db.SaveChangesAsync();
        (await db.ChannelOutboundDeliveries.CountAsync()).ShouldBe(6);
        // The original exact-source constraint remains independent of root uniqueness.
        var sameSource = NewDelivery(otherChannel.Id, Guid.NewGuid(), 20, ChannelOutboundDeliveryState.Ready, capture: null);
        sameSource.SourceKey = root.SourceKey;
        await RejectInsertAsync(schema, sameSource, "IX_ChannelOutboundDeliveries_SourceKey");
    }

    [Test]
    public async Task C519_Database_rejects_duplicate_tail_starts()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var channel = NewChannel(DateTime.UtcNow);
        db.ChatChannels.Add(channel);
        var root = NewDelivery(channel.Id, Guid.NewGuid(), 10, ChannelOutboundDeliveryState.Captured);
        var otherRoot = NewDelivery(channel.Id, root.SourceSessionId, 20, ChannelOutboundDeliveryState.Captured);
        db.ChannelOutboundDeliveries.AddRange(root, otherRoot);
        await db.SaveChangesAsync();
        var tail = NewTail(root, 30, 35);
        db.ChannelOutboundDeliveries.Add(tail);
        await db.SaveChangesAsync();
        await RejectInsertAsync(schema, NewTail(root, 30, 40), "IX_ChannelOutboundDeliveries_TailStart");
        db.ChannelOutboundDeliveries.AddRange(NewTail(root, 36, 40), NewTail(otherRoot, 30, 40));
        await db.SaveChangesAsync();
        (await db.ChannelOutboundDeliveries.CountAsync(d => d.RootDeliveryId != null)).ShouldBe(3);
    }

    [Test]
    public async Task C519_Database_preserves_root_references()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var channel = NewChannel(DateTime.UtcNow);
        db.ChatChannels.Add(channel);
        var root = NewDelivery(channel.Id, Guid.NewGuid(), 10, ChannelOutboundDeliveryState.Captured);
        db.ChannelOutboundDeliveries.Add(root);
        await db.SaveChangesAsync();
        var tail = NewTail(root, 30, 35);
        db.ChannelOutboundDeliveries.Add(tail);
        await db.SaveChangesAsync();
        // Direct SQL bypasses EF's client-side relationship fixup/delete behavior.
        var error = await Should.ThrowAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"ChannelOutboundDeliveries\" WHERE \"Id\" = {root.Id}"));
        error.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        error.ConstraintName.ShouldBe("FK_ChannelOutboundDeliveries_ChannelOutboundDeliveries_RootDeliv~");
        (await db.ChannelOutboundDeliveries.AsNoTracking().AnyAsync(d => d.Id == root.Id)).ShouldBeTrue();
        (await db.ChannelOutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == tail.Id)).RootDeliveryId.ShouldBe(root.Id);
        await db.ChannelOutboundDeliveries.Where(d => d.Id == tail.Id).ExecuteDeleteAsync();
        (await db.ChannelOutboundDeliveries.Where(d => d.Id == root.Id).ExecuteDeleteAsync()).ShouldBe(1);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static ChatChannel NewChannel(DateTime now) => new()
    {
        Id = Guid.NewGuid(), Provider = "slack", ExternalId = Guid.NewGuid().ToString("N"),
        CreatedAt = now, UpdatedAt = now,
    };

    private static ChannelOutboundDelivery NewDelivery(Guid channelId, Guid sessionId, long prompt,
        ChannelOutboundDeliveryState state, string? capture = "{\"version\":1}") => new()
    {
        Id = Guid.NewGuid(), SourceKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
        ChannelId = channelId, ProjectId = Guid.NewGuid(), InboundAgentId = Guid.NewGuid(),
        SourceSessionId = sessionId, PromptSequence = prompt, FirstTextSequence = prompt + 1,
        LastTextSequence = prompt + 2, SendKind = "main", CaptureJson = capture, State = state,
        CreatedAt = DateTime.UtcNow, DeadlineAt = DateTime.UtcNow.AddMinutes(2),
    };

    private static ChannelOutboundDelivery NewTail(ChannelOutboundDelivery root, long first, long last)
    {
        var tail = NewDelivery(root.ChannelId, root.SourceSessionId, root.PromptSequence,
            ChannelOutboundDeliveryState.Captured);
        tail.RootDeliveryId = root.Id;
        tail.SendKind = "trailing";
        tail.FirstTextSequence = first;
        tail.LastTextSequence = last;
        return tail;
    }

    private static async Task RejectInsertAsync(IsolatedTestSchema schema,
        ChannelOutboundDelivery delivery, string constraint)
    {
        await using var db = CreateContext(schema);
        db.ChannelOutboundDeliveries.Add(delivery);
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = error.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe(constraint);
    }
}
