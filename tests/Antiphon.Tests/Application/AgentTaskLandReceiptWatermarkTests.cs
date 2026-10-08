using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using Mode = Antiphon.Tests.TestHelpers.LandReceiptScanHarness.ScriptedTranscriptRunner.Mode;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1121 S2: the reconciler's negative receipt-scan cache over the real store, runtime ingest
/// and queue. A proof may omit only the receipt SELECT for a terminal destination whose committed
/// state is unchanged; every committed change, attempt or payload change, and every unknown keeps
/// today's scan, and transcript-confirmed prompts stay the only receipt.
/// </summary>
[Category("Integration")]
public sealed class AgentTaskLandReceiptWatermarkTests
{
    private const long Floor = LandReceiptScanHarness.Floor;

    private sealed record Pass(int Commands, int ReceiptSelects, int ReceiptRows, IReadOnlyList<long?> Floors,
        IReadOnlyList<Guid?> Sessions, int Pulls, IReadOnlyList<string> Sql, string Roster);

    private static async Task<Pass> PassAsync(LandReceiptScanHarness h, Guid noteId)
    {
        h.ResetCounters();
        await h.Service.ReconcileAsync(noteId, CancellationToken.None);
        var scans = h.Receipts.Scans;
        return new(h.Commands.Total, scans.Count, scans.Sum(s => s.Rows), scans.Select(s => s.Floor).ToList(),
            scans.Select(s => s.Session).ToList(), h.Runner.TotalPulls, scans.Select(s => s.Sql).ToList(), h.Commands.Roster());
    }

    private static async Task<IReadOnlyList<Pass>> PassesAsync(LandReceiptScanHarness h, Guid noteId, int count)
    {
        var passes = new List<Pass>();
        for (var i = 0; i < count; i++) passes.Add(await PassAsync(h, noteId));
        return passes;
    }

    /// <summary>
    /// The quiet cohort: 49 committed rows plus a TurnEnd ingested through the runtime before the
    /// enqueue pass, the keyed row typed once above <see cref="Floor"/>, the destination Stopped.
    /// </summary>
    private static async Task<(AgentTaskLandNotification Note, SessionQueuedMessage Row, IReadOnlyList<SessionRunnerTranscriptEvent> Events)>
        ArrangeAsync(LandReceiptScanHarness h, bool bodyAtFloor = false, Func<string, Func<long, (string, string)>?>? candidates = null,
            string? detail = null, Action<SessionQueuedMessage>? row = null, SessionStatus destination = SessionStatus.Stopped)
    {
        IReadOnlyList<SessionRunnerTranscriptEvent> events = [];
        var note = await h.SeedLinkedNoteAsync(detail, async seeded =>
            events = await h.SeedTranscriptAsync(h.SessionId, bodyAtFloor ? seeded.Body : "below the floor",
                candidate: candidates?.Invoke(seeded.Body)));
        var sent = await h.MarkSentAsync(note, row);
        await h.SetStatusAsync(h.SessionId, destination);
        await h.AssertCoherentAsync(h.SessionId);
        return (note, sent, events);
    }

    private static void ShouldNotHaveTyped(LandReceiptScanHarness h)
    {
        h.Bridge.Adapter.Inputs.ShouldBeEmpty("receipt reconciliation must never type");
        h.Runner.KillCalls.ShouldBe(0);
    }

    private static async Task ShouldBeConfirmedAtAsync(LandReceiptScanHarness h, AgentTaskLandNotification note, long expected,
        long floor = Floor, bool? legacy = null, LandNotificationKind? kind = null)
    {
        var saved = await h.NoteAsync(note.Id);
        var oracle = await h.InMemoryFirstReceiptAsync(saved.ParentSessionId!.Value, floor, legacy ?? saved.IsLegacy, kind ?? saved.Kind, saved.Body);
        oracle.ShouldBe(expected, "the in-memory CARD-0641 rule over the committed rows");
        saved.State.ShouldBe(LandNotificationState.Confirmed);
        saved.ConfirmedAt.ShouldNotBeNull();
        saved.ConfirmingPromptSequence.ShouldBe(expected);
    }

