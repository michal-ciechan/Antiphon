using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Infrastructure.Agents.SessionRunner;

/// <summary>
/// The last body a session's buffer or snapshot read returned, kept only for its ETag.
/// Nothing expires it. A later 200 with no validator drops the entry.
/// </summary>
internal sealed class PtyReadHold<T> where T : class
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, (string ETag, T Body)> _held = new();

    public bool TryGet(Guid sessionId, out string etag, out T body)
    {
        lock (_gate)
        {
            if (_held.TryGetValue(sessionId, out var held))
            {
                etag = held.ETag;
                body = held.Body;
                return true;
            }
        }

        etag = "";
        body = null!;
        return false;
    }

    public void Remember(Guid sessionId, string etag, T body)
    {
        lock (_gate)
            _held[sessionId] = (etag, body);
    }

    public void Forget(Guid sessionId)
    {
        lock (_gate)
            _held.Remove(sessionId);
    }
}

/// <summary>
/// CARD-1763. Buffer and snapshot bodies live with the phone-home connection, so every
/// client resolved on that socket sends the same validator. A new connection starts empty.
/// </summary>
internal sealed class PhoneHomePtyReads
{
    public PtyReadHold<SessionRunnerBufferDto> Buffer { get; } = new();
    public PtyReadHold<SessionRunnerSnapshotDto> Snapshot { get; } = new();

    public void Forget(Guid sessionId)
    {
        Buffer.Forget(sessionId);
        Snapshot.Forget(sessionId);
    }
}
