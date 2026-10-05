using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Prepares report-derived coordinates without writing outcomes or holding a database lock.</summary>
public sealed class ReviewEvidenceBindingService(AppDbContext db, ITaskProgressGit? git = null)
{
    public sealed record Candidate(
        Guid? SubjectTaskId, string? ReviewedSourceSha, bool? ReviewedSourceClean,
        string? ReviewedSourceRef, string? ReviewedRepositoryPath, VerificationScope? Scope,
        IReadOnlyList<string> Warnings, IReadOnlyList<string> ConsistencyWarnings,
        string? ReportSha256 = null, DateTime? ObservedAt = null, string? ConfirmedReviewSha = null,
        string? ObservedSubjectSha = null, bool WitnessRefused = false,
        string? SubjectBranch = null, string? SubjectBaseline = null)
    {
        public bool Bound => ReviewedSourceSha is not null;
    }

    /// <summary>Compatibility path: first settlement retains CARD-0788 warning-and-bind semantics.</summary>
    public async Task<Candidate> PrepareFirstSettlementAsync(
        AgentTask review, StageOutcomeKind outcome, ReviewEvidence.Result evidence, CancellationToken ct)
    {
        var profiled = review.VerificationProfileVersion is not null && review.VerificationRound is not null;
        var empty = new Candidate(review.FollowUpOfTaskId, null, null, null, null,
            profiled ? VerificationScope.Unknown : null, [], []);
        if (review.Role != AgentTaskRole.Review || review.Stage != OrchestrationStage.Review
            || review.Status != AgentTaskStatus.Succeeded
            || !(outcome == StageOutcomeKind.Clean || profiled && outcome == StageOutcomeKind.Found))
            return empty;
        if (!evidence.Usable || evidence.SubjectTaskId is not { } named)
            return empty with { Warnings = [evidence.Warning ?? "Review settled without usable review evidence."] };
        if (review.FollowUpOfTaskId is { } follow && follow != named)
            return empty with { Warnings = ["Review evidence subject does not match the follow-up subject."] };
        var subject = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == named, ct);
        if (subject is null || subject.Workspace != WorkspaceMode.Worktree || !SubjectAuthorized(review, subject))
            return empty with { Warnings = ["Review evidence named a subject that is not an authorized Worktree landing owner."] };