    private static async Task ShouldStayOpenAsync(LandReceiptScanHarness h, AgentTaskLandNotification note,
        LandNotificationState state = LandNotificationState.AwaitingReceipt)
    {
        var saved = await h.NoteAsync(note.Id);
        saved.State.ShouldBe(state);
        saved.ConfirmedAt.ShouldBeNull();
        saved.ConfirmingPromptSequence.ShouldBeNull();
    }

    // V-3 -------------------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("two-kind-outcome-404")]
    [Arguments("two-kind-outcome-cold-store")]
    [Arguments("one-kind-legacy-outcome-null-verdict-404")]
    [Arguments("dispatch-base-canceled-row")]
    [Arguments("task-completion-unprofiled-404")]
    [Arguments("two-kind-outcome-live-committed-row")]
    public async Task C1121_UnchangedTranscriptSkipsOnlyReceiptSelect(string shape, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        var (note, row, events) = await ArrangeAsync(h, row: shape == "dispatch-base-canceled-row"
            ? r => r.Status = QueuedMessageStatus.Canceled : null);
        var expected = new[] { 6, 5, 5 };
        switch (shape)
        {
            case "two-kind-outcome-cold-store":
                // A restart: the store is first touched by the reconciler's own observation.
                await h.RestartAsync();
                expected = [7, 5, 5];
                break;
            case "one-kind-legacy-outcome-null-verdict-404":
                await h.UpdateNoteAsync(note.Id, n => n.IsLegacy = true);
                break;
            case "dispatch-base-canceled-row":
                await h.UpdateNoteAsync(note.Id, n => n.Kind = LandNotificationKind.DispatchBase);
                break;
            case "task-completion-unprofiled-404":
                await h.UpdateNoteAsync(note.Id, n => n.Kind = LandNotificationKind.TaskCompletion);
                expected = [7, 6, 6]; // its existing delivery-generation SELECT
                break;
            case "two-kind-outcome-live-committed-row":
                // The runner still holds the session and answers one row the store already committed.
                h.Runner.Transcript = Mode.Entries;
                h.Runner.NextEntries = _ => [events[5]];
                expected = [9, 8, 8];
                break;
        }

        var passes = await PassesAsync(h, note.Id, 3);

        passes.Select(p => p.Commands).ShouldBe(expected, string.Join("\n----\n", passes.Select(p => p.Roster)));
        passes.Select(p => p.ReceiptSelects).ShouldBe([1, 0, 0]);
        passes.Select(p => p.ReceiptRows).ShouldBe([48, 0, 0]);
        passes.ShouldAllBe(p => p.Pulls == 1, "the catch-up pull runs on every pass, hit or miss");
        passes[0].Floors.ShouldBe([Floor]);
        passes[0].Sessions.ShouldBe([h.SessionId]);
        if (shape == "one-kind-legacy-outcome-null-verdict-404")
            passes[0].Sql.Single().ShouldContain("t.\"Kind\" = 'UserPrompt'");
        else if (shape != "dispatch-base-canceled-row" && shape != "task-completion-unprofiled-404")
            passes[0].Sql.Single().ShouldContain("t.\"Kind\" IN ('UserPrompt', 'QueuedUserPrompt')");
        var metrics = h.Cache!.GetMetrics();
        metrics.Proofs.ShouldBe(1);
        metrics.Publishes.ShouldBe(1);
        metrics.Hits.ShouldBe(2);
        if (shape == "dispatch-base-canceled-row")
        {
            await ShouldStayOpenAsync(h, note, LandNotificationState.Canceled);
            (await h.NoteAsync(note.Id)).LastErrorCode.ShouldBe("queue_canceled_unconfirmed");
        }
        else
            await ShouldStayOpenAsync(h, note);
        var after = await h.RowAsync(row.Id);
        after.Status.ShouldBe(row.Status);
        after.SentAt.ShouldBe(row.SentAt);
        after.DeliveryAttempts.ShouldBe(row.DeliveryAttempts);
        ShouldNotHaveTyped(h);
    }

