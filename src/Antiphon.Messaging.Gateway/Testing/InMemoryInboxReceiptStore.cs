using Antiphon.Messaging;

namespace Antiphon.Messaging.Gateway.Testing;

/// <summary>In-memory inbox for the inbound-unconsumed monitor (tests + fake gateway).</summary>
public sealed class InMemoryInboxReceiptStore : IInboxReceiptStore, IInboundReceiptSink
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, InboundReceipt> _byId = [];
    private readonly Dictionary<(string Channel, string MessageId), Guid> _byKey = [];

    public IReadOnlyList<InboundReceipt> All
    {
        get { lock (_gate) return _byId.Values.ToList(); }
    }

    public Task RecordAsync(
        ChannelMessage message, string envelopeJson, string topic, int partition, long offset, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = (message.Channel, message.ChannelMessageId);
            if (_byKey.TryGetValue(key, out var existingId))
            {
                var existing = _byId[existingId];
                if (existing.Offset == 0 && existing.Topic.Length == 0)
                {
                    _byId[existingId] = existing with { Topic = topic, Partition = partition, Offset = offset };
                }
                return Task.CompletedTask;
            }

            var id = Guid.NewGuid();
            _byKey[key] = id;
            _byId[id] = new InboundReceipt
            {
                Id = id,
                Channel = message.Channel,
                ChannelMessageId = message.ChannelMessageId,
                ConversationId = message.Conversation.Id,
                ReplyHandle = message.ReplyHandle,
                FirstSeenAt = message.Timestamp.ToUniversalTime(),
                Topic = topic,
                Partition = partition,
                Offset = offset,
            };
        }
        return Task.CompletedTask;
    }

    public void Add(InboundReceipt receipt)
    {
        lock (_gate)
        {
            _byId[receipt.Id] = receipt;
            _byKey[(receipt.Channel, receipt.ChannelMessageId)] = receipt.Id;
        }
    }

    public Task<IReadOnlyList<InboundReceipt>> GetOverdueAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<InboundReceipt> list = _byId.Values
                .Where(r => r.FirstSeenAt <= cutoff && r.Topic.Length > 0)
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task MarkAcknowledgedAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var receipt))
                _byId[id] = receipt with { AcknowledgedAt = at, NextAckAttemptAt = null };
        }
        return Task.CompletedTask;
    }

    public Task MarkOperationalEventPublishedAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var receipt))
                _byId[id] = receipt with { OperationalEventPublishedAt = at };
        }
        return Task.CompletedTask;
    }

    public Task ScheduleAckRetryAsync(Guid id, DateTimeOffset nextAttemptAt, int attemptCount, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var receipt))
                _byId[id] = receipt with { NextAckAttemptAt = nextAttemptAt, AckAttemptCount = attemptCount };
        }
        return Task.CompletedTask;
    }
}
