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
    public void Unique_violation_is_detected_from_a_wrapped_postgres_23505()
    {
        InboxUniqueConstraint.IsViolation(DuplicateInboxException()).ShouldBeTrue();
        InboxUniqueConstraint.IsViolation(new InvalidOperationException("nope")).ShouldBeFalse();
        InboxUniqueConstraint.IsViolation(new DbUpdateException("saving", new InvalidOperationException("nope")))
            .ShouldBeFalse();
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

    private sealed class StoreHarness : IAsyncDisposable
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
