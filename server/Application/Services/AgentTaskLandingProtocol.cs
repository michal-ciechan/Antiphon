using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Durable checkpoints precede their dependent mutation. Publication is monotonic.</summary>
public sealed class AgentTaskLandingProtocol(AppDbContext db, ILandingGit git,
    IRepositoryMutationLease leases, IWorktreeManager worktrees, ILandingVerifier verifier, TimeProvider clock)
{
    private readonly AgentTaskLandingState _state = new();

    public async Task<LandingProtocolResult> RunAsync(AgentTask task, RepositoryLease lease, CancellationToken ct)
    {
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
                Require(op.SchemaVersion == 1, "landing_schema_unsupported");
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
                if (op.Phase == LandPhase.RebaseStarted)
                    throw new LandingRefusal("interrupted_rebase_requires_inspection");
                if (op.Phase == LandPhase.Verified && !_state.HasPublication(op)
                    && task.LandRequestedAt > op.UpdatedAt
                    && await PreparationChangedAsync(op, coordinates, task.LandVerifyFilter, ct))
                {
                    // No target-advance intent has been saved yet. An explicit request may
                    // prepare changed work afresh, retaining the old verification and pins.
                    previousToReplace = op;
                    op = null;
                }
                else if (op.Phase == LandPhase.Refused)
                {
                    var fresh = await git.InspectAsync(coordinates, ct);
                    Require(_state.CanReplaceRefused(op, fresh, task.LandRequestedAt > op.UpdatedAt, true),
                        op.LastReason ?? "refused_operation_requires_new_evidence");
                    previousToReplace = op;
                    op = null; // Keep the previous operation active until a complete replacement can commit.
                }
                else
                {
                    Require(op.TaskId == task.Id && op.SourceFullRef == coordinates.SourceFullRef
                        && op.TargetFullRef == coordinates.TargetFullRef && SamePath(op.RepositoryPath, coordinates.RepositoryPath)
                        && SamePath(op.WorktreePath, coordinates.WorktreePath) && SamePath(common, op.CommonDirectory),
                        "pending_operation_coordinates_changed");
                    Require(await git.DestinationAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct)
                        == Destination(op), "remote_configuration_changed");
                }
            }

            if (op is not null && _state.HasPublication(op))
            {
                op.Mode = LandOperationMode.CleanupRetry;
                op.CleanupStartedAt = Now();
                op.CleanupCompletedAt = null;
                await SaveAsync(op, ct);
                return await CleanupAsync(op, lease, ct);
            }

            var source = await git.InspectAsync(coordinates, ct);
            Require(source.Accepted, source.Reason ?? "source_unknown");
            var snapshot = source.Snapshot!;
            if (op is null)
            {
                var destination = await git.DestinationAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct);
                var target = await CommitAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct);
                op = new AgentTaskLanding
                {
                    Id = Guid.NewGuid(), TaskId = task.Id, Phase = LandPhase.Inspected,
                    CreatedAt = Now(), UpdatedAt = Now(), RepositoryPath = coordinates.RepositoryPath,
                    WorktreePath = snapshot.RegisteredPath, CommonDirectory = snapshot.CommonDirectory,
                    GitDirectory = snapshot.GitDirectory, SourceFullRef = coordinates.SourceFullRef,
                    OriginalSourceSha = snapshot.HeadSha, TargetFullRef = coordinates.TargetFullRef,
                    TargetBeforeSha = target, RemoteName = destination.RemoteName,
                    DestinationFullRef = destination.FullRef, RemoteFingerprint = destination.Fingerprint,
                    VerificationFilter = task.LandVerifyFilter,
                };
                op.RecoveryRefPrefix = $"refs/antiphon/land/{task.Id:N}/{op.Id:N}";
                op.TargetCheckoutPath = await TargetCheckoutAsync(op, ct);
                op.TargetCheckoutRecorded = true;
                await PersistNewOperationAsync(task, op, previousToReplace, ct);
            }
            else
            {
                op.Mode = LandOperationMode.ResumePublication;
                Require(Matches(snapshot, op, op.RebasedSourceSha ?? op.OriginalSourceSha), "source_changed");
                await SaveAsync(op, ct);
            }

            if (op.Phase == LandPhase.Inspected)
            {
                await RecheckSourceAsync(op, op.OriginalSourceSha, ct);
                Require(await CommitAsync(op.RepositoryPath, op.TargetFullRef, ct) == op.TargetBeforeSha, "target_changed");
                await PinAsync(op, "source", op.OriginalSourceSha, ct);
                op.SourcePinned = true;
                await SaveAsync(op, ct);
                await PinAsync(op, "target-before", op.TargetBeforeSha, ct);
                op.TargetPinned = true;
                await TransitionAsync(op, LandPhase.RecoveryPinned, ct);
            }
            if (op.Phase == LandPhase.RecoveryPinned)
            {
                await RecheckSourceAsync(op, op.OriginalSourceSha, ct);
                var remote = await ObserveAsync(op, op.OriginalSourceSha, ct);
                await RecheckSourceAsync(op, op.OriginalSourceSha, ct);
                op.RemoteBeforeSha = remote.Sha;
                await SaveAsync(op, ct);
                if (remote.ContainsSource)
                {
                    op.VerifiedSourceSha = op.OriginalSourceSha;
                    op.VerifiedAt = Now();
                    op.VerificationSkipReason = "exact_remote_containment";
                    await TransitionAsync(op, LandPhase.Verified, ct);
                    await ConfirmAsync(op, remote, LandPublicationOutcome.AlreadyPresent, ct);
                    return await CleanupAsync(op, lease, ct);
                }
                Require(await IsAncestorAsync(op.RepositoryPath, remote.Sha!, op.TargetBeforeSha, ct), "remote_ahead_or_diverged");
                await RecheckSourceAsync(op, op.OriginalSourceSha, ct);
                await CheckTargetAsync(op, op.TargetBeforeSha, ct);
                op.RebaseStartedAt = Now();
                await TransitionAsync(op, LandPhase.RebaseStarted, ct);
                await RecheckSourceAsync(op, op.OriginalSourceSha, ct);
                await CheckTargetAsync(op, op.TargetBeforeSha, ct);
                var rebase = await MutateAsync(op, op.WorktreePath,
                    ["-c", "rebase.autoStash=false", "-c", "rebase.updateRefs=false", "rebase", op.TargetBeforeSha], ct);
                if (!rebase.Succeeded)
                {
                    // Only this live invocation may abort the rebase it started.
                    var conflicts = await git.RunAsync(op.WorktreePath, ["diff", "--name-only", "--diff-filter=U", "-z"], ct);
                    var abort = await MutateAsync(op, op.WorktreePath, ["rebase", "--abort"], ct);
                    Require(abort.Succeeded, "rebase_abort_failed");
                    await RecheckSourceAsync(op, op.OriginalSourceSha, ct);
                    var files = conflicts.Succeeded ? conflicts.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];
                    op.LastReason = files.Length > 0 ? "rebase_conflict" : "rebase_failed";
                    _state.Transition(op, LandPhase.Refused, Now());
                    return new(op, op.LastReason, files);
                }
                Require(rebase.RebaseHeadSha is not null, "interrupted_rebase_requires_inspection");
                await RecheckSourceAsync(op, rebase.RebaseHeadSha!, ct);
                var prepared = await git.InspectAsync(Coordinates(op), ct);
                Require(prepared.Accepted && prepared.Snapshot!.GitDirectory == op.GitDirectory,
                    prepared.Reason ?? "source_changed");
                Require(prepared.Snapshot!.HeadSha == rebase.RebaseHeadSha, "source_changed");
                op.RebasedSourceSha = rebase.RebaseHeadSha!;
                await PinAsync(op, "prepared", op.RebasedSourceSha, ct);
                op.PreparedPinned = true;
                op.PreparedAt = Now();
                await TransitionAsync(op, LandPhase.Prepared, ct);
            }
            if (op.Phase == LandPhase.Prepared)
            {
                await RecheckSourceAsync(op, op.RebasedSourceSha!, ct);
                await CheckTargetAsync(op, op.TargetBeforeSha, ct);
                op.VerificationCommand = "dotnet build; dotnet run --project tests/Antiphon.Tests";
                op.VerificationStartedAt = Now();
                await SaveAsync(op, ct);
                if (op.RebasedSourceSha == op.OriginalSourceSha && string.IsNullOrWhiteSpace(op.VerificationFilter))
                    op.VerificationSkipReason = "base_unchanged";
                else
                {
                    var verification = await verifier.VerifyAsync(op.WorktreePath, op.VerificationFilter, ct);
                    Require(verification.Passed, "verification_failed");
                    op.VerificationPassed = true;
                }
                await RecheckSourceAsync(op, op.RebasedSourceSha!, ct);
                await CheckTargetAsync(op, op.TargetBeforeSha, ct);
                op.VerifiedSourceSha = op.RebasedSourceSha;
                op.VerifiedAt = Now();
                await TransitionAsync(op, LandPhase.Verified, ct);
            }
            if (op.Phase == LandPhase.Verified)
            {
                await RecheckSourceAsync(op, op.VerifiedSourceSha!, ct);
                await CheckTargetAsync(op, op.TargetBeforeSha, ct);
                await TransitionAsync(op, LandPhase.TargetAdvanceStarted, ct);
            }
            if (op.Phase == LandPhase.TargetAdvanceStarted)
            {
                await RecheckSourceAsync(op, op.VerifiedSourceSha!, ct);
                var current = await CommitAsync(op.RepositoryPath, op.TargetFullRef, ct);
                Require(current == op.TargetBeforeSha || current == op.VerifiedSourceSha, "target_changed");
                await CheckTargetAsync(op, current, ct);
                if (current != op.VerifiedSourceSha)
                {
                    Require(await IsAncestorAsync(op.RepositoryPath, current, op.VerifiedSourceSha!, ct), "target_not_fast_forward");
                    var checkout = await TargetCheckoutAsync(op, ct);
                    var advanced = checkout is null
                        ? await MutateAsync(op, op.RepositoryPath, ["update-ref", "--no-deref", op.TargetFullRef, op.VerifiedSourceSha!, current], ct)
                        : await MutateAsync(op, checkout, ["-c", "merge.autoStash=false", "merge", "--ff-only", op.VerifiedSourceSha!], ct);
                    Require(advanced.Succeeded, "target_advance_failed");
                }
                await CheckTargetAsync(op, op.VerifiedSourceSha!, ct);
                op.LocalTargetAfterSha = op.VerifiedSourceSha;
                await TransitionAsync(op, LandPhase.LocalTargetAdvanced, ct);
            }
            if (op.Phase is LandPhase.LocalTargetAdvanced or LandPhase.PushStarted)
            {
                await RecheckSourceAsync(op, op.VerifiedSourceSha!, ct);
                await CheckTargetAsync(op, op.VerifiedSourceSha!, ct);
                var beforePush = await ObserveAsync(op, op.VerifiedSourceSha!, ct);
                if (!beforePush.ContainsSource)
                {
                    Require(beforePush.Sha == op.RemoteBeforeSha, "remote_changed_before_push");
                    await RecheckSourceAsync(op, op.VerifiedSourceSha!, ct);
                    await CheckTargetAsync(op, op.VerifiedSourceSha!, ct);
                    if (op.Phase != LandPhase.PushStarted)
                    {
                        op.PushStartedAt = Now();
                        await TransitionAsync(op, LandPhase.PushStarted, ct);
                    }
                    await RecheckSourceAsync(op, op.VerifiedSourceSha!, ct);
                    await CheckTargetAsync(op, op.VerifiedSourceSha!, ct);
                    var pushed = await OwnedAsync(op, "push", started =>
                        git.PushOwnedAsync(op.RepositoryPath, Destination(op), op.VerifiedSourceSha!, started, ct), ct);
                    op.PushExitCode = pushed.ExitCode;
                    await SaveAsync(op, ct);
                    beforePush = await ObserveAsync(op, op.VerifiedSourceSha!, ct);
                    Require(beforePush.ContainsSource, pushed.Succeeded ? "push_unconfirmed" : "push_rejected");
                }
                await ConfirmAsync(op, beforePush,
                    op.PushExitCode == 0 ? LandPublicationOutcome.Landed : LandPublicationOutcome.AlreadyPresent, ct);
            }
            Require(_state.HasPublication(op), "publication_unconfirmed");
            return await CleanupAsync(op, lease, ct);
        }
        catch (Exception ex) when (ex is LandingRefusal or IOException or UnauthorizedAccessException or ArgumentException)
        {
            var reason = ex is LandingRefusal ? ex.Message : "landing_io_error";
            if (op is not null)
            {
                op.LastReason = reason;
                if (_state.HasPublication(op)) op.Cleanup = LandCleanupStatus.Refused;
                else if (op.Phase is LandPhase.Inspected or LandPhase.RecoveryPinned or LandPhase.RebaseStarted or LandPhase.Prepared)
                {
                    op.Publication = LandPublicationOutcome.Refused;
                    _state.Transition(op, LandPhase.Refused, Now());
                }
                // The caller commits terminal evidence, event, stages and pending state together.
                op.UpdatedAt = Now();
                op.ConcurrencyToken = Guid.NewGuid();
            }
            return new(op, reason, []);
        }
    }

    private async Task<LandingProtocolResult> CleanupAsync(AgentTaskLanding op, RepositoryLease lease, CancellationToken ct)
    {
        if (op.Phase == LandPhase.PublicationConfirmed)
        {
            op.CleanupStartedAt = Now();
            op.ExpectedDeletionSha = op.VerifiedSourceSha;
            op.Cleanup = LandCleanupStatus.Pending;
            await TransitionAsync(op, LandPhase.CleanupStarted, ct);
        }
        var removed = await worktrees.TryRemoveAsync(new(WorktreeRemovalPurpose.Publication, Coordinates(op),
            op.CommonDirectory, op.GitDirectory, op.VerifiedSourceSha!, op.TargetBeforeSha, op.Id, lease), ct);
        op.DirectoryRemoved = removed.DirectoryGone;
        op.RegistrationRemoved = removed.Unregistered;
        op.BranchRemoved = removed.BranchDeleted;
        op.LastReason = removed.Residue;
        op.Cleanup = removed.IsClean ? LandCleanupStatus.Complete : LandCleanupStatus.Refused;
        op.CleanupCompletedAt = Now();
        // The terminal cleanup evidence is committed with the outcome event and pending-task
        // changes by AgentTaskLandService. Until then the saved cleanup intent supports retry.
        if (removed.IsClean && op.Phase != LandPhase.Complete) _state.Transition(op, LandPhase.Complete, Now());
        return new(op, removed.Residue, []);
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

    private async Task RecheckSourceAsync(AgentTaskLanding op, string sha, CancellationToken ct)
    {
        var currentTask = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == op.TaskId, ct);
        Require(currentTask is not null && currentTask.ActiveLandingId == op.Id
            && currentTask.Status == AgentTaskStatus.Succeeded
            && currentTask.RepoPath is not null && SamePath(currentTask.RepoPath, op.RepositoryPath)
            && currentTask.WorktreePath is not null && SamePath(currentTask.WorktreePath, op.WorktreePath)
            && currentTask.WorktreeBranch is not null && FullRef(currentTask.WorktreeBranch) == op.SourceFullRef
            && FullRef(currentTask.MergeTargetRef ?? "master") == op.TargetFullRef, "task_coordinates_changed");
        if (!_state.HasPublication(op))
            Require(currentTask!.LandVerifyFilter == op.VerificationFilter, "verification_filter_changed");
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
        var inspected = await git.InspectAsync(Coordinates(op), ct);
        Require(inspected.Accepted && Matches(inspected.Snapshot!, op, sha), inspected.Reason ?? "source_changed");
    }

    private async Task<bool> PreparationChangedAsync(AgentTaskLanding op, LandSourceCoordinates coordinates,
        string? verificationFilter, CancellationToken ct)
    {
        var fresh = await git.InspectAsync(coordinates, ct);
        Require(fresh.Accepted, fresh.Reason ?? "source_unknown");
        if (!Matches(fresh.Snapshot!, op, op.VerifiedSourceSha!)
            || coordinates.SourceFullRef != op.SourceFullRef
            || coordinates.TargetFullRef != op.TargetFullRef
            || !SamePath(coordinates.RepositoryPath, op.RepositoryPath)
            || verificationFilter != op.VerificationFilter)
            return true;
        if (await git.DestinationAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct) != Destination(op)
            || await CommitAsync(op.RepositoryPath, op.TargetFullRef, ct) != op.TargetBeforeSha)
            return true;
        var checkout = await TargetCheckoutAsync(op, ct);
        return !op.TargetCheckoutRecorded || (checkout is null ? op.TargetCheckoutPath is not null
            : op.TargetCheckoutPath is null || !SamePath(checkout, op.TargetCheckoutPath));
    }

    private async Task CheckTargetAsync(AgentTaskLanding op, string expected, CancellationToken ct)
    {
        Require(await CommitAsync(op.RepositoryPath, op.TargetFullRef, ct) == expected, "target_changed");
        var checkout = await TargetCheckoutAsync(op, ct);
        Require(op.TargetCheckoutRecorded && (checkout is null ? op.TargetCheckoutPath is null
            : op.TargetCheckoutPath is not null && SamePath(checkout, op.TargetCheckoutPath)), "target_checkout_changed");
        if (checkout is null) return;
        Require(!await git.HasActiveSequencerAsync(checkout, ct), "target_active_sequencer");
        var symbolic = await git.RunAsync(checkout, ["symbolic-ref", "-q", "HEAD"], ct);
        Require(symbolic.Succeeded && symbolic.Output.Trim() == op.TargetFullRef, "target_checkout_changed");
        var status = await git.RunAsync(checkout,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], ct);
        Require(status.Succeeded && status.Output.Length == 0, "target_dirty_or_unknown");
        Require(await CommitAsync(checkout, "HEAD", ct) == expected, "target_changed");
    }

    private async Task<string?> TargetCheckoutAsync(AgentTaskLanding op, CancellationToken ct)
    {
        var rows = (await git.RegistrationsAsync(op.RepositoryPath, ct)).Where(r => r.Branch == op.TargetFullRef).ToList();
        Require(rows.Count <= 1, "ambiguous_target_checkout");
        if (rows.Count == 0) return null;
        Require(!rows[0].Locked && !rows[0].Prunable, "target_registration_unavailable");
        return await git.CanonicalDirectoryAsync(rows[0].Path, ct);
    }

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

    private Task SaveAsync(AgentTaskLanding op, CancellationToken ct)
    {
        op.UpdatedAt = Now();
        op.ConcurrencyToken = Guid.NewGuid();
        return db.SaveChangesAsync(ct);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
    private static void Require(bool condition, string reason) { if (!condition) throw new LandingRefusal(reason); }
    private static string FullRef(string branch) => branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
    private static bool SamePath(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static LandSourceCoordinates Coordinates(AgentTaskLanding op) => new(op.TaskId, op.RepositoryPath,
        op.WorktreePath, op.SourceFullRef, op.TargetFullRef);
    private static LandingDestination Destination(AgentTaskLanding op) => new(op.RemoteName, op.DestinationFullRef, op.RemoteFingerprint);
    private static bool Matches(LandSourceSnapshot snapshot, AgentTaskLanding op, string sha) => snapshot.HeadSha == sha
        && SamePath(snapshot.CommonDirectory, op.CommonDirectory) && SamePath(snapshot.GitDirectory, op.GitDirectory)
        && SamePath(snapshot.RegisteredPath, op.WorktreePath);
    private sealed class LandingRefusal(string reason) : Exception(reason);
}

public sealed record LandingProtocolResult(AgentTaskLanding? Operation, string? Reason, IReadOnlyList<string> Conflicts)
{
    public bool Published => Operation is not null && new AgentTaskLandingState().HasPublication(Operation);
}
