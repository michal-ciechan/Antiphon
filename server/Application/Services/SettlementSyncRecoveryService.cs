using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1082 D-5. Retries an accepted desktop fast-forward. At most 32 due rows, Git outside
/// the claim transaction, and the only write is the debt row. An empty table costs the one
/// indexed read in <see cref="SweepAsync"/>. This service does not read
/// <c>RunnerSyncDebtOnSettlement</c>: that switch stops settlement from minting a row, and a
/// row already accepted keeps recovering.
/// </summary>
public sealed class SettlementSyncRecoveryService(AppDbContext db, RemoteWorkspaceService workspace,
    IEventBus events, TimeProvider clock, ILogger<SettlementSyncRecoveryService> logger)
{
    internal Func<string, CancellationToken, Task>? BoundaryAsync { get; set; }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || db.ChangeTracker.HasChanges()) return 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await db.AgentTaskSyncDebts.AsNoTracking()
            .Where(d => d.State == AgentTaskSyncDebtState.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id).Select(d => d.Id).Take(32).ToListAsync(ct);
        var attempted = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var claim = await ClaimAsync(id, now, ct);
                if (claim is null) continue;
                attempted++;
                if (BoundaryAsync is not null) await BoundaryAsync("claimed", ct);
                var result = await workspace.SyncSettledAsync(claim.Task, claim.Debt, ct);
                if (BoundaryAsync is not null) await BoundaryAsync("synced", ct);
                if (!await SaveAsync(claim, result, ct)) continue;
                if (BoundaryAsync is not null) await BoundaryAsync("saved", ct);
                // A missed publish is recoverable from the debt row on the next read.
                await events.PublishToGroupAsync("dashboard", "AgentTaskChanged",
                    new { taskId = claim.Task.Id, rootId = claim.Task.RootTaskId }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The claim already paced this row. A poison row must not starve its neighbor,
                // and the log must not keep a path or Git stderr.
                logger.LogWarning("Settlement sync debt {DebtId} remains pending ({Type})", id, ex.GetType().Name);
            }
        }
        return attempted;
    }

    private async Task<Claim?> ClaimAsync(Guid id, DateTime due, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var snapshot = await db.AgentTaskSyncDebts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        if (snapshot is null) return null;
        await LockTaskAsync(snapshot.TaskId, ct);
        var debt = await db.AgentTaskSyncDebts.AsNoTracking().SingleAsync(d => d.Id == id, ct);
        if (debt.State != AgentTaskSyncDebtState.Pending || debt.NextAttemptAt is null || debt.NextAttemptAt > due)
            return null;
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == debt.TaskId, ct);
        if (!EpisodeMatches(task, debt))
        {
            await TerminalAsync(debt, AgentTaskSyncDebtState.Held,
                RemoteSettlementSyncReasons.SettlementSyncEpisodeChanged, due, ct);
            await tx.CommitAsync(ct);
            return null;
        }
        if (await RegistrationGoneAsync(debt, ct))
        {
            await TerminalAsync(debt, AgentTaskSyncDebtState.Superseded,
                RemoteSettlementSyncReasons.SettlementSyncSuperseded, due, ct);
            await tx.CommitAsync(ct);
            return null;
        }
        var attempts = debt.Attempts + 1;
        var next = due.AddMinutes(BackoffMinutes(attempts));
        var changed = await db.AgentTaskSyncDebts.Where(d => d.Id == id && d.Revision == debt.Revision)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.Attempts, attempts)
                .SetProperty(d => d.NextAttemptAt, next)
                .SetProperty(d => d.UpdatedAt, due)
                .SetProperty(d => d.Revision, d => d.Revision + 1), ct);
        if (changed != 1) return null;
        await tx.CommitAsync(ct);
        debt.Revision++;
        debt.Attempts = attempts;
        debt.NextAttemptAt = next;
        return new Claim(task!, debt);
    }

    private async Task<bool> SaveAsync(Claim claim, RemoteSettlementSyncResult result, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(claim.Task.Id, ct);
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == claim.Task.Id, ct);
        var drifted = task is null
            || task.Status != claim.Task.Status
            || task.Attempt != claim.Task.Attempt
            || task.ProgressBaselineJson != claim.Task.ProgressBaselineJson
            || task.WorktreePath != claim.Task.WorktreePath;
        var superseded = result.Reason == RemoteSettlementSyncReasons.RetirementReserved
            || (result.Reason == RemoteSettlementSyncReasons.IdentityMismatch && WorktreeMissing(claim.Debt))
            || await RegistrationGoneAsync(claim.Debt, ct);
        var ready = result.Confirmed
            && string.Equals(result.DesktopAfterSha, claim.Debt.SourceSha, StringComparison.Ordinal)
            && string.Equals(result.RemoteSha, claim.Debt.SourceSha, StringComparison.Ordinal)
            && string.Equals(result.FullRef, claim.Debt.FullRef, StringComparison.Ordinal);
        var held = result.State is RemoteSettlementSyncState.Refused or RemoteSettlementSyncState.NotApplicable
            || result.Reason == RemoteSettlementSyncReasons.BaselineUnavailable
            || result.Reason == RemoteSettlementSyncReasons.BranchNotPushed;
        AgentTaskSyncDebtState state;
        string reason;
        string? confirmed = null;
        DateTime? readyAt = null;
        DateTime? next = null;
        if (drifted)
        {
            state = AgentTaskSyncDebtState.Held;
            reason = RemoteSettlementSyncReasons.SettlementSyncEpisodeChanged;
        }
        else if (superseded)
        {
            state = AgentTaskSyncDebtState.Superseded;
            reason = RemoteSettlementSyncReasons.SettlementSyncSuperseded;
        }
        else if (ready)
        {
            state = AgentTaskSyncDebtState.Ready;
            reason = RemoteSettlementSyncReasons.SettlementSyncReady;
            confirmed = claim.Debt.SourceSha;
            readyAt = clock.GetUtcNow().UtcDateTime;
        }
        else if (held)
        {
            state = AgentTaskSyncDebtState.Held;
            reason = result.Reason ?? "runner_sync_unavailable";
        }
        else
        {
            state = AgentTaskSyncDebtState.Pending;
            reason = result.Reason ?? RemoteSettlementSyncReasons.LeaseBusy;
            next = claim.Debt.NextAttemptAt;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var changed = await db.AgentTaskSyncDebts.Where(d => d.Id == claim.Debt.Id
                && d.Revision == claim.Debt.Revision
                && d.State == AgentTaskSyncDebtState.Pending
                && d.SourceSha == claim.Debt.SourceSha
                && d.Attempt == claim.Debt.Attempt)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.State, state)
                .SetProperty(d => d.ReasonCode, reason)
                .SetProperty(d => d.NextAttemptAt, next)
                .SetProperty(d => d.SourceReadyAt, readyAt)
                .SetProperty(d => d.ConfirmedSha, confirmed)
                .SetProperty(d => d.UpdatedAt, now)
                .SetProperty(d => d.Revision, d => d.Revision + 1), ct);
        await tx.CommitAsync(ct);
        return changed == 1;
    }

    private async Task TerminalAsync(AgentTaskSyncDebt debt, AgentTaskSyncDebtState state, string reason,
        DateTime now, CancellationToken ct) =>
        await db.AgentTaskSyncDebts.Where(d => d.Id == debt.Id && d.Revision == debt.Revision
                && d.State == AgentTaskSyncDebtState.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.State, state)
                .SetProperty(d => d.ReasonCode, reason)
                .SetProperty(d => d.NextAttemptAt, (DateTime?)null)
                .SetProperty(d => d.UpdatedAt, now)
                .SetProperty(d => d.Revision, d => d.Revision + 1), ct);

    private static bool EpisodeMatches(AgentTask? task, AgentTaskSyncDebt debt)
    {
        if (task is null) return false;
        if (task.Status is not (AgentTaskStatus.Succeeded or AgentTaskStatus.Failed)) return false;
        if (task.Attempt != debt.Attempt) return false;
        if (!string.Equals(task.WorktreePath, debt.WorktreePath, StringComparison.Ordinal)) return false;
        var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)?.Primary;
        return baseline is not null
            && string.Equals(baseline.LocalSha, debt.BaselineSha, StringComparison.Ordinal)
            && string.Equals(baseline.FullRef, debt.FullRef, StringComparison.Ordinal);
    }

    /// <summary>
    /// The recorded worktree is gone, or a retirement row exists for this attempt. Checked
    /// before Git so a retired checkout is not fast-forwarded.
    /// </summary>
    private async Task<bool> RegistrationGoneAsync(AgentTaskSyncDebt debt, CancellationToken ct)
    {
        if (WorktreeMissing(debt)) return true;
        return await db.TaskWorktreeRetirements.AsNoTracking()
            .AnyAsync(r => r.TaskId == debt.TaskId && r.TaskAttempt == debt.Attempt, ct);
    }

    private static bool WorktreeMissing(AgentTaskSyncDebt debt) =>
        string.IsNullOrWhiteSpace(debt.WorktreePath) || !Directory.Exists(debt.WorktreePath);

    private static int BackoffMinutes(int attempts) => attempts switch { 1 => 1, 2 => 2, 3 => 4, _ => 5 };

    private Task<int> LockTaskAsync(Guid taskId, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"""SELECT "Id" FROM "AgentTasks" WHERE "Id" = {taskId} FOR UPDATE""", ct);

    private sealed record Claim(AgentTask Task, AgentTaskSyncDebt Debt);
}
