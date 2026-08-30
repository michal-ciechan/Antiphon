using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

public sealed class KafkaConsumerGroupOffsetReader(
    IOptions<AntiphonGatewayOptions> options,
    ILogger<KafkaConsumerGroupOffsetReader> logger) : IConsumerGroupOffsetReader
{
    private readonly AntiphonGatewayOptions _options = options.Value;

    public async Task<long?> GetCommittedOffsetAsync(
        string groupId, string topic, int partition, CancellationToken cancellationToken)
    {
        try
        {
            using var admin = new AdminClientBuilder(new AdminClientConfig
            {
                BootstrapServers = _options.BootstrapServers,
            }).Build();

            var spec = new ConsumerGroupTopicPartitions(
                groupId,
                [new TopicPartition(topic, new Partition(partition))]);
            var results = await admin.ListConsumerGroupOffsetsAsync([spec]);
            var report = results.FirstOrDefault();
            if (report is null)
            {
                logger.LogWarning(
                    "[inbound-unconsumed] group-offset query returned nothing for {Group} {Topic}/{Partition}",
                    groupId, topic, partition);
                return null;
            }

            var match = report.Partitions.FirstOrDefault(p =>
                p.Topic == topic && p.Partition.Value == partition);
            if (match is null || match.Error.IsError || match.Offset == Offset.Unset)
                return null;
            return match.Offset.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "[inbound-unconsumed] group-offset query threw for {Group} {Topic}/{Partition}",
                groupId, topic, partition);
            return null;
        }
    }
}
