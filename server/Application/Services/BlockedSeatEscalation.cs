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
        bool seatWatchEnabled = true) =>
        throw new NotImplementedException("CARD-1803");
}
