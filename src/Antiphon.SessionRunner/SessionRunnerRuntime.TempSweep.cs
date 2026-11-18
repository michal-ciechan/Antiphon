namespace Antiphon.SessionRunner;

public sealed partial class SessionRunnerRuntime
{
    // Every creation path holds this gate before registering a session or opening
    // its log. Keep the gate through the sweep's final file validation and unlink.
    // A busy identity is retained; maintenance never waits behind a launch.
    internal bool TryWithUntrackedSession(Guid sessionId, Action action)
    {
        var gate = _launchLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0)) return false;
        try
        {
            if (_sessions.ContainsKey(sessionId)) return false;
            action();
            return true;
        }
        finally { gate.Release(); }
    }
}
