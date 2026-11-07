using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.HostLoad;

/// <summary>Observation times stay attached to counts; a replay never borrows today's count.</summary>
public sealed class HostLoadCounterCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<HostLoadCounters>> _history = new(StringComparer.Ordinal);
    public void Observe(string runnerId, HostLoadCounters counters)
    {
        lock (_gate)
        {
            if (!_history.TryGetValue(Canonical(runnerId), out var rows)) _history[Canonical(runnerId)] = rows = new();
            rows.Enqueue(counters); while (rows.Count > 720) rows.Dequeue();
        }
    }
    public HostLoadCounters Nearest(string runnerId, DateTimeOffset sampledAt)
    {
        lock (_gate)
        {
            var nearest = _history.GetValueOrDefault(Canonical(runnerId))?
                .Where(c => c.CountedAt is { } at && (at - sampledAt).Duration() <= TimeSpan.FromSeconds(15))
                .OrderBy(c => (c.CountedAt!.Value - sampledAt).Duration()).ThenBy(c => c.CountedAt).FirstOrDefault();
            return nearest ?? new(null, null, null, null, null, null, null, null, null, null);
        }
    }
    public static string Canonical(string id) => id is "local" or "desktop" ? "desktop" : id;
}
