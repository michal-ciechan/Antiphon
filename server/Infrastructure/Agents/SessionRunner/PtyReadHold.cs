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
