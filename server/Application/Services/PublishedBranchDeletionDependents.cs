using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0824 S2. The open task (and the term) that still depends on a landed branch.</summary>
public sealed record BranchDependent(Guid TaskId, string Term);

/// <summary>
/// CARD-0824 S2 (V-17, Q-2). The first open dependent of a landed branch, or null. Each term is its own query so a
/// dropped term is visible; an open dependent defers the deletion, which is re-evaluated every sweep.
/// </summary>
public static class PublishedBranchDeletionDependents
{
    private static readonly AgentTaskStatus[] Open =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];

    /// <summary>
    /// Every spelling a task row may store for the landed branch: short, full or as its tracking name. The dependent
    /// terms and the runner lookup (Review 370083f1 A-1) both match on this one list.
    /// </summary>
    public static string[] BranchNames(string sourceFullRef)
    {
        var branch = sourceFullRef.StartsWith("refs/heads/", StringComparison.Ordinal) ? sourceFullRef[11..] : sourceFullRef;
        return [branch, "refs/heads/" + branch, "origin/" + branch, "refs/remotes/origin/" + branch];
    }

    public static async Task<BranchDependent?> FindAsync(AppDbContext db, AgentTaskLanding op, CancellationToken ct)
    {
        var names = BranchNames(op.SourceFullRef);
        var owner = op.TaskId;
        var open = db.AgentTasks.AsNoTracking().Where(t => Open.Contains(t.Status));

        var hit = await open.Where(t => t.Id == owner).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (hit is { } reopened) return new(reopened, "owner_open");
        hit = await open.Where(t => t.Id != owner && t.WorktreeBaseBranch != null && names.Contains(t.WorktreeBaseBranch))
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (hit is { } baseBranch) return new(baseBranch, "worktree_base_branch");
        hit = await open.Where(t => t.Id != owner && t.WorktreeBaseTaskId == owner).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (hit is { } baseTask) return new(baseTask, "worktree_base_task");
        hit = await open.Where(t => t.Id != owner && t.RequestedWorktreeBaseTaskId == owner).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (hit is { } requested) return new(requested, "requested_worktree_base_task");
        // CARD-0753: an open repair against this owner may still need the owner's branch.
        hit = await open.Where(t => t.Id != owner && t.RepairSourceTaskId == owner).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (hit is { } repair) return new(repair, "repair_source_open");
        hit = await open.Where(t => t.Id != owner && t.WorktreeBranch != null && names.Contains(t.WorktreeBranch))
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (hit is { } sameBranch) return new(sameBranch, "same_branch_open");
        // Review 370083f1 A-3: the owner's own pending requests count too (a new land of the same unchanged branch,
        // for example to another target, still needs its source). A completed land's request is no longer pending, so
        // this holds only while some land of the branch is unfinished; it fails closed, never deletes early.
        hit = await db.AgentTaskLandRequests.AsNoTracking()
            .Where(r => r.IsPending)
            .Join(db.AgentTasks.AsNoTracking(), r => r.TaskId, t => t.Id, (r, t) => t)
            .Where(t => t.Id == owner || t.WorktreeBranch != null && names.Contains(t.WorktreeBranch))
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (hit is { } pendingLand) return new(pendingLand, "pending_land_request");
        // The brief's card rule: a branch bound to a card that is not Done is kept until the card is Done.
        hit = await db.AgentTasks.AsNoTracking()
            .Where(t => (t.Id == owner || t.WorktreeBranch != null && names.Contains(t.WorktreeBranch)) && t.CardId != null)
            .Join(db.Cards.AsNoTracking(), t => t.CardId, c => (Guid?)c.Id, (t, c) => new { t.Id, c.Status })
            .Where(x => x.Status != CardStatus.Done)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (hit is { } card) return new(card, "card_not_done");
        return null;
    }
}
