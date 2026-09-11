using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Explicit cleanup seals launch admission; it never stops or kills a process.</summary>
public sealed class VerificationCleanupService(AppDbContext db, ISessionRunnerClient runner,
    IRepositoryMutationLease leases, IWorktreeManager worktrees, TimeProvider clock)
{
    public async Task<WorktreeRemoval> CleanupAsync(Guid taskId, CancellationToken ct)
    {
        VerificationCleanupSeal seal;
        AgentTask task;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            task = await db.AgentTasks.FromSqlInterpolated($"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {taskId} FOR UPDATE")
                .SingleOrDefaultAsync(ct) ?? throw new NotFoundException(nameof(AgentTask), taskId);
            await db.Entry(task).ReloadAsync(ct);
            if (task.Role != AgentTaskRole.Mutation || task.Workspace != WorkspaceMode.Worktree
                || task.SourceLandingOperationId is null || task.VerificationCustodyContractVersion != 1
                || task.VerificationCreationJson is null
                || task.Status is not (AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled))
                throw new ConflictException("verification_cleanup_requires_terminal_snapshot");
            var attempts = await db.VerificationExecutions.AsNoTracking().Where(e => e.TaskId == taskId).Select(e => e.Id).ToListAsync(ct);
            seal = task.VerificationCleanupSealJson is not null
                ? JsonSerializer.Deserialize<VerificationCleanupSeal>(task.VerificationCleanupSealJson)!
                : new(Guid.NewGuid(), task.VerificationExecutionRevision,
                    JsonSerializer.Deserialize<VerificationCreationCoordinates>(task.VerificationCreationJson)!,
                    attempts.Order().ToArray(), attempts.Count == 0 && task.VerificationExecutionRevision == 0, clock.GetUtcNow().UtcDateTime);
            task.VerificationCleanupSealJson ??= JsonSerializer.Serialize(seal);
            task.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // No repository lock across runner I/O. Even a failure leaves the seal standing.
        foreach (var executionId in seal.Executions)
        {
            var execution = await db.VerificationExecutions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == executionId && e.TaskId == taskId, ct);
            if (execution is null) return await ResidueAsync(task, "verification_execution_history_missing", ct);
            try
            {
                var binding = JsonSerializer.Deserialize<VerificationExecutionBinding>(execution.BindingJson)!;
                if (execution.ReceiptBytes is not null)
                {
                    new VerificationReceiptPolicy().ValidateImported(execution, binding);
                    continue;
                }
                var status = await runner.ReadVerificationCustodyAsync(binding, seal: true, ct);
                if (status.Binding != binding || status.Receipt is null || status.Host is null)
                    return await ResidueAsync(task, $"{execution.Id:D}: {status.Reason ?? status.State.ToString()}", ct);
                var receipt = new VerificationReceiptPolicy().Validate(status.Receipt, binding, status.Host);
                if (receipt.Disposition != status.State)
                    return await ResidueAsync(task, "verification_custody_invalid_receipt", ct);
                await using var import = await db.Database.BeginTransactionAsync(ct);
                var row = await db.VerificationExecutions.FromSqlInterpolated(
                    $"SELECT * FROM \"VerificationExecutions\" WHERE \"Id\" = {executionId} FOR UPDATE").SingleAsync(ct);
                await db.Entry(row).ReloadAsync(ct);
                var host = JsonSerializer.Serialize(status.Host);
                if (row.BindingJson != execution.BindingJson || row.HostIdentityJson is not null && row.HostIdentityJson != host
                    || row.ReceiptBytes is not null && !row.ReceiptBytes.AsSpan().SequenceEqual(status.Receipt))
                    throw new VerificationCustodyException("verification_custody_conflicting_receipt");
                row.HostIdentityJson ??= host;
                row.ReceiptBytes ??= status.Receipt;
                row.ReceiptDigest ??= Convert.ToHexString(SHA256.HashData(status.Receipt));
                row.ReceiptImportedAt ??= clock.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(ct);
                await import.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is VerificationCustodyException or JsonException or HttpRequestException or ConflictException)
            {
                return await ResidueAsync(task, $"{executionId:D}: verification_custody_unavailable ({ex.GetType().Name})", ct);
            }
        }

        await using var lease = await leases.TryAcquireAsync(seal.Creation.RepositoryPath, ct);
        if (lease is null) return await ResidueAsync(task, "repository_busy_or_child_recovery_required", ct);
        var source = new LandSourceCoordinates(taskId, seal.Creation.RepositoryPath, seal.Creation.WorktreePath,
            "refs/heads/" + seal.Creation.Branch, "");
        // An explicit durable start permits recovery of a removal interrupted after Git removed
        // the tree. Its initial presence/identity is checked by the removal implementation.
        var result = await worktrees.TryRemoveAsync(new(WorktreeRemovalPurpose.Verification, source,
            seal.Creation.CommonGitDirectory, seal.Creation.WorktreeGitDirectory, task.SourceLandingSha!,
            task.SourceLandingSha!, null, lease, VerificationSealId: seal.Id), ct);
        task.VerificationDirectoryRemoved |= result.DirectoryGone;
        task.VerificationRegistrationRemoved |= result.Unregistered;
        task.VerificationBranchRemoved |= result.BranchDeleted;
        task.VerificationCleanupResidue = result.Residue;
        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<WorktreeRemoval> ResidueAsync(AgentTask task, string reason, CancellationToken ct)
    {
        task.VerificationCleanupResidue = reason;
        await db.SaveChangesAsync(ct);
        return new(task.VerificationRegistrationRemoved, task.VerificationDirectoryRemoved, task.VerificationBranchRemoved, reason);
    }
}
