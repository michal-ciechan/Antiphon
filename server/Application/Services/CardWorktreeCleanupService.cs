using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Inventories exact task attempts for a Done generation. Discovery never grants deletion
/// authority and continues after every known endpoint completes, including after restart.
/// </summary>
public sealed class CardWorktreeCleanupService(AppDbContext db, TimeProvider clock)
{
    internal Func<CancellationToken, Task>? BeforeInventorySaveAsync { get; set; }

    public async Task<IReadOnlyList<Guid>> DiscoverAsync(Guid cardId, CancellationToken ct)
    {
        // Inventory is non-destructive. Uniqueness converges competing callbacks; deletion
        // admission must independently serialize with the card writer and workspace users.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var card = await db.Cards.AsNoTracking().SingleOrDefaultAsync(c => c.Id == cardId, ct);
        if (card is null || card.Status != CardStatus.Done)
        {
            await RevokeUnissuedAsync(cardId, null, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return [];
        }

        // Archive/content edits are not generations. A Done-to-Done column move does not
        // manufacture another authority; the last transition into Done owns the generation.
        var revision = await db.CardRevisions.AsNoTracking()
            .Where(r => r.CardId == cardId && r.ToStatus == CardStatus.Done && r.FromStatus != CardStatus.Done)
            .OrderByDescending(r => r.RevisionNumber).FirstOrDefaultAsync(ct);
        if (revision is null)
        {
            await transaction.CommitAsync(ct);
            return []; // A legacy status without an immutable move is not deletion authority.
        }
        await RevokeUnissuedAsync(cardId, revision.Id, ct);
        var cleanup = await db.CardWorktreeCleanups.SingleOrDefaultAsync(
            r => r.CardId == cardId && r.DoneRevisionId == revision.Id, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (cleanup is null)
        {
            cleanup = new CardWorktreeCleanup
            {
                Id = Guid.NewGuid(), CardId = cardId, DoneRevisionId = revision.Id,
                DoneAt = revision.CreatedAt, CreatedAt = now
            };
            db.CardWorktreeCleanups.Add(cleanup);
        }
        var existing = await db.CardWorktreeCleanupTargets
            .Where(t => t.CleanupId == cleanup.Id).ToListAsync(ct);
        var tasks = await db.AgentTasks.AsNoTracking().Where(t => t.CardId == cardId).ToListAsync(ct);
        var current = new List<Guid>();
        foreach (var task in tasks)
        {
            var repository = WorkspaceReservationKey.NormalizePath(task.RepoPath);
            var path = WorkspaceReservationKey.NormalizePath(task.WorktreePath);
            var branch = WorkspaceReservationKey.NormalizeRef(task.WorktreeBranch);
            var identity = Digest(repository + "\n" + path + "\n" + branch);
            var target = existing.SingleOrDefault(t => t.TaskId == task.Id && t.TaskAttempt == task.Attempt && t.WorkspaceIdentity == identity);
            if (target is null)
            {
                target = new CardWorktreeCleanupTarget
                {
                    Id = Guid.NewGuid(), CleanupId = cleanup.Id, TaskId = task.Id,
                    TaskAttempt = task.Attempt, WorkspaceIdentity = identity, RepositoryPath = repository,
                    WorktreePath = path, SourceFullRef = branch, CreatedAt = now,
                    ExclusionReason = ClassifyOwnership(task, path, branch)
                };
                db.CardWorktreeCleanupTargets.Add(target);
                if (target.ExclusionReason is null)
                    target.Endpoints.Add(new CardWorktreeCleanupEndpoint
                    {
                        Id = Guid.NewGuid(), TargetId = target.Id, EndpointIdentity = "local",
                        RepositoryPath = repository, WorktreePath = path, SourceFullRef = branch,
                        State = CardWorktreeCleanupEndpointState.Pending, UpdatedAt = now
                    });
                // A historical remote path alone does not identify a runner store or task
                // creation. Guarded mirror discovery must provide that independent binding.
            }
            current.Add(target.Id);
        }
        cleanup.LastDiscoveredAt = now;
        if (BeforeInventorySaveAsync is not null) await BeforeInventorySaveAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another discovery committed this generation/attempt first. Read its durable
            // result after rolling back our whole inventory; no failed tracked row survives.
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
            return await db.CardWorktreeCleanupTargets.AsNoTracking()
                .Where(t => t.Cleanup.CardId == cardId && t.Cleanup.DoneRevisionId == revision.Id)
                .Select(t => t.Id).ToListAsync(ct);
        }
        return current;
    }

    private async Task RevokeUnissuedAsync(Guid cardId, Guid? currentRevision, CancellationToken ct)
    {
        var endpoints = await db.CardWorktreeCleanupEndpoints
            .Where(e => e.Target.Cleanup.CardId == cardId
                && (currentRevision == null || e.Target.Cleanup.DoneRevisionId != currentRevision)
                && e.OperationId == null && e.State != CardWorktreeCleanupEndpointState.Revoked)
            .ToListAsync(ct);
        foreach (var endpoint in endpoints)
        {
            endpoint.State = CardWorktreeCleanupEndpointState.Revoked;
            endpoint.Reason = "done_generation_revoked";
            endpoint.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            endpoint.ConcurrencyToken = Guid.NewGuid();
        }
    }

    private static string? ClassifyOwnership(AgentTask task, string path, string branch)
    {
        if (task.SourceLandingOperationId is not null) return "source_landing_excluded";
        if (task.Workspace != WorkspaceMode.Worktree) return "shared_or_borrowed_workspace";
        if (string.IsNullOrWhiteSpace(task.RepoPath) || string.IsNullOrWhiteSpace(path)) return "identity_unknown";
        var shortId = task.Id.ToString("N")[..8];
        if (Path.GetFileName(path) != "card-task-" + shortId || branch != "refs/heads/feat/card-task-" + shortId)
            return "ordinary_owner_required";
        return null;
    }

    private static string Digest(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
