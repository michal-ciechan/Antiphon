namespace Antiphon.Agents.Pty;

/// <summary>
/// Positive, post-Enter evidence that a composer accepted a submitted body.
/// </summary>
public static class SubmitEvidence
{
    public static bool IsPositive(
        SubmitEvidenceKind kind,
        string screenBeforeSubmit,
        string screenNow,
        string body)
    {
        if (kind != SubmitEvidenceKind.Codex)
            return false;

        return CodexWorkingIndicator.IsVisible(screenNow)
            || (ComposerDeliveryEvidence.HeadFragmentIsVisible(screenBeforeSubmit, body)
                && !ComposerDeliveryEvidence.HeadFragmentIsVisible(screenNow, body));
    }
}

public enum SubmitEvidenceKind
{
    Standard,
    Codex,
}
