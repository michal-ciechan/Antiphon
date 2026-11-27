using Npgsql;

namespace Antiphon.Messaging.Service;

/// <summary>
/// Inbox is written from both ingress (<see cref="EfInboxReceiptStore.RecordAsync"/>) and
/// <see cref="InboxConsumerService"/> for the same (Channel, ConversationId, ChannelMessageId). The unique
/// index is the idempotency key; a 23505 is a lost insert race, not a distinct message.
/// </summary>
internal static class InboxUniqueConstraint
{
    internal const string ChannelMessageIndex = "IX_Inbox_Channel_ConversationId_ChannelMessageId";

    public static bool IsViolation(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
            if (ex is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ChannelMessageIndex })
                return true;
        return false;
    }
}
