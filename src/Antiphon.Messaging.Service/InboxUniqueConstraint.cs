using Npgsql;

namespace Antiphon.Messaging.Service;

/// <summary>
/// Inbox is written from both ingress (<see cref="EfInboxReceiptStore.RecordAsync"/>) and
/// <see cref="InboxConsumerService"/> for the same (Channel, ChannelMessageId). The unique
/// index is the idempotency key; a 23505 is a lost insert race, not a distinct message.
/// </summary>
internal static class InboxUniqueConstraint
{
    internal const string ChannelMessageIndex = "IX_Inbox_Channel_ChannelMessageId";

    public static bool IsViolation(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException pg &&
                (pg.SqlState == PostgresErrorCodes.UniqueViolation
                 || string.Equals(pg.ConstraintName, ChannelMessageIndex, StringComparison.Ordinal)))
                return true;

            if (IsSqliteUniqueConstraint(ex))
                return true;
        }

        return false;
    }

    private static bool IsSqliteUniqueConstraint(Exception ex)
    {
        if (!string.Equals(ex.GetType().FullName, "Microsoft.Data.Sqlite.SqliteException", StringComparison.Ordinal))
            return false;
        var code = ex.GetType().GetProperty("SqliteErrorCode")?.GetValue(ex);
        if (code is 19 or 2067)
            return true;
        return ex.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }
}
