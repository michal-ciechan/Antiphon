using Antiphon.Messaging.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Service;

[Category("Integration")]
[Category("Slow")]
public sealed class InboxConversationMigrationTests
{
    private static PostgreSqlContainer Postgres = null!;

    [Before(Class)]
    public static Task StartDatabaseAsync()
    {
        Postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        return Postgres.StartAsync();
    }

    [After(Class)]
    public static async Task StopDatabaseAsync() => await Postgres.DisposeAsync();

    private static async Task<MessagingDbContext> CreateDatabaseAsync()
    {
        var name = "migration_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(Postgres.GetConnectionString());
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
        await create.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = name };
        return new MessagingDbContext(new DbContextOptionsBuilder<MessagingDbContext>().UseNpgsql(connection.ConnectionString).Options);
    }

    [Test]
    public async Task Receipt_rollback_refuses_an_unmatched_acceptance_and_allows_an_empty_journal()
    {
        // Exercise real migration history, transactional DDL and provider error codes.
        await using var db = await CreateDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        const string previous = "20261014172813_ScopeInboxIdentityToConversation";
        const string receiptMigration = "20261127101000_AddInboxAnswerReceipts";
        await migrator.MigrateAsync(receiptMigration);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "InboxAnswerReceipts" VALUES ('telegram', '1', '2', NOW(), '3')
            """);
        var refused = await Should.ThrowAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        refused.MessageText.ShouldContain("InboxAnswerReceipts");
        (await db.Database.GetAppliedMigrationsAsync()).ShouldContain(receiptMigration);
        (await db.InboxAnswerReceipts.Select(r => r.OutboundMessageId).SingleAsync()).ShouldBe("3");
        await db.InboxAnswerReceipts.ExecuteDeleteAsync();
        await migrator.MigrateAsync(previous);
        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotContain(receiptMigration);
        await migrator.MigrateAsync(receiptMigration);
        (await db.InboxAnswerReceipts.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Upgrade_preserves_legacy_data_and_enforces_conversation_scoped_identity()
    {
        await using var db = await CreateDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        const string previous = "20260830190000_AddInboxLagMetadata";
        await migrator.MigrateAsync(previous);
        var id = Guid.NewGuid();
        db.Inbox.Add(new InboxMessage
        {
            Id = id, Channel = "telegram", ChannelMessageId = "1068", ConversationId = "chat-1",
            Text = "existing message", EnvelopeJson = "{\"original\":true}", ReplyHandle = "chat-1",
            ReceivedAt = DateTimeOffset.UnixEpoch, Topic = "channels.inbound", Partition = 0, Offset = 10,
            AcknowledgedAt = DateTimeOffset.UnixEpoch.AddMinutes(1), AckAttemptCount = 2,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await db.Database.MigrateAsync();

        var row = await db.Inbox.AsNoTracking().SingleAsync();
        row.Id.ShouldBe(id);
        row.Text.ShouldBe("existing message");
        row.EnvelopeJson.ShouldBe("{\"original\":true}");
        row.Offset.ShouldBe(10);
        row.AcknowledgedAt.ShouldBe(DateTimeOffset.UnixEpoch.AddMinutes(1));
        row.AckAttemptCount.ShouldBe(2);

        db.Inbox.Add(NewRow("chat-2"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Inbox.Add(NewRow("chat-2"));
        var duplicate = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pg = duplicate.InnerException.ShouldBeOfType<PostgresException>();
        pg.SqlState.ShouldBe("23505");
        pg.ConstraintName.ShouldBe("IX_Inbox_Channel_ConversationId_ChannelMessageId");
        db.ChangeTracker.Clear();

        // Rollback cannot represent the two legitimate chats: refuse without deleting either.
        var rollback = await Should.ThrowAsync<PostgresException>(() => migrator.MigrateAsync(previous));
        rollback.SqlState.ShouldBe("23505");
        (await db.Inbox.CountAsync()).ShouldBe(2);
        await db.Database.MigrateAsync();
        (await db.Inbox.CountAsync()).ShouldBe(2);

        // Once the operator has independently resolved the collision, rollback and re-upgrade work.
        await db.Inbox.Where(x => x.ConversationId == "chat-2").ExecuteDeleteAsync();
        await migrator.MigrateAsync(previous);
        await db.Database.MigrateAsync();
        (await db.Inbox.AsNoTracking().SingleAsync()).Id.ShouldBe(id);
    }

    private static InboxMessage NewRow(string chat) => new()
    {
        Id = Guid.NewGuid(), Channel = "telegram", ChannelMessageId = "1068", ConversationId = chat,
        ReceivedAt = DateTimeOffset.UnixEpoch, ReplyHandle = chat, EnvelopeJson = "{}",
    };
}