    // V-4 -------------------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("matching-user-prompt")]
    [Arguments("matching-queued-user-prompt")]
    [Arguments("rebased-low-sequence-old-timestamp")]
    [Arguments("unrelated-assistant-text")]
    [Arguments("fence-prune-reseed")]
    [Arguments("ingest-lower-runner-sequence-after-reseed")]
    public async Task C1121_AnyCommittedChangeReopensFullScan(string change, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        var (note, _, events) = await ArrangeAsync(h);
        var cached = await PassesAsync(h, note.Id, 2);
        cached.Select(p => p.ReceiptSelects).ShouldBe([1, 0], "a cached miss before the change");
        var store = h.Store!;
        var before = await store.ReadAsync(h.SessionId, ct);

        long? stored = null;
        switch (change)
        {
            case "matching-user-prompt":
                stored = (await h.IngestAsync(h.SessionId, (TranscriptKinds.UserPrompt, note.Body))).Single();
                break;
            case "matching-queued-user-prompt":
                stored = (await h.IngestAsync(h.SessionId, (TranscriptKinds.QueuedUserPrompt, note.Body))).Single();
                break;
            case "rebased-low-sequence-old-timestamp":
                stored = (await h.IngestEventsAsync(h.SessionId,
                    [h.Event(h.SessionId, 1, TranscriptKinds.UserPrompt, note.Body, DateTimeOffset.UtcNow.AddHours(-1))])).Single();
                stored.ShouldBe(before.LastSequence + 1, "a low runner sequence is rebased above the committed maximum");
                break;
            case "unrelated-assistant-text":
                await h.IngestAsync(h.SessionId, (TranscriptKinds.AssistantText, "unrelated assistant text"));
                break;
            case "fence-prune-reseed":
            case "ingest-lower-runner-sequence-after-reseed":
                await using (var mutation = await store.BeginMutationAsync([h.SessionId], ct))
                {
                    await using var db = h.Fixture();
                    (await db.TranscriptEntries.Where(t => t.AgentSessionId == h.SessionId && t.Sequence == Floor + 1)
                        .ExecuteDeleteAsync(ct)).ShouldBe(1);
                    await mutation.PublishCommittedAsync(ct);
                }
                var reseeded = await store.ReadAsync(h.SessionId, ct);
                reseeded.ResetEpoch.ShouldBe(before.ResetEpoch + 1);
                if (change == "ingest-lower-runner-sequence-after-reseed")
                    stored = (await h.IngestEventsAsync(h.SessionId,
                        [h.Event(h.SessionId, 3, TranscriptKinds.UserPrompt, note.Body)])).Single();
                break;
        }
        (await store.ReadAsync(h.SessionId, ct)).Revision.ShouldBeGreaterThan(before.Revision);

        var next = await PassAsync(h, note.Id);
        next.ReceiptSelects.ShouldBe(1, "a committed change reopens the full scan");
        next.Floors.ShouldBe([Floor], "the scan floor stays the keyed row's original baseline");
        var metrics = h.Cache!.GetMetrics();
        metrics.Hits.ShouldBe(1, "the pre-change proof is never reused");
        if (stored is long sequence)
        {
            sequence.ShouldBeGreaterThan(Floor);
            await ShouldBeConfirmedAtAsync(h, note, sequence);
            metrics.Proofs.ShouldBe(0, "confirmation removes the note's proof");
        }
        else
        {
            next.ReceiptRows.ShouldBe(change == "fence-prune-reseed" ? 47 : 48);
            await ShouldStayOpenAsync(h, note);
            metrics.Publishes.ShouldBe(2, "the new miss is certified against the new state");
            (await PassAsync(h, note.Id)).ReceiptSelects.ShouldBe(0, "and reused while that state holds");
        }
        ShouldNotHaveTyped(h);
    }

