using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data;

/// <summary>Every known endpoint in the generation is a deletion root, including mirrors.</summary>
internal static class CardDoneArtifactRoots
{
    public static async Task<IReadOnlyList<string>> ReadAsync(AppDbContext db, Guid cleanupId, CancellationToken ct)
    {
        var roots = await db.CardWorktreeCleanupEndpoints.AsNoTracking()
            .Where(e => e.Target.CleanupId == cleanupId).Select(e => e.WorktreePath).ToListAsync(ct);
        roots.AddRange(await db.CardWorktreeCleanupTargets.AsNoTracking()
            .Where(t => t.CleanupId == cleanupId).Select(t => t.WorktreePath).ToListAsync(ct));
        return roots.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal)
            .SelectMany(p => new[] { p, WorktreeSetAside.SetAsidePath(p) }).ToArray();
    }
}
