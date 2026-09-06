using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

/// <summary>Compatibility projection: unknown evidence is null, never a synthetic zero.</summary>
public sealed class KafkaConsumerGroupOffsetReader : IConsumerGroupOffsetReader
{
    private readonly IConsumerGroupObservationReader _reader;
    public KafkaConsumerGroupOffsetReader(IOptions<AntiphonGatewayOptions> options,
        ILogger<KafkaConsumerGroupOffsetReader> logger)
        : this(new KafkaConsumerGroupObservationReader(options, TimeProvider.System)) { }
    public KafkaConsumerGroupOffsetReader(IConsumerGroupObservationReader reader) => _reader = reader;
    public async Task<long?> GetCommittedOffsetAsync(string groupId, string topic, int partition, CancellationToken cancellationToken)
    {
        var observation = await _reader.ObserveAsync(groupId, topic, cancellationToken);
        var offset = observation.Partitions.FirstOrDefault(p => p.Partition == partition);
        return observation.GroupStatus == ConsumerGroupStatus.Present &&
            offset?.Status == PartitionOffsetStatus.CommittedOffset && offset.CommittedNextOffset is >= 0
            ? offset.CommittedNextOffset : null;
    }
}