    // V-5 -------------------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("lower-baseline-includes-existing-match")]
    [Arguments("attempts-increment")]
    [Arguments("expected-body-changed-to-existing-prompt")]
    [Arguments("queue-identity-changed")]
    [Arguments("legacy-flag-flip-widens-acceptance")]
    [Arguments("kind-flip")]
    public async Task C1121_AttemptAndPayloadChangesReopenOriginalFloor(string change, CancellationToken ct)
    {
        const long QueuedMatch = 30;
        await using var h = await LandReceiptScanHarness.CreateAsync();
        // The body is already committed AT the floor; the two acceptance arms also hold it as a
        // QueuedUserPrompt at 30. A suffix cursor at the previous LastSequence would hide both.
        var widening = change is "legacy-flag-flip-widens-acceptance" or "kind-flip";
        var (note, row, _) = await ArrangeAsync(h, bodyAtFloor: true, candidates: widening
            ? body => seq => seq == QueuedMatch ? (TranscriptKinds.QueuedUserPrompt, body) : (TranscriptKinds.UserPrompt, LandReceiptScanHarness.Filler(seq))
            : null);
        if (change == "legacy-flag-flip-widens-acceptance")
            await h.UpdateNoteAsync(note.Id, n => n.IsLegacy = true);
        if (change == "kind-flip")
            await h.UpdateNoteAsync(note.Id, n => n.Kind = LandNotificationKind.DispatchBase);
        var cached = await PassesAsync(h, note.Id, 2);
        cached.Select(p => p.ReceiptSelects).ShouldBe([1, 0], "a cached miss before the change");
        await ShouldStayOpenAsync(h, note);

        long? confirmAt = null;
        var floor = Floor;
        switch (change)
        {
            case "lower-baseline-includes-existing-match":
                floor = Floor - 1;
                await h.UpdateRowAsync(row.Id, r => r.LastDeliveryBaselineSequence = Floor - 1);
                confirmAt = Floor;
                break;
            case "attempts-increment":
                await h.UpdateRowAsync(row.Id, r => r.DeliveryAttempts = 2);
                break;
            case "expected-body-changed-to-existing-prompt":
                var text = LandReceiptScanHarness.Filler(20);
                await h.UpdateNoteAsync(note.Id, n => n.Body = text);
                await h.UpdateRowAsync(row.Id, r => r.Body = text);
                confirmAt = 20;
                break;
            case "queue-identity-changed":
            {
                var second = await h.Bridge.SeedPendingMessageAsync(note.Body, h.SessionId, deliveryAttempts: 1,
                    baselineSequence: Floor, origin: QueuedMessageOrigin.Delegation, status: QueuedMessageStatus.Sent);
                await h.UpdateNoteAsync(note.Id, n => n.QueueMessageId = second);
                break;
            }
            case "legacy-flag-flip-widens-acceptance":
                await h.UpdateNoteAsync(note.Id, n => n.IsLegacy = false);
                confirmAt = QueuedMatch;
                break;
            case "kind-flip":
                await h.UpdateNoteAsync(note.Id, n => n.Kind = LandNotificationKind.Outcome);
                confirmAt = QueuedMatch;
                break;
        }

        var next = await PassAsync(h, note.Id);
        next.ReceiptSelects.ShouldBe(1, "an attempt or payload change reopens the full scan");
        next.Floors.ShouldBe([floor], "the floor is the keyed row's current baseline, never a cached cursor");
        var metrics = h.Cache!.GetMetrics();
        metrics.Hits.ShouldBe(1);
        if (confirmAt is long sequence)
        {
            await ShouldBeConfirmedAtAsync(h, note, sequence, floor);
            metrics.Proofs.ShouldBe(0);
        }
        else
        {
            await ShouldStayOpenAsync(h, note);
            metrics.Publishes.ShouldBe(2, "the new miss is bound to the changed attempt or row");
            (await PassAsync(h, note.Id)).ReceiptSelects.ShouldBe(0);
            if (change == "queue-identity-changed")
            {
                // The new proof names the new row: re-keying back is a different context again.
                await h.UpdateNoteAsync(note.Id, n => n.QueueMessageId = row.Id);
                (await PassAsync(h, note.Id)).ReceiptSelects.ShouldBe(1);
            }
        }
        ShouldNotHaveTyped(h);
    }

