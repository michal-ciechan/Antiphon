using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Initial ref-only producer. Admission belongs to the dispatcher.</summary>
public sealed class RefOnlyDesktopProvisioner(AppDbContext db, TaskWorkspaceContextResolver contexts,
    DelegationWorktreeService worktrees, ILandingGit git, ITaskProgressGit progress,
    IRepositoryMutationLease leases, IOptions<GitSettings> settings)
{
    public Task<TaskWorkspaceContext> PlanAsync(AgentTask task, RepositoryLease lease, CancellationToken ct) =>
        PlanAsync(task, lease, null, null, null, null, ct);

    /// <summary>Freeze source and durable ownership. No ref, directory or registration is created.</summary>
    public async Task<TaskWorkspaceContext> PlanAsync(AgentTask task, RepositoryLease lease,
        string? repairSha, ResolvedBase? cardBase, Guid? cardBaseTaskId, string? cardBaseBranch, CancellationToken ct)
    {
        if (task.DesktopWorkspaceKind != DesktopWorkspaceKind.Checkout || task.TaskRefWorkspaceBindingId is not null)
            return await contexts.ResolveCurrentAsync(task, ct);
        if (!RemoteWorkspaceService.IsEligible(task) || RunnerRequestIntent.IsDesktopAlias(task.RunnerId)
            || task.WorktreePath is not null || task.WorktreeBranch is not null)
            throw Refuse("ref_only_existing_workspace_not_convertible");
        var repo = await git.CanonicalDirectoryAsync(task.RepoPath!, ct);
        var common = await git.CommonDirectoryAsync(repo, ct);
        if (!leases.Owns(lease, common)) throw Refuse("ref_only_repository_lease_required");
        var resolved = await worktrees.SelectBaseAsync(task, repairSha, cardBase, ct);
        var selected = await progress.RevParseCommitAsync(repo, resolved.Ref, ct);
        if (!selected.Succeeded || !GitObjectId.IsFull(selected.Sha)) throw Refuse("ref_only_selected_base_required");
        var branch = await worktrees.MintBranchAsync(task, ct);
        var endpoint = await progress.EndpointFingerprintAsync(repo, ct);
        if (endpoint is null) throw Refuse("ref_only_endpoint_required");
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        task.RepoPath = repo;
        task.WorktreeBranch = branch;
        task.WorktreeBaseSha = selected.Sha;
        task.WorktreeBaseRef = resolved.Ref;
        task.WorktreeBaseSource = resolved.Source;
        task.WorktreeBaseTaskId = resolved.Source switch
        {
            WorktreeBaseSource.Repair => task.RepairSourceTaskId,
            WorktreeBaseSource.CardCurrent => cardBaseTaskId,
            _ => null,
        };
        task.WorktreeBaseBranch = resolved.Source == WorktreeBaseSource.CardCurrent ? cardBaseBranch : null;
        var context = await contexts.RecordPlannedAsync(task, common,
            Infrastructure.Git.WorktreeRoots.Resolve(settings.Value), ct);
        // Persist the endpoint before the producer crosses Git. This is an intent baseline;
        // Published is independently required by every settlement reader.
        var now = DateTime.UtcNow;
        task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(new(2, now, now,
            new(repo, common, task.Id, null, context.FullRef, selected.Sha!,
                new(ProgressRemoteState.Unavailable, EndpointFingerprint: endpoint, Reason: "ref_only_planned"))
            { DesktopWorkspaceKind = DesktopWorkspaceKind.RefOnly, TaskRefWorkspaceBindingId = context.BindingId }, null));
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return context;
    }

    private static ConflictException Refuse(string code) => new(code, code);
}
