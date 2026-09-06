using Antiphon.Messaging.Gateway;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;
using static Antiphon.Messaging.Tests.Gateway.ConsumerLagAssessmentTests;

namespace Antiphon.Messaging.Tests.Gateway;

public sealed partial class InboundUnconsumedMonitorTests
{
    internal static void Untouched(Harness h, int index = 0)
    {
        var r = h.Store.All[index];
        r.AcknowledgedAt.ShouldBeNull(); r.OperationalEventPublishedAt.ShouldBeNull();
        r.NextAckAttemptAt.ShouldBeNull(); r.AckAttemptCount.ShouldBe(0);
    }

    [Test]
    public async Task Absent_group_with_overdue_receipt_sends_nothing_and_degrades_readiness()
    {
        var h = Harness.OverdueUnconsumed();
        h.Offsets.Observation = Observation(group: ConsumerGroupStatus.Absent, reason: "group_absent");
        (await h.TickAsync()).ShouldBe(0);
        h.Adapter.Sent.ShouldBeEmpty(); h.Publisher.Events.ShouldBeEmpty(); Untouched(h);
        h.Status.GetSnapshot().State.ShouldBe(MonitorState.Degraded);
        h.Status.GetSnapshot().ReasonCode.ShouldBe("group_absent");
        h.Logs.Entries.Where(e => e.Level == LogLevel.Error).ShouldHaveSingleItem().Text.ShouldContain("antiphon-consumer");
    }

    [Test]
    public async Task Known_group_with_no_members_and_retained_commit_still_detects_lag()
    {
        var h = Harness.OverdueUnconsumed();
        h.Offsets.Observation = Observation() with { MemberCount = 0 };
        (await h.TickAsync()).ShouldBe(1);
        h.Adapter.Sent.ShouldHaveSingleItem().Text.ShouldBe(InboundUnconsumedMonitorService.AcknowledgementText);
        h.Publisher.Events.ShouldHaveSingleItem();
        h.Store.All[0].AcknowledgedAt.ShouldNotBeNull(); h.Store.All[0].OperationalEventPublishedAt.ShouldNotBeNull();
        h.Status.GetSnapshot().State.ShouldBe(MonitorState.Ready);
    }

    [Test]
    public async Task Absent_group_and_no_commit_partition_have_distinct_reason_codes_and_neither_sends()
    {
        var h = Harness.OverdueUnconsumed();
        foreach (var o in new[] { Observation(group: ConsumerGroupStatus.Absent, reason: "group_absent"),
            Observation(status: PartitionOffsetStatus.NoCommit, commit: null, reason: "no_committed_offsets") })
        {
            h.Offsets.Observation = o;
            (await h.TickAsync()).ShouldBe(0);
            h.Status.GetSnapshot().ReasonCode.ShouldBe(o.ReasonCode);
            h.Adapter.Sent.ShouldBeEmpty(); h.Publisher.Events.ShouldBeEmpty(); Untouched(h);
        }
    }

    [Test]
    public async Task Partial_partition_failure_acts_only_on_confirmed_partitions()
    {
        var h = Harness.OverdueUnconsumed();
        h.Store.Add(h.Store.All[0] with { Id = Guid.NewGuid(), Partition = 1, ChannelMessageId = "m-2" });
        h.Offsets.Observation = Observation(reason: "partition_query_failed") with
        { Partitions = [new(0, PartitionOffsetStatus.CommittedOffset, 10, null), new(1, PartitionOffsetStatus.QueryFailed, null, "partition_query_failed")] };
        (await h.TickAsync()).ShouldBe(1);
        h.Adapter.Sent.ShouldHaveSingleItem().ReplyToMessageId.ShouldBe("m-1"); h.Publisher.Events.ShouldHaveSingleItem();
        Untouched(h, 1);
        h.Status.GetSnapshot().State.ShouldBe(MonitorState.Degraded);
        h.Status.GetSnapshot().ReasonCode.ShouldBe("partition_query_failed");
    }

    [Test]
    public async Task Unknown_after_success_does_not_reuse_the_previous_offset()
    {
        var h = Harness.OverdueUnconsumed(); h.Offsets.Committed = 11;
        await h.TickAsync(); h.Status.GetSnapshot().State.ShouldBe(MonitorState.Ready);
        // A newer receipt would lag the old commit. It must not borrow the previous pass's evidence.
        h.Store.Add(h.Store.All[0] with { Id = Guid.NewGuid(), Offset = 12, ChannelMessageId = "m-2" });
        h.Offsets.Observation = Observation(status: PartitionOffsetStatus.QueryFailed, commit: null, reason: "partition_query_failed");
        await h.TickAsync();
        h.Adapter.Sent.ShouldBeEmpty(); h.Publisher.Events.ShouldBeEmpty(); Untouched(h); Untouched(h, 1);
        h.Status.GetSnapshot().State.ShouldBe(MonitorState.Degraded);
    }

    [Test]
    public async Task Receipt_partition_outside_snapshot_is_unknown()
    {
        var h = new Harness();
        h.Store.Add(new() { Id = Guid.NewGuid(), Channel = "slack", ChannelMessageId = "missing", ConversationId = "c", ReplyHandle = "r", FirstSeenAt = h.Clock.GetUtcNow().AddMinutes(-6), Topic = "channels.inbound", Partition = 1, Offset = 100 });
        h.Offsets.Observation = Observation(commit: 99);
        await h.TickAsync(); h.Adapter.Sent.ShouldBeEmpty(); h.Publisher.Events.ShouldBeEmpty(); Untouched(h);
        h.Status.GetSnapshot().ReasonCode.ShouldBe("partition_outside_snapshot");
    }

    [Test]
    public async Task Unknown_observation_gates_before_acknowledge_health_and_publish()
    {
        var h = Harness.OverdueUnconsumed();
        h.Offsets.Observation = Observation(group: ConsumerGroupStatus.QueryFailed, reason: "query_failed");
        await h.TickAsync();
        h.Adapter.Sent.ShouldBeEmpty(); h.Health.Calls.ShouldBe(0); h.Publisher.Events.ShouldBeEmpty(); Untouched(h);
    }

    [Test]
    public async Task Empty_inbox_still_observes_the_group_and_updates_readiness()
    {
        var h = new Harness();
        h.Offsets.Observation = Observation(group: ConsumerGroupStatus.Absent, reason: "group_absent");
        await h.TickAsync(); h.Offsets.Calls.ShouldHaveSingleItem().ShouldBe(("antiphon-consumer", "channels.inbound"));
        h.Status.GetSnapshot().State.ShouldBe(MonitorState.Degraded);
        h.Offsets.Observation = Observation(); await h.TickAsync(); h.Status.GetSnapshot().State.ShouldBe(MonitorState.Ready);
    }

    [Test]
    public async Task Three_overdue_receipts_share_one_observation()
    {
        var h = Harness.OverdueUnconsumed();
        for (var i = 0; i < 2; i++) h.Store.Add(h.Store.All[0] with { Id = Guid.NewGuid(), ChannelMessageId = $"other-{i}" });
        await h.TickAsync(); h.Offsets.Calls.Count.ShouldBe(1); h.Adapter.Sent.Count.ShouldBe(3);
    }
}
