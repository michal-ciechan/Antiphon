using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-1764. The last whole transcript a session's read returned, with the runner stream it
/// belongs to. Only a gap-free body is held. A session end forgets it; otherwise the least recently
/// read session is dropped past <see cref="MaxSessions"/> or past the byte cap (CARD-1798).
/// </summary>
internal sealed class TranscriptReadHold
{
    public const int MaxSessions = 256;

    /// <summary>CARD-1798. The held bodies together: <see cref="MaxSessions"/> times the CARD-1763 256 KiB body.</summary>
    public const long DefaultMaxBytes = MaxSessions * 256L * 1024;

    // Fixed cost of one held row: the record, its boxed fields and its slot in the array.
    private const long RowBytes = 256;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, (string Stream, SessionRunnerTranscriptDto Body, long Bytes, long Touched)> _held = new();
    private readonly long _maxBytes;
    private long _bytes;
    private long _touch;

    public TranscriptReadHold(long maxBytes = DefaultMaxBytes) => _maxBytes = maxBytes;

    public bool TryGet(Guid sessionId, out string stream, out SessionRunnerTranscriptDto body, out long bytes)
    {
        lock (_gate)
        {
            if (_held.TryGetValue(sessionId, out var held))
            {
                _held[sessionId] = held with { Touched = ++_touch };
                stream = held.Stream;
                body = held.Body;
                bytes = held.Bytes;
                return true;
            }
        }

        stream = "";
        body = null!;
        bytes = 0;
        return false;
    }

    /// <summary><paramref name="bytes"/> is <see cref="Weigh"/> of the body's entries.</summary>
    public void Remember(Guid sessionId, string stream, SessionRunnerTranscriptDto body, long bytes)
    {
        lock (_gate)
        {
            if (_held.Remove(sessionId, out var prior))
                _bytes -= prior.Bytes;
            // A body past the whole cap is still answered; it is just not held.
            if (bytes > _maxBytes)
                return;
            _held[sessionId] = (stream, body, bytes, ++_touch);
            _bytes += bytes;
            while (_held.Count > MaxSessions || _bytes > _maxBytes)
            {
                var oldest = _held.MinBy(entry => entry.Value.Touched);
                _held.Remove(oldest.Key);
                _bytes -= oldest.Value.Bytes;
            }
        }
    }

    public void Forget(Guid sessionId)
    {
        lock (_gate)
        {
            if (_held.Remove(sessionId, out var prior))
                _bytes -= prior.Bytes;
        }
    }

    /// <summary>
    /// CARD-1798. An estimate of what <paramref name="entries"/> keep resident: a fixed cost per row
    /// plus two bytes for each character of its strings.
    /// </summary>
    public static long Weigh(IReadOnlyList<SessionRunnerTranscriptEvent> entries)
    {
        var bytes = 0L;
        foreach (var entry in entries)
        {
            var chars = (long)Length(entry.Kind) + Length(entry.Uuid) + Length(entry.ParentUuid) + Length(entry.Role)
                + Length(entry.Text) + Length(entry.ToolName) + Length(entry.ToolInput) + Length(entry.ToolUseId)
                + Length(entry.StopReason) + Length(entry.ApiCallId) + Length(entry.ApiErrorClass) + Length(entry.Model)
                + Length(entry.ApiErrorTimeZoneId);
            bytes += RowBytes + 2 * chars;
        }

        return bytes;
    }

    private static int Length(string? text) => text?.Length ?? 0;

    internal long Bytes
    {
        get
        {
            lock (_gate)
                return _bytes;
        }
    }

    internal int Count
    {
        get
        {
            lock (_gate)
                return _held.Count;
        }
    }
}

/// <summary>
/// CARD-1764. One transcript read through a since-cursor. A delta is applied only when it is the
/// same stream and generation, starts at the cursor plus one, and ends at the runner's
/// <c>LastSequence</c> with no gap. Anything else is a full read, so a missed row is never read
/// as nothing having arrived. A refused cursor whose answer is already the whole stream is that
/// full read (CARD-1798).
/// </summary>
internal static class TranscriptSinceRead
{
    public static async Task<SessionRunnerTranscriptDto> ReadAsync(
        Guid sessionId,
        TranscriptReadHold hold,
        bool cursorEnabled,
        Func<long, string?, CancellationToken, Task<RunnerTranscriptDto>> fetch,
        Func<RunnerTranscriptDto, SessionRunnerTranscriptDto> map,
        CancellationToken ct)
    {
        if (cursorEnabled
            && hold.TryGet(sessionId, out var stream, out var held, out var heldBytes)
            && TranscriptSince.IsCursor(held.LastSequence, stream))
        {
            var answer = await fetch(held.LastSequence, stream, ct).ConfigureAwait(false);
            var delta = map(answer);
            if (Join(sessionId, stream, held, answer.Stream, delta) is { } joined)
            {
                hold.Remember(sessionId, stream, joined, heldBytes + TranscriptReadHold.Weigh(delta.Entries));
                return joined;
            }

            // The runner answers a cursor it cannot serve with the whole transcript of its stream.
            if (Holdable(sessionId, answer.Stream, delta))
            {
                hold.Remember(sessionId, answer.Stream!, delta, TranscriptReadHold.Weigh(delta.Entries));
                return delta;
            }
        }

        var full = await fetch(0, null, ct).ConfigureAwait(false);
        var body = map(full);
        if (cursorEnabled && Holdable(sessionId, full.Stream, body))
            hold.Remember(sessionId, full.Stream!, body, TranscriptReadHold.Weigh(body.Entries));
        else
            hold.Forget(sessionId);
        return body;
    }

    /// <summary>The held transcript plus <paramref name="delta"/>, or null when the delta is not gap-free.</summary>
    internal static SessionRunnerTranscriptDto? Join(
        Guid sessionId, string heldStream, SessionRunnerTranscriptDto held, string? stream, SessionRunnerTranscriptDto delta)
    {
        if (delta.SessionId != sessionId
            || string.IsNullOrEmpty(stream)
            || !string.Equals(stream, heldStream, StringComparison.Ordinal)
            || !SessionGeneration.Equal(held.AcceptedStartedAt, delta.AcceptedStartedAt))
            return null;

        var expected = held.LastSequence;
        foreach (var entry in delta.Entries)
        {
            if (entry.SessionId != sessionId || entry.Sequence != expected + 1)
                return null;
            expected = entry.Sequence;
        }

        if (expected != delta.LastSequence)
            return null;

        // Nothing arrived: the held rows are answered as they are.
        if (delta.Entries.Count == 0)
            return delta with { Entries = held.Entries };

        var entries = new SessionRunnerTranscriptEvent[held.Entries.Count + delta.Entries.Count];
        for (var i = 0; i < held.Entries.Count; i++)
            entries[i] = held.Entries[i];
        for (var i = 0; i < delta.Entries.Count; i++)
            entries[held.Entries.Count + i] = delta.Entries[i];
        return delta with { Entries = entries };
    }

    /// <summary>A full body is held only when it is the whole stream: rows 1..LastSequence, no gap.</summary>
    internal static bool Holdable(Guid sessionId, string? stream, SessionRunnerTranscriptDto body)
    {
        if (string.IsNullOrEmpty(stream) || body.SessionId != sessionId || body.AcceptedStartedAt is null)
            return false;
        var expected = 0L;
        foreach (var entry in body.Entries)
        {
            if (entry.SessionId != sessionId || entry.Sequence != expected + 1)
                return false;
            expected = entry.Sequence;
        }

        return expected == body.LastSequence;
    }
}
