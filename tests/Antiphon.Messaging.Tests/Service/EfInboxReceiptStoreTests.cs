using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Service;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Service;

public sealed class EfInboxReceiptStoreTests
{
    [Test]
    public async Task Same_message_id_in_two_chats_preserves_both_receipts_and_redelivery()
    {
        await using var harness = await StoreHarness.CreateAsync();
        await harness.Store.RecordAsync(Sample("1068", "8738110514"), "{\"text\":\"dm\"}", "channels.inbound", 0, 10, CancellationToken.None);
        await harness.Store.RecordAsync(Sample("1068", "-5052370282"), "{\"text\":\"group\"}", "channels.inbound", 1, 20, CancellationToken.None);
        await harness.Store.RecordAsync(Sample("1068", "-5052370282"), "{\"text\":\"replay\"}", "channels.inbound", 1, 21, CancellationToken.None);

        var rows = await harness.LoadAsync();
        rows.Count.ShouldBe(2);
        var dm = rows.Single(x => x.ConversationId == "8738110514");
        dm.EnvelopeJson.ShouldBe("{\"text\":\"dm\"}");
        dm.Offset.ShouldBe(10);
        var group = rows.Single(x => x.ConversationId == "-5052370282");
        group.EnvelopeJson.ShouldBe("{\"text\":\"group\"}");
        group.Partition.ShouldBe(1);
        group.Offset.ShouldBe(20);
        group.ReplyHandle.ShouldBe("-5052370282");
    }

    [Test]
    public async Task Database_unique_key_allows_two_chats_but_rejects_a_true_duplicate()
    {
        await using var harness = await StoreHarness.CreateAsync();
        await harness.InsertAsync(Row("chat-1"));
        await harness.InsertAsync(Row("chat-2"));
        await harness.InsertIgnoringDuplicateAsync(Row("chat-2"));
        (await harness.LoadAsync()).Select(x => x.ConversationId).Order().ShouldBe(["chat-1", "chat-2"]);
    }

    [Test]
    public async Task Offset_backfill_does_not_attach_another_chats_delivery_to_a_legacy_row()
    {
        await using var harness = await StoreHarness.CreateAsync();
        await harness.InsertAsync(Row("chat-1"));
        await harness.Store.RecordAsync(Sample("1068", "chat-2"), "{}", "channels.inbound", 2, 99, CancellationToken.None);
        var rows = await harness.LoadAsync();
        rows.Count.ShouldBe(2);
        rows.Single(x => x.ConversationId == "chat-1").Offset.ShouldBeNull();
        rows.Single(x => x.ConversationId == "chat-2").Offset.ShouldBe(99);
    }

    private static InboxMessage Row(string conversationId) => new()
    {
        Id = Guid.NewGuid(), Channel = "telegram", ChannelMessageId = "1068",
        ConversationId = conversationId, ReplyHandle = conversationId,
        ReceivedAt = DateTimeOffset.UnixEpoch, EnvelopeJson = "{}",
    };

    [Test]
    public void Unique_violation_is_detected_from_a_wrapped_postgres_23505()
    {
        InboxUniqueConstraint.IsViolation(DuplicateInboxException()).ShouldBeTrue();
        InboxUniqueConstraint.IsViolation(new InvalidOperationException("nope")).ShouldBeFalse();
        InboxUniqueConstraint.IsViolation(new DbUpdateException("saving", new InvalidOperationException("nope")))
            .ShouldBeFalse();
    }

