using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The desktop strategy for an already-recorded RefOnly task. Owns no directory or registration.
/// Publication, ancestry, reservations and lease scheduling belong to the common sync engine.
/// </summary>
public sealed class RefOnlyDesktopWorkspaceService(
    TaskWorkspaceContextResolver contexts, ILandingGit git, ITaskProgressGit progress,
    IRepositoryMutationLease leases)
{
    public sealed record Observation(TaskWorkspaceContext Context, string Sha);

    internal Task<bool> MatchesParkAsync(AgentTask task, AgentTaskPark park, bool resumed, CancellationToken ct) =>
        contexts.MatchesParkAsync(task, park, resumed, ct);

    public async Task<Observation> InspectAsync(AgentTask task, ProgressSourceBaseline baseline,
        TaskWorkspaceContext? expected, CancellationToken ct)
    {
        var context = await contexts.ResolveCurrentAsync(task, ct);
        if (context.Kind != DesktopWorkspaceKind.RefOnly
            || baseline.DesktopWorkspaceKind != context.Kind
            || baseline.TaskRefWorkspaceBindingId != context.BindingId
            || baseline.RegisteredCheckout is not null
            || baseline.OwnerTaskId != task.Id
            || baseline.FullRef != context.FullRef || baseline.LocalSha != context.BaseSha
            || !WorkspaceReservationKey.PathsEqual(baseline.CanonicalRepository, context.GitWorkingDirectory)
            || !WorkspaceReservationKey.PathsEqual(baseline.CanonicalCommonDirectory, context.CommonDirectory!))
            throw Refuse("ref_only_baseline_identity_mismatch");
        if (context.PreparationState != TaskRefWorkspacePreparationState.Published)
            throw Refuse("ref_only_preparation_incomplete");
        if (expected is not null && context != expected)
            throw Refuse("ref_only_binding_state_changed");

        var repo = context.GitWorkingDirectory;
        var canonical = await git.CanonicalDirectoryAsync(repo, ct);
        if (!WorkspaceReservationKey.PathsEqual(canonical, repo))
            throw Refuse(RemoteSettlementSyncReasons.IdentityMismatch);
        TaskWorkspaceContextResolver.RequireRepositoryIdentity(context, await git.CommonDirectoryAsync(repo, ct));
        var endpoint = await progress.EndpointFingerprintAsync(repo, ct);
        if (endpoint is null) throw Refuse(RemoteSettlementSyncReasons.EndpointAmbiguous);
        if (!string.Equals(endpoint, baseline.Remote.EndpointFingerprint, StringComparison.OrdinalIgnoreCase))
            throw Refuse(RemoteSettlementSyncReasons.EndpointChanged);
        if ((await git.RegistrationsAsync(repo, ct)).Any(r => r.Branch == context.FullRef))
            throw Refuse("ref_only_source_checkout_attached");
        // Never dereference a malicious symbolic source ref into main or another task's ref.
        var symbolic = await git.RunAsync(repo, ["symbolic-ref", "-q", context.FullRef], ct);
        if (symbolic.ExitCode != 1) throw Refuse("ref_only_source_ref_not_direct");
        var tip = await progress.RevParseCommitAsync(repo, context.FullRef, ct);
        if (!tip.Succeeded || !GitObjectId.IsFull(tip.Sha))
            throw Refuse(RemoteSettlementSyncReasons.InspectionUnavailable);
        return new(context, tip.Sha!);
    }

    public async Task<bool> AdvanceAsync(TaskWorkspaceContext context, RepositoryLease lease,
        string expectedOld, string sourceSha, CancellationToken ct)
    {
        if (context.Kind != DesktopWorkspaceKind.RefOnly || context.CommonDirectory is null
            || !leases.Owns(lease, context.CommonDirectory)
            || !GitObjectId.IsFull(expectedOld) || !GitObjectId.IsFull(sourceSha))
            throw Refuse("ref_only_repository_lease_required");
        var result = await git.RunAsync(context.GitWorkingDirectory,
            ["update-ref", "--no-deref", context.FullRef, sourceSha, expectedOld], ct);
        return result.Succeeded;
    }

    private static ConflictException Refuse(string reason) => new(reason, reason);
}
