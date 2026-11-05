using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>New admissions only. Recorded workspace mode is independent of this gate.</summary>
public sealed class RefOnlyDesktopGate(IOptionsMonitor<RefOnlyDesktopSettings> monitor, ILogger<RefOnlyDesktopGate> logger)
{
    private int _warned;

    public bool Admits(AgentTask task)
    {
        RefOnlyDesktopSettings settings;
        try { settings = monitor.CurrentValue; }
        catch (OptionsValidationException ex)
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
                logger.LogWarning("{Setting} is invalid ({Problem}); new ref-only admission is closed until corrected",
                    RefOnlyDesktopSettings.SectionName, ex.Message);
            return false;
        }
        if (!WorkspaceSlotRoles.TryParse(settings.Roles, out var roles, out var problem))
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
                logger.LogWarning("{Setting} is invalid ({Problem}); new ref-only admission is closed until corrected",
                    RefOnlyDesktopSettings.SectionName, problem);
            return false;
        }
        Interlocked.Exchange(ref _warned, 0);
        return settings.Enabled && RemoteWorkspaceService.IsEligible(task) && Enum.IsDefined(task.Role)
            && settings.RunnerIds.Contains(task.RunnerId!, StringComparer.Ordinal)
            && roles.Admits(task.Role, task.SourceLandingOperationId).Lease;
    }
}
