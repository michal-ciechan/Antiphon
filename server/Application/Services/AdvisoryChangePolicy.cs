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
public sealed record AdvisoryDelta(string Path, string Before, string After, AdvisoryContent Content);

/// <summary>
/// Caller policy only. Content classification is an independent inspection of the complete delta,
/// not a filename inference. Unknown content receives ordinary Review. This class never writes
/// historical outcomes, marks source clean, or validates receipts.
/// </summary>
public sealed class AdvisoryChangePolicy
{
    public AdvisoryDisposition Decide(AdvisoryAssessment assessment) => new(assessment,
        assessment.HasAdvisory || assessment.Behavior != AdvisoryBehavior.Satisfied
            || assessment.Verification != AdvisoryVerification.Complete ? AdvisoryAction.Code : AdvisoryAction.None);

    // Before D-2 every source-changing follow-up takes ordinary Review.
    public bool CanUseSeparateDocs(IReadOnlyList<AdvisoryDelta> changes) => false;
}
