using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Delivery-time composer verification (the TUI-echo-probe replacement): a delivered body must
/// show up on the rendered screen BEFORE the submitting Enter goes out, and the Enter must produce
/// output. Failure paths: message reverts to Pending, a DeliveryVerificationFailed incident is
/// recorded, and (always-on agents only) the wedged session is killed for the supervisor to
/// restart; the stranded-queue watchdog then redelivers. The FakeAgentProtocolAdapter simulates
/// the composer: typed input echoes into the rendered screen, a lone CR clears it and emits an
/// ack (EchoTypedInputToScreen=false / SubmitAck="" simulate the two wedge modes).
/// Harness lives in <see cref="BridgeQueueHarness"/> (shared with the launch-note, compaction
/// recovery, and batching suites).
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class SessionMessageQueueDeliveryVerificationTests
{
    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static Task<BridgeQueueHarness> CreateHarnessAsync(bool alwaysOn) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions { AlwaysOn = alwaysOn });

    [Test]
    public async Task Verified_delivery_types_body_then_submits_and_leaves_no_incident()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "verified hello", MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Messages.ShouldBeEmpty("idle session: the message delivers straight away");
        h.Adapter.Inputs.ShouldBe(["verified hello", "\r"]);
        h.Adapter.SubmittedBodies.ShouldBe(["verified hello"]);
        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(i => i.AgentId == h.AgentId)).ShouldBeFalse();
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task Wedged_composer_withholds_enter_reverts_message_and_restarts_always_on_agent()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.EchoTypedInputToScreen = false; // typed text never renders: wedged terminal

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "into the void", MessageSendMode.WhenIdle, CancellationToken.None);

        // The body was typed but the submitting Enter must be withheld — the message is not
        // lost into a dead composer.
        h.Adapter.Inputs.ShouldBe(["into the void"]);

        dto.Messages.Count.ShouldBe(1);
        dto.Messages[0].Status.ShouldBe(nameof(QueuedMessageStatus.Pending));

        await using var db = CreateContext();
        var incident = await db.AgentIncidents.SingleOrDefaultAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
        incident.ShouldNotBeNull();
        incident.Message.ShouldContain("never appeared in the composer");

        h.Adapter.Killed.ShouldBeTrue("always-on agent: the wedged session is killed for the supervisor to restart");
    }

    [Test]
    public async Task Swallowed_submit_reverts_message_and_restarts_always_on_agent()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.SubmitAck = ""; // Enter lands but produces no output: submit swallowed

        await h.Queue.EnqueueAsync(
            h.SessionId, "swallowed submit", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["swallowed submit", "\r"]);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.SentAt.ShouldBeNull();

        var incident = await db.AgentIncidents.SingleOrDefaultAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
        incident.ShouldNotBeNull();
        incident.Message.ShouldContain("no output");
        h.Adapter.Killed.ShouldBeTrue();
    }

    [Test]
    public async Task Non_always_on_agent_gets_incident_and_revert_but_no_kill()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.EchoTypedInputToScreen = false;

        await h.Queue.EnqueueAsync(
            h.SessionId, "manual agent message", MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeTrue();
        h.Adapter.Killed.ShouldBeFalse("not always-on: never kill a human's session out from under them");
    }

    [Test]
    public async Task Send_now_throws_conflict_when_delivery_cannot_be_verified()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.EchoTypedInputToScreen = false;

        await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.EnqueueAsync(h.SessionId, "send now please", MessageSendMode.Now, CancellationToken.None));

        h.Adapter.Inputs.ShouldBe(["send now please"], "Enter must be withheld on an unverified delivery");
    }

    [Test]
    public async Task Failed_turn_end_flush_does_not_broadcast_session_finished()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.MarkWorkingAsync();
        await h.Queue.EnqueueAsync(h.SessionId, "held message", MessageSendMode.WhenIdle, CancellationToken.None);
        h.Adapter.EchoTypedInputToScreen = false;

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.EventBus.PublishedEvents.ShouldNotContain(
            e => e.EventName == "SessionFinished",
            "a failed delivery is not 'queue empty and agent finished'");
        h.EventBus.PublishedEvents.ShouldContain(e => e.EventName == "SessionQueueChanged");
    }

    [Test]
    public async Task Stranded_watchdog_redelivers_pending_messages_on_idle_always_on_sessions()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.SeedPendingMessageAsync("stranded message");

        var flushed = await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        flushed.ShouldBe(1);
        h.Adapter.Inputs.ShouldBe(["stranded message", "\r"]);
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    [Test]
    public async Task Stranded_watchdog_skips_non_always_on_agents_and_working_sessions()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);

        await h.SeedPendingMessageAsync("not mine to flush");
        (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None))
            .ShouldBe(0, "non-always-on agents are never auto-flushed");

        // Flip to always-on but make the session busy: still not flushed.
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.AlwaysOn, true));
        }
        await h.MarkWorkingAsync();
        (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None))
            .ShouldBe(0, "working sessions are never interrupted");

        h.Adapter.Inputs.ShouldBeEmpty();
    }

    // PR 6's inseparable pair: the CompactBoundary transcript kind ships WITH this exclusion — a
    // boundary row after the last TurnEnd would otherwise read as "working" forever, stranding
    // every WhenIdle message (including the compaction recovery note itself).
    [Test]
    public async Task Session_with_compact_boundary_after_last_turn_end_reads_idle()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.InsertTurnAsync("earlier question", "earlier answer");
        await h.InsertTranscriptEntryAsync(
            Antiphon.SessionRunner.Contracts.TranscriptKinds.CompactBoundary, "Context compacted (auto)");

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "after the compaction", MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Messages.ShouldBeEmpty("a compacted-but-idle session must take the idle fast-path");
        h.Adapter.SubmittedBodies.ShouldBe(["after the compaction"]);
    }

    [Test]
    public async Task Fresh_start_migrates_pending_messages_to_the_new_session()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.SeedPendingMessageAsync("survive the fresh fallback");

        // End the old session so StartAsync creates a NEW session row (fresh=true skips resume;
        // liveness is judged from the DB status).
        await using (var db = CreateContext())
        {
            await db.AgentSessions.Where(s => s.Id == h.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Failed));
        }

        // Fresh scope: the harness scope's DbContext still tracks the agent from creation
        // (identity resolution would hide the PersistentSessionId set via ExecuteUpdate).
        using var controlScope = h.Provider.CreateScope();
        var control = controlScope.ServiceProvider.GetRequiredService<AgentControlService>();
        var started = await control.StartAsync(
            h.AgentId, new StartAgentRequest(RemoteControl: false, Fresh: true), CancellationToken.None);

        var newSessionId = Guid.Parse(started.PersistentSessionId!);
        newSessionId.ShouldNotBe(h.SessionId);

        await using var verify = CreateContext();
        var message = await verify.SessionQueuedMessages.SingleAsync(m => m.Body == "survive the fresh fallback");
        message.AgentSessionId.ShouldBe(newSessionId, "pending messages must follow the agent to its new session");
    }
}
