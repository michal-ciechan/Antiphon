using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Durable checkpoints precede their dependent mutation. Publication is monotonic.
/// CARD-0688 (schema 3): the source is the task branch ref plus the reviewed SHA; the rebase and the
/// verification build run in the one detached land worktree; the push comes first and the canonical
/// checkout is fast-forwarded after publication. The task worktree is not touched until cleanup.
/// </summary>
public sealed class AgentTaskLandingProtocol(AppDbContext db, ILandingGit git,
    IRepositoryMutationLease leases, IWorktreeManager worktrees, ILandingVerifier verifier, TimeProvider clock,
    IWorktreeCleanupJournal? cleanupJournal = null, ILogger<AgentTaskLandingProtocol>? logger = null,
    IOptions<GitSettings>? gitSettings = null, ILandWorkspace? landWorkspace = null)
{
    private readonly AgentTaskLandingState _state = new();

    /// <summary>CARD-0688 D-11: wall seconds of this invocation's land-worktree reset(s), for the profile line.</summary>
    internal double? LastResetSeconds { get; private set; }

    /// <summary>The land worktree seam; built from the Git settings when DI did not register one.</summary>
    internal ILandWorkspace? LandWorkspace { get; } = landWorkspace
        ?? (gitSettings?.Value is { WorktreeBasePath.Length: > 0 } ? new LandWorkspace(git, gitSettings, clock) : null);

    public Task<LandingProtocolResult> RunAsync(AgentTask task, RepositoryLease lease, CancellationToken ct)
        => RunAsync(task, lease, null, ct);

    public async Task<LandingProtocolResult> RunAsync(AgentTask task, RepositoryLease lease,
        AgentTaskLandRequest? request, CancellationToken ct)
    {
        if (task.RepairSourceTaskId is Guid repairOwner)
            throw new Application.Exceptions.ConflictException(
                $"Repair tasks cannot be landed; commission Land on the original owner {DelegationReportFormatter.Short(repairOwner)}.",
                "repair_source_landing_owner_required");
        if (task.Role == AgentTaskRole.Mutation || task.SourceLandingOperationId is not null)
            throw new Application.Exceptions.ConflictException("Mutation snapshots cannot be landed.", "verification_publication_forbidden");
        AgentTaskLanding? op = task.ActiveLandingId is Guid id
            ? await db.AgentTaskLandings.SingleAsync(o => o.Id == id && o.TaskId == task.Id, ct) : null;
        AgentTaskLanding? previousToReplace = null;
        try
        {
            if (task.RepoPath is null || task.WorktreePath is null || task.WorktreeBranch is null)
                throw new LandingRefusal("source_coordinates_missing");
            var coordinates = new LandSourceCoordinates(task.Id, task.RepoPath, task.WorktreePath,
                FullRef(task.WorktreeBranch), FullRef(task.MergeTargetRef ?? "master"));
            var common = await git.CommonDirectoryAsync(coordinates.RepositoryPath, ct);
            Require(leases.Owns(lease, common), "repository_lease_required");
            if (op is not null)
            {
                await db.Entry(op).ReloadAsync(ct);
                Require(op.SchemaVersion is 1 or 2 or 3, "landing_schema_unsupported");
                Require(op.Active, "landing_operation_inactive");
                if (op.ChildOperation is not null)
                {
                    Require(op.ChildProcessId is not null && op.ChildProcessStartTicks is not null,
                        "interrupted_process_requires_inspection");
                    var alive = await git.IsProcessAliveAsync(op.ChildProcessId!.Value, op.ChildProcessStartTicks!.Value, ct);
                    Require(alive == false, "interrupted_process_requires_inspection");
                    op.ChildOperation = null;
                    op.ChildProcessId = null;
                    op.ChildProcessStartTicks = null;
                    await SaveAsync(op, ct);
                }
                var published = _state.HasPublication(op);
                if (op.SchemaVersion != 3 && !published
                    && op.Phase is not (LandPhase.LocalTargetAdvanced or LandPhase.PushStarted or LandPhase.Refused))
                    // CARD-0688 D-7: the old protocol's unpublished work resumes only where both protocols coincide.
                    throw new LandingRefusal("landing_schema_superseded");
                if (op.SchemaVersion == 3 && op.Phase == LandPhase.RebaseStarted)
                    // D-3: the land worktree is disposable; the next request's reset is the inspection.
                    throw new LandingRefusal("interrupted_rebase");
                if (op.Phase >= LandPhase.TargetAdvanceStarted && op.Phase != LandPhase.Refused)
                {
                    RequireCoordinates(op, coordinates, common);
                    Require(await git.DestinationAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct)
                        == Destination(op), "remote_configuration_changed");
                }
                else if (op.Phase == LandPhase.Verified && !published
                    && request is not null && request.Id != op.ApprovalLandRequestId
                    && await PreparationChangedAsync(op, coordinates, request.VerifyFilter ?? task.LandVerifyFilter, ct))
                {
                    previousToReplace = op;
                    op = null;
                }
                else if (op.Phase == LandPhase.Refused)
                {
                    var explicitRequest = request is not null
                        && GitObjectId.IsFull(request.ExpectedSourceSha)
                        && (op.ApprovalLandRequestId is null || request.Id != op.ApprovalLandRequestId);
                    Require(_state.CanReplaceRefused(op, explicitRequest, true),
                        op.LastReason ?? "refused_operation_requires_new_evidence");
                    previousToReplace = op;
                    op = null;
                }
                else
                {
                    RequireCoordinates(op, coordinates, common);
                    Require(await git.DestinationAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct)
                        == Destination(op), "remote_configuration_changed");
                }
            }

            if (request is { CleanupOnly: true })
            {
                Require(request.RequiredLandingOperationId is Guid required && op is not null && op.Id == required
                    && _state.HasPublication(op), "cleanup_only_operation_mismatch");
            }

            if (op is not null && _state.HasPublication(op))
            {
                op.Mode = LandOperationMode.CleanupRetry;
                op.CleanupStartedAt = Now();
                op.CleanupCompletedAt = null;
                await SaveAsync(op, ct);
                return await CleanupAsync(op, lease, request, ct);
            }

            if (op is null)
            {
                // The resolver creates operations; this path replaces one for an explicit, already-resolved request.
                Require(request is not null && GitObjectId.IsFull(request.ExpectedSourceSha),
                    "legacy_review_binding_required");
                var approval = request!;
                if (approval.RecoveryMode == LandRecoveryMode.None)
                    await RecheckFinalVerificationAsync(task.Id, approval.ReviewEvidenceId, approval.ExpectedSourceSha, ct);
                else
                {
                    var recoverySource = await db.AgentTasks.AsNoTracking()
                        .SingleOrDefaultAsync(t => t.Id == approval.RecoverySourceTaskId, ct);
                    Require(recoverySource is not null && approval.ReviewEvidenceId is not null,
                        "recovery_authority_changed");
                    try
                    {
                        await LandApproval.LoadRecoveryEvidenceAsync(db, approval.ReviewEvidenceId!.Value,
                            approval.ExpectedSourceSha!, recoverySource!, ct);
                    }
                    catch (Antiphon.Server.Application.Exceptions.ConflictException ex)
                    {
                        throw new LandingRefusal(ex.Code ?? "recovery_review_invalid");
                    }
                }
                Require(approval.SourceResolutionState == LandSourceResolutionState.Resolved
                    && approval.ResolvedSourceSha == approval.ExpectedSourceSha
                    && GitObjectId.IsFull(approval.RemoteSourceSha) && approval.RemoteSourceFingerprint is { Length: 64 }
                    && GitObjectId.IsFull(approval.LocalBeforeSha), "source_resolution_required");
                var created = await LandOperationFactory.CreateAsync(git, RequireWorkspace(), Now(), task, approval,
                    coordinates, common, previousToReplace, ct);
                Require(created.Operation is not null, created.Reason ?? "landing_io_error");
                op = created.Operation!;
                await PersistNewOperationAsync(task, op, previousToReplace, ct);
            }
            else
            {
                if (op.SourcePinned || op.Phase != LandPhase.Inspected)
                    op.Mode = LandOperationMode.ResumePublication;
                await RecheckApprovalAsync(op, request, ct);
                await RecheckRemoteSourceAsync(op, ct);
                await RecheckSourceAsync(op, ct);
                await SaveAsync(op, ct);
            }

            if (op.Phase == LandPhase.Inspected)
            {
                await RecheckApprovalAsync(op, request, ct);
                await RecheckSourceAsync(op, ct);
                await PinAsync(op, "source", op.OriginalSourceSha, ct);
                op.SourcePinned = true;
                await SaveAsync(op, ct);
                await PinAsync(op, "target-before", op.TargetBeforeSha, ct);
                op.TargetPinned = true;
                await TransitionAsync(op, LandPhase.RecoveryPinned, ct);
            }
            if (op.Phase == LandPhase.RecoveryPinned)
            {
                await RecheckApprovalAsync(op, request, ct);
                await RecheckSourceAsync(op, ct);
                if (await IsAncestorAsync(op.RepositoryPath, InputSha(op), op.TargetBeforeSha, ct))
                {
                    // The observed remote target already carries the reviewed commit.
                    await RecheckRemoteSourceAsync(op, ct);
                    var remote = await ObserveAsync(op, InputSha(op), ct);
                    if (!remote.ContainsSource)
                        throw new LandingRefusal("remote_changed_before_push", remote.Sha);
                    await RecheckSourceAsync(op, ct);
                    op.VerifiedSourceSha = InputSha(op);
                    op.VerifiedAt = Now();
                    op.VerificationSkipReason = "exact_remote_containment";
                    await TransitionAsync(op, LandPhase.Verified, ct);
                    await ConfirmAsync(op, remote, LandPublicationOutcome.AlreadyPresent, ct);
                    return await CleanupAsync(op, lease, request, ct);
                }
                await RecheckRemoteSourceAsync(op, ct);
                await RecheckSourceAsync(op, ct);
                var land = await PrepareLandWorktreeAsync(op, InputSha(op), ct);
                op.RebaseStartedAt = Now();
                await TransitionAsync(op, LandPhase.RebaseStarted, ct);
                await RecheckApprovalAsync(op, request, ct);
                await RecheckSourceAsync(op, ct);
                var rebase = await MutateAsync(op, land,
                    ["-c", "rebase.autoStash=false", "-c", "rebase.updateRefs=false", "rebase", op.TargetBeforeSha], ct);
                if (!rebase.Succeeded)
                {
                    // Only this live invocation may abort the rebase it started.
                    var conflicts = await git.RunAsync(land, ["diff", "--name-only", "--diff-filter=U", "-z"], ct);
                    var abort = await MutateAsync(op, land, ["rebase", "--abort"], ct);
                    if (await TryIndexLockRefusalAsync(land, ct) is { } rebaseLock)
                        throw new LandingRefusal(rebaseLock.Code, rebaseLock.Detail);
                    Require(abort.Succeeded, "rebase_abort_failed");
                    await RecheckSourceAsync(op, ct);
                    var files = conflicts.Succeeded ? conflicts.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];
                    op.LastReason = files.Length > 0 ? "rebase_conflict" : "rebase_failed";
                    _state.Transition(op, LandPhase.Refused, Now());
                    return new(op, op.LastReason, files);
                }
                Require(rebase.RebaseHeadSha is not null, "interrupted_rebase");
                await RecheckSourceAsync(op, ct);
                Require(await CommitAsync(land, "HEAD", ct) == rebase.RebaseHeadSha, "land_worktree_changed");
                op.RebasedSourceSha = rebase.RebaseHeadSha!;
                await PinAsync(op, "prepared", op.RebasedSourceSha, ct);
                op.PreparedPinned = true;
                op.PreparedAt = Now();
                await TransitionAsync(op, LandPhase.Prepared, ct);
            }
            if (op.Phase == LandPhase.Prepared)
            {
                await RecheckApprovalAsync(op, request, ct);
                await RecheckSourceAsync(op, ct);
                op.VerificationCommand = "dotnet build; dotnet run --project tests/Antiphon.Tests";
                op.VerificationStartedAt = Now();
                await SaveAsync(op, ct);
                if (op.RebasedSourceSha == op.OriginalSourceSha && string.IsNullOrWhiteSpace(op.VerificationFilter)
                    && (op.PreparationInputSha is null || op.PreparationInputSha == op.OriginalSourceSha))
                {
                    op.VerificationSkipReason = "base_unchanged";
                    try { logger?.LogInformation("Landing verifier skipped task {TaskId} operation {OperationId} request {RequestId}; no child",
                        op.TaskId, op.Id, request?.Id); } catch (Exception) { }
                }
                else
                {
                    // Another repository land may have reset the shared land worktree since this op prepared.
                    var land = await VerifiedLandWorktreeAsync(op, op.RebasedSourceSha!, ct);
                    var verification = await verifier.VerifyAsync(land, op.VerificationFilter,
                        new(op.TaskId, op.Id, request?.Id), ct);
                    Require(verification.Passed, "verification_failed");
                    op.VerificationPassed = true;
                }
                await RecheckSourceAsync(op, ct);
                op.VerifiedSourceSha = op.RebasedSourceSha;
                op.VerifiedAt = Now();
                await TransitionAsync(op, LandPhase.Verified, ct);
            }
            if (op.Phase is LandPhase.Verified or LandPhase.LocalTargetAdvanced or LandPhase.PushStarted)
            {
                await RecheckApprovalAsync(op, request, ct);
                await RecheckRemoteSourceAsync(op, ct);
                await RecheckSourceAsync(op, ct);
                var beforePush = await ObserveAsync(op, op.VerifiedSourceSha!, ct);
                if (!beforePush.ContainsSource)
                {
                    // The pre-push observation is the CAS: a plain push of a descendant of the observed tip.
                    var baseline = op.RemoteBeforeSha ?? op.TargetBeforeSha;
                    if (beforePush.Sha != baseline)
                        throw new LandingRefusal("remote_changed_before_push", beforePush.Sha);
                    await RecheckSourceAsync(op, ct);
                    if (op.Phase != LandPhase.PushStarted)
                    {
                        op.PushStartedAt = Now();
                        await TransitionAsync(op, LandPhase.PushStarted, ct);
                    }
                    await RecheckSourceAsync(op, ct);
                    var pushed = await OwnedAsync(op, "push", started =>
                        git.PushOwnedAsync(op.RepositoryPath, Destination(op), op.VerifiedSourceSha!, started, ct), ct);
                    op.PushExitCode = pushed.ExitCode;
                    await SaveAsync(op, ct);
                    beforePush = await ObserveAsync(op, op.VerifiedSourceSha!, ct);
                    if (!beforePush.ContainsSource)
                    {
                        // A rejected push is a target race only when this re-observation shows the tip moved.
                        // A successful push that the remote does not contain stays push_unconfirmed.
                        if (!pushed.Succeeded && beforePush.Sha != baseline)
                            throw new LandingRefusal("remote_changed_before_push", beforePush.Sha);
                        Require(false, pushed.Succeeded ? "push_unconfirmed" : "push_rejected");
                    }
                }
                await ConfirmAsync(op, beforePush,
                    op.PushExitCode == 0 ? LandPublicationOutcome.Landed : LandPublicationOutcome.AlreadyPresent, ct);
            }
            Require(_state.HasPublication(op), "publication_unconfirmed");
            return await CleanupAsync(op, lease, request, ct);
        }
        catch (Exception ex) when (ex is LandingRefusal or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Durable publication is FailRequestAsync's job: swallowing here would settle as
            // a normal land with LastReason=landing_io_error and skip the interrupted-after-publication path.
            if (op is not null && _state.HasPublication(op)) throw;
            var reason = ex is LandingRefusal ? ex.Message : "landing_io_error";
            var detail = ex is LandingRefusal refusal ? refusal.Detail : null;
            if (op is not null)
            {
                op.LastReason = reason;
                // RebaseStarted is not resumable: a lock refusal after abort (or a rebase that
                // never started) must become Refused, or the next POST hits interrupted_rebase.
                // RecoveryPinned / Inspected / Prepared keep lock codes excluded so V-8 stays pinned.
                // CARD-0688: superseded schema-2 work, and a schema-3 remote that moved before the push
                // (nothing published; the next request rebases onto the new tip), are terminal too.
                var lockCode = reason is GitIndexLock.StaleCode or GitIndexLock.HeldCode;
                if (op.Phase != LandPhase.Refused && (op.Phase == LandPhase.RebaseStarted
                    || (op.Phase is LandPhase.Inspected or LandPhase.RecoveryPinned or LandPhase.Prepared && !lockCode)
                    || reason == "landing_schema_superseded"
                    || op.SchemaVersion == 3 && reason == "remote_changed_before_push"
                        && op.Phase is LandPhase.Verified or LandPhase.PushStarted
                    || op.SchemaVersion != 3
                        && (reason is "remote_changed_before_push" or "source_changed")
                        && op.Phase is LandPhase.LocalTargetAdvanced or LandPhase.PushStarted))
                {
                    op.Publication = LandPublicationOutcome.Refused;
                    _state.Transition(op, LandPhase.Refused, Now());
                }
                // The caller commits terminal evidence, event, stages and pending state together.
                op.UpdatedAt = Now();
                op.ConcurrencyToken = Guid.NewGuid();
            }
            return new(op, reason, []) { Detail = detail };
        }
    }

    internal Task<WorktreeCleanupEvidence> ReadCleanupEvidenceAsync(Guid operationId, Guid requestId, CancellationToken ct) =>
        cleanupJournal?.ReadEvidenceAsync(operationId, requestId, ct) ?? Task.FromResult(new WorktreeCleanupEvidence(null, null));

    private async Task<LandingProtocolResult> CleanupAsync(AgentTaskLanding op, RepositoryLease lease,
        AgentTaskLandRequest? request, CancellationToken ct)
    {
        if (op.Phase == LandPhase.PublicationConfirmed)
        {
            await AdvanceCanonicalAsync(op, ct);
            op.CleanupStartedAt = Now();
            op.ExpectedDeletionSha = AgentTaskLandingState.ExpectedDeletion(op);
            op.Cleanup = LandCleanupStatus.Pending;
            await TransitionAsync(op, LandPhase.CleanupStarted, ct);
        }
        WorktreeRemoval removed;
        try
        {
            if (request is null || cleanupJournal is null)
                removed = new(false, false, false, "cleanup_request_journal_required");
            else
            {
                // D-6: the branch and worktree identity is ExpectedDeletionSha; containment is proven for the landed SHA.
                var attempt = await cleanupJournal.GetOrCreateAsync(new(request.Id, op.Id, op.TaskId,
                    op.RepositoryPath, op.WorktreePath, op.CommonDirectory, op.GitDirectory,
                    op.SourceFullRef, op.TargetFullRef, op.ExpectedDeletionSha!, op.TargetBeforeSha), ct);
                var context = new WorktreeCleanupContext(attempt.Id, request.Id, op.Id, op.TaskId);
                removed = await worktrees.TryRemoveAsync(new(WorktreeRemovalPurpose.Publication, Coordinates(op),
                    op.CommonDirectory, op.GitDirectory, op.ExpectedDeletionSha!, op.TargetBeforeSha, op.Id, lease,
                    CleanupContext: context, LandedSha: op.VerifiedSourceSha), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { removed = new(false, false, false, "cleanup_evidence_storage_unavailable"); }
        op.DirectoryRemoved = removed.DirectoryGone;
        op.RegistrationRemoved = removed.Unregistered;
        op.BranchRemoved = removed.BranchDeleted;
        op.LastReason = removed.Residue is null ? null : WorktreeCleanupPresentation.Clip(removed.Residue, 400);
        op.Cleanup = removed.IsClean ? LandCleanupStatus.Complete : LandCleanupStatus.Refused;
        op.CleanupCompletedAt = Now();
        // The terminal cleanup evidence is committed with the outcome event and pending-task
        // changes by AgentTaskLandService. Until then the saved cleanup intent supports retry.
        if (removed.IsClean && op.Phase != LandPhase.Complete) _state.Transition(op, LandPhase.Complete, Now());
        // CARD-0665 D-8: LastReason keeps the bare code; the protected paths or retained count ride beside it.
        return new(op, removed.Residue, []) { Detail = removed.Detail };
    }

    /// <summary>
    /// CARD-0688 D-4/D-5: after publication, fast-forward the canonical checkout as one known path. Every
    /// failure is a recorded residue reason, never a refusal: publication is already confirmed and final.
    /// </summary>
    private async Task AdvanceCanonicalAsync(AgentTaskLanding op, CancellationToken ct)
    {
        if (op.CanonicalAdvancedAt is not null || op.CanonicalAdvanceReason is not null) return;
        op.CanonicalAdvanceStartedAt ??= Now();
        await SaveAsync(op, ct);
        string? reason;
        try { reason = await CanonicalAdvanceAsync(op, ct); }
        catch (Exception ex) when (ex is LandingRefusal or IOException or UnauthorizedAccessException
            or ArgumentException or TimeoutException)
        { reason = ex is LandingRefusal refusal && refusal.Message.StartsWith("canonical_", StringComparison.Ordinal)
            ? refusal.Message : "canonical_advance_failed"; }
        if (reason is null) op.CanonicalAdvancedAt = Now();
        else
        {
            op.CanonicalAdvanceReason = reason;
            try { logger?.LogWarning("Land {OperationId} published but did not advance the canonical checkout: {Reason} ({Checkout})",
                op.Id, reason, op.TargetCheckoutPath ?? op.RepositoryPath); } catch (Exception) { }
        }
        await SaveAsync(op, ct);
    }

    private async Task<string?> CanonicalAdvanceAsync(AgentTaskLanding op, CancellationToken ct)
    {
        var (reason, checkout) = await CanonicalDecisionAsync(op, ct);
        op.TargetCheckoutRecorded = true;
        op.TargetCheckoutPath = checkout;
        if (reason is not null) return reason;
        var verified = op.VerifiedSourceSha!;
        var current = checkout is null
            ? await CommitAsync(op.RepositoryPath, op.TargetFullRef, ct)
            : await CommitAsync(checkout, "HEAD", ct);
        if (current == verified)
        {
            // Already advanced is also a proven local identity (including schema-2 recovery).
            op.LocalTargetAfterSha = current;
            return null;
        }
        if (!await IsAncestorAsync(op.RepositoryPath, current, verified, ct)) return "canonical_diverged";
        // CARD-0642 R1 carried over: the HEAD-file read is the last check before the mutation, because another
        // process can switch a worktree onto the target unseen while the ancestry check runs.
        var (again, currentCheckout) = ScanTarget(op);
        if (again is not null) return again;
        if (!GitIndexLock.PathsEqual(checkout, currentCheckout))
            return "canonical_checkout_changed";
        LandingGitResult advanced;
        if (checkout is null)
            advanced = await MutateAsync(op, op.RepositoryPath,
                ["update-ref", "--no-deref", op.TargetFullRef, verified, current], ct);
        else
        {
            advanced = await MutateAsync(op, checkout, ["-c", "merge.autoStash=false", "merge", "--ff-only", verified], ct);
            if (!advanced.Succeeded && await TryIndexLockRefusalAsync(checkout, ct) is not null) return "canonical_index_lock";
        }
        if (!advanced.Succeeded) return "canonical_advance_failed";
        var after = checkout is null
            ? await CommitAsync(op.RepositoryPath, op.TargetFullRef, ct)
            : await CommitAsync(checkout, "HEAD", ct);
        if (after != verified) return "canonical_advance_failed";
        op.LocalTargetAfterSha = verified;
        return null;
    }

    /// <summary>
    /// D-5: HEAD files (no git) prove the target is checked out nowhere but the main checkout, then the main
    /// checkout's lock, sequencer and dirtiness are read once. A null checkout means the target is checked out
    /// nowhere and moves by <c>update-ref</c>. Separate from the mutation so each decision is testable alone.
    /// </summary>
    private async Task<(string? Reason, string? Checkout)> CanonicalDecisionAsync(AgentTaskLanding op, CancellationToken ct)
    {
        var (reason, checkout) = ScanTarget(op);
        if (reason is not null || checkout is null) return (reason, checkout);
        if (await TryIndexLockRefusalAsync(checkout, ct) is not null) return ("canonical_index_lock", checkout);
        if (await git.HasActiveSequencerAsync(checkout, ct)) return ("canonical_active_sequencer", checkout);
        var status = await git.RunAsync(checkout,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], ct);
        if (!status.Succeeded || status.Output.Length != 0) return ("canonical_checkout_dirty", checkout);
        return (null, checkout);
    }

    private IReadOnlyList<LandingHeadFile> HeadsOf(AgentTaskLanding op) =>
        LandWorkspace?.ScanHeadFiles(op.CommonDirectory) ?? Infrastructure.Git.LandWorkspace.Scan(op.CommonDirectory);

    /// <summary>The file-only half of the decision: the target is checked out in the main checkout (its path),
    /// nowhere (null) or elsewhere / unknowably (a residue reason). An unreadable main HEAD is never "nowhere".</summary>
    private (string? Reason, string? Checkout) ScanTarget(AgentTaskLanding op)
    {
        var heads = HeadsOf(op);
        var elsewhere = heads.FirstOrDefault(h => !h.IsMain && h.SymbolicRef == op.TargetFullRef);
        if (elsewhere is not null) return ("target_checked_out_elsewhere", elsewhere.WorktreePath ?? elsewhere.AdminDirectory);
        var unknown = heads.FirstOrDefault(h => h.SymbolicRef is null && h.Sha is null);
        if (unknown is not null) return ("canonical_checkout_unknown", unknown.WorktreePath ?? unknown.AdminDirectory);
        var main = heads.First(h => h.IsMain);
        if (main.SymbolicRef != op.TargetFullRef) return (null, null);
        return main.WorktreePath is null ? ("canonical_checkout_unknown", null) : (null, main.WorktreePath);
    }

    private ILandWorkspace RequireWorkspace() => LandWorkspace ?? throw new LandingRefusal("land_worktree_root_unavailable");

    /// <summary>D-3/D-10: reset the land worktree to <paramref name="sha"/> (owned children), then refuse a live index lock.</summary>
    private async Task<string> PrepareLandWorktreeAsync(AgentTaskLanding op, string sha, CancellationToken ct)
    {
        Require(!string.IsNullOrWhiteSpace(op.LandWorktreePath), "land_worktree_root_unavailable");
        var started = clock.GetTimestamp();
        var ready = await RequireWorkspace().EnsureAsync(op.RepositoryPath, op.LandWorktreePath!, sha,
            (directory, arguments, token) => MutateAsync(op, directory, arguments, token), ct);
        LastResetSeconds = (LastResetSeconds ?? 0) + clock.GetElapsedTime(started).TotalSeconds;
        if (ready.Reason is not null) throw new LandingRefusal(ready.Reason, ready.Detail);
        if (op.Phase == LandPhase.RecoveryPinned) op.LandWorkspaceReadyAt = Now();
        await SaveAsync(op, ct);
        await RequireNoIndexLockAsync(op.LandWorktreePath!, ct);
        return op.LandWorktreePath!;
    }

    /// <summary>The land worktree at <paramref name="sha"/> with a clean tracked tree; re-prepared when another land moved it.</summary>
    private async Task<string> VerifiedLandWorktreeAsync(AgentTaskLanding op, string sha, CancellationToken ct)
    {
        var land = op.LandWorktreePath!;
        if (Directory.Exists(land))
        {
            var head = await git.RunAsync(land, ["rev-parse", "--verify", "HEAD^{commit}"], ct);
            var status = await git.RunAsync(land, ["status", "--porcelain=v1", "-z", "--untracked-files=no"], ct);
            if (head.Succeeded && head.Output.Trim() == sha && status.Succeeded && status.Output.Length == 0) return land;
        }
        return await PrepareLandWorktreeAsync(op, sha, ct);
    }

    private async Task ConfirmAsync(AgentTaskLanding op, LandingRemoteObservation remote,
        LandPublicationOutcome publication, CancellationToken ct)
    {
        Require(remote.Reason is null && remote.ContainsSource && remote.Sha is not null, "publication_unconfirmed");
        op.Publication = publication;
        op.ObservedRemoteTargetSha = remote.Sha;
        op.RemoteConfirmedAt = Now();
        op.ConfirmationMethod = "push-endpoint-read-fetch-ancestry";
        await TransitionAsync(op, LandPhase.PublicationConfirmed, ct);
    }

    private async Task PersistNewOperationAsync(AgentTask task, AgentTaskLanding operation,
        AgentTaskLanding? previous, CancellationToken ct)
    {
        // The unique active-operation constraint needs the old row updated first. Both saves
        // and the task pointer change commit together; failed preparation leaves the old row active.
        await using var transaction = previous is null ? null : await db.Database.BeginTransactionAsync(ct);
        if (previous is not null)
        {
            previous.Active = false;
            previous.ConcurrencyToken = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }
        db.AgentTaskLandings.Add(operation);
        task.ActiveLandingId = operation.Id;
        task.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    private static string InputSha(AgentTaskLanding op) =>
        op.PreparationInputSha ?? op.OriginalSourceSha;

    /// <summary>The branch tip the source must still have: schema 3 never moves it; schema 1/2 rebased it.</summary>
    private static string ExpectedBranch(AgentTaskLanding op) => op.SchemaVersion == 3
        ? op.SourceLocalSha!
        : op.VerifiedSourceSha ?? op.RebasedSourceSha ?? InputSha(op);

    /// <summary>
    /// CARD-0544 D-5. Every unpublished checkpoint re-reads the owner's final-review latch and its
    /// persisted approval; a removed, superseded or scope-invalidated approval, or an owner newly
    /// latched without one, refuses before the next target/push mutation.
    /// </summary>
    private async Task RecheckFinalVerificationAsync(Guid ownerId, Guid? evidenceId, string? expectedSha, CancellationToken ct)
    {
        var refusal = await LandApproval.RevalidateFinalVerificationAsync(db, ownerId, evidenceId, expectedSha, ct);
        Require(refusal is null, refusal ?? "");
    }

    /// <summary>CARD-0642 D-6 / CARD-0688 D-8: the DB half (latch, request identity, filter), at every boundary.</summary>
    private async Task RecheckApprovalAsync(AgentTaskLanding op, AgentTaskLandRequest? request, CancellationToken ct)
    {
        if (op.RecoveryMode == LandRecoveryMode.None)
            await RecheckFinalVerificationAsync(op.TaskId, request?.ReviewEvidenceId ?? op.ReviewEvidenceId,
                op.OriginalSourceSha, ct);
        else if (!_state.HasPublication(op))
        {
            Require(request is not null && request.RecoveryMode == op.RecoveryMode
                && request.RecoveryOwnerStatus == op.RecoveryOwnerStatus
                && request.RecoverySourceTaskId == op.RecoverySourceTaskId
                && request.RecoverySourceFullRef == op.RecoverySourceFullRef
                && request.ReviewEvidenceId == op.ReviewEvidenceId,
                "recovery_authority_changed");
            var source = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == op.RecoverySourceTaskId, ct);
            var owner = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == op.TaskId, ct);
            Require(source is not null && source.Workspace == WorkspaceMode.Worktree
                && source.Role == AgentTaskRole.Code && source.RepairSourceTaskId is null
                && source.SourceLandingOperationId is null && owner is not null
                && source.CardId == owner.CardId && source.ProjectId == owner.ProjectId,
                "recovery_source_invalid");
            try
            {
                await LandApproval.LoadRecoveryEvidenceAsync(db, op.ReviewEvidenceId!.Value,
                    op.OriginalSourceSha, source!, ct);
            }
            catch (Antiphon.Server.Application.Exceptions.ConflictException ex)
            {
                throw new LandingRefusal(ex.Code ?? "recovery_review_invalid");
            }
            Require(op.SourceRemoteFingerprint is { Length: 64 }
                && op.RecoverySourceFullRef is not null, "recovery_source_unbound");
            var sourceRemote = await git.RecheckSourceRemoteAsync(op.RepositoryPath,
                op.RecoverySourceFullRef!, op.OriginalSourceSha, op.SourceRemoteFingerprint!, ct);
            Require(sourceRemote.Accepted && sourceRemote.Sha == op.OriginalSourceSha,
                sourceRemote.Reason ?? "recovery_source_changed");
        }
        if (request is null) return;
        Require(request.TaskId == op.TaskId, "stale_land_request");
        if (op.ApprovalLandRequestId is Guid bound && bound != request.Id)
        {
            Require(request.ExpectedSourceSha == op.OriginalSourceSha, "land_request_identity_conflict");
            var boundRequest = await db.AgentTaskLandRequests.AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == bound && r.TaskId == op.TaskId, ct);
            Require(boundRequest is not null && boundRequest.RequestedAt <= request.RequestedAt,
                "land_request_identity_conflict");
        }
        if (request.ExpectedSourceSha is not null)
            Require(request.ExpectedSourceSha == op.OriginalSourceSha, "resume_approval_changed");
        if (request.ReviewEvidenceId is not null && op.ReviewEvidenceId is not null)
            Require(request.ReviewEvidenceId == op.ReviewEvidenceId, "resume_approval_changed");
        Require(request.VerifyFilter == op.VerificationFilter, "verification_filter_changed");
        if (!_state.HasPublication(op))
        {
            var ownerFilter = await db.AgentTasks.AsNoTracking()
                .Where(t => t.Id == op.TaskId).Select(t => t.LandVerifyFilter).SingleOrDefaultAsync(ct);
            Require(ownerFilter == op.VerificationFilter, "verification_filter_changed");
        }
    }

    /// <summary>The network half: one <c>ls-remote</c> (no fetch, no pin) before each mutation (D-8).</summary>
    private async Task RecheckRemoteSourceAsync(AgentTaskLanding op, CancellationToken ct)
    {
        if (_state.HasPublication(op))
            return;
        if (op.SchemaVersion is not (2 or 3) || op.SourceRemoteSha is null || op.SourceRemoteFingerprint is null
            || !GitObjectId.IsFull(op.ReviewedSourceSha) || op.ApprovalLandRequestId is null)
        {
            throw new LandingRefusal(op.SchemaVersion is not (2 or 3) || !GitObjectId.IsFull(op.ReviewedSourceSha)
                || op.ApprovalLandRequestId is null
                ? "legacy_review_binding_required"
                : "source_resolution_required");
        }
        var recheck = await git.RecheckSourceRemoteAsync(op.RepositoryPath, op.SourceFullRef, op.SourceRemoteSha,
            op.SourceRemoteFingerprint, ct);
        // A different endpoint is a different remote source, as the full observation always reported it.
        Require(recheck.Reason != "source_remote_endpoint_changed", "source_remote_changed");
        Require(recheck.Accepted, recheck.Reason ?? "source_remote_unreadable");
        Require(recheck.Sha == op.SourceRemoteSha && recheck.Fingerprint == op.SourceRemoteFingerprint,
            "source_remote_changed");
    }

    /// <summary>DB coordinates, recovery pins and one <c>show-ref</c> of the branch (I-1, I-2, I-8).</summary>
    private async Task RecheckSourceAsync(AgentTaskLanding op, CancellationToken ct)
    {
        var currentTask = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == op.TaskId, ct);
        Require(currentTask is not null && currentTask.ActiveLandingId == op.Id
            && (op.RecoveryMode == LandRecoveryMode.None
                ? currentTask.Status == AgentTaskStatus.Succeeded
                : currentTask.Role == AgentTaskRole.Code
                    && LandApproval.RecoveryStatusEligible(currentTask.Status))
            && currentTask.RepoPath is not null && SamePath(currentTask.RepoPath, op.RepositoryPath)
            && currentTask.WorktreePath is not null && SamePath(currentTask.WorktreePath, op.WorktreePath)
            && currentTask.WorktreeBranch is not null && FullRef(currentTask.WorktreeBranch) == op.SourceFullRef
            && FullRef(currentTask.MergeTargetRef ?? "master") == op.TargetFullRef, "task_coordinates_changed");
        var pins = new List<(string Name, string Sha)>();
        if (op.SourcePinned) pins.Add(("source", op.OriginalSourceSha));
        if (op.TargetPinned) pins.Add(("target-before", op.TargetBeforeSha));
        if (op.PreparedPinned) pins.Add(("prepared", op.RebasedSourceSha!));
        foreach (var pin in pins)
        {
            var read = await git.RunAsync(op.RepositoryPath,
                ["show-ref", "--verify", "--hash", op.RecoveryRefPrefix + "/" + pin.Name], ct);
            Require(read.Succeeded && read.Output.Trim() == pin.Sha, "recovery_pin_changed");
        }
        var branch = await LandOperationFactory.ReadBranchAsync(git, op.RepositoryPath, op.SourceFullRef, ct);
        Require(branch.Sha is not null && branch.Sha == ExpectedBranch(op), branch.Reason ?? "source_changed");
    }

    private async Task<bool> PreparationChangedAsync(AgentTaskLanding op, LandSourceCoordinates coordinates,
        string? verificationFilter, CancellationToken ct)
    {
        var branch = await LandOperationFactory.ReadBranchAsync(git, coordinates.RepositoryPath, coordinates.SourceFullRef, ct);
        Require(branch.Reason is null, branch.Reason ?? "source_unknown");
        if (branch.Sha != ExpectedBranch(op)
            || coordinates.SourceFullRef != op.SourceFullRef
            || coordinates.TargetFullRef != op.TargetFullRef
            || !SamePath(coordinates.RepositoryPath, op.RepositoryPath)
            || verificationFilter != op.VerificationFilter)
            return true;
        return await git.DestinationAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct) != Destination(op);
    }

    private static void RequireCoordinates(AgentTaskLanding op, LandSourceCoordinates coordinates, string common) =>
        Require(op.TaskId == coordinates.TaskId && op.SourceFullRef == coordinates.SourceFullRef
            && op.TargetFullRef == coordinates.TargetFullRef && SamePath(op.RepositoryPath, coordinates.RepositoryPath)
            && SamePath(op.WorktreePath, coordinates.WorktreePath) && SamePath(common, op.CommonDirectory),
            "pending_operation_coordinates_changed");

    private async Task<LandingRemoteObservation> ObserveAsync(AgentTaskLanding op, string sha, CancellationToken ct)
    {
        var observed = await git.ObserveAsync(op.RepositoryPath, Destination(op), sha, op.RecoveryRefPrefix + "/remote-observed", ct);
        Require(observed.Reason is null && observed.Sha is not null, observed.Reason ?? "remote_unknown");
        return observed;
    }

    private async Task PinAsync(AgentTaskLanding op, string name, string sha, CancellationToken ct) =>
        Require((await git.PinAsync(op.RepositoryPath, op.RecoveryRefPrefix + "/" + name, sha, ct)).Succeeded, "recovery_pin_failed");

    private async Task<LandingGitResult> MutateAsync(AgentTaskLanding op, string repository,
        IReadOnlyList<string> arguments, CancellationToken ct)
        => await OwnedAsync(op, string.Join(" ", arguments.Take(5)),
            started => git.RunOwnedAsync(repository, arguments, started, ct), ct);

    private async Task<LandingGitResult> OwnedAsync(AgentTaskLanding op, string description,
        Func<Func<int, long, CancellationToken, Task>, Task<LandingGitResult>> execute, CancellationToken ct)
    {
        op.ChildOperation = description;
        op.ChildProcessId = null;
        op.ChildProcessStartTicks = null;
        await SaveAsync(op, ct);
        var result = await execute(async (pid, ticks, token) =>
        {
            op.ChildProcessId = pid;
            op.ChildProcessStartTicks = ticks;
            await SaveAsync(op, token);
        });
        op.ChildOperation = null;
        op.ChildProcessId = null;
        op.ChildProcessStartTicks = null;
        await SaveAsync(op, ct);
        return result;
    }

    private async Task<string> CommitAsync(string repository, string revision, CancellationToken ct)
    {
        var result = await git.RunAsync(repository, ["rev-parse", "--verify", revision + "^{commit}"], ct);
        var sha = result.Output.Trim();
        Require(result.Succeeded && sha.Length is 40 or 64 && sha.All(char.IsAsciiHexDigit), "commit_lookup_failed");
        return sha;
    }

    private async Task<bool> IsAncestorAsync(string repository, string source, string target, CancellationToken ct)
    {
        var result = await git.RunAsync(repository, ["merge-base", "--is-ancestor", source, target], ct);
        Require(result.ExitCode is 0 or 1, "ancestry_error");
        return result.ExitCode == 0;
    }

    private async Task TransitionAsync(AgentTaskLanding op, LandPhase next, CancellationToken ct)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {op.TaskId} FOR UPDATE", ct);
        _state.Transition(op, next, Now());
        var request = await db.AgentTaskLandRequests.SingleOrDefaultAsync(r => r.TaskId == op.TaskId && r.IsPending, ct);
        if (request is not null) await db.Entry(request).ReloadAsync(ct);
        if (request is not null && next != LandPhase.Refused && (int)next > request.HighestProgress)
        {
            request.HighestProgress = (int)next;
            request.LastProgressAt = Now();
            request.LandingOperationId = op.Id;
            request.WarningAt = request.ErrorAt = null;
            request.ConcurrencyToken = Guid.NewGuid();
        }
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    private async Task SaveAsync(AgentTaskLanding op, CancellationToken ct)
    {
        op.UpdatedAt = Now();
        op.ConcurrencyToken = Guid.NewGuid();
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
    private async Task RequireNoIndexLockAsync(string checkout, CancellationToken ct)
    {
        if (await TryIndexLockRefusalAsync(checkout, ct) is { } hit)
            throw new LandingRefusal(hit.Code, hit.Detail);
    }

    private async Task<(string Code, string Detail)?> TryIndexLockRefusalAsync(string checkout, CancellationToken ct)
    {
        var observation = await git.InspectIndexLockAsync(checkout, ct);
        return GitIndexLock.Refusal(observation, GitIndexLock.StaleAfter(gitSettings?.Value.IndexLockStaleAfterSeconds),
            clock.GetUtcNow().UtcDateTime);
    }

    private static void Require(bool condition, string reason) { if (!condition) throw new LandingRefusal(reason); }
    private static string FullRef(string branch) => branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
    private static bool SamePath(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static LandSourceCoordinates Coordinates(AgentTaskLanding op) => new(op.TaskId, op.RepositoryPath,
        op.WorktreePath, op.SourceFullRef, op.TargetFullRef);
    private static LandingDestination Destination(AgentTaskLanding op) => new(op.RemoteName, op.DestinationFullRef, op.RemoteFingerprint);
    private sealed class LandingRefusal(string reason, string? detail = null) : Exception(reason)
    {
        public string? Detail { get; } = detail;
    }
}

public sealed record LandingProtocolResult(AgentTaskLanding? Operation, string? Reason, IReadOnlyList<string> Conflicts)
{
    public bool Published => Operation is not null && new AgentTaskLandingState().HasPublication(Operation);
    public string? Detail { get; init; }
}
