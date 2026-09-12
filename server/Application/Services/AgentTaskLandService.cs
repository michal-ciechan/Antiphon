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
        LandDeliveryBoundary? boundary = null)
    {
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
    }

    /// <summary>Persist and queue an explicit land request. The endpoint returns before git runs.</summary>
    public async Task<LandRequestResult> RequestAsync(Guid taskId, LandAgentTaskRequest body, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {taskId} FOR UPDATE", ct);
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId.ToString());
        await _db.Entry(task).ReloadAsync(ct);
        if (task.Workspace != WorkspaceMode.Worktree)
            throw new ConflictException("Only a Worktree task can be landed.");
        if (task.Status != AgentTaskStatus.Succeeded)
            throw new ConflictException($"Task {DelegationReportFormatter.Short(task.Id)} must have succeeded before it can land.");

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
        if (!_boundary.DropWakeup("land-request", request.Id)) _queue.TryEnqueue(taskId, request.VerifyFilter, request.Id);
        await PublishAsync(task, ct);
        return new LandRequestResult(task.Id, pending ? "requeued" : "queued", request.Id,
            request.ReplyTo == AgentTaskReplyTo.None ? "not-required" : "tracked");
    }

    /// <summary>Run one background land request. A Shared-writer hold leaves the request pending.</summary>
    public async Task<LandRunResult> RunAsync(Guid taskId, string? verifyFilter, CancellationToken ct)
        => await RunRequestAsync(taskId, null, verifyFilter, ct);

    public async Task<LandRunResult> RunRequestAsync(Guid taskId, Guid? requestId, string? verifyFilter, CancellationToken ct)
    {
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return LandRunResult.Complete;
        if (task.LandRequestedAt is null)
            return LandRunResult.Complete;
        if (requestId is not null && task.CurrentLandRequestId != requestId)
            return LandRunResult.Complete;
        var request = await EnsureRequestAsync(task, ct);
        if (!request.IsPending) return LandRunResult.Complete;
        await _boundary.ReachedAsync("before-execution", taskId, request.Id, ct);
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
            return LandRunResult.Complete;
        }

        if (_protocol is null || _leases is null || _landingGit is null || task.RepoPath is null)
        {
            await RefuseAsync(task, "landing_protocol_unavailable", ct);
            return LandRunResult.Complete;
        }
        await using var lease = await _leases.TryAcquireAsync(task.RepoPath, ct);
        if (lease is null)
        {
            await HoldAsync(task, request, "repository_mutation_lease_busy", null,
                "Repository mutation lease is occupied; owner unknown.", ct);
            return LandRunResult.Held;
        }
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
        await _db.Entry(task).ReloadAsync(ct);
        await _db.Entry(request).ReloadAsync(ct);
        var active = task.ActiveLandingId is Guid operationId
            ? await _db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == operationId, ct)
            : null;
        var published = active is not null && new AgentTaskLandingState().HasPublication(active);
        // Resume unpublished work in the protocol. Source resolution already ran when the
        // operation was created; re-resolving can inspect/FF a live rebase or skip publication
        // coordinate checks.
        if (!published && (active is null || active.Phase == LandPhase.Refused))
        {
            var canResolve = request.SchemaVersion == 2 && GitObjectId.IsFull(request.ExpectedSourceSha);
            if (!canResolve && active is not { Phase: LandPhase.Refused })
            {
                if (request.SourceRefusalReason is null)
                {
                    request.SourceRefusalReason = "legacy_review_binding_required";
                    request.ConcurrencyToken = Guid.NewGuid();
                    await _db.SaveChangesAsync(ct);
                }
                await RefuseAsync(task, FormatSourceRefusal(request, "legacy_review_binding_required"), ct);
                return LandRunResult.Complete;
            }
            if (canResolve)
            {
                var resolved = await new AgentTaskLandSourceResolver(_db, _landingGit, _leases, _clock)
                    .ResolveAsync(task, request, lease, ct);
                if (resolved.Reason is not null)
                {
                    if (request.SourceRefusalReason is null)
                    {
                        request.SourceRefusalReason = resolved.Reason;
                        request.ConcurrencyToken = Guid.NewGuid();
                        await _db.SaveChangesAsync(ct);
                    }
                    await RefuseAsync(task, FormatSourceRefusal(request, resolved.Reason), ct);
                    return LandRunResult.Complete;
                }
                await _db.Entry(task).ReloadAsync(ct);
                await _db.Entry(request).ReloadAsync(ct);
            }
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
            await RefuseAsync(task, result.Reason ?? "publication_unconfirmed", ct);
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
            DurationSeconds(op, OrchestrationStage.Cleanup), result.Reason ?? "cleanup complete");
        var type = op.Publication == LandPublicationOutcome.AlreadyPresent ? AgentTaskEventType.AlreadyPresent
            : op.Cleanup == LandCleanupStatus.Complete ? AgentTaskEventType.Landed : AgentTaskEventType.LandedWithResidue;
        var (marker, warnings) = await CollectUnlandedSiblingsAsync(task, op.RepositoryPath, ct, op.VerifiedSourceSha);
        await SettleLandedAsync(task, type, AppendUnlandedMarker(FormatOutcome(op), marker), warnings, ct);
        return LandRunResult.Complete;
    }

    internal static string FormatOutcome(AgentTaskLanding op) =>
        $"{(op.Publication == LandPublicationOutcome.AlreadyPresent ? "already present" : "landed")} operation={op.Id:N} mode={op.Mode} "
        + $"source={op.OriginalSourceSha} reviewed={op.ReviewedSourceSha ?? op.OriginalSourceSha} verified={op.VerifiedSourceSha} -> {op.RemoteName}:{op.DestinationFullRef}; "
        + $"remote={op.ObservedRemoteTargetSha} confirmed at {op.RemoteConfirmedAt:O}; "
        + (op.PushStartedAt is null ? "no push attempted; " : $"push exit={op.PushExitCode?.ToString() ?? "unknown"}; ")
        + $"cleanup={op.Cleanup}" + (op.LastReason is null ? "" : $": {op.LastReason}");

    internal static string FormatSourceRefusal(AgentTaskLandRequest request, string reason) =>
        $"{reason} expected={request.ExpectedSourceSha ?? "null"} local={request.LocalBeforeSha ?? "null"} "
        + $"remote={request.RemoteSourceSha ?? "null"} candidate={request.CandidateSourceSha ?? "null"}";

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
        CancellationToken ct)
    {
        var expectedRequest = task.CurrentLandRequestId;
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(task.Id, ct);
        await _db.Entry(task).ReloadAsync(ct);
        if (task.CurrentLandRequestId != expectedRequest) return;
        var request = await EnsureRequestAsync(task, ct);
        await _db.Entry(request).ReloadAsync(ct);
        if (request.TerminalEventId is not null) return;
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var warning in warnings)
            _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.Warning, warning, now));
        var op = task.ActiveLandingId is Guid id ? await _db.AgentTaskLandings.SingleAsync(o => o.Id == id, ct) : null;
        if (op is not null)
        {
            var alreadyReported = await _db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task.Id
                && e.LandingOperationId == op.Id && (e.Type == AgentTaskEventType.Landed
                    || e.Type == AgentTaskEventType.LandedWithResidue || e.Type == AgentTaskEventType.AlreadyPresent), ct);
            if (alreadyReported) type = AgentTaskEventType.LandingCleanup;
        }
        var terminal = Event(task.Id, type, outcome, now);
        SetLandingEvidence(terminal, op);
        _db.AgentTaskEvents.Add(terminal);
        CompleteRequest(task, request, terminal);
        AddNotification(task, request, terminal, LandNotificationKind.Outcome);
        ClearPending(task);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _boundary.ReachedAsync("terminal-committed", task.Id, terminal.Id, ct);
        await PublishAsync(task, ct);
    }

    private static string AppendUnlandedMarker(string outcome, string? marker) =>
        marker is null ? outcome : $"{outcome}, {marker}";

    /// <summary>
    /// Same-card kept Worktree branches whose tip is not an ancestor of the rebased HEAD
    /// (CARD-0215). Warn on a surviving uncontained branch; absence grants no landing authority.
    /// </summary>
    private async Task<(string? Marker, IReadOnlyList<string> Warnings)> CollectUnlandedSiblingsAsync(
        AgentTask task, string rebasedHeadRepo, CancellationToken ct, string? verifiedSha = null)
    {
        if (task.CardId is null || task.RepoPath is null || !Directory.Exists(rebasedHeadRepo))
            return (null, []);

        var siblings = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Id != task.Id
                && t.CardId == task.CardId
                && t.Workspace == WorkspaceMode.Worktree
                && (t.Status == AgentTaskStatus.Succeeded || t.Status == AgentTaskStatus.Blocked)
                && t.WorktreeBranch != null)
            .Select(t => new { t.Id, t.WorktreeBranch, t.RepoPath, t.WorktreePath })
            .ToListAsync(ct);
        if (siblings.Count == 0)
            return (null, []);

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
            if (await _worktrees.IsAncestorOfBaseAsync(rebasedHeadRepo, branch, verifiedSha ?? "HEAD", ct))
                continue;

            var shortId = DelegationReportFormatter.Short(sibling.Id);
            tokens.Add($"{shortId}:{branch}");
            warnings.Add(
                $"{cardIdentifier}'s kept branch {branch} (task {shortId}) is not an ancestor of the rebased HEAD.");
        }

        return tokens.Count == 0
            ? (null, [])
            : ($"unlanded-sibling={string.Join(",", tokens)}", warnings);
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
                _db.AgentTaskEvents.Add(Event(row.Id, AgentTaskEventType.Warning,
                    $"Land attempt {row.LandAttempt} started {row.LandStartedAt:u} did not finish (server restarted); re-running.",
                    _clock.GetUtcNow().UtcDateTime));
                row.LandStartedAt = null;
                row.ConcurrencyToken = Guid.NewGuid();
                await _db.SaveChangesAsync(ct);
            }

            _queue.TryEnqueue(row.Id, request.VerifyFilter, request.Id);
        }
    }

    /// <summary>
    /// Drain-side failure: <c>RunAsync</c> threw. Writes <c>LandRefused</c>, keeps the branch,
    /// clears the pending request. If this write itself throws the row stays pending for the sweep.
    /// </summary>
    public async Task FailAsync(Guid taskId, Exception exception, CancellationToken ct)
        => await FailRequestAsync(taskId, null, exception, ct);

    public async Task FailRequestAsync(Guid taskId, Guid? requestId, Exception exception, CancellationToken ct)
    {
        var task = await _db.AgentTasks.SingleOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return;
        _db.ChangeTracker.Clear();
        task = await _db.AgentTasks.SingleAsync(t => t.Id == taskId, ct);
        if (requestId is not null && requestId != task.CurrentLandRequestId) return;
        var request = await GetRequestAsync(task, ct);
        if (request?.TerminalEventId is not null || task.LandRequestedAt is null) return;
        var op = task.ActiveLandingId is Guid id
            ? await _db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == id, ct) : null;
        if (op is not null && new AgentTaskLandingState().HasPublication(op))
        {
            op.LastReason = "landing_interrupted_after_publication";
            if (op.Cleanup != LandCleanupStatus.Complete) op.Cleanup = LandCleanupStatus.Pending;
            var type = op.Publication == LandPublicationOutcome.AlreadyPresent ? AgentTaskEventType.AlreadyPresent
                : op.Cleanup == LandCleanupStatus.Complete ? AgentTaskEventType.Landed : AgentTaskEventType.LandedWithResidue;
            await SettleLandedAsync(task, type, FormatOutcome(op), [], ct);
            return;
        }
        await PersistRefusalAsync(task, "land unconfirmed: operation failed; inspect durable evidence", "landing_failed", ct);
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
        var now = _clock.GetUtcNow().UtcDateTime;
        var terminal = Event(task.Id, AgentTaskEventType.LandRefused, line, now);
        var opId = request.LandingOperationId ?? task.ActiveLandingId;
        var op = opId is Guid bound
            ? await _db.AgentTaskLandings.SingleOrDefaultAsync(o => o.Id == bound, ct)
            : null;
        SetLandingEvidence(terminal, op);
        _db.AgentTaskEvents.Add(terminal);
        _db.AgentTaskEvents.Add(Event(task.Id, AgentTaskEventType.Warning,
            $"Landing not confirmed; no cleanup authorized by this result. {warningDetail}",
            now));
        CompleteRequest(task, request, terminal);
        AddNotification(task, request, terminal, LandNotificationKind.Outcome);
        ClearPending(task);
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _boundary.ReachedAsync("terminal-committed", task.Id, terminal.Id, ct);
        await PublishAsync(task, ct);
    }

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

    private async Task HoldAsync(AgentTask task, AgentTaskLandRequest request, string reason,
        AgentTask? holder, string detail, CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await LockTaskAsync(task.Id, ct);
        await _db.Entry(request).ReloadAsync(ct);
        if (!request.IsPending) return;
        var now = _clock.GetUtcNow().UtcDateTime;
        request.LastEvaluatedAt = now;
        var changed = request.State != LandRequestState.Held || request.HoldReasonCode != reason
            || request.HoldingTaskId != holder?.Id;
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
            AddNotification(task, request, held, LandNotificationKind.Held);
        }
        request.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await PublishAsync(task, ct);
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

    private void AddNotification(AgentTask task, AgentTaskLandRequest request, AgentTaskEvent source, LandNotificationKind kind)
        => _db.AgentTaskLandNotifications.Add(LandNotificationPayload.Create(request, source, kind));

    internal static async Task<LandVerification> VerifyAsync(string worktree, string? filter, CancellationToken ct)
        => await VerifyWithObserverAsync(worktree, filter, null, ct);

    internal static async Task<LandVerification> VerifyWithObserverAsync(string worktree, string? filter,
        ILandingChildObserver? observer, CancellationToken ct)
    {
        // SDK artifacts isolate both bin and obj by project, outside the source checkout.
        // Unique owned outputs are retained; never recursively erase pre-existing bin-* paths.
        var output = Path.Combine(Path.GetTempPath(), "antiphon-land-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var build = await RunProcessAsync(worktree, observer, ct, "dotnet", "build", "--artifacts-path", output);
        if (!build.Ok) return LandVerification.Failure("build", Tail(build));
        if (string.IsNullOrWhiteSpace(filter)) return LandVerification.Success("build OK");
        var tests = await RunProcessAsync(worktree, observer, ct, "dotnet", "run", "--project", "tests/Antiphon.Tests",
            "--artifacts-path", output, "--", "--treenode-filter", filter, "--report-trx",
            "--report-trx-filename", "landing-verification.trx");
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

    private sealed record ProcessResult(bool Ok, string StdOut, string StdErr);
}

internal sealed record LandVerification(bool Ok, string Step, string Tail, string Description)
{
    public static LandVerification Success(string description) => new(true, string.Empty, string.Empty, description);
    public static LandVerification Failure(string step, string tail) => new(false, step, tail, string.Empty);
}

public sealed record LandRequestResult(Guid TaskId, string Status, Guid RequestId = default, string Notification = "tracked");
public enum LandRunResult { Complete, Held }