    // V-6 -------------------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("store-absent")]
    [Arguments("store-disabled")]
    [Arguments("destination-running")]
    [Arguments("destination-starting")]
    [Arguments("runner-404-with-retained-persist-failure")]
    [Arguments("runner-pull-persist-fails")]
    [Arguments("timestamp-only-baseline")]
    [Arguments("profiled-completion")]
    [Arguments("pointer-headline-row")]
    [Arguments("legacy-check-note")]
    [Arguments("oversized-body")]
    [Arguments("cache-clock-fault")]
    [Arguments("fallback-confirms-with-unavailable-runner")]
    public async Task C1121_UnknownEvidenceUsesExistingScan(string evidence, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync(new()
        {
            Store = evidence != "store-absent",
            StoreEnabled = evidence != "store-disabled",
            CacheClock = evidence == "cache-clock-fault" ? new FaultingClock() : null,
        });
        var (note, row, _) = await ArrangeAsync(h,
            candidates: evidence == "fallback-confirms-with-unavailable-runner"
                ? body => seq => seq == Floor + 1 ? (TranscriptKinds.UserPrompt, body) : (TranscriptKinds.UserPrompt, LandReceiptScanHarness.Filler(seq))
                : null,
            row: evidence == "timestamp-only-baseline" ? r => r.LastDeliveryBaselineSequence = null : null,
            destination: evidence switch
            {
                "destination-running" => SessionStatus.Running,
                "destination-starting" => SessionStatus.Starting,
                _ => SessionStatus.Stopped,
            });
        int[]? commands = null;
        switch (evidence)
        {
            case "runner-404-with-retained-persist-failure":
                h.SaveFault.Armed = true;
                await h.IngestAsync(h.SessionId, (TranscriptKinds.AssistantText, "a write that never commits"));
                h.SaveFault.Armed = false;
                h.Runtime.TryGetTranscriptPersistFailure(h.SessionId, out _).ShouldBeTrue();
                break;
            case "runner-pull-persist-fails":
                // The runner holds a new row on every pull; its insert (and its stub) is refused.
                h.Runner.Transcript = Mode.Entries;
                h.Runner.NextEntries = session => [h.Event(session, 0, TranscriptKinds.AssistantText, "refused",
                    uuid: LandReceiptScanHarness.RejectTranscriptUuid.Prefix + Guid.NewGuid().ToString("N"))];
                break;
            case "profiled-completion":
            {
                var wire = note.Body;
                await h.UpdateNoteAsync(note.Id, n =>
                {
                    n.Kind = LandNotificationKind.TaskCompletion;
                    n.CompletionSnapshotJson = "{}";
                    n.CompletionDeliveryJson = TaskCompletionNotification.SerializeDelivery(new TaskCompletionNotification.Delivery(
                        TaskCompletionNotification.SnapshotVersion, "raw", wire, wire, TaskCompletionNotification.Sha256(wire),
                        [row.Id], null, null, DateTime.UtcNow));
                });
                commands = [7, 7, 7]; // its existing delivery-generation SELECT, every pass
                break;
            }
            case "pointer-headline-row":
                await h.UpdateRowAsync(row.Id, r =>
                {
                    r.RemoteSpillBody = note.Body;
                    r.Body = TypedBodySpill.PointerHeadline + "\nRead " + TypedBodySpill.InboxRelativePath(r.Id.ToString("D"));
                });
                break;
            case "legacy-check-note":
                await h.UpdateNoteAsync(note.Id, n => n.Kind = LandNotificationKind.LegacyCheckNote);
                break;
            case "oversized-body":
                // An ordinary body, equal on note and row, refused only for its size.
                var oversized = note.Body + "\n" + new string('d', LandReceiptScanCache.MaxExpectedTextChars);
                oversized.Length.ShouldBeGreaterThan(LandReceiptScanCache.MaxExpectedTextChars);
                await h.UpdateNoteAsync(note.Id, n => n.Body = oversized);
                await h.UpdateRowAsync(row.Id, r => r.Body = oversized);
                break;
        }

        var passes = await PassesAsync(h, note.Id, 3);

        if (evidence == "fallback-confirms-with-unavailable-runner")
        {
            passes[0].ReceiptSelects.ShouldBe(1);
            passes[0].Pulls.ShouldBe(1);
            await ShouldBeConfirmedAtAsync(h, note, Floor + 1);
        }
        else
        {
            passes.Select(p => p.ReceiptSelects).ShouldBe([1, 1, 1], "unknown evidence keeps today's scan every pass");
            passes.ShouldAllBe(p => p.Pulls == 1);
            if (commands is not null)
                passes.Select(p => p.Commands).ShouldBe(commands, string.Join("\n----\n", passes.Select(p => p.Roster)));
            var saved = await h.NoteAsync(note.Id);
            saved.State.ShouldBe(LandNotificationState.AwaitingReceipt);
            saved.ConfirmedAt.ShouldBeNull();
            saved.EnqueueAttempts.ShouldBe(note.EnqueueAttempts, "a refusal is never a failed reconcile");
            saved.LastErrorCode?.ShouldNotStartWith("notification_reconcile_failed");
            if (evidence is "cache-clock-fault" or "store-absent" or "store-disabled" or "destination-running")
                saved.LastErrorCode.ShouldBeNull();
        }
        var metrics = h.Cache!.GetMetrics();
        metrics.Proofs.ShouldBe(0);
        metrics.Publishes.ShouldBe(0);
        metrics.Hits.ShouldBe(0);
        ShouldNotHaveTyped(h);
    }

