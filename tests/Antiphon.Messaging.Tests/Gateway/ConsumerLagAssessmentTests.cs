using Antiphon.Messaging.Gateway;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Gateway;

public sealed class ConsumerLagAssessmentTests
{
    internal static ConsumerGroupObservation Observation(ConsumerGroupStatus group = ConsumerGroupStatus.Present,
        PartitionOffsetStatus status = PartitionOffsetStatus.CommittedOffset, long? commit = 10, string? reason = null) =>
        new("antiphon-consumer", "channels.inbound", group, reason, 0, [new(0, status, commit, reason)], DateTimeOffset.UtcNow);

    [Test]
    [Arguments("partition_query_failed")]
    [Arguments("no_committed_offsets")]
    [Arguments("partition_outside_snapshot")]
    [Arguments("group_absent")]
    [Arguments("broker_unreachable")]
    [Arguments("query_timeout")]
    [Arguments("authorization_failed")]
    [Arguments("query_failed")]
    [Arguments("negative_commit")]
    [Arguments("unset_commit")]
    [Arguments("receipt_offset_invalid")]
    public void Assess_is_unknown_for(string reason)
    {
        var o = reason switch
        {
            "partition_query_failed" => Observation(status: PartitionOffsetStatus.QueryFailed, commit: null),
            "no_committed_offsets" => Observation(status: PartitionOffsetStatus.NoCommit, commit: null),
            "group_absent" => Observation(group: ConsumerGroupStatus.Absent),
            "broker_unreachable" or "query_timeout" or "authorization_failed" or "query_failed" => Observation(group: ConsumerGroupStatus.QueryFailed, reason: reason),
            "negative_commit" => Observation(commit: -1),
            "unset_commit" => Observation(commit: -1001),
            _ => Observation(),
        };
        ConsumerLag.Assess(o, reason == "partition_outside_snapshot" ? 1 : 0, reason == "receipt_offset_invalid" ? -1 : 10).ShouldBe(LagAssessment.Unknown);
    }

    [Test]
    public void Legacy_IsUnconsumed_is_true_only_for_confirmed_lag()
    {
        foreach (var commit in new long?[] { null, -1001, -1, 11 }) ConsumerLag.IsUnconsumed(commit, 10).ShouldBeFalse();
        ConsumerLag.IsUnconsumed(10, -1).ShouldBeFalse();
        ConsumerLag.IsUnconsumed(9, 10).ShouldBeTrue();
        ConsumerLag.IsUnconsumed(10, 10).ShouldBeTrue();
    }

    [Test]
    [Arguments(9L, 10L, LagAssessment.Unconsumed)]
    [Arguments(10L, 10L, LagAssessment.Unconsumed)]
    [Arguments(11L, 10L, LagAssessment.Consumed)]
    [Arguments(0L, 0L, LagAssessment.Unconsumed)]
    [Arguments(1L, 0L, LagAssessment.Consumed)]
    public void Assess_boundary(long commit, long record, LagAssessment expected) =>
        ConsumerLag.Assess(Observation(commit: commit), 0, record).ShouldBe(expected);

    [Test]
    public async Task Legacy_adapter_projects_only_committed_offsets()
    {
        var reader = new ScriptedReader { Observation = Observation(commit: 7) };
        var adapter = new KafkaConsumerGroupOffsetReader(reader);
        (await adapter.GetCommittedOffsetAsync("g", "t", 0, default)).ShouldBe(7);
        foreach (var o in new[] { Observation(status: PartitionOffsetStatus.NoCommit, commit: null),
            Observation(status: PartitionOffsetStatus.QueryFailed, commit: null), Observation(group: ConsumerGroupStatus.Absent), Observation() with { Partitions = [] } })
        {
            reader.Observation = o;
            (await adapter.GetCommittedOffsetAsync("g", "t", 0, default)).ShouldBeNull();
        }
    }
    private sealed class ScriptedReader : IConsumerGroupObservationReader
    {
        public ConsumerGroupObservation Observation { get; set; } = ConsumerLagAssessmentTests.Observation();
        public Task<ConsumerGroupObservation> ObserveAsync(string groupId, string topic, CancellationToken cancellationToken) => Task.FromResult(Observation);
    }
}
