using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1610 S1. One reading of a Failed task whose session has not authoritatively exited.
/// Attention and task detail both call this so the two surfaces cannot disagree.
/// Notice state stays null until the notice ledger exists.
/// </summary>
internal static class FailedTaskSessionFacts
{
    public const string SeatHeld = "Failed, session still Running: seat held";
    public const string ReleaseUnknown = "session state unknown: release unconfirmed";

    public static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(2);

    public static FailedTaskSessionFactsDto Describe(
        AgentTask task,
        AgentSession session,
        bool working,
        SeatOccupancySnapshot? snapshot,
        DateTime now)
    {
        var fresh = IsFreshListed(snapshot, session, now);
        var activity = fresh ? (working ? "Working" : "idle") : "unknown";
        return new FailedTaskSessionFactsDto(
            fresh ? SeatHeld : ReleaseUnknown,
            activity,
            task.Id,
            task.CardId,
            session.Id,
            task.FailureReason,
            NoticeState: null);
    }

    /// <summary>
    /// A fresh listed snapshot shows the same generation still Running. Unavailable inventory,
    /// a missing or stale snapshot, a generation mismatch, or a listed host that does not name
    /// the session are all unconfirmed.
    /// </summary>
    public static bool IsFreshListed(SeatOccupancySnapshot? snapshot, AgentSession session, DateTime now)
    {
        if (snapshot is null || snapshot.Hosts.Count == 0)
            return false;
        var generated = AsUtc(snapshot.GeneratedAt);
        if (generated == default)
            return false;
        if ((AsUtc(now) - generated).Duration() > FreshWindow)
            return false;
        if (!snapshot.Hosts.Any(host =>
                string.Equals(host.InventoryState, "listed", StringComparison.OrdinalIgnoreCase)))
            return false;
        var seat = snapshot.Hosts.SelectMany(host => host.Seats)
            .FirstOrDefault(candidate => candidate.SessionId == session.Id);
        if (seat is null)
            return false;
        if (!string.Equals(seat.RunnerStatus, "Running", StringComparison.OrdinalIgnoreCase))
            return false;
        return SessionGeneration.Equal(seat.StartedAt, session.StartedAt);
    }

    internal static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
