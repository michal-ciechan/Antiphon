using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.StateMachine;

/// <summary>
/// Pure logic state machine for workflow status transitions.
/// No DI, no I/O, no infrastructure dependencies.
/// </summary>
public static class WorkflowStateMachine
{
    private static readonly Dictionary<WorkflowStatus, WorkflowStatus[]> Transitions = new()
    {
        [WorkflowStatus.Created] = [WorkflowStatus.Running],
        [WorkflowStatus.Running] = [WorkflowStatus.GateWaiting, WorkflowStatus.Paused, WorkflowStatus.Failed, WorkflowStatus.Completed],
        [WorkflowStatus.GateWaiting] = [WorkflowStatus.Running, WorkflowStatus.Paused, WorkflowStatus.Abandoned],
        [WorkflowStatus.Paused] = [WorkflowStatus.Running, WorkflowStatus.Abandoned],
        [WorkflowStatus.Failed] = [WorkflowStatus.Running],
        [WorkflowStatus.Completed] = [],
        [WorkflowStatus.Abandoned] = [],
    };

    /// <summary>
    /// Returns true if the transition from <paramref name="from"/> to <paramref name="to"/> is valid.
    /// </summary>
    public static bool CanTransition(WorkflowStatus from, WorkflowStatus to)
    {
        return Transitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>
    /// Returns the list of valid target states from <paramref name="currentState"/>.
    /// </summary>
    public static IReadOnlyList<WorkflowStatus> GetAvailableTransitions(WorkflowStatus currentState)
    {
        return Transitions.TryGetValue(currentState, out var targets) ? targets : [];
    }
}
