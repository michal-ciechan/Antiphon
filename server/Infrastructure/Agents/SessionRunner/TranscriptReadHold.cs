using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// CARD-1764. The last whole transcript a session's read returned, with the runner stream it
/// belongs to. Only a gap-free body is held. Nothing expires it; the oldest session is dropped
/// past <see cref="MaxSessions"/>.
/// </summary>
internal sealed class TranscriptReadHold
{
    public const int MaxSessions = 256;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, (string Stream, SessionRunnerTranscriptDto Body, long Touched)> _held = new();
    private long _touch;

    public bool TryGet(Guid sessionId, out string stream, out SessionRunnerTranscriptDto body)
    {
        lock (_gate)
        {
            if (_held.TryGetValue(sessionId, out var held))
            {
                _held[sessionId] = held with { Touched = ++_touch };
                stream = held.Stream;
                body = held.Body;
                return true;
            }
        }

        stream = "";
        body = null!;
        return false;
    }

    public void Remember(Guid sessionId, string stream, SessionRunnerTranscriptDto body)
    {
        lock (_gate)
        {
            _held[sessionId] = (stream, body, ++_touch);
            if (_held.Count <= MaxSessions)
                return;
            var oldest = _held.MinBy(entry => entry.Value.Touched).Key;
            _held.Remove(oldest);
        }
    }

    public void Forget(Guid sessionId)
    {
        lock (_gate)
            _held.Remove(sessionId);
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
/// as nothing having arrived.
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
            && hold.TryGet(sessionId, out var stream, out var held)
            && TranscriptSince.IsCursor(held.LastSequence, stream))
        {
            var answer = await fetch(held.LastSequence, stream, ct).ConfigureAwait(false);
            var delta = map(answer);
            if (Join(sessionId, stream, held, answer.Stream, delta) is { } joined)
            {
                hold.Remember(sessionId, stream, joined);
                return joined;
            }
        }

        var full = await fetch(0, null, ct).ConfigureAwait(false);
        var body = map(full);
        if (cursorEnabled && Holdable(sessionId, full.Stream, body))
            hold.Remember(sessionId, full.Stream!, body);
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
