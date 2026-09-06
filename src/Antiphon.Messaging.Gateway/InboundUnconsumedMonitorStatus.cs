using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Messaging.Gateway;

public enum MonitorState { Validating, Ready, Degraded, Stale, Disabled }

public sealed record InboundUnconsumedMonitorSnapshot(
    MonitorState State, string? ReasonCode, string? ExpectedGroup, string WatchedGroup, string Topic,
    DateTimeOffset? ObservedAt, double? AgeSeconds, IReadOnlyList<PartitionOffsetObservation> Partitions)
{
    [JsonIgnore]
    public int HttpStatusCode => State is MonitorState.Ready or MonitorState.Disabled ? 200 : 503;
}

public interface IInboundUnconsumedMonitorStatus
{
    InboundUnconsumedMonitorSnapshot GetSnapshot();
}

public sealed class InboundUnconsumedMonitorStatus : IInboundUnconsumedMonitorStatus
{
    private readonly object _gate = new();
    private readonly AntiphonGatewayOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<InboundUnconsumedMonitorStatus> _logger;
    private InboundUnconsumedMonitorSnapshot _snapshot;
    private DateTimeOffset? _lastError;

    public InboundUnconsumedMonitorStatus(IOptions<AntiphonGatewayOptions> options, TimeProvider time,
        ILogger<InboundUnconsumedMonitorStatus> logger)
    {
        _options = options.Value;
        _time = time;
        _logger = logger;
        _snapshot = new(MonitorState.Validating, null, _options.ExpectedAntiphonConsumerGroup,
            _options.AntiphonConsumerGroup, _options.ResolveInboundTopic(), null, null, []);
    }

    public InboundUnconsumedMonitorSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var age = _snapshot.ObservedAt.HasValue ? Math.Max(0, (_time.GetUtcNow() - _snapshot.ObservedAt.Value).TotalSeconds) : (double?)null;
            return _snapshot with
            {
                AgeSeconds = age,
                State = _snapshot.State != MonitorState.Disabled && age > 2 * _options.InboundUnconsumedPollSeconds + _options.ObservationBudgetSeconds
                    ? MonitorState.Stale : _snapshot.State,
            };
        }
    }

    internal void Disable(bool noStore)
    {
        lock (_gate) _snapshot = _snapshot with { State = MonitorState.Disabled, ReasonCode = noStore ? "no_inbox_store" : null };
    }

    internal void Update(ConsumerGroupObservation observation, string? failure = null)
    {
        lock (_gate)
        {
            var reason = failure ?? observation.ReasonCode;
            var ready = failure is null && observation.GroupStatus == ConsumerGroupStatus.Present &&
                observation.Partitions.Any(p => p.Status == PartitionOffsetStatus.CommittedOffset && p.CommittedNextOffset is >= 0) &&
                !observation.Partitions.Any(p => p.Status == PartitionOffsetStatus.QueryFailed);
            if (!ready) reason ??= "no_committed_offsets";
            var state = ready ? MonitorState.Ready : MonitorState.Degraded;
            var now = _time.GetUtcNow();
            if (state == MonitorState.Degraded && (_snapshot.State != MonitorState.Degraded || _lastError is null || now - _lastError >= TimeSpan.FromMinutes(5)))
            {
                _logger.LogError("[inbound-unconsumed] {ReasonCode} watched group {Group} topic {Topic}", reason, _options.AntiphonConsumerGroup, _options.ResolveInboundTopic());
                _lastError = now;
            }
            if (state == MonitorState.Ready && _snapshot.State != MonitorState.Ready)
                _logger.LogInformation("[inbound-unconsumed] monitor ready group {Group} topic {Topic}", _options.AntiphonConsumerGroup, _options.ResolveInboundTopic());
            _snapshot = _snapshot with { State = state, ReasonCode = reason, ObservedAt = observation.ObservedAt, Partitions = observation.Partitions.ToArray() };
        }
    }
}
