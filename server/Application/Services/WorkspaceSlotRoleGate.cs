using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0824 S1c (T-D-3). Decides whether a task's role may lease a runner workspace slot, reading
/// <c>Delegation:WorkspaceSlotRoles</c> from the options monitor at every call so a hot reload applies to the next
/// lease. It never throws.
/// </summary>
public sealed class WorkspaceSlotRoleGate
{
    private readonly string _value;

    public WorkspaceSlotRoleGate(IOptionsMonitor<DelegationSettings>? monitor, ILogger<WorkspaceSlotRoleGate> logger)
    {
        // S1c red-first stub: the value is read once.
        _value = monitor?.CurrentValue.WorkspaceSlotRoles ?? WorkspaceSlotRoles.All;
    }

    public WorkspaceSlotRoleVerdict Evaluate(AgentTaskRole role, Guid? sourceLandingOperationId)
    {
        WorkspaceSlotRoles.TryParse(_value, out var set, out _);
        return set.Admits(role, sourceLandingOperationId);
    }
}

/// <summary>
/// CARD-0824 S1c (T-D-4). One preparation's lease decision. <see cref="Recorded"/> is true when it is (or was just
/// saved as) that preparation's <see cref="AgentTaskEventType.WorkspaceSlotDecision"/> event.
/// </summary>
public sealed record WorkspaceSlotDecision(bool Slot, string Reason, bool Recorded = false, DateTime? At = null);
