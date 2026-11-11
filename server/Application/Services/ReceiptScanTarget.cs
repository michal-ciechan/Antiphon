using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1157 D-2. Decides whether a keyed land note's receipt scan reads the keyed queue row's
/// session instead of the note's parent session. True needs every positive condition below; any
/// other shape, including a null or empty id, returns false and keeps the parent scan.
/// <c>ReceiptScanDestinationTests.C1157_FollowsQueueDestinationOnlyWhenEveryPositiveHolds</c> pins
/// each condition. The reconciler calls it to choose the session a receipt scan reads
/// (<c>AgentTaskLandReceiptWatermarkTests.C1121_ForeignDestinationFollowsKeyedRow</c>).
/// </summary>
internal static class ReceiptScanTarget
{
    /// <summary>
    /// D-2 items 1-12, in order. <paramref name="expectedText"/> is the text the reconciler already
    /// resolved for the receipt scan; a profiled or pointer note fails item 5 or 12.
    /// </summary>
    public static bool FollowsQueueDestination(AgentTaskLandNotification note, SessionQueuedMessage row, string expectedText)
    {
        if (note.ParentSessionId is not Guid parent || parent == Guid.Empty) return false;
        if (row.AgentSessionId == Guid.Empty || row.AgentSessionId == parent) return false;
        if (row.SourceLandNotificationId is Guid source && source != note.Id) return false;
        if (!LandNoteReceipt.AcceptsQueuedPrompt(note.IsLegacy, note.Kind)) return false;
        if (note.CompletionSnapshotJson is not null || note.CompletionDeliveryJson is not null) return false;
        if (row.Status != QueuedMessageStatus.Sent) return false;
        if (row.DeliveryAttempts <= 0) return false;
        if (row.LastDeliveryBaselineSequence is not long baseline || baseline < 0) return false;
        if (row.RemoteSpillBody is not null) return false;
        if (!string.Equals(row.Body, note.Body, StringComparison.Ordinal)) return false;
        if (row.Body.Contains(TypedBodySpill.PointerHeadline, StringComparison.Ordinal)) return false;
        return string.Equals(expectedText, note.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// CARD-0747. A TaskCompletion's keyed row that a standing agent's fresh restart carried to the
    /// successor session (<c>AgentControlService</c> moves Pending rows) and that was typed there.
    /// Its identity, delivery generation and receipt belong to that session; the note keeps its
    /// parent. Needs the row keyed to this note with the note's digest, and one typed attempt with
    /// a recorded generation. Pinned by <c>C0747_Carried_completion_confirms_on_the_session_it_was_typed_to</c>.
    /// </summary>
    public static bool FollowsCarriedCompletion(AgentTaskLandNotification note, SessionQueuedMessage row)
    {
        if (note.Kind != LandNotificationKind.TaskCompletion || note.IsLegacy) return false;
        if (note.ParentSessionId is not Guid parent || parent == Guid.Empty) return false;
        if (row.AgentSessionId == Guid.Empty || row.AgentSessionId == parent) return false;
        if (row.SourceLandNotificationId != note.Id) return false;
        if (!string.Equals(row.ContentDigest, note.ContentDigest, StringComparison.Ordinal)) return false;
        if (row.Status != QueuedMessageStatus.Sent || row.DeliveryAttempts <= 0) return false;
        return row.LastDeliveryGeneration is not null;
    }
}
