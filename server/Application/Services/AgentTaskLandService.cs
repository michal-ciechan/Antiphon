using System.Diagnostics;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The deterministic, explicit continuation from a reviewed Worktree branch to a pushed target.
/// It is deliberately separate from settlement merge-back: orchestration tasks retain their
/// current immediate parent-branch behaviour, while a reviewed root waits for this operation.
/// </summary>
public sealed class AgentTaskLandService
{
    private readonly AgentTaskLandingProtocol? _protocol;
    private readonly IRepositoryMutationLease? _leases;
    private readonly ILandingGit? _landingGit;
    private readonly AppDbContext _db;
    private readonly DelegationWorktreeService _worktrees;
    private readonly AgentTaskService _tasks;
    private readonly AgentTaskLandQueue _queue;
    private readonly SessionMessageQueueService _messages;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _clock;
    private readonly DelegationSettings _settings;
    private readonly ILogger<AgentTaskLandService> _logger;
    private readonly LandDeliveryBoundary _boundary;
    private readonly GitSettings? _gitSettings;
    private readonly WorkspaceUseAdmission? _workspaceUse;
    private readonly RepositoryLeaseWaiters? _leaseWaiters;
    private LandExecutionIdentity? _execution;

    /// <summary>CARD-0672 D-2: the land stood aside at admission for queued dispatches.</summary>
    public const string LeaseYieldedToDispatchCode = "repository_lease_yielded_to_dispatch";

    public AgentTaskLandService(
        AppDbContext db,
        DelegationWorktreeService worktrees,
        AgentTaskService tasks,
        AgentTaskLandQueue queue,
        SessionMessageQueueService messages,
        IEventBus eventBus,
        TimeProvider clock,
        IOptions<DelegationSettings> settings,
        ILogger<AgentTaskLandService> logger,
        AgentTaskLandingProtocol? protocol = null, IRepositoryMutationLease? leases = null, ILandingGit? landingGit = null,
        LandDeliveryBoundary? boundary = null, IOptions<GitSettings>? gitSettings = null,
        WorkspaceUseAdmission? workspaceUse = null,
        RepositoryLeaseWaiters? leaseWaiters = null)
    {
        _leaseWaiters = leaseWaiters;
        _workspaceUse = workspaceUse;
        _protocol = protocol;
        _boundary = boundary ?? new LandDeliveryBoundary();
        _leases = leases;
        _landingGit = landingGit;
        _db = db;
        _worktrees = worktrees;
        _tasks = tasks;
        _queue = queue;
        _messages = messages;
        _eventBus = eventBus;
        _clock = clock;
        _settings = settings.Value;
        _logger = logger;
        _gitSettings = gitSettings?.Value;
    }

    /// <summary>Persist and queue an explicit land request. The endpoint returns before git runs.</summary>
    public async Task<LandRequestResult> RequestAsync(Guid taskId, LandAgentTaskRequest body, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {taskId} FOR UPDATE", ct);
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId.ToString());
        await _db.Entry(task).ReloadAsync(ct);
        if (task.RepairSourceTaskId is Guid repairOwner)
            throw new ConflictException(
                $"Repair tasks cannot be landed; commission Land on the original owner {DelegationReportFormatter.Short(repairOwner)}.",
                "repair_source_landing_owner_required");
        if (task.Role == AgentTaskRole.Mutation || task.SourceLandingOperationId is not null)
            throw new ConflictException("Mutation snapshots cannot be landed.", "verification_publication_forbidden");
        if (task.Workspace != WorkspaceMode.Worktree)
            throw new ConflictException("Only a Worktree task can be landed.");
        if (task.Status != AgentTaskStatus.Succeeded)
            throw new ConflictException($"Task {DelegationReportFormatter.Short(task.Id)} must have succeeded before it can land.");
        WorkspaceReservationSnapshot? admitted = null;
        var committed = false;
        if (_workspaceUse is not null && !string.IsNullOrWhiteSpace(task.WorktreePath))
        {
            admitted = await _workspaceUse.RequireConsumerAsync(new WorkspaceReservationCommand(
                WorkspaceReservationKey.ForTask(task.WorktreePath, task.WorkingDirectory, task.WorktreeBranch, task.RepoPath),
                WorkspaceReservationKind.Launch, task.Id), ct);
        }

