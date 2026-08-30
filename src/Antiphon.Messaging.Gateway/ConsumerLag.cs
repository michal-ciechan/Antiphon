namespace Antiphon.Messaging.Gateway;

/// <summary>
/// Kafka committed offset is the next offset the consumer will read. A stored inbox record is
/// unconsumed when that committed offset is absent or has not moved past the record.
/// </summary>
public static class ConsumerLag
{
    public static bool IsUnconsumed(long? committedNextOffset, long recordOffset) =>
        committedNextOffset is null || committedNextOffset.Value <= recordOffset;
}
