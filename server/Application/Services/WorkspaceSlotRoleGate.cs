using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0824 S1c (T-D-3). Decides whether a task's role may lease a runner workspace slot, reading
/// <c>Delegation:WorkspaceSlotRoles</c> from the options monitor at every call so a hot reload applies to the next
/// lease. It reads only the role value: <c>WorkspaceSlotsEnabled</c> stays restart-only on <see cref="IOptions{TOptions}"/>.
/// An invalid value, or an invalid reload of any <c>Delegation:*</c> key (the monitor then throws
/// <see cref="OptionsValidationException"/> on every read), fails closed: no lease, one warning, never a throw, so a task
/// gets the legacy mirror rather than a hold. With no monitor the value is the default <c>*</c>.
/// </summary>
public sealed class WorkspaceSlotRoleGate(IOptionsMonitor<DelegationSettings>? monitor, ILogger<WorkspaceSlotRoleGate> logger)
{
    private int _warned;

    public WorkspaceSlotRoleVerdict Evaluate(AgentTaskRole role, Guid? sourceLandingOperationId)
    {
        string? value;
        string? problem = null;
        var error = "";
        try
        {
            value = monitor is null ? WorkspaceSlotRoles.All : monitor.CurrentValue.WorkspaceSlotRoles;
        }
        catch (OptionsValidationException ex)
        {
            value = null;
            problem = ex.Message;
        }

        if (problem is null && WorkspaceSlotRoles.TryParse(value, out var set, out error))
        {
            Interlocked.Exchange(ref _warned, 0);
            return set.Admits(role, sourceLandingOperationId);
        }

        // One warning per invalid spell, not one per decision.
        if (Interlocked.Exchange(ref _warned, 1) == 0)
            logger.LogWarning(
                "{Setting} cannot be read ({Problem}); no task leases a new workspace slot until it is fixed, and releases continue",
                WorkspaceSlotRoles.SettingName, problem ?? error);
        return new WorkspaceSlotRoleVerdict(false, WorkspaceSlotRoleVerdict.RolesInvalid);
    }
}

/// <summary>
/// CARD-0824 S1c (T-D-4). One preparation's lease decision. <see cref="Recorded"/> is true when it is (or was just
/// saved as) that preparation's <see cref="AgentTaskEventType.WorkspaceSlotDecision"/> event, written at <see cref="At"/>.
/// </summary>
public sealed record WorkspaceSlotDecision(bool Slot, string Reason, bool Recorded = false, DateTime? At = null);
