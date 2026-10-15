using Antiphon.Server.Domain;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

// The policy describes caller actions; it grants no Review evidence or landing authority.
public enum AdvisoryBehavior { Unknown, Satisfied, Substantive }
public enum AdvisoryVerification { Incomplete, Complete }
public enum AdvisoryAttribution { Unresolved, Introduced, Inherited }
public enum AdvisoryAction { None, Code, Verification, Review }
public enum AdvisoryContent { Unknown, Narrative, Instructions, Policy, TestSelection, Schema,
    ExecutableExample, GeneratedInput, Configuration, Code, Comment }

public sealed record AdvisoryAssessment(AdvisoryBehavior Behavior, AdvisoryVerification Verification,
    bool HasAdvisory, AdvisoryAttribution Attribution);
public sealed record AdvisoryDisposition(AdvisoryAssessment Assessment, AdvisoryAction Action);
public sealed record AdvisoryDelta(string Path, string Before, string After, AdvisoryContent Content)
{
    public string OriginalPath { get; init; } = Path;
    public string BeforeMode { get; init; } = "100644";
    public string AfterMode { get; init; } = "100644";
}
public sealed record AdvisoryFollowUp(AgentTask Owner, StageOutcome Review, AgentTask Helper,
    string CurrentReviewedTip, string PublishedReviewedSourceSha, string HelperSha);

/// <summary>
/// Caller policy only. Content classification is an independent inspection of the complete delta,
/// not a filename inference. Unknown content receives ordinary Review. This class never writes
/// historical outcomes, marks source clean, or validates receipts.
/// </summary>
public sealed class AdvisoryChangePolicy
{
    public AdvisoryDisposition Decide(AdvisoryAssessment assessment) => new(assessment,
        assessment.Behavior == AdvisoryBehavior.Substantive ? AdvisoryAction.Code
        : assessment.Behavior != AdvisoryBehavior.Satisfied ? AdvisoryAction.Review
        : assessment.Verification != AdvisoryVerification.Complete ? AdvisoryAction.Verification
        : AdvisoryAction.None);

    // Narrative is an explicit inspection result for the entire delta and its consumers. These
    // additional mechanical refusals do not infer semantics from Markdown or a filename. Unknown
    // meaning, executable consumers and unsupported markup must receive ordinary Review.
    public bool CanUseSeparateDocs(IReadOnlyList<AdvisoryDelta> changes) => changes.Count > 0
        && changes.All(change => change.Content == AdvisoryContent.Narrative
            && IsNarrativePath(change.Path) && IsNarrativePath(change.OriginalPath)
            && change.BeforeMode is "100644" or "000000" && change.AfterMode == "100644"
            && change.Before != change.After && !string.IsNullOrWhiteSpace(change.After)
            && IsPlainProse(change.Before) && IsPlainProse(change.After));

    /// <summary>
    /// A caller proposal, never landing authority. The caller must obtain the current Review and
    /// the original source SHA from a confirmed publication receipt, inspect the complete delta,
    /// and retain its digest with the separate helper approval. Land admission still validates
    /// the helper's exact committed SHA; this predicate cannot mint or copy Review evidence.
    /// </summary>
    public bool CanPrepareSeparateDocs(AdvisoryFollowUp source, IReadOnlyList<AdvisoryDelta> changes)
    {
        var (owner, review, helper, currentTip, publishedSource, helperSha) = source;
        return CanUseSeparateDocs(changes)
            && owner.Role == AgentTaskRole.Code && helper.Role == AgentTaskRole.Docs
            && owner.Id != helper.Id && owner.WorktreePath != helper.WorktreePath
            && helper.Workspace == WorkspaceMode.Worktree && helper.Status == AgentTaskStatus.Succeeded
            && !owner.RequiresFinalVerificationReview && !helper.RequiresFinalVerificationReview
            && review.Stage == OrchestrationStage.Review && review.Source == StageOutcomeSource.Delegate
            && review.Outcome == StageOutcomeKind.Clean && review.ReviewedSourceClean == true
            && review.CommissionedRound == VerificationRound.Final && review.OrdinaryScopeCompleted == VerificationScope.Full
            && review.SubjectTaskId == owner.Id && review.ReviewedSourceRef == FullRef(owner.WorktreeBranch)
            && !string.IsNullOrWhiteSpace(owner.RepoPath) && review.ReviewedRepositoryPath == owner.RepoPath
            && helper.RepoPath == owner.RepoPath && FullRef(helper.WorktreeBranch) is { } helperRef
            && helperRef != review.ReviewedSourceRef
            && GitObjectId.IsFull(review.ReviewedSourceSha) && currentTip == review.ReviewedSourceSha
            && publishedSource == review.ReviewedSourceSha && GitObjectId.IsFull(helperSha) && helperSha != currentTip;
    }

    private static string? FullRef(string? branch) => string.IsNullOrWhiteSpace(branch) ? null
        : branch.StartsWith("refs/heads/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;

    private static bool IsNarrativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':')) return false;
        var parts = path.ToLowerInvariant().Split('/');
        if (parts.Length < 3 || parts[0] != "docs" || !parts[^1].EndsWith(".md", StringComparison.Ordinal)) return false;
        if (parts.Any(part => part is "" or "." or ".." or "superpowers" or "cards" or "adr"
            or "instructions" or "policy" or "schemas" or "configuration" or "tests" or "scripts" or "bundles")) return false;
        return parts[^1] is not ("agents.md" or "claude.md" or "skill.md" or "readme.md");
    }

    // Deliberately narrow plain prose. Fences, inline code, HTML, links, front matter and
    // directives are unsupported. Comments in any language are refused above, without a
    // purported token-equivalence proof or a regex pretending to provide one.
    private static bool IsPlainProse(string text) => text.All(character => char.IsLetterOrDigit(character)
        || character is ' ' or '\r' or '\n' || ".,!?'()-".Contains(character));
}
