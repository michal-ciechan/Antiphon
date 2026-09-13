namespace Antiphon.Server.Application.Services;

/// <summary>S1 stand-in: records that reconcile was requested and performs no filesystem or runner I/O.</summary>
public sealed class NoOpAgentPinnedInstructionReconciler : IAgentPinnedInstructionReconciler
{
    private readonly List<(Guid AgentId, int Revision)> _calls = [];
    private readonly object _gate = new();

    public IReadOnlyList<(Guid AgentId, int Revision)> Calls
    {
        get
        {
            lock (_gate)
                return _calls.ToList();
        }
    }

    public int CallCount
    {
        get
        {
            lock (_gate)
                return _calls.Count;
        }
    }

    public Task ReconcileAfterCommitAsync(Guid agentId, int revision, CancellationToken ct)
    {
        lock (_gate)
            _calls.Add((agentId, revision));
        return Task.CompletedTask;
    }
}
