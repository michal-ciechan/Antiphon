using Antiphon.Messaging.Client.Testing;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0338 S4: <see cref="ChatChannelService.SendAsync"/> stamps outbound LastReplyAt /
/// LastReplyPreview and leaves inbound LastMessageAt / LastAuthor / LastChannelMessageId alone.
/// </summary>
[Category("Integration")]
public class ChatChannelServiceTests
{
    [Test]
    public async Task SendAsync_stamps_LastReplyAt_without_touching_inbound_columns()
    {
        var inboundAt = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeTimeProvider(new DateTimeOffset(inboundAt.AddMinutes(12)));
        var id = Guid.NewGuid();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions());
        db.ChatChannels.Add(new ChatChannel
        {
            Id = id,
            Provider = "telegram",
            ExternalId = $"send-stamp-{id:N}",
            Kind = ChatChannelKind.Direct,
            LastChannelMessageId = "inbound-1",
            LastMessageAt = inboundAt,
            LastMessagePreview = "from mike",
            LastAuthor = "Mike",
            MessageCount = 4,
            CreatedAt = inboundAt,
            UpdatedAt = inboundAt,
        });
        await db.SaveChangesAsync();

        try
        {
            var producer = new FakeAntiphonMessagingClient();
            var service = new ChatChannelService(db, clock, producer);
            await service.SendAsync(id, "digest ping", CancellationToken.None);

            producer.SentReplies.ShouldHaveSingleItem().Text.ShouldBe("digest ping");

            await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions());
            var row = await verify.ChatChannels.AsNoTracking().SingleAsync(c => c.Id == id);
            row.LastReplyAt.ShouldBe(clock.GetUtcNow().UtcDateTime);
            row.LastReplyPreview.ShouldBe("digest ping");
            row.LastMessageAt.ShouldBe(inboundAt);
            row.LastAuthor.ShouldBe("Mike");
            row.LastChannelMessageId.ShouldBe("inbound-1");
        }
        finally
        {
            await db.ChatChannels.Where(c => c.Id == id).ExecuteDeleteAsync();
        }
    }
}