        var fullRef = FullRef(subject.WorktreeBranch);
        var sha = evidence.ReviewedSourceSha;
        var warnings = new List<string>();
        if (GitObjectId.IsFull(review.WorktreeBaseSha)
            && !string.Equals(review.WorktreeBaseSha, sha, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"review-evidence-warning=review_evidence_sha_not_review_base: "
                + $"This review's checkout was cut at {review.WorktreeBaseSha}; the block claims {sha}.");
        var progress = TaskProgressJson.TryReadEvidence(subject.CompletionProgressEvidenceJson);
        var tip = progress?.RemoteSync?.ConfirmedSha;
        if (!GitObjectId.IsFull(tip))
            tip = progress?.Sources?.FirstOrDefault(s => s.Origin == ProgressOrigin.Primary)?.VerifiedSha;
        if (GitObjectId.IsFull(tip) && !string.Equals(tip, sha, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"review-evidence-warning=review_evidence_subject_tip_mismatch: "
                + $"Subject {DelegationReportFormatter.Short(subject.Id)} on {fullRef} was at {tip} "
                + $"as confirmed at its settlement; the block claims {sha}. "
                + "If a different task's pushed branch was reviewed, commission a fresh same-card Review naming that task.");
        return new(subject.Id, sha, evidence.ReviewedSourceClean, fullRef, subject.RepoPath,
            profiled ? ReviewEvidence.CapToRound(evidence.Scope, review.VerificationRound!.Value) : null, [], warnings);
    }

    /// <summary>Strict preparation for S2/S3. The supplied sync belongs to this successful turn.</summary>
    public async Task<Candidate> PrepareRepairAsync(
        AgentTask review, StageOutcomeKind outcome, string report, bool storedBody,
        RemoteSettlementSyncResult? currentSync, RepositoryLease? heldLease, CancellationToken ct)
    {
        var evidence = storedBody ? ReviewEvidence.TryParseStoredBody(report) : ReviewEvidence.TryParse(report);
        var candidate = await PrepareFirstSettlementAsync(review, outcome, evidence, ct);
        if (!candidate.Bound) return candidate;
        Candidate Refuse(string reason) => candidate with
        {
            ReviewedSourceSha = null, ReviewedSourceClean = null, ReviewedSourceRef = null,
            ReviewedRepositoryPath = null, Scope = review.VerificationProfileVersion is not null
                && review.VerificationRound is not null ? VerificationScope.Unknown : null,
            Warnings = [reason], ConsistencyWarnings = [],
            WitnessRefused = true,
        };
        if (evidence.ReviewedSourceClean != true)
            return Refuse("review_evidence_source_not_clean");
        var subject = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == candidate.SubjectTaskId, ct);
        if (subject.RepoPath != candidate.ReviewedRepositoryPath || FullRef(subject.WorktreeBranch) != candidate.ReviewedSourceRef)
            return Refuse("review_evidence_subject_changed");
        if (!SameRepository(review.RepoPath, subject.RepoPath))
            return Refuse("review_evidence_repository_mismatch");
        if (candidate.ReviewedSourceRef is not { } sourceRef || !sourceRef.StartsWith("refs/heads/", StringComparison.Ordinal))
            return Refuse("review_evidence_ref_unavailable");
        if (!string.IsNullOrWhiteSpace(review.RunnerId))
        {
            if (currentSync is null
                || currentSync.State is not (RemoteSettlementSyncState.Synchronized or RemoteSettlementSyncState.NoPushedProgress)
                || !GitObjectId.IsFull(currentSync.DesktopAfterSha)
                || !string.Equals(currentSync.DesktopAfterSha, candidate.ReviewedSourceSha, StringComparison.OrdinalIgnoreCase))
                return Refuse("review_evidence_sync_unconfirmed");
            if (currentSync.MirrorDirty != false)
                return Refuse("review_evidence_mirror_not_clean");
            if (currentSync.FullRef != FullRef(review.WorktreeBranch))
                return Refuse("review_evidence_sync_ref_mismatch");
        }
        var baseline = TaskProgressJson.TryReadBaseline(subject.ProgressBaselineJson)?.Primary;
        var fingerprint = baseline?.Remote.EndpointFingerprint;
        if (fingerprint is not { Length: 64 }
            || baseline!.FullRef != sourceRef || !SameRepository(baseline.CanonicalRepository, subject.RepoPath))
            return Refuse("review_evidence_endpoint_unavailable");
        if (git is null) return Refuse("review_evidence_observation_unavailable");
        ProgressRemoteObservation observation;
        try
        {
            observation = heldLease is null
                ? await git.ObserveExactRefAsync(subject.RepoPath!, sourceRef, fingerprint, subject.Id, ct)
                : await git.ObserveExactRefUnderLeaseAsync(subject.RepoPath!, sourceRef, fingerprint, subject.Id, heldLease, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Refuse("review_evidence_observation_unavailable");
        }
        if (observation.State != ProgressRemoteState.Present || !GitObjectId.IsFull(observation.Sha))
            return Refuse("review_evidence_subject_ref_unavailable");
        if (!string.Equals(observation.EndpointFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            return Refuse("review_evidence_endpoint_changed");
        if (!string.Equals(observation.Sha, candidate.ReviewedSourceSha, StringComparison.OrdinalIgnoreCase))
            return Refuse("review_evidence_subject_ref_mismatch");
        return candidate with
        {
            ReportSha256 = ReportDigest(report), ObservedAt = DateTime.UtcNow,
            ConfirmedReviewSha = currentSync?.DesktopAfterSha?.ToLowerInvariant(),
            ObservedSubjectSha = observation.Sha!.ToLowerInvariant(), ConsistencyWarnings = [],
            SubjectBranch = subject.WorktreeBranch, SubjectBaseline = subject.ProgressBaselineJson,
        };
    }

    public static string ReportDigest(string report) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(report)));
    public static string? FullRef(string? branch) => string.IsNullOrWhiteSpace(branch) ? null
        : branch.StartsWith("refs/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
    private static bool SubjectAuthorized(AgentTask review, AgentTask subject) =>
        review.Id == subject.Id || review.FollowUpOfTaskId == subject.Id
        || review.CardId is { } card && subject.CardId == card;
    private static bool SameRepository(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}

/// <summary>Versioned authority record stored in StageOutcome.Ref, never clipped to fit its DB limit.</summary>
public sealed record ReviewRebindProvenance(
    string Mode, string ReportSha256, Guid SourceEventId, DateTime ObservedAt,
    string? ConfirmedReviewSha, string ObservedSubjectSha, string? Actor)
{
    public const string Prefix = "review-rebind-v1:";
    public const int MaxLength = 1000;
    public string Serialize()
    {
        if (!Valid()) throw new ArgumentException("Invalid review rebind provenance.");
        var value = Prefix + JsonSerializer.Serialize(this, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        if (value.Length > MaxLength) throw new ArgumentException("Review rebind provenance exceeds Ref capacity.");
        return value;
    }
    public static ReviewRebindProvenance? TryParse(string? value)
    {
        if (value is null || value.Length > MaxLength || !value.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<ReviewRebindProvenance>(value[Prefix.Length..],
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return record?.Valid() == true ? record : null;
        }
        catch (JsonException) { return null; }
    }
    private bool Valid() => Mode is "settlement" or "recovery" && SourceEventId != Guid.Empty
        && ObservedAt.Kind == DateTimeKind.Utc && ReportSha256 is { Length: 64 }
        && ReportSha256.All(Uri.IsHexDigit) && GitObjectId.IsFull(ObservedSubjectSha)
        && (ConfirmedReviewSha is null || GitObjectId.IsFull(ConfirmedReviewSha))
        && (Mode != "recovery" || !string.IsNullOrWhiteSpace(Actor));
}