    [Test]
    public void Unique_violation_is_not_inferred_from_constraint_name_alone()
    {
        InboxUniqueConstraint.IsViolation(InboxConstraintFailureWithoutUniqueSqlState()).ShouldBeFalse();
        InboxUniqueConstraint.IsViolation(new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation)).ShouldBeTrue();
    }

    [Test]
    public async Task RecordAsync_same_channel_and_message_id_twice_does_not_throw()
    {
        await using var harness = await StoreHarness.CreateAsync();
        var message = Sample("662");

        await harness.Store.RecordAsync(message, "{}", "channels.inbound", 0, 10, CancellationToken.None);
        await harness.Store.RecordAsync(message, "{}", "channels.inbound", 0, 11, CancellationToken.None);

        var rows = await harness.LoadAsync();
        var row = rows.ShouldHaveSingleItem();
        row.Channel.ShouldBe("telegram");
        row.ChannelMessageId.ShouldBe("662");
        row.Offset.ShouldBe(10);
    }

    [Test]
    public async Task RecordAsync_concurrent_inserts_of_the_same_key_keep_one_row()
    {
        await using var harness = await StoreHarness.CreateAsync();
        var message = Sample("662");

        await Task.WhenAll(
            harness.Store.RecordAsync(message, "{}", "channels.inbound", 0, 10, CancellationToken.None),
            harness.Store.RecordAsync(message, "{}", "channels.inbound", 0, 10, CancellationToken.None),
            harness.Store.RecordAsync(message, "{}", "channels.inbound", 0, 10, CancellationToken.None));

        (await harness.LoadAsync()).ShouldHaveSingleItem().ChannelMessageId.ShouldBe("662");
    }

    [Test]
    public async Task SaveChanges_of_a_duplicate_channel_message_id_is_ignored()
    {
        await using var harness = await StoreHarness.CreateAsync();
        await harness.InsertAsync(new InboxMessage
        {
            Id = Guid.NewGuid(),
            Channel = "telegram",
            ChannelMessageId = "662",
            ConversationId = "chat-1",
            ReplyHandle = "chat-1",
            Status = InboxStatus.Pending,
            ReceivedAt = DateTimeOffset.UnixEpoch,
            EnvelopeJson = "{}",
        });

        await harness.InsertIgnoringDuplicateAsync(new InboxMessage
        {
            Id = Guid.NewGuid(),
            Channel = "telegram",
            ChannelMessageId = "662",
            ConversationId = "chat-1",
            ReplyHandle = "chat-1",
            Status = InboxStatus.Pending,
            ReceivedAt = DateTimeOffset.UnixEpoch,
            EnvelopeJson = "{}",
        });

        (await harness.LoadAsync()).ShouldHaveSingleItem().ChannelMessageId.ShouldBe("662");
    }

    [Test]
    public async Task RecordAsync_fills_offset_when_the_existing_row_has_none()
    {
        await using var harness = await StoreHarness.CreateAsync();
        await harness.InsertAsync(new InboxMessage
        {
            Id = Guid.NewGuid(),
            Channel = "telegram",
            ChannelMessageId = "662",
            ConversationId = "chat-1",
            ReplyHandle = "chat-1",
            Status = InboxStatus.Pending,
            ReceivedAt = DateTimeOffset.UnixEpoch,
            EnvelopeJson = "{}",
        });

        await harness.Store.RecordAsync(Sample("662"), "{}", "channels.inbound", 2, 99, CancellationToken.None);

        var row = (await harness.LoadAsync()).ShouldHaveSingleItem();
        row.Topic.ShouldBe("channels.inbound");
        row.Partition.ShouldBe(2);
        row.Offset.ShouldBe(99);
    }

    internal static DbUpdateException DuplicateInboxException()
    {
        var postgres = new PostgresException(
            "duplicate key value violates unique constraint \"IX_Inbox_Channel_ChannelMessageId\"",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation);
        return new DbUpdateException("An error occurred while saving the entity changes.", postgres);
    }

    internal static DbUpdateException InboxConstraintFailureWithoutUniqueSqlState()
    {
        var postgres = new PostgresException(
            messageText: "could not serialize access due to concurrent update",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: PostgresErrorCodes.SerializationFailure,
            constraintName: InboxUniqueConstraint.ChannelMessageIndex);
        return new DbUpdateException("An error occurred while saving the entity changes.", postgres);
    }

    internal static ChannelMessage Sample(string channelMessageId, string conversationId = "chat-1") => new()
    {
        Id = "id-1",
        Channel = "telegram",
        ChannelMessageId = channelMessageId,
        Conversation = new Conversation { Id = conversationId, Kind = ConversationKind.Group },
        Author = new Participant { Id = "user-1", DisplayName = "Ada" },
        Timestamp = DateTimeOffset.UnixEpoch,
        Text = "hello",
        ReplyHandle = conversationId,
        Raw = JsonDocument.Parse("{}").RootElement.Clone(),
    };

    internal sealed class StoreHarness : IAsyncDisposable
    {
        private StoreHarness(ServiceProvider services, SqliteConnection connection)
        {
            Services = services;
            Connection = connection;
        }

        private ServiceProvider Services { get; }
        private SqliteConnection Connection { get; }
        public EfInboxReceiptStore Store => Services.GetRequiredService<EfInboxReceiptStore>();

        public static async Task<StoreHarness> CreateAsync()
        {
            var connection = new SqliteConnection(
                $"Data Source=file:inbox-{Guid.NewGuid():N}?mode=memory&cache=shared");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ILogger<EfInboxReceiptStore>>(NullLogger<EfInboxReceiptStore>.Instance);
            services.AddDbContext<MessagingDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton<EfInboxReceiptStore>();
            var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<MessagingDbContext>().Database.EnsureCreatedAsync();
            }

            return new StoreHarness(provider, connection);
        }

        public async Task InsertAsync(InboxMessage row)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
            db.Inbox.Add(row);
            await db.SaveChangesAsync();
        }

        public async Task InsertIgnoringDuplicateAsync(InboxMessage row)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
            db.Inbox.Add(row);
            await InboxUniqueConstraint.SaveChangesIgnoringDuplicateAsync(
                db, NullLogger<EfInboxReceiptStore>.Instance, row.Channel, row.ChannelMessageId, CancellationToken.None);
        }

        public async Task<List<InboxMessage>> LoadAsync()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
            return await db.Inbox.AsNoTracking().ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