    // V-10 ------------------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("cached-stays-open")]
    [Arguments("parent-rehomed-to-row-destination")]
    [Arguments("row-destination-changed")]
    public async Task C1121_ForeignDestinationNoteStaysOpenExactlyAsToday(string arm, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        var parent = h.SessionId;
        var (note, row, _) = await ArrangeAsync(h);
        // CARD-1157 shape: the keyed row was delivered to B, whose transcript holds the complete body.
        var other = await h.AddStoppedSessionAsync();
        var atOther = (await h.IngestEventsAsync(other, [h.Event(other, Floor + 5, TranscriptKinds.UserPrompt, note.Body)])).Single();
        atOther.ShouldBeGreaterThan(Floor);
        await h.UpdateRowAsync(row.Id, r => r.AgentSessionId = other);

        var cached = await PassesAsync(h, note.Id, 3);
        cached.Select(p => p.ReceiptSelects).ShouldBe([1, 0, 0]);
        cached[0].Sessions.ShouldBe([parent], "the scan target stays the note's parent session");
        cached[0].ReceiptRows.ShouldBe(48);
        await ShouldStayOpenAsync(h, note);
        h.Cache!.GetMetrics().Publishes.ShouldBe(1);

        switch (arm)
        {
            case "cached-stays-open":
            {
                // The same fixture reconciled with no cache registered: every outcome column is today's.
                await h.SetStatusAsync(parent, SessionStatus.Running);
                var control = await h.SeedLinkedNoteAsync();
                await h.SetStatusAsync(parent, SessionStatus.Stopped);
                var controlRow = await h.MarkSentAsync(control, r => r.AgentSessionId = other);
                await using var db = h.Fixture();
                var today = new AgentTaskLandNotificationService(db, h.Bridge.Queue, new CompletionNoteFlushQueue(), h.Runtime, TimeProvider.System);
                for (var i = 0; i < 3; i++) await today.ReconcileAsync(control.Id, ct);
                var a = await h.NoteAsync(note.Id);
                var b = await h.NoteAsync(control.Id);
                (a.State, a.ConfirmedAt, a.ConfirmingPromptSequence, a.LastErrorCode, a.EnqueueAttempts, a.ParentSessionId, a.QueueMessageId is null)
                    .ShouldBe((b.State, b.ConfirmedAt, b.ConfirmingPromptSequence, b.LastErrorCode, b.EnqueueAttempts, b.ParentSessionId, b.QueueMessageId is null));
                (await h.RowAsync(controlRow.Id)).AgentSessionId.ShouldBe(other);
                break;
            }
            case "parent-rehomed-to-row-destination":
            {
                await h.UpdateNoteAsync(note.Id, n => n.ParentSessionId = other);
                var next = await PassAsync(h, note.Id);
                next.ReceiptSelects.ShouldBe(1);
                next.Sessions.ShouldBe([other]);
                await ShouldBeConfirmedAtAsync(h, note, atOther);
                break;
            }
            case "row-destination-changed":
            {
                await h.UpdateRowAsync(row.Id, r => r.AgentSessionId = parent);
                var next = await PassAsync(h, note.Id);
                next.ReceiptSelects.ShouldBe(1);
                next.Sessions.ShouldBe([parent]);
                next.ReceiptRows.ShouldBe(48);
                await ShouldStayOpenAsync(h, note);
                h.Cache!.GetMetrics().Publishes.ShouldBe(2);
                break;
            }
        }
        ShouldNotHaveTyped(h);
    }

