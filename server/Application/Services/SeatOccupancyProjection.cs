using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-1079: how a remembered runner seat is classed. Detection only.</summary>
public enum SeatClass
{
    Exited,
    PooledWarm,
    Active,
    IdleBlocked,
    IdleTerminal,
    IdleUnbound,
}

/// <summary>
/// Pure seat rules. <see cref="RunnerSlotService.OccupiesCapacity"/> and
/// <see cref="RunnerSlotService.IsOrphan"/> stay the capacity and orphan predicates;
/// this type does not copy them.
/// </summary>
public static class SeatOccupancyProjection
{
    /// <summary>
    /// Class follows capacity, warmth and the bound task.
    /// <paramref name="desktopLive"/> is part of the row the caller already has;
    /// desktop liveness stays with <see cref="RunnerSlotService.IsOrphan"/>.
    /// </summary>
    public static SeatClass Classify(bool occupies, bool desktopLive, bool pooledWarm, AgentTaskStatus? bound)
    {
        if (!occupies)
            return SeatClass.Exited;
        if (pooledWarm)
            return SeatClass.PooledWarm;
        return bound switch
        {
            AgentTaskStatus.Dispatched or AgentTaskStatus.Working => SeatClass.Active,
            AgentTaskStatus.Blocked => SeatClass.IdleBlocked,
            AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled => SeatClass.IdleTerminal,
            AgentTaskStatus.Queued or null => SeatClass.IdleUnbound,
        };
    }

    public static DateTime IdleSince(
        SeatClass seat, DateTime? blockedAt, DateTime? completedAt, DateTime runnerStartedAt) =>
        seat switch
        {
            SeatClass.IdleBlocked => blockedAt ?? runnerStartedAt,
            SeatClass.IdleTerminal => completedAt ?? runnerStartedAt,
            _ => runnerStartedAt,
        };

    /// <summary>Inclusive at both thresholds. Below warning there is no row.</summary>
    public static AlertSeverity? Severity(TimeSpan age, AttentionSettings settings)
    {
        if (age >= TimeSpan.FromMinutes(settings.SeatIdleErrorMinutes))
            return AlertSeverity.Error;
        if (age >= TimeSpan.FromMinutes(settings.SeatIdleWarningMinutes))
            return AlertSeverity.Warning;
        return null;
    }

    public static int Divergence(int inFlight, int dispatchedWorking) => inFlight - dispatchedWorking;

    /// <summary><c>yes</c> only when the bound attempt has a publication receipt; otherwise <c>unknown</c>.</summary>
    public static string Pushed(bool receipt) => receipt ? "yes" : "unknown";
}
