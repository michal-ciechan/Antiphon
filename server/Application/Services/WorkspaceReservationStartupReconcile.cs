using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0664 D-8: the server-startup reconcile hook (one-time backfill of the orphaned
/// workspace-use <c>Launch</c> backlog, repeated harmlessly on every start).
/// </summary>
public static class WorkspaceReservationStartupReconcile
{
    /// <summary>
    /// Releases orphaned <c>Launch</c> rows and returns the count. Best-effort: a failed
    /// reconcile is logged and returns null, and never blocks startup.
    /// </summary>
    public static async Task<int?> RunAsync(IServiceProvider services, ILogger logger, CancellationToken ct)
    {
        try
        {
            var released = await services.GetRequiredService<IWorkspaceReservationJournal>()
                .ReleaseOrphanedConsumersAsync(ct);
            logger.LogInformation("Workspace reservations reconciled: released {Count} orphaned Launch rows", released);
            return released;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Workspace reservation reconcile failed at startup; continuing");
            return null;
        }
    }
}
