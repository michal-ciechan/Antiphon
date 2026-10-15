using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Verification;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Attaches execution to an existing typed Review judgement; never creates a Review or edits history.</summary>
public sealed class ReviewEvidenceCorrectionService(AppDbContext db, ICheckpointReceiptValidator validator,
    TimeProvider clock, LandDeliveryBoundary boundary)
{
    private sealed record Assessment(string Behavior, Guid SubjectTaskId, string SourceSha,
        string SourceRef, string RepositoryPath);

    public async Task<ReviewEvidenceCorrectionResponse> CorrectAsync(Guid reviewId,
        ReviewEvidenceCorrectionRequest request, string actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 200 || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Length > 2000 || request.PredecessorEvidenceId == Guid.Empty
            || !IsDigest(request.ExpectedReportDigest) || !IsDigest(request.BundleDigest))
            throw new ValidationException("request", "Exact evidence digests and a bounded audit reason are required.");
        var review = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == reviewId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), reviewId);
        RequireReview(review, request);
        var old = await db.StageOutcomes.AsNoTracking().SingleOrDefaultAsync(o => o.Id == request.PredecessorEvidenceId, ct);
        RequirePredecessor(old, review, request);
        var imported = await validator.ImportAsync(request, ct);
        if (imported.MissingRows.Length > 0)
            return new("incomplete", null, null, imported.MissingRows);
        await boundary.ReachedAsync("review-correction-prepared", reviewId, old!.Id, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ReviewEvidenceRecoveryService.LockTasksAsync(db, [reviewId, request.SubjectTaskId], ct);
        await boundary.ReachedAsync("review-correction-locked", reviewId, old.Id, ct);
        var current = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == reviewId, ct);
        if (current.ConcurrencyToken != review.ConcurrencyToken || current.Result != review.Result
            || current.Goal != review.Goal || current.FollowUpOfTaskId != review.FollowUpOfTaskId
            || current.ProjectId != review.ProjectId || current.CardId != review.CardId
            || current.RepoPath != review.RepoPath) throw Refuse("prepared_report_changed");
        RequireReview(current, request);
        RequirePredecessor(await db.StageOutcomes.AsNoTracking().SingleOrDefaultAsync(o => o.Id == old.Id, ct), current, request);
        var subject = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == request.SubjectTaskId, ct);
        if (subject is null || subject.Workspace != WorkspaceMode.Worktree || subject.Role != AgentTaskRole.Code
            || subject.RepoPath != request.RepositoryPath || FullRef(subject.WorktreeBranch) != request.SourceRef
            || subject.ProjectId != current.ProjectId || subject.CardId != current.CardId)
            throw Refuse("subject_changed");
        var existing = await db.ReviewEvidenceCorrections.AsNoTracking().SingleOrDefaultAsync(c =>
            c.PredecessorOutcomeId == old.Id && c.CanonicalBundleDigest == request.BundleDigest, ct);
        if (existing is not null)
        {
            if (existing.ReportDigest != request.ExpectedReportDigest || existing.SuccessorOutcomeId is not Guid next)
                throw Refuse("correction_changed");
            await LandApproval.LoadUsableEvidenceAsync(db, next, request.SourceSha, subject, ct);
            await transaction.CommitAsync(ct);
            return new("already-corrected", existing.Id, next, []);
        }
        var active = await StageOutcomeService.ActiveQuery(db.StageOutcomes.AsNoTracking(), db)
            .Where(o => o.StageTaskId == reviewId && o.Stage == OrchestrationStage.Review)
            .OrderByDescending(o => o.RecordedAt).ThenByDescending(o => o.Id).FirstOrDefaultAsync(ct);
        if (active?.Id != old.Id) throw Refuse("predecessor_superseded");
        var id = Guid.NewGuid();
        var at = clock.GetUtcNow().UtcDateTime;
        var successor = new StageOutcome
        {
            Id = Guid.NewGuid(), Stage = OrchestrationStage.Review, Outcome = StageOutcomeKind.Clean,
            Source = StageOutcomeSource.Orchestrator, StageTaskId = reviewId, SubjectTaskId = subject.Id,
            CardId = old.CardId, SupersedesId = old.Id, RecordedAt = at,
            CostUsd = old.CostUsd, TokensIn = old.TokensIn, TokensOut = old.TokensOut,
            DurationSeconds = old.DurationSeconds, ResolutionTaskId = old.ResolutionTaskId,
            ResolutionCostUsd = old.ResolutionCostUsd, Detail = "Validated execution attached to retained typed behavior assessment.",
            Ref = "correction:v1:" + id.ToString("D"), ReviewedSourceSha = request.SourceSha,
            ReviewedSourceClean = true, ReviewedSourceRef = request.SourceRef, ReviewedRepositoryPath = request.RepositoryPath,
            VerificationProfileVersion = 1, CommissionedRound = VerificationRound.Final, OrdinaryScopeCompleted = VerificationScope.Full,
        };
        var correction = new ReviewEvidenceCorrection
        {
            Id = id, ReviewTaskId = reviewId, PredecessorOutcomeId = old.Id, SubjectTaskId = subject.Id,
            ProjectId = current.ProjectId, CardId = current.CardId,
            BoardId = current.CardId is Guid card ? await db.Cards.Where(c => c.Id == card).Select(c => (Guid?)c.BoardId).SingleAsync(ct) : null,
            ReportDigest = request.ExpectedReportDigest, SourceSha = request.SourceSha, SourceRef = request.SourceRef,
            RepositoryPath = request.RepositoryPath, SelectionPath = request.Selection.Path,
            SelectionCommitSha = request.Selection.CommitSha, SelectionSection = request.Selection.Section,
            SelectionDigest = request.Selection.Digest, RequiredRowsJson = JsonSerializer.Serialize(request.Selection.Rows),
            CanonicalBundleDigest = request.BundleDigest, ImportedEvidenceIdentity = imported.Identity,
            ProducerIdentity = request.ProducerIdentity, ValidationResultJson = imported.ValidationJson,
            Actor = actor, Reason = request.Reason, RecordedAt = at, SuccessorOutcomeId = successor.Id,
        };
        db.ReviewEvidenceCorrections.Add(correction);
        db.StageOutcomes.Add(successor);
        await boundary.ReachedAsync("review-correction-before-audit", reviewId, id, ct);
        db.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = reviewId,
            Type = AgentTaskEventType.FindingRecorded, At = at, Detail = JsonSerializer.Serialize(new {
                correctionId = id, previousEvidenceId = old.Id, reviewEvidenceId = successor.Id,
                reportDigest = correction.ReportDigest, bundleDigest = correction.CanonicalBundleDigest,
                actor, reason = request.Reason }) });
        await boundary.ReachedAsync("review-correction-before-save", reviewId, id, ct);
        await db.SaveChangesAsync(ct);
        await boundary.ReachedAsync("review-correction-before-commit", reviewId, id, ct);
        await transaction.CommitAsync(ct);
        await boundary.ReachedAsync("review-correction-committed", reviewId, id, ct);
        return new("corrected", id, successor.Id, []);
    }

    private static void RequireReview(AgentTask review, ReviewEvidenceCorrectionRequest request)
    {
        if (review.Role != AgentTaskRole.Review || review.Stage != OrchestrationStage.Review
            || review.Status != AgentTaskStatus.Succeeded || review.CompletedAt is null
            || review.VerificationProfileVersion != 1 || review.VerificationRound != VerificationRound.Final
            || review.FollowUpOfTaskId != request.SubjectTaskId || review.RepoPath != request.RepositoryPath)
            throw Refuse("review_ineligible");
        if (Digest(review.Result ?? "") != request.ExpectedReportDigest) throw Refuse("report_changed");
        var assessment = ReadBlock<Assessment>(review.Result, "--- correction assessment v1 ---");
        if (assessment?.Behavior != "Satisfied") throw Refuse("behavior_requires_review");
        if (assessment.SubjectTaskId != request.SubjectTaskId || assessment.SourceSha != request.SourceSha
            || assessment.SourceRef != request.SourceRef || assessment.RepositoryPath != request.RepositoryPath)
            throw Refuse("assessment_coordinates_mismatch");
        var selection = ReadBlock<ReviewCorrectionSelection>(review.Goal, "--- correction selection v1 ---");
        if (selection is null || JsonSerializer.Serialize(selection) != JsonSerializer.Serialize(request.Selection))
            throw Refuse("commissioned_selection_mismatch");
    }

    private static void RequirePredecessor(StageOutcome? old, AgentTask review, ReviewEvidenceCorrectionRequest request)
    {
        if (old is null || old.StageTaskId != review.Id || old.Stage != OrchestrationStage.Review
            || old.Source != StageOutcomeSource.Delegate || old.SubjectTaskId != request.SubjectTaskId
            || old.ReviewedSourceSha is { } sha && sha != request.SourceSha
            || old.ReviewedSourceRef is { } sourceRef && sourceRef != request.SourceRef
            || old.ReviewedRepositoryPath is { } repo && repo != request.RepositoryPath)
            throw Refuse("predecessor_ineligible");
    }

    internal static T? ReadBlock<T>(string? text, string marker) where T : class
    {
        if (text is null) return null;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var hits = new List<int>();
        var fenced = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```") || lines[i].TrimStart().StartsWith("~~~")) fenced = !fenced;
            if (!fenced && lines[i] == marker) hits.Add(i);
        }
        if (hits.Count != 1 || hits[0] + 1 >= lines.Length) return null;
        try
        {
            using var document = JsonDocument.Parse(lines[hits[0] + 1]);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() != 1)) return null;
            return JsonSerializer.Deserialize<T>(document.RootElement.GetRawText(), new JsonSerializerOptions {
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
        }
        catch (JsonException) { return null; }
    }

    internal static async Task ValidateLinkAsync(AppDbContext db, StageOutcome outcome, CancellationToken ct)
    {
        var rows = await db.ReviewEvidenceCorrections.AsNoTracking().Where(c => c.SuccessorOutcomeId == outcome.Id).ToListAsync(ct);
        if (rows.Count == 0 && outcome.Ref?.StartsWith("correction:", StringComparison.Ordinal) != true) return;
        if (rows.Count != 1) throw Refuse("link_missing");
        var c = rows[0];
        if (outcome.Ref != "correction:v1:" + c.Id.ToString("D") || outcome.SupersedesId != c.PredecessorOutcomeId
            || outcome.StageTaskId != c.ReviewTaskId || outcome.SubjectTaskId != c.SubjectTaskId
            || outcome.ReviewedSourceSha != c.SourceSha || outcome.ReviewedSourceRef != c.SourceRef
            || outcome.ReviewedRepositoryPath != c.RepositoryPath || outcome.CommissionedRound != VerificationRound.Final
            || outcome.OrdinaryScopeCompleted != VerificationScope.Full || c.SchemaVersion != 1)
            throw Refuse("link_mismatch");
        var review = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == c.ReviewTaskId, ct);
        if (review is null || Digest(review.Result ?? "") != c.ReportDigest) throw Refuse("link_report_changed");
        await CheckpointReceiptValidator.VerifyRetainedAsync(c.ImportedEvidenceIdentity, c.CanonicalBundleDigest, ct);
    }

    internal static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static bool IsDigest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string? FullRef(string? branch) => branch is null ? null : branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
    internal static ConflictException Refuse(string code) => new("Review evidence correction refused: " + code, "review_evidence_correction_" + code);
}
