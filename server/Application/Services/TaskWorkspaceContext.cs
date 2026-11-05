using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Recorded coordinates, not execution or deletion authority. Consumers validate the live repository common
/// directory and remote execution binding before crossing their respective I/O boundaries.
/// </summary>
public sealed record TaskWorkspaceContext(DesktopWorkspaceKind Kind, Guid? BindingId, string IdentityPath,
    string GitWorkingDirectory, string? CommonDirectory, string? ExecutionDirectory, string FullRef, string? BaseSha)
{
    public TaskRefWorkspacePreparationState? PreparationState { get; init; }
    public long? BindingRevision { get; init; }
}

public sealed class TaskWorkspaceContextResolver(AppDbContext db)
{
    /// <summary>Re-read the persisted owner after an I/O/lease gap; a caller's stale row is never authority.</summary>
    public async Task<TaskWorkspaceContext> ResolveCurrentAsync(AgentTask task, CancellationToken ct)
    {
        var current = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == task.Id, ct);
        if (current is null || current.DesktopWorkspaceKind != task.DesktopWorkspaceKind
            || current.TaskRefWorkspaceBindingId != task.TaskRefWorkspaceBindingId
            || current.RepoPath != task.RepoPath || current.WorktreePath != task.WorktreePath
            || current.WorktreeBranch != task.WorktreeBranch || current.Attempt != task.Attempt
            || current.WorktreeBaseSha != task.WorktreeBaseSha || current.WorktreeBaseRef != task.WorktreeBaseRef
            || current.WorktreeBaseSource != task.WorktreeBaseSource || current.WorktreeBaseTaskId != task.WorktreeBaseTaskId
            || current.ProgressBaselineJson != task.ProgressBaselineJson
            || current.RunnerId != task.RunnerId || current.RemoteWorktreePath != task.RemoteWorktreePath)
            throw Refuse("ref_only_binding_state_changed");
        return await ResolveAsync(current, ct);
    }

    public async Task<TaskWorkspaceContext> ResolveAsync(AgentTask task, CancellationToken ct)
    {
        if (!Enum.IsDefined(task.DesktopWorkspaceKind))
            throw Refuse("desktop_workspace_kind_invalid");
        if (task.DesktopWorkspaceKind == DesktopWorkspaceKind.Checkout)
        {
            if (task.TaskRefWorkspaceBindingId is not null)
                throw Refuse("desktop_workspace_binding_contradictory");
            return new(DesktopWorkspaceKind.Checkout, null, task.WorktreePath ?? task.WorkingDirectory,
                task.WorktreePath ?? task.WorkingDirectory, null, task.WorktreePath,
                FullRef(task.WorktreeBranch), task.WorktreeBaseSha);
        }
        if (string.IsNullOrWhiteSpace(task.WorktreePath))
            throw Refuse("source_coordinates_missing");
        if (string.IsNullOrWhiteSpace(task.RepoPath))
            throw Refuse("ref_only_repository_required");
        if (task.TaskRefWorkspaceBindingId is not Guid id || id == Guid.Empty)
            throw Refuse("ref_only_binding_required");
        var binding = await db.TaskRefWorkspaceBindings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (binding is null)
            throw Refuse("ref_only_binding_identity_mismatch");
        if (binding.TaskId != task.Id)
            throw Refuse("ref_only_binding_task_mismatch");
        if (!SamePath(binding.RepositoryPath, task.RepoPath))
            throw Refuse("ref_only_binding_repository_mismatch");
        if (string.IsNullOrWhiteSpace(binding.CommonDirectory))
            throw Refuse("ref_only_common_directory_required");
        Absolute(binding.CommonDirectory, "ref_only_common_directory_required");
        if (SamePath(binding.IdentityPath, binding.RepositoryPath) || SamePath(binding.IdentityPath, binding.CommonDirectory))
            throw Refuse("ref_only_identity_not_unique");
        if (!SamePath(binding.IdentityPath, task.WorktreePath))
            throw Refuse("ref_only_binding_identity_path_mismatch");
        if (binding.FullRef != FullRef(task.WorktreeBranch) || binding.FullRef != FullRef(binding.Branch)
            || !binding.FullRef.StartsWith("refs/heads/", StringComparison.Ordinal))
            throw Refuse("ref_only_binding_ref_mismatch");
        if (!GitObjectId.IsFull(binding.SelectedBaseSha) || binding.SelectedBaseSha != task.WorktreeBaseSha
            || binding.SelectedBaseRef != task.WorktreeBaseRef || binding.SelectedBaseSource != task.WorktreeBaseSource
            || binding.SelectedBaseTaskId != task.WorktreeBaseTaskId)
            throw Refuse("ref_only_binding_base_mismatch");
        if (!Enum.IsDefined(binding.PreparationState) || binding.Revision < 0)
            throw Refuse("ref_only_binding_preparation_invalid");
        return new(DesktopWorkspaceKind.RefOnly, binding.Id, binding.IdentityPath, binding.RepositoryPath,
            binding.CommonDirectory, task.RemoteWorktreePath, binding.FullRef, binding.SelectedBaseSha)
        { PreparationState = binding.PreparationState, BindingRevision = binding.Revision };
    }

    public string IdentityForNewBinding(string configuredRoot, Guid taskId)
    {
        if (taskId == Guid.Empty) throw Refuse("ref_only_task_identity_required");
        return Path.Combine(Absolute(configuredRoot, "ref_only_identity_root_invalid"), ".ref-only", taskId.ToString("N"));
    }

    public static void RequireRepositoryIdentity(TaskWorkspaceContext context, string commonDirectory)
    {
        if (context.Kind == DesktopWorkspaceKind.RefOnly
            && (context.CommonDirectory is null || !SamePath(context.CommonDirectory, commonDirectory)))
            throw Refuse("ref_only_binding_repository_mismatch");
    }

    /// <summary>
    /// Record an already selected new preparation before any ref mutation. This method neither creates a ref nor
    /// launches preparation. S5b-3 owns admission and the repository lease around the subsequent Git crossing.
    /// An existing recorded binding is resolved unconditionally; settings and attempts cannot select it again.
    /// </summary>
    public async Task<TaskWorkspaceContext> RecordPlannedAsync(AgentTask task, string commonDirectory, string configuredRoot,
        CancellationToken ct)
    {
        if (task.DesktopWorkspaceKind != DesktopWorkspaceKind.Checkout || task.TaskRefWorkspaceBindingId is not null)
            return await ResolveAsync(task, ct);
        if (!RemoteWorkspaceService.IsEligible(task) || task.WorktreePath is not null)
            throw Refuse("ref_only_existing_workspace_not_convertible");
        var repository = Absolute(task.RepoPath, "ref_only_repository_required");
        var common = Absolute(commonDirectory, "ref_only_common_directory_required");
        var identity = IdentityForNewBinding(configuredRoot, task.Id);
        var fullRef = FullRef(task.WorktreeBranch);
        if (!fullRef.StartsWith("refs/heads/", StringComparison.Ordinal) || fullRef.Length <= 11
            || !GitObjectId.IsFull(task.WorktreeBaseSha) || string.IsNullOrWhiteSpace(task.WorktreeBaseRef)
            || task.WorktreeBaseSource == WorktreeBaseSource.Unset || !Enum.IsDefined(task.WorktreeBaseSource))
            throw Refuse("ref_only_selected_base_required");
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {task.Id} FOR UPDATE", ct);
        var persisted = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        if (persisted.DesktopWorkspaceKind != DesktopWorkspaceKind.Checkout || persisted.TaskRefWorkspaceBindingId is not null
            || persisted.WorktreePath is not null)
            throw Refuse("ref_only_binding_state_changed");
        if (await db.TaskRefWorkspaceBindings.AnyAsync(b => b.TaskId == task.Id, ct))
            throw Refuse("ref_only_binding_state_changed");
        var owners = await db.AgentTasks.AsNoTracking()
            .Where(t => t.Id != task.Id && t.RepoPath != null
                && (t.WorktreeBranch == fullRef || t.WorktreeBranch == fullRef.Substring(11)))
            .Select(t => new { t.RepoPath, t.WorktreeBranch }).ToListAsync(ct);
        if (owners.Any(t => SamePath(t.RepoPath!, repository) && FullRef(t.WorktreeBranch) == fullRef))
            throw Refuse("ref_only_legacy_ref_owner_conflict");
        if (await db.TaskRefWorkspaceBindings.AnyAsync(b => b.RepositoryPath == repository && b.FullRef == fullRef, ct))
            throw Refuse("ref_only_binding_owner_conflict");
        var binding = new TaskRefWorkspaceBinding
        {
            Id = Guid.NewGuid(), TaskId = task.Id, RepositoryPath = repository, CommonDirectory = common,
            IdentityPath = identity, Branch = fullRef[11..], FullRef = fullRef, SelectedBaseSha = task.WorktreeBaseSha!,
            SelectedBaseRef = task.WorktreeBaseRef!, SelectedBaseSource = task.WorktreeBaseSource,
            SelectedBaseTaskId = task.WorktreeBaseTaskId, PreparationState = TaskRefWorkspacePreparationState.Planned,
            Revision = 0,
        };
        var previousToken = task.ConcurrencyToken;
        task.DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly;
        task.TaskRefWorkspaceBindingId = binding.Id;
        task.WorktreePath = identity;
        task.ConcurrencyToken = Guid.NewGuid();
        db.TaskRefWorkspaceBindings.Add(binding);
        try
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            db.Entry(binding).State = EntityState.Detached;
            task.DesktopWorkspaceKind = DesktopWorkspaceKind.Checkout;
            task.TaskRefWorkspaceBindingId = null;
            task.WorktreePath = null;
            task.ConcurrencyToken = previousToken;
            if (transaction is not null) await transaction.RollbackAsync(ct);
            if (ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
                throw Refuse("ref_only_binding_owner_conflict");
            throw;
        }
        return await ResolveAsync(task, ct);
    }

    private static string FullRef(string? branch) => string.IsNullOrWhiteSpace(branch) ? ""
        : branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;

    private static string Absolute(string? path, string reason)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Any(char.IsControl))
            throw Refuse(reason);
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw Refuse(reason); }
    }

    private static bool SamePath(string left, string right) =>
        WorkspaceReservationKey.PathsEqual(Absolute(left, "ref_only_binding_path_invalid"), Absolute(right, "ref_only_binding_path_invalid"));
    private static ConflictException Refuse(string code) => new(code, code);
}
