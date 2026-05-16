namespace Antiphon.Server.Application.Services;

public sealed class OrchestratorControlState
{
    private readonly object _gate = new();
    private bool _paused;

    public bool IsPaused
    {
        get
        {
            lock (_gate)
                return _paused;
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
}
