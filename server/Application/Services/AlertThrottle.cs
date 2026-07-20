using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Singleton per-sink accumulator behind the hard send window (Q6): alerts pile up grouped by
/// dedup key; the flusher drains a sink only when its window has elapsed (or a Critical arrives
/// with the bypass enabled). Generalises the WatchdogCooldownStore pattern.
/// </summary>
public sealed class AlertThrottle
{
    public sealed class PendingGroup
    {
        public required string DedupKey { get; init; }
        public required Guid ExemplarAlertId { get; init; }
        public AlertSeverity Severity { get; set; }
        public required string Source { get; init; }
        public required string Title { get; init; }
        public string? Detail { get; set; }
        public int Count { get; set; }
    }

    private sealed class SinkState
    {
        public readonly Dictionary<string, PendingGroup> Pending = new(StringComparer.Ordinal);
        public DateTime LastSentUtc = DateTime.MinValue;
        public bool HasCritical;
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, SinkState> _sinks = new();

    public void Enqueue(Guid sinkChannelId, Alert alert)
    {
        lock (_gate)
        {
            if (!_sinks.TryGetValue(sinkChannelId, out var sink))
                _sinks[sinkChannelId] = sink = new SinkState();

            if (sink.Pending.TryGetValue(alert.DedupKey, out var group))
            {
                group.Count++;
                group.Detail = alert.Detail ?? group.Detail;
                if (alert.Severity > group.Severity)
                    group.Severity = alert.Severity;
            }
            else
            {
                sink.Pending[alert.DedupKey] = new PendingGroup
                {
                    DedupKey = alert.DedupKey,
                    ExemplarAlertId = alert.Id,
                    Severity = alert.Severity,
                    Source = alert.Source,
                    Title = alert.Title,
                    Detail = alert.Detail,
                    Count = 1,
                };
            }

            if (alert.Severity == AlertSeverity.Critical)
                sink.HasCritical = true;
        }
    }

    /// <summary>
    /// Drains every sink whose window has elapsed (or that holds a Critical while the bypass is
    /// on). Draining stamps LastSent, so the hard window restarts from this flush.
    /// </summary>
    public IReadOnlyList<(Guid SinkChannelId, IReadOnlyList<PendingGroup> Groups)> CollectDue(
        DateTime nowUtc, TimeSpan window, bool criticalBypass)
    {
        var due = new List<(Guid, IReadOnlyList<PendingGroup>)>();
        lock (_gate)
        {
            foreach (var (sinkId, sink) in _sinks)
            {
                if (sink.Pending.Count == 0)
                    continue;

                var windowOpen = nowUtc - sink.LastSentUtc >= window;
                if (!windowOpen && !(criticalBypass && sink.HasCritical))
                    continue;

                due.Add((sinkId, sink.Pending.Values
                    .OrderByDescending(g => g.Severity)
                    .ThenByDescending(g => g.Count)
                    .ToList()));
                sink.Pending.Clear();
                sink.HasCritical = false;
                sink.LastSentUtc = nowUtc;
            }
        }

        return due;
    }
}
