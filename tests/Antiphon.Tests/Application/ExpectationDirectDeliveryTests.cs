using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
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

        // Every Enter swallowed: no transcript record by the deadline.
        adapter.SwallowSubmits = 99;
        var swallowed = await f.NudgeAsync();
        (await f.DeliverAsync(swallowed.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        adapter.Inputs.Count(i => i == swallowed.Body).ShouldBe(1, "the body is typed once; retries are Enter-only");
        adapter.Inputs.ShouldNotContain("\u001b");

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
        adapter.SwallowSubmits = 99;
        var stranded = await f.NudgeAsync();
        (await f.DeliverAsync(stranded.Id)).Outcome.ShouldBe(ExpectationSendOutcome.Unconfirmed);
        adapter.SwallowSubmits = 0;
        var writes = adapter.Inputs.Count;

        // Ordinary WhenIdle flush on an idle session must not append to the stranded body.
        await f.Harness.Queue.EnqueueAsync(f.SessionId, "ordinary caller note", MessageSendMode.WhenIdle, CancellationToken.None);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.Inputs.Count.ShouldBe(writes, "repeated ordinary flushes typed nothing on top of the watchdog body");

        // Ordinary send-now is refused rather than typed on top of it.
        await Should.ThrowAsync<ConflictException>(() => f.Harness.Queue.EnqueueAsync(
            f.SessionId, "operator send now", MessageSendMode.Now, CancellationToken.None));
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
        adapter.PrimeComposer(string.Empty);
        await f.Harness.Queue.FlushSessionAsync(f.SessionId, CancellationToken.None);
        adapter.SubmittedBodies.ShouldContain("ordinary caller note");
        adapter.KillCount.ShouldBe(0);
    }
}
