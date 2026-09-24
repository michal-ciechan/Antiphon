using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Persisted workspace-use reservation I/O. Short transactions only; never Git.</summary>
public interface IWorkspaceReservationJournal
{
    Task<IReadOnlyList<WorkspaceReservationSnapshot>> ReadActiveAsync(WorkspaceReservationKey key, CancellationToken ct);

    /// <summary>
    /// CARD-0664 D-1: active rows at <paramref name="key"/>; with <paramref name="liveOwnersOnly"/>,
    /// orphaned <c>Launch</c> rows (owner ended or missing, past grace) are left out. Read-only.
    /// </summary>
    Task<IReadOnlyList<WorkspaceReservationSnapshot>> ReadActiveAsync(
        WorkspaceReservationKey key, bool liveOwnersOnly, CancellationToken ct);

    Task<WorkspaceReservationCommitResult> TryAdmitConsumerAsync(WorkspaceReservationCommand command, CancellationToken ct);

    Task<WorkspaceReservationCommitResult> TryClaimRetirementAsync(WorkspaceReservationCommand command, CancellationToken ct);

    Task InvalidateUnclaimedReleaseAsync(Guid taskId, CancellationToken ct);

    Task ReleaseConsumerAsync(Guid reservationId, int generation, CancellationToken ct);

    /// <summary>CARD-0664: release the task's <c>Launch</c> rows whose owners are no longer live.</summary>
    Task ReleaseTaskConsumersAsync(Guid taskId, CancellationToken ct);

    /// <summary>CARD-0664: release the session's <c>Launch</c> rows whose owners are no longer live.</summary>
    Task ReleaseSessionConsumersAsync(Guid sessionId, CancellationToken ct);

    /// <summary>CARD-0664 D-8: release every orphaned <c>Launch</c> row; returns the released count.</summary>
    Task<int> ReleaseOrphanedConsumersAsync(CancellationToken ct);
}
