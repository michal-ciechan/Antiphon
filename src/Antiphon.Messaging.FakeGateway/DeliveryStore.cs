using System.Text.Json;

namespace Antiphon.Messaging.FakeGateway;

/// <summary>One recorded would-be delivery (a ChannelReply the real gateway would have sent).</summary>
public sealed record RecordedDelivery(
    long Seq,
    DateTime RecordedAtUtc,
    string Channel,
    string? ConversationId,
    string? ReplyHandle,
    string? Text,
    ChannelReplyKind Kind);

/// <summary>
/// The fake gateway's memory: everything consumed from channels.outbound, queryable over HTTP for
/// test assertions and appended to a JSONL file for grep-ability. Reset between tests via DELETE.
/// </summary>
public sealed class DeliveryStore
{
    private readonly object _gate = new();
    private readonly List<RecordedDelivery> _deliveries = [];
    private readonly string? _jsonlPath;
    private long _seq;

    public DeliveryStore(string? jsonlPath)
    {
        _jsonlPath = jsonlPath;
        if (jsonlPath is not null)
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonlPath))!);
    }

    public RecordedDelivery Record(ChannelReply reply, DateTime nowUtc)
    {
        RecordedDelivery delivery;
        lock (_gate)
        {
            delivery = new RecordedDelivery(
                ++_seq, nowUtc, reply.Channel, reply.ConversationId, reply.ReplyHandle, reply.Text, reply.Kind);
            _deliveries.Add(delivery);
        }

        if (_jsonlPath is not null)
        {
            try
            {
                File.AppendAllText(_jsonlPath, JsonSerializer.Serialize(delivery) + Environment.NewLine);
            }
            catch
            {
                // Log file is a convenience; the in-memory record is the API contract.
            }
        }

        return delivery;
    }

    public IReadOnlyList<RecordedDelivery> Query(long? sinceSeq, string? channel, string? conversationId)
    {
        lock (_gate)
        {
            return _deliveries
                .Where(d => sinceSeq is null || d.Seq > sinceSeq)
                .Where(d => channel is null || string.Equals(d.Channel, channel, StringComparison.OrdinalIgnoreCase))
                .Where(d => conversationId is null || d.ConversationId == conversationId)
                .ToList();
        }
    }

    public int Reset()
    {
        lock (_gate)
        {
            var count = _deliveries.Count;
            _deliveries.Clear();
            return count;
        }
    }
}

/// <summary>Outage simulation switch: while paused the consumer stops polling (gateway "down").</summary>
public sealed class PauseState
{
    private volatile bool _paused;

    public bool Paused => _paused;

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;
}
