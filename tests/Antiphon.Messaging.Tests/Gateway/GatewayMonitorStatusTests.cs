using System.Text.Json;
using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using static Antiphon.Messaging.Tests.Gateway.ConsumerLagAssessmentTests;
using static Antiphon.Messaging.Tests.Gateway.InboundUnconsumedMonitorTests;

namespace Antiphon.Messaging.Tests.Gateway;

public sealed class GatewayMonitorStatusTests
{
    [Test]
    public async Task Absent_then_established_then_lag_then_caught_up_transitions()
    {
        var h = Harness.OverdueUnconsumed();
        h.Offsets.Observation = Observation(group: ConsumerGroupStatus.Absent, reason: "group_absent");
        await h.TickAsync();
        h.Logs.Entries.Count(e => e.Level == LogLevel.Error).ShouldBe(1);
        h.Clock.Advance(TimeSpan.FromMinutes(1)); await h.TickAsync();
        h.Logs.Entries.Count(e => e.Level == LogLevel.Error).ShouldBe(1);
        h.Clock.Advance(TimeSpan.FromMinutes(5)); await h.TickAsync();
        h.Logs.Entries.Count(e => e.Level == LogLevel.Error).ShouldBe(2);
        h.Offsets.Observation = Observation(commit: 11) with { ObservedAt = h.Clock.GetUtcNow() };
        await h.TickAsync(); h.Status.GetSnapshot().State.ShouldBe(MonitorState.Ready);
        h.Logs.Entries.Where(e => e.Level == LogLevel.Information).ShouldHaveSingleItem().Text.ShouldContain("monitor ready");
        h.Offsets.Observation = Observation() with { ObservedAt = h.Clock.GetUtcNow() };
        await h.TickAsync(); h.Adapter.Sent.ShouldHaveSingleItem(); h.Publisher.Events.ShouldHaveSingleItem();
        h.Status.GetSnapshot().State.ShouldBe(MonitorState.Ready);
        h.Offsets.Observation = Observation(commit: 11) with { ObservedAt = h.Clock.GetUtcNow() };
        await h.TickAsync(); h.Adapter.Sent.Count.ShouldBe(1); h.Publisher.Events.Count.ShouldBe(1);
    }

    [Test]
    public async Task Cached_ready_expires_after_two_polls_plus_budget()
    {
        var h = new Harness(); await h.TickAsync();
        h.Clock.Advance(TimeSpan.FromSeconds(129)); h.Status.GetSnapshot().State.ShouldBe(MonitorState.Ready);
        h.Status.GetSnapshot().HttpStatusCode.ShouldBe(200);
        h.Clock.Advance(TimeSpan.FromSeconds(2)); h.Status.GetSnapshot().State.ShouldBe(MonitorState.Stale);
        h.Status.GetSnapshot().HttpStatusCode.ShouldBe(503);
        h.Offsets.Observation = Observation() with { ObservedAt = h.Clock.GetUtcNow() };
        await h.TickAsync(); h.Status.GetSnapshot().State.ShouldBe(MonitorState.Ready);
    }

    [Test]
    public async Task New_partition_no_commit_then_first_commit()
    {
        var h = Harness.OverdueUnconsumed();
        h.Store.Add(h.Store.All[0] with { Partition = 1, Offset = 3 });
        foreach (var commit in new long?[] { null, 4, 3 })
        {
            h.Offsets.Observation = Observation() with { Partitions = [new(0, PartitionOffsetStatus.CommittedOffset, 10, null),
                new(1, commit is null ? PartitionOffsetStatus.NoCommit : PartitionOffsetStatus.CommittedOffset, commit, commit is null ? "no_committed_offsets" : null)] };
            await h.TickAsync(); h.Status.GetSnapshot().State.ShouldBe(MonitorState.Ready);
            h.Adapter.Sent.Count.ShouldBe(commit == 3 ? 1 : 0);
        }
    }

    [Test]
    public async Task Inbox_query_failure_is_degraded_not_ready()
    {
        var h = new Harness();
        var monitor = new InboundUnconsumedMonitorService([new FailingStore()], h.Offsets, [], h.Publisher, h.Health,
            Options.Create(new AntiphonGatewayOptions()), h.Clock, NullLogger<InboundUnconsumedMonitorService>.Instance, h.Status);
        (await monitor.TickAsync(default)).ShouldBe(0);
        h.Status.GetSnapshot().State.ShouldBe(MonitorState.Degraded);
        h.Status.GetSnapshot().ReasonCode.ShouldBe("inbox_query_failed");
    }

    [Test]
    public void Snapshot_maps_to_http_status_and_allowlisted_payload()
    {
        foreach (var state in Enum.GetValues<MonitorState>())
        {
            var snapshot = new InboundUnconsumedMonitorSnapshot(state, "query_failed", "expected", "watched", "topic",
                DateTimeOffset.UtcNow, 1, [new(0, PartitionOffsetStatus.QueryFailed, null, "partition_query_failed")]);
            snapshot.HttpStatusCode.ShouldBe(state is MonitorState.Ready or MonitorState.Disabled ? 200 : 503);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, MessagingJson.Options));
            json.RootElement.EnumerateObject().Select(p => p.Name).Order().ShouldBe(new[]
                { "ageSeconds", "expectedGroup", "observedAt", "partitions", "reasonCode", "state", "topic", "watchedGroup" });
            json.RootElement.GetProperty("partitions")[0].EnumerateObject().Select(p => p.Name).Order().ShouldBe(new[]
                { "committedNextOffset", "partition", "reasonCode", "status" });
        }
    }

    private sealed class FailingStore : IInboxReceiptStore
    {
        public Task<IReadOnlyList<InboundReceipt>> GetOverdueAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => throw new IOException("never-log-this");
        public Task MarkAcknowledgedAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task MarkOperationalEventPublishedAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ScheduleAckRetryAsync(Guid id, DateTimeOffset nextAttemptAt, int attemptCount, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
