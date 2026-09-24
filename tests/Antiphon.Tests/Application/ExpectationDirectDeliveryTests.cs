using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0650 V-4 direct delivery: the watchdog types through the real queue service, bypasses the
/// WhenIdle policy, and never kills, restarts, escapes or retypes.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public sealed class ExpectationDirectDeliveryTests
{
    [Test]
    public async Task C650_Working_caller_receives_prompt_without_note_or_lease_progress()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        await f.Harness.MarkWorkingAsync();
        // The ordinary caller note is stuck behind the working turn, exactly as overnight.
        var queued = await f.Harness.Queue.EnqueueAsync(
            f.SessionId, "land note for CARD-0641", MessageSendMode.WhenIdle, CancellationToken.None);
        queued.Working.ShouldBeTrue("sanity: the caller is mid-turn");
        f.Harness.Adapter.Inputs.ShouldBeEmpty("sanity: WhenIdle held the note");
        var nudge = await f.NudgeAsync();

        var result = await f.DeliverAsync(nudge.Id);

        f.Harness.Adapter.SubmittedBodies.ShouldBe([nudge.Body], "the watchdog prompt reached the working caller once");
        f.Harness.Adapter.Inputs.ShouldBe([nudge.Body, "\r"]);
        result.Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        var stored = await f.ReloadAsync(nudge.Id);
        stored.AttemptState.ShouldBe(ExpectationAttemptState.Confirmed);
        stored.ReceiptAt.ShouldNotBeNull();
        stored.DestinationSessionId.ShouldBe(f.SessionId);
        stored.DestinationGeneration.ShouldBe(await f.GenerationAsync());
        stored.BaselineSequence.ShouldNotBeNull();
        stored.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.None);

        await using var db = f.Db();
        var note = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.SessionId);
        note.Status.ShouldBe(QueuedMessageStatus.Pending, "the watchdog neither drained nor needed the note queue");
        note.DeliveryAttempts.ShouldBe(0);
        f.Harness.Adapter.KillCount.ShouldBe(0);
    }

    [Test]
    public async Task C650_Input_uses_normalized_paste_and_separate_enter()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var id = Guid.NewGuid();
        var nudge = await f.NudgeAsync(body: $"Expectation nudge {id:D}\r\nqueue held 42m\r\nReply [expectation-ack:{id:D}]");

        var result = await f.DeliverAsync(nudge.Id);

        f.Harness.Adapter.Inputs.Count.ShouldBe(2, "body and Enter are separate writes");
        f.Harness.Adapter.Inputs[0].ShouldBe(
            $"\x1b[200~Expectation nudge {id:D}\nqueue held 42m\nReply [expectation-ack:{id:D}]\x1b[201~",
            "CRLF is normalized to LF inside one bracketed paste");
        f.Harness.Adapter.Inputs[1].ShouldBe("\r");
        result.Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        (await f.ReloadAsync(nudge.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Confirmed);
    }

    [Test]
    public async Task C650_Unsafe_composer_or_modal_sends_no_bytes()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var generation = await f.GenerationAsync();
        var adapter = f.Harness.Adapter;

        // 1. A parked ordinary row typed in this generation still stands whole in the composer.
        const string parked = "the parked caller note still standing in the composer";
        adapter.PrimeComposer(parked);
        await using (var db = f.Db())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Body = parked, Status = QueuedMessageStatus.Pending,
                Sequence = 1, CreatedAt = DateTime.UtcNow.AddMinutes(-20), DeliveryAttempts = 3,
                LastDeliveryStartedAt = DateTime.UtcNow.AddMinutes(-15), LastDeliveryGeneration = generation,
            });
            await db.SaveChangesAsync();
        }
        var first = await f.NudgeAsync();
        (await f.DeliverAsync(first.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        adapter.Inputs.ShouldBeEmpty("a parked body in the composer means no watchdog byte");
        var firstStored = await f.ReloadAsync(first.Id);
        firstStored.AttemptState.ShouldBe(ExpectationAttemptState.Refused);
        firstStored.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due, "a refusal is operator debt, not a WhenIdle fallback");
        (await f.Harness.Queue.GetQueueAsync(f.SessionId, CancellationToken.None)).Messages
            .ShouldAllBe(m => m.Body == parked, "no WhenIdle row was created for the nudge");

        // 2. An interrupted Sent row awaiting reconciliation, with the composer now clear.
        adapter.PrimeComposer(string.Empty);
        await using (var db = f.Db())
        {
            await db.SessionQueuedMessages.Where(m => m.AgentSessionId == f.SessionId).ExecuteDeleteAsync();
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Body = "interrupted note", Status = QueuedMessageStatus.Sent,
                Sequence = 2, CreatedAt = DateTime.UtcNow.AddMinutes(-3), DeliveryAttempts = 1,
                LastDeliveryStartedAt = DateTime.UtcNow.AddMinutes(-2), LastDeliveryGeneration = generation,
            });
            await db.SaveChangesAsync();
        }
        var second = await f.NudgeAsync();
        (await f.DeliverAsync(second.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        adapter.Inputs.ShouldBeEmpty("an interrupted Sent row may still own the composer");

        // 3. A Remote Control modal episode for this generation.
        await using (var db = f.Db())
        {
            await db.SessionQueuedMessages.Where(m => m.AgentSessionId == f.SessionId).ExecuteDeleteAsync();
            db.RemoteControlModalEpisodes.Add(new RemoteControlModalEpisode
            {
                Id = Guid.NewGuid(), SessionId = f.SessionId, AcceptedStartedAt = generation,
                FirstObservedAt = DateTime.UtcNow, LastObservedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var third = await f.NudgeAsync();
        (await f.DeliverAsync(third.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        adapter.Inputs.ShouldBeEmpty("a modal screen routes to the operator; no Escape, no body");

        // Control: with the modal resolved the same session takes the next nudge.
        await using (var db = f.Db())
        {
            await db.RemoteControlModalEpisodes.Where(e => e.SessionId == f.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.ResolvedAt, DateTime.UtcNow));
        }
        var control = await f.NudgeAsync();
        (await f.DeliverAsync(control.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        adapter.Inputs.ShouldBe([control.Body, "\r"]);
        adapter.KillCount.ShouldBe(0);
    }

    [Test]
    public async Task C650_Delivery_failure_never_stops_or_restarts_session()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var adapter = f.Harness.Adapter;

        // Transport failure on the body write: the attempt is uncertain, never retried here.
        adapter.ThrowOnSend = new InvalidOperationException("runner socket reset");
        var transport = await f.NudgeAsync();
        (await f.DeliverAsync(transport.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Uncertain);
        adapter.ThrowOnSend = null;

        // No composer evidence: Enter withheld; no overlay Escape-and-retype for the watchdog.
        adapter.EchoTypedInputToScreen = false;
        var noEvidence = await f.NudgeAsync();
        (await f.DeliverAsync(noEvidence.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        adapter.EchoTypedInputToScreen = true;
        adapter.Inputs.ShouldBe([noEvidence.Body], "one body write, no Escape, no retype, no Enter");
        (await f.ReloadAsync(noEvidence.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed,
            "Enter was withheld, so the body may still stand in the composer");

        // Every Enter swallowed: no transcript record by the deadline.
        adapter.SwallowSubmits = 99;
        var swallowed = await f.NudgeAsync();
        (await f.DeliverAsync(swallowed.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        adapter.Inputs.Count(i => i == swallowed.Body).ShouldBe(1, "the body is typed once; retries are Enter-only");
        adapter.Inputs.ShouldNotContain("\u001b");
        (await f.ReloadAsync(swallowed.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed,
            "NoTranscriptRecord past a floor: every Enter may have been swallowed, so the body may still stand");

        adapter.KillCount.ShouldBe(0);
        adapter.Killed.ShouldBeFalse();
        adapter.KillGenerationCalls.ShouldBeEmpty();
        await using var db = f.Db();
        (await db.AgentSessions.SingleAsync(s => s.Id == f.SessionId)).Status.ShouldBe(SessionStatus.Running);
        (await db.AgentIncidents.CountAsync(i => i.SessionId == f.SessionId)).ShouldBe(0,
            "generic delivery-failure recovery did not run");
        foreach (var id in new[] { transport.Id, noEvidence.Id, swallowed.Id })
            (await f.ReloadAsync(id)).OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due);
    }

    [Test]
    public async Task C650_Destination_change_prevents_typing()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var adapter = f.Harness.Adapter;
        var generation = await f.GenerationAsync();

        // The owner's binding moved after the destination was resolved.
        var committed = 0;
        Task<bool> Commit(ExpectationSendAttempt _, CancellationToken __)
        {
            committed++;
            return Task.FromResult(true);
        }
        var replacement = await f.AddSessionAsync(ownedByStandingAgent: true, makeCurrent: true);
        var moved = await f.Sender.SendAsync(f.SessionId, generation, f.World.AgentId, "moved body", Commit, CancellationToken.None);
        moved.Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        moved.AttemptCommitted.ShouldBeFalse();

        // The session restarted under the same id: a new generation.
        await using (var db = f.Db())
        {
            await db.Agents.Where(a => a.Id == f.World.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, f.SessionId.ToString("D")));
            await db.AgentSessions.Where(s => s.Id == f.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.StartedAt, DateTime.UtcNow));
        }
        var restarted = await f.Sender.SendAsync(f.SessionId, generation, f.World.AgentId, "stale generation body", Commit, CancellationToken.None);
        restarted.Outcome.ShouldBe(ExpectationSendOutcome.Refused);

        // A session not owned by the standing agent is never a destination.
        var foreign = await f.Sender.SendAsync(f.SessionId, await f.GenerationAsync(), Guid.NewGuid(), "foreign body", Commit, CancellationToken.None);
        foreign.Outcome.ShouldBe(ExpectationSendOutcome.Refused);

        adapter.Inputs.ShouldBeEmpty("no byte reached a destination that changed");
        committed.ShouldBe(0, "no attempt was committed for a changed destination");

        // Control: the current generation and owner accept a send.
        var current = await f.Sender.SendAsync(f.SessionId, await f.GenerationAsync(), f.World.AgentId, "current body", Commit, CancellationToken.None);
        current.Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        committed.ShouldBe(1);
        adapter.Inputs.ShouldBe(["current body", "\r"]);
        replacement.ShouldNotBe(f.SessionId);
    }

    [Test]
    public async Task C650_Uncertain_watchdog_body_blocks_later_ordinary_input()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var adapter = f.Harness.Adapter;
        // Every Enter is swallowed: the screen redraws, the body stays whole in the composer and no
        // record past the floor ever arrives (NoTranscriptRecord).
        adapter.SwallowSubmits = 99;
        var stranded = await f.NudgeAsync();
        (await f.DeliverAsync(stranded.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        var strandedStored = await f.ReloadAsync(stranded.Id);
        strandedStored.AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed,
            "a swallowed Enter is not submit evidence, so the body holds the composer");
        strandedStored.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due);
        adapter.SubmittedBodies.ShouldBeEmpty("sanity: nothing was submitted");
        var writes = adapter.Inputs.Count;

        // The idle local-command poll (Codex /status, Grok /usage) must not type on top of it.
        var poll = await f.Harness.Queue.TryPollLocalCommandAsync(
            f.SessionId,
            new LocalCommandPoll(AgentKind.Codex, "/status", [], OpensOverlay: false, OverlaySettleMs: 0, PanelTimeoutSeconds: 1),
            CancellationToken.None);
        poll.ShouldBeOfType<LocalCommandPollResult.Skipped>().Reason.ShouldBe("unconfirmed expectation prompt");
        adapter.Inputs.Count.ShouldBe(writes, "the poll sent no Escape, command or Enter");

        // Ordinary WhenIdle flush on an idle session must not append to the stranded body.
        var queued = await f.Harness.Queue.EnqueueAsync(
            f.SessionId, "ordinary caller note", MessageSendMode.WhenIdle, CancellationToken.None);
        var noteId = queued.Messages.Single(m => m.Body == "ordinary caller note").Id;
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.Inputs.Count.ShouldBe(writes, "repeated ordinary flushes typed nothing on top of the watchdog body");

        // Ordinary Now and SendNow are refused rather than typed on top of it.
        await Should.ThrowAsync<ConflictException>(() => f.Harness.Queue.EnqueueAsync(
            f.SessionId, "operator send now", MessageSendMode.Now, CancellationToken.None));
        await Should.ThrowAsync<ConflictException>(() => f.Harness.Queue.SendNowAsync(
            f.SessionId, noteId, CancellationToken.None));
        adapter.Inputs.Count.ShouldBe(writes);

        // A second watchdog nudge is refused too.
        var next = await f.NudgeAsync();
        (await f.DeliverAsync(next.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        adapter.Inputs.Count.ShouldBe(writes);

        await using (var db = f.Db())
        {
            var note = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == f.SessionId);
            note.Status.ShouldBe(QueuedMessageStatus.Pending);
            note.DeliveryAttempts.ShouldBe(0);
        }

        // Proven empty composer releases the hold: the queue may type again.
        adapter.SwallowSubmits = 0;
        adapter.PrimeComposer(string.Empty);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.SubmittedBodies.ShouldBe(["ordinary caller note"]);
        adapter.KillCount.ShouldBe(0);
    }

    [Test]
    public async Task C650_No_baseline_screen_only_submit_holds_until_the_transcript_shows_it()
    {
        // No transcript floor and every Enter swallowed. The screen redrew (Claude's screen-only
        // submit verdict) but no record carries the prompt, so the body may still stand whole in
        // the composer: it holds, and the operator is paged.
        await using var f = await ExpectationDeliveryFixture.CreateAsync(observable: false);
        var adapter = f.Harness.Adapter;
        adapter.SwallowSubmits = 99;
        adapter.SubmitAck = "\nclaude submit redraw";
        var unbased = await f.NudgeAsync();
        var sent = await f.DeliverAsync(unbased.Id);
        sent.Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        sent.Reason.ShouldBe("screen_only_submit", "a redraw is the screen-only verdict, which is not submit evidence without a record");
        var stored = await f.ReloadAsync(unbased.Id);
        stored.BaselineSequence.ShouldBeNull("sanity: no floor");
        stored.AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed, "no positive submit evidence");
        stored.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due, "the unconfirmed hold pages the operator");
        adapter.SubmittedBodies.ShouldBeEmpty("sanity: the body never left the composer");
        var writes = adapter.Inputs.Count;

        await f.Harness.Queue.EnqueueAsync(f.SessionId, "land note for CARD-0641", MessageSendMode.WhenIdle, CancellationToken.None);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        await Should.ThrowAsync<ConflictException>(() => f.Harness.Queue.EnqueueAsync(
            f.SessionId, "operator send now", MessageSendMode.Now, CancellationToken.None));
        adapter.Inputs.Count.ShouldBe(writes, "nothing is typed on top of a body that may stand in the composer");

        // The Enter finally lands. Its echo stays in the conversation, so the screen alone can
        // never show the composer empty, and time passing is not evidence either.
        adapter.SwallowSubmits = 0;
        adapter.PrimeComposer(string.Empty);
        adapter.Emit("\n> " + unbased.Body + "\n");
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.Inputs.Count.ShouldBe(writes, "an echo on screen does not release the hold");

        // Positive evidence: the transcript binds and shows the prompt taken as a turn. The hold releases.
        await BridgeQueueHarness.InsertEntryAsync(f.SessionId, TranscriptKinds.UserPrompt, unbased.Body,
            timestamp: DateTime.UtcNow, connectionString: f.Schema.ConnectionString);
        await BridgeQueueHarness.InsertEntryAsync(f.SessionId, TranscriptKinds.TurnEnd, stopReason: "end_turn",
            connectionString: f.Schema.ConnectionString);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.SubmittedBodies.ShouldBe(["land note for CARD-0641"]);
        (await f.ReloadAsync(unbased.Id)).ReceiptAt.ShouldBeNull("releasing the hold is not a receipt without a floor");
        adapter.KillCount.ShouldBe(0);
    }

    [Test]
    public async Task C650_Submitted_prompt_left_on_screen_does_not_hold_ordinary_input()
    {
        // No transcript floor: the prompt is submitted and recorded but can never be confirmed.
        // Its echo stays in the rendered conversation, as it does on an idle caller.
        await using var f = await ExpectationDeliveryFixture.CreateAsync(observable: false);
        var adapter = f.Harness.Adapter;
        var record = adapter.OnSubmitted!;
        Func<string, Task> echoThenRecord = async submitted =>
        {
            adapter.Emit("\n> " + submitted + "\n");
            await record(submitted);
        };
        adapter.OnSubmitted = echoThenRecord;

        var unbased = await f.NudgeAsync();
        (await f.DeliverAsync(unbased.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed, "no floor, no receipt");
        (await f.ReloadAsync(unbased.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Submitted);
        adapter.SnapshotRenderedScreen().Contains(unbased.Body, StringComparison.Ordinal)
            .ShouldBeTrue("sanity: the submitted echo is still on screen");

        // WhenIdle, Now and SendNow all proceed.
        await f.Harness.Queue.EnqueueAsync(f.SessionId, "land note for CARD-0641", MessageSendMode.WhenIdle, CancellationToken.None);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain("land note for CARD-0641");
        await f.Harness.Queue.EnqueueAsync(f.SessionId, "operator send now", MessageSendMode.Now, CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain("operator send now");
        await f.Harness.MarkWorkingAsync();
        var held = await f.Harness.Queue.EnqueueAsync(
            f.SessionId, "forced past the working turn", MessageSendMode.WhenIdle, CancellationToken.None);
        var heldId = held.Messages.Single(m => m.Body == "forced past the working turn").Id;
        await f.Harness.Queue.SendNowAsync(f.SessionId, heldId, CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain("forced past the working turn");

        // A Truncated verdict: Enter went out and only a partial prompt was recorded.
        var id = Guid.NewGuid();
        var longBody = $"Expectation nudge {id:D}: "
            + string.Join(' ', Enumerable.Repeat("queue held past its age limit", 12))
            + $" Reply {ExpectationPromptFormatter.AckMarker(id)}";
        var truncated = await f.NudgeAsync(body: longBody);
        adapter.OnSubmitted = async submitted =>
        {
            adapter.Emit("\n> " + submitted + "\n");
            await BridgeQueueHarness.InsertEntryAsync(f.SessionId, TranscriptKinds.UserPrompt, submitted[..240],
                timestamp: DateTime.UtcNow, connectionString: f.Schema.ConnectionString);
            await BridgeQueueHarness.InsertEntryAsync(f.SessionId, TranscriptKinds.TurnEnd, stopReason: "end_turn",
                connectionString: f.Schema.ConnectionString);
        };
        (await f.DeliverAsync(truncated.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        (await f.ReloadAsync(truncated.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Submitted);
        adapter.OnSubmitted = echoThenRecord;

        // Both echoes still stand on screen; the next nudge and a later note still go through.
        var screen = adapter.SnapshotRenderedScreen();
        screen.Contains(unbased.Body, StringComparison.Ordinal).ShouldBeTrue();
        screen.Contains(longBody[..80], StringComparison.Ordinal).ShouldBeTrue();
        var later = await f.NudgeAsync();
        (await f.DeliverAsync(later.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        await f.Harness.Queue.EnqueueAsync(f.SessionId, "the next caller note", MessageSendMode.Now, CancellationToken.None);
        adapter.SubmittedBodies.ShouldBe([
            unbased.Body, "land note for CARD-0641", "operator send now", "forced past the working turn",
            longBody, later.Body, "the next caller note"]);
        adapter.KillCount.ShouldBe(0);
    }

    [Test]
    public async Task C650_Terminal_overlay_refuses_before_any_byte()
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var adapter = f.Harness.Adapter;
        await using (var db = f.Db())
        {
            await db.AgentSessions.Where(s => s.Id == f.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.AgentKind, AgentKind.Grok));
        }

        // Grok's measured /usage overlay is open. The watchdog has no Escape arm, so it refuses.
        adapter.OverlayOpen = true;
        var blocked = await f.NudgeAsync();
        (await f.DeliverAsync(blocked.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Refused);
        adapter.Inputs.ShouldBeEmpty("no Escape and no body reached the overlay");
        adapter.OverlayOpen.ShouldBeTrue();
        var stored = await f.ReloadAsync(blocked.Id);
        stored.AttemptState.ShouldBe(ExpectationAttemptState.Refused);
        stored.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due, "a modal screen routes to the operator");

        // Control: with the overlay closed the same session takes the next nudge.
        adapter.OverlayOpen = false;
        var control = await f.NudgeAsync();
        (await f.DeliverAsync(control.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Confirmed);
        adapter.Inputs.ShouldBe([control.Body, "\r"]);
    }

    [Test]
    public async Task C650_Submitted_echo_above_a_readable_composer_does_not_hold()
    {
        // Review 02e08ff1: the prompt was submitted and its echo stays in the conversation, but no
        // transcript record ever arrives (NoTranscriptRecord). Claude's composer is readable, and
        // it is empty, so the echo above it must not hold ordinary input forever.
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var adapter = f.Harness.Adapter;
        adapter.ClaudeComposerChrome = true;
        EchoWithoutRecordingTheWatchdogPrompt(adapter);
        var nudge = await f.NudgeAsync();
        (await f.DeliverAsync(nudge.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        (await f.ReloadAsync(nudge.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed,
            "sanity: no record carries the body, so it cannot be Submitted");
        adapter.SubmittedBodies.ShouldBe([nudge.Body], "sanity: the Enter was taken");
        var screen = adapter.SnapshotRenderedScreen();
        screen.Contains(nudge.Body, StringComparison.Ordinal).ShouldBeTrue("sanity: the echo is on screen");
        Antiphon.Agents.Pty.ClaudeScreen.TryReadComposer(screen, out var composer).ShouldBeTrue();
        composer.ShouldBeEmpty("sanity: the composer region is empty");

        await f.Harness.Queue.EnqueueAsync(f.SessionId, "land note for CARD-0641", MessageSendMode.WhenIdle, CancellationToken.None);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain("land note for CARD-0641", "an empty composer releases the hold");
        await f.Harness.Queue.EnqueueAsync(f.SessionId, "operator send now", MessageSendMode.Now, CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain("operator send now");
        adapter.KillCount.ShouldBe(0);
    }

    [Test]
    public async Task C650_Swallowed_enter_keeps_holding_while_the_composer_shows_the_body()
    {
        // The same Claude layout, but the Enter is swallowed: the body is still in the composer.
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var adapter = f.Harness.Adapter;
        adapter.ClaudeComposerChrome = true;
        adapter.SwallowSubmits = 99;
        var nudge = await f.NudgeAsync();
        (await f.DeliverAsync(nudge.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        adapter.SubmittedBodies.ShouldBeEmpty("sanity: nothing was submitted");
        Antiphon.Agents.Pty.ClaudeScreen.TryReadComposer(adapter.SnapshotRenderedScreen(), out var composer).ShouldBeTrue();
        composer.ShouldContain("Expectation nudge", Case.Sensitive, "sanity: the body stands in the composer");
        var writes = adapter.Inputs.Count;

        await f.Harness.Queue.EnqueueAsync(f.SessionId, "land note for CARD-0641", MessageSendMode.WhenIdle, CancellationToken.None);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        var refused = await Should.ThrowAsync<ConflictException>(() => f.Harness.Queue.EnqueueAsync(
            f.SessionId, "operator send now", MessageSendMode.Now, CancellationToken.None));
        refused.Message.ShouldContain($"/api/sessions/{f.SessionId:D}/expectation-hold/release",
            Case.Sensitive, "the refusal tells the operator the audited release exists");
        adapter.Inputs.Count.ShouldBe(writes, "nothing is typed on top of the body in the composer");

        // Only the tail of a long body is visible once its head scrolls out of the composer. The
        // composer is still not empty, so it still holds.
        adapter.PrimeComposer(nudge.Body[^24..]);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.Inputs.Count.ShouldBe(writes, "a partly visible body still holds");

        // A proven empty composer releases it.
        adapter.SwallowSubmits = 0;
        adapter.PrimeComposer(string.Empty);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.SubmittedBodies.ShouldBe(["land note for CARD-0641"]);
        adapter.KillCount.ShouldBe(0);
    }

    [Test]
    public async Task C650_Operator_release_clears_a_whole_screen_hold_with_an_audit_and_no_input()
    {
        // No readable composer region (the bare fake screen), so the whole-screen rule applies and
        // the submitted echo holds. The operator's audited release is the way out.
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        var adapter = f.Harness.Adapter;
        EchoWithoutRecordingTheWatchdogPrompt(adapter);
        var task = await f.SubjectTaskAsync();
        var nudge = await f.NudgeAsync(checkTaskId: task);
        (await f.DeliverAsync(nudge.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        (await f.ReloadAsync(nudge.Id)).AttemptState.ShouldBe(ExpectationAttemptState.Unconfirmed);
        Antiphon.Agents.Pty.ClaudeScreen.TryReadComposer(adapter.SnapshotRenderedScreen(), out _)
            .ShouldBeFalse("sanity: no composer region on this screen");
        var route = $"/api/sessions/{f.SessionId:D}/expectation-hold/release";

        // The hold names the release in the nudge's Check note and on its audit card.
        await using (var db = f.Db())
        {
            (await db.AgentTaskEvents.AnyAsync(e => e.AgentTaskId == task && e.Type == AgentTaskEventType.Check
                && e.Detail!.Contains(route))).ShouldBeTrue("the Check note names the release route");
            (await db.CardComments.AnyAsync(c => c.CardId == f.World.CardId && c.Body.Contains(route)))
                .ShouldBeTrue("the audit card names the release route");
        }

        await f.Harness.Queue.EnqueueAsync(f.SessionId, "land note for CARD-0641", MessageSendMode.WhenIdle, CancellationToken.None);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        var writes = adapter.Inputs.Count;
        adapter.SubmittedBodies.ShouldBe([nudge.Body], "sanity: the echo holds under the whole-screen rule");

        await Should.ThrowAsync<ValidationException>(() => f.Harness.Queue.ReleaseExpectationHoldAsync(
            f.SessionId, "  ", CancellationToken.None));
        var released = await f.Harness.Queue.ReleaseExpectationHoldAsync(
            f.SessionId, "checked the pane: the nudge was submitted", CancellationToken.None);
        released.ReleasedNudgeIds.ShouldBe([nudge.Id]);
        adapter.Inputs.Count.ShouldBe(writes, "the release types nothing");

        var stored = await f.ReloadAsync(nudge.Id);
        stored.AttemptState.ShouldBe(ExpectationAttemptState.Released);
        stored.OperatorOutboxState.ShouldBe(ExpectationOperatorOutboxState.Due, "release is not a receipt; the page stays due");
        await using (var db = f.Db())
        {
            (await db.CardComments.CountAsync(c => c.CardId == f.World.CardId
                && c.Body.Contains("[expectation-hold-released:" + nudge.Id.ToString("D") + "]")
                && c.Body.Contains("checked the pane: the nudge was submitted"))).ShouldBe(1);
            (await db.AgentTaskEvents.CountAsync(e => e.AgentTaskId == task && e.Type == AgentTaskEventType.Check
                && e.Detail!.Contains("[expectation-hold-released:" + nudge.Id.ToString("D") + "]"))).ShouldBe(1);
        }

        // Released, so ordinary input proceeds. A second release finds nothing and records nothing.
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.SubmittedBodies.ShouldBe([nudge.Body, "land note for CARD-0641"]);
        (await f.Harness.Queue.ReleaseExpectationHoldAsync(f.SessionId, "again", CancellationToken.None))
            .ReleasedNudgeIds.ShouldBeEmpty();
        await using (var db = f.Db())
        {
            (await db.CardComments.CountAsync(c => c.Body.Contains("[expectation-hold-released:"))).ShouldBe(1);
        }
        adapter.KillCount.ShouldBe(0);
    }

    /// <summary>
    /// The watchdog prompt is taken and its echo stays in the conversation, but its transcript record
    /// never arrives (NoTranscriptRecord). Ordinary notes still record as usual.
    /// </summary>
    private static void EchoWithoutRecordingTheWatchdogPrompt(Antiphon.Tests.Agents.FakeAgentProtocolAdapter adapter)
    {
        var record = adapter.OnSubmitted!;
        adapter.OnSubmitted = async submitted =>
        {
            adapter.Emit("\n> " + submitted + "\n");
            if (!submitted.StartsWith("Expectation nudge", StringComparison.Ordinal))
                await record(submitted);
        };
    }
}
