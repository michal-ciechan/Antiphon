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

    public static bool Blocks(
        WorkspaceReservationKind kind, Guid? rowRetirementId, Guid? claimRetirementId,
        DateTime createdAt, DateTime now, TimeSpan grace,
        OwnerFacts owner)
    {
        if (kind == WorkspaceReservationKind.Retirement)
            return rowRetirementId != claimRetirementId;
        if (kind != WorkspaceReservationKind.Launch)
            return true;
        // Red stub: every Launch row blocks.
        return true;
    }
}
