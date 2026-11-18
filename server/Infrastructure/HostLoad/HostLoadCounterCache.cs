using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.HostLoad;

/// <summary>Observation times stay attached to counts; a replay never borrows today's count.</summary>
public sealed class HostLoadCounterCache
{
    private readonly HostLoadRunnerKeys _keys;
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<HostLoadCounters>> _history = new(StringComparer.Ordinal);

    public HostLoadCounterCache() : this(HostLoadRunnerKeys.Default) { }
    public HostLoadCounterCache(HostLoadRunnerKeys keys) => _keys = keys;

    public void Observe(string runnerId, HostLoadCounters counters)
    {
        lock (_gate)
        {
            if (!_history.TryGetValue(_keys.Canonical(runnerId), out var rows)) _history[_keys.Canonical(runnerId)] = rows = new();
            rows.Enqueue(counters); while (rows.Count > 720) rows.Dequeue();
        }
    }
    public HostLoadCounters Nearest(string runnerId, DateTimeOffset sampledAt)
    {
        lock (_gate)
        {
            var nearest = _history.GetValueOrDefault(_keys.Canonical(runnerId))?
                .Where(c => c.CountedAt is { } at && (at - sampledAt).Duration() <= TimeSpan.FromSeconds(15))
                .OrderBy(c => (c.CountedAt!.Value - sampledAt).Duration()).ThenBy(c => c.CountedAt).FirstOrDefault();
            return nearest ?? new(null, null, null, null, null, null, null, null, null, null);
        }
    }

    /// <summary>Default-key fold. Production readers use an injected <see cref="HostLoadRunnerKeys"/>.</summary>
    public static string Canonical(string id) => HostLoadRunnerKeys.Default.Canonical(id);
}
