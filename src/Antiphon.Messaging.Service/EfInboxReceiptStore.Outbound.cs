using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Messaging.Service;

public sealed partial class EfInboxReceiptStore
{
    public async Task<bool> TryBeginSendAsync(string operationId, ChannelReply reply, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        var ids = reply.Kind == ChannelReplyKind.Answer && !string.IsNullOrEmpty(reply.ConversationId)
            ? reply.AnsweredMessageIds : null;
        var json = JsonSerializer.Serialize(ids ?? []);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "GatewayOutboundOperations" ("Id", "Channel", "ConversationId", "AnsweredMessageIdsJson", "StartedAt")
            VALUES ({operationId}, {reply.Channel}, {reply.ConversationId}, {json}, {DateTimeOffset.UtcNow})
            ON CONFLICT ("Id") DO NOTHING
            """, ct) == 1;
    }

    public async Task<bool> ConfirmSendAsync(string operationId, SendResult accepted, CancellationToken ct)
    {
        if (!accepted.Ok) throw new ArgumentException("Provider acceptance is required.", nameof(accepted));
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var changed = await db.GatewayOutboundOperations.Where(o => o.Id == operationId && o.AcceptedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.AcceptedAt, now)
                .SetProperty(o => o.ProviderMessageId, accepted.ChannelMessageId), ct);
        var op = await db.GatewayOutboundOperations.AsNoTracking().SingleAsync(o => o.Id == operationId, ct);
        if (changed == 0 && op.ProviderMessageId != accepted.ChannelMessageId)
            return false;
        if (changed != 0)
        {
            var ids = JsonSerializer.Deserialize<string[]>(op.AnsweredMessageIdsJson)!;
            await RecordAnswerIdsAsync(db, op.Channel, op.ConversationId, ids, accepted.ChannelMessageId, now, ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}
