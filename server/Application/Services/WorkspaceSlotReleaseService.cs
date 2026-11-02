using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0824 S1 D-B. RED-COMMIT STUB: releases nothing.</summary>
public sealed class WorkspaceSlotReleaseService(
    AppDbContext db, RemoteWorkspaceService remote, TimeProvider clock, ILogger<WorkspaceSlotReleaseService> logger)
{
    public Task<int> ReconcileAsync(CancellationToken ct) => Task.FromResult(0);

    internal Task<bool> EligibleAsync(AgentTask task, CancellationToken ct) => Task.FromResult(false);
}
