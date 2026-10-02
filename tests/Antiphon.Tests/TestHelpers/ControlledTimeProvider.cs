using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.TestHelpers;

/// <summary>A manual clock with an observable timer registration/change barrier.</summary>
internal sealed class ControlledTimeProvider : TimeProvider
{
    private readonly FakeTimeProvider _inner;
    private readonly object _gate = new();
    private readonly List<TimerEvent> _events = [];
    private TaskCompletionSource _changed = NewSignal();
    private int _nextId;

    public ControlledTimeProvider(DateTimeOffset? start = null) =>
        _inner = new FakeTimeProvider(start ?? DateTimeOffset.Parse("2026-09-25T00:00:00Z"));

    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();
    public override long GetTimestamp() => _inner.GetTimestamp();
    public override long TimestampFrequency => _inner.TimestampFrequency;
    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    public IReadOnlyList<TimerEvent> Events
    {
        get { lock (_gate) return _events.ToArray(); }
    }

    public void AdvanceTo(DateTimeOffset target)
    {
        var delta = target - GetUtcNow();
        if (delta < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(target));
        _inner.Advance(delta);
    }

    public void Advance(TimeSpan delta) => _inner.Advance(delta);

    public async Task<TimerEvent> WaitForTimerAsync(
        Func<TimerEvent, bool> predicate, int afterSequence = 0, CancellationToken ct = default)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_events.LastOrDefault(e => e.Sequence > afterSequence && predicate(e)) is { } found)
                    return found;
                changed = _changed.Task;
            }
            await changed.WaitAsync(ct);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var id = Interlocked.Increment(ref _nextId);
        var timer = _inner.CreateTimer(callback, state, dueTime, period);
        Record(id, "create", dueTime, period);
        return new ObservedTimer(this, id, timer);
    }

    private void Record(int id, string action, TimeSpan due, TimeSpan period)
    {
        lock (_gate)
        {
            _events.Add(new TimerEvent(_events.Count + 1, id, action, GetUtcNow(), due, period));
            _changed.TrySetResult();
            _changed = NewSignal();
        }
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal sealed record TimerEvent(
        int Sequence, int TimerId, string Action, DateTimeOffset RegisteredAt,
        TimeSpan DueTime, TimeSpan Period)
    {
        public DateTimeOffset Deadline => RegisteredAt + DueTime;
    }

    private sealed class ObservedTimer(ControlledTimeProvider owner, int id, ITimer inner) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            var changed = inner.Change(dueTime, period);
            if (changed) owner.Record(id, "change", dueTime, period);
            return changed;
        }

        public void Dispose() => inner.Dispose();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
