namespace Antiphon.Server.Application.Services;

public sealed class OrchestratorControlState
{
    private readonly object _gate = new();
    private bool _paused;
    private DateTime? _lastTrackerSyncAt;

    public bool IsPaused
    {
        get
        {
            lock (_gate)
                return _paused;
        }
    }

    /// <summary>
    /// Last time the orchestrator tick ran the read-only external tracker sync.
    /// Lives here (singleton) because OrchestratorService is scoped per tick.
    /// </summary>
    public DateTime? LastTrackerSyncAt
    {
        get
        {
            lock (_gate)
                return _lastTrackerSyncAt;
        }
    }

    public bool Pause()
    {
        lock (_gate)
        {
            _paused = true;
            return _paused;
        }
    }

    public bool Resume()
    {
        lock (_gate)
        {
            _paused = false;
            return _paused;
        }
    }

    public void MarkTrackerSynced(DateTime utcNow)
    {
        lock (_gate)
            _lastTrackerSyncAt = utcNow;
    }
}
