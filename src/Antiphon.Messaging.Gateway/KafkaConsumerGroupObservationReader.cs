using System.Diagnostics;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

/// <summary>Each observation owns and disposes its admin client, including on a canceled wait.</summary>
public sealed class KafkaConsumerGroupObservationReader : IConsumerGroupObservationReader
{
    private readonly Func<IAdminClient> _factory;
    private readonly TimeSpan _budget;
    private readonly TimeProvider _time;

    public KafkaConsumerGroupObservationReader(IOptions<AntiphonGatewayOptions> options,
        TimeProvider time, Func<IAdminClient>? adminClientFactory = null)
    {
        _time = time;
        _budget = TimeSpan.FromSeconds(Math.Max(1, options.Value.ObservationBudgetSeconds));
        _factory = adminClientFactory ?? (() => new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            AllowAutoCreateTopics = false,
        }).SetLogHandler((_, _) => { }).Build());
    }

    public async Task<ConsumerGroupObservation> ObserveAsync(string groupId, string topic, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_budget);
        var token = deadline.Token;
        var watch = Stopwatch.StartNew();
        TimeSpan Remaining() => TimeSpan.FromMilliseconds(Math.Max(1, (_budget - watch.Elapsed).TotalMilliseconds));
        ConsumerGroupObservation Failed(string reason, ConsumerGroupStatus state = ConsumerGroupStatus.QueryFailed) =>
            new(groupId, topic, state, reason, null, [], _time.GetUtcNow());
        // Metadata is synchronous. The worker retains ownership until bounded native calls finish.
        var work = Task.Run(async () =>
        {
            using var admin = _factory();
            token.ThrowIfCancellationRequested();
            ConsumerGroupDescription? group;
            try
            {
                var description = await admin.DescribeConsumerGroupsAsync([groupId],
                    new DescribeConsumerGroupsOptions { RequestTimeout = Remaining() });
                group = description.ConsumerGroupDescriptions.FirstOrDefault(g => g.GroupId == groupId);
            }
            catch (DescribeConsumerGroupsException ex)
            {
                group = ex.Results.ConsumerGroupDescriptions.FirstOrDefault(g => g.GroupId == groupId);
                if (group is null) throw;
            }
            token.ThrowIfCancellationRequested();
            if (group?.Error.Code == ErrorCode.GroupIdNotFound ||
                (group is { State: ConsumerGroupState.Dead } && !group.Error.IsError))
                return Failed("group_absent", ConsumerGroupStatus.Absent);
            if (group is null) return Failed("query_failed");
            if (group.Error.IsError) return Failed(Reason(group.Error.Code));
            if (group.State == ConsumerGroupState.Unknown) return Failed("query_failed");
            var metadata = admin.GetMetadata(topic, Remaining()).Topics.FirstOrDefault(t => t.Topic == topic);
            token.ThrowIfCancellationRequested();
            if (metadata is null || metadata.Error.Code == ErrorCode.UnknownTopicOrPart)
                return Failed("topic_absent");
            if (metadata.Error.IsError) return Failed(Reason(metadata.Error.Code));
            var requested = metadata.Partitions.Select(p => new TopicPartition(topic, p.PartitionId)).ToArray();
            if (requested.Length == 0) return Failed("topic_absent");
            List<TopicPartitionOffsetError>? offsets;
            try
            {
                var reports = await admin.ListConsumerGroupOffsetsAsync(
                    [new ConsumerGroupTopicPartitions(groupId, requested.ToList())],
                    new ListConsumerGroupOffsetsOptions { RequestTimeout = Remaining() });
                offsets = reports.FirstOrDefault(r => r.Group == groupId)?.Partitions;
            }
            catch (ListConsumerGroupOffsetsException ex)
            {
                offsets = ex.Results.FirstOrDefault(r => r.Group == groupId)?.Partitions;
                if (offsets is null) throw;
            }
            token.ThrowIfCancellationRequested();
            var partitions = metadata.Partitions.Select(p =>
            {
                var offset = offsets?.FirstOrDefault(o => o.Topic == topic && o.Partition.Value == p.PartitionId);
                if (p.Error.IsError || offset is null || offset.Error.IsError)
                    return new PartitionOffsetObservation(p.PartitionId, PartitionOffsetStatus.QueryFailed, null, "partition_query_failed");
                return offset.Offset.Value >= 0
                    ? new PartitionOffsetObservation(p.PartitionId, PartitionOffsetStatus.CommittedOffset, offset.Offset.Value, null)
                    : new PartitionOffsetObservation(p.PartitionId, PartitionOffsetStatus.NoCommit, null, "no_committed_offsets");
            }).ToArray();
            var reason = partitions.Any(p => p.Status == PartitionOffsetStatus.QueryFailed) ? "partition_query_failed"
                : partitions.All(p => p.Status == PartitionOffsetStatus.NoCommit) ? "no_committed_offsets" : null;
            return new ConsumerGroupObservation(groupId, topic, ConsumerGroupStatus.Present, reason,
                group.Members.Count, partitions, _time.GetUtcNow());
        }, CancellationToken.None);
        try { return await work.WaitAsync(token); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Failed("query_timeout"); }
        catch (KafkaException ex) { return Failed(Reason(ex.Error.Code)); }
        catch (Exception) { return Failed("query_failed"); }
        finally
        {
            _ = work.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static string Reason(ErrorCode code) => code switch
    {
        ErrorCode.GroupAuthorizationFailed or ErrorCode.TopicAuthorizationFailed or ErrorCode.ClusterAuthorizationFailed => "authorization_failed",
        ErrorCode.Local_TimedOut or ErrorCode.RequestTimedOut => "query_timeout",
        ErrorCode.Local_AllBrokersDown or ErrorCode.Local_Transport => "broker_unreachable",
        _ => "query_failed",
    };
}
