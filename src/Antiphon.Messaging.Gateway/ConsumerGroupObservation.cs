namespace Antiphon.Messaging.Gateway;

public enum ConsumerGroupStatus { Present, Absent, QueryFailed }
public enum PartitionOffsetStatus { CommittedOffset, NoCommit, QueryFailed }
public enum LagAssessment { Unknown, Consumed, Unconsumed }

public sealed record PartitionOffsetObservation(
    int Partition, PartitionOffsetStatus Status, long? CommittedNextOffset, string? ReasonCode);

public sealed record ConsumerGroupObservation(
    string GroupId, string Topic, ConsumerGroupStatus GroupStatus, string? ReasonCode,
    int? MemberCount, IReadOnlyList<PartitionOffsetObservation> Partitions, DateTimeOffset ObservedAt);

public interface IConsumerGroupObservationReader
{
    Task<ConsumerGroupObservation> ObserveAsync(string groupId, string topic, CancellationToken cancellationToken);
}
