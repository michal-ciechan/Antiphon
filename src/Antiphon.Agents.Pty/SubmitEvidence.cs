namespace Antiphon.Agents.Pty;

/// <summary>
/// Positive, post-Enter evidence that a composer accepted a submitted body.
/// </summary>
public static class SubmitEvidence
{
    /// <summary>
    /// One-shot screen predicate. Emptied-composer here is a single poll (the CARD-0299 hole);
    /// the queue must not latch it — it requires the head gone for
    /// <c>PostEvidenceSettleMs</c> of consecutive snapshots, and re-checks the current
    /// screen at the unobservable deadline.
    /// </summary>
    public static bool IsPositive(
        SubmitEvidenceKind kind,
        string screenBeforeSubmit,
        string screenNow,
        string body)
    {
        if (kind != SubmitEvidenceKind.Codex)
            return false;

        return CodexWorkingIndicator.IsVisible(screenNow)
            || IsEmptiedComposer(screenBeforeSubmit, screenNow, body);
    }

    public static bool IsEmptiedComposer(
        string screenBeforeSubmit,
        string screenNow,
        string body) =>
        ComposerDeliveryEvidence.HeadFragmentIsVisible(screenBeforeSubmit, body)
        && !ComposerDeliveryEvidence.HeadFragmentIsVisible(screenNow, body);
}

public enum SubmitEvidenceKind
{
    Standard,
    Codex,
}
