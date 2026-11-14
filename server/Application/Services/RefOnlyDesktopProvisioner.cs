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
using System.Security.Cryptography;
using System.Text;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>Initial ref-only producer. Admission belongs to the dispatcher.</summary>
public sealed class RefOnlyDesktopProvisioner(AppDbContext db, TaskWorkspaceContextResolver contexts,
    DelegationWorktreeService worktrees, ILandingGit git, ITaskProgressGit progress,
    IRepositoryMutationLease leases, IOptions<GitSettings> settings,
    IOptions<DelegationSettings>? delegation = null)
{
    internal Func<string, CancellationToken, Task>? PreparationBoundaryAsync { get; set; }

    /// <summary>Initial publication, outside the dispatch claim. Never performs settlement or checkout recovery.</summary>
    public async Task<RemotePushResult> PublishAsync(AgentTask task, CancellationToken ct)
    {
        try
        {
            if (db.Database.CurrentTransaction is not null) throw Refuse("ref_only_claim_not_committed");
            var context = await contexts.ResolveCurrentAsync(task, ct);
            if (context.Kind != DesktopWorkspaceKind.RefOnly || !RemoteWorkspaceService.IsEligible(task)
                || RunnerRequestIntent.IsDesktopAlias(task.RunnerId)) throw Refuse("ref_only_requires_remote_runner");
            var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson);
            if (baseline is not { SchemaVersion: 2 } || baseline.Primary.DesktopWorkspaceKind != context.Kind
                || baseline.Primary.TaskRefWorkspaceBindingId != context.BindingId
                || baseline.Primary.OwnerTaskId != task.Id || baseline.Primary.RegisteredCheckout is not null
                || baseline.Primary.FullRef != context.FullRef || baseline.Primary.LocalSha != context.BaseSha
                || !WorkspaceReservationKey.PathsEqual(baseline.Primary.CanonicalRepository, context.GitWorkingDirectory)
                || !WorkspaceReservationKey.PathsEqual(baseline.Primary.CanonicalCommonDirectory, context.CommonDirectory!))
                throw Refuse("ref_only_baseline_identity_mismatch");
            var endpoint = baseline.Primary.Remote.EndpointFingerprint;
            if (string.IsNullOrWhiteSpace(endpoint)) throw Refuse("ref_only_endpoint_required");
            await using var lease = await leases.TryAcquireAsync(context.GitWorkingDirectory,
                new RepositoryLeaseOwnerTag(task.Id, RepositoryChildPurposes.RemotePrepPush), ct);
            if (lease is null) throw Refuse("repository_lease_busy");
            await RequireCurrentAsync(task, context, ct);
            await RequireGitIdentityAsync(context, endpoint, lease, ct);
            var repo = context.GitWorkingDirectory;
            var source = context.BaseSha!;
            var tip = await git.RunAsync(repo, ["show-ref", "--verify", "--hash", context.FullRef], ct);
            if (context.PreparationState == TaskRefWorkspacePreparationState.Planned)
            {
                if (tip.Succeeded)
                {
                    // The Git-side creation receipt closes the crash gap before RefCreated is saved.
                    // A coincidentally equal tip, including an unowned ref with no reflog, is insufficient.
                    var receipt = await git.RunAsync(repo, ["reflog", "show", "-1", "--format=%H%x09%gs", context.FullRef], ct);
                    if (tip.Output.Trim() != source || !receipt.Succeeded
                        || receipt.Output.Trim() != source + "\t" + CreationReceipt(context))
                        throw Refuse("ref_only_local_ref_collision");
                }
                else
                {
                    if (tip.ExitCode != 128 && tip.ExitCode != 1) throw Refuse("ref_only_local_ref_unreadable");
                    // show-ref --verify returns 128 for a missing full ref; prove absence independently.
                    var absent = await git.RunAsync(repo, ["show-ref", "--verify", "--quiet", context.FullRef], ct);
                    if (absent.ExitCode != 1) throw Refuse("ref_only_local_ref_unreadable");
                    await BoundaryAsync("before-local-ref", ct);
                    await RequireCurrentAsync(task, context, ct);
                    await RequireGitIdentityAsync(context, endpoint, lease, ct);
                    var created = await git.RunAsync(repo, ["update-ref", "--no-deref", "--create-reflog",
                        "-m", CreationReceipt(context), context.FullRef, source, new string('0', source.Length)], ct);
                    if (!created.Succeeded) throw Refuse("ref_only_local_ref_collision");
                    await BoundaryAsync("after-local-ref", ct);
                }
                context = await TransitionAsync(task, context, TaskRefWorkspacePreparationState.RefCreated, null, ct);
                await BoundaryAsync("ref-created", ct);
            }
            else if (!tip.Succeeded || !GitObjectId.IsFull(tip.Output.Trim()))
                throw Refuse("ref_only_local_ref_unreadable");

            await RequireCurrentAsync(task, context, ct);
            await RequireGitIdentityAsync(context, endpoint, lease, ct);
            var urls = await git.RunAsync(repo, ["remote", "get-url", "--push", "--all", "origin"], ct);
            var endpoints = urls.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (!urls.Succeeded || endpoints.Length != 1
                || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoints[0]))) != endpoint
                || !RepositoryCloneSource.TryNormalize(endpoints[0], out var repository))
                throw Refuse("ref_only_endpoint_changed");
            var remote = await progress.ObserveExactRefUnderLeaseAsync(repo, context.FullRef, endpoint, task.Id, lease, ct);
            if (context.PreparationState == TaskRefWorkspacePreparationState.Published)
            {
                // This path never pushes the creation SHA again. Progress must first synchronize
                // through the existing ancestry/CAS settlement protocol before a new mirror is cut.
                if (remote.State != ProgressRemoteState.Present || remote.Sha != tip.Output.Trim())
                    throw Refuse("ref_only_published_source_changed");
                return new(true, remote.Sha, null, repository);
            }
            if ((await progress.RevParseCommitAsync(repo, context.FullRef, ct)).Sha != source)
                throw Refuse("ref_only_local_ref_changed");
            var intent = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)!.Primary.Remote.Reason == PublicationIntent;
            if (remote.State == ProgressRemoteState.Missing)
            {
                if (!intent)
                    context = await TransitionAsync(task, context, TaskRefWorkspacePreparationState.RefCreated,
                        new(ProgressRemoteState.Missing, EndpointFingerprint: endpoint, Reason: PublicationIntent), ct);
                await BoundaryAsync("before-push", ct);
                await RequireCurrentAsync(task, context, ct);
                await RequireGitIdentityAsync(context, endpoint, lease, ct);
                var push = await git.RunAsync(repo,
                    ["push", "--no-follow-tags", "--force-with-lease=" + context.FullRef + ":",
                        endpoints[0], source + ":" + context.FullRef],
                    new LandingGitRunOptions(TimeSpan.FromMinutes((delegation?.Value ?? new()).RemotePrepPushBudgetMinutes),
                        new RepositoryChildTag(task.Id, RepositoryChildPurposes.RemotePrepPush)), ct);
                if (!push.Succeeded) throw Refuse("ref_only_initial_push_failed");
                await BoundaryAsync("after-push", ct);
                remote = await progress.ObserveExactRefUnderLeaseAsync(repo, context.FullRef, endpoint, task.Id, lease, ct);
            }
            else if (!intent || remote.State != ProgressRemoteState.Present || remote.Sha != source)
                throw Refuse("ref_only_remote_ref_collision");
            if (remote.State != ProgressRemoteState.Present || remote.Sha != source || remote.EndpointFingerprint != endpoint)
                throw Refuse("ref_only_publication_unconfirmed");
            await RequireCurrentAsync(task, context, ct);
            await RequireGitIdentityAsync(context, endpoint, lease, ct);
            if ((await progress.RevParseCommitAsync(repo, context.FullRef, ct)).Sha != source)
                throw Refuse("ref_only_local_ref_changed");
            foreach (var name in new[] { "primary-local", "primary-remote" })
                if (!(await progress.PinBaselineAsync(repo, task.Id, name, source, ct)).Succeeded)
                    throw Refuse("ref_only_baseline_pin_failed");
            await TransitionAsync(task, context, TaskRefWorkspacePreparationState.Published,
                new(ProgressRemoteState.Present, source, endpoint), ct);
            await BoundaryAsync("published", ct);
            return new(true, source, null, repository);
        }
        catch (ConflictException ex) { return new(false, null, ex.Code); }
    }

    private const string PublicationIntent = "ref_only_initial_publication_intent";
    private static string CreationReceipt(TaskWorkspaceContext context) => "antiphon ref-only binding " + context.BindingId!.Value.ToString("N");
    private Task BoundaryAsync(string site, CancellationToken ct) => PreparationBoundaryAsync?.Invoke(site, ct) ?? Task.CompletedTask;

    private async Task RequireCurrentAsync(AgentTask task, TaskWorkspaceContext expected, CancellationToken ct)
    {
        if (await contexts.ResolveCurrentAsync(task, ct) != expected
            || !await db.AgentTasks.AsNoTracking().AnyAsync(t => t.Id == task.Id && t.Attempt == task.Attempt
                && t.RunnerId == task.RunnerId && t.Status == AgentTaskStatus.Queued
                && t.ConcurrencyToken == task.ConcurrencyToken, ct))
            throw Refuse("ref_only_binding_state_changed");
    }

    private async Task RequireGitIdentityAsync(TaskWorkspaceContext context, string endpoint,
        RepositoryLease lease, CancellationToken ct)
    {
        var repo = context.GitWorkingDirectory;
        var common = await git.CommonDirectoryAsync(repo, ct);
        TaskWorkspaceContextResolver.RequireRepositoryIdentity(context, common);
        if (!leases.Owns(lease, common)
            || !WorkspaceReservationKey.PathsEqual(await git.CanonicalDirectoryAsync(repo, ct), repo))
            throw Refuse("ref_only_repository_lease_required");
        if (await progress.EndpointFingerprintAsync(repo, ct) != endpoint) throw Refuse("ref_only_endpoint_changed");
        if ((await git.LiveRegistrationsAsync(repo, ct)).Any(r => r.Branch == context.FullRef))
            throw Refuse("ref_only_source_checkout_attached");
        if ((await git.RunAsync(repo, ["symbolic-ref", "-q", context.FullRef], ct)).ExitCode != 1)
            throw Refuse("ref_only_source_ref_not_direct");
    }

    private async Task<TaskWorkspaceContext> TransitionAsync(AgentTask task, TaskWorkspaceContext context,
        TaskRefWorkspacePreparationState next, ProgressRemoteBaseline? remote, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {task.Id} FOR UPDATE", ct);
        await RequireCurrentAsync(task, context, ct);
        var changed = await db.TaskRefWorkspaceBindings.Where(b => b.Id == context.BindingId
                && b.TaskId == task.Id && b.Revision == context.BindingRevision && b.PreparationState == context.PreparationState)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.PreparationState, next)
                .SetProperty(b => b.Revision, b => b.Revision + 1), ct);
        if (changed != 1) throw Refuse("ref_only_binding_state_changed");
        if (remote is not null)
        {
            var baseline = TaskProgressJson.TryReadBaseline(task.ProgressBaselineJson)!;
            if (db.Entry(task).State == EntityState.Detached) db.Attach(task);
            task.ProgressBaselineJson = TaskProgressJson.SerializeBaseline(baseline with { Primary = baseline.Primary with { Remote = remote } });
            db.Entry(task).Property(t => t.ProgressBaselineJson).IsModified = true;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return context with { PreparationState = next, BindingRevision = context.BindingRevision + 1 };
    }

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
