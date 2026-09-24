using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0650 V-4 receipt: only a complete submitted prompt, in the frozen destination, past the
/// committed floor, confirms a watchdog attempt. A committed attempt is never typed again.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public sealed class ExpectationReceiptTests
{
    [Test]
    public async Task C650_Complete_submitted_prompt_confirms_both_supported_kinds()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var userPrompt = await f.NudgeAsync();
        (await f.DeliverAsync(userPrompt.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        var first = await f.ReloadAsync(userPrompt.Id);
        first.AttemptState.ShouldBe(ExpectationAttemptState.Confirmed);
        first.ReceiptAt.ShouldNotBeNull();

        // A working session records a submitted queued command as QueuedUserPrompt.
        await f.Harness.MarkWorkingAsync();
        f.Harness.Adapter.OnSubmitted = submitted =>
            BridgeQueueHarness.InsertEntryAsync(f.SessionId, TranscriptKinds.QueuedUserPrompt, submitted,
                timestamp: DateTime.UtcNow, connectionString: f.Schema.ConnectionString);
        var queuedPrompt = await f.NudgeAsync();
        (await f.DeliverAsync(queuedPrompt.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        var second = await f.ReloadAsync(queuedPrompt.Id);
        second.AttemptState.ShouldBe(ExpectationAttemptState.Confirmed);
        second.ReceiptAt.ShouldNotBeNull();
        f.Harness.Adapter.SubmittedBodies.ShouldBe([userPrompt.Body, queuedPrompt.Body]);
    }

    [Test]
    public async Task C650_Rejects_wrong_session_floor_partial_and_housekeeping_receipts()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var generation = await f.GenerationAsync();
        var other = await f.AddSessionAsync(ownedByStandingAgent: true, makeCurrent: false);

        // Below or at the floor: an old complete record is not this attempt's receipt.
        var old = await f.NudgeAsync();
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, old.Body);
        var oldNudge = await Attempted(f, old.Body, generation, await f.MaxSequenceAsync());

        // Another session: a complete record there confirms nothing here.
        var otherNudge = await Attempted(f, null, generation, await f.MaxSequenceAsync());
        await f.AppendTranscriptAsync(other, TranscriptKinds.UserPrompt, otherNudge.Body);

        // Partial: identity without completeness.
        var partialNudge = await Attempted(f, null, generation, await f.MaxSequenceAsync());
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, partialNudge.Body[..(partialNudge.Body.Length / 2)]);

        // Housekeeping rows carry the whole text and are still not a submitted prompt.
        var housekeepingNudge = await Attempted(f, null, generation, await f.MaxSequenceAsync());
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.QueueEnqueue, housekeepingNudge.Body);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.QueueDequeue, housekeepingNudge.Body);
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.AssistantText, housekeepingNudge.Body);

        foreach (var nudge in new[] { oldNudge, otherNudge, partialNudge, housekeepingNudge })
        {
            await f.DeliverAsync(nudge.Id);
            var stored = await f.ReloadAsync(nudge.Id);
            stored.AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed, nudge.Body);
            stored.ReceiptAt.ShouldBeNull();
        }
        f.Harness.Adapter.Inputs.ShouldBeEmpty("receipt reconciliation never types");

        // Control: the complete prompt past the floor in the destination confirms.
        await f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, housekeepingNudge.Body);
        await f.DeliverAsync(housekeepingNudge.Id);
        (await f.ReloadAsync(housekeepingNudge.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Confirmed);
    }

    [Test]
    public async Task C650_Crash_after_attempt_commit_never_retypes()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var generation = await f.GenerationAsync();
        var floor = await f.MaxSequenceAsync();
        var crashed = await f.NudgeAsync(state: ExpectationAttemptState.Attempting,
            destination: f.SessionId, generation: generation, baseline: floor);

        var first = await f.DeliverAsync(crashed.Id);
        var second = await f.DeliverAsync(crashed.Id);

        f.Harness.Adapter.Inputs.ShouldBeEmpty("a committed attempt is never typed again, even after a crash");
        first.Outcome.ShouldBe(ExpectationSendOutcome.Uncertain);
        second.Outcome.ShouldBe(ExpectationSendOutcome.Uncertain);
        var stored = await f.ReloadAsync(crashed.Id);
        stored.AttemptState.ShouldBe(ExpectationAttemptState.Uncertain);
        stored.DestinationSessionId.ShouldBe(f.SessionId);
        stored.BaselineSequence.ShouldBe(floor);
        stored.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due);
        f.CatchUp.Pulled.ShouldContain(f.SessionId, "catch-up precedes the absence judgment");
    }

    [Test]
    public async Task C650_Late_receipt_recovers_uncertain_attempt()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var generation = await f.GenerationAsync();
        var floor = await f.MaxSequenceAsync();
        var uncertain = await f.NudgeAsync(state: ExpectationAttemptState.Uncertain,
            destination: f.SessionId, generation: generation, baseline: floor);
        // The record only exists once the pull has run: the catch-up must come first.
        f.CatchUp.OnCatchUp = () => f.AppendTranscriptAsync(f.SessionId, TranscriptKinds.UserPrompt, uncertain.Body);

        var result = await f.DeliverAsync(uncertain.Id);

        result.Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        var stored = await f.ReloadAsync(uncertain.Id);
        stored.AttemptState.ShouldBe(ExpectationAttemptState.Confirmed);
        stored.ReceiptAt.ShouldNotBeNull();
        f.Harness.Adapter.Inputs.ShouldBeEmpty("late receipt is found, not re-created");
    }

    [Test]
    public async Task C650_No_observable_baseline_never_invents_confirmation()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync(observable: false);
        (await f.MaxSequenceAsync()).ShouldBe(0, "sanity: no transcript row, so no floor");
        var nudge = await f.NudgeAsync();

        var result = await f.DeliverAsync(nudge.Id);

        f.Harness.Adapter.SubmittedBodies.ShouldBe([nudge.Body], "the body was typed once");
        result.Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        var stored = await f.ReloadAsync(nudge.Id);
        stored.AttemptState.ShouldBe(ExpectationAttemptState.Submitted, "submitted, never confirmed");
        stored.BaselineSequence.ShouldBeNull();
        stored.ReceiptAt.ShouldBeNull();

        // A later pass still has no floor to judge a matching row against.
        (await f.DeliverAsync(nudge.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        (await f.ReloadAsync(nudge.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Submitted);
        f.Harness.Adapter.SubmittedBodies.Count.ShouldBe(1);
    }

    [Test]
    public async Task C650_New_session_does_not_adopt_old_attempt()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var generation = await f.GenerationAsync();
        var floor = await f.MaxSequenceAsync();
        var oldAttempt = await f.NudgeAsync(state: ExpectationAttemptState.Unconfirmed,
            destination: f.SessionId, generation: generation, baseline: floor);

        // The standing agent restarted into a new session, which happens to echo the old body.
        var replacement = await f.AddSessionAsync(ownedByStandingAgent: true, makeCurrent: true);
        var replacementAdapter = new Antiphon.Tests.Agents.FakeAgentProtocolAdapter();
        f.Harness.Runtime.Register(replacement, replacementAdapter);
        await f.AppendTranscriptAsync(replacement, TranscriptKinds.UserPrompt, oldAttempt.Body);

        await f.DeliverAsync(oldAttempt.Id);

        var stored = await f.ReloadAsync(oldAttempt.Id);
        stored.AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed);
        stored.DestinationSessionId.ShouldBe(f.SessionId, "the frozen destination never moves");
        stored.ReceiptAt.ShouldBeNull();
        replacementAdapter.Inputs.ShouldBeEmpty("the old attempt is not replayed into the new session");
        f.Harness.Adapter.Inputs.ShouldBeEmpty();

        // A new nudge goes to the current owned session.
        var fresh = await f.NudgeAsync();
        await f.DeliverAsync(fresh.Id);
        replacementAdapter.Inputs.ShouldContain(fresh.Body);
        (await f.ReloadAsync(fresh.Id)).DestinationSessionId.ShouldBe(replacement);
    }

    private static Task<Antiphon.Server.Domain.Entities.ExpectationNudge> Attempted(
        ExpectationDeliveryFixture f, string? body, DateTime generation, long floor) =>
        f.NudgeAsync(body: body, state: ExpectationAttemptState.Unconfirmed,
            destination: f.SessionId, generation: generation, baseline: floor);
}
