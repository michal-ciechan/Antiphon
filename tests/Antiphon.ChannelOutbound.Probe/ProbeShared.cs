using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

// What a unified recovery graph (UnifiedRecoveryProbe) needs besides the server: the probe's own config, clock, event
// bus and session stopper. Antiphon.Tests compiles this file and the graph too, so a restart's recovery can run in
// the test process (UnifiedOutboundTransport.RecoverAsync) on the same code as a recovery child.
internal sealed record ProbeConfig(string ConnectionString, string StoreRoot, Guid DeliveryId,
    string EvidencePath, string MarkerPath, string? Barrier, int ClockOffsetSeconds,
    string? Mode = null, string WorkspaceRoot = "", Guid ProjectId = default,
    Guid ConverterAgentId = default, Guid ChannelId = default, Guid SessionId = default,
    Guid CorrelationId = default, bool AllowPublication = false,
    bool FailPublishCommit = false, string? BootstrapServers = null, string? Topic = null,
    string? WorkerExe = null, string? WorkerHome = null, string? WorkerGate = null,
    string? LaunchSpecPath = null, string? ExpectedSourceSha = null, string? StallPoint = null,
    int DeadlineSeconds = 30, string? CompiledModelPath = null);

// Wall time moved by the parent's offset. A copy made WithTimerScale below 1 fires its timers (the recovery host's
// scan cycles) that much sooner; the time it reads is the same.
internal sealed class ProbeClock : TimeProvider
{
    private readonly int _offsetSeconds;
    private readonly double _timerScale;

    public ProbeClock(int offsetSeconds) : this(offsetSeconds, 1) { }

    private ProbeClock(int offsetSeconds, double timerScale)
    {
        if (!double.IsFinite(timerScale) || timerScale <= 0 || timerScale > 1)
            throw new ArgumentOutOfRangeException(nameof(timerScale), timerScale, "A timer scale is in (0, 1].");
        _offsetSeconds = offsetSeconds;
        _timerScale = timerScale;
    }

    public ProbeClock WithTimerScale(double timerScale) => new(_offsetSeconds, timerScale);

    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow().AddSeconds(_offsetSeconds);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        _timerScale == 1
            ? base.CreateTimer(callback, state, dueTime, period)
            : new ScaledTimer(base.CreateTimer(callback, state, Scale(dueTime), Scale(period)), this);

    // Zero, infinite (negative) and sub-millisecond spans keep their meaning; one of 1 ms or more stays at least 1 ms
    // (a 0 period would mean "do not repeat"). A span past the system timer's limit is not refused here; the host's
    // 1 s scan interval is far inside it.
    private TimeSpan Scale(TimeSpan span) =>
        span < TimeSpan.FromMilliseconds(1)
            ? span
            : TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond, (long)(span.Ticks * _timerScale)));

    // Both the spans a timer is created with and any later Change (PeriodicTimer's Period setter) are scaled.
    private sealed class ScaledTimer(ITimer inner, ProbeClock clock) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(clock.Scale(dueTime), clock.Scale(period));
        public void Dispose() => inner.Dispose();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

internal sealed class ProbeEventBus : IEventBus
{
    public Task PublishToGroupAsync(string group, string eventName, object payload,
        CancellationToken ct = default) => Task.CompletedTask;
    public Task PublishToAllAsync(string eventName, object payload,
        CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class RefusingSessionStopper : IDelegateSessionStopper
{
    public Task KillAsync(Guid sessionId, CancellationToken ct) =>
        throw new InvalidOperationException("Unexpected external session stop from conversion probe.");
    public Task KillAsync(Guid sessionId, SessionTerminationSource source, CancellationToken ct) =>
        throw new InvalidOperationException("Unexpected external session stop from conversion probe.");
}
