using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0664 D-1: whether an active workspace-use reservation still blocks a retirement claim
/// (and still counts as a live consumer after one). A <see cref="WorkspaceReservationKind.Launch"/>
/// row lives as long as its owner, plus a short grace window for the admit-before-status writes.
/// </summary>
public static class WorkspaceReservationLiveness
{
    public sealed record OwnerFacts(
        bool TaskFound,
        AgentTaskStatus? TaskStatus,
        bool LandPending,
        bool SessionFound,
        SessionStatus? SessionStatus)
    {
        public static OwnerFacts None { get; } = new(false, null, false, false, null);
    }

    private static readonly AgentTaskStatus[] LiveTaskStatuses =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];

    private static readonly SessionStatus[] LiveSessionStatuses =
        [SessionStatus.Created, SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    public static bool Blocks(
        WorkspaceReservationKind kind, Guid? rowRetirementId, Guid? claimRetirementId,
        DateTime createdAt, DateTime now, TimeSpan grace,
        OwnerFacts owner)
    {
        if (kind == WorkspaceReservationKind.Retirement)
            return rowRetirementId != claimRetirementId;
        if (kind != WorkspaceReservationKind.Launch)
            return true;
        if (now - createdAt < grace)
            return true;
        if (owner.TaskFound && (owner.LandPending || owner.TaskStatus is { } task && LiveTaskStatuses.Contains(task)))
            return true;
        return owner.SessionFound && owner.SessionStatus is { } session && LiveSessionStatuses.Contains(session);
    }
}
