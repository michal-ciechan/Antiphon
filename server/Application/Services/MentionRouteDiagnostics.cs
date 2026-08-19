using System.Collections.Concurrent;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// In-memory mention-route stage trail. CARD-0050 S4: when a channel-routing
/// test times out, the last recorded stage names where the pipeline stopped
/// (debounce / dequeue / DB / publish) instead of a bare 5s predicate miss.
/// Recording is append-only and never participates in routing control flow.
/// </summary>
public sealed class MentionRouteDiagnostics
{
    public const string DeltaObserved = "delta-observed";
    public const string PendingScheduled = "pending-scheduled";
    public const string PendingCleared = "pending-cleared";
    public const string DebounceDelayStarted = "debounce-delay-started";
    public const string DebounceFired = "debounce-fired";
    public const string DebounceStale = "debounce-stale";
    public const string DebounceCancelled = "debounce-cancelled";
    public const string DebounceNoMentions = "debounce-no-mentions";
    public const string DebounceNotReady = "debounce-not-ready";
    public const string CommandEnqueued = "command-enqueued";
    public const string CommandDropped = "command-dropped";
    public const string CommandSkippedDuplicate = "command-skipped-duplicate";
    public const string CommandDequeued = "command-dequeued";
    public const string RouteStarted = "route-started";
    public const string SourceQueryReturned = "source-query-returned";
    public const string TargetQueryReturned = "target-query-returned";
    public const string InputSent = "input-sent";
    public const string EventPublished = "event-published";
    public const string RouteReturned = "route-returned";
    public const string RouteFailed = "route-failed";

    private const int MaxEntries = 256;
    private readonly ConcurrentQueue<MentionRouteStage> _entries = new();
    private int _count;
    private long _t0Ticks;

    public void Record(Guid sourceSessionId, string stage, string? detail = null)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            Interlocked.CompareExchange(ref _t0Ticks, now.UtcTicks, 0);
            _entries.Enqueue(new MentionRouteStage(now, sourceSessionId, stage, detail));
            if (Interlocked.Increment(ref _count) > MaxEntries && _entries.TryDequeue(out _))
                Interlocked.Decrement(ref _count);
        }
        catch
        {
            // Diagnostics must never affect routing.
        }
    }

    public IReadOnlyList<MentionRouteStage> Snapshot() => _entries.ToArray();

    public MentionRouteStage? Last
    {
        get
        {
            var entries = Snapshot();
            return entries.Count == 0 ? null : entries[^1];
        }
    }

    public string Format(Guid? sourceSessionId = null)
    {
        var entries = Snapshot();
        if (sourceSessionId is Guid id)
            entries = entries.Where(e => e.SourceSessionId == id).ToArray();
        if (entries.Count == 0)
            return "(no mention-route stages recorded)";

        var t0 = new DateTimeOffset(_t0Ticks, TimeSpan.Zero);
        return string.Join(
            Environment.NewLine,
            entries.Select(e =>
            {
                var relMs = (e.Timestamp - t0).TotalMilliseconds;
                var detail = string.IsNullOrEmpty(e.Detail) ? "" : $" {e.Detail}";
                return $"{e.Timestamp:O} +{relMs:F0}ms {e.Stage} source={e.SourceSessionId:N}{detail}";
            }));
    }
}

public readonly record struct MentionRouteStage(
    DateTimeOffset Timestamp,
    Guid SourceSessionId,
    string Stage,
    string? Detail);
