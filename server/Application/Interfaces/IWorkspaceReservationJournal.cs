using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Persisted workspace-use reservation I/O. Short transactions only; never Git.</summary>
public interface IWorkspaceReservationJournal
{
    Task<IReadOnlyList<WorkspaceReservationSnapshot>> ReadActiveAsync(WorkspaceReservationKey key, CancellationToken ct);

    Task<WorkspaceReservationCommitResult> TryAdmitConsumerAsync(WorkspaceReservationCommand command, CancellationToken ct);

    Task<WorkspaceReservationCommitResult> TryClaimRetirementAsync(WorkspaceReservationCommand command, CancellationToken ct);

    Task InvalidateUnclaimedReleaseAsync(Guid taskId, CancellationToken ct);

    Task ReleaseConsumerAsync(Guid reservationId, int generation, CancellationToken ct);
}
