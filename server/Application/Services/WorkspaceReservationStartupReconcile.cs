using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0664 D-8: the server-startup reconcile hook (one-time backfill of the orphaned
/// workspace-use <c>Launch</c> backlog, repeated harmlessly on every start).
/// </summary>
public static class WorkspaceReservationStartupReconcile
{
    /// <summary>Red-first stub: releases nothing.</summary>
    public static Task<int?> RunAsync(IServiceProvider services, ILogger logger, CancellationToken ct) =>
        Task.FromResult<int?>(0);
}
