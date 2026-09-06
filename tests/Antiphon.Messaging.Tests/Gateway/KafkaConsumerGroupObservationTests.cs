using System.Diagnostics;
using Antiphon.Messaging.Gateway;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.Redpanda;
using TUnit.Core;

namespace Antiphon.Messaging.Tests.Gateway;

[NotInParallel]
public sealed class KafkaConsumerGroupObservationTests
{
    private static RedpandaContainer? _broker;

    [Before(Class)]
    public static async Task StartAsync()
    {
        if (Environment.GetEnvironmentVariable("ANTIPHON_BROKER_TESTS") != "1") return;
        _broker = new RedpandaBuilder("docker.redpanda.com/redpandadata/redpanda:v25.3.4").WithCommand("--set", "redpanda.auto_create_topics_enabled=false").Build();
        await _broker.StartAsync();
    }

    [After(Class)]
    public static async Task StopAsync() { if (_broker is not null) await _broker.DisposeAsync(); }

    private KafkaConsumerGroupObservationReader Reader(string bootstrap, int budget = 10) =>
        new(Options.Create(new AntiphonGatewayOptions { BootstrapServers = bootstrap, ObservationBudgetSeconds = budget }), TimeProvider.System);

    [Test]
    public async Task Unreachable_broker_is_query_failed_within_budget()
    {
        var watch = Stopwatch.StartNew();
        var o = await Reader("127.0.0.1:1", 2).ObserveAsync("c0410-unreachable", "c0410-topic", default);
        o.GroupStatus.ShouldBe(ConsumerGroupStatus.QueryFailed);
        new[] { "broker_unreachable", "query_timeout" }.ShouldContain(o.ReasonCode!);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task Cancellation_propagates_and_is_not_an_observation_failure()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var watch = Stopwatch.StartNew();
        await Should.ThrowAsync<OperationCanceledException>(() => Reader("127.0.0.1:1").ObserveAsync("c0410-cancel", "c0410-topic", cancel.Token));
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    private async Task<(string Bootstrap, string Group, string Topic)> SetupAsync(int partitions = 1)
    {
        if (_broker is null) Skip.Test("set ANTIPHON_BROKER_TESTS=1 with Docker running");
        var bootstrap = _broker!.GetBootstrapAddress();
        var name = $"c0410-case-{Guid.NewGuid():N}";
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = name, NumPartitions = partitions, ReplicationFactor = 1 }]);
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrap }).Build();
        for (var p = 0; p < partitions; p++)
            for (var i = 0; i < 3; i++) await producer.ProduceAsync(new TopicPartition(name, p), new() { Key = "test", Value = "test" });
        return (bootstrap, name + "-group", name);
    }

    private IConsumer<string, string> Consumer(string bootstrap, string group) =>
        new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = bootstrap, GroupId = group,
            EnableAutoCommit = false, AutoOffsetReset = AutoOffsetReset.Earliest, AllowAutoCreateTopics = false }).Build();

    private async Task<ConsumerGroupObservation> ObserveAsync((string Bootstrap, string Group, string Topic) context)
    {
        var watch = Stopwatch.StartNew();
        var o = await Reader(context.Bootstrap).ObserveAsync(context.Group, context.Topic, default);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        return o;
    }

    [Test]
    public async Task Never_created_group_is_absent()
    {
        var c = await SetupAsync();
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = c.Bootstrap }).Build();
        try
        {
            var raw = await admin.DescribeConsumerGroupsAsync([c.Group], new DescribeConsumerGroupsOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
            foreach (var g in raw.ConsumerGroupDescriptions) Console.WriteLine($"V-35a raw State={g.State} Error={g.Error.Code}");
        }
        catch (DescribeConsumerGroupsException ex)
        { foreach (var g in ex.Results.ConsumerGroupDescriptions) Console.WriteLine($"V-35a raw State={g.State} Error={g.Error.Code}"); }
        var offsets = await admin.ListConsumerGroupOffsetsAsync([new ConsumerGroupTopicPartitions(c.Group, [new(c.Topic, 0)])], new ListConsumerGroupOffsetsOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
        foreach (var offset in offsets.SelectMany(r => r.Partitions)) Console.WriteLine($"V-35a raw Offset={offset.Offset.Value} Error={offset.Error.Code}");
        var o = await ObserveAsync(c); o.GroupStatus.ShouldBe(ConsumerGroupStatus.Absent); o.ReasonCode.ShouldBe("group_absent");
        var settled = await admin.DescribeConsumerGroupsAsync([c.Group], new DescribeConsumerGroupsOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
        foreach (var g in settled.ConsumerGroupDescriptions) Console.WriteLine($"V-35a settled State={g.State} Error={g.Error.Code}");
    }

    [Test]
    public async Task Joined_member_without_commit_is_present_no_commit()
    {
        var c = await SetupAsync(); using var consumer = Consumer(c.Bootstrap, c.Group);
        consumer.Subscribe(c.Topic);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        consumer.Consume(timeout.Token).ShouldNotBeNull();
        var o = await ObserveAsync(c); o.GroupStatus.ShouldBe(ConsumerGroupStatus.Present);
        o.Partitions.ShouldAllBe(p => p.Status == PartitionOffsetStatus.NoCommit);
        o.ReasonCode.ShouldBe("no_committed_offsets"); consumer.Close();
    }

    private async Task<ConsumerGroupObservation> CommittedAsync(long commit, int partitions = 1)
    {
        var c = await SetupAsync(partitions);
        using (var consumer = Consumer(c.Bootstrap, c.Group))
        {
            consumer.Assign(new TopicPartition(c.Topic, 0));
            consumer.Commit([new TopicPartitionOffset(c.Topic, 0, commit)]); consumer.Close();
        }
        return await ObserveAsync(c);
    }

    [Test]
    public async Task Committed_then_left_is_present_with_offset()
    {
        var o = await CommittedAsync(2); o.GroupStatus.ShouldBe(ConsumerGroupStatus.Present);
        o.MemberCount.ShouldBe(0); o.Partitions[0].CommittedNextOffset.ShouldBe(2);
    }

    [Test]
    public async Task Real_backlog_assesses_unconsumed_past_the_commit()
    {
        var o = await CommittedAsync(1); o.Partitions[0].CommittedNextOffset.ShouldBe(1);
        ConsumerLag.Assess(o, 0, 0).ShouldBe(LagAssessment.Consumed);
        ConsumerLag.Assess(o, 0, 1).ShouldBe(LagAssessment.Unconsumed);
        ConsumerLag.Assess(o, 0, 2).ShouldBe(LagAssessment.Unconsumed);
    }

    [Test]
    public async Task Caught_up_commit_assesses_consumed() => ConsumerLag.Assess(await CommittedAsync(3), 0, 2).ShouldBe(LagAssessment.Consumed);

    [Test]
    public async Task Two_partitions_commit_only_one()
    {
        var o = await CommittedAsync(1, 2); o.GroupStatus.ShouldBe(ConsumerGroupStatus.Present);
        o.Partitions.Single(p => p.Partition == 0).Status.ShouldBe(PartitionOffsetStatus.CommittedOffset);
        o.Partitions.Single(p => p.Partition == 1).Status.ShouldBe(PartitionOffsetStatus.NoCommit);
    }

    [Test]
    public async Task Absent_topic_is_unknown_evidence()
    {
        var c = await SetupAsync();
        using var consumer = Consumer(c.Bootstrap, c.Group);
        consumer.Assign(new TopicPartition(c.Topic, 0)); consumer.Commit([new TopicPartitionOffset(c.Topic, 0, 1)]);
        var o = await ObserveAsync((c.Bootstrap, c.Group, c.Topic + "-absent"));
        o.Partitions.ShouldBeEmpty(); o.ReasonCode.ShouldBe("topic_absent"); ConsumerLag.Assess(o, 0, 2).ShouldBe(LagAssessment.Unknown);
    }
}
