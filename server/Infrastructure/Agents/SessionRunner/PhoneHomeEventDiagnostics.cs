using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>The reservation travels with the event, including through scheduler queues.</summary>
public sealed class PhoneHomeQueuedEvent(
    PhoneHomeFrame frame, int payloadBytes, long enqueuedAt, Action<PhoneHomeQueuedEvent, bool> release)
{
    private int _released;
    public PhoneHomeFrame Frame { get; } = frame;
    public int PayloadBytes { get; } = payloadBytes;
    public long EnqueuedAt { get; } = enqueuedAt;
    internal LinkedListNode<PhoneHomeQueuedEvent>? Node { get; set; }
    public void Release() => Finish(true);
    internal void Abandon() => Finish(false);
    private void Finish(bool observed)
    {
        if (Interlocked.Exchange(ref _released, 1) == 0) release(this, observed);
    }
}

public sealed record PhoneHomeAccountingSnapshot(
    int PendingEvents, int PendingEventBytes, long ReceivedEvents, long ReceivedPayloadBytes,
    long ReleasedEvents, long ReleasedPayloadBytes, double OldestPendingMs);
public sealed record PhoneHomeOverflowSnapshot(PhoneHomeAccountingSnapshot At, int IncomingPayloadBytes, string Dimension);
public sealed record PhoneHomeEventKindCounts(string EventName, long ReceivedEvents, long ReceivedPayloadBytes,
    long ReleasedEvents, long ReleasedPayloadBytes);
public sealed record PhoneHomeProducerSample(Guid SessionId, long Events, long PayloadBytes);
public sealed record PhoneHomeActiveObserver(int Worker, Guid SessionId, string EventName, string Phase, double ElapsedMs);
public sealed record PhoneHomeDiagnosticSnapshot(PhoneHomeEventKindCounts[] Kinds, PhoneHomeProducerSample[] Sessions,
    long OtherSessionEvents, long OtherSessionPayloadBytes, PhoneHomeActiveObserver[] Active,
    IReadOnlyDictionary<string, double> PhaseMaxMs);

/// <summary>Fixed kind buckets, at most 16 producer samples and at most 16 active workers.
/// Only metadata is retained; payloads never enter this accumulator.</summary>
public sealed class PhoneHomeEventDiagnostics(TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PhoneHomeEventKindCounts> _kinds = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, PhoneHomeProducerSample> _sessions = new();
    private readonly Dictionary<int, Active> _active = new();
    private readonly Dictionary<string, double> _phaseMax = new(StringComparer.Ordinal);
    private long _otherEvents, _otherBytes;
    private long? _lastWarning;

    public void Admitted(string? eventName, int bytes)
    {
        var kind = Kind(eventName);
        lock (_gate)
        {
            var old = _kinds.GetValueOrDefault(kind) ?? new(kind, 0, 0, 0, 0);
            _kinds[kind] = old with { ReceivedEvents = checked(old.ReceivedEvents + 1), ReceivedPayloadBytes = checked(old.ReceivedPayloadBytes + bytes) };
        }
    }
    public void Released(string? eventName, int bytes)
    {
        var kind = Kind(eventName);
        lock (_gate)
        {
            var old = _kinds[kind];
            _kinds[kind] = old with { ReleasedEvents = checked(old.ReleasedEvents + 1), ReleasedPayloadBytes = checked(old.ReleasedPayloadBytes + bytes) };
        }
    }
    public void Producer(Guid sessionId, int bytes)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var sample))
                _sessions[sessionId] = sample with { Events = checked(sample.Events + 1), PayloadBytes = checked(sample.PayloadBytes + bytes) };
            else if (_sessions.Count < 16) _sessions.Add(sessionId, new(sessionId, 1, bytes));
            else { _otherEvents = checked(_otherEvents + 1); _otherBytes = checked(_otherBytes + bytes); }
        }
    }
    internal void Phase(int worker, Guid sessionId, string eventName, string phase)
    {
        lock (_gate)
        {
            EndUnderLock(worker);
            _active[worker] = new(sessionId, Kind(eventName), phase, clock.GetTimestamp());
        }
    }
    internal void End(int worker) { lock (_gate) EndUnderLock(worker); }
    internal void Parsed(long started)
    {
        lock (_gate) RecordMax("parse", clock.GetElapsedTime(started).TotalMilliseconds);
    }
    private void EndUnderLock(int worker)
    {
        if (_active.Remove(worker, out var old)) RecordMax(old.Phase, clock.GetElapsedTime(old.Started).TotalMilliseconds);
    }
    private void RecordMax(string phase, double ms) => _phaseMax[phase] = Math.Max(_phaseMax.GetValueOrDefault(phase), ms);
    public PhoneHomeDiagnosticSnapshot Snapshot()
    {
        lock (_gate)
        {
            var active = ActiveSnapshot();
            var maxima = new Dictionary<string, double>(_phaseMax, StringComparer.Ordinal);
            foreach (var item in active) maxima[item.Phase] = Math.Max(maxima.GetValueOrDefault(item.Phase), item.ElapsedMs);
            return new(_kinds.Values.ToArray(), _sessions.Values.ToArray(), _otherEvents, _otherBytes, active, maxima);
        }
    }
    internal PhoneHomeActiveObserver? TakeSlowWarning()
    {
        lock (_gate)
        {
            if (_lastWarning is { } last && clock.GetElapsedTime(last) < TimeSpan.FromSeconds(10)) return null;
            var slow = ActiveSnapshot().Where(a => a.ElapsedMs >= 1000).OrderByDescending(a => a.ElapsedMs).FirstOrDefault();
            if (slow is not null) _lastWarning = clock.GetTimestamp();
            return slow;
        }
    }
    private PhoneHomeActiveObserver[] ActiveSnapshot() => _active.Select(pair => new PhoneHomeActiveObserver(
        pair.Key, pair.Value.SessionId, pair.Value.EventName, pair.Value.Phase,
        clock.GetElapsedTime(pair.Value.Started).TotalMilliseconds)).ToArray();
    private static string Kind(string? name) => name switch
    {
        SessionRunnerEventNames.SessionOutput => SessionRunnerEventNames.SessionOutput,
        SessionRunnerEventNames.SessionTranscript => SessionRunnerEventNames.SessionTranscript,
        SessionRunnerEventNames.SessionExited => SessionRunnerEventNames.SessionExited,
        SessionRunnerEventNames.SessionTranscriptFault => SessionRunnerEventNames.SessionTranscriptFault,
        SessionRunnerEventNames.SessionTranscriptBound => SessionRunnerEventNames.SessionTranscriptBound,
        _ => "unknown"
    };
    private sealed record Active(Guid SessionId, string EventName, string Phase, long Started);
}
