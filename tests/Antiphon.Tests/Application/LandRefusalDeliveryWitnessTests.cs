using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1196. A live LandRefused outcome travels through the real session queue to one
/// complete recipient UserPrompt, then real reconciliation clears an older historical refusal.
/// The adapter is a controlled provider substitute, not a native PTY or transcript file.
/// </summary>
[Category("Integration")]
[NotInParallel("c1196-land-refusal")]
public sealed class LandRefusalDeliveryWitnessTests
{
    /// <summary>
    /// case: idle caller, immediate or restart-after-terminal, receives one complete UserPrompt
    /// and real reconciliation drops the older historical key. mutation: drop the later-outcome
    /// resolution, the producer outcome, or the new-row wakeup. red: history stays, produced-outcome
    /// count is 0, or the bounded wakeup is missed.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("immediate")]
    [Arguments("restart-after-terminal")]
    public async Task C1196_Eligible_recipient_receipt_resolves_older_refusal(string mode, CancellationToken ct)
    {
        await using var world = await LandRefusalDeliveryHarness.CreateAsync();
        var produced = await world.ProduceAsync();
        if (mode == "restart-after-terminal")
        {
            await world.RestartAsync();
            var survived = await world.ReadNoteAsync(produced.NoteId);
            survived.Id.ShouldBe(produced.NoteId);
            survived.Body.ShouldBe(produced.Body);
            survived.Digest.ShouldBe(produced.Digest);
            survived.SourceEventId.ShouldBe(produced.EventId);
            survived.ParentSessionId.ShouldBe(produced.SessionId);
            survived.QueueMessageId.ShouldBeNull();
            survived.ConfirmedAt.ShouldBeNull();
        }

        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");
        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(0, "C1196:before-wakeup-submissions");
        var linked = await world.ReadKeyedAsync(produced.NoteId);
        var row = linked.ShouldNotBeNull();
        row.Attempts.ShouldBe(0);
        row.SessionId.ShouldBe(world.SessionId);
        row.Body.ShouldBe(produced.Body);
        row.Digest.ShouldBe(produced.Digest);
        row.SourceTaskId.ShouldBe(produced.TaskId);
        (await world.ReadNoteAsync(produced.NoteId)).QueueMessageId.ShouldBe(row.Id);
        (await world.CountKeyedAsync(produced.NoteId)).ShouldBe(1);

        (await world.ConsumeWakeupAsync()).ShouldBeTrue("C1196:wakeup-received");
        await AssertDeliveredAsync(world, produced, row.Id);
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");
        (await world.ReadNoteAsync(produced.NoteId)).ConfirmedAt.ShouldBeNull();

        await world.ReconcileLiveAsync();
        await AssertConfirmedAsync(world, produced);
        (await world.ReadHistoryKeysAsync()).ShouldNotContain(world.HistoricalKey, "C1196:history-resolved");

        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(1, "C1196:submission-count");
        (await world.ReadNoteAsync(produced.NoteId)).ConfirmingSequence.ShouldNotBeNull();
        (await world.ReadHistoryKeysAsync()).ShouldNotContain(world.HistoricalKey, "C1196:history-resolved");
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// case: a working caller stays Pending and untyped until a recorded TurnEnd, then the same
    /// row completes. mutation: admit FlushIfIdleAsync while working. red: a submission exists
    /// before the turn ends.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1196_Busy_recipient_waits_for_turn_end_before_resolution(CancellationToken ct)
    {
        await using var world = await LandRefusalDeliveryHarness.CreateAsync();
        await world.MarkWorkingAsync();
        (await world.IsWorkingAsync()).ShouldBeTrue();
        var produced = await world.ProduceAsync();
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey);

        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(0, "C1196:before-wakeup-submissions");
        (await world.ConsumeWakeupAsync()).ShouldBeTrue("C1196:wakeup-received");
        var pending = (await world.ReadKeyedAsync(produced.NoteId)).ShouldNotBeNull();
        pending.Status.ShouldBe(QueuedMessageStatus.Pending);
        pending.Attempts.ShouldBe(0);
        world.Submissions.Count.ShouldBe(0, "C1196:busy-submissions");
        (await world.ReadPromptsAsync(world.SessionId, TranscriptKinds.UserPrompt))
            .ShouldNotContain(p => PromptSubmissionMatch.IsConfirmedBy(produced.Body, p.Text));
        (await world.ReadNoteAsync(produced.NoteId)).ConfirmedAt.ShouldBeNull();
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");

        await world.MarkIdleTurnAsync();
        (await world.IsWorkingAsync()).ShouldBeFalse();
        await world.OnTurnEndAsync();
        await AssertDeliveredAsync(world, produced, pending.Id);
        (await world.ReadKeyedAsync(produced.NoteId))!.Id.ShouldBe(pending.Id);
        (await world.ReadKeyedAsync(produced.NoteId))!.Body.ShouldBe(produced.Body);

        await world.ReconcileLiveAsync();
        await AssertConfirmedAsync(world, produced);
        (await world.ReadHistoryKeysAsync()).ShouldNotContain(world.HistoricalKey, "C1196:history-resolved");
        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(1, "C1196:submission-count");
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// case: an enqueue fault leaves the same note RetryPending, then the same obligation links
    /// after its persisted due time. mutation: treat RetryPending as terminal, or commit the keyed
    /// row before the queue save fault. red: the backlink stays null at the due time, or a row
    /// exists before that time.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("before-enqueue")]
    [Arguments("queue-save")]
    public async Task C1196_Enqueue_failure_recovers_the_same_obligation(string mode, CancellationToken ct)
    {
        var cut = mode == "before-enqueue" ? RefusalCut.BeforeEnqueue
            : mode == "queue-save" ? RefusalCut.QueueSave
            : throw new ArgumentException(mode);
        await using var world = await LandRefusalDeliveryHarness.CreateAsync(new RefusalFault(cut));
        var produced = await world.ProduceAsync();
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");
        await world.ReconcileLiveAsync();
        world.Fault.Fired.ShouldBe(1);
        world.Fault.Armed = false;
        world.Submissions.Count.ShouldBe(0);
        (await world.CountKeyedAsync()).ShouldBe(0);
        var failed = await world.ReadNoteAsync();
        failed.State.ShouldBe(LandNotificationState.RetryPending);
        failed.QueueMessageId.ShouldBeNull();
        failed.NextAttemptAt.ShouldBeGreaterThan(world.Clock.GetUtcNow().UtcDateTime);
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey);

        await world.ReconcileLiveAsync();
        (await world.CountKeyedAsync()).ShouldBe(0, "C1196:before-due-rows");
        world.Submissions.Count.ShouldBe(0);

        await world.RestartAsync();
        await world.AdvanceToPersistedDueAsync();
        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(0, "C1196:before-wakeup-submissions");
        var linkedNote = await world.ReadNoteAsync();
        linkedNote.QueueMessageId.ShouldNotBeNull("C1196:retry-linked");
        var linked = (await world.ReadKeyedAsync()).ShouldNotBeNull();
        linkedNote.QueueMessageId.ShouldBe(linked.Id);
        linked.Attempts.ShouldBe(0);
        linked.Body.ShouldBe(produced.Body);
        linked.Digest.ShouldBe(produced.Digest);
        await WakeAndConfirmAsync(world, produced, linked.Id, "C1196:wakeup-received");
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// case: cancellation after the queue row commits leaves the note unlinked, and restart
    /// reattaches that same row. mutation: skip the existing-row link, or skip its wakeup.
    /// red: the backlink stays null, or the recovered signal never arrives.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1196_Crash_after_queue_insert_recovers_the_existing_key(CancellationToken ct)
    {
        await using var world = await LandRefusalDeliveryHarness.CreateAsync(new RefusalFault(RefusalCut.QueueInserted));
        var produced = await world.ProduceAsync();
        await Should.ThrowAsync<OperationCanceledException>(world.ReconcileLiveAsync);
        world.Fault.Fired.ShouldBe(1);
        var row = (await world.ReadKeyedAsync()).ShouldNotBeNull();
        (await world.ReadNoteAsync()).QueueMessageId.ShouldBeNull("C1196:cut-unlinked");
        (await world.CountKeyedAsync()).ShouldBe(1);
        row.Attempts.ShouldBe(0);
        world.Submissions.Count.ShouldBe(0);
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");

        await world.RestartAsync();
        await world.ReconcileLiveAsync();
        (await world.ReadNoteAsync()).QueueMessageId.ShouldBe(row.Id, "C1196:recovered-link");
        world.Submissions.Count.ShouldBe(0, "C1196:before-wakeup-submissions");
        (await world.ConsumeWakeupAsync()).ShouldBeTrue("C1196:recovered-wakeup");
        await AssertDeliveredAsync(world, produced, row.Id);
        await ConfirmResolvedAsync(world, produced);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// case: a lost wakeup leaves a linked pending row that only the stranded sweep types, including
    /// when the caller is busy. mutation: make the stranded sweep a no-op, or let it type while
    /// working. red: no UserPrompt, or a submission exists while the caller is working.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("eligible")]
    [Arguments("busy")]
    public async Task C1196_Lost_wakeup_is_recovered_by_the_stranded_queue(string mode, CancellationToken ct)
    {
        var busy = mode == "busy" ? true
            : mode == "eligible" ? false
            : throw new ArgumentException(mode);
        await using var world = await LandRefusalDeliveryHarness.CreateAsync(new RefusalFault(RefusalCut.LostWakeup));
        if (busy)
        {
            await world.MarkWorkingAsync();
            (await world.IsWorkingAsync()).ShouldBeTrue();
        }

        var produced = await world.ProduceAsync();
        await Should.ThrowAsync<OperationCanceledException>(world.ReconcileLiveAsync);
        world.Fault.Fired.ShouldBe(1);
        var pending = (await world.ReadKeyedAsync()).ShouldNotBeNull();
        (await world.ReadNoteAsync()).QueueMessageId.ShouldBe(pending.Id);
        pending.Status.ShouldBe(QueuedMessageStatus.Pending);
        pending.Attempts.ShouldBe(0);
        world.Submissions.Count.ShouldBe(0);
        (await world.ReadNoteAsync()).ConfirmedAt.ShouldBeNull();
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");

        await world.RestartAsync();
        if (busy)
        {
            (await world.IsWorkingAsync()).ShouldBeTrue();
            await world.FlushStrandedAsync();
            world.Submissions.Count.ShouldBe(0, "C1196:stranded-busy-submissions");
            pending.Id.ShouldBe((await world.ReadKeyedAsync())!.Id);
            (await world.ReadKeyedAsync())!.Attempts.ShouldBe(0);
            await world.MarkIdleTurnAsync();
            (await world.IsWorkingAsync()).ShouldBeFalse();
        }

        await world.FlushStrandedAsync();
        (await world.ReadPromptsAsync(world.SessionId, TranscriptKinds.UserPrompt))
            .Count(p => PromptSubmissionMatch.IsCompleteIn(produced.Body, p.Text))
            .ShouldBe(1, "C1196:stranded-userprompt");
        await AssertDeliveredAsync(world, produced, pending.Id);
        await ConfirmResolvedAsync(world, produced);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// case: a crash after the complete UserPrompt, or before the receipt save, confirms on restart
    /// without typing again. mutation: clear the confirmation assignment, or enqueue again while
    /// recovering the receipt. red: confirmation stays empty, or a second submission appears.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("queue-before-verdict")]
    [Arguments("receipt-before-save")]
    public async Task C1196_Receipt_save_crash_recovers_without_retyping(string mode, CancellationToken ct)
    {
        var cut = mode == "queue-before-verdict" ? RefusalCut.QueueBeforeVerdict
            : mode == "receipt-before-save" ? RefusalCut.ReceiptBeforeSave
            : throw new ArgumentException(mode);
        await using var world = await LandRefusalDeliveryHarness.CreateAsync(new RefusalFault(cut));
        var produced = await world.ProduceAsync();
        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(0, "C1196:before-wakeup-submissions");
        if (cut == RefusalCut.QueueBeforeVerdict)
            await Should.ThrowAsync<OperationCanceledException>(world.ConsumeWakeupAsync);
        else
        {
            (await world.ConsumeWakeupAsync()).ShouldBeTrue("C1196:wakeup-received");
            await Should.ThrowAsync<OperationCanceledException>(world.ReconcileLiveAsync);
        }

        world.Fault.Fired.ShouldBe(1);
        var prompt = (await world.ReadPromptsAsync(world.SessionId, TranscriptKinds.UserPrompt))
            .Where(p => p.Text == world.Submissions.ShouldHaveSingleItem().Text
                && PromptSubmissionMatch.IsCompleteIn(produced.Body, p.Text))
            .ShouldHaveSingleItem();
        var note = await world.ReadNoteAsync();
        note.ConfirmedAt.ShouldBeNull();
        note.ConfirmingSequence.ShouldBeNull();
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");
        if (cut == RefusalCut.QueueBeforeVerdict)
        {
            var sent = (await world.ReadKeyedAsync()).ShouldNotBeNull();
            sent.Status.ShouldBe(QueuedMessageStatus.Sent);
            sent.Verdict.ShouldBeNull();
            sent.Attempts.ShouldBeGreaterThan(0);
        }

        await world.RestartAsync();
        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(1, "C1196:submission-count");
        var saved = await world.ReadNoteAsync();
        (saved.State == LandNotificationState.Confirmed
            && saved.ConfirmedAt is not null
            && saved.ConfirmingSequence == prompt.Sequence)
            .ShouldBeTrue("C1196:receipt-persisted");
        (await world.ReadHistoryKeysAsync()).ShouldNotContain(world.HistoricalKey, "C1196:history-resolved");
        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(1, "C1196:submission-count");
        (await world.ReadNoteAsync()).ConfirmingSequence.ShouldBe(prompt.Sequence);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// case: one invalid receipt condition leaves the live refusal open. mutation: accept Sent
    /// metadata, a head-only body, another session, the baseline sequence, or a zero attempt.
    /// red: ConfirmedAt or the confirming sequence becomes set.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("sent-without-prompt")]
    [Arguments("head-only")]
    [Arguments("wrong-session")]
    [Arguments("at-baseline")]
    [Arguments("no-attempt")]
    public async Task C1196_False_receipt_evidence_keeps_the_refusal_open(string mode, CancellationToken ct)
    {
        await using var world = await LandRefusalDeliveryHarness.CreateAsync();
        if (mode == "sent-without-prompt")
            world.Transcript = LandRefusalDeliveryHarness.TranscriptMode.Suppress;
        else if (mode == "head-only")
            world.Transcript = LandRefusalDeliveryHarness.TranscriptMode.HeadOnly;
        else if (mode == "wrong-session")
            world.Transcript = LandRefusalDeliveryHarness.TranscriptMode.WrongSession;
        else if (mode is not ("at-baseline" or "no-attempt"))
            throw new ArgumentException(mode);

        var produced = await world.ProduceAsync();
        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(0, "C1196:before-wakeup-submissions");
        if (mode == "wrong-session")
        {
            var floor = await world.MaxSequenceAsync(world.SessionId);
            await world.PadOtherSessionAsync((int)floor + 2);
        }

        (await world.ConsumeWakeupAsync()).ShouldBeTrue("C1196:wakeup-received");
        var row = (await world.ReadKeyedAsync()).ShouldNotBeNull();
        row.Attempts.ShouldBeGreaterThan(0);
        row.Baseline.ShouldNotBeNull();
        row.StartedAt.ShouldNotBeNull();
        var submission = world.Submissions.ShouldHaveSingleItem("C1196:submission-count");
        if (mode == "sent-without-prompt")
        {
            submission.InsertedText.ShouldBeNull();
            (await world.ReadPromptsAsync(world.SessionId, TranscriptKinds.UserPrompt))
                .ShouldNotContain(p => PromptSubmissionMatch.IsConfirmedBy(produced.Body, p.Text));
            var attempts = row.Attempts;
            var baseline = row.Baseline;
            await world.SetQueueFieldsAsync(m =>
            {
                m.Status = QueuedMessageStatus.Sent;
                m.DeliveryVerdict = DeliveryVerdict.Delivered;
            });
            var sent = (await world.ReadKeyedAsync()).ShouldNotBeNull();
            sent.Status.ShouldBe(QueuedMessageStatus.Sent);
            sent.Verdict.ShouldBe(DeliveryVerdict.Delivered);
            sent.Attempts.ShouldBe(attempts);
            sent.Baseline.ShouldBe(baseline);
        }
        else if (mode == "head-only")
        {
            produced.Body.Length.ShouldBeGreaterThan(PromptSubmissionMatch.MatchWindowChars);
            submission.InsertedText.ShouldNotBeNull();
            PromptSubmissionMatch.IsConfirmedBy(produced.Body, submission.InsertedText).ShouldBeTrue();
            PromptSubmissionMatch.IsCompleteIn(produced.Body, submission.InsertedText).ShouldBeFalse();
            submission.InsertedKind.ShouldBe(TranscriptKinds.UserPrompt);
        }
        else if (mode == "wrong-session")
        {
            submission.InsertedText.ShouldBe(submission.Text);
            (await world.ReadPromptsAsync(world.SessionId, TranscriptKinds.UserPrompt))
                .ShouldNotContain(p => PromptSubmissionMatch.IsConfirmedBy(produced.Body, p.Text));
            var foreign = (await world.ReadPromptsAsync(world.OtherSessionId, TranscriptKinds.UserPrompt))
                .Where(p => p.Text == submission.Text).ShouldHaveSingleItem();
            foreign.Sequence.ShouldBeGreaterThan(row.Baseline!.Value);
            PromptSubmissionMatch.IsConfirmedBy(produced.Body, foreign.Text).ShouldBeTrue();
            PromptSubmissionMatch.IsCompleteIn(produced.Body, foreign.Text).ShouldBeTrue();
            (await world.ReadKeyedAsync())!.SessionId.ShouldBe(world.SessionId);
        }
        else
        {
            await AssertDeliveredAsync(world, produced, row.Id);
            var prompt = (await world.ReadPromptsAsync(world.SessionId, TranscriptKinds.UserPrompt))
                .Where(p => p.Text == submission.Text).ShouldHaveSingleItem();
            if (mode == "at-baseline")
                await world.SetQueueFieldsAsync(m => m.LastDeliveryBaselineSequence = prompt.Sequence);
            else
                await world.SetQueueFieldsAsync(m => m.DeliveryAttempts = 0);
            var flipped = (await world.ReadKeyedAsync()).ShouldNotBeNull();
            flipped.StartedAt.ShouldNotBeNull();
            if (mode == "at-baseline")
            {
                flipped.Baseline.ShouldBe(prompt.Sequence);
                flipped.Attempts.ShouldBeGreaterThan(0);
            }
            else
            {
                flipped.Attempts.ShouldBe(0);
                flipped.Baseline.ShouldNotBeNull();
            }
        }

        await world.ReconcileLiveAsync();
        await AssertFalseReceiptAsync(world);
        await world.ReconcileLiveAsync();
        await AssertFalseReceiptAsync(world);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// case: a real receipt clears only the older refusal it covers. mutation: ignore task identity,
    /// strict event order, or Outcome kind. red: the protected historical key disappears.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("other-task")]
    [Arguments("newer-refusal")]
    [Arguments("equal-time-refusal")]
    [Arguments("non-outcome")]
    public async Task C1196_Receipt_does_not_resolve_another_task_or_newer_refusal(string mode, CancellationToken ct)
    {
        await using var world = await LandRefusalDeliveryHarness.CreateAsync();
        var produced = await world.ProduceAsync();
        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(0, "C1196:before-wakeup-submissions");
        (await world.ConsumeWakeupAsync()).ShouldBeTrue("C1196:wakeup-received");
        var row = (await world.ReadKeyedAsync()).ShouldNotBeNull();
        await AssertDeliveredAsync(world, produced, row.Id);
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");

        string protectedKey;
        if (mode == "other-task")
        {
            var other = await world.SeedOtherTaskAsync(produced.EventAt.AddDays(-1));
            protectedKey = "legacy-land:" + other.ToString("N");
        }
        else if (mode == "newer-refusal")
        {
            var at = produced.EventAt.AddSeconds(1);
            var id = await world.SeedHistoricalRefusalAsync(produced.TaskId, at);
            world.AdvanceTo(at);
            protectedKey = "legacy-land:" + id.ToString("N");
        }
        else if (mode == "equal-time-refusal")
        {
            var id = await world.SeedHistoricalRefusalAsync(produced.TaskId, produced.EventAt);
            world.AdvanceTo(produced.EventAt);
            protectedKey = "legacy-land:" + id.ToString("N");
        }
        else if (mode == "non-outcome")
            protectedKey = world.HistoricalKey;
        else
            throw new ArgumentException(mode);

        await world.ReconcileLiveAsync();
        await AssertConfirmedAsync(world, produced);
        world.Submissions.Count.ShouldBe(1, "C1196:submission-count");
        if (mode == "non-outcome")
        {
            (await world.ReadHistoryKeysAsync()).ShouldNotContain(world.HistoricalKey, "C1196:history-resolved");
            await world.SetNoteKindAsync(LandNotificationKind.Held);
            (await world.ReadNoteAsync()).Kind.ShouldBe(LandNotificationKind.Held);
        }
        else if (mode == "other-task")
            (await world.ReadHistoryKeysAsync()).ShouldNotContain(world.HistoricalKey, "C1196:history-resolved");

        (await world.ReadHistoryKeysAsync()).ShouldContain(protectedKey, "C1196:protected-history");
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// case: an unlinked legacy note is reconciled twice and never submitted. mutation: delete the
    /// legacy early return. red: a keyed row or a submission appears.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1196_Unlinked_legacy_note_never_submits(CancellationToken ct)
    {
        await using var world = await LandRefusalDeliveryHarness.CreateAsync();
        var noteId = await world.SeedLegacyNoteAsync();
        var note = await world.ReadNoteAsync(noteId);
        note.IsLegacy.ShouldBeTrue();
        note.State.ShouldBe(LandNotificationState.AwaitingReceipt);
        note.QueueMessageId.ShouldBeNull();
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");
        await world.ReconcileLiveAsync();
        await world.ReconcileLiveAsync();
        (await world.CountKeyedAsync(noteId)).ShouldBe(0, "C1196:legacy-keyed-rows");
        world.Submissions.Count.ShouldBe(0);
        (await world.ReadNoteAsync(noteId)).ConfirmedAt.ShouldBeNull();
        (await world.ReadNoteAsync(noteId)).QueueMessageId.ShouldBeNull();
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// case: one transport failure before typing reverts the same row, and the stranded sweep
    /// retries it once. mutation: skip the revert. red: the row stays Sent after the fault.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task C1196_Pretyping_transport_failure_recovers_the_same_row(CancellationToken ct)
    {
        await using var world = await LandRefusalDeliveryHarness.CreateAsync(new RefusalFault(RefusalCut.QueueBeforeTyping));
        var produced = await world.ProduceAsync();
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");
        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(0, "C1196:before-wakeup-submissions");
        (await world.ConsumeWakeupAsync()).ShouldBeTrue("C1196:wakeup-received");
        world.Fault.Fired.ShouldBe(1);
        var reverted = (await world.ReadKeyedAsync()).ShouldNotBeNull();
        (reverted.Status == QueuedMessageStatus.Pending
            && reverted.Attempts == 1
            && reverted.Baseline is not null
            && reverted.StartedAt is not null)
            .ShouldBeTrue("C1196:pretyping-reverted");
        world.Submissions.Count.ShouldBe(0);
        (await world.ReadPromptsAsync(world.SessionId, TranscriptKinds.UserPrompt))
            .ShouldNotContain(p => PromptSubmissionMatch.IsConfirmedBy(produced.Body, p.Text));
        (await world.ReadNoteAsync()).ConfirmedAt.ShouldBeNull();
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey);

        await world.RestartAsync();
        (await world.IsWorkingAsync()).ShouldBeFalse();
        await world.FlushStrandedAsync();
        var retried = (await world.ReadKeyedAsync()).ShouldNotBeNull();
        retried.Id.ShouldBe(reverted.Id);
        retried.Attempts.ShouldBe(2);
        await AssertDeliveredAsync(world, produced, reverted.Id);
        await ConfirmResolvedAsync(world, produced);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// case: recovery refuses a keyed row whose destination or digest differs, then accepts the
    /// restored row. mutation: link despite a destination or digest mismatch. red: the backlink is
    /// set while the mismatch remains.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    [Arguments("wrong-destination")]
    [Arguments("wrong-digest")]
    [Arguments("missing-digest")]
    public async Task C1196_Recovery_link_requires_matching_destination_and_digest(string mode, CancellationToken ct)
    {
        await using var world = await LandRefusalDeliveryHarness.CreateAsync(new RefusalFault(RefusalCut.QueueInserted));
        var produced = await world.ProduceAsync();
        await Should.ThrowAsync<OperationCanceledException>(world.ReconcileLiveAsync);
        var row = (await world.ReadKeyedAsync()).ShouldNotBeNull();
        (await world.ReadNoteAsync()).QueueMessageId.ShouldBeNull("C1196:cut-unlinked");
        row.SessionId.ShouldBe(world.SessionId);
        row.Digest.ShouldBe(produced.Digest);
        world.Submissions.Count.ShouldBe(0);
        if (mode == "wrong-destination")
            await world.SetQueueFieldsAsync(m => m.AgentSessionId = world.OtherSessionId);
        else if (mode == "wrong-digest")
            await world.SetQueueFieldsAsync(m => m.ContentDigest = "c1196-wrong-digest");
        else if (mode == "missing-digest")
            await world.SetQueueFieldsAsync(m => m.ContentDigest = null);
        else
            throw new ArgumentException(mode);

        await world.ReconcileLiveAsync();
        var refused = await world.ReadNoteAsync();
        refused.QueueMessageId.ShouldBeNull("C1196:identity-refused-link");
        refused.LastError.ShouldNotBeNull().ShouldContain("ConflictException");
        (await world.CountKeyedAsync()).ShouldBe(1);
        world.Submissions.Count.ShouldBe(0);
        refused.ConfirmedAt.ShouldBeNull();
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");
        (await world.ReadKeyedAsync())!.Attempts.ShouldBe(0);

        if (mode == "wrong-destination")
            await world.SetQueueFieldsAsync(m => m.AgentSessionId = world.SessionId);
        else
            await world.SetQueueFieldsAsync(m => m.ContentDigest = produced.Digest);
        await world.RestartAsync();
        await world.AdvanceToPersistedDueAsync();
        await world.ReconcileLiveAsync();
        (await world.ReadNoteAsync()).QueueMessageId.ShouldBe(row.Id, "C1196:recovered-link");
        world.Submissions.Count.ShouldBe(0, "C1196:before-wakeup-submissions");
        (await world.ConsumeWakeupAsync()).ShouldBeTrue("C1196:recovered-wakeup");
        await AssertDeliveredAsync(world, produced, row.Id);
        await ConfirmResolvedAsync(world, produced);
        ct.ThrowIfCancellationRequested();
    }

    private static async Task WakeAndConfirmAsync(
        LandRefusalDeliveryHarness world, LandRefusalDeliveryHarness.Produced produced, Guid queueId, string wakeupLabel)
    {
        (await world.ConsumeWakeupAsync()).ShouldBeTrue(wakeupLabel);
        await AssertDeliveredAsync(world, produced, queueId);
        await ConfirmResolvedAsync(world, produced);
    }

    private static async Task ConfirmResolvedAsync(
        LandRefusalDeliveryHarness world, LandRefusalDeliveryHarness.Produced produced)
    {
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");
        (await world.ReadNoteAsync(produced.NoteId)).ConfirmedAt.ShouldBeNull();
        await world.ReconcileLiveAsync();
        await AssertConfirmedAsync(world, produced);
        (await world.ReadHistoryKeysAsync()).ShouldNotContain(world.HistoricalKey, "C1196:history-resolved");
        await world.ReconcileLiveAsync();
        world.Submissions.Count.ShouldBe(1, "C1196:submission-count");
    }

    private static async Task AssertFalseReceiptAsync(LandRefusalDeliveryHarness world)
    {
        var note = await world.ReadNoteAsync();
        (note.ConfirmedAt is null && note.ConfirmingSequence is null)
            .ShouldBeTrue("C1196:false-evidence-unconfirmed");
        (await world.ReadHistoryKeysAsync()).ShouldContain(world.HistoricalKey, "C1196:history-before-confirm");
    }

    private static async Task AssertDeliveredAsync(
        LandRefusalDeliveryHarness world, LandRefusalDeliveryHarness.Produced produced, Guid queueId)
    {
        var submission = world.Submissions.ShouldHaveSingleItem("C1196:submission-count");
        (submission.RowFound && submission.Attempts == 1 && submission.Baseline is not null)
            .ShouldBeTrue("C1196:claim-durable-at-submit");
        submission.InsertedKind.ShouldBe(TranscriptKinds.UserPrompt);
        submission.InsertedText.ShouldBe(submission.Text);
        var prompts = await world.ReadPromptsAsync(world.SessionId, TranscriptKinds.UserPrompt);
        var prompt = prompts.Where(p => p.Text == submission.Text).ShouldHaveSingleItem();
        prompt.Sequence.ShouldBeGreaterThan(submission.Baseline!.Value);
        PromptSubmissionMatch.IsConfirmedBy(produced.Body, prompt.Text).ShouldBeTrue();
        PromptSubmissionMatch.IsCompleteIn(produced.Body, prompt.Text).ShouldBeTrue();
        var row = (await world.ReadKeyedAsync(produced.NoteId)).ShouldNotBeNull();
        row.Id.ShouldBe(queueId);
        row.Attempts.ShouldBeGreaterThan(0);
        row.StartedAt.ShouldNotBeNull();
        row.Baseline.ShouldNotBeNull();
        row.Body.ShouldBe(produced.Body);
        row.Digest.ShouldBe(produced.Digest);
    }

    private static async Task AssertConfirmedAsync(
        LandRefusalDeliveryHarness world, LandRefusalDeliveryHarness.Produced produced)
    {
        var note = await world.ReadNoteAsync(produced.NoteId);
        note.State.ShouldBe(LandNotificationState.Confirmed);
        note.ConfirmedAt.ShouldNotBeNull();
        note.ConfirmingSequence.ShouldNotBeNull();
        note.Body.ShouldBe(produced.Body);
        note.Digest.ShouldBe(produced.Digest);
        var prompts = await world.ReadPromptsAsync(world.SessionId, TranscriptKinds.UserPrompt);
        prompts.ShouldContain(p => p.Sequence == note.ConfirmingSequence
            && PromptSubmissionMatch.IsCompleteIn(produced.Body, p.Text));
    }
}
