using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>The operation supplies source identity, never authorization or a capacity bucket.</summary>
public sealed class SourceLandingAdmission(AppDbContext db, ILandingGit git, ISessionRunnerClient runner)
{
    public async Task<AgentTaskLanding> RequireSourceAsync(AgentTask task, CancellationToken ct)
    {
        if (task.Kind != AgentTaskKind.Worker || task.Role != AgentTaskRole.Mutation
            || task.Workspace != WorkspaceMode.Worktree || task.MergeTargetRef is not null
            || task.FollowUpOfTaskId is not null || task.CardId is null || task.RepoPath is null)
            throw new ConflictException("SourceLanding requires a fresh Worker/Mutation Worktree and a companion card.", "verification_source_mode");
        var op = await db.AgentTaskLandings.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == task.SourceLandingOperationId, ct);
        if (op is null || !new AgentTaskLandingState().HasPublication(op))
            throw new ConflictException("SourceLanding requires confirmed structured publication.", "verification_source_unconfirmed");
        var source = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == op.TaskId, ct);
        if (source is null || source.ProjectId != task.ProjectId || source.RepoPath is null
            || source.CardId is null || source.CardId == task.CardId
            || source.Role == AgentTaskRole.Mutation)
            throw new ConflictException("SourceLanding source task/project/card does not match.", "verification_source_identity_mismatch");
        var cards = await db.Cards.AsNoTracking().Where(c => c.Id == task.CardId || c.Id == source.CardId)
            .Select(c => new { c.Id, c.BoardId }).ToListAsync(ct);
        if (cards.Count != 2 || cards[0].BoardId != cards[1].BoardId)
            throw new ConflictException("SourceLanding needs a distinct companion on the original board.", "verification_source_card_mismatch");
        var common = await git.CommonDirectoryAsync(task.RepoPath, ct);
        if (!SamePath(common, await git.CanonicalDirectoryAsync(op.CommonDirectory, ct))
            || !SamePath(common, await git.CommonDirectoryAsync(source.RepoPath, ct))
            || !SamePath(common, await git.CommonDirectoryAsync(op.RepositoryPath, ct)))
            throw new ConflictException("SourceLanding repository identity does not match.", "verification_source_repository_mismatch");
        if (task.SourceLandingSha is not null && task.SourceLandingSha != op.VerifiedSourceSha)
            throw new ConflictException("SourceLanding commit changed.", "verification_source_identity_mismatch");
        return op;
    }

    public async Task<Guid> RequireSupportAsync(CancellationToken ct)
    {
        var capabilities = await runner.GetCapabilitiesAsync(ct);
        if (capabilities?.Features?.Contains(RunnerCapabilityFeatures.VerificationCustodyV1) != true
            || capabilities.VerificationCustodyBackend != "windows-job-v1"
            || capabilities.RunnerStoreId is not Guid id || id == Guid.Empty)
            throw new ConflictException("The selected runner does not support verification custody.", "verification_custody_unsupported_backend");
        return id;
    }

    public async Task RequireUniqueOpenAsync(Guid operationId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Source admission needs a transaction.");
        var key = "antiphon.verification.source:" + operationId.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({key}))", ct);
        var existing = await db.AgentTasks.AsNoTracking().Where(t => t.SourceLandingOperationId == operationId
            && (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Dispatched
                || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked))
            .Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (existing is Guid taskId)
            throw new ConflictException($"SourceLanding already has open task {taskId:D}.", "verification_source_already_open");
    }

    private bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
