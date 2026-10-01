using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Read-only, paged observations. Implementations must never fetch, prune or update refs.</summary>
public interface IHostCleanupWorktreeProbe
{
    Task<HostCleanupWorktreePage> ReadPageAsync(string hostId, string? cursor,
        int take, CancellationToken cancellationToken);
}

public sealed record HostCleanupWorktreePage(
    IReadOnlyList<HostCleanupWorktreeFacts> Items, string? NextCursor, bool Complete,
    string? IncompleteReason = null);

public interface IHostCleanupExistingOwnerStatusProbe
{
    Task<string?> ReadAvailabilityReasonAsync(string owner, CancellationToken cancellationToken);
}