        try
        {
            var shortId = DelegationReportFormatter.Short(task.Id);
            if (_queue.IsActive(taskId) && task.LandRequestedAt is null)
                _queue.Release(taskId);
            if (_queue.IsActive(taskId))
            {
                var requested = task.LandRequestedAt?.ToString("u") ?? "unknown";
                var state = task.LandStartedAt is null
                    ? ", queued"
                    : $", started {task.LandStartedAt:u}, attempt {task.LandAttempt}";
                throw new ConflictException(
                    $"Task {shortId} land is running in this server: requested {requested}{state}. Wait for its outcome event.",
                    "land_running");
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            var filter = ClipFilter(body.Verify);
            await _db.Entry(task).ReloadAsync(ct);
            var request = task.LandRequestedAt is not null ? await EnsureRequestAsync(task, ct) : await GetRequestAsync(task, ct);
            var pending = request is { IsPending: true } && task.LandRequestedAt is not null;
            if (pending)
            {
                var suppliedSha = LandApproval.NormalizeExpectedSha(body.ExpectedSourceSha, required: false);
                var suppliedEvidence = body.ReviewEvidenceId;
                if (suppliedSha is not null && request!.ExpectedSourceSha is not null && suppliedSha != request.ExpectedSourceSha
                    || suppliedEvidence is not null && request!.ReviewEvidenceId is not null && suppliedEvidence != request.ReviewEvidenceId
                    || body.Verify is not null && filter != request!.VerifyFilter
                    || suppliedSha is not null && request!.ExpectedSourceSha is null
                    || suppliedEvidence is not null && request!.ReviewEvidenceId is null)
                    throw new ConflictException("A pending land request cannot change expected SHA, evidence or filter.",
                        "land_request_identity_conflict");
                if (request!.State == LandRequestState.NeedsResolution) request.State = LandRequestState.Queued;
            }
            else
            {
                var published = task.ActiveLandingId is Guid opId
                    ? await _db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == opId, ct)
                    : null;
                var inherit = published is not null && new AgentTaskLandingState().HasPublication(published);
                var expected = LandApproval.NormalizeExpectedSha(body.ExpectedSourceSha, required: !inherit);
                if (expected is null && inherit)
                    expected = published!.OriginalSourceSha;
                if (expected is not null && inherit && expected != published!.OriginalSourceSha)
                    throw new ConflictException("Cleanup retry expectedSourceSha does not match the published original.",
                        "land_request_identity_conflict");
                Guid? evidenceId = body.ReviewEvidenceId;
                // CARD-0544 D-5: once any Interim work was admitted for this owner, no explicit-caller
                // fallback remains. A cleanup-only retry after confirmed publication needs no new sweep.
                if (!inherit && evidenceId is null && task.RequiresFinalVerificationReview)
                    throw new ConflictException(
                        "This owner had Interim verification; land requires reviewEvidenceId for a Clean Final Review that completed Full scope.",
                        LandApproval.FinalReviewRequiredCode);
                var kind = LandApprovalKind.ExplicitCaller;
                if (evidenceId is { } eid)
                {
                    var evidence = await LandApproval.LoadUsableEvidenceAsync(_db, eid, expected!, task, ct);
                    evidenceId = evidence.Id;
                    kind = LandApprovalKind.ReviewEvidence;
                }
                request = NewRequest(task, now, filter, expected, evidenceId, kind);
                _db.AgentTaskLandRequests.Add(request);
                task.CurrentLandRequestId = request.Id;
                task.LandRequestedAt = now;
                task.LandStartedAt = null;
                task.LandAttempt = 0;
                var requestedEvent = Event(task.Id, AgentTaskEventType.LandRequested,
                    ApprovalRequestedDetail(filter, expected, evidenceId), now);
                requestedEvent.LandRequestId = request.Id;
                _db.AgentTaskEvents.Add(requestedEvent);
            }
            task.LandVerifyFilter = request!.VerifyFilter;
            task.ConcurrencyToken = Guid.NewGuid();
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            committed = true;
            if (!_boundary.DropWakeup("land-request", request.Id)) _queue.TryEnqueue(taskId, request.VerifyFilter, request.Id);
            await PublishAsync(task, ct);
            return new LandRequestResult(task.Id, pending ? "requeued" : "queued", request.Id,
                request.ReplyTo == AgentTaskReplyTo.None ? "not-required" : "tracked");
        }
        catch when (!committed)
        {
            // CARD-0664 D-4: a request refused after admission (land_running, identity conflict,
            // evidence, final review) gives its own Launch row back before the error surfaces.
            if (_workspaceUse is not null)
                await _workspaceUse.ReleaseAsync(admitted, CancellationToken.None);
            throw;
        }
    }

    /// <summary>Queue cleanup-only retry of a confirmed publication. Never publishes.</summary>
    public async Task<LandRequestResult> RequestCleanupRetryAsync(Guid taskId, Guid operationId, Guid? sweepRunId, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {taskId} FOR UPDATE", ct);
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId.ToString());
        var op = await _db.AgentTaskLandings.AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId, ct);
        if (op is null || !op.Active || op.TaskId != taskId || !new AgentTaskLandingState().HasPublication(op)
            || op.Cleanup == LandCleanupStatus.Complete)
            throw new ConflictException("Cleanup retry requires the exact confirmed active operation.", "publication_unconfirmed");
        if (await _db.AgentTaskLandRequests.AnyAsync(r => r.TaskId == taskId && r.IsPending, ct))
            throw new ConflictException("A pending land request already exists.", "land_request_pending");

        var now = _clock.GetUtcNow().UtcDateTime;
        var request = NewRequest(task, now, task.LandVerifyFilter, op.OriginalSourceSha, op.ReviewEvidenceId,
            LandApprovalKind.InheritedResume);
        request.CleanupOnly = true;
        request.RequiredLandingOperationId = operationId;
        request.SweepRunId = sweepRunId;
        request.Origin = LandRequestOrigin.ScheduledCleanup;
        request.ReplyTo = AgentTaskReplyTo.None;
        request.ParentSessionId = null;
        _db.AgentTaskLandRequests.Add(request);
        task.CurrentLandRequestId = request.Id;
        task.LandRequestedAt = now;
        task.LandStartedAt = null;
        task.LandAttempt = 0;
        task.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (!_boundary.DropWakeup("land-request", request.Id)) _queue.TryEnqueue(taskId, request.VerifyFilter, request.Id);
        return new LandRequestResult(task.Id, "queued", request.Id, "not-required");
    }

    /// <summary>Run one background land request. A Shared-writer hold leaves the request pending.</summary>
    public async Task<LandRunResult> RunAsync(Guid taskId, string? verifyFilter, CancellationToken ct)
        => await RunRequestAsync(taskId, null, verifyFilter, ct);

    public async Task<LandRunResult> RunRequestAsync(Guid taskId, Guid? requestId, string? verifyFilter, CancellationToken ct)
    {
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is not null && task.RepairSourceTaskId is Guid repairOwner)
            throw new ConflictException(
                $"Repair tasks cannot be landed; commission Land on the original owner {DelegationReportFormatter.Short(repairOwner)}.",
                "repair_source_landing_owner_required");
        if (task is not null && (task.Role == AgentTaskRole.Mutation || task.SourceLandingOperationId is not null))
            throw new ConflictException("Mutation snapshots cannot be landed.", "verification_publication_forbidden");
        if (task is null)
            return LandRunResult.Complete;
        if (task.LandRequestedAt is null)
            return LandRunResult.Complete;
        if (requestId is not null && task.CurrentLandRequestId != requestId)
            return LandRunResult.Complete;
        var request = await EnsureRequestAsync(task, ct);
        try
        {
            return await RunEnsuredRequestAsync(task, request, ct);
        }
        finally
        {
            // CARD-0672 D-2: every return that leaves the request terminal ends its yield entry.
            if (!request.IsPending)
                _leaseWaiters?.EndYield(request.Id);
        }
    }

    private async Task<LandRunResult> RunEnsuredRequestAsync(AgentTask task, AgentTaskLandRequest request, CancellationToken ct)
    {
        if (!request.IsPending) return LandRunResult.Complete;
        await _boundary.ReachedAsync("before-execution", task.Id, request.Id, ct);
        request.LastEvaluatedAt = _clock.GetUtcNow().UtcDateTime;
        if (task.Status == AgentTaskStatus.Blocked)
            return LandRunResult.Complete;
        if (task.Status != AgentTaskStatus.Succeeded || task.Workspace != WorkspaceMode.Worktree)
        {
            await using var canceled = await _db.Database.BeginTransactionAsync(ct);
            await LockTaskAsync(task.Id, ct);
            await _db.Entry(task).ReloadAsync(ct);
            await _db.Entry(request).ReloadAsync(ct);
            if (task.CurrentLandRequestId != request.Id || !request.IsPending || task.LandRequestedAt is null
                || task.Status == AgentTaskStatus.Blocked || task.Status == AgentTaskStatus.Succeeded && task.Workspace == WorkspaceMode.Worktree)
                return LandRunResult.Complete;
            request.State = LandRequestState.Canceled;
            request.IsPending = false;
            request.ReconciliationError = "task_no_longer_eligible";
            ClearPending(task);
            await _db.SaveChangesAsync(ct);
            await canceled.CommitAsync(ct);
            await ReleaseLandOwnerAsync(task.Id);
            return LandRunResult.Complete;
        }

        if (_protocol is null || _leases is null || _landingGit is null || task.RepoPath is null)
        {
            await RefuseAsync(task, "landing_protocol_unavailable", ct);
            return LandRunResult.Complete;
        }
        // CARD-0672 D-2: dispatch first. While a task refused the lease waits on this repository the
        // land stands aside before it acquires, for at most LandYieldToDispatchMaxSeconds.
        if (await YieldToDispatchAsync(task, request, ct) is { } yielded)
            return yielded;
        await using var lease = await _leases.TryAcquireAsync(
            task.RepoPath, new RepositoryLeaseOwnerTag(task.Id, RepositoryLeasePurposes.Land), ct);
        if (lease is null)
        {
            await HoldOnBusyLeaseAsync(task, request, ct);
            return LandRunResult.Held;
        }
        // CARD-0642 D-4/D-7: one read-cache scope per land, disposed before the lease it relies on.
        using var gitScope = _landingGit.BeginOperationScope();
        var wall = Stopwatch.StartNew();
        var outcome = "Exception";
        try
        {
            var run = await RunLeasedAsync(task, request, lease, ct);
            outcome = run.ToString();
            return run;
        }
        finally
        {
            var terminal = _db.ChangeTracker.Entries<AgentTaskEvent>().Select(e => e.Entity)
                .Where(e => e.AgentTaskId == task.Id && e.LandRequestId == request.Id && e.IsLandTerminal)
                .OrderBy(e => e.At).LastOrDefault();
            var profile = gitScope.Profile;
            var phases = PhaseSeconds(task.ActiveLandingId is Guid landed
                ? _db.ChangeTracker.Entries<AgentTaskLanding>().Select(e => e.Entity).FirstOrDefault(o => o.Id == landed) : null,
                _protocol?.LastResetSeconds);
            _logger.LogInformation(
                "Land git profile task={TaskId} request={RequestId} outcome={Outcome} wallSeconds={WallSeconds} processes={Processes} "
                + "worktreeList={WorktreeList} registrationHits={RegistrationHits} canonicalHits={CanonicalHits} "
                + "inspections={Inspections} remote={Remote} gitSeconds={GitSeconds} "
                + "reset={Reset} rebase={Rebase} verify={Verify} push={Push} canonical={Canonical} cleanup={Cleanup}",
                task.Id, request.Id, terminal is null ? outcome : $"{outcome}/{terminal.Type}",
                Math.Round(wall.Elapsed.TotalSeconds, 2), profile.Processes, profile.WorktreeLists, profile.RegistrationHits,
                profile.CanonicalHits, profile.Inspections, profile.RemoteRoundTrips, Math.Round(profile.GitSeconds, 2),
                phases.Reset, phases.Rebase, phases.Verify, phases.Push, phases.Canonical, phases.Cleanup);
        }
    }

    private async Task<LandRunResult> RunLeasedAsync(AgentTask task, AgentTaskLandRequest request, RepositoryLease lease,
        CancellationToken ct)
    {
        await _db.Entry(task).ReloadAsync(ct);
        if (task.LandRequestedAt is null || task.Status != AgentTaskStatus.Succeeded || task.CurrentLandRequestId != request.Id)
            return LandRunResult.Complete;
        await _db.Entry(request).ReloadAsync(ct);
        var holder = await FindWriterAsync(task, lease.CommonDirectory, ct);
        if (holder is not null)
        {
            await HoldAsync(task, request, "repository_or_source_writer", holder,
                $"Landing waits for repository/source writer {holder.Id:N} ({holder.Status}).", ct);
            return LandRunResult.Held;
        }
        var indexLock = await ProbeAdmissionIndexLockAsync(task, ct);
        if (indexLock is not null)
        {
            await HoldAsync(task, request, indexLock.Value.Code, null, indexLock.Value.Detail, ct);
            return LandRunResult.Held;
        }
        await using (var admission = await _db.Database.BeginTransactionAsync(ct))
        {
        await LockTaskAsync(task.Id, ct);
        await _db.Entry(task).ReloadAsync(ct);
        await _db.Entry(request).ReloadAsync(ct);
        if (!request.IsPending || task.CurrentLandRequestId != request.Id || task.Status != AgentTaskStatus.Succeeded
            || task.LandRequestedAt is null || request.State == LandRequestState.NeedsResolution) return LandRunResult.Complete;
        if (task.LandRequestedAt != request.RequestedAt || task.LandAttempt != request.Attempt)
        {
            request.ReconciliationError = "land_request_mirror_disagreement";
            await _db.SaveChangesAsync(ct); await admission.CommitAsync(ct);
            return LandRunResult.Complete;
        }
        if (request.State == LandRequestState.Held)
        {
            var released = Event(task.Id, AgentTaskEventType.HeldReleased, "Land admitted; hold released.", _clock.GetUtcNow().UtcDateTime);
            released.LandRequestId = request.Id;
            _db.AgentTaskEvents.Add(released);
        }
        request.State = LandRequestState.Running;
        request.HoldReasonCode = request.HoldDetail = null;
        request.HoldingTaskId = null;
        request.HoldingTaskStatus = null;
        request.HeldSince = null;
        if (request.HighestProgress < -1)
        {
            request.HighestProgress = -1;
            request.LastProgressAt = _clock.GetUtcNow().UtcDateTime;
            request.WarningAt = request.ErrorAt = null;
        }
        task.LandStartedAt = _clock.GetUtcNow().UtcDateTime;
        request.StartedAt ??= task.LandStartedAt;
        request.LastAttemptAt = task.LandStartedAt;
        request.Attempt = task.LandAttempt + 1;
        task.LandAttempt += 1;
        task.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
        await admission.CommitAsync(ct);
        }
        _execution = new LandExecutionIdentity(request.Id, request.Attempt);
        // CARD-0672 D-2: admission ends this request's yield budget; a retry of it yields afresh.
        _leaseWaiters?.EndYield(request.Id);
        await _db.Entry(task).ReloadAsync(ct);
        await _db.Entry(request).ReloadAsync(ct);
        var active = task.ActiveLandingId is Guid operationId
            ? await _db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == operationId, ct)
            : null;
        var published = active is not null && new AgentTaskLandingState().HasPublication(active);
        // Resume unpublished work in the protocol only when this same request already has a
        // durable remote-source snapshot. Missing snapshot (legacy/unpublished) and a replacement
        // request must resolve again; otherwise remote movement is never observed and a retry can
        // inherit null remote fields, which disables every later remote check.
        var resumeExisting = !published && active is not null && active.Phase != LandPhase.Refused
            && active.SchemaVersion is 2 or 3
            && GitObjectId.IsFull(active.SourceRemoteSha)
            && active.SourceRemoteFingerprint is { Length: 64 }
            && GitObjectId.IsFull(active.ReviewedSourceSha)
            && active.ApprovalLandRequestId == request.Id;
        if (request.CleanupOnly)
        {
            if (request.RequiredLandingOperationId is not Guid required
                || active is null || active.Id != required || !published)
            {
                await RefuseAsync(task, "cleanup_only_operation_mismatch", ct);
                return LandRunResult.Complete;
            }
        }
        var state = new AgentTaskLandingState();
        var raceRetries = 0;
        var budget = _settings.LandTargetRaceRetries;
        while (true)
        {
        if (!request.CleanupOnly && !published && !resumeExisting)
        {
            var canResolve = request.SchemaVersion == 2 && GitObjectId.IsFull(request.ExpectedSourceSha);
            if (!canResolve)
            {
                await RefuseAsync(task, FormatSourceRefusal(request, "legacy_review_binding_required"), ct);
                return LandRunResult.Complete;
            }
            try
            {
                var resolved = await new AgentTaskLandSourceResolver(_db, _landingGit, _leases, _clock,
                    _gitSettings is null ? null : Options.Create(_gitSettings), _protocol.LandWorkspace)
                    .ResolveAsync(task, request, lease, ct);
                if (resolved.StaleRequest) return LandRunResult.Complete;
                if (resolved.Reason is not null)
                {
                    await RefuseAsync(task, AppendDetail(FormatSourceRefusal(request, resolved.Reason), resolved.Detail), ct);
                    return LandRunResult.Complete;
                }
            }
            catch (LandSourceResolutionConflictException ex)
            {
                await FailRequestAsync(task.Id, request.Id, ex, ct);
                return LandRunResult.Complete;
            }
            await _db.Entry(task).ReloadAsync(ct);
            await _db.Entry(request).ReloadAsync(ct);
        }
        var result = await _protocol.RunAsync(task, lease, request, ct);
        if (result.Conflicts.Count > 0)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            await LockTaskAsync(task.Id, ct);
            await _db.Entry(task).ReloadAsync(ct);
            await _db.Entry(request).ReloadAsync(ct);
            if (task.CurrentLandRequestId != request.Id || !request.IsPending || request.State == LandRequestState.NeedsResolution)
                return LandRunResult.Complete;
            task.Status = AgentTaskStatus.Blocked;
            task.FailureReason = "Landing rebase conflicted.";
            var helper = await _tasks.CreateMergeTaskAsync(task, result.Conflicts, ct, task.MergeTargetRef ?? "master");
            Record(task, OrchestrationStage.Rebase, StageOutcomeKind.Found, DurationSeconds(result.Operation, OrchestrationStage.Rebase),
                string.Join(", ", result.Conflicts), helper is null ? "merge task cap reached" : DelegationReportFormatter.Short(helper.Id));
            var conflictEvent = Event(task.Id, AgentTaskEventType.Conflicted,
                string.Join(", ", result.Conflicts), _clock.GetUtcNow().UtcDateTime);
            SetLandingEvidence(conflictEvent, result.Operation);
            conflictEvent.LandRequestId = request.Id;
            request.State = LandRequestState.NeedsResolution;
            request.LandingOperationId = result.Operation?.Id;
            _db.AgentTaskEvents.Add(conflictEvent);
            AddNotification(task, request, conflictEvent, LandNotificationKind.Conflict);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await PublishAsync(task, ct);
            return LandRunResult.Complete;
        }
        var op = result.Operation;
        if (!result.Published)
        {
            if (op?.RebasedSourceSha is not null && op.Mode != LandOperationMode.CleanupRetry)
                Record(task, OrchestrationStage.Rebase, StageOutcomeKind.Clean, DurationSeconds(op, OrchestrationStage.Rebase));
            if (result.Reason == "verification_failed")
                Record(task, OrchestrationStage.Verify, StageOutcomeKind.Found, DurationSeconds(op, OrchestrationStage.Verify), "build failed or selected tests failed", task.WorktreePath);
            if (op?.VerifiedAt is not null)
                Record(task, OrchestrationStage.Verify, op.VerificationPassed ? StageOutcomeKind.Clean : StageOutcomeKind.Skipped,
                    DurationSeconds(op, OrchestrationStage.Verify), op.VerificationSkipReason ?? "verification passed");
            if (op is not null && state.IsTargetRaceRefusal(op) && raceRetries < budget)
            {
                raceRetries++;
                if (!await WriteRaceRetryAsync(task, request, op, raceRetries, budget, result.Detail, ct))
                    return LandRunResult.Complete;
                await _db.Entry(task).ReloadAsync(ct);
                await _db.Entry(request).ReloadAsync(ct);
                resumeExisting = false;
                published = false;
                continue;
            }
            var reason = result.Reason ?? "publication_unconfirmed";
            var detail = reason == "remote_changed_before_push" && GitObjectId.IsFull(result.Detail) ? null : result.Detail;
            var line = raceRetries > 0 && op is not null && state.IsTargetRaceRefusal(op)
                ? $"{reason}; after {raceRetries} automatic rebases (Delegation:LandTargetRaceRetries={budget}); run -Land again"
                : AppendDetail(reason, detail);
            await RefuseAsync(task, line, ct);
            return LandRunResult.Complete;
        }
        if (op!.Mode != LandOperationMode.CleanupRetry)
        {
            Record(task, OrchestrationStage.Rebase, op.RebasedSourceSha is null ? StageOutcomeKind.Skipped : StageOutcomeKind.Clean, DurationSeconds(op, OrchestrationStage.Rebase));
            Record(task, OrchestrationStage.Verify, op.VerificationPassed ? StageOutcomeKind.Clean : StageOutcomeKind.Skipped,
                DurationSeconds(op, OrchestrationStage.Verify), op.VerificationSkipReason ?? "build OK; selected verification passed");
        }
        Record(task, OrchestrationStage.Cleanup,
            op.Cleanup == LandCleanupStatus.Complete ? StageOutcomeKind.Clean : StageOutcomeKind.Failed,
            DurationSeconds(op, OrchestrationStage.Cleanup), AppendDetail(result.Reason ?? "cleanup complete", result.Detail));
        var type = TerminalType(op);
        var (siblings, warnings) = await CollectUnlandedSiblingsAsync(task, op.RepositoryPath, ct, op.VerifiedSourceSha);
        var marker = UnlandedMarker(siblings);
        if (CanonicalWarning(op) is { } canonical) warnings = [.. warnings, canonical];
        await SettleLandedAsync(task, type, AppendUnlandedMarker(FormatOutcome(op, result.Detail), marker),
            warnings, siblings, ct, result.Detail);
        return LandRunResult.Complete;
        }
    }

    /// <summary>CARD-0711: one warning and a restarted progress clock, in the same transaction as the refused operation.</summary>
    private async Task<bool> WriteRaceRetryAsync(AgentTask task, AgentTaskLandRequest request, AgentTaskLanding op,
        int retry, int budget, string? observedTip, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var warning = Event(task.Id, AgentTaskEventType.Warning,
            $"Land raced with a push to {op.RemoteName}:{op.DestinationFullRef} (retry {retry} of {budget}): the target moved from "
            + $"{ShortSha(op.RemoteBeforeSha ?? op.TargetBeforeSha)} to {ShortSha(observedTip)} after the candidate "
            + $"{ShortSha(op.VerifiedSourceSha ?? op.RebasedSourceSha ?? op.OriginalSourceSha)} was verified; rebasing "
            + $"{ShortSha(op.ReviewedSourceSha ?? op.OriginalSourceSha)} onto the new tip.", now);
        warning.LandRequestId = request.Id;
        warning.LandingOperationId = op.Id;
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(task.Id, ct);
        await _db.Entry(task).ReloadAsync(ct);
        await _db.Entry(request).ReloadAsync(ct);
        if (task.CurrentLandRequestId != request.Id || !request.IsPending) return false;
        request.HighestProgress = -1;
        request.LastProgressAt = now;
        request.WarningAt = request.ErrorAt = null;
        request.ConcurrencyToken = Guid.NewGuid();
        _db.AgentTaskEvents.Add(warning);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static string ShortSha(string? sha) => sha is { Length: >= 8 } ? sha[..8] : "unknown";

    internal static string FormatOutcome(AgentTaskLanding op, string? cleanupDetail = null) =>
        $"{(op.Publication == LandPublicationOutcome.AlreadyPresent ? "already present" : "landed")} operation={op.Id:N} mode={op.Mode} "
        + $"source={op.OriginalSourceSha} reviewed={op.ReviewedSourceSha ?? op.OriginalSourceSha} verified={op.VerifiedSourceSha} -> {op.RemoteName}:{op.DestinationFullRef}; "
        + $"remote={op.ObservedRemoteTargetSha} confirmed at {op.RemoteConfirmedAt:O}; "
        + (op.PushStartedAt is null ? "no push attempted; " : $"push exit={op.PushExitCode?.ToString() ?? "unknown"}; ")
        + $"cleanup={op.Cleanup}" + (op.LastReason is null ? "" : $": {op.LastReason}")
        + (string.IsNullOrEmpty(cleanupDetail) ? "" : $"; {cleanupDetail}")
        + (CanonicalOutcome(op) is { } canonical ? $"; canonical={canonical}" : "");

    /// <summary>CARD-0688 D-4: <c>advanced</c>, <c>already</c> or the residue reason; null before the step ran.</summary>
    internal static string? CanonicalOutcome(AgentTaskLanding op) =>
        op.CanonicalAdvanceReason
        ?? (op.CanonicalAdvancedAt is null ? null
            : op.SchemaVersion == 3 && op.LocalTargetAfterSha is not null && op.LocalTargetAfterSha == op.VerifiedSourceSha
                ? "advanced" : "already");

    /// <summary>A published land whose canonical checkout did not advance is landed with residue (D-4).</summary>
    internal static AgentTaskEventType TerminalType(AgentTaskLanding op) =>
        op.Publication == LandPublicationOutcome.AlreadyPresent ? AgentTaskEventType.AlreadyPresent
        : op.Cleanup == LandCleanupStatus.Complete && op.CanonicalAdvanceReason is null ? AgentTaskEventType.Landed
        : AgentTaskEventType.LandedWithResidue;

    internal static string? CanonicalWarning(AgentTaskLanding op) => op.CanonicalAdvanceReason is not { } reason ? null
        : $"Published {op.VerifiedSourceSha} to {op.RemoteName}:{op.DestinationFullRef}, but the canonical checkout "
          + $"{op.TargetCheckoutPath ?? op.RepositoryPath} was not fast-forwarded ({reason}). Fix it there with `git pull --rebase`, "
          + "then restart with scripts/restart-apphost.ps1 (CARD-0358 runbook). Publication is unaffected.";

    internal static string FormatSourceRefusal(AgentTaskLandRequest request, string reason) =>
        LandFailureDiagnostic.AppendInspection(
            $"{reason} expected={request.ExpectedSourceSha ?? "null"} local={request.LocalBeforeSha ?? "null"} "
            + $"remote={request.RemoteSourceSha ?? "null"} candidate={request.CandidateSourceSha ?? "null"}",
            request);

    internal static string AppendDetail(string reason, string? detail) =>
        string.IsNullOrEmpty(detail) ? reason : reason + "; " + detail;

    /// <summary>
    /// CARD-0543 V-11s / CARD-0688 D-10: probe the source worktree when its directory still exists (cleanup
    /// still mutates it), the land worktree when it exists, and the main checkout only when
    /// <c>&lt;common&gt;/HEAD</c> names the merge target. No registration listing: the main checkout is a file
    /// read. A missing source-worktree directory is not a lock: cleanup never clears
    /// <see cref="AgentTask.WorktreePath"/>, and probing it would turn <c>index_lock_path_error</c> into a
    /// permanent <c>git_index_lock_held</c> hold. A common-directory lookup failure falls back to RepoPath.
    /// </summary>
    private async Task<(string Code, string Detail)?> ProbeAdmissionIndexLockAsync(AgentTask task, CancellationToken ct)
    {
        if (_landingGit is null || task.RepoPath is null)
            return null;
        if (task.WorktreePath is not null
            && await ProbeCheckoutIndexLockAsync(task.WorktreePath, ct) is { } source)
            return source;

        string? land = null, targetCheckout = null;
        try
        {
            var common = await _landingGit.CommonDirectoryAsync(task.RepoPath, ct);
            land = _protocol?.LandWorkspace?.PathFor(common);
            // Only a real worktree can hold a lock; a foreign directory there is EnsureAsync's named refusal,
            // never a permanent index_lock_path_error hold.
            if (land is not null && !Path.Exists(Path.Combine(land, ".git"))) land = null;
            var targetRef = FullRef(task.MergeTargetRef ?? "master");
            var main = Infrastructure.Git.LandWorkspace.Scan(common).First(h => h.IsMain);
            if (main.SymbolicRef == targetRef) targetCheckout = main.WorktreePath ?? task.RepoPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            targetCheckout = task.RepoPath;
        }

        foreach (var checkout in new[] { land, targetCheckout })
        {
            if (checkout is null || GitIndexLock.PathsEqual(checkout, task.WorktreePath)) continue;
            if (await ProbeCheckoutIndexLockAsync(checkout, ct) is { } hit) return hit;
        }
        return null;
    }

    private async Task<(string Code, string Detail)?> ProbeCheckoutIndexLockAsync(string checkout, CancellationToken ct)
    {
        if (!Directory.Exists(checkout))
            return null;
        var observation = await _landingGit!.InspectIndexLockAsync(checkout, ct);
        if (!Directory.Exists(checkout))
            return null;
        return GitIndexLock.Refusal(observation, GitIndexLock.StaleAfter(_gitSettings?.IndexLockStaleAfterSeconds),
            _clock.GetUtcNow().UtcDateTime);
    }

    private static string FullRef(string branch) =>
        branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;

    private async Task<AgentTask?> FindWriterAsync(AgentTask task, string common, CancellationToken ct)
    {
        var candidates = await _db.AgentTasks.AsNoTracking().Where(t => t.Id != task.Id
            && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked))
            .Where(AgentTaskRoles.NotSpecialist).ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            var sourcePath = candidate.WorktreePath ?? candidate.WorkingDirectory;
            if (task.WorktreePath is not null && string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(task.WorktreePath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return candidate;
            if (task.WorktreePath is not null && Directory.Exists(task.WorktreePath) && Directory.Exists(sourcePath))
            {
                try
                {
                    if (string.Equals(await _landingGit!.CanonicalDirectoryAsync(task.WorktreePath, ct),
                        await _landingGit.CanonicalDirectoryAsync(sourcePath, ct),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return candidate;
                }
                catch (IOException) { return candidate; }
            }
            if (candidate.Workspace != WorkspaceMode.Shared) continue;
            try
            {
                if (string.Equals(await _landingGit!.CommonDirectoryAsync(candidate.RepoPath ?? candidate.WorkingDirectory, ct), common,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return candidate;
            }
            catch (IOException) { return candidate; } // An inaccessible active writer cannot prove disjoint ownership.
        }
        return null;
    }

    private async Task SettleLandedAsync(
        AgentTask task,
        AgentTaskEventType type,
        string outcome,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string>? unlandedSiblings,
        CancellationToken ct,
        string? cleanupDetail = null)
    {
        var expectedRequest = task.CurrentLandRequestId;
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(task.Id, ct);
        await _db.Entry(task).ReloadAsync(ct);
        if (task.CurrentLandRequestId != expectedRequest) return;
        var request = await EnsureRequestAsync(task, ct);
        await _db.Entry(request).ReloadAsync(ct);
        if (request.TerminalEventId is not null) return;
        await CompleteTerminalLockedAsync(task, request, type, outcome, warnings, unlandedSiblings, ct, cleanupDetail);
    }

    private static string AppendUnlandedMarker(string outcome, string? marker) =>
        marker is null ? outcome : $"{outcome}, {marker}";

    /// <summary>The landed-event marker for the sibling tokens; null when nothing is unlanded.</summary>
    internal static string? UnlandedMarker(IReadOnlyList<string> siblings) =>
        siblings.Count == 0 ? null : $"unlanded-sibling={string.Join(",", siblings)}";

    /// <summary>
    /// Same-card kept Worktree branches whose tip is not an ancestor of the rebased HEAD
    /// (CARD-0215). Warn on a surviving uncontained branch; absence grants no landing authority.
    /// Internal so the stage-outcome component rows call it as a typed seam (CARD-0567).
    /// </summary>
    internal async Task<(IReadOnlyList<string> Siblings, IReadOnlyList<string> Warnings)> CollectUnlandedSiblingsAsync(
        AgentTask task, string rebasedHeadRepo, CancellationToken ct, string? verifiedSha = null)
    {
        if (task.CardId is null || task.RepoPath is null || !Directory.Exists(rebasedHeadRepo))
            return ([], []);

        var siblings = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Id != task.Id
                && t.CardId == task.CardId
                && t.Workspace == WorkspaceMode.Worktree
                && (t.Status == AgentTaskStatus.Succeeded || t.Status == AgentTaskStatus.Blocked)
                && t.WorktreeBranch != null)
            .Select(t => new { t.Id, t.WorktreeBranch, t.RepoPath, t.WorktreePath })
            .ToListAsync(ct);
        if (siblings.Count == 0)
            return ([], []);

        var cardIdentifier = await _db.Cards.AsNoTracking()
            .Where(c => c.Id == task.CardId)
            .Select(c => c.Identifier)
            .FirstOrDefaultAsync(ct) ?? "the card";

        var tokens = new List<string>();
        var warnings = new List<string>();
        foreach (var sibling in siblings)
        {
            var branch = sibling.WorktreeBranch!;
            if (!DelegationWorktreeService.SharesRepo(task.RepoPath, sibling.RepoPath)
                && !DelegationWorktreeService.SharesRepo(task.RepoPath, sibling.WorktreePath))
                continue;
            if (!await _worktrees.KeptBranchExistsAsync(task.RepoPath, branch, ct))
                continue;
            if (await _worktrees.ContainsPatchesAsync(rebasedHeadRepo, branch, verifiedSha ?? "HEAD", ct))
                continue;

            var shortId = DelegationReportFormatter.Short(sibling.Id);
            tokens.Add($"{shortId}:{branch}");
            warnings.Add(
                $"{cardIdentifier}'s kept branch {branch} (task {shortId}) is not an ancestor of the rebased HEAD.");
        }

        return (tokens, warnings);
    }

    /// <summary>Pure form of the Shared-writer rule, kept visible for the lease contract tests.</summary>
    internal static bool IsHeldBehindSharedWriter(AgentTask landing, IEnumerable<AgentTask> candidates)
    {
        var key = ScopeResolver.KeyFor(landing.RepoPath, landing.WorkingDirectory);
        return candidates.Any(t => t.Id != landing.Id && t.Workspace == WorkspaceMode.Shared
            && !AgentTaskRoles.IsSpecialist(t.Role)
            && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
            && ScopeResolver.KeyFor(t.RepoPath, t.WorkingDirectory) == key);
    }

    /// <summary>
    /// Re-enqueue pending lands this process does not hold. Runs immediately at boot and every
    /// <see cref="DelegationSettings.LandSweepSeconds"/>.
    /// </summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        var stale = await _db.AgentTasks
            .Where(t => t.LandRequestedAt != null
                && t.Status != AgentTaskStatus.Succeeded
                && t.Status != AgentTaskStatus.Blocked)
            .ToListAsync(ct);
        foreach (var row in stale)
        {
            await using var canceled = await _db.Database.BeginTransactionAsync(ct);
            await LockTaskAsync(row.Id, ct);
            await _db.Entry(row).ReloadAsync(ct);
            if (row.LandRequestedAt is null || row.Status is AgentTaskStatus.Succeeded or AgentTaskStatus.Blocked) continue;
            var request = await EnsureRequestAsync(row, ct);
            await _db.Entry(request).ReloadAsync(ct);
            if (!request.IsPending) continue;
            request.State = LandRequestState.Canceled;
            request.IsPending = false;
            request.ReconciliationError = "task_no_longer_eligible";
            ClearPending(row);
            await _db.SaveChangesAsync(ct);
            await canceled.CommitAsync(ct);
            await ReleaseLandOwnerAsync(row.Id);
        }

        var pending = await _db.AgentTasks
            .Where(t => t.LandRequestedAt != null
                && t.Status == AgentTaskStatus.Succeeded
                && t.Workspace == WorkspaceMode.Worktree)
            .ToListAsync(ct);
        var maxAttempts = Math.Clamp(_settings.LandMaxAttempts, 1, 10);
        foreach (var row in pending)
        {
            if (_queue.IsActive(row.Id))
                continue;
            var request = await EnsureRequestAsync(row, ct);
            if (!request.IsPending || request.State == LandRequestState.NeedsResolution) continue;
            if (row.LandAttempt >= maxAttempts)
            {
                var last = row.LandStartedAt?.ToString("u") ?? "unknown";
                await RefuseAsync(row,
                    $"land interrupted {row.LandAttempt} times without finishing (last started {last}); not retried automatically — run -Land again",
                    ct);
                continue;
            }

            if (row.LandStartedAt is not null)
            {
                var expectedRequestId = row.CurrentLandRequestId;
                await using (var restart = await _db.Database.BeginTransactionAsync(ct))
                {
                    await LockTaskAsync(row.Id, ct);
                    await _db.Entry(row).ReloadAsync(ct);
                    if (row.CurrentLandRequestId == expectedRequestId && row.LandStartedAt is not null
                        && row.Status == AgentTaskStatus.Succeeded)
                    {
                        request = await GetRequestAsync(row, ct);
                        if (request is { IsPending: true })
                        {
                            await _db.Entry(request).ReloadAsync(ct);
                            if (request.IsPending)
                            {
                                _db.AgentTaskEvents.Add(Event(row.Id, AgentTaskEventType.Warning,
                                    $"Land attempt {row.LandAttempt} started {row.LandStartedAt:u} did not finish (server restarted); re-running.",
                                    _clock.GetUtcNow().UtcDateTime));
                                row.LandStartedAt = null;
                                row.ConcurrencyToken = Guid.NewGuid();
                                await _db.SaveChangesAsync(ct);
                                await restart.CommitAsync(ct);
                            }
                        }
                    }
                }
            }

            await _db.Entry(row).ReloadAsync(ct);
            if (row.LandRequestedAt is null || row.Status != AgentTaskStatus.Succeeded) continue;
            request = await GetRequestAsync(row, ct);
            if (request is null || !request.IsPending || request.State == LandRequestState.NeedsResolution) continue;
            _queue.TryEnqueue(row.Id, request.VerifyFilter, request.Id);
        }
    }

    /// <summary>
    /// Drain-side failure: <c>RunAsync</c> threw. Writes <c>LandRefused</c>, keeps the branch,
    /// clears the pending request. If this write itself throws the row stays pending for the sweep.
    /// </summary>
    public async Task FailAsync(Guid taskId, Exception exception, CancellationToken ct)
        => await FailRequestAsync(taskId, null, exception, ct);

    public async Task<LandFailureHandleResult> FailRequestAsync(Guid taskId, Guid? requestId, Exception exception, CancellationToken ct)
    {
        var diagnosticId = Guid.NewGuid();
        _db.ChangeTracker.Clear();
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return new(diagnosticId, LandFailureDiagnostic.Unexpected, LandFailureDiagnostic.ExceptionTypeName(exception), requestId ?? Guid.Empty, 0);
        var expectedRequest = _execution?.RequestId ?? requestId ?? task.CurrentLandRequestId;
        var expectedAttempt = _execution?.Attempt;
        if (expectedRequest is null)
        {
            if (task.LandRequestedAt is null)
                return new(diagnosticId, LandFailureDiagnostic.Unexpected, LandFailureDiagnostic.ExceptionTypeName(exception), Guid.Empty, 0);
            expectedRequest = (await EnsureRequestAsync(task, ct)).Id;
        }
        if (requestId is not null && requestId != expectedRequest)
            return new(diagnosticId, LandFailureDiagnostic.Unexpected, LandFailureDiagnostic.ExceptionTypeName(exception), expectedRequest.Value, expectedAttempt ?? 0);

        var code = LandFailureDiagnostic.Classify(exception);
        var typeName = LandFailureDiagnostic.ExceptionTypeName(exception);
        _logger.LogWarning(exception,
            "Land operation failed for task {TaskId} request {RequestId} attempt {Attempt} exception {ExceptionType} code {Code} diagnostic {DiagnosticId}",
            taskId, expectedRequest, expectedAttempt ?? 0, typeName, code, diagnosticId);
        try
        {
            await PersistFailureAsync(task, expectedRequest.Value, expectedAttempt, diagnosticId, exception, ct);
        }
        catch (Exception persistEx) when (persistEx is not OperationCanceledException)
        {
            throw new LandFailurePersistenceException(diagnosticId, persistEx);
        }
        return new(diagnosticId, code, typeName, expectedRequest.Value, expectedAttempt ?? 0);
    }

    private async Task PersistFailureAsync(AgentTask task, Guid expectedRequest, int? expectedAttempt,
        Guid diagnosticId, Exception exception, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(task.Id, ct);
        await _db.Entry(task).ReloadAsync(ct);
        if (task.CurrentLandRequestId != expectedRequest) return;
        var request = await GetRequestAsync(task, ct);
        if (request is null) return;
        await _db.Entry(request).ReloadAsync(ct);
        if (request.Id != expectedRequest) return;
        if (expectedAttempt is int attempt && request.Attempt != attempt) return;
        if (request.TerminalEventId is not null) return;
        if (task.LandRequestedAt is null && task.ActiveLandingId is null) return;

        var op = task.ActiveLandingId is Guid id
            ? await _db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == id, ct) : null;
        var published = op is not null && new AgentTaskLandingState().HasPublication(op);
        var code = LandFailureDiagnostic.Classify(exception, published);
        var typeName = LandFailureDiagnostic.ExceptionTypeName(exception);
        request.TerminalFailureCode = code;
        request.FailureDiagnosticId = diagnosticId;
        request.FailureExceptionType = typeName;

        if (published)
        {
            op!.LastReason = LandFailureDiagnostic.InterruptedAfterPublication;
            if (op.Cleanup != LandCleanupStatus.Complete) op.Cleanup = LandCleanupStatus.Pending;
            await CompleteTerminalLockedAsync(task, request, TerminalType(op), FormatOutcome(op), [], null, ct);
            return;
        }

        if (task.LandRequestedAt is null) return;
        var line = LandFailureDiagnostic.FormatUnconfirmed(code, diagnosticId, typeName, request);
        await CompleteTerminalLockedAsync(task, request, AgentTaskEventType.LandRefused, line,
            [$"Landing not confirmed; no cleanup authorized by this result. {code}"], null, ct);
    }

    private async Task RefuseAsync(AgentTask task, string detail, CancellationToken ct) =>
        await PersistRefusalAsync(task, $"land refused: {detail}", detail, ct);

    private async Task PersistRefusalAsync(AgentTask task, string line, string warningDetail, CancellationToken ct)
    {
        var expectedRequest = task.CurrentLandRequestId;
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(task.Id, ct);
        await _db.Entry(task).ReloadAsync(ct);
        if (task.CurrentLandRequestId != expectedRequest) return;
        var request = await EnsureRequestAsync(task, ct);
        await _db.Entry(request).ReloadAsync(ct);
        if (request.TerminalEventId is not null) return;
        await CompleteTerminalLockedAsync(task, request, AgentTaskEventType.LandRefused, line,
            [$"Landing not confirmed; no cleanup authorized by this result. {warningDetail}"], null, ct);
    }

    private async Task CompleteTerminalLockedAsync(AgentTask task, AgentTaskLandRequest request,
        AgentTaskEventType type, string outcome, IReadOnlyList<string> warnings, IReadOnlyList<string>? unlandedSiblings,
        CancellationToken ct, string? cleanupDetail = null)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var warning in warnings)
            _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.Warning, warning, now));
        var opId = request.LandingOperationId ?? task.ActiveLandingId;
        var op = opId is Guid bound
            ? await _db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == bound, ct)
            : null;
        if (op is not null && type is AgentTaskEventType.Landed or AgentTaskEventType.LandedWithResidue
            or AgentTaskEventType.AlreadyPresent)
        {
            var alreadyReported = await _db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id
                && e.LandingOperationId == op.Id && (e.Type == AgentTaskEventType.Landed
                    || e.Type == AgentTaskEventType.LandedWithResidue || e.Type == AgentTaskEventType.AlreadyPresent), ct);
            if (alreadyReported) type = AgentTaskEventType.LandingCleanup;
        }
        WorktreeCleanupAttempt? cleanupAttempt = null;
        WorktreeCleanupReference? notificationCapture = null;
        if (op is not null && _protocol is not null)
        {
            var cleanupEvidence = await _protocol.ReadCleanupEvidenceAsync(op.Id, request.Id, ct);
            if (cleanupEvidence.CurrentAttemptId is Guid attemptId)
            {
                cleanupAttempt = await _db.WorktreeCleanupAttempts.SingleAsync(a => a.Id == attemptId, ct);
                if (cleanupAttempt.CaptureState == WorktreeCleanupCaptureState.Pending
                    || cleanupAttempt.InitialCommandId is not null && cleanupAttempt.InitialCompletedAt is null
                        && cleanupAttempt.CaptureState == WorktreeCleanupCaptureState.NotNeeded)
                {
                    cleanupAttempt.CaptureState = WorktreeCleanupCaptureState.Interrupted;
                    cleanupAttempt.Summary = $"capture={cleanupAttempt.Id:N} at {cleanupAttempt.CaptureAt ?? cleanupAttempt.InitialIntentAt:O}; Interrupted";
                    cleanupEvidence = cleanupEvidence with { Capture = new WorktreeCleanupPresentation().Reference(cleanupAttempt) };
                }
            }
            var detail = new WorktreeCleanupPresentation().Detail(op.LastReason ?? "cleanup complete", cleanupEvidence.Capture);
            if (cleanupEvidence.Capture is not null)
            {
                outcome += "; " + detail;
                notificationCapture = cleanupEvidence.Capture;
            }
            if (op.CleanupStartedAt is not null && new AgentTaskLandingState().HasPublication(op))
            {
                var stage = _db.StageOutcomes.Local.LastOrDefault(s => s.SubjectTaskId == task.Id
                    && s.Stage == OrchestrationStage.Cleanup && _db.Entry(s).State == EntityState.Added);
                // CARD-0665 D-8: the stage keeps the protected paths / retained count; the outcome
                // line already carries them from the protocol result.
                var stageDetail = cleanupDetail is null ? detail : new WorktreeCleanupPresentation().Detail(
                    AppendDetail(op.LastReason ?? "cleanup complete", cleanupDetail), cleanupEvidence.Capture);
                if (stage is null) Record(task, OrchestrationStage.Cleanup,
                    op.Cleanup == LandCleanupStatus.Complete ? StageOutcomeKind.Clean : StageOutcomeKind.Failed,
                    DurationSeconds(op, OrchestrationStage.Cleanup), stageDetail);
                else stage.Detail = stageDetail;
            }
        }
        var terminal = Event(task.Id, type, outcome, now);
        if (cleanupAttempt is not null && op is not null)
        {
            cleanupAttempt.DirectoryGone = op.DirectoryRemoved;
            cleanupAttempt.Unregistered = op.RegistrationRemoved;
            cleanupAttempt.BranchDeleted = op.BranchRemoved;
            cleanupAttempt.Residue = op.LastReason;
            cleanupAttempt.FinalizedAt = now;
            cleanupAttempt.TerminalEventId = terminal.Id;
            cleanupAttempt.ConcurrencyToken = Guid.NewGuid();
        }
        SetLandingEvidence(terminal, op);
        _db.AgentTaskEvents.Add(terminal);
        CompleteRequest(task, request, terminal);
        AddNotification(task, request, terminal, LandNotificationKind.Outcome, notificationCapture, unlandedSiblings,
            AppendDetail(AppendDetail(op?.LastReason ?? "cleanup complete",
                op?.CanonicalAdvanceReason is { } canonicalReason ? $"canonical={canonicalReason}" : null), cleanupDetail));
        ClearPending(task);
        await _db.SaveChangesAsync(ct);
        if (_db.Database.CurrentTransaction is { } open) await open.CommitAsync(ct);
        await ReleaseLandOwnerAsync(task.Id);
        await _boundary.ReachedAsync("terminal-committed", task.Id, terminal.Id, ct);
        await PublishAsync(task, ct);
    }

    /// <summary>
    /// CARD-0664 D-4: after a ClearPending commit no pending land keeps the owner live, so its
    /// ended-owner Launch rows go back. Best-effort; the outcome is already committed.
    /// </summary>
    private Task ReleaseLandOwnerAsync(Guid taskId) =>
        _workspaceUse?.ReleaseTaskConsumersAsync(taskId, CancellationToken.None) ?? Task.CompletedTask;

    private static void ClearPending(AgentTask task)
    {
        task.LandRequestedAt = null;
        task.LandVerifyFilter = null;
        task.LandStartedAt = null;
        task.ConcurrencyToken = Guid.NewGuid();
    }

    private static void SetLandingEvidence(AgentTaskEvent terminal, AgentTaskLanding? op)
    {
        terminal.LandingOperationId = op?.Id;
        terminal.LandingPublication = op?.Publication;
        terminal.LandingCleanup = op?.Cleanup;
        terminal.LandingMode = op?.Mode;
    }

    /// <summary>CARD-0688 D-11: per-phase wall seconds from the operation's own timestamps; "-" when a phase did not run.</summary>
    internal static (string Reset, string Rebase, string Verify, string Push, string Canonical, string Cleanup) PhaseSeconds(
        AgentTaskLanding? op, double? resetSeconds = null)
    {
        static string Span(DateTime? start, DateTime? end) => start is { } s && end is { } e
            ? Math.Round(Math.Max(0, (e - s).TotalSeconds), 2).ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
        if (op is null) return (resetSeconds is null ? "-" : Math.Round(resetSeconds.Value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture), "-", "-", "-", "-", "-");
        // The reset is timed in-process (RebaseStartedAt keeps meaning rebase intent, after the reset).
        var reset = resetSeconds is { } r ? Math.Round(r, 2).ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
        return (reset, Span(op.RebaseStartedAt, op.PreparedAt), Span(op.VerificationStartedAt, op.VerifiedAt),
            Span(op.PushStartedAt, op.RemoteConfirmedAt), Span(op.CanonicalAdvanceStartedAt, op.CanonicalAdvancedAt ?? (op.CanonicalAdvanceReason is null ? null : op.CleanupStartedAt)),
            Span(op.CleanupStartedAt, op.CleanupCompletedAt));
    }

    internal static int DurationSeconds(AgentTaskLanding? op, OrchestrationStage stage)
    {
        if (op is null) return 0;
        var (start, end) = stage switch
        {
            OrchestrationStage.Rebase => (op.RebaseStartedAt, op.PreparedAt ?? op.UpdatedAt),
            OrchestrationStage.Verify => (op.VerificationStartedAt, op.VerifiedAt ?? op.UpdatedAt),
            OrchestrationStage.Cleanup => (op.CleanupStartedAt, op.CleanupCompletedAt ?? op.UpdatedAt),
            _ => ((DateTime?)null, op.UpdatedAt),
        };
        return start is null ? 0 : (int)Math.Clamp((end - start.Value).TotalSeconds, 0, int.MaxValue);
    }

    private static string? ClipFilter(string? verifyFilter)
    {
        if (string.IsNullOrWhiteSpace(verifyFilter))
            return null;
        var trimmed = verifyFilter.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400];
    }

    private Task LockTaskAsync(Guid id, CancellationToken ct) =>
        _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {id} FOR UPDATE", ct);

    private async Task<AgentTaskLandRequest?> GetRequestAsync(AgentTask task, CancellationToken ct) =>
        task.CurrentLandRequestId is Guid id ? await _db.AgentTaskLandRequests.SingleAsync(r => r.Id == id, ct) : null;

    private static AgentTaskLandRequest NewRequest(AgentTask task, DateTime at, string? filter,
        string? expectedSha = null, Guid? evidenceId = null, LandApprovalKind kind = LandApprovalKind.ExplicitCaller) => new()
    {
        Id = Guid.NewGuid(), TaskId = task.Id, RequestedAt = at, VerifyFilter = filter,
        ReplyTo = task.ReplyTo, ParentSessionId = task.ParentSessionId, State = LandRequestState.Queued,
        LastEvaluatedAt = at, LastProgressAt = at,
        SchemaVersion = expectedSha is null ? 1 : 2,
        ExpectedSourceSha = expectedSha,
        ReviewEvidenceId = evidenceId,
        ApprovalKind = kind,
        ApprovedAt = expectedSha is null ? null : at,
        SourceFullRefSnapshot = task.WorktreeBranch is null ? null
            : task.WorktreeBranch.StartsWith("refs/", StringComparison.Ordinal) ? task.WorktreeBranch : "refs/heads/" + task.WorktreeBranch,
        RepositoryPathSnapshot = task.RepoPath,
        WorktreePathSnapshot = task.WorktreePath,
        TargetFullRefSnapshot = task.MergeTargetRef is null ? "refs/heads/master"
            : task.MergeTargetRef.StartsWith("refs/", StringComparison.Ordinal) ? task.MergeTargetRef : "refs/heads/" + task.MergeTargetRef,
    };

    private static string ApprovalRequestedDetail(string? filter, string? expected, Guid? evidence)
    {
        var bits = new List<string>();
        if (expected is not null) bits.Add($"expected={expected}");
        if (evidence is { } id) bits.Add($"evidence={id:N}");
        if (filter is not null) bits.Add($"filter={filter}");
        return bits.Count == 0 ? "Land requested." : "Land requested: " + string.Join("; ", bits);
    }

    private async Task<AgentTaskLandRequest> EnsureRequestAsync(AgentTask task, CancellationToken ct)
    {
        var request = await GetRequestAsync(task, ct);
        if (request is not null) return request;
        request = NewRequest(task, task.LandRequestedAt ?? _clock.GetUtcNow().UtcDateTime, task.LandVerifyFilter);
        request.StartedAt = request.LastAttemptAt = task.LandStartedAt;
        request.Attempt = task.LandAttempt;
        _db.AgentTaskLandRequests.Add(request);
        task.CurrentLandRequestId = request.Id;
        await _db.SaveChangesAsync(ct);
        return request;
    }

    /// <summary>
    /// CARD-0672 D-2. Null means acquire as usual. A land yields only BEFORE it holds the lease: a
    /// land's phases share one continuous lease (the read cache, the child journal and CARD-0688
    /// all assume it), so admission is the one place the order can change. The sweep re-picks a
    /// yielded request every LandSweepSeconds; the dispatcher's tick takes the lease in between.
    /// </summary>
    private async Task<LandRunResult?> YieldToDispatchAsync(AgentTask task, AgentTaskLandRequest request, CancellationToken ct)
    {
        if (_leaseWaiters is null)
            return null;
        var max = _settings.LandYieldToDispatchMaxSeconds;
        if (max <= 0 || _leaseWaiters.IsEmpty)
        {
            _leaseWaiters.EndYield(request.Id);
            return null;
        }

        IReadOnlyList<RepositoryLeaseWaiter> waiters;
        try
        {
            waiters = _leaseWaiters.Snapshot(await _landingGit!.CommonDirectoryAsync(task.RepoPath!, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // The acquire below resolves the same directory and reports its own failure.
            return null;
        }

        if (waiters.Count == 0)
        {
            _leaseWaiters.EndYield(request.Id);
            return null;
        }

        var now = _clock.GetUtcNow();
        var first = _leaseWaiters.FirstYield(request.Id, now);
        if (now - first >= TimeSpan.FromSeconds(max))
        {
            if (_leaseWaiters.MarkExhausted(request.Id))
            {
                var waited = (int)(now - first).TotalSeconds;
                var warning = Event(task.Id, AgentTaskEventType.Warning,
                    $"Land yield budget exhausted after {waited}s; proceeding with {waiters.Count} queued dispatch(es) still waiting "
                    + "(Delegation:LandYieldToDispatchMaxSeconds).", now.UtcDateTime);
                warning.LandRequestId = request.Id;
                // Under the task lock with a fresh request row, so the event alone is written.
                await using var warned = await _db.Database.BeginTransactionAsync(ct);
                await LockTaskAsync(task.Id, ct);
                await _db.Entry(request).ReloadAsync(ct);
                _db.AgentTaskEvents.Add(warning);
                await _db.SaveChangesAsync(ct);
                await warned.CommitAsync(ct);
                _logger.LogWarning(
                    "Land of task {TaskId} request {RequestId} stops yielding after {Seconds}s with {Waiters} lease waiter(s)",
                    task.Id, request.Id, waited, waiters.Count);
            }

            return null;
        }

        var shorts = string.Join(", ", waiters.Take(8).Select(w => DelegationReportFormatter.Short(w.TaskId)));
        var purposes = string.Join(", ", waiters.Select(w => w.Purpose).Distinct(StringComparer.Ordinal));
        var detail = $"Land yields the repository mutation lease to {waiters.Count} queued dispatch(es): {shorts} ({purposes}); "
            + "resumes within Delegation:LandSweepSeconds.";
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(task.Id, ct);
        await _db.Entry(request).ReloadAsync(ct);
        if (!request.IsPending)
            return LandRunResult.Complete;
        var at = now.UtcDateTime;
        request.LastEvaluatedAt = at;
        // One Held event per episode, deduplicated on the reason as HoldAsync is. No caller note:
        // a yield resolves itself within a sweep (the CARD-0641 note is for holds that need one).
        var changed = request.State != LandRequestState.Held || request.HoldReasonCode != LeaseYieldedToDispatchCode;
        request.State = LandRequestState.Held;
        request.HoldReasonCode = LeaseYieldedToDispatchCode;
        request.HoldDetail = detail.Length <= 2000 ? detail : detail[..2000];
        request.HoldingTaskId = null;
        request.HoldingTaskStatus = null;
        if (changed)
        {
            request.HeldSince = first.UtcDateTime;
            request.HoldEpisode++;
            var held = Event(task.Id, AgentTaskEventType.Held, request.HoldDetail, at);
            if (task.ActiveLandingId is Guid operationId)
                SetLandingEvidence(held, await _db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == operationId, ct));
            held.LandRequestId = request.Id;
            _db.AgentTaskEvents.Add(held);
        }

        request.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (changed)
            await PublishAsync(task, ct);
        return LandRunResult.Held;
    }

    private async Task HoldOnBusyLeaseAsync(AgentTask task, AgentTaskLandRequest request, CancellationToken ct)
    {
        var observed = await _leases!.FindOwnerAsync(task.RepoPath!, ct);
        if (observed?.State == RepositoryLeaseOwnerState.Known)
        {
            var owner = observed.TaskId is Guid ownerId
                ? await _db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == ownerId, ct)
                : null;
            var who = observed.TaskId is Guid id ? id.ToString("N") : "none";
            var status = owner?.Status.ToString() ?? "unknown";
            // The tagged id decides note ownership even when its task row is missing.
            await HoldAsync(task, request, "repository_mutation_lease_busy", owner,
                $"Repository mutation lease is occupied by task {who} ({observed.Purpose}); status={status}.", ct,
                observed.TaskId);
            return;
        }

        if (observed?.State == RepositoryLeaseOwnerState.Untagged)
        {
            await HoldAsync(task, request, "repository_mutation_lease_busy", null,
                "Repository mutation lease is occupied; owner untagged in-process.", ct);
            return;
        }

        if (observed is null)
        {
            await HoldAsync(task, request, "repository_mutation_lease_busy", null,
                "Repository mutation lease is occupied; owner unknown.", ct);
            return;
        }

        AgentTask? writer = null;
        try
        {
            var common = await _landingGit!.CommonDirectoryAsync(task.RepoPath!, ct);
            writer = await FindWriterAsync(task, common, ct);
        }
        catch (IOException) { }

        var keepKnown = request.HoldReasonCode == "repository_mutation_lease_busy" && request.HoldingTaskId is Guid;
        AgentTask? preserved = null;
        if (keepKnown)
        {
            preserved = await _db.AgentTasks.AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == request.HoldingTaskId, ct);
            preserved ??= new AgentTask
            {
                Id = request.HoldingTaskId!.Value,
                Status = request.HoldingTaskStatus ?? AgentTaskStatus.Working,
            };
        }

        var detail = "Repository mutation lease is occupied; owner unknown.";
        if (keepKnown)
            detail += $" last-known lease owner={request.HoldingTaskId:N}.";
        if (writer is not null)
            detail += $" admission writer {writer.Id:N} ({writer.Status}).";
        await HoldAsync(task, request, "repository_mutation_lease_busy", preserved, detail, ct);
    }

    private async Task HoldAsync(AgentTask task, AgentTaskLandRequest request, string reason,
        AgentTask? holder, string detail, CancellationToken ct, Guid? ownerTaskId = null)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _boundary.ReachedAsync("hold-before-lock", task.Id, request.Id, ct);
        await LockTaskAsync(task.Id, ct);
        await _db.Entry(request).ReloadAsync(ct);
        if (!request.IsPending) return;
        var now = _clock.GetUtcNow().UtcDateTime;
        request.LastEvaluatedAt = now;
        // The note owner is the identified task even when its row is gone (a tagged lease names it).
        var owner = ownerTaskId ?? holder?.Id;
        var observedOwner = owner is Guid ownerId ? HoldNotificationOwnerKey(ownerId) : UnknownHoldNotificationOwner;
        var changed = request.State != LandRequestState.Held || request.HoldReasonCode != reason
            || request.HoldingTaskId != holder?.Id
            || owner is not null && request.HoldNotificationOwnerKey is { } anchor && anchor != observedOwner;
        // Unconditional: the first post-upgrade evaluation adopts existing Held debt even when the
        // diagnostics are unchanged, so a later takeover compares against a real anchor.
        var notify = await AdvanceHoldNotificationOwnerAsync(request, observedOwner, ct);
        request.State = LandRequestState.Held;
        request.HoldReasonCode = reason;
        request.HoldDetail = detail.Length <= 2000 ? detail : detail[..2000];
        request.HoldingTaskId = holder?.Id;
        request.HoldingTaskStatus = holder?.Status;
        if (changed)
        {
            request.HeldSince = now;
            request.HoldEpisode++;
            var held = Event(task.Id, AgentTaskEventType.Held, request.HoldDetail, now);
            if (task.ActiveLandingId is Guid operationId)
                SetLandingEvidence(held, await _db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == operationId, ct));
            held.LandRequestId = request.Id;
            _db.AgentTaskEvents.Add(held);
            if (notify)
                AddNotification(task, request, held, LandNotificationKind.Held);
        }
        request.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await PublishAsync(task, ct);
    }

    internal const string UnknownHoldNotificationOwner = "unknown";

    internal static string HoldNotificationOwnerKey(Guid taskId) => $"task:{taskId:N}";

    // CARD-0641 D-5: caller notes deduplicate per request and stable holder; the Held event above
    // stays per episode. Runs inside HoldAsync's task-locked transaction so anchor and note commit
    // or roll back together. Unknown never replaces or refines into a new holder. A true result on an
    // unchanged evaluation mints nothing: that case is only the silent adoption of a null anchor.
    private async Task<bool> AdvanceHoldNotificationOwnerAsync(AgentTaskLandRequest request, string observed, CancellationToken ct)
    {
        var anchor = request.HoldNotificationOwnerKey;
        if (anchor is null)
        {
            // Pre-upgrade Held notes for this request are existing debt: adopt, never replay.
            var existing = await _db.AgentTaskLandNotifications.AsNoTracking()
                .AnyAsync(n => n.RequestId == request.Id && n.Kind == LandNotificationKind.Held, ct);
            request.HoldNotificationOwnerKey = observed;
            return !existing;
        }

        if (observed == UnknownHoldNotificationOwner || observed == anchor)
            return false;
        request.HoldNotificationOwnerKey = observed;
        return anchor != UnknownHoldNotificationOwner;
    }

    private static void CompleteRequest(AgentTask task, AgentTaskLandRequest request, AgentTaskEvent terminal)
    {
        terminal.LandRequestId = request.Id;
        terminal.IsLandTerminal = true;
        request.TerminalEventId = terminal.Id;
        request.LandingOperationId = terminal.LandingOperationId;
        request.State = LandRequestState.Completed;
        request.IsPending = false;
        request.HoldReasonCode = request.HoldDetail = null;
        request.HoldingTaskId = null;
        request.HoldingTaskStatus = null;
        request.HeldSince = null;
        request.ConcurrencyToken = Guid.NewGuid();
    }

    private void AddNotification(AgentTask task, AgentTaskLandRequest request, AgentTaskEvent source, LandNotificationKind kind,
        WorktreeCleanupReference? cleanupCapture = null, IReadOnlyList<string>? unlandedSiblings = null, string? cleanupReason = null)
        => _db.AgentTaskLandNotifications.Add(LandNotificationPayload.Create(request, source, kind, cleanupCapture, unlandedSiblings, cleanupReason));

    internal static async Task<LandVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
        => await VerifyWithObserverAsync(worktree, filter, null, ct);

    /// <summary>Starts one verifier child and waits for it; the seam V-8 fakes so nothing is built.</summary>
    internal delegate Task<ProcessResult> LandProcessRunner(string cwd, ILandingChildObserver? observer, CancellationToken ct, string file, string[] args);

    internal static async Task<LandVerification> VerifyWithObserverAsync(string worktree, string? filter,
        ILandingChildObserver? observer, CancellationToken ct, IBuildSlotGate? buildSlots = null, LandProcessRunner? runProcess = null, string? artifactsPath = null)
    {
        runProcess ??= (cwd, obs, token, file, args) => RunProcessAsync(cwd, obs, token, file, args);
        // CARD-0589 S4: the build and the test run hold one host build slot, like a delegate's
        // checkpoint row. No gate (a verifier built outside DI) keeps the unbudgeted behaviour; an
        // unreachable runner builds unleased at -maxcpucount:4 and is never a land failure.
        BuildSlotHold? slot = null;
        void Report(string line)
        {
            try { observer?.Line(line); } catch (Exception) { /* Secondary observation only. */ }
        }
        if (buildSlots is not null)
        {
            var name = Path.GetFileName(worktree.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            slot = await buildSlots.AcquireAsync("land-verify " + name, Report, ct);
            if (slot.Outcome == BuildSlotHoldOutcome.Timeout)
                return LandVerification.Failure("build-slot", $"build_slot_timeout position={slot.QueuePosition}");
        }
        try
        {
            return await VerifyUnderSlotAsync(worktree, filter, observer, ct, slot, runProcess, artifactsPath);
        }
        finally
        {
            if (slot is not null)
                await buildSlots!.ReleaseAsync(slot, Report, CancellationToken.None);
        }
    }

    private static async Task<LandVerification> VerifyUnderSlotAsync(string worktree, string? filter,
        ILandingChildObserver? observer, CancellationToken ct, BuildSlotHold? slot, LandProcessRunner runProcess, string? artifactsPath)
    {
        // SDK artifacts isolate both bin and obj by project, outside the source checkout.
        // Unique owned outputs are retained; never recursively erase pre-existing bin-* paths.
        // CARD-0688 D-9: a caller-supplied path is plumbing for R3; R1 always passes null.
        var output = artifactsPath ?? Path.Combine(Path.GetTempPath(), "antiphon-land-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        string[] buildArgs = slot is { MaxCpuCount: > 0 }
            ? ["build", "--artifacts-path", output, "-maxcpucount:" + slot.MaxCpuCount]
            : ["build", "--artifacts-path", output];
        var build = await runProcess(worktree, observer, ct, "dotnet", buildArgs);
        if (!build.Ok) return LandVerification.Failure("build", Tail(build));
        if (string.IsNullOrWhiteSpace(filter)) return LandVerification.Success("build OK");
        // Reuse the capped build's artifacts; dotnet run's implicit build would run uncapped.
        var tests = await runProcess(worktree, observer, ct, "dotnet", ["run", "--project", "tests/Antiphon.Tests",
            "--no-build", "--artifacts-path", output, "--", "--treenode-filter", filter, "--report-trx",
            "--report-trx-filename", "landing-verification.trx"]);
        if (!tests.Ok) return LandVerification.Failure("tests", Tail(tests));
        var reports = Directory.GetFiles(output, "landing-verification.trx", SearchOption.AllDirectories);
        if (reports.Length != 1) return LandVerification.Failure("tests", "fresh_test_report_missing_or_ambiguous");
        var report = System.Xml.Linq.XDocument.Load(reports[0]);
        var counters = report.Descendants().SingleOrDefault(e => e.Name.LocalName == "Counters");
        if (!HasPassingTestCounters(counters, out var executed))
            return LandVerification.Failure("tests", "no_confirmed_passing_tests");
        return LandVerification.Success($"build OK, tests {executed}/{executed}");
    }

    // Keep report interpretation independently testable from the child exit-code guard.
    internal static bool HasPassingTestCounters(System.Xml.Linq.XElement? counters, out int executed)
        => int.TryParse(counters?.Attribute("executed")?.Value, out executed) && executed != 0
            && counters?.Attribute("passed")?.Value == executed.ToString()
            && counters?.Attribute("failed")?.Value == "0";

    private static async Task<ProcessResult> RunProcessAsync(string cwd, ILandingChildObserver? observer, CancellationToken ct, string file, params string[] args)
    {
        var start = new ProcessStartInfo { FileName = file, WorkingDirectory = cwd, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (observer is not null) await observer.BeforeStartAsync(ct);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {file}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            if (observer is not null) await observer.StartedAsync(process.Id, process.StartTime.ToUniversalTime().Ticks, ct);
            await process.WaitForExitAsync(ct);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            if (observer is not null) await observer.ExitedAsync(CancellationToken.None);
            throw;
        }
        await Task.WhenAll(stdout, stderr);
        if (observer is not null) await observer.ExitedAsync(CancellationToken.None);
        try { observer?.Completed(); } catch (Exception) { /* Secondary observation only. */ }
        return new ProcessResult(process.ExitCode == 0, await stdout, await stderr);
    }

    private static string Tail(ProcessResult result)
    {
        var text = (result.StdOut + "\n" + result.StdErr).Trim();
        return text.Length <= 1800 ? text : text[^1800..];
    }

    private void Record(
        AgentTask task,
        OrchestrationStage stage,
        StageOutcomeKind outcome,
        int durationSeconds,
        string detail = "",
        string? @ref = null)
    {
        _db.StageOutcomes.Add(new StageOutcome
        {
            Id = Guid.NewGuid(),
            Stage = stage,
            Outcome = outcome,
            Source = StageOutcomeSource.Server,
            SubjectTaskId = task.Id,
            CardId = task.CardId,
            DurationSeconds = durationSeconds,
            Detail = Clip(detail),
            Ref = @ref,
            RecordedAt = _clock.GetUtcNow().UtcDateTime,
        });
    }

    internal static string Clip(string detail) =>
        detail.Length <= StageOutcome.DetailMaxLength
            ? detail
            : detail[..StageOutcome.DetailMaxLength];

    private Task PublishAsync(AgentTask task, CancellationToken ct) =>
        _eventBus.PublishToAllAsync("AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);

    private static AgentTaskEvent Event(Guid taskId, AgentTaskEventType type, string detail, DateTime at) =>
        new() { Id = Guid.NewGuid(), AgentTaskId = taskId, Type = type, Detail = detail, At = at };

    internal sealed record ProcessResult(bool Ok, string StdOut, string StdErr);
}

internal sealed record LandVerification(bool Ok, string Step, string Tail, string Description)
{
    public static LandVerification Success(string description) => new(true, string.Empty, string.Empty, description);
    public static LandVerification Failure(string step, string tail) => new(false, step, tail, string.Empty);
}

public sealed record LandRequestResult(Guid TaskId, string Status, Guid RequestId = default, string Notification = "tracked");
public enum LandRunResult { Complete, Held }
internal sealed record LandExecutionIdentity(Guid RequestId, int Attempt);
public sealed record LandFailureHandleResult(Guid DiagnosticId, string Code, string? ExceptionType, Guid RequestId, int Attempt);
