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
