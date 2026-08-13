using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
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
    // every WhenIdle message (including the compaction recovery note itself). Still idle after
    // CARD-0041, and for the SAME reason: an auto boundary is excluded from activity, not ranked
    // as an end (the manual-boundary-as-end cases are further down).
    [Test]
    public async Task Session_with_compact_boundary_after_last_turn_end_reads_idle()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.InsertTurnAsync("earlier question", "earlier answer");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.CompactBoundary, "Context compacted (auto)");

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "after the compaction", MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Messages.ShouldBeEmpty("a compacted-but-idle session must take the idle fast-path");
        h.Adapter.SubmittedBodies.ShouldBe(["after the compaction"]);
    }

    // ---- CARD-0041: a compacted session read "working" for two days --------------------------
    // The stored rows of session e77fb0a7 after its last real turn, verbatim (identifiers are the
    // stored sequences; note the timestamps are NON-monotonic against sequence — the boundary is
    // stamped LATER than the continuation record that follows it). Two shapes escaped the old
    // exclusions: the RAW typed "/compact …" prompt (Claude records the typed text as a plain user
    // record IN ADDITION to the <command-name> wrapper) and the synthetic continuation prompt.
    // The fix ranks a MANUAL boundary as the turn's end and excludes the continuation from
    // activity; both are needed, and neither may lean on the backfill timestamp override.
    private const string CompactionContinuation =
        "This session is being continued from a previous conversation that ran out of context. "
        + "The conversation is summarized below:";

    private static DateTime At(int hour, int minute, int second) =>
        new(2026, 8, 11, hour, minute, second, DateTimeKind.Utc);

    [Test]
    public async Task Card_0041_post_compaction_records_read_idle_on_sequence_alone()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "the real work", timestamp: At(10, 40, 0));
        await h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "done", timestamp: At(10, 46, 22));
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: At(10, 46, 22));
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, "/compact This session is being handed NEW, unrelated work",
            timestamp: At(10, 53, 10));
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.CompactBoundary, "Context compacted (manual)", timestamp: At(10, 54, 4));
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, CompactionContinuation, timestamp: At(10, 53, 54));
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, "<command-name>/compact</command-name>", timestamp: At(10, 53, 10));
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, "<local-command-stdout>Compacted</local-command-stdout>",
            timestamp: At(10, 54, 4));

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "the brief that stranded for two days", MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Working.ShouldBeFalse("the manual boundary is the turn's end; nothing after it is activity");
        dto.Messages.ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBe(["the brief that stranded for two days"]);
    }

    // Boundary-as-end ALONE would leave this stuck: with the continuation stamped after the
    // boundary, the timestamp override cannot rescue it either. The exclusion does the work — and
    // the runner, which has no override at all, depends on it for every shape.
    [Test]
    public async Task Continuation_prompt_after_a_manual_boundary_is_not_activity()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: At(10, 46, 22));
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.CompactBoundary, "Context compacted (manual)", timestamp: At(10, 54, 4));
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, CompactionContinuation, timestamp: At(10, 54, 30));

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "after the continuation", MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Working.ShouldBeFalse();
        h.Adapter.SubmittedBodies.ShouldBe(["after the continuation"]);
    }

    // The deliberate NON-exclusion: matching raw "/"-prefixed text was rejected, because a real
    // prompt may legitimately start with a slash. Without a boundary to outrank it, it is activity.
    [Test]
    public async Task Raw_slash_prefixed_prompt_with_no_boundary_still_reads_working()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.InsertTurnAsync("earlier question", "earlier answer");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "/compact keep the API contract notes");

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "must wait", MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Working.ShouldBeTrue("a typed prompt is activity, boundary or no boundary");
        dto.Messages.Select(m => m.Body).ShouldBe(["must wait"]);
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    // The manual-only scoping (plan refinement B): auto-compaction fires when a request starts over
    // the context threshold — MID-turn, prompt already submitted. Counting it as an end would type
    // a WhenIdle message into a working composer.
    [Test]
    public async Task Auto_compaction_boundary_mid_turn_still_reads_working()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.InsertTurnAsync("earlier question", "earlier answer");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "now do the big thing");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.CompactBoundary, "Context compacted (auto)");

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "must not interrupt", MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Working.ShouldBeTrue("an auto boundary lands mid-turn — it proves nothing about idleness");
        dto.Messages.Select(m => m.Body).ShouldBe(["must not interrupt"]);
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    // Reading idle is not enough: nothing else will ever flush this session (compaction makes no
    // API call, so no TurnEnd follows), and the stranded watchdog only serves always-on/delegation
    // messages. The boundary itself flushes — through the NARROW path.
    [Test]
    public async Task Manual_boundary_flushes_a_message_stranded_before_the_compaction()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.InsertTurnAsync("earlier question", "earlier answer");
        // The raw typed prompt: the session reads working from here until the boundary lands.
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, "/compact hand this session new work");
        await h.SeedPendingMessageAsync("the stranded brief");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();

        await h.Runtime.ObserveTranscriptAsync(ManualBoundaryEvent(h.SessionId, 100), CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe(["the stranded brief"]);
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    // The flush is deliberately NOT the turn-end path: an empty queue there publishes
    // SessionFinished (a spurious "Agent finished" on every idle /compact) and runs the reply/task
    // dispatchers, which would settle a delegated task against the STALE pre-compaction report.
    [Test]
    public async Task Manual_boundary_with_an_empty_queue_publishes_no_finished_event()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        await h.InsertTurnAsync("earlier question", "earlier answer");
        h.EventBus.Clear();

        await h.Runtime.ObserveTranscriptAsync(ManualBoundaryEvent(h.SessionId, 101), CancellationToken.None);

        h.EventBus.PublishedEvents.ShouldNotContain(
            e => e.EventName == "SessionFinished",
            "a compaction is not a finished turn — no toast, no settlement");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
    }

    private static SessionRunnerTranscriptEvent ManualBoundaryEvent(Guid sessionId, long sequence) => new(
        sessionId, sequence, TranscriptKinds.CompactBoundary,
        Guid.NewGuid().ToString(), null, DateTimeOffset.UtcNow, null,
        "Context compacted (manual)", null, null, null, null, null);

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
