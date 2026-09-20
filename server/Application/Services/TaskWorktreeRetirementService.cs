using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>Caller-reviewed workspace release, revoke, inventory and typed settled-task retirement.</summary>
public sealed class TaskWorktreeRetirementService
{
    private static readonly AgentTaskStatus[] Terminal =
        [AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled];
    private static readonly AgentTaskStatus[] Live =
        [AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly WorktreeResidueSettings _settings;
    private readonly GitSettings _git;
    private readonly ILandingGit? _gitIo;
    private readonly IAgentReportStore? _reports;
    private readonly IWorkspaceReservationJournal? _reservations;
    private readonly IRetirementCommandJournal? _commands;
    private readonly IWorktreeManager? _worktrees;
    private readonly IRepositoryMutationLease? _leases;
    private readonly WorkspaceUseAdmission? _admission;
    private readonly ILogger<TaskWorktreeRetirementService> _logger;

    public TaskWorktreeRetirementService(
        AppDbContext db,
        TimeProvider clock,
        IOptions<WorktreeResidueSettings> settings,
        IOptions<GitSettings> git,
        ILogger<TaskWorktreeRetirementService> logger,
        ILandingGit? gitIo = null,
        IAgentReportStore? reports = null,
        IWorkspaceReservationJournal? reservations = null,
        IRetirementCommandJournal? commands = null,
        IWorktreeManager? worktrees = null,
        IRepositoryMutationLease? leases = null,
        WorkspaceUseAdmission? admission = null)
    {
        _db = db;
        _clock = clock;
        _settings = settings.Value;
        _git = git.Value;
        _logger = logger;
        _gitIo = gitIo;
        _reports = reports;
        _reservations = reservations;
        _commands = commands;
        _worktrees = worktrees;
        _leases = leases;
        _admission = admission;
    }

    public async Task<WorktreeRetirementDto> ReleaseAsync(
        Guid taskId,
        ReleaseWorktreeRetirementRequest body,
        AgentTaskService.Caller caller,
        CancellationToken ct)
    {
        if (!body.NoFurtherWorkspaceUse)
            throw new ValidationException(nameof(body.NoFurtherWorkspaceUse), "NoFurtherWorkspaceUse is required.");
        if (string.IsNullOrWhiteSpace(body.Reason))
            throw new ValidationException(nameof(body.Reason), "A disposition reason is required.");
        if (!GitObjectId.TryNormalize(body.SourceSha, out var sourceSha))
            throw new ValidationException(nameof(body.SourceSha), "SourceSha must be a full lowercase hex object id.");

        AuthorizeCaller(caller, taskId);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {taskId} FOR UPDATE", ct);
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId.ToString());

        if (task.ConcurrencyToken != body.ExpectedTaskRevision)
            throw new ConflictException("The reviewed task revision is stale.", "stale_approval_snapshot");
        if (!Terminal.Contains(task.Status))
            throw new ConflictException("Only a terminal task can release its workspace.", "terminal_required");
        if (task.CompletedAt is null
            || UtcNow() - task.CompletedAt.Value < TimeSpan.FromMinutes(_settings.MinSettledMinutes))
            throw new ConflictException("Completion has not met the settling floor.", "settling_floor");
        if (task.Workspace != WorkspaceMode.Worktree)
            throw new ConflictException("Only an ordinary Worktree task can be retired.", "outside_scope");
        if (task.Role == AgentTaskRole.Mutation)
            throw new ConflictException("Mutation workspaces are excluded.", "mutation_excluded");
        if (task.SourceLandingOperationId is not null)
            throw new ConflictException("SourceLanding bindings cannot borrow ordinary retirement.", "source_landing_excluded");
        if (task.RepairSourceTaskId is not null)
            throw new ConflictException("Repair-source tasks cannot retire an owner's workspace.", "repair_source_excluded");

        var identity = await RequireUniqueOrdinaryOwnerAsync(task, ct);
        var reportDigest = Digest(task.Result);
        if (!string.IsNullOrWhiteSpace(body.ReportDigest)
            && !string.Equals(body.ReportDigest, reportDigest, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("The reviewed report digest does not match current Result.", "stale_approval_snapshot");
        if (string.IsNullOrWhiteSpace(task.Result) && !body.MissingReportReviewed)
            throw new ConflictException("A missing report needs an explicit reviewed disposition.", "handoff_pending");
        if (!await ArtifactsRetrievableAsync(task, ct))
            throw new ConflictException("Referenced reports or deliverables are not retrievable outside the tree.", "artifact_unpreserved");

        var currentSha = await ResolveSourceShaAsync(task, identity.SourceFullRef, ct);
        if (!string.Equals(currentSha, sourceSha, StringComparison.Ordinal))
            throw new ConflictException("The reviewed source SHA does not match current source identity.", "stale_approval_snapshot");

        var dispositions = body.HandoffDispositions ?? [];
        if (!HandoffResolved(task, dispositions))
            throw new ConflictException("Outstanding handoffs require an explicit disposition.", "handoff_pending");
        if (await HasRecoveryDebtAsync(task, ct))
            throw new ConflictException("Open commit, merge or landing recovery blocks retirement.", "recovery_debt");

        var existing = await _db.TaskWorktreeRetirements.SingleOrDefaultAsync(
            r => r.TaskId == task.Id && r.TaskAttempt == task.Attempt && r.Active, ct);
        if (existing is not null)
        {
            if (existing.State is WorktreeRetirementState.Claimed or WorktreeRetirementState.CommandStarted
                or WorktreeRetirementState.Partial or WorktreeRetirementState.Complete)
            {
                await tx.CommitAsync(ct);
                return ToDto(existing);
            }

            if (SnapshotsEqual(existing, task, identity, currentSha, reportDigest, dispositions))
            {
                await tx.CommitAsync(ct);
                return ToDto(existing);
            }

            throw new ConflictException("A conflicting release already exists for this attempt.", "release_conflict");
        }

        var now = UtcNow();
        var row = new TaskWorktreeRetirement
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            TaskAttempt = task.Attempt,
            TerminalStatus = task.Status,
            TaskCompletedAt = task.CompletedAt.Value,
            ReportDigest = string.IsNullOrWhiteSpace(task.Result) ? null : reportDigest,
            MissingReportReviewed = body.MissingReportReviewed && string.IsNullOrWhiteSpace(task.Result),
            ReleasedTaskRevision = task.ConcurrencyToken,
            CallerSessionId = caller.SessionId,
            CallerTaskId = caller.Task?.Id,
            CallerIdentity = caller.Task is null ? "operator" : caller.Task.Id.ToString("D"),
            ReleaseReason = body.Reason.Trim(),
            ReleasedAt = now,
            HandoffDispositionJson = JsonSerializer.Serialize(dispositions, JsonOptions),
            RepositoryPath = identity.RepositoryPath,
            CommonDirectory = identity.CommonDirectory,
            WorktreePath = identity.WorktreePath,
            GitDirectory = identity.GitDirectory,
            SourceFullRef = identity.SourceFullRef,
            SourceSha = sourceSha,
            TargetFullRef = identity.TargetFullRef,
            RemoteName = identity.RemoteName,
            DestinationFullRef = identity.DestinationFullRef,
            RemoteFingerprint = identity.RemoteFingerprint,
            ResultPreservationPath = task.ResultFilePath,
            DeliverablePreservationPath = task.DeliverablePath ?? task.DeliverableBundleDir,
            State = WorktreeRetirementState.Released,
            Active = true,
            UpdatedAt = now,
        };
        _db.TaskWorktreeRetirements.Add(row);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(row);
    }

    public async Task RevokeAsync(Guid taskId, Guid retirementId, AgentTaskService.Caller caller, CancellationToken ct)
    {
        AuthorizeCaller(caller, taskId);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var row = await _db.TaskWorktreeRetirements.SingleOrDefaultAsync(r => r.Id == retirementId && r.TaskId == taskId, ct)
            ?? throw new NotFoundException(nameof(TaskWorktreeRetirement), retirementId.ToString());
        if (row.CommandIntentId is not null
            || row.State is WorktreeRetirementState.CommandStarted or WorktreeRetirementState.Partial
                or WorktreeRetirementState.Complete)
            throw new ConflictException("A retirement with deletion intent cannot be revoked.", "revoke_after_intent");
        if (row.State == WorktreeRetirementState.Claimed)
            throw new ConflictException("An active retirement claim cannot be revoked.", "revoke_after_claim");
        if (row.State == WorktreeRetirementState.Revoked)
        {
            await tx.CommitAsync(ct);
            return;
        }

        row.Active = false;
        row.State = WorktreeRetirementState.Revoked;
        row.UpdatedAt = UtcNow();
        row.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<WorktreeResidueRunDto> GetRunAsync(Guid runId, int page, AgentTaskService.Caller caller, CancellationToken ct)
    {
        if (caller.Task is not null)
            throw new ForbiddenException("A child task token cannot read residue runs.", "caller_scope");
        var run = await _db.WorktreeResidueRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new NotFoundException(nameof(WorktreeResidueRun), runId.ToString());
        var limit = Math.Max(1, _settings.RunResultPageSize);
        var skip = Math.Max(0, page) * limit;
        var total = await _db.WorktreeResidueRunCandidates.CountAsync(c => c.RunId == runId, ct);
        var rows = await _db.WorktreeResidueRunCandidates.AsNoTracking()
            .Where(c => c.RunId == runId)
            .OrderBy(c => c.EvaluatedAt).ThenBy(c => c.Id)
            .Skip(skip).Take(limit)
            .ToListAsync(ct);
        return new WorktreeResidueRunDto(
            run.Id, run.StartedAt, run.FinishedAt, run.Execute, run.Preview, run.ActionBudget, run.ActionsAccepted,
            run.Candidates, run.Held, run.Deferred, run.Queued, run.Refused, run.Partial, run.Removed,
            rows.Select(c => new WorktreeResidueCandidateDto(
                c.Id, c.Lane, c.Outcome, c.ReasonCode, c.TaskId, c.RetirementId, c.LandingOperationId, c.LandRequestId,
                c.Path, c.Branch, c.DirectoryRemoved, c.RegistrationRemoved, c.BranchRemoved)).ToList(),
            page, limit, total);
    }

    public async Task<TaskWorktreeRetirement?> ReadActiveAsync(Guid taskId, int attempt, CancellationToken ct) =>
        await _db.TaskWorktreeRetirements.AsNoTracking()
            .SingleOrDefaultAsync(r => r.TaskId == taskId && r.TaskAttempt == attempt && r.Active, ct);

    public bool SnapshotStillValid(TaskWorktreeRetirement release, AgentTask task) =>
        release.TaskAttempt == task.Attempt
        && release.TerminalStatus == task.Status
        && string.Equals(release.ReportDigest ?? "", Digest(task.Result), StringComparison.OrdinalIgnoreCase)
        && string.Equals(FullRef(task.WorktreeBranch), release.SourceFullRef, StringComparison.Ordinal)
        && PathsEqual(task.WorktreePath, release.WorktreePath)
        && PathsEqual(task.RepoPath, release.RepositoryPath);

    public async Task<(bool Authorized, string? Reason)> EvaluateEligibilityAsync(AgentTask task, TaskWorktreeRetirement? release, CancellationToken ct)
    {
        if (!Terminal.Contains(task.Status)) return (false, "live_owner");
        if (task.CompletedAt is null
            || UtcNow() - task.CompletedAt.Value < TimeSpan.FromMinutes(_settings.MinSettledMinutes))
            return (false, "settling");
        if (task.Workspace != WorkspaceMode.Worktree) return (false, "outside_scope");
        if (task.Role == AgentTaskRole.Mutation) return (false, "mutation_excluded");
        if (task.SourceLandingOperationId is not null) return (false, "source_landing_excluded");
        if (task.RepairSourceTaskId is not null) return (false, "repair_source_excluded");
        if (release is null || !release.Active || release.State == WorktreeRetirementState.Revoked)
            return (false, "release_required");
        if (!SnapshotStillValid(release, task)) return (false, "release_stale");
        if (!HandoffResolved(task, ParseDispositions(release.HandoffDispositionJson)))
            return (false, "handoff_pending");
        if (!await ArtifactsRetrievableAsync(task, ct)) return (false, "artifact_unpreserved");
        if (await HasRecoveryDebtAsync(task, ct)) return (false, "recovery_debt");
        if (_admission is not null)
        {
            var path = task.WorktreePath ?? task.WorkingDirectory ?? "";
            if (await _admission.HasLiveTaskConsumerAsync(path, task.WorktreeBranch ?? "", task.Id, ct))
                return (false, "live_owner");
            if (await _admission.HasLiveSessionOwnerAsync(path, ct))
                return (false, "live_owner");
        }
        try { await RequireUniqueOrdinaryOwnerAsync(task, ct); }
        catch (ConflictException ex) { return (false, ex.Code ?? "identity_unknown"); }
        return (true, null);
    }

    public async Task<WorktreeRemoval> TryRetireAsync(TaskWorktreeRetirement retirement, Guid? runId, CancellationToken ct)
    {
        if (_worktrees is null || _leases is null || _commands is null)
            return new(false, false, false, "retirement_executor_unavailable");
        if (retirement.State == WorktreeRetirementState.Complete)
            return new(true, true, true, null);

        await using var lease = await _leases.TryAcquireAsync(retirement.RepositoryPath, ct);
        if (lease is null) return new(false, false, false, "repository_lease_required");

        var attempt = await GetOrCreateAttemptAsync(retirement, runId, ct);
        if (attempt.CommandIntentId is Guid spent
            && attempt.DirectoryRemoved is not true)
            return new(attempt.DirectoryRemoved == true, attempt.RegistrationRemoved == true,
                attempt.BranchRemoved == true, "cleanup_command_slot_spent");

        var claim = await ClaimAsync(retirement, attempt, ct);
        if (!claim) return new(false, false, false, "retirement_claim_refused");

        if (_admission is not null)
        {
            var consumers = await _admission.FindLiveConsumersAsync(
                new WorkspaceReservationKey(retirement.WorktreePath, retirement.SourceFullRef, retirement.CommonDirectory),
                retirement.TaskId, ct);
            if (consumers.Count > 0
                || await _admission.HasLiveTaskConsumerAsync(retirement.WorktreePath, retirement.SourceFullRef, retirement.TaskId, ct)
                || await _admission.HasLiveSessionOwnerAsync(retirement.WorktreePath, ct))
            {
                await ReleaseClaimBeforeIntentAsync(retirement, ct);
                return new(false, false, false, "live_owner");
            }
        }

        var commandId = Guid.NewGuid();
        if (!await _commands.TryCommitIntentAsync(attempt.Id, commandId, ct))
            return new(false, false, false, "cleanup_command_slot_spent");
        retirement.CommandIntentId = commandId;
        retirement.CommandStartedAt = UtcNow();
        retirement.State = WorktreeRetirementState.CommandStarted;
        retirement.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);

        var request = new WorktreeRemovalRequest(
            WorktreeRemovalPurpose.SettledTask,
            new LandSourceCoordinates(retirement.TaskId, retirement.RepositoryPath, retirement.WorktreePath,
                retirement.SourceFullRef, retirement.TargetFullRef),
            retirement.CommonDirectory, retirement.GitDirectory, retirement.SourceSha,
            retirement.ObservedTargetSha ?? new string('0', 40),
            null, lease, ManagedRoot: ResolveManagedRoot(), RetirementId: retirement.Id,
            HasDeletionIntent: true);
        var removed = await _worktrees.TryRemoveAsync(request, ct);
        await _commands.RecordComponentAsync(attempt.Id, removed.DirectoryGone, removed.Unregistered,
            removed.BranchDeleted, removed.Residue, ct);
        retirement.DirectoryRemovedAt = removed.DirectoryGone ? UtcNow() : retirement.DirectoryRemovedAt;
        retirement.RegistrationRemovedAt = removed.Unregistered ? UtcNow() : retirement.RegistrationRemovedAt;
        retirement.BranchRemovedAt = removed.BranchDeleted ? UtcNow() : retirement.BranchRemovedAt;
        retirement.LastReason = removed.Residue;
        if (removed.IsClean)
        {
            retirement.State = WorktreeRetirementState.Complete;
            retirement.RetirementCompletedAt = UtcNow();
            if (_admission is not null)
                await _admission.FenceCompletedAsync(retirement, ct);
        }
        else if (removed.DirectoryGone || removed.Unregistered || removed.BranchDeleted)
            retirement.State = WorktreeRetirementState.Partial;
        else
        {
            retirement.State = WorktreeRetirementState.Refused;
            await ReleaseClaimBeforeIntentAsync(retirement, ct, keepIntent: true);
        }

        retirement.UpdatedAt = UtcNow();
        retirement.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
        return removed;
    }

    private async Task<bool> ClaimAsync(TaskWorktreeRetirement retirement, TaskWorktreeRetirementAttempt attempt, CancellationToken ct)
    {
        if (retirement.State is WorktreeRetirementState.Claimed or WorktreeRetirementState.CommandStarted
            or WorktreeRetirementState.Partial)
            return retirement.ClaimAttemptId == attempt.Id || retirement.ClaimAttemptId is null;

        if (_reservations is not null)
        {
            var claimed = await _reservations.TryClaimRetirementAsync(new(
                new WorkspaceReservationKey(retirement.WorktreePath, retirement.SourceFullRef, retirement.CommonDirectory),
                WorkspaceReservationKind.Retirement, retirement.TaskId, null, retirement.Id), ct);
            if (!claimed.Accepted) return false;
        }

        retirement.ClaimedAt = UtcNow();
        retirement.ClaimAttemptId = attempt.Id;
        retirement.State = WorktreeRetirementState.Claimed;
        retirement.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task ReleaseClaimBeforeIntentAsync(TaskWorktreeRetirement retirement, CancellationToken ct, bool keepIntent = false)
    {
        if (keepIntent || retirement.CommandIntentId is not null) return;
        retirement.ClaimedAt = null;
        retirement.ClaimAttemptId = null;
        retirement.State = WorktreeRetirementState.Released;
        retirement.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
    }

    private async Task<TaskWorktreeRetirementAttempt> GetOrCreateAttemptAsync(
        TaskWorktreeRetirement retirement, Guid? runId, CancellationToken ct)
    {
        var latest = await _db.TaskWorktreeRetirementAttempts
            .Where(a => a.RetirementId == retirement.Id)
            .OrderByDescending(a => a.AttemptNumber)
            .FirstOrDefaultAsync(ct);
        if (latest is not null && latest.FinishedAt is null) return latest;
        var now = UtcNow();
        var attempt = new TaskWorktreeRetirementAttempt
        {
            Id = Guid.NewGuid(),
            RetirementId = retirement.Id,
            SweepRunId = runId,
            AttemptNumber = (latest?.AttemptNumber ?? 0) + 1,
            CreatedAt = now,
            StartedAt = now,
            NotBefore = now,
            ReleasedTaskRevision = retirement.ReleasedTaskRevision,
        };
        _db.TaskWorktreeRetirementAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);
        return attempt;
    }

    private void AuthorizeCaller(AgentTaskService.Caller caller, Guid taskId)
    {
        if (caller.Task is null) return;
        if (caller.Task.Id != taskId && caller.Task.RootTaskId != taskId)
            throw new ForbiddenException("A child task token cannot release arbitrary other tasks.", "caller_scope");
        if (caller.Task.Id != taskId && caller.Task.ParentTaskId != taskId)
            throw new ForbiddenException("A child task token cannot release a sibling workspace.", "caller_scope");
    }

    private async Task<RetirementIdentity> RequireUniqueOrdinaryOwnerAsync(AgentTask task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.WorktreePath) || string.IsNullOrWhiteSpace(task.WorktreeBranch)
            || string.IsNullOrWhiteSpace(task.RepoPath))
            throw new ConflictException("Retirement needs exact recorded worktree coordinates.", "identity_unknown");
        var shortId = DelegationReportFormatter.Short(task.Id);
        var leaf = "card-task-" + shortId;
        var expectedBranch = "feat/card-task-" + shortId;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(task.WorktreePath));
        if (!name.Equals(leaf, StringComparison.OrdinalIgnoreCase)
            || !BranchName(task.WorktreeBranch).Equals(expectedBranch, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Retirement is limited to ordinary card-task leaf worktrees.", "outside_scope");
        var root = ResolveManagedRoot();
        if (!string.IsNullOrWhiteSpace(root) && !DelegationWorkspaceResolver.IsWithinRoot(task.WorktreePath, root))
            throw new ConflictException("Worktree is outside the managed root.", "outside_scope");

        var pathHits = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.WorktreePath != null)
            .Select(t => new { t.Id, t.WorktreePath, t.WorktreeBranch })
            .ToListAsync(ct);
        var owners = pathHits.Where(t =>
                PathsEqual(t.WorktreePath, task.WorktreePath)
                || string.Equals(BranchName(t.WorktreeBranch), BranchName(task.WorktreeBranch), StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Id).Distinct().ToList();
        if (owners.Count != 1 || owners[0] != task.Id)
            throw new ConflictException("Authority requires exactly one full task owner.", "identity_ambiguous");

        var repo = task.RepoPath!;
        string common = task.RepoPath!, gitDir = Path.Combine(task.RepoPath!, ".git");
        if (_gitIo is not null)
        {
            common = await _gitIo.CommonDirectoryAsync(repo, ct);
            var inspect = await _gitIo.InspectAsync(new(task.Id, repo, task.WorktreePath!, FullRef(task.WorktreeBranch),
                FullRef(task.MergeTargetRef ?? ChooseTarget(task))), ct);
            if (inspect.Snapshot is not null) gitDir = inspect.Snapshot.GitDirectory;
        }

        var target = FullRef(ChooseTarget(task));
        var destination = _gitIo is null
            ? new LandingDestination("origin", target, "")
            : await _gitIo.DestinationAsync(repo, target, ct);
        return new RetirementIdentity(repo, common, task.WorktreePath!, gitDir, FullRef(task.WorktreeBranch),
            target, destination.RemoteName, destination.FullRef, destination.Fingerprint);
    }

    private string ChooseTarget(AgentTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.MergeTargetRef)) return task.MergeTargetRef;
        return WorktreeBaseResolver.ChooseConfiguredDefault(null, _git.DefaultBranch);
    }

    private async Task<string> ResolveSourceShaAsync(AgentTask task, string sourceFullRef, CancellationToken ct)
    {
        if (_gitIo is not null)
        {
            var parsed = await _gitIo.RunAsync(task.RepoPath!, ["rev-parse", "--verify", sourceFullRef + "^{commit}"], ct);
            if (parsed.Succeeded && GitObjectId.IsFull(parsed.Output.Trim())) return parsed.Output.Trim();
        }

        if (GitObjectId.IsFull(task.WorktreeBaseSha)) return task.WorktreeBaseSha!;
        throw new ConflictException("Source SHA could not be resolved.", "identity_unknown");
    }

    private async Task<bool> ArtifactsRetrievableAsync(AgentTask task, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(task.Result))
        {
            if (!string.IsNullOrWhiteSpace(task.ResultFilePath)
                && InsideTree(task.ResultFilePath, task.WorktreePath)
                && (_reports is null || !await _reports.IsUsableAsync(task.ResultFilePath, task.Result, ct)))
                return false;
        }

        foreach (var path in new[] { task.DeliverablePath, task.DeliverableBundleDir, task.DeliverablePdfPath })
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (InsideTree(path, task.WorktreePath) && !File.Exists(path) && !Directory.Exists(path))
                return false;
        }

        return true;
    }

    private async Task<bool> HasRecoveryDebtAsync(AgentTask task, CancellationToken ct)
    {
        if (await _db.AgentTaskLandRequests.AnyAsync(r => r.TaskId == task.Id && r.IsPending, ct))
            return true;
        if (await _db.AgentTaskLandings.AnyAsync(o => o.TaskId == task.Id, ct))
            return true;
        return false;
    }

    private static bool HandoffResolved(AgentTask task, IReadOnlyList<WorktreeHandoffDispositionDto> dispositions)
    {
        if (task.NextStage is null && !string.IsNullOrWhiteSpace(task.Result))
            return true;
        if (task.NextStage is null && string.IsNullOrWhiteSpace(task.Result))
            return dispositions.Any(d => d.Kind == WorktreeHandoffDispositionKind.MissingReviewed);
        return dispositions.Any(d => d.NextStage == task.NextStage
            && d.Kind is WorktreeHandoffDispositionKind.Consumed
                or WorktreeHandoffDispositionKind.Superseded
                or WorktreeHandoffDispositionKind.Canceled
            && !string.IsNullOrWhiteSpace(d.Reason));
    }

    private static bool SnapshotsEqual(
        TaskWorktreeRetirement existing, AgentTask task, RetirementIdentity identity, string sourceSha,
        string reportDigest, IReadOnlyList<WorktreeHandoffDispositionDto> dispositions) =>
        existing.TaskAttempt == task.Attempt
        && existing.ReleasedTaskRevision == task.ConcurrencyToken
        && string.Equals(existing.SourceSha, sourceSha, StringComparison.OrdinalIgnoreCase)
        && string.Equals(existing.ReportDigest ?? "", reportDigest, StringComparison.OrdinalIgnoreCase)
        && string.Equals(existing.HandoffDispositionJson, JsonSerializer.Serialize(dispositions, JsonOptions), StringComparison.Ordinal)
        && PathsEqual(existing.WorktreePath, identity.WorktreePath);

    private static IReadOnlyList<WorktreeHandoffDispositionDto> ParseDispositions(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<WorktreeHandoffDispositionDto>>(json, JsonOptions)
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static WorktreeRetirementDto ToDto(TaskWorktreeRetirement row) => new(
        row.Id, row.TaskId, row.TaskAttempt, row.State, row.ReleasedTaskRevision, row.SourceSha, row.SourceFullRef,
        row.WorktreePath, row.ReleasedAt, row.ClaimedAt, row.CommandStartedAt,
        row.DirectoryRemovedAt is not null, row.RegistrationRemovedAt is not null, row.BranchRemovedAt is not null,
        row.LastReason);

    private static string Digest(string? result) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(result ?? "")));

    private static string FullRef(string? value) => value is null ? "refs/heads/master"
        : value.StartsWith("refs/", StringComparison.Ordinal) ? value : "refs/heads/" + value;

    private static string BranchName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value;
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool InsideTree(string path, string? tree)
    {
        if (string.IsNullOrWhiteSpace(tree)) return false;
        try { return DelegationWorkspaceResolver.IsWithinRoot(path, tree); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return false; }
    }

    private string ResolveManagedRoot()
    {
        var configured = _git.WorktreeBasePath;
        return string.IsNullOrWhiteSpace(configured) ? "" : Path.GetFullPath(configured);
    }

    private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;

    private sealed record RetirementIdentity(
        string RepositoryPath, string CommonDirectory, string WorktreePath, string GitDirectory,
        string SourceFullRef, string TargetFullRef, string RemoteName, string DestinationFullRef, string RemoteFingerprint);

    private sealed class NullWorktrees : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task<int> PruneStaleAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
