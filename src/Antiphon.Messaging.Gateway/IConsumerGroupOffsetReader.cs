namespace Antiphon.Messaging.Gateway;

/// <summary>
/// Reads a consumer group's committed next-to-consume offset for one topic partition.
/// Null means the group or partition has no committed offset (treated as unconsumed).
/// </summary>
public interface IConsumerGroupOffsetReader
{
    Task<long?> GetCommittedOffsetAsync(string groupId, string topic, int partition, CancellationToken cancellationToken);
}
