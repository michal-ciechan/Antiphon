namespace Antiphon.Messaging.Gateway;

/// <summary>
/// Reads a consumer group's committed next-to-consume offset for one topic partition.
/// Null means unknown evidence and must never authorize an unconsumed notice.
/// </summary>
public interface IConsumerGroupOffsetReader
{
    Task<long?> GetCommittedOffsetAsync(string groupId, string topic, int partition, CancellationToken cancellationToken);
}

