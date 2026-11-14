using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>Initial ref-only producer. Admission belongs to the dispatcher.</summary>
public sealed class RefOnlyDesktopProvisioner(TaskWorkspaceContextResolver contexts)
{
    // Test-first API scaffold: the first acceptance must distinguish legacy coordinates.
    public Task<TaskWorkspaceContext> PlanAsync(AgentTask task, RepositoryLease lease, CancellationToken ct) =>
        contexts.ResolveAsync(task, ct);
}
