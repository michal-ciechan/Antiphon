using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1489 S1: the broker's lease lifecycle over the last <see cref="Window"/>, for
/// <c>GET /build-slots/metrics</c>. Not thread-safe: <see cref="BuildSlotBroker"/> calls it under its
/// own lock. At most <see cref="Capacity"/> lifecycle records (grant, release, reap, waiter drop) are
/// kept; the oldest go first and the snapshot says so. Refusal polls are per-minute counters, so a
/// long queue's polling cannot push grants out. <see cref="Snapshot"/> neither prunes nor records.
/// </summary>
public sealed class BuildSlotMetricsRecorder(DateTime startedAtUtc)
{
    public const int Capacity = 4096;
    public static readonly TimeSpan Window = TimeSpan.FromHours(3);

    private readonly Queue<Record> _records = new();
    private readonly Queue<RefusalMinute> _refusals = new();
    private RefusalMinute? _currentMinute;
    private long _dropped;
    private DateTime? _lastDroppedAtUtc;

    public void Granted(DateTime now, TimeSpan waited) => Add(new Record(Kind.Grant, now, waited));
    public void Released(DateTime now, TimeSpan held) => Add(new Record(Kind.Release, now, held));
    public void Reaped(DateTime now, TimeSpan held) => Add(new Record(Kind.Reap, now, held));
    public void WaiterDropped(DateTime now) => Add(new Record(Kind.WaiterDrop, now, TimeSpan.Zero));

    public void Refused(DateTime now, bool memoryFloor)
    {
        var minute = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        if (_currentMinute is null || _currentMinute.MinuteUtc != minute)
        {
            _currentMinute = new RefusalMinute(minute);
            _refusals.Enqueue(_currentMinute);
        }
        if (memoryFloor) _currentMinute.MemoryFloor++;
        else _currentMinute.Busy++;
        while (_refusals.Peek().MinuteUtc < now - Window - TimeSpan.FromMinutes(1))
            _refusals.Dequeue();
    }

    public BuildSlotMetrics Snapshot(DateTime now)
    {
        var nominal = now - Window;
        var start = startedAtUtc > nominal ? startedAtUtc : nominal;
        var truncated = _lastDroppedAtUtc is { } dropped && dropped >= start;
        if (truncated && _records.Count > 0 && _records.Peek().AtUtc > start)
            start = _records.Peek().AtUtc;

        var inWindow = _records.Where(r => r.AtUtc >= start && r.AtUtc <= now).ToList();
        var startMinute = new DateTime(start.Ticks - start.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
        var refusals = _refusals.Where(b => b.MinuteUtc >= startMinute && b.MinuteUtc <= now).ToList();
        var windowSeconds = (now - start).TotalSeconds;
        var releases = inWindow.Count(r => r.Kind == Kind.Release);
        return new BuildSlotMetrics(
            startedAtUtc,
            now,
            start,
            windowSeconds,
            PartialWindow: startedAtUtc > nominal,
            Truncated: truncated,
            DroppedRecords: _dropped,
            RetainedRecords: _records.Count,
            RecordCapacity: Capacity,
            Grants: inWindow.Count(r => r.Kind == Kind.Grant),
            Releases: releases,
            Reaps: inWindow.Count(r => r.Kind == Kind.Reap),
            WaiterDrops: inWindow.Count(r => r.Kind == Kind.WaiterDrop),
            BusyRefusals: refusals.Sum(b => b.Busy),
            MemoryFloorRefusals: refusals.Sum(b => b.MemoryFloor),
            AcquireWait: Stats(inWindow.Where(r => r.Kind == Kind.Grant)),
            Held: Stats(inWindow.Where(r => r.Kind is Kind.Release or Kind.Reap)),
            ReleasesPerHour: windowSeconds > 0 ? releases / (windowSeconds / 3600) : null);
    }

    private void Add(Record record)
    {
        _records.Enqueue(record);
        while (_records.Count > 0 && _records.Peek().AtUtc < record.AtUtc - Window)
            _records.Dequeue();
        while (_records.Count > Capacity)
        {
            _lastDroppedAtUtc = _records.Dequeue().AtUtc;
            _dropped++;
        }
    }

    private static BuildSlotDurationStats Stats(IEnumerable<Record> records)
    {
        var seconds = records.Select(r => r.Duration.TotalSeconds).Order().ToList();
        if (seconds.Count == 0)
            return new BuildSlotDurationStats(0, null, null, null);
        var rank = (int)Math.Ceiling(0.95 * seconds.Count);
        return new BuildSlotDurationStats(seconds.Count, seconds.Average(), seconds[rank - 1], seconds[^1]);
    }

    private enum Kind { Grant, Release, Reap, WaiterDrop }

    private sealed record Record(Kind Kind, DateTime AtUtc, TimeSpan Duration);

    private sealed class RefusalMinute(DateTime minuteUtc)
    {
        public DateTime MinuteUtc { get; } = minuteUtc;
        public int Busy { get; set; }
        public int MemoryFloor { get; set; }
    }
}
