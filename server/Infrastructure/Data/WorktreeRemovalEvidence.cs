using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Infrastructure.Data;

public sealed class WorktreeRemovalEvidence(IServiceScopeFactory scopes) : IWorktreeRemovalEvidence
{
    public async Task<AgentTaskLanding?> ReadAsync(Guid operationId, CancellationToken ct)
    {
        // Independent context: unsaved tracked mutations cannot authorize deletion.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var op = await db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId, ct);
        if (op is null) return null;
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == op.TaskId, ct);
        if (task is null || task.ActiveLandingId != op.Id
            || task.SourceLandingOperationId is not null || task.Role == AgentTaskRole.Mutation
            || task.Status != Domain.Enums.AgentTaskStatus.Succeeded
            || FullRef(task.WorktreeBranch) != op.SourceFullRef
            || FullRef(task.MergeTargetRef ?? "master") != op.TargetFullRef
            || !SamePath(task.RepoPath, op.RepositoryPath) || !SamePath(task.WorktreePath, op.WorktreePath)) return null;
        return op;
    }

    public async Task<VerificationRemovalAuthority?> ReadVerificationAsync(WorktreeRemovalRequest request, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var git = scope.ServiceProvider.GetRequiredService<ILandingGit>();
        var manager = scope.ServiceProvider.GetRequiredService<IWorktreeManager>();
        try
        {
            var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == request.Source.TaskId, ct);
            if (request.Purpose != WorktreeRemovalPurpose.Verification || task is null
                || task.Role != AgentTaskRole.Mutation || task.Workspace != WorkspaceMode.Worktree
                || task.Status is not (AgentTaskStatus.Succeeded or AgentTaskStatus.Failed or AgentTaskStatus.Canceled)
                || task.SourceLandingOperationId is not Guid operationId || task.VerificationCustodyContractVersion != 1
                || task.VerificationCleanupSealJson is null || task.VerificationCreationJson is null
                || task.SourceLandingSha != request.ExpectedSourceSha || task.WorktreeBaseSha != task.SourceLandingSha
                || task.MergeTargetRef is not null || string.IsNullOrWhiteSpace(task.Result)) return null;
            var seal = JsonSerializer.Deserialize<VerificationCleanupSeal>(task.VerificationCleanupSealJson)!;
            var creation = JsonSerializer.Deserialize<VerificationCreationCoordinates>(task.VerificationCreationJson)!;
            if (seal.Id != request.VerificationSealId || seal.Revision != task.VerificationExecutionRevision
                || seal.Creation != creation || creation.Branch != $"feat/card-task-{DelegationReportFormatter.Short(task.Id)}"
                || task.WorktreeBranch != creation.Branch || request.Source.SourceFullRef != "refs/heads/" + creation.Branch
                || !SamePath(creation.RepositoryPath, request.Source.RepositoryPath)
                || !SamePath(task.RepoPath, creation.RepositoryPath) || !SamePath(task.WorktreePath, creation.WorktreePath)
                || !SamePath(creation.WorktreePath, request.Source.WorktreePath)
                || !SamePath(creation.WorktreeGitDirectory, request.GitDirectory)
                || !SamePath(creation.CommonGitDirectory, request.CommonDirectory)) return null;
            var op = await db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId, ct);
            if (op is null || !new AgentTaskLandingState().HasPublication(op) || op.VerifiedSourceSha != task.SourceLandingSha
                || !SamePath(op.CommonDirectory, creation.CommonGitDirectory)) return null;
            var metadata = await manager.ReadVerificationCreationAsync(creation.WorktreePath, ct);
            if (metadata is null || metadata.CreationId != creation.CreationId || metadata.InitialSha != task.SourceLandingSha
                || metadata.Branch != creation.Branch || !SamePath(metadata.RepositoryPath, creation.RepositoryPath)
                || !SamePath(metadata.WorktreePath, creation.WorktreePath) || !SamePath(metadata.GitDirectory, creation.WorktreeGitDirectory)) return null;
            var executions = await db.VerificationExecutions.AsNoTracking().Where(e => e.TaskId == task.Id).ToListAsync(ct);
            if (executions.Count != seal.Revision || !executions.Select(e => e.Id).Order().SequenceEqual(seal.Executions.Order())
                || executions.Count == 0 && !seal.NeverReserved) return null;
            foreach (var row in executions)
            {
                var binding = JsonSerializer.Deserialize<VerificationExecutionBinding>(row.BindingJson)!;
                if (binding.Source != new VerificationSourceIdentity(task.Id, operationId, task.SourceLandingSha!)
                    || binding.Creation != creation || binding.CustodyContractVersion != 1 || binding.Backend != "windows-job-v1") return null;
                new VerificationReceiptPolicy().ValidateImported(row, binding);
            }
            var sessionIds = executions.Select(e => e.SessionId).ToList();
            if (await db.AgentSessions.AsNoTracking().AnyAsync(s => sessionIds.Contains(s.Id)
                    && (s.Status == SessionStatus.Running || s.Status == SessionStatus.Starting), ct)
                || await db.Agents.AsNoTracking().AnyAsync(a => a.Id == task.AgentId
                    && (a.Status == AgentStatus.Running || a.PoolIdleSince != null), ct)) return null;
            var livePaths = await db.AgentSessions.AsNoTracking().Where(s => s.Status == SessionStatus.Running || s.Status == SessionStatus.Starting)
                .Select(s => s.Cwd).ToListAsync(ct);
            livePaths.AddRange(await db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id
                    && (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked))
                .Select(t => t.WorktreePath ?? t.WorkingDirectory).ToListAsync(ct));
            livePaths.AddRange(await db.Agents.AsNoTracking().Where(a => a.Status == AgentStatus.Running || a.PoolIdleSince != null)
                .Select(a => a.WorkingDirectory).ToListAsync(ct));
            foreach (var path in livePaths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                if (DelegationWorkspaceResolver.IsWithinRoot(path, creation.WorktreePath)) return null;
                if (Directory.Exists(path) && DelegationWorkspaceResolver.IsWithinRoot(await git.CanonicalDirectoryAsync(path, ct), creation.WorktreePath)) return null;
            }
            var root = Path.Combine(creation.CommonGitDirectory, "antiphon", "verification", operationId.ToString("N"), task.Id.ToString("N"));
            var canonicalRoot = await git.CanonicalDirectoryAsync(root, ct);
            if (!SamePath(root, canonicalRoot) || canonicalRoot.StartsWith(creation.WorktreePath + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return null;
            var evidencePath = Path.Combine(root, "restoration.json");
            if ((File.GetAttributes(evidencePath) & FileAttributes.ReparsePoint) != 0) return null;
            var restoration = JsonSerializer.Deserialize<VerificationRestoration>(await File.ReadAllBytesAsync(evidencePath, ct), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (restoration is null || restoration.SchemaVersion != 1 || !restoration.Restored
                || restoration.Source != new VerificationSourceIdentity(task.Id, operationId, task.SourceLandingSha!)
                || restoration.CreationId != creation.CreationId || string.IsNullOrWhiteSpace(restoration.Disposition)
                || restoration.Outputs is null
                || restoration.ReportSha256 != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(task.Result)))) return null;
            return new(seal, restoration, task.VerificationCleanupStartedAt);
        }
        catch (Exception ex) when (ex is JsonException or VerificationCustodyException or IOException or UnauthorizedAccessException or ArgumentException)
        { return null; }
    }

    public async Task<bool> RecordVerificationRemovalStartAsync(WorktreeRemovalRequest request, CancellationToken ct)
    {
        if (await ReadVerificationAsync(request, ct) is null) return false;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var task = await db.AgentTasks.FromSqlInterpolated($"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {request.Source.TaskId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
        if (task?.VerificationCleanupSealJson is null
            || JsonSerializer.Deserialize<VerificationCleanupSeal>(task.VerificationCleanupSealJson)?.Id != request.VerificationSealId) return false;
        task.VerificationCleanupStartedAt ??= scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    private static string? FullRef(string? value) => value is null ? null
        : value.StartsWith("refs/", StringComparison.Ordinal) ? value : "refs/heads/" + value;
    private static bool SamePath(string? left, string right) => left is not null && string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
