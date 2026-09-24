using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0641's transcript receipt contract for one land/completion note, shared by the reconciler
/// that confirms it and the CARD-0650 watchdog that only reads it. A prompt is receipt only when it
/// is in the note's destination session, above the keyed row's delivery floor, of a kind the note
/// accepts, and carries the complete expected text. Arrival alone, or the text quoted in a prompt
/// that is not this delivery, is never receipt.
/// </summary>
internal static class LandNoteReceipt
{
    /// <summary>
    /// CARD-0641 D-2: a submitted QueuedUserPrompt is receipt for non-legacy Held, Aged, Conflict
    /// and Outcome notes. Every other kind, including a legacy Outcome, stays on UserPrompt.
    /// </summary>
    public static bool AcceptsQueuedPrompt(bool isLegacy, LandNotificationKind kind) =>
        !isLegacy && kind is LandNotificationKind.Held
            or LandNotificationKind.Aged or LandNotificationKind.Conflict or LandNotificationKind.Outcome;

    /// <summary>
    /// Candidate prompts in <paramref name="session"/> above the delivery floor: the baseline
    /// sequence when the keyed row recorded one, else its delivery start less the clock tolerance.
    /// Null when the row has neither, so nothing can be receipt yet.
    /// </summary>
    public static IQueryable<TranscriptEntry>? Prompts(
        IQueryable<TranscriptEntry> entries,
        Guid session,
        bool isLegacy,
        LandNotificationKind kind,
        long? baselineSequence,
        DateTime? deliveryStartedAt,
        int clockToleranceSeconds)
    {
        var prompts = entries.Where(p => p.AgentSessionId == session && p.Text != null);
        prompts = AcceptsQueuedPrompt(isLegacy, kind)
            ? prompts.Where(p => p.Kind == TranscriptKinds.UserPrompt || p.Kind == TranscriptKinds.QueuedUserPrompt)
            : prompts.Where(p => p.Kind == TranscriptKinds.UserPrompt);
        if (baselineSequence is long floor)
            return prompts.Where(p => p.Sequence > floor);
        if (deliveryStartedAt is DateTime started)
        {
            var floorTime = started.AddSeconds(-Math.Max(0, clockToleranceSeconds));
            return prompts.Where(p => p.Timestamp >= floorTime);
        }

        return null;
    }

    /// <summary>Identity (head window) and completeness (whole expected text), CARD-0055/CARD-0024.</summary>
    public static bool IsReceipt(string expected, string promptText) =>
        PromptSubmissionMatch.IsConfirmedBy(expected, promptText)
        && PromptSubmissionMatch.IsCompleteIn(expected, promptText);
}
