using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1803. Pure rule for a digest page when a Blocked seat has held past the hour bound
/// and the caller note is still undelivered. Detection only.
/// </summary>
public static class BlockedSeatEscalation
{
    public static bool Qualifies(
        AgentTaskStatus status,
        AgentTaskRole role,
        SeatOccupancySnapshot? snapshot,
        Guid taskId,
        DateTime now,
        int escalationHours,
        bool undeliveredCallerNote,
        bool seatWatchEnabled = true)
    {
        if (!seatWatchEnabled || snapshot is null || snapshot.Hosts.Count == 0)
            return false;
        if (status != AgentTaskStatus.Blocked || AgentTaskRoles.IsSpecialist(role))
            return false;
        if (!undeliveredCallerNote || escalationHours < 1)
            return false;
        var seat = OccupyingIdleBlockedSeat(snapshot, taskId);
        return seat is not null && now - seat.IdleSince >= TimeSpan.FromHours(escalationHours);
    }

    /// <summary>
    /// The occupying <see cref="SeatClass.IdleBlocked"/> seat for <paramref name="taskId"/> on a runner host.
    /// A local host, a non-occupying seat, and any other class do not match.
    /// </summary>
    public static SeatObservation? OccupyingIdleBlockedSeat(SeatOccupancySnapshot? snapshot, Guid taskId)
    {
        if (snapshot is null)
            return null;
        foreach (var host in snapshot.Hosts)
        {
            if (!SeatOccupancyProjection.IsRunnerHost(host.Kind, host.HostId))
                continue;
            foreach (var seat in host.Seats)
            {
                if (seat.TaskId == taskId && seat.Occupies && seat.Class == SeatClass.IdleBlocked)
                    return seat;
            }
        }

        return null;
    }
}
