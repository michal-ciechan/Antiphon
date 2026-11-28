namespace Antiphon.Server.Application.Services;

/// <summary>
/// Live connections to a session's browser group. The terminal, transcript and queue all use
/// this group, so any subscriber is treated as an operator attachment. Admission to the group
/// and a verified stop exclude each other until stop readback finishes.
/// </summary>
public sealed class SessionTerminalAttachments
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HashSet<string>> _subscribers = [];
    private readonly HashSet<Guid> _stopping = [];
    private readonly Dictionary<Guid, int> _inputWriters = [];

    public const string StopInFlightMessage = "This session is being stopped or awaiting confirmation of exit.";

    public IDisposable? TryBeginInput(Guid sessionId)
    {
        lock (_gate)
        {
            if (_stopping.Contains(sessionId)) return null;
            _inputWriters[sessionId] = _inputWriters.GetValueOrDefault(sessionId) + 1;
            return new InputLease(this, sessionId);
        }
    }

    private void EndInput(Guid sessionId)
    {
        lock (_gate)
        {
            var remaining = _inputWriters[sessionId] - 1;
            if (remaining == 0) _inputWriters.Remove(sessionId);
            else _inputWriters[sessionId] = remaining;
        }
    }

    private sealed class InputLease(SessionTerminalAttachments owner, Guid sessionId) : IDisposable
    {
        private SessionTerminalAttachments? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndInput(sessionId);
    }

    public bool TryAttach(Guid sessionId, string connection)
    {
        lock (_gate)
        {
            if (_stopping.Contains(sessionId)) return false;
            if (!_subscribers.TryGetValue(sessionId, out var connections))
                _subscribers.Add(sessionId, connections = []);
            connections.Add(connection);
            return true;
        }
    }

    public void Detach(Guid sessionId, string connection)
    {
        lock (_gate)
            RemoveSubscriber(sessionId, connection);
    }

    public void Disconnect(string connection)
    {
        lock (_gate)
        {
            foreach (var session in _subscribers.Keys.ToArray())
                RemoveSubscriber(session, connection);
        }
    }

    private void RemoveSubscriber(Guid sessionId, string connection)
    {
        if (_subscribers.TryGetValue(sessionId, out var connections) && connections.Remove(connection)
            && connections.Count == 0)
            _subscribers.Remove(sessionId);
    }

    public bool IsAttached(Guid sessionId)
    {
        lock (_gate)
            return _subscribers.ContainsKey(sessionId) || _inputWriters.ContainsKey(sessionId);
    }

    internal bool TryBeginStop(Guid sessionId)
    {
        lock (_gate)
            return !_subscribers.ContainsKey(sessionId) && !_inputWriters.ContainsKey(sessionId) && _stopping.Add(sessionId);
    }

    internal void EndStop(Guid sessionId)
    {
        lock (_gate)
            _stopping.Remove(sessionId);
    }
}
