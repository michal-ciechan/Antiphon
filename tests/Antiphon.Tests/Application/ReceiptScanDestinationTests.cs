using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1157 policy lane (no database). Every row drives the production
/// <see cref="ReceiptScanTarget.FollowsQueueDestination"/> with exactly one member changed from the
/// positive fixture.
/// </summary>
[Category("Unit")]
public sealed class ReceiptScanDestinationTests
{
    private static readonly Guid ParentA = Guid.Parse("a1157000-0000-0000-0000-00000000000a");
    private static readonly Guid DestinationB = Guid.Parse("b1157000-0000-0000-0000-00000000000b");

    private const string Body =
        "Land outcome for task 1157: merged onto master.\n"
        + "The keyed queue row was carried to the standing session before its first attempt.\n"
        + "Its receipt is the complete prompt in that session's transcript above the delivery floor.\n"
        + "Anything else keeps the parent scan exactly as before.";

    [Test]
    // admitted: D-2 holds
    [Arguments("positive-held")]
    [Arguments("positive-aged")]
    [Arguments("positive-conflict")]
    [Arguments("positive-outcome")]
    [Arguments("back-pointer-null")]
    [Arguments("baseline-zero")]
    // item 1
    [Arguments("parent-null")]
    [Arguments("parent-empty")]
    // item 2
    [Arguments("same-session")]
    [Arguments("queue-destination-empty")]
    // item 3
    [Arguments("back-pointer-other")]
    // item 4
    [Arguments("kind-dispatch-base")]
    [Arguments("kind-delivery-failure")]
    [Arguments("kind-task-completion")]
    [Arguments("kind-legacy-check-note")]
    [Arguments("kind-undefined")]
    [Arguments("is-legacy")]
    // item 5
    [Arguments("completion-snapshot")]
    [Arguments("completion-delivery")]
    // item 6
    [Arguments("status-pending")]
    [Arguments("status-canceled")]
    // item 7
    [Arguments("attempts-zero")]
    // item 8
    [Arguments("baseline-null")]
    [Arguments("baseline-negative")]
    // item 9
    [Arguments("remote-spill-body")]
    // item 10
    [Arguments("body-one-char")]
    [Arguments("body-case-differs")]
    // item 11
    [Arguments("pointer-headline")]
    // item 12
    [Arguments("expected-text-differs")]
    public void C1157_FollowsQueueDestinationOnlyWhenEveryPositiveHolds(string flip)
    {
        var (note, row) = Positive();
        var expectedText = Body;
        var expected = false;
        switch (flip)
        {
            case "positive-held": note.Kind = LandNotificationKind.Held; expected = true; break;
            case "positive-aged": note.Kind = LandNotificationKind.Aged; expected = true; break;
            case "positive-conflict": note.Kind = LandNotificationKind.Conflict; expected = true; break;
            case "positive-outcome": expected = true; break;
            case "back-pointer-null": row.SourceLandNotificationId = null; expected = true; break;
            case "baseline-zero": row.LastDeliveryBaselineSequence = 0; expected = true; break;
            case "parent-null": note.ParentSessionId = null; break;
            case "parent-empty": note.ParentSessionId = Guid.Empty; break;
            case "same-session": row.AgentSessionId = ParentA; break;
            case "queue-destination-empty": row.AgentSessionId = Guid.Empty; break;
            case "back-pointer-other": row.SourceLandNotificationId = Guid.NewGuid(); break;
            case "kind-dispatch-base": note.Kind = LandNotificationKind.DispatchBase; break;
            case "kind-delivery-failure": note.Kind = LandNotificationKind.DeliveryFailure; break;
            case "kind-task-completion": note.Kind = LandNotificationKind.TaskCompletion; break;
            case "kind-legacy-check-note": note.Kind = LandNotificationKind.LegacyCheckNote; break;
            case "kind-undefined": note.Kind = (LandNotificationKind)999; break;
            case "is-legacy": note.IsLegacy = true; break;
            case "completion-snapshot": note.CompletionSnapshotJson = "{}"; break;
            case "completion-delivery": note.CompletionDeliveryJson = "{}"; break;
            case "status-pending": row.Status = QueuedMessageStatus.Pending; break;
            case "status-canceled": row.Status = QueuedMessageStatus.Canceled; break;
            case "attempts-zero": row.DeliveryAttempts = 0; break;
            case "baseline-null": row.LastDeliveryBaselineSequence = null; break;
            case "baseline-negative": row.LastDeliveryBaselineSequence = -1; break;
            case "remote-spill-body": row.RemoteSpillBody = "spilled"; break;
            case "body-one-char": row.Body = Body + "."; break;
            case "body-case-differs": row.Body = "l" + Body[1..]; break;
            case "pointer-headline":
                note.Body = row.Body = expectedText = Body + "\n" + TypedBodySpill.PointerHeadline;
                break;
            case "expected-text-differs": expectedText = Body + "."; break;
            default: throw new ArgumentOutOfRangeException(nameof(flip), flip, null);
        }

        ReceiptScanTarget.FollowsQueueDestination(note, row, expectedText).ShouldBe(expected, flip);
    }

    /// <summary>
    /// A keyed non-legacy Outcome note on parent A whose queue row was sent to B with the
    /// production back-pointer, one attempt, baseline 10 and an equal body.
    /// </summary>
    private static (AgentTaskLandNotification Note, SessionQueuedMessage Row) Positive()
    {
        var noteId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var note = new AgentTaskLandNotification
        {
            Id = noteId,
            IsLegacy = false,
            Kind = LandNotificationKind.Outcome,
            ParentSessionId = ParentA,
            Body = Body,
            State = LandNotificationState.AwaitingReceipt,
            QueueMessageId = rowId,
        };
        var row = new SessionQueuedMessage
        {
            Id = rowId,
            AgentSessionId = DestinationB,
            SourceLandNotificationId = noteId,
            Status = QueuedMessageStatus.Sent,
            DeliveryAttempts = 1,
            LastDeliveryBaselineSequence = 10,
            Body = Body,
        };
        return (note, row);
    }
}
