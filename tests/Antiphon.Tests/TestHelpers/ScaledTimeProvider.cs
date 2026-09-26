using System.Diagnostics;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Offset over the real clock whose timers run at a fixed multiple of real time.
/// A frozen <see cref="TimeProvider"/> hangs the queue's poll loops (CARD-0222):
/// <see cref="Advance"/> moves <see cref="GetUtcNow"/> only and does not fire pending timers.
/// </summary>
internal sealed class ScaledTimeProvider : TimeProvider
{
    private readonly double _speed;
    private readonly DateTimeOffset _start;
    private readonly long _origin;
    private TimeSpan _offset;

    public ScaledTimeProvider(double speed, DateTimeOffset? start = null)
    {
        if (double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed));
        _speed = speed;
        _start = start ?? DateTimeOffset.UtcNow;
        _origin = Stopwatch.GetTimestamp();
    }

    /// <summary>Jumps <see cref="GetUtcNow"/> without completing a pending <see cref="Task.Delay"/>.</summary>
    public void Advance(TimeSpan by) => _offset += by;

    public override DateTimeOffset GetUtcNow()
    {
        var elapsed = Stopwatch.GetElapsedTime(_origin);
        var scaled = TimeSpan.FromTicks((long)Math.Round(elapsed.Ticks * _speed));
        return _start + _offset + scaled;
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long TimestampFrequency => Stopwatch.Frequency;

    public override long GetTimestamp()
    {
        var scaled = (long)Math.Round((Stopwatch.GetTimestamp() - _origin) * _speed);
        var offset = (long)Math.Round(_offset.Ticks * (TimestampFrequency / (double)TimeSpan.TicksPerSecond));
        return _origin + scaled + offset;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        TimeProvider.System.CreateTimer(callback, state, Scale(dueTime), Scale(period));

    private TimeSpan Scale(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
            return value;
        var milliseconds = Math.Ceiling(value.TotalMilliseconds / _speed);
        if (milliseconds < 1)
            milliseconds = 1;
        return TimeSpan.FromMilliseconds(milliseconds);
    }
}
