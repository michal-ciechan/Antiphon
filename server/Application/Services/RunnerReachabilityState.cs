namespace Antiphon.Server.Application.Services;

/// <summary>
/// Singleton edge detector for session-runner reachability: alerts fire on transitions only,
/// never per poll (the reconciler probes every 15s).
/// </summary>
public sealed class RunnerReachabilityState
{
    private readonly object _gate = new();
    private bool? _reachable;

    /// <summary>Returns true when this call is the unreachable EDGE (was reachable/unknown-healthy before).</summary>
    public bool MarkUnreachable()
    {
        lock (_gate)
        {
            var edge = _reachable == true;
            _reachable = false;
            return edge;
        }
    }

    /// <summary>Returns true when this call is the recovery EDGE (was unreachable before).</summary>
    public bool MarkReachable()
    {
        lock (_gate)
        {
            var edge = _reachable == false;
            _reachable = true;
            return edge;
        }
    }
}