    // V-11 ------------------------------------------------------------------------------------------

    [Test]
    [Timeout(180_000)]
    [Arguments("running-destination-real-delivery")]
    [Arguments("relaunch-after-cached-miss")]
    public async Task C1121_RealQueueDeliveryIsNeverHiddenByTheCache(string arm, CancellationToken ct)
    {
        await using var h = await LandReceiptScanHarness.CreateAsync();
        if (arm == "running-destination-real-delivery")
        {
            AgentTaskLandNotification note = null!;
            // A body that fits one write, so the queue types it whole rather than spilling it.
            note = await h.SeedLinkedNoteAsync("C1121 real delivery outcome", beforeEnqueue: async seeded =>
                await h.SeedTranscriptAsync(h.SessionId));
            await h.AssertCoherentAsync(h.SessionId);
            // The real queue types the keyed row; the submission lands through the runtime ingest.
            await h.Bridge.Queue.FlushIfIdleAsync(h.SessionId, ct);
            h.Bridge.Adapter.SubmittedBodies.ShouldNotBeEmpty();
            var row = await h.RowAsync(note.QueueMessageId!.Value);
            row.Status.ShouldBe(QueuedMessageStatus.Sent);
            row.DeliveryAttempts.ShouldBeGreaterThan(0);
            var floor = row.LastDeliveryBaselineSequence.ShouldNotBeNull();

            var pass = await PassAsync(h, note.Id);
            pass.ReceiptSelects.ShouldBe(1);
            row.Body.ShouldBe(note.Body, "typed whole");
            var confirmed = await h.InMemoryFirstReceiptAsync(h.SessionId, floor, false, LandNotificationKind.Outcome, note.Body);
            await ShouldBeConfirmedAtAsync(h, note, confirmed.ShouldNotBeNull(), floor);
            h.Cache!.GetMetrics().Publishes.ShouldBe(0, "a live destination is never certified (A-1)");
            h.Runner.KillCalls.ShouldBe(0);
        }
        else
        {
            var (note, _, _) = await ArrangeAsync(h);
            (await PassesAsync(h, note.Id, 2)).Select(p => p.ReceiptSelects).ShouldBe([1, 0]);
            // The recipient comes back to life and takes the prompt the way the launch path does.
            await h.SetStatusAsync(h.SessionId, SessionStatus.Running);
            await h.Bridge.Adapter.OnSubmitted!(note.Body);
            var next = await PassAsync(h, note.Id);
            next.ReceiptSelects.ShouldBe(1, "a running destination is outside the whitelist");
            var max = (await h.Store!.ReadAsync(h.SessionId, ct)).LastSequence;
            await ShouldBeConfirmedAtAsync(h, note, max - 1); // the prompt, then its TurnEnd
            ShouldNotHaveTyped(h);
        }
    }

    private sealed class FaultingClock : TimeProvider
    {
        public override long GetTimestamp() => throw new InvalidOperationException("cache clock fault");
    }
}
