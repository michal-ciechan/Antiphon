using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Singleton record of which dispatcher sweeps are still running from an earlier tick (CARD-0633
/// D-3). A sweep that overran its budget and grace is abandoned on its own service scope; until it
/// actually finishes, the next tick skips that sweep by name rather than starting a second copy,
/// so a reproducible hang leaks at most one scope per sweep name.
///
/// <para>A singleton for the same reason <see cref="DeadSessionFirstSeenState"/> is one: the
/// dispatcher is scoped and rebuilt every tick. In memory on purpose; a restart has no running
/// sweeps.</para>
/// </summary>
public sealed class SweepInFlightState
{
    private readonly ConcurrentDictionary<string, Entry> _running = new(StringComparer.Ordinal);

    /// <summary>Claim <paramref name="name"/>; false (with the holder's start) when it is still running.</summary>
    public bool TryBegin(string name, DateTime now, out DateTime runningSince)
    {
        var entry = new Entry(now);
        var current = _running.GetOrAdd(name, entry);
        runningSince = current.Since;
        return ReferenceEquals(current, entry);
    }

    /// <summary>Release <paramref name="name"/> once its sweep has really finished.</summary>
    public void End(string name)
    {
        if (_running.TryRemove(name, out var entry))
            entry.Released.TrySetResult();
    }

    public bool IsRunning(string name) => _running.ContainsKey(name);

    /// <summary>Completes when <paramref name="name"/> is no longer running (tests and diagnostics).</summary>
    public Task WhenReleasedAsync(string name) =>
        _running.TryGetValue(name, out var entry) ? entry.Released.Task : Task.CompletedTask;

    private sealed class Entry(DateTime since)
    {
        public DateTime Since { get; } = since;
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
