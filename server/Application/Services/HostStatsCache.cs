using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

public enum HostStatsFailureKind { Transient, Offline, Unsupported }

public sealed class HostStatsCache(HostStatsSettings settings, TimeProvider time)
{
    private sealed record Entry(RunnerHostStatsDto? Snapshot, DateTimeOffset? ObservedAt, HostStatsFailureKind? Failure);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _states = new(StringComparer.Ordinal);
    private IReadOnlyList<SessionRunnerCatalogueEntryDto> _catalogue = [];
    private IReadOnlyDictionary<string, HostStatsAntiphonDto> _counters = new Dictionary<string, HostStatsAntiphonDto>();
    private string? _counterSignature;
    private bool _changed;

    public void BeginTick() { }

    public void Record(string hostId, RunnerHostStatsDto dto)
    {
        lock (_gate)
        {
            _entries.TryGetValue(hostId, out var previous);
            if (previous?.Snapshot?.At != dto.At || previous?.Failure is not null)
                _changed = true;
            var observedAt = previous?.Snapshot?.At == dto.At
                ? previous.ObservedAt
                : time.GetUtcNow();
            _entries[hostId] = new Entry(dto, observedAt, null);
        }
    }

    public void RecordFailure(string hostId, HostStatsFailureKind kind)
    {
        lock (_gate)
        {
            _entries.TryGetValue(hostId, out var previous);
            if (previous?.Failure != kind)
                _changed = true;
            _entries[hostId] = new Entry(previous?.Snapshot, previous?.ObservedAt, kind);
        }
    }

    public bool Changed { get { lock (_gate) return _changed; } }
    public void Published() { lock (_gate) _changed = false; }

    public void SetProjection(IReadOnlyList<SessionRunnerCatalogueEntryDto> catalogue,
        IReadOnlyDictionary<string, HostStatsAntiphonDto> counters)
    {
        lock (_gate)
        {
            var signature = System.Text.Json.JsonSerializer.Serialize(counters.OrderBy(x => x.Key));
            if (_counterSignature != signature || !_catalogue.Select(x => x.RunnerId).SequenceEqual(catalogue.Select(x => x.RunnerId)))
                _changed = true;
            _counterSignature = signature;
            _catalogue = catalogue;
            _counters = counters;
        }
    }

    public IReadOnlyList<HostStatsDto> Project() => Project(_catalogue, _counters);

    public IReadOnlyList<HostStatsDto> Project(
        IReadOnlyList<SessionRunnerCatalogueEntryDto> catalogue,
        IReadOnlyDictionary<string, HostStatsAntiphonDto>? counters = null)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            return catalogue.Select(row =>
            {
                _entries.TryGetValue(row.RunnerId, out var entry);
                var state = !settings.Enabled ? "offline"
                    : entry?.Failure == HostStatsFailureKind.Unsupported ? "unsupported"
                    : (!row.Available && row.RunnerId != RunnerPlatformWire.DesktopId)
                        || entry?.Failure == HostStatsFailureKind.Offline ? "offline"
                    : entry?.Snapshot is null ? "offline"
                    : entry.ObservedAt is { } at && now - at <= TimeSpan.FromMilliseconds(settings.StaleAfterMs)
                        ? "live" : "stale";
                if (!_states.TryGetValue(row.RunnerId, out var old) || old != state)
                {
                    _states[row.RunnerId] = state;
                    _changed = true;
                }
                var snapshot = entry?.Snapshot;
                HostStatsAntiphonDto? antiphon = null;
                if (counters is not null)
                    counters.TryGetValue(row.RunnerId, out antiphon);
                antiphon ??= new HostStatsAntiphonDto(0, new Dictionary<string, int>(),
                    new Dictionary<string, int>(), 0, 0, 0, null, row.Capacity, snapshot?.BuildSlots);
                antiphon = antiphon with { SeatsDeclared = row.Capacity, BuildSlots = snapshot?.BuildSlots };
                return new HostStatsDto(row.RunnerId, row.DisplayName, row.Platform, state,
                    !settings.Enabled ? "disabled" : state == "offline" ? row.UnavailableReason : null,
                    entry?.ObservedAt, snapshot?.IntervalSeconds, snapshot?.Cores,
                    state == "unsupported" ? null : snapshot?.Current,
                    state == "unsupported" ? null : snapshot?.Rollups, antiphon);
            }).ToArray();
        }
    }
}
