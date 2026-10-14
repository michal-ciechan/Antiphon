using Antiphon.Messaging.Gateway;
using Antiphon.Messaging.Gateway.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Service;

public sealed class InMemoryInboxReceiptStoreTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Chat_identity_is_preserved_for_recorded_and_seeded_receipts(bool seed)
    {
        var store = new InMemoryInboxReceiptStore();
        if (seed)
            store.Add(new InboundReceipt
            {
                Id = Guid.NewGuid(), Channel = "telegram", ChannelMessageId = "1068",
                ConversationId = "chat-1", ReplyHandle = "chat-1", FirstSeenAt = DateTimeOffset.UnixEpoch,
                Topic = "channels.inbound", Partition = 0, Offset = 10,
            });
        else
            await store.RecordAsync(EfInboxReceiptStoreTests.Sample("1068", "chat-1"), "{}", "channels.inbound", 0, 10, CancellationToken.None);
        await store.RecordAsync(EfInboxReceiptStoreTests.Sample("1068", "chat-2"), "{}", "channels.inbound", 1, 20, CancellationToken.None);
        await store.RecordAsync(EfInboxReceiptStoreTests.Sample("1068", "chat-1"), "{}", "channels.inbound", 0, 11, CancellationToken.None);
        await store.RecordAsync(EfInboxReceiptStoreTests.Sample("1068", "chat-2"), "{}", "channels.inbound", 1, 21, CancellationToken.None);

        var rows = store.All;
        rows.Count.ShouldBe(2);
        rows.Single(x => x.ConversationId == "chat-1").Offset.ShouldBe(10);
        var group = rows.Single(x => x.ConversationId == "chat-2");
        group.Offset.ShouldBe(20);
        group.ReplyHandle.ShouldBe("chat-2");
        (await store.GetOverdueAsync(DateTimeOffset.UnixEpoch, CancellationToken.None)).Count.ShouldBe(2);
    }
}
