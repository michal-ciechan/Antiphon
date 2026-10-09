using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1157 policy lane (no database). V-1 drives the production
/// <see cref="ReceiptScanTarget.FollowsQueueDestination"/> with exactly one member changed from the
/// positive fixture. V-4 reads the runtime invariant; V-5 drives the production cache.
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
    /// V-4. The CARD-1121 receipt paragraph names the scanned session (CARD-1157 D-8). This pins the
    /// wording only; <c>C1121_ForeignDestinationFollowsKeyedRow</c> and V-5 pin the behavior.
    /// </summary>
    [Test]
    public void C1157_RuntimeInvariantNamesTheScannedSession()
    {
        var text = Collapse(ReadRepositoryFile("docs/session-runtime-invariants.md"));

        text.ShouldContain(Collapse("The scanned session is the keyed row's AgentSessionId when "
            + "ReceiptScanTarget.FollowsQueueDestination is true and that session row exists; otherwise it is "
            + "the note's ParentSessionId (CARD-1157)."));
        text.ShouldContain("the scanned session's `StartedAt`");
        text.ShouldContain("a Stopped or Failed scanned session");
        text.ShouldNotContain("the destination's `StartedAt`");
        text.ShouldNotContain("a Stopped or Failed destination");
    }

    /// <summary>
    /// V-5. A negative-scan proof binds the session the scan read. The context starts on the parent;
    /// the reconciler rebinds it to the row destination when it scans there. Every row drives the
    /// production <see cref="LandReceiptScanCache.TryBuildContext"/>, <see cref="LandReceiptScanCache.StateStamp.TryCreate"/>,
    /// <see cref="LandReceiptScanCache.Publish"/> and <see cref="LandReceiptScanCache.TryReuse"/>.
    /// </summary>
    [Test]
    [Arguments("build-context-names-the-parent")]
    [Arguments("positive-destination-scan")]
    [Arguments("parent-stamp-cannot-vouch-for-destination-scan")]
    [Arguments("proof-context-scan-session-is-bound")]
    public void C1157_ProofBindsTheScannedSession(string arm)
    {
        var (note, row) = Positive();
        LandReceiptScanCache.TryBuildContext(note, row, SessionStatus.Stopped, Body, out var built, out var refusal)
            .ShouldBeTrue(refusal);
        var context = built!;
        context.QueueDestination.ShouldBe(DestinationB, "A-3 binds an unequal row destination");
        if (arm == "build-context-names-the-parent")
        {
            context.ScanSessionId.ShouldBe(ParentA);
            return;
        }

        var destinationScan = context with { ScanSessionId = DestinationB };
        var parentStamp = Stamp(ParentA, ParentStartedAt);
        var destinationStamp = Stamp(DestinationB, DestinationStartedAt);
        var (publishContext, publishStamp, reuseStamp) = arm switch
        {
            "positive-destination-scan" => (destinationScan, destinationStamp, destinationStamp),
            "parent-stamp-cannot-vouch-for-destination-scan" => (destinationScan, parentStamp, parentStamp),
            // An unsafe publication only a mutated reconciler could make: the stamp and identity checks
            // both pass at reuse, so the context member alone refuses.
            "proof-context-scan-session-is-bound" => (context, destinationStamp, destinationStamp),
            _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, null),
        };
        var cache = new LandReceiptScanCache(new FixedClock());
        cache.Publish(publishContext, publishStamp, publishStamp, matchObserved: false, saveCompleted: true,
            cache.TryGetTimestamp()).ShouldBeTrue();

        var reused = cache.TryReuse(destinationScan, reuseStamp, out var reuseRefusal);

        switch (arm)
        {
            case "positive-destination-scan":
                reused.ShouldBeTrue(reuseRefusal);
                break;
            case "parent-stamp-cannot-vouch-for-destination-scan":
                reused.ShouldBeFalse();
                reuseRefusal.ShouldBe("identity:ScanSession");
                break;
            case "proof-context-scan-session-is-bound":
                reused.ShouldBeFalse();
                reuseRefusal.ShouldBe("context:ScanSessionId");
                break;
        }
    }

    private static readonly DateTime ParentStartedAt = new(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DestinationStartedAt = ParentStartedAt.AddHours(-1);

    // A Ready committed state of one session whose accepted generation is that session's StartedAt.
    private static LandReceiptScanCache.StateStamp Stamp(Guid session, DateTime startedAt)
    {
        var snapshot = new SessionStateSnapshot(session)
        {
            ServerEpoch = Guid.Parse("e1157000-0000-0000-0000-0000000000e0"), Revision = 7, ResetEpoch = 0,
            Readiness = SessionStateReadiness.Ready, AcceptedGeneration = startedAt, Count = 49, LastSequence = 58,
        };
        LandReceiptScanCache.StateStamp.TryCreate(LandReceiptScanCache.Observation.Known(snapshot), startedAt,
            out var stamp, out var refusal).ShouldBeTrue(refusal);
        return stamp!;
    }

    private static string Collapse(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ReadRepositoryFile(string relative)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "docs", "session-runtime-invariants.md")))
                return File.ReadAllText(Path.Combine(directory, relative));
            directory = Path.GetDirectoryName(directory)!;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    /// <summary>A cache clock that never moves: a proof is reused at the instant it was published.</summary>
    private sealed class FixedClock : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => 1_000_000_000_000;
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
