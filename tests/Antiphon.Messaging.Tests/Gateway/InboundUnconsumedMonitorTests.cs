using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using Antiphon.Messaging.Gateway.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Gateway;

/// <summary>
/// CARD-0245 S2a — lag probe, exactly-once acknowledgement, operational event, negatives.
/// No broker: fake offset reader, in-memory inbox, scripted adapter.
/// </summary>
public sealed class InboundUnconsumedMonitorTests
{
    [Test]
    public void Committed_before_record_is_unconsumed() =>
        ConsumerLag.IsUnconsumed(committedNextOffset: 9, recordOffset: 10).ShouldBeTrue();

    [Test]
    public void Committed_at_record_is_unconsumed() =>
        ConsumerLag.IsUnconsumed(committedNextOffset: 10, recordOffset: 10).ShouldBeTrue();

    [Test]
    public void Committed_past_record_is_consumed() =>
        ConsumerLag.IsUnconsumed(committedNextOffset: 11, recordOffset: 10).ShouldBeFalse();

    [Test]
    public void Absent_group_offset_is_unconsumed() =>
        ConsumerLag.IsUnconsumed(committedNextOffset: null, recordOffset: 10).ShouldBeTrue();

    [Test]
    public async Task Overdue_unconsumed_acknowledges_once_and_publishes_one_event()
    {
        var harness = Harness.OverdueUnconsumed();
        (await harness.TickAsync()).ShouldBe(1);
        (await harness.TickAsync()).ShouldBe(1); // still unconsumed, but watermarks hold
        (await harness.TickAsync()).ShouldBe(1);

        harness.Adapter.Sent.Count.ShouldBe(1);
        harness.Adapter.Sent[0].Text.ShouldBe(InboundUnconsumedMonitorService.AcknowledgementText);
        harness.Adapter.Sent[0].ReplyToMessageId.ShouldBe("m-1");
        harness.Adapter.Sent[0].ReplyHandle.ShouldBe("thread-1");
        harness.Publisher.Events.Count.ShouldBe(1);
        harness.Publisher.Events[0].Acknowledged.ShouldBeTrue();
        harness.Publisher.Events[0].AppHostHealth.ShouldBe("fail: timeout");
        harness.Store.All.ShouldHaveSingleItem().AcknowledgedAt.ShouldNotBeNull();
        harness.Store.All[0].OperationalEventPublishedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Failed_send_retries_and_does_not_duplicate_a_successful_send()
    {
        var harness = Harness.OverdueUnconsumed();
        harness.Adapter.Results.Clear();
        harness.Adapter.Results.Enqueue(SendResult.Failed("boom"));
        harness.Adapter.Results.Enqueue(SendResult.Sent("ok"));

        (await harness.TickAsync()).ShouldBe(1);
        harness.Adapter.Sent.Count.ShouldBe(1);
        harness.Publisher.Events.Count.ShouldBe(1);
        harness.Publisher.Events[0].Acknowledged.ShouldBeFalse();
        harness.Store.All[0].AcknowledgedAt.ShouldBeNull();
        harness.Store.All[0].NextAckAttemptAt.ShouldNotBeNull();

        // Immediate retry is withheld until backoff elapses.
        (await harness.TickAsync()).ShouldBe(1);
        harness.Adapter.Sent.Count.ShouldBe(1);

        harness.Clock.Advance(TimeSpan.FromMinutes(2));
        (await harness.TickAsync()).ShouldBe(1);
        harness.Adapter.Sent.Count.ShouldBe(2);
        harness.Store.All[0].AcknowledgedAt.ShouldNotBeNull();
        harness.Publisher.Events.Count.ShouldBe(1, "successful send after a failed one must not re-emit");
    }

    [Test]
    public async Task Committed_past_produces_neither_message_nor_event()
    {
        var harness = Harness.OverdueUnconsumed();
        harness.Offsets.Committed = 11;
        (await harness.TickAsync()).ShouldBe(0);
        harness.Adapter.Sent.ShouldBeEmpty();
        harness.Publisher.Events.ShouldBeEmpty();
    }

    [Test]
    public async Task Under_budget_age_produces_neither_message_nor_event()
    {
        var harness = Harness.OverdueUnconsumed(age: TimeSpan.FromMinutes(2));
        (await harness.TickAsync()).ShouldBe(0);
        harness.Adapter.Sent.ShouldBeEmpty();
        harness.Publisher.Events.ShouldBeEmpty();
    }

    [Test]
    public async Task Health_outage_is_diagnostic_only_lag_still_decides()
    {
        var harness = Harness.OverdueUnconsumed();
        harness.Health.Result = "fail: timeout";
        harness.Offsets.Committed = 11;
        (await harness.TickAsync()).ShouldBe(0);
        harness.Publisher.Events.ShouldBeEmpty();

        harness.Offsets.Committed = 10;
        (await harness.TickAsync()).ShouldBe(1);
        harness.Publisher.Events.ShouldHaveSingleItem().AppHostHealth.ShouldBe("fail: timeout");
        harness.Adapter.Sent.ShouldHaveSingleItem();
    }

    private sealed class Harness
    {
        public InMemoryInboxReceiptStore Store { get; } = new();
        public FakeOffsets Offsets { get; } = new() { Committed = 10 };
        public ScriptedAdapter Adapter { get; } = new();
        public CapturingPublisher Publisher { get; } = new();
        public FakeHealth Health { get; } = new() { Result = "fail: timeout" };
        public OffsetClock Clock { get; } = new();
        public InboundUnconsumedMonitorService Sut { get; }

        private Harness()
        {
            Sut = new InboundUnconsumedMonitorService(
                [Store],
                Offsets,
                [Adapter],
                Publisher,
                Health,
                Options.Create(new AntiphonGatewayOptions
                {
                    InboundUnconsumedMinutes = 5,
                    InboundUnconsumedPollSeconds = 60,
                    AntiphonConsumerGroup = "antiphon-consumer",
                    InboundUnconsumedMonitorEnabled = true,
                }),
                Clock,
                NullLogger<InboundUnconsumedMonitorService>.Instance);
        }

        public static Harness OverdueUnconsumed(TimeSpan? age = null)
        {
            var harness = new Harness();
            var firstSeen = harness.Clock.GetUtcNow() - (age ?? TimeSpan.FromMinutes(6));
            harness.Store.Add(new InboundReceipt
            {
                Id = Guid.NewGuid(),
                Channel = "slack",
                ChannelMessageId = "m-1",
                ConversationId = "C123",
                ReplyHandle = "thread-1",
                FirstSeenAt = firstSeen,
                Topic = "channels.inbound",
                Partition = 0,
                Offset = 10,
            });
            if (harness.Adapter.Results.Count == 0)
                harness.Adapter.Results.Enqueue(SendResult.Sent("1"));
            return harness;
        }

        public Task<int> TickAsync() => Sut.TickAsync(CancellationToken.None);
    }

    private sealed class FakeOffsets : IConsumerGroupOffsetReader
    {
        public long? Committed { get; set; }
        public Task<long?> GetCommittedOffsetAsync(string groupId, string topic, int partition, CancellationToken cancellationToken)
            => Task.FromResult(Committed);
    }

    private sealed class CapturingPublisher : IInboundUnconsumedEventPublisher
    {
        public List<InboundUnconsumedEvent> Events { get; } = [];
        public Task PublishAsync(InboundUnconsumedEvent evt, CancellationToken cancellationToken)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHealth : IAppHostHealthProbe
    {
        public string Result { get; set; } = "http 200";
        public Task<string> ProbeAsync(CancellationToken cancellationToken) => Task.FromResult(Result);
    }

    private sealed class ScriptedAdapter : IChannelAdapter
    {
        public Queue<SendResult> Results { get; } = new();
        public List<ChannelReply> Sent { get; } = [];
        public string Channel => "slack";
        public ChannelCapabilities Capabilities { get; } = new() { Channel = "slack" };
        public IAsyncEnumerable<ChannelMessage> ReceiveAsync(CancellationToken cancellationToken)
            => Empty();
        public Task<SendResult> SendAsync(ChannelReply reply, CancellationToken cancellationToken)
        {
            Sent.Add(reply);
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : SendResult.Sent("auto"));
        }
        private static async IAsyncEnumerable<ChannelMessage> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    /// <summary>Offset over the real clock (CARD-0222): never a frozen instant.</summary>
    private sealed class OffsetClock : TimeProvider
    {
        private TimeSpan _offset;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
        public void Advance(TimeSpan span) => _offset += span;
    }
}
