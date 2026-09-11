using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

internal static class LandApproval
{
    public static string? NormalizeExpectedSha(string? value, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                throw new ValidationException("expectedSourceSha", "expectedSourceSha is required for a fresh land request.",
                    "expected_source_sha_required");
            return null;
        }

        if (!GitObjectId.TryNormalize(value, out var sha))
            throw new ValidationException("expectedSourceSha",
                "expectedSourceSha must be a full 40- or 64-character object ID.", "expected_source_sha_invalid");
        return sha;
    }

    public static async Task<StageOutcome> LoadUsableEvidenceAsync(AppDbContext db, Guid evidenceId, string expectedSha,
        AgentTask subject, CancellationToken ct)
    {
        var row = await db.StageOutcomes.AsNoTracking().SingleOrDefaultAsync(o => o.Id == evidenceId, ct)
            ?? throw new ConflictException("Review evidence was not found.", "review_evidence_missing");
        if (row.Stage != OrchestrationStage.Review || row.Outcome != StageOutcomeKind.Clean
            || string.IsNullOrWhiteSpace(row.ReviewedSourceSha))
            throw new ConflictException("Review evidence is not a usable clean Review approval.", "review_evidence_ineligible");
        if (row.SubjectTaskId != subject.Id)
            throw new ConflictException("Review evidence subject is not this landing owner.", "review_evidence_subject_mismatch");
        if (!GitObjectId.IsFull(row.ReviewedSourceSha) || row.ReviewedSourceSha != expectedSha)
            throw new ConflictException("Review evidence SHA does not match expectedSourceSha.", "review_evidence_sha_mismatch");
        var sourceRef = subject.WorktreeBranch is null ? null
            : subject.WorktreeBranch.StartsWith("refs/", StringComparison.Ordinal)
                ? subject.WorktreeBranch : "refs/heads/" + subject.WorktreeBranch;
        if (!string.Equals(row.ReviewedSourceRef, sourceRef, StringComparison.Ordinal))
            throw new ConflictException("Review evidence source ref does not match the landing owner.", "review_evidence_ref_mismatch");
        if (!SameRepository(row.ReviewedRepositoryPath, subject.RepoPath))
            throw new ConflictException("Review evidence repository does not match the landing owner.",
                "review_evidence_repository_mismatch");
        var superseded = await db.StageOutcomes.AsNoTracking()
            .AnyAsync(o => o.SupersedesId == row.Id, ct);
        if (superseded)
            throw new ConflictException("Review evidence has been superseded.", "review_evidence_superseded");
        return row;
    }

    private static bool SameRepository(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }
}
