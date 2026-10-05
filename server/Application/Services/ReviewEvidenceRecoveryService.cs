using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Named, append-only recovery from the retained successful report; never runs settlement or delivery.</summary>
public sealed class ReviewEvidenceRecoveryService(
    AppDbContext db, ReviewEvidenceBindingService binding, ITaskProgressGit git,
    TimeProvider clock, LandDeliveryBoundary boundary)
{
    private sealed record Identity(Guid Token, AgentTaskStatus Status, string? Result, Guid? FollowUp,
        Guid? Card, Guid? Project, string? Repository, string? Branch, string? Baseline, string? Sync,
        string? Runner, int? Profile, VerificationRound? Round, AgentTaskRole Role,
        OrchestrationStage? Stage, WorkspaceMode Workspace, DateTime? CompletedAt)
    {
        public static Identity Of(AgentTask t) => new(t.ConcurrencyToken, t.Status, t.Result,
            t.FollowUpOfTaskId, t.CardId, t.ProjectId, t.RepoPath, t.WorktreeBranch, t.ProgressBaselineJson,
            t.CompletionProgressEvidenceJson, t.RunnerId, t.VerificationProfileVersion, t.VerificationRound,
            t.Role, t.Stage, t.Workspace, t.CompletedAt);
    }

    public async Task<ReviewEvidenceRecoveryResponse> RebindAsync(
        Guid reviewId, ReviewEvidenceRecoveryRequest request, string actor, CancellationToken ct)
    {
        if (request.EvidenceId == Guid.Empty || request.ExpectedReportSha256 is not { Length: 64 }
            || !request.ExpectedReportSha256.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 2000
            || string.IsNullOrWhiteSpace(actor) || actor.Length > 200)
            throw new ValidationException("request", "Named evidence, exact SHA-256 and a bounded audit reason are required.");
        var review = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == reviewId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), reviewId);
        RequireEligible(review);
        var digest = ReviewEvidenceBindingService.ReportDigest(review.Result!);
        if (!digest.Equals(request.ExpectedReportSha256, StringComparison.OrdinalIgnoreCase))
            throw Refuse("report_changed");
        var old = await db.StageOutcomes.AsNoTracking().SingleOrDefaultAsync(o => o.Id == request.EvidenceId, ct);
        RequirePredecessor(old, reviewId);
        var identity = Identity.Of(review);
        var existing = await MatchingSuccessorAsync(old!, digest, ct);
        if (existing is not null)
        {
            await using var repeat = await db.Database.BeginTransactionAsync(ct);
            await LockTasksAsync(db, [reviewId], ct);
            var repeatedCurrent = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == reviewId, ct);
            if (Identity.Of(repeatedCurrent) != identity) throw Refuse("prepared_report_changed");
            existing = await MatchingSuccessorAsync(old!, digest, ct) ?? throw Refuse("predecessor_changed");
            await repeat.CommitAsync(ct);
            return Receipt("already-bound", reviewId, old!.Id, existing, digest);
        }

        if (!DelegationReportFormatter.TryReadFindingLine(reviewId, review.Result!, out var finding, out var findingDetail)
            || finding != StageOutcomeKind.Clean) throw Refuse("finding_not_clean");
        var sync = TaskProgressJson.TryReadEvidence(review.CompletionProgressEvidenceJson)?.RemoteSync;
        // Historical sync is required even when fresh refs match. It is not a substitute for them.
        if (sync is null || sync.State is not (RemoteSettlementSyncState.Synchronized or RemoteSettlementSyncState.NoPushedProgress)
            || sync.ConfirmedSha is null || sync.MirrorDirty != false
            || sync.FullRef != ReviewEvidenceBindingService.FullRef(review.WorktreeBranch))
            throw Refuse("stored_sync_unconfirmed");
        var candidate = await binding.PrepareRepairAsync(review, finding, review.Result!, true,
            new RemoteSettlementSyncResult(sync.State, FullRef: sync.FullRef, DesktopAfterSha: sync.ConfirmedSha,
                MirrorDirty: sync.MirrorDirty), null, ct);
        if (!candidate.Bound || candidate.ReviewedSourceClean != true || candidate.Scope != VerificationScope.Full)
            throw Refuse(candidate.Warnings.FirstOrDefault() ?? "scope_not_full");
        if (!string.Equals(sync.ConfirmedSha, candidate.ReviewedSourceSha, StringComparison.OrdinalIgnoreCase))
            throw Refuse("stored_sync_sha_mismatch");
        var baseline = TaskProgressJson.TryReadBaseline(review.ProgressBaselineJson)?.Primary;
        if (baseline is null || baseline.FullRef != sync.FullRef
            || baseline.CanonicalRepository != review.RepoPath || baseline.Remote.EndpointFingerprint is not { Length: 64 } fingerprint)
            throw Refuse("review_endpoint_unavailable");
        ProgressRemoteObservation observation;
        try { observation = await git.ObserveExactRefAsync(review.RepoPath!, sync.FullRef!, fingerprint, reviewId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw Refuse("review_ref_unavailable"); }
        if (observation.State != ProgressRemoteState.Present
            || !string.Equals(observation.Sha, candidate.ReviewedSourceSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(observation.EndpointFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            throw Refuse("review_ref_mismatch");
        var sourceEvent = await SourceEventAsync(review, ct);
        var provenance = new ReviewRebindProvenance("recovery", digest, sourceEvent,
            clock.GetUtcNow().UtcDateTime, sync.ConfirmedSha.ToLowerInvariant(), candidate.ObservedSubjectSha!, actor).Serialize();
        await boundary.ReachedAsync("review-recovery-prepared", reviewId, old!.Id, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockTasksAsync(db, [reviewId], ct);
        await boundary.ReachedAsync("review-recovery-locked", reviewId, old.Id, ct);
        var current = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == reviewId, ct);
        if (Identity.Of(current) != identity) throw Refuse("prepared_report_changed");
        RequirePredecessor(await db.StageOutcomes.AsNoTracking().SingleOrDefaultAsync(o => o.Id == old.Id, ct), reviewId);
        existing = await MatchingSuccessorAsync(old, digest, ct);
        if (existing is not null) return Receipt("already-bound", reviewId, old.Id, existing, digest);
        var subject = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == candidate.SubjectTaskId, ct);
        if (subject is null || subject.Workspace != WorkspaceMode.Worktree
            || subject.RepoPath != candidate.ReviewedRepositoryPath || subject.WorktreeBranch != candidate.SubjectBranch
            || subject.ProgressBaselineJson != candidate.SubjectBaseline
            || (current.FollowUpOfTaskId is { } follow ? follow != subject.Id : current.CardId is null || current.CardId != subject.CardId))
            throw Refuse("prepared_coordinates_changed");
        if (await SourceEventAsync(current, ct) != sourceEvent) throw Refuse("provenance_changed");
        var row = new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Orchestrator, StageTaskId = reviewId, SubjectTaskId = subject.Id,
            CardId = old.CardId, SupersedesId = old.Id, RecordedAt = clock.GetUtcNow().UtcDateTime,
            CostUsd = old.CostUsd, TokensIn = old.TokensIn, TokensOut = old.TokensOut,
            DurationSeconds = old.DurationSeconds,
            Detail = findingDetail.Length <= StageOutcome.DetailMaxLength ? findingDetail : findingDetail[..StageOutcome.DetailMaxLength],
            Ref = provenance,
            ReviewedSourceSha = candidate.ReviewedSourceSha, ReviewedSourceClean = true,
            ReviewedSourceRef = candidate.ReviewedSourceRef, ReviewedRepositoryPath = candidate.ReviewedRepositoryPath,
            VerificationProfileVersion = 1, CommissionedRound = VerificationRound.Final,
            OrdinaryScopeCompleted = VerificationScope.Full,
        };
        db.StageOutcomes.Add(row);
        await boundary.ReachedAsync("review-recovery-before-audit", reviewId, row.Id, ct);
        var audit = JsonSerializer.Serialize(new { previousEvidenceId = old.Id, reviewEvidenceId = row.Id,
            reportSha256 = digest, actor, reason = request.Reason, sourceEventId = sourceEvent,
            observedAt = row.RecordedAt, confirmedReviewSha = sync.ConfirmedSha,
            observedSubjectSha = candidate.ObservedSubjectSha });
        if (audit.Length > 4000) throw new ValidationException("reason", "Encoded recovery audit exceeds event capacity.");
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(), AgentTaskId = reviewId, Type = AgentTaskEventType.FindingRecorded, At = row.RecordedAt,
            Detail = audit,
        });
        await boundary.ReachedAsync("review-recovery-before-save", reviewId, row.Id, ct);
        await db.SaveChangesAsync(ct);
        await boundary.ReachedAsync("review-recovery-before-commit", reviewId, row.Id, ct);
        await transaction.CommitAsync(ct);
        return Receipt("bound", reviewId, old.Id, row, digest);
    }

    internal static Task LockTasksAsync(AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct) => LockAllAsync(db, ids, ct);
    private static async Task LockAllAsync(AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        foreach (var id in ids.Distinct().Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AgentTasks\" WHERE \"Id\" = {id} FOR UPDATE", ct);
    }

    private static void RequireEligible(AgentTask review)
    {
        if (review.Role != AgentTaskRole.Review || review.Stage != OrchestrationStage.Review
            || review.Status != AgentTaskStatus.Succeeded || review.Workspace != WorkspaceMode.Worktree
            || review.VerificationProfileVersion != 1 || review.VerificationRound != VerificationRound.Final
            || string.IsNullOrWhiteSpace(review.Result)) throw Refuse("task_ineligible");
    }
    private static void RequirePredecessor(StageOutcome? old, Guid id)
    {
        if (old is null || old.StageTaskId != id || old.Stage != OrchestrationStage.Review
            || old.Source != StageOutcomeSource.Delegate || old.ReviewedSourceSha is not null
            || old.ReviewedSourceRef is not null || old.ReviewedRepositoryPath is not null)
            throw Refuse("predecessor_ineligible");
    }
    private async Task<StageOutcome?> MatchingSuccessorAsync(StageOutcome old, string digest, CancellationToken ct)
    {
        var successors = await db.StageOutcomes.AsNoTracking().Where(o => o.SupersedesId == old.Id).ToListAsync(ct);
        if (successors.Count == 0)
        {
            var active = await StageOutcomeService.ActiveQuery(db.StageOutcomes.AsNoTracking(), db)
                .Where(o => o.StageTaskId == old.StageTaskId && o.Stage == OrchestrationStage.Review)
                .OrderByDescending(o => o.RecordedAt).ThenByDescending(o => o.Id).FirstOrDefaultAsync(ct);
            if (active?.Id != old.Id) throw Refuse("predecessor_superseded");
            return null;
        }
        if (successors.Count == 1 && successors[0] is { Source: StageOutcomeSource.Orchestrator,
                Outcome: StageOutcomeKind.Clean, ReviewedSourceClean: true, OrdinaryScopeCompleted: VerificationScope.Full } next
            && ReviewRebindProvenance.TryParse(next.Ref) is { Mode: "recovery" } p && p.ReportSha256 == digest
            && !await db.StageOutcomes.AnyAsync(o => o.SupersedesId == next.Id, ct)) return next;
        throw Refuse("predecessor_superseded");
    }
    private async Task<Guid> SourceEventAsync(AgentTask review, CancellationToken ct)
    {
        var events = await db.AgentTaskEvents.AsNoTracking().Where(e => e.AgentTaskId == review.Id
            && e.Type == AgentTaskEventType.Completed && e.At == review.CompletedAt
            && e.Detail.StartsWith("Delegate reported ")).ToListAsync(ct);
        var snapshots = await db.AgentTaskLandNotifications.AsNoTracking().Where(n => n.TaskId == review.Id
            && n.Kind == LandNotificationKind.TaskCompletion && n.CompletionSnapshotJson != null).ToListAsync(ct);
        var matches = snapshots.Select(n => TaskCompletionNotification.TryReadSnapshot(n.CompletionSnapshotJson))
            .Where(s => s is not null && s.TaskId == review.Id && s.Status == AgentTaskStatus.Succeeded
                && s.RawResult == review.Result && events.Any(e => e.Id == s.SourceEventId))
            .Select(s => s!.SourceEventId).Distinct().ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0 && events.Count == 1) return events[0].Id;
        throw Refuse("completion_provenance_unavailable");
    }
    private static ReviewEvidenceRecoveryResponse Receipt(string disposition, Guid id, Guid old, StageOutcome next, string digest) =>
        new(disposition, id, old, next.Id, digest, next.SubjectTaskId!.Value, next.ReviewedSourceSha!);
    private static ConflictException Refuse(string code) => new("Stored Review evidence recovery refused: " + code,
        "review_evidence_rebind_" + code);
}
