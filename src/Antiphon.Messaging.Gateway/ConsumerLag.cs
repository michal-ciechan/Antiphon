namespace Antiphon.Messaging.Gateway;

/// <summary>
/// Kafka committed offset is the next offset the consumer will read. A stored inbox record is
/// unconsumed only when a known committed offset has not moved past the record.
/// </summary>
public static class ConsumerLag
{
    public static bool IsUnconsumed(long? committedNextOffset, long recordOffset) =>
        committedNextOffset is >= 0 && recordOffset >= 0 && committedNextOffset.Value <= recordOffset;

    public static LagAssessment Assess(ConsumerGroupObservation observation, int partition, long recordOffset)
    {
        var offset = observation.Partitions.FirstOrDefault(p => p.Partition == partition);
        if (observation.GroupStatus != ConsumerGroupStatus.Present || recordOffset < 0 ||
            offset?.Status != PartitionOffsetStatus.CommittedOffset || offset.CommittedNextOffset is not >= 0)
            return LagAssessment.Unknown;
        return offset.CommittedNextOffset > recordOffset ? LagAssessment.Consumed : LagAssessment.Unconsumed;
    }
}
