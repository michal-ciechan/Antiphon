using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0824 S2. The open task (and the term) that still depends on a landed branch.</summary>
public sealed record BranchDependent(Guid TaskId, string Term);

/// <summary>CARD-0824 S2 (V-17). Stub: reports no dependent.</summary>
public static class PublishedBranchDeletionDependents
{
    public static Task<BranchDependent?> FindAsync(AppDbContext db, AgentTaskLanding op, CancellationToken ct) =>
        Task.FromResult<BranchDependent?>(null);
}
