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

/// <summary>Reservation and runner-call intent share the task row lock with irreversible cleanup sealing.</summary>
public sealed class VerificationExecutionService(AppDbContext db, SourceLandingAdmission admission, TimeProvider clock, ILandingGit git)
{
    public async Task<VerificationExecutionBinding> ReserveAsync(AgentTask task, AgentSession session, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Reservation requires the task admission transaction.");
        if (task.VerificationCleanupSealJson is not null || task.VerificationCustodyContractVersion != 1
            || task.VerificationCreationJson is null || task.SourceLandingOperationId is not Guid operation
            || task.SourceLandingSha is null || session.SessionBackend != SessionBackend.PtyHost)
            throw new ConflictException("verification_reservation_refused");
        if (await db.VerificationExecutions.AnyAsync(e => e.TaskId == task.Id && e.ReceiptBytes == null, ct))
            throw new ConflictException("Previous verification execution has unresolved custody.", "verification_custody_unresolved");
        var store = await admission.RequireSupportAsync(ct);
        session.StartedAt = new DateTime(session.StartedAt.Ticks - session.StartedAt.Ticks % 10, DateTimeKind.Utc);
        var binding = new VerificationExecutionBinding(Guid.NewGuid(), new(task.Id, operation, task.SourceLandingSha),
            new(session.Id, session.StartedAt), JsonSerializer.Deserialize<VerificationCreationCoordinates>(task.VerificationCreationJson)
                ?? throw new ConflictException("verification_creation_identity_mismatch"), RunnerStoreId: store);
        db.VerificationExecutions.Add(new()
        {
            Id = binding.ExecutionId, TaskId = task.Id, SourceLandingOperationId = operation,
            SessionId = session.Id, AcceptedStartedAt = session.StartedAt,
            BindingJson = JsonSerializer.Serialize(binding), CreatedAt = clock.GetUtcNow().UtcDateTime,
        });
        task.VerificationExecutionRevision++;
        return binding;
    }

    public async Task<AgentLaunchSpec> PrepareLaunchAsync(AgentSession session, AgentLaunchSpec spec, CancellationToken ct)
    {
        // Search retained history, not only today's task-to-session pointer. Legacy launch requests
        // cannot bypass custody by dropping the optional wire binding or reusing a SessionId.
        var attempts = await db.VerificationExecutions.AsNoTracking().Where(e => e.SessionId == session.Id).ToListAsync(ct);
        var sourcedTask = await db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId == session.Id && t.SourceLandingOperationId != null).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        var snapshots = await db.AgentTasks.AsNoTracking().Where(t => t.SourceLandingOperationId != null && t.WorktreePath != null)
            .Select(t => new { t.Id, t.WorktreePath }).ToListAsync(ct);
        if (snapshots.Count != 0)
        {
            var cwd = await git.CanonicalDirectoryAsync(spec.Cwd, ct);
            foreach (var snapshot in snapshots)
                if (Directory.Exists(snapshot.WorktreePath)
                    && DelegationWorkspaceResolver.IsWithinRoot(cwd, await git.CanonicalDirectoryAsync(snapshot.WorktreePath!, ct)))
                {
                    if (sourcedTask != snapshot.Id) throw new ConflictException("verification_snapshot_owned_by_another_task");
                }
        }
        if (attempts.Count == 0 && sourcedTask is null && spec.VerificationBinding is null) return spec;
        var execution = attempts.SingleOrDefault(e => e.AcceptedStartedAt == session.StartedAt);
        if (execution is null) throw new ConflictException("verification_binding_required");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var task = await db.AgentTasks.FromSqlInterpolated($"SELECT * FROM \"AgentTasks\" WHERE \"Id\" = {execution.TaskId} FOR UPDATE")
            .AsNoTracking().SingleAsync(ct);
        var current = await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id, ct);
        var binding = JsonSerializer.Deserialize<VerificationExecutionBinding>(execution.BindingJson)
            ?? throw new ConflictException("verification_binding_required");
        if (binding.ExecutionId != execution.Id || binding.Source.TaskId != task.Id
            || binding.Generation != new VerificationSessionGeneration(session.Id, execution.AcceptedStartedAt)
            || binding.CustodyContractVersion != 1 || binding.Backend != "windows-job-v1" || binding.RunnerStoreId == Guid.Empty
            || task.VerificationCleanupSealJson is not null || task.Status is not (AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
            || task.AgentSessionId != session.Id || current.StartedAt != binding.Generation.AcceptedStartedAt
            || current.Status is not (SessionStatus.Starting or SessionStatus.Running)
            || task.SourceLandingOperationId != binding.Source.SourceOperationId || task.SourceLandingSha != binding.Source.LandedSha
            || task.VerificationCreationJson != JsonSerializer.Serialize(binding.Creation)
            || spec.VerificationBinding is not null && spec.VerificationBinding != binding
            || spec.Backend != SessionBackend.PtyHost || spec.Cwd != binding.Creation.WorktreePath)
            throw new ConflictException("verification_custody_identity_mismatch_or_sealed");
        await db.VerificationExecutions.Where(e => e.Id == execution.Id && e.RunnerCallIntentAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RunnerCallIntentAt, clock.GetUtcNow().UtcDateTime), ct);
        await tx.CommitAsync(ct);
        return spec with { VerificationBinding = binding };
    }
}
