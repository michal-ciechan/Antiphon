using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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

    // "No incident" assertions go through this so a failure names the incident instead of
    // reading "True should be False" (CARD-0201 spent a build cycle learning it was kind 36).
    private static async Task<List<string>> IncidentsOfAsync(AppDbContext db, Guid agentId) =>
        (await db.AgentIncidents
            .Where(i => i.AgentId == agentId)
            .Select(i => new { i.Kind, i.Severity, i.Message })
            .ToListAsync())
        .Select(i => $"{i.Kind}:{i.Severity}:{i.Message}")
        .ToList();

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
        (await IncidentsOfAsync(db, h.AgentId)).ShouldBeEmpty();
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task Wedged_composer_withholds_enter_reverts_message_and_restarts_always_on_agent()
    {
        // A session that has TAKEN A TURN and then wedged — the shape CARD-0055 was designed for and
        // the one the kill is right about. CARD-0103's refund covers the opposite shape (a session
        // that has produced nothing yet, i.e. is still becoming input-responsive) and must not reach
        // in here; the transcript entry is what tells them apart, so this test states it outright.
        await using var h = await ObservableHarnessAsync();
        h.Adapter.EchoTypedInputToScreen = false; // typed text never renders: wedged terminal

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "into the void", MessageSendMode.WhenIdle, CancellationToken.None);

        // CARD-0137 S5: idle Supported kinds (Claude, after S1) get one Esc-and-retype. Enter is
        // still withheld — the composer never showed the body.
        h.Adapter.Inputs.ShouldBe(["into the void", "\u001b", "into the void"]);
        h.Adapter.Inputs.ShouldNotContain("\r");

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
        // Enter lands but produces no output AND submits nothing. Both knobs: SubmitAck alone
        // still records the prompt in the fake, and a recorded (stamped) prompt IS a confirmed
        // delivery whatever the screen did (CARD-0201) — the swallow is what this test is about.
        h.Adapter.SubmitAck = "";
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.EnqueueAsync(
            h.SessionId, "swallowed submit", MessageSendMode.WhenIdle, CancellationToken.None);

        // CARD-0164: an unobservable baseline (no transcript yet) now gets the SAME
        // SubmitAttempts-bounded re-press loop an observable baseline already had, instead of
        // failing fast on the first swallowed Enter - so a genuinely-swallowed submit burns all
        // 3 attempts before reverting, same as it always has for an observable-baseline session.
        h.Adapter.Inputs.ShouldBe(["swallowed submit", "\r", "\r", "\r"]);

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

        h.Adapter.Inputs.ShouldBe(["send now please", "\u001b", "send now please"],
            "S5 one-shot recovery, then Enter withheld on an unverified delivery");
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

    [Test]
    public async Task Queued_user_prompt_is_inert_for_the_server_working_rule()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.QueuedUserPrompt,
            "a completion note accepted by a busy composer",
            timestamp: DateTime.UtcNow.AddMinutes(1));

        await using var db = CreateContext();
        (await SessionMessageQueueService.IsWorkingAsync(db, h.SessionId, CancellationToken.None))
            .ShouldBeFalse("a queued_command confirms delivery only; it is not turn activity");
    }

    [Test]
    public async Task Queue_operation_rows_are_inert_for_the_server_working_rule()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.InsertTranscriptEntryAsync(TranscriptKinds.QueueEnqueue, "Hi", timestamp: DateTime.UtcNow.AddMinutes(1));
        await h.InsertTranscriptEntryAsync(TranscriptKinds.QueueDequeue, "Hi", timestamp: DateTime.UtcNow.AddMinutes(1));
        await h.InsertTranscriptEntryAsync(TranscriptKinds.QueueRemove, "Hi", timestamp: DateTime.UtcNow.AddMinutes(1));

        await using var db = CreateContext();
        (await SessionMessageQueueService.IsWorkingAsync(db, h.SessionId, CancellationToken.None))
            .ShouldBeFalse("queue-operation housekeeping is not turn activity");
    }

    [Test]
    public async Task Queued_user_prompt_is_a_turn_prompt_for_settlement_and_the_delivery_watchdog()
    {
        // CARD-0132 S2.4 kept QueuedUserPrompt inert here (this test used to assert TurnPrompts
        // empty / HasTurnPromptSinceAsync false). CARD-0135 reverses that: the timestamp-ranking
        // trap that justified inertness does not transfer to TranscriptPromptSpan (sequence
        // orders; Timestamp only meets DispatchedAt). Working/idle stays inert — see
        // Queued_user_prompt_is_inert_for_the_server_working_rule, which must stay green unedited.
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        var dispatchedAt = DateTime.UtcNow.AddMinutes(-1);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.QueuedUserPrompt,
            "a queued completion note is not a task brief",
            timestamp: DateTime.UtcNow);

        await using var db = CreateContext();
        var span = await TranscriptPromptSpan.LoadAsync(db, h.SessionId, dispatchedAt, CancellationToken.None);
        var queued = span.TurnPrompts.ShouldHaveSingleItem();
        queued.Kind.ShouldBe(TranscriptKinds.QueuedUserPrompt);
        (await TranscriptPromptSpan.HasTurnPromptSinceAsync(db, h.SessionId, dispatchedAt, CancellationToken.None))
            .ShouldBeTrue();
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

    // ---- CARD-0055: a delivery is Sent only when its UserPrompt record exists ------------------
    //
    // The old verdict was "the output sequence advanced after Enter", which any redraw satisfies.
    // Measured on session cefed08a: ea2feb92's note was marked Sent at 15:16:20Z and did not reach
    // Claude until 17:00:09Z (104 minutes), when the NEXT delivery's Enter pushed it in; 15c9150e's
    // note was marked Sent because that same Enter produced a new UserPrompt record — carrying the
    // STALE body — while its own body died in the composer, never in the transcript at all.
    //
    // The harness's fake now models the whole round trip: a submitted body becomes a UserPrompt row
    // (BridgeQueueHarness wires FakeAgentProtocolAdapter.OnSubmitted), and the two measured composer
    // states are reproducible — SwallowSubmits (redraw, no submit, composer keeps the body) and
    // StaleSubmitBody (someone else's body goes in, ours stays behind).

    private static async Task<BridgeQueueHarness> ObservableHarnessAsync(bool alwaysOn = true)
    {
        var h = await CreateHarnessAsync(alwaysOn);
        // The observability gate wants a bound, ingesting transcript. One completed turn is the
        // cheapest honest way to say "this session's transcript is live" — and it leaves the
        // session idle, so a WhenIdle enqueue delivers straight away.
        await h.InsertTurnAsync("an earlier prompt", "an earlier answer");
        return h;
    }

    [Test]
    public async Task A_swallowed_first_enter_is_re_pressed_and_the_delivery_confirms()
    {
        await using var h = await ObservableHarnessAsync();
        h.Adapter.SwallowSubmits = 1; // ea2feb92: the screen redraws, the composer keeps the body

        await h.Queue.EnqueueAsync(
            h.SessionId, "the note that was swallowed", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["the note that was swallowed", "\r", "\r"],
            "the retry is a second Enter and nothing else — the body is NEVER re-typed");
        h.Adapter.SubmittedBodies.ShouldBe(["the note that was swallowed"],
            "one body in, exactly once");

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
        (await IncidentsOfAsync(db, h.AgentId)).ShouldBeEmpty();
        h.Adapter.Killed.ShouldBeFalse();
    }

    // THE pin, and the reason this design exists: in the measured 15c9150e failure a new UserPrompt
    // record DID appear — carrying the previous delivery's body — and the old code called that Sent.
    // Record arrival is not confirmation; the record's TEXT must be ours.
    [Test]
    public async Task A_record_carrying_a_stale_body_is_rejected_and_the_enter_is_re_pressed()
    {
        await using var h = await ObservableHarnessAsync();
        const string stale = "the previous note, still sitting in the composer";
        const string ours = "the note this delivery is actually about";
        h.Adapter.StaleSubmitBody = stale;

        await h.Queue.EnqueueAsync(h.SessionId, ours, MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe([stale, ours],
            "the first Enter submitted the stale body; the re-press submitted ours");
        h.Adapter.Inputs.ShouldBe([ours, "\r", "\r"]);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);

        var prompts = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == h.SessionId && t.Kind == TranscriptKinds.UserPrompt)
            .OrderBy(t => t.Sequence)
            .Select(t => t.Text)
            .ToListAsync();
        prompts.ShouldContain(ours,
            "under the old rule the stale record's arrival ended the delivery and OUR body was lost");
    }

    // The other half of the same rule: a stale record must not be able to certify a delivery on its
    // own. All Enters swallowed after the stale one, so ours never lands.
    [Test]
    public async Task A_stale_record_alone_never_produces_delivered()
    {
        await using var h = await ObservableHarnessAsync();
        const string stale = "a completely different body that Enter submitted";
        h.Adapter.StaleSubmitBody = stale;
        h.Adapter.SwallowSubmits = 99; // every Enter AFTER the stale one is swallowed

        await h.Queue.EnqueueAsync(
            h.SessionId, "the body that never made it in", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldBe([stale], "ours never got in");

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending, "a record with the wrong text is not our delivery");
        message.SentAt.ShouldBeNull();

        (await db.TranscriptEntries.CountAsync(t =>
            t.AgentSessionId == h.SessionId
            && t.Kind == TranscriptKinds.UserPrompt
            && t.Text == stale))
            .ShouldBe(1, "sanity: a genuinely NEW UserPrompt record did arrive during the window");

        var incident = await db.AgentIncidents.SingleOrDefaultAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
        incident.ShouldNotBeNull();
        incident.Message.ShouldContain("never became a transcript record");
    }

    // Sequence advance alone can no longer say Delivered. The fake still emits SubmitAck on every
    // swallowed Enter, so the screen genuinely redraws — the exact signal that used to be enough.
    [Test]
    public async Task Screen_output_advancing_without_a_record_is_no_longer_delivered()
    {
        await using var h = await ObservableHarnessAsync();
        h.Adapter.SwallowSubmits = 99; // every Enter redraws and submits nothing
        h.Adapter.SubmitAck.ShouldNotBeEmpty("sanity: the screen really does advance on each Enter");

        await h.Queue.EnqueueAsync(
            h.SessionId, "into a composer that keeps it", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["into a composer that keeps it", "\r", "\r", "\r"],
            "SubmitAttempts Enters total, and never a re-typed body");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        var incident = await db.AgentIncidents.SingleOrDefaultAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
        incident.ShouldNotBeNull();
        incident.Message.ShouldContain("never became a transcript record");
    }

    // Degrade, never fail, when ground truth is absent (the echo-probe lesson). A session with NO
    // transcript rows is either not bound yet — a fresh session's launch note is queued before its
    // JSONL exists (CARD-0006) — or its bind failed. Those keep the legacy screen-only verdict.
    [Test]
    public async Task A_session_with_no_transcript_entries_keeps_the_legacy_screen_only_verdict()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.SwallowSubmits = 99; // would fail transcript confirmation outright

        await h.Queue.EnqueueAsync(
            h.SessionId, "the launch note, before any transcript exists",
            MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["the launch note, before any transcript exists", "\r"],
            "no confirm loop, so no re-press");

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent, "screen advanced: today's verdict still stands here");
        // CARD-0180 S3: the screen-only fallback is now an incident (observation only — still Sent,
        // still no kill). Deduped per session; this is the first send so it records once.
        var unverified = await db.AgentIncidents.SingleOrDefaultAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified);
        unverified.ShouldNotBeNull();
        unverified.Severity.ShouldBe(AlertSeverity.Warning);
        unverified.Message.ShouldNotContain("Send-now", Case.Sensitive,
            "CARD-0201: this was a WhenIdle queue delivery; the observation must not name Mode:Now");
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task Codex_unobservable_body_trailing_frames_are_not_submit_evidence_and_re_enter_until_no_submit_output()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";
        h.Adapter.ComposerFramesAfterEvidence = [" frame one", " frame two", " frame three"];

        var ex = await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueAsync(
            h.SessionId, "codex body that keeps rendering after evidence", MessageSendMode.Now,
            CancellationToken.None));

        h.Adapter.Inputs.ShouldBe([
            "codex body that keeps rendering after evidence", "\r", "\r", "\r"],
            "Codex must not credit the body's trailing frames to the submitting Enter");
        ex.Message.ShouldContain("submitting Enter produced no output");
    }

    [Test]
    public async Task Codex_unobservable_no_post_enter_output_returns_no_submit_output_after_three_enters()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";

        var ex = await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueAsync(
            h.SessionId, "codex enter produces nothing", MessageSendMode.Now, CancellationToken.None));

        h.Adapter.Inputs.ShouldBe(["codex enter produces nothing", "\r", "\r", "\r"]);
        ex.Message.ShouldContain("submitting Enter produced no output");
    }

    [Test]
    public async Task Codex_unobservable_transient_empty_frame_does_not_latch_emptied_composer()
    {
        // CARD-0299: echo the body, one empty/ghost snapshot, then the body again. Today's
        // hole latched emptied-composer on that single poll, suppressed re-Enter, and
        // certified Sent after 1 Enter. Must send 3 Enters and return NoSubmitOutput.
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";
        h.Adapter.EmptyComposerSnapshotsAfterEnter = 1;

        var ex = await Should.ThrowAsync<ConflictException>(() => h.Queue.EnqueueAsync(
            h.SessionId, "codex body that flickers empty then returns", MessageSendMode.Now,
            CancellationToken.None));

        h.Adapter.Inputs.ShouldBe([
            "codex body that flickers empty then returns", "\r", "\r", "\r"],
            "a single empty/ghost snapshot must not latch emptied-composer and suppress re-Enter");
        ex.Message.ShouldContain("submitting Enter produced no output");

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified))
            .ShouldBeFalse("must not Sent / DeliveryUnverified on a transient empty frame");
    }

    [Test]
    public async Task Codex_unobservable_working_indicator_confirms_by_screen()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "\n• Working (0s • esc to interrupt)";

        var receipt = await h.Queue.EnqueueAsync(
            h.SessionId, "codex working indicator confirms submit", MessageSendMode.Now,
            CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["codex working indicator confirms submit", "\r"]);
        receipt.LastDelivery!.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Screen);
    }

    [Test]
    public async Task Claude_unobservable_keeps_advance_based_screen_verdict_after_settled_baseline()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "\nclaude submit redraw";
        h.Adapter.ComposerFramesAfterEvidence = [" body frame one", " body frame two", " body frame three"];

        var receipt = await h.Queue.EnqueueAsync(
            h.SessionId, "claude body that finishes rendering", MessageSendMode.Now, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["claude body that finishes rendering", "\r"]);
        receipt.LastDelivery!.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Screen);
    }

    [Test]
    public async Task Grok_unobservable_redraw_with_body_visible_is_NoSubmitOutput_not_Sent()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "\nStarting session\nMCP (0/2)";

        await h.Queue.EnqueueAsync(
            h.SessionId, "grok body that stays in the composer after a redraw",
            MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe([
            "grok body that stays in the composer after a redraw", "\r", "\r", "\r"]);
        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryVerdict.ShouldBe(DeliveryVerdict.NoSubmitOutput);
        message.SentAt.ShouldBeNull();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified))
            .ShouldBeFalse("redraw-only output must not certify Sent / DeliveryUnverified");
    }

    [Test]
    public async Task Grok_unobservable_transient_empty_frame_does_not_latch_emptied_composer()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        h.Adapter.SwallowSubmits = 99;
        h.Adapter.SubmitAck = "";
        h.Adapter.EmptyComposerSnapshotsAfterEnter = 1;

        await h.Queue.EnqueueAsync(
            h.SessionId, "grok body that flickers empty then returns",
            MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe([
            "grok body that flickers empty then returns", "\r", "\r", "\r"]);
        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryVerdict.ShouldBe(DeliveryVerdict.NoSubmitOutput);
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified))
            .ShouldBeFalse();
    }

    [Test]
    public async Task Grok_unobservable_sustained_composer_departure_confirms_by_screen()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;
        h.Adapter.SubmitAck = "";

        var receipt = await h.Queue.EnqueueAsync(
            h.SessionId, "grok body that leaves the composer without a transcript",
            MessageSendMode.Now, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["grok body that leaves the composer without a transcript", "\r"]);
        receipt.LastDelivery!.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Screen);
        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified))
            .ShouldBeTrue();
    }

    [Test]
    public async Task Grok_matching_UserPrompt_still_wins_as_transcript_proof()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        await SetKindAsync(h.SessionId, AgentKind.Grok);

        var receipt = await h.Queue.EnqueueAsync(
            h.SessionId, "grok body confirmed by a matching UserPrompt row",
            MessageSendMode.Now, CancellationToken.None);

        receipt.LastDelivery!.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Transcript);
        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified))
            .ShouldBeFalse();
    }

    // CARD-0201: the mirror of the test above, and the distinction it turns on. The same
    // pre-first-turn session (zero transcript rows, so CARD-0164's unobservable-baseline loop),
    // but the submitted prompt DOES land as a TIMESTAMPED UserPrompt row — the shape a bound
    // transcript produces. That confirms through the transcript-first arm the moment the row
    // exists: no deadline, no screen-only fallback, and therefore no DeliveryUnverified
    // observation, because nothing was left unverified. Two "leaves no incident" tests in this
    // class rode the fallback for a day after CARD-0180 S3 made it an incident, because the
    // harness stamped no timestamp and the loop treats a null stamp as no evidence — this pins
    // the boundary with a deadline long enough that falling back cannot pass unnoticed.
    [Test]
    public async Task A_pre_first_turn_delivery_whose_record_is_timestamped_confirms_by_transcript_not_the_fallback()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            // The fallback fires only AT the deadline; make reaching it unmistakable in the timing.
            ConfigureDeliveryVerification = v => v.TranscriptConfirmTimeoutSeconds = 20,
        });
        const string body = "the launch note, before any transcript exists, confirmed by its row";
        var started = DateTime.UtcNow;

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(10),
            "a transcript-confirmed delivery returns as soon as the row lands; only the fallback waits out the 20s deadline");
        h.Adapter.Inputs.ShouldBe([body, "\r"], "confirmed on the first Enter — no re-press");
        h.Adapter.SubmittedBodies.ShouldBe([body]);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
        (await IncidentsOfAsync(db, h.AgentId))
            .ShouldBeEmpty("nothing was left unverified, so there is nothing to observe");
        h.Adapter.Killed.ShouldBeFalse();
    }

    // A body too short to identify by text (the auto-continue "Continue.") takes the weak arm: the
    // record's existence is the confirmation. It must not spuriously re-press.
    [Test]
    public async Task A_body_too_short_to_identify_confirms_on_the_weak_arm()
    {
        await using var h = await ObservableHarnessAsync();
        PromptSubmissionMatch.RequiresTextMatch("Continue.")
            .ShouldBeFalse("sanity: this body is below MinMatchChars, so no text match is available");

        await h.Queue.EnqueueAsync(h.SessionId, "Continue.", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["Continue.", "\r"], "one Enter — the weak arm confirmed immediately");
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    // A batched channel delivery runs well past the 200-char match window, so it can only confirm
    // on its HEAD — which is stable framing ChannelPromptFormat produces and Claude records verbatim.
    [Test]
    public async Task A_batch_body_longer_than_the_match_window_confirms_on_its_head()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Bridge = new Antiphon.Server.Application.Settings.ChannelBridgeSettings
            {
                Enabled = true,
                BatchingEnabled = true,
                DebounceWindowMs = 0,
            },
        });

        await h.InsertTurnAsync("an earlier prompt", "an earlier answer");
        await h.MarkWorkingAsync(); // hold them pending so they coalesce into one delivery
        foreach (var n in new[] { "one", "two", "three" })
        {
            await h.Queue.EnqueueAsync(
                h.SessionId,
                $"[Telegram \"Family\" - Mike] message {n}: " + new string('x', 120),
                MessageSendMode.WhenIdle, CancellationToken.None,
                origin: QueuedMessageOrigin.Channel, conversationKey: "telegram:-100777");
        }

        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        var body = h.Adapter.SubmittedBodies.ShouldHaveSingleItem();
        body.Length.ShouldBeGreaterThan(
            PromptSubmissionMatch.MatchWindowChars,
            "sanity: this only proves anything if the body outruns the window");
        h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(1, "confirmed on the head — no re-press");

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.Where(m => m.AgentSessionId == h.SessionId).ToListAsync())
            .ShouldAllBe(m => m.Status == QueuedMessageStatus.Sent);
    }

    // ---- CARD-0055 slice 3: the failure path ---------------------------------------------------
    //
    // A failed verification is ambiguous by construction: either the body never reached Claude, or
    // it did and the matcher was blind. Everything here exists so the ambiguity is resolved by the
    // transcript rather than by re-typing and hoping.

    // THE anti-duplicate pin. A previously attempted message whose body IS in the transcript past
    // its stored baseline is marked Sent with ZERO writes to the terminal — the automatic retry is
    // safe only because it looks before it types.
    [Test]
    public async Task Late_confirm_marks_the_message_sent_with_zero_writes_to_the_terminal()
    {
        await using var h = await ObservableHarnessAsync();
        const string body = "the delegation brief that actually went in";

        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: floor);
        // It really did land — the first attempt's confirmation was simply blind.
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty("late-confirmed: nothing may be typed, not even an Enter");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.SentAt.ShouldNotBeNull();
        (await IncidentsOfAsync(db, h.AgentId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Queued_user_prompt_confirms_delivery_without_a_second_enter()
    {
        await using var h = await ObservableHarnessAsync();
        h.Adapter.OnSubmitted = async submitted =>
        {
            await h.InsertTranscriptEntryAsync(TranscriptKinds.QueuedUserPrompt, submitted);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        };

        await h.Queue.EnqueueAsync(
            h.SessionId, "the completion note Claude accepted into its composer queue",
            MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(
            ["the completion note Claude accepted into its composer queue", "\r"],
            "the queued_command record is the confirmation; it must not cause an Enter re-press");
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    [Test]
    public async Task Queue_enqueue_does_not_confirm_delivery()
    {
        await using var h = await ObservableHarnessAsync(alwaysOn: false);
        const string body = "a body the TUI queued because a modal was standing";
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: floor);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.QueueEnqueue, body);
        // If late-confirm treated the enqueue as proof, it would mark Sent with zero writes.
        // Block redelivery so a later successful submit cannot masquerade as that confirm.
        h.Adapter.ThrowOnSend = new InvalidOperationException("redelivery must not run for this pin");

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldNotBe(QueuedMessageStatus.Sent, "CARD-0132 S2.2: enqueue is still not proof of submit");
    }

    [Test]
    public async Task ModeNow_question_tool_ToolResult_confirms_without_a_second_enter()
    {
        await using var h = await ObservableHarnessAsync();
        const string body = "Proceed as planned (Recommended)";
        h.Adapter.OnSubmitted = async submitted =>
        {
            await h.InsertTranscriptEntryAsync(
                TranscriptKinds.ToolResult,
                $"{GrokQuestionTool.CompletedAnswerPrefix} \"q\"=\"{submitted}\". You can now continue.",
                toolName: GrokQuestionTool.AskUserQuestionName,
                toolUseId: "call-question-now");
        };

        var dto = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.Now, CancellationToken.None);
        dto.LastDelivery.ShouldNotBeNull();
        dto.LastDelivery!.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Transcript);
        h.Adapter.Inputs.ShouldBe([body, "\r"]);
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId))
            .ShouldBe(0, "Mode:Now stays row-less; S2 is what makes the popup answer return 200");
        (await IncidentsOfAsync(db, h.AgentId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Question_tool_ToolResult_confirms_delivery_without_a_second_enter()
    {
        await using var h = await ObservableHarnessAsync();
        const string body = "Proceed as planned (Recommended)";
        h.Adapter.OnSubmitted = async submitted =>
        {
            await h.InsertTranscriptEntryAsync(
                TranscriptKinds.ToolResult,
                $"{GrokQuestionTool.CompletedAnswerPrefix} \"q\"=\"{submitted}\". You can now continue.",
                toolName: GrokQuestionTool.AskUserQuestionName,
                toolUseId: "call-question-1");
        };

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe([body, "\r"],
            "the completed ask_user_question ToolResult is the confirmation; no Enter re-press");
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
        (await IncidentsOfAsync(db, h.AgentId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Read_file_ToolResult_does_not_confirm_delivery()
    {
        await using var h = await ObservableHarnessAsync(alwaysOn: false);
        const string body = "Proceed as planned (Recommended)";
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: floor);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.ToolResult,
            $"file contents that happen to mention {body}",
            toolName: "read_file",
            toolUseId: "call-read-1");
        h.Adapter.ThrowOnSend = new InvalidOperationException("redelivery must not run for this pin");

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldNotBe(QueuedMessageStatus.Sent,
                "Claude/read_file ToolResult must not confirm — CARD-0241 does not widen to every ToolResult");
    }

    [Test]
    public async Task Claude_ToolResult_without_question_wrapper_does_not_confirm()
    {
        await using var h = await ObservableHarnessAsync(alwaysOn: false);
        const string body = "Proceed as planned (Recommended)";
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: floor);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.ToolResult,
            body,
            toolName: null,
            toolUseId: "toolu_bash");
        h.Adapter.ThrowOnSend = new InvalidOperationException("redelivery must not run for this pin");

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldNotBe(QueuedMessageStatus.Sent);
    }

    [Test]
    public async Task Queued_user_prompt_late_confirm_never_types_the_body_twice()
    {
        await using var h = await ObservableHarnessAsync(alwaysOn: false);
        const string body = "the completion note whose first attempt entered Claude's composer queue";
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: floor);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.QueuedUserPrompt, body);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty("late-confirm must see the queued_command before any retry");
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    // The same thing end to end, which is the shape that actually happens in production: a delivery
    // fails verification, the body turns out to have gone in anyway, and the NEXT flush must not
    // put it in a second time.
    [Test]
    public async Task A_failed_delivery_whose_body_landed_anyway_is_never_typed_twice()
    {
        await using var h = await ObservableHarnessAsync(alwaysOn: false);
        const string body = "the channel reply a human is waiting for";
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using (var failed = CreateContext())
        {
            (await failed.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
                .Status.ShouldBe(QueuedMessageStatus.Pending, "precondition: the delivery failed");
        }

        // The truth arrives late: the body was submitted after all (a stalled tailer catches up,
        // or a later Enter pushed the held composer in).
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        var writesBefore = h.Adapter.Inputs.Count;
        h.Adapter.SwallowSubmits = 0; // a re-type would now succeed — the point is that none happens
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.Count.ShouldBe(writesBefore, "the redelivery looked first and found the body already in");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    [Test]
    public async Task Attempt_metadata_survives_the_revert_a_failed_delivery_does()
    {
        await using var h = await ObservableHarnessAsync();
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.EnqueueAsync(
            h.SessionId, "a body that will not be confirmed", MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBe(1, "the attempt happened; the revert must not pretend otherwise");
        message.LastDeliveryStartedAt.ShouldNotBeNull();
        message.LastDeliveryBaselineSequence.ShouldBe(floor, "the floor the next late-confirm will read");
    }

    // The loop has to stop somewhere. At the cap the message parks: still Pending, still visible in
    // the queue UI (where cancel and re-enqueue exist), but no automatic path types it again.
    [Test]
    public async Task A_message_at_the_attempts_cap_parks_and_the_watchdog_leaves_it_alone()
    {
        await using var h = await ObservableHarnessAsync();
        await h.SeedPendingMessageAsync(
            "a body that has failed three times", deliveryAttempts: 3, baselineSequence: 999_999);

        (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None))
            .ShouldBe(0, "a parked message must not even wake the watchdog");
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty("no automatic path re-types a parked message");
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending, "parked, not lost — a human can still see and resend it");
    }

    // Parking a CHANNEL-bound agent's message is a human waiting on a dead line, so the incident is
    // Critical (the mirror of TranscriptBindFailed's severity rule). MaxDeliveryAttempts=1 parks on
    // the first failure so the whole path runs in one delivery.
    [Test]
    public async Task Parking_a_channel_bound_agents_message_raises_a_critical_incident()
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = true,
            Supervision = new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings
                {
                    Enabled = true,
                    EvidenceTimeoutSeconds = 1,
                    PollIntervalMs = 50,
                    PostSubmitAdvanceTimeoutSeconds = 1,
                    StrandedAgeSeconds = 0,
                    TranscriptConfirmTimeoutSeconds = 2,
                    ReEnterIntervalSeconds = 1,
                    MaxDeliveryAttempts = 1,
                },
            },
        });

        await h.InsertTurnAsync("an earlier prompt", "an earlier answer");
        await h.BindChannelAsync();
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.EnqueueAsync(
            h.SessionId, "the reply that never reached the agent", MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        incident.Message.ShouldContain("PARKED");
        incident.Message.ShouldContain("channel-bound");

        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    // The working-kill guard: a session that is NOW working is evidence the submit may have
    // succeeded with the matcher blind. Killing it would abort a live turn to settle a bookkeeping
    // doubt. Modelled exactly that way — the submit lands and the agent starts working, but no
    // UserPrompt row is ever ingested.
    [Test]
    public async Task A_working_session_is_not_killed_when_the_record_never_arrives()
    {
        await using var h = await ObservableHarnessAsync();
        h.Adapter.OnSubmitted = _ =>
            h.InsertTranscriptEntryAsync(TranscriptKinds.AssistantText, "on it");

        await h.Queue.EnqueueAsync(
            h.SessionId, "the body whose record never got ingested",
            MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Killed.ShouldBeFalse("never abort a live turn over a bookkeeping doubt");

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending, "still queued — the next turn-end flush late-confirms it");
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed);
        incident.Message.ShouldContain("mid-turn");
        incident.Severity.ShouldBe(AlertSeverity.Error);
    }

    // The post-failure grace window (live miss 2026-08-16, CARD-0047's check interpreter). The
    // confirm deadline expiring says OUR INGESTION had not caught up, which is not the same claim
    // as "the submit failed" — on session 22e0df09 the record landed 0.8s after the verdict, by
    // which time the always-on kill had already destroyed a session that had taken the message
    // correctly, and the existing late-confirm ran on the corpse. A brand-new session is where this
    // bites: its transcript file does not exist until the first submit creates it, so discovery,
    // binding and first ingestion all land inside the confirm window.
    [Test]
    public async Task A_record_that_lands_just_after_the_deadline_confirms_instead_of_killing()
    {
        await using var h = await ObservableHarnessAsync();
        // Submitted for real, but the row shows up after the confirm deadline (3s) and inside the
        // grace (3s) — modelling ingestion lag, not a failed submit.
        h.Adapter.OnSubmitted = body =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(4));
                await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, body);
            });
            return Task.CompletedTask;
        };

        await h.Queue.EnqueueAsync(
            h.SessionId, "the body whose record was merely slow", MessageSendMode.WhenIdle,
            CancellationToken.None);

        h.Adapter.Killed.ShouldBeFalse("the session took the message; killing it destroys working state");
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent, "the body IS in the transcript — it delivered");
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeFalse("nothing failed, so nothing is worth alerting a human about");
    }

    // The other side of the guard: an IDLE always-on session with no record is the wedge case the
    // kill exists for — restart it, get a fresh composer, let the watchdog redeliver.
    [Test]
    public async Task An_idle_always_on_session_with_no_record_is_still_killed()
    {
        await using var h = await ObservableHarnessAsync();
        h.Adapter.SwallowSubmits = 99; // nothing submitted, so the session stays idle

        await h.Queue.EnqueueAsync(
            h.SessionId, "into a composer that keeps everything", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Killed.ShouldBeTrue();
        await using var db = CreateContext();
        (await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .Message.ShouldContain("Restarting the session");
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

    // ---- CARD-0024: identity is not completeness -----------------------------------------------
    //
    // A UserPrompt whose HEAD matches is Sent under CARD-0055. A clip that keeps the opening
    // frame (first-chunk-only, or the 2026-08-10 head+tail splice) therefore used to be marked
    // Sent. Completeness is a second check on the same row: identity without it parks immediately,
    // does not re-type, does not kill, and late-confirm must not promote the splice.

    private static string LongQueuedBody() =>
        "CARD-0024 truncation body — head frame that identity matches. " + new string('x', 800);

    private static async Task ClipSubmitToPrefixAsync(BridgeQueueHarness h, int keepChars = 250)
    {
        h.Adapter.OnSubmitted = async submitted =>
        {
            var clipped = submitted.Length <= keepChars ? submitted : submitted[..keepChars];
            await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, clipped);
            await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        };
    }

    [Test]
    public async Task A_clipped_prefix_parks_as_truncated_not_sent()
    {
        await using var h = await ObservableHarnessAsync();
        var body = LongQueuedBody();
        body.Length.ShouldBeGreaterThan(PromptSubmissionMatch.MatchWindowChars);
        PromptSubmissionMatch.IsConfirmedBy(body, body[..250]).ShouldBeTrue("sanity: this clip still identifies");
        PromptSubmissionMatch.IsCompleteIn(body, body[..250]).ShouldBeFalse("sanity: this clip is incomplete");
        await ClipSubmitToPrefixAsync(h);

        var dto = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Messages.Count.ShouldBe(1);
        dto.Messages[0].Status.ShouldBe(nameof(QueuedMessageStatus.Pending));
        dto.Messages[0].Parked.ShouldBeTrue("truncated parks immediately — not after MaxDeliveryAttempts retries");
        h.Adapter.Inputs.ShouldBe([body, "\r"], "no re-press and no re-type: the splice is already the current turn");
        h.Adapter.Killed.ShouldBeFalse("the session took a turn; truncation is not a wedge");

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(3, "parked: attempts jumped to the cap so no automatic path retries");
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery);
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.Message.ShouldContain("PARKED");
        incident.Message.ShouldContain("splice");
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeFalse("truncation is not a wedge and must not reuse the verification-failed kind");
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.OversizedTerminalDelivery))
            .ShouldBeFalse("size-before-send is a different signal; do not conflate it with a measured splice");
    }

    [Test]
    public async Task A_complete_long_body_still_marks_sent()
    {
        await using var h = await ObservableHarnessAsync();
        var body = LongQueuedBody();

        var dto = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Messages.ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBe([body]);
        h.Adapter.Killed.ShouldBeFalse();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
        (await IncidentsOfAsync(db, h.AgentId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Truncation_parks_immediately_and_the_watchdog_does_not_retype()
    {
        await using var h = await ObservableHarnessAsync();
        var body = LongQueuedBody();
        await ClipSubmitToPrefixAsync(h);

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);
        var writes = h.Adapter.Inputs.Count;
        writes.ShouldBe(2);

        (await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None))
            .ShouldBe(0, "a truncated park must not even wake the watchdog");
        h.Adapter.Inputs.Count.ShouldBe(writes);
        h.Adapter.SubmittedBodies.Count.ShouldBe(1, "the body was typed once; parking forbids a second copy");
    }

    [Test]
    public async Task Truncating_a_channel_bound_agent_raises_a_critical_incident()
    {
        await using var h = await ObservableHarnessAsync();
        await h.BindChannelAsync();
        var body = LongQueuedBody();
        await ClipSubmitToPrefixAsync(h);

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery);
        incident.Severity.ShouldBe(AlertSeverity.Critical);
        incident.Message.ShouldContain("channel-bound");
        h.Adapter.Killed.ShouldBeFalse();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task Late_confirm_does_not_promote_a_truncated_body_to_sent()
    {
        await using var h = await ObservableHarnessAsync();
        var body = LongQueuedBody();
        var clipped = body[..250];

        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: floor);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, clipped);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty("truncated late-confirm parks; it must not type a second copy");
        await using (var db = CreateContext())
        {
            var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
            message.Status.ShouldBe(QueuedMessageStatus.Pending);
            message.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(3);
            var incidents = await db.AgentIncidents
                .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery)
                .ToListAsync();
            incidents.ShouldHaveSingleItem();
        }

        // A subsequent flush must not raise a second incident or flip the row to Sent.
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);
        await using var again = CreateContext();
        (await again.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
        (await again.AgentIncidents.CountAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery))
            .ShouldBe(1, "deduped on the message id — late-confirm must not raise a second row");
    }

    [Test]
    public async Task A_truncated_queued_user_prompt_parks_instead_of_becoming_sent()
    {
        await using var h = await ObservableHarnessAsync();
        var body = LongQueuedBody();
        var floor = await h.CurrentTranscriptMaxSequenceAsync();
        await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: floor);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.QueuedUserPrompt, body[..250]);
        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty("a clipped queued_command must not be re-typed");
        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(3, "incomplete confirmation parks the row");
    }

    // ---- CARD-0103 slice 2: a pre-first-turn NoComposerEvidence does not consume an attempt ----
    //
    // The budget arithmetic was the defect. Three attempts on a ~60s watchdog cadence is ~2.5
    // minutes, and a Claude TUI that is painted but not yet draining stdin was measured deaf for
    // 48-200 seconds (2026-08-20). So the whole retry budget was spendable INSIDE one stall: the
    // brief parked at 2:30 in a session that was perfectly healthy, and the dispatcher failed the
    // task at 10:00 with "Boot prompt was never delivered."
    //
    // The refund is scoped to the triple condition and every leg is negated below, because the leak
    // that matters is into the "started working and then got stuck" case CARD-0055 already handles.

    [Test]
    public async Task A_pre_first_turn_no_evidence_refunds_the_attempt_and_withholds_the_kill()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.EchoTypedInputToScreen = false; // painted, but not reading

        await h.Queue.EnqueueAsync(
            h.SessionId, "the delegate brief", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["the delegate brief", "\u001b", "the delegate brief"],
            "S5 one-shot Esc-and-retype; Enter is still withheld — that is unchanged");

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBe(0,
            "the attempt is refunded: nothing was submitted (the Enter was withheld) and the session "
            + "has produced no transcript row at all, so it is still waking rather than wedged");
        message.LastDeliveryStartedAt.ShouldNotBeNull(
            "the attempt still HAPPENED — only the charge is refunded, and the timestamp is what says so");
        message.LastDeliveryBaselineSequence.ShouldBeNull("the pre-first-turn signal itself");

        h.Adapter.Killed.ShouldBeFalse(
            "killing a session that is merely still becoming ready relaunches it straight into the "
            + "same race — CARD-0047's restart loop by another route");
    }

    [Test]
    public async Task The_refunded_failure_reports_one_warning_not_an_error_per_attempt()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.EchoTypedInputToScreen = false;

        await h.Queue.EnqueueAsync(
            h.SessionId, "the delegate brief", MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var incident = (await db.AgentIncidents
            .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed)
            .ToListAsync())
            .ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Warning,
            "not an Error: the session is healthy and the message is coming back round");
        incident.Message.ShouldContain("no transcript activity");
        incident.Message.ShouldContain("refunded");
    }

    // The point of the refund: the message stays eligible for the 60s stranded sweep instead of
    // parking after three tries, so it gets ~8 chances inside the dispatcher's 10-minute watchdog.
    [Test]
    public async Task A_refunded_message_survives_more_sweeps_than_the_attempt_cap_allows()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.EchoTypedInputToScreen = false;

        await h.Queue.EnqueueAsync(
            h.SessionId, "the delegate brief", MessageSendMode.WhenIdle, CancellationToken.None);
        for (var sweep = 0; sweep < 4; sweep++)
            await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        h.Adapter.Inputs.Count(i => i == "the delegate brief").ShouldBe(10,
            "the first delivery plus four sweeps, each with S5's retype — under the old accounting "
            + "the message would have parked after three and the last two sweeps would have skipped it");
        h.Adapter.Inputs.Count(i => i == "\u001b").ShouldBe(5, "one Esc per delivery, never two");
        h.Adapter.Inputs.ShouldNotContain("\r");

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBe(0, "every one of them was refunded");
        (await db.AgentIncidents.CountAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBe(1, "one fault per message, not one incident per attempt");
        h.Adapter.Killed.ShouldBeFalse();
    }

    // THE negative case. A session that started working and then stalled has a transcript, so its
    // stamped baseline is non-null, so nothing here is refunded and CARD-0055's design is untouched.
    [Test]
    public async Task A_session_that_worked_and_then_stalled_still_charges_the_attempt_and_is_killed()
    {
        await using var h = await ObservableHarnessAsync();
        h.Adapter.EchoTypedInputToScreen = false;

        await h.Queue.EnqueueAsync(
            h.SessionId, "into a stalled composer", MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.DeliveryAttempts.ShouldBe(1,
            "this session HAS taken a turn: no-evidence here is a wedge, and the attempt is spent");
        message.LastDeliveryBaselineSequence.ShouldNotBeNull();
        h.Adapter.Killed.ShouldBeTrue("the wedged-composer kill is unchanged for the shape it was built for");

        var incident = (await db.AgentIncidents
            .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed)
            .ToListAsync())
            .ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Error);
    }

    // The grace is a wall clock from ENQUEUE, and it is deliberately inside the dispatcher's
    // 10-minute watchdog: a genuinely dead session still spends its attempts and still fails loudly.
    [Test]
    public async Task A_pre_first_turn_message_past_its_grace_charges_normally_and_is_killed()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.EchoTypedInputToScreen = false;
        await h.SeedPendingMessageAsync(
            "a brief that has been failing for twenty minutes",
            createdAtUtc: DateTime.UtcNow - TimeSpan.FromMinutes(20));

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.DeliveryAttempts.ShouldBe(1, "past the grace the counter is honest again");
        message.LastDeliveryBaselineSequence.ShouldBeNull("still pre-first-turn — only the clock differs");
        h.Adapter.Killed.ShouldBeTrue();
        (await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .Severity.ShouldBe(AlertSeverity.Error);
    }

    // Refund only on THIS verdict. NoSubmitOutput means the Enter went out, so something may have
    // been submitted — "not charged" is not provably safe there and is not offered.
    [Test]
    public async Task A_pre_first_turn_swallowed_submit_still_charges_the_attempt()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        // Enter lands, produces no output, submits nothing: NoSubmitOutput, not NoComposerEvidence.
        // SwallowSubmits is required — a recorded prompt would confirm the delivery (CARD-0201).
        h.Adapter.SubmitAck = "";
        h.Adapter.SwallowSubmits = 99;

        await h.Queue.EnqueueAsync(
            h.SessionId, "a body whose enter went out", MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.DeliveryAttempts.ShouldBe(1, "the Enter was sent; the attempt is not refundable");
        message.LastDeliveryBaselineSequence.ShouldBeNull();
        h.Adapter.Killed.ShouldBeTrue();
    }

    // Not always-on: no kill either way, but the accounting still has to be right, because the
    // attempt counter is what the queue UI and every flush predicate read.
    [Test]
    public async Task The_refund_applies_to_non_always_on_agents_too()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.EchoTypedInputToScreen = false;

        await h.Queue.EnqueueAsync(
            h.SessionId, "a manual agent's first message", MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .DeliveryAttempts.ShouldBe(0);
        h.Adapter.Killed.ShouldBeFalse();
    }

    // ---- CARD-0137 S3 / L0: refuse Forbidden bodies before a byte is typed --------------------
    //
    // The catalog named Codex `/usage` as forbidden ("opens a picker whose highlighted option
    // redeems the account's one usage-limit reset") but the normal EnqueueAsync/DeliverAsync path
    // never read that map. Sending `{"Body":"/usage"}` to a Codex session would type it, pass
    // composer evidence, press Enter (firing the picker), then CARD-0055's confirm loop would
    // re-press Enter into that picker. These tests pin that the hole is closed at BOTH call sites.

    [Test]
    public async Task Codex_slash_usage_is_refused_at_enqueue_with_zero_bytes_typed()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetKindAsync(h.SessionId, AgentKind.Codex);

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            h.Queue.EnqueueAsync(h.SessionId, "/usage", MessageSendMode.WhenIdle, CancellationToken.None));

        ex.Errors.ContainsKey("body").ShouldBeTrue();
        ex.Errors["body"].ShouldContain(s => s.Contains("reset", StringComparison.OrdinalIgnoreCase));
        h.Adapter.Inputs.ShouldBeEmpty("nothing may be typed — not the body, not Enter, not Esc");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        h.Adapter.Killed.ShouldBeFalse();

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId))
            .ShouldBe(0, "EnqueueAsync refuses before persist");
        (await db.AgentIncidents.CountAsync(i => i.SessionId == h.SessionId)).ShouldBe(0);
    }

    [Test]
    public async Task Codex_slash_usage_Now_is_refused_with_zero_bytes_typed()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetKindAsync(h.SessionId, AgentKind.Codex);

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            h.Queue.EnqueueAsync(h.SessionId, "/usage", MessageSendMode.Now, CancellationToken.None));

        ex.Errors["body"].ShouldContain(s => s.Contains("reset", StringComparison.OrdinalIgnoreCase));
        h.Adapter.Inputs.ShouldBeEmpty();
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task Codex_slash_usage_with_arguments_is_refused_too()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetKindAsync(h.SessionId, AgentKind.Codex);

        await Should.ThrowAsync<ValidationException>(() =>
            h.Queue.EnqueueAsync(
                h.SessionId, "/usage --json", MessageSendMode.WhenIdle, CancellationToken.None));

        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task A_queued_Codex_slash_usage_is_refused_at_deliver_parks_and_does_not_kill()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        await h.SeedPendingMessageAsync("/usage");

        await h.Queue.FlushStrandedQueuesAsync(CancellationToken.None);

        h.Adapter.Inputs.ShouldBeEmpty("DeliverAsync refuses before SendInputAsync");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        h.Adapter.Killed.ShouldBeFalse("refusing a Forbidden body is never a wedge");

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(3, "parked immediately — retrying a body we refuse to type is pointless");
        message.SentAt.ShouldBeNull();

        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.ForbiddenTerminalBody);
        incident.Severity.ShouldBe(AlertSeverity.Error);
        incident.Message.ShouldContain("reset", Case.Insensitive);
        incident.Message.ShouldContain("PARKED");
        incident.Message.ShouldContain("not restarted");
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeFalse("a refused body is not a wedge");
    }

    [Test]
    public async Task The_poll_path_still_throws_on_Codex_slash_usage_after_Forbidden_moved()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        var poll = new LocalCommandPoll(
            AgentKind.Codex, "/usage", [], OpensOverlay: false,
            OverlaySettleMs: 0, PanelTimeoutSeconds: 2);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => h.Queue.TryPollLocalCommandAsync(h.SessionId, poll, CancellationToken.None));
        ex.Message.ShouldContain("/usage");
        ex.Message.ShouldContain("reset", Case.Insensitive);
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Claude_auto_compact_still_enqueues_and_delivers_through_the_normal_path()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, ContextCompactionService.CompactTriggerBody,
            MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Messages.ShouldBeEmpty("idle Claude: /compact delivers straight away");
        h.Adapter.SubmittedBodies.ShouldBe([ContextCompactionService.CompactTriggerBody]);
        h.Adapter.Inputs.ShouldContain("\r");
        h.Adapter.Killed.ShouldBeFalse();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
        (await IncidentsOfAsync(db, h.AgentId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Grok_slash_usage_is_not_forbidden_and_still_types()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetKindAsync(h.SessionId, AgentKind.Grok);

        await h.Queue.EnqueueAsync(
            h.SessionId, "/usage", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldNotBeEmpty("Grok /usage is a declared command, not a Forbidden one");
        h.Adapter.Inputs.ShouldContain("/usage");
    }

    // ---- CARD-0137 S4 / L1: local-command arm — one Enter, no confirm loop, no kill ----------

    [Test]
    public async Task A_WritesUserPrompt_false_command_sends_exactly_one_Enter_and_skips_confirm()
    {
        await using var h = await ObservableHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        // Real Grok /usage writes no UserPrompt. The harness's OnSubmitted would fake one and
        // make the confirm loop look like the local-command arm (one Enter, Sent). Null it so a
        // regression onto CARD-0055 re-presses Enter and this goes red.
        h.Adapter.OnSubmitted = null;

        await h.Queue.EnqueueAsync(h.SessionId, "/usage", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(1, "one Enter; a re-press would land on a picker");
        h.Adapter.Inputs.ShouldContain("/usage");
        h.Adapter.Killed.ShouldBeFalse();

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    [Test]
    public async Task A_WritesUserPrompt_true_command_keeps_the_confirm_loop_and_represses_Enter()
    {
        await using var h = await ObservableHarnessAsync();
        h.Adapter.SwallowSubmits = 1;

        await h.Queue.EnqueueAsync(
            h.SessionId, ContextCompactionService.CompactTriggerBody,
            MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.Count(i => i == "\r").ShouldBe(2,
            "CARD-0082 auto-compact stays on today's path: a swallowed Enter is re-pressed");
        h.Adapter.SubmittedBodies.ShouldBe([ContextCompactionService.CompactTriggerBody]);
        h.Adapter.Killed.ShouldBeFalse();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
        (await IncidentsOfAsync(db, h.AgentId)).ShouldBeEmpty();
    }

    // ---- CARD-0137 S5/S6: overlay recovery (reactive) and proactive detector -----------------

    [Test]
    public async Task NoComposerEvidence_on_a_working_session_sends_no_Esc()
    {
        await using var h = await ObservableHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        await h.MarkWorkingAsync();
        // Default OverlayScreen contains Grok's DetectFragments, so S6 matches AND S5 would
        // recover — both go through TryDismissOverlayAsync's working-gate.
        h.Adapter.OverlayOpen = true;

        await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.EnqueueAsync(
                h.SessionId, "into a permission dialog", MessageSendMode.Now, CancellationToken.None));

        h.Adapter.Inputs.ShouldBe(["into a permission dialog"]);
        h.Adapter.Inputs.ShouldNotContain("\u001b",
            "working (permission-dialog) sessions must never receive Esc, even when Supported");
    }

    [Test]
    public async Task NoComposerEvidence_on_idle_Supported_kind_sends_one_Esc_retypes_and_succeeds()
    {
        await using var h = await ObservableHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        h.Adapter.OverlayOpen = true;
        h.Adapter.OverlayScreen = "an unmeasured overlay that is not in DetectFragments";

        await h.Queue.EnqueueAsync(h.SessionId, "hello after overlay", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["hello after overlay", "\u001b", "hello after overlay", "\r"]);
        h.Adapter.SubmittedBodies.ShouldBe(["hello after overlay"]);
        h.Adapter.Killed.ShouldBeFalse();
        h.Adapter.OverlayOpen.ShouldBeFalse();
    }

    [Test]
    public async Task NoComposerEvidence_on_idle_Unknown_kind_sends_no_Esc()
    {
        await using var h = await ObservableHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Codex);
        h.Adapter.EchoTypedInputToScreen = false;

        await h.Queue.EnqueueAsync(
            h.SessionId, "codex is unknown", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["codex is unknown"]);
        h.Adapter.Inputs.ShouldNotContain("\u001b");
    }

    [Test]
    public async Task Overlay_recovery_is_one_shot_two_evidence_failures_produce_one_Esc()
    {
        await using var h = await ObservableHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        h.Adapter.OverlayOpen = true;
        h.Adapter.OverlayScreen = "an unmeasured overlay";
        h.Adapter.EchoTypedInputToScreen = false;

        await h.Queue.EnqueueAsync(
            h.SessionId, "still deaf after esc", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.Count(i => i == "\u001b").ShouldBe(1);
        h.Adapter.Inputs.ShouldBe(["still deaf after esc", "\u001b", "still deaf after esc"]);
        h.Adapter.Killed.ShouldBeTrue("idle always-on, evidence still missing after one Esc: today's kill");
    }

    [Test]
    public async Task Proactive_detector_Escs_before_typing_when_a_measured_fragment_is_visible()
    {
        await using var h = await ObservableHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        h.Adapter.OverlayOpen = true;

        await h.Queue.EnqueueAsync(h.SessionId, "after measured overlay", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs[0].ShouldBe("\u001b", "S6 Escs before the doomed type");
        h.Adapter.Inputs.ShouldBe(["\u001b", "after measured overlay", "\r"]);
        h.Adapter.SubmittedBodies.ShouldBe(["after measured overlay"]);
    }

    [Test]
    public async Task Proactive_detector_does_not_Esc_an_unmeasured_modal()
    {
        await using var h = await ObservableHarnessAsync();
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        h.Adapter.OverlayOpen = true;
        h.Adapter.OverlayScreen = "Do you want to approve this tool?";

        await h.Queue.EnqueueAsync(
            h.SessionId, "typed into an unmeasured modal", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs[0].ShouldNotBe("\u001b",
            "S6 must not match an unmeasured modal; S5 may still Esc after evidence fails");
        h.Adapter.Inputs[0].ShouldBe("typed into an unmeasured modal");
    }

    [Test]
    public async Task Mode_Now_waits_for_the_per_session_lock()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        var sem = h.Queue.GetLock(h.SessionId);
        await sem.WaitAsync();
        Task<SessionQueueDto> send;
        try
        {
            send = h.Queue.EnqueueAsync(
                h.SessionId, "now-locked", MessageSendMode.Now, CancellationToken.None);
            var finished = await Task.WhenAny(send, Task.Delay(400));
            finished.ShouldNotBe(send, "Mode.Now must wait on GetLock — the poll's invariant");
            h.Adapter.Inputs.ShouldBeEmpty("nothing may be typed while another path holds the lock");
        }
        finally
        {
            sem.Release();
        }

        var dto = await send;
        dto.Messages.ShouldBeEmpty();
        h.Adapter.SubmittedBodies.ShouldBe(["now-locked"]);
    }

    [Test]
    public async Task LocalCommandNotAccepted_never_kills_an_always_on_idle_agent()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetKindAsync(h.SessionId, AgentKind.Grok);
        h.Adapter.EchoTypedInputToScreen = false;

        await h.Queue.EnqueueAsync(
            h.SessionId, "/usage", MessageSendMode.WhenIdle, CancellationToken.None);

        h.Adapter.Inputs.ShouldBe(["/usage"], "Enter withheld when the composer did not take the command");
        h.Adapter.SubmittedBodies.ShouldBeEmpty();
        h.Adapter.Killed.ShouldBeFalse("parking is fine; killing is not");

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.SentAt.ShouldBeNull();
    }

    // ---- CARD-0164 B2: unobservable-baseline transcript-first confirm --------------------------
    //
    // Fresh session (zero TranscriptEntries) used to skip the confirm loop and rely on
    // WaitForSequenceAdvanceAsync. Herdr's revision is sticky, so that path false-Negatives.
    // B2 runs the confirm loop from sequence floor 0 with a wall-clock floor; screen advance is
    // only the deadline fallback. PromptSubmissionMatch is untouched.

    [Test]
    public async Task Card0164_unobservable_matching_complete_row_with_fresh_timestamp_confirms()
    {
        // (i) Headline false-negative pin — RED without B2 (NoSubmitOutput).
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.SubmitAck = ""; // sequence never advances
        const string body = "CARD0164 unobservable confirm body that is long enough";
        h.Adapter.OnSubmitted = b =>
        {
            // Fire-and-forget insert mid-confirm-window (same shape as the grace-lag pin).
#pragma warning disable CS4014
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400));
                await h.InsertTranscriptEntryAsync(
                    TranscriptKinds.UserPrompt, b, timestamp: DateTime.UtcNow);
            });
#pragma warning restore CS4014
            return Task.CompletedTask;
        };

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
        (await IncidentsOfAsync(db, h.AgentId)).ShouldBeEmpty();
    }

    [Test]
    public async Task Card0164_unobservable_no_row_and_no_advance_still_NoSubmitOutput()
    {
        // (ii) Never-weaken: sticky sequence + no row → still fails.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.SubmitAck = "";
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;

        await h.Queue.EnqueueAsync(
            h.SessionId, "CARD0164 never-weaken body long enough", MessageSendMode.WhenIdle,
            CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBe(1);
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeTrue();
    }

    [Test]
    public async Task Card0164_unobservable_old_timestamp_row_does_not_confirm()
    {
        // (ii) Resume-history / backfill shape — wall-clock floor rejects it.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.SubmitAck = "";
        const string body = "CARD0164 old-timestamp body that is long enough";
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, body,
            timestamp: DateTime.UtcNow - TimeSpan.FromMinutes(10));

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task Card0164_unobservable_null_timestamp_row_does_not_confirm()
    {
        // (ii) Null timestamp is never evidence.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.SubmitAck = "";
        const string body = "CARD0164 null-timestamp body that is long enough";
        h.Adapter.OnSubmitted = async b =>
        {
            await h.InsertTranscriptEntryAsync(TranscriptKinds.UserPrompt, b); // Timestamp null
        };

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task Card0164_unobservable_identity_without_completeness_is_Truncated()
    {
        // (iii) CARD-0024 Truncated from zero baseline.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.SubmitAck = "";
        // Body longer than MatchWindowChars so a head-only record identity-matches but is incomplete.
        var body = "CARD0164 truncation head " + new string('X', 220) + " TAIL-MARKER-MUST-BE-ABSENT";
        var clipped = body[..220]; // contains head needle, missing TAIL
        h.Adapter.OnSubmitted = _ =>
        {
#pragma warning disable CS4014
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300));
                await h.InsertTranscriptEntryAsync(
                    TranscriptKinds.UserPrompt, clipped, timestamp: DateTime.UtcNow);
            });
#pragma warning restore CS4014
            return Task.CompletedTask;
        };

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBeGreaterThanOrEqualTo(3); // parked at MaxDeliveryAttempts
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.TruncatedTerminalDelivery))
            .ShouldBeTrue();
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task Card0164_unobservable_weak_arm_rejects_old_timestamp()
    {
        // (v) Short body: old-timestamp row must not weak-confirm.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.SubmitAck = "";
        const string body = "ok"; // under MinMatchChars
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;

        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, "unrelated old",
            timestamp: DateTime.UtcNow - TimeSpan.FromHours(1));

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending, "old-timestamp row must not weak-confirm");
    }

    [Test]
    public async Task Card0164_unobservable_weak_arm_confirms_on_fresh_timestamp()
    {
        // (v) Short body: any fresh-timestamped row confirms.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.SubmitAck = "";
        const string body = "ok"; // under MinMatchChars
        h.Adapter.OnSubmitted = _ =>
        {
#pragma warning disable CS4014
            Task.Run(async () =>
            {
                await Task.Delay(200);
                await h.InsertTranscriptEntryAsync(
                    TranscriptKinds.UserPrompt, "any fresh row", timestamp: DateTime.UtcNow);
            });
#pragma warning restore CS4014
            return Task.CompletedTask;
        };

        await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    [Test]
    public async Task Card0164_unobservable_screen_advance_at_deadline_is_degraded_Delivered()
    {
        // (vi) Bind-failed no-regression: no row + sequence DOES advance → Delivered at deadline.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.OnSubmitted = _ => Task.CompletedTask; // no timestamped row
        // SubmitAck default "\n" advances sequence.

        await h.Queue.EnqueueAsync(
            h.SessionId, "CARD0164 screen-fallback body long enough", MessageSendMode.WhenIdle,
            CancellationToken.None);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Sent);
    }

    [Test]
    public async Task Card0164_unobservable_agent_status_working_does_not_confirm()
    {
        // (viii) Status prohibition — working/done is never delivery evidence.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.SubmitAck = "";
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;
        h.Runtime.SetTestAgentStatus(h.SessionId, "working");

        await h.Queue.EnqueueAsync(
            h.SessionId, "CARD0164 status-prohibition body long enough", MessageSendMode.WhenIdle,
            CancellationToken.None);

        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId))
            .Status.ShouldBe(QueuedMessageStatus.Pending);
    }

    [Test]
    public async Task Card0164_null_baseline_attempt_is_late_confirmed_without_retype()
    {
        // (iv) Double-type pin — RED without B3 (late-confirm skipped null baselines → re-types).
        await using var h = await ObservableHarnessAsync(alwaysOn: false);
        const string body = "CARD0164 null-baseline late-confirm body long enough";
        var id = await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: null);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, body, timestamp: DateTime.UtcNow);

        var inputsBefore = h.Adapter.Inputs.Count;
        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.Inputs.Count.ShouldBe(inputsBefore, "late-confirm must not write to the terminal");
        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.Id == id);
        message.Status.ShouldBe(QueuedMessageStatus.Sent);
        message.SentAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Card0164_null_baseline_old_timestamp_still_redelivers()
    {
        // (iv) Negative: old-timestamp match must not late-confirm — redelivery proceeds.
        await using var h = await ObservableHarnessAsync(alwaysOn: false);
        const string body = "CARD0164 null-baseline old-row body long enough";
        var id = await h.SeedPendingMessageAsync(body, deliveryAttempts: 1, baselineSequence: null);
        await h.InsertTranscriptEntryAsync(
            TranscriptKinds.UserPrompt, body,
            timestamp: DateTime.UtcNow - TimeSpan.FromHours(2));

        await h.Queue.OnTurnEndAsync(h.SessionId, CancellationToken.None);

        h.Adapter.SubmittedBodies.ShouldContain(body, "old row is not a floor — redelivery types");
        await using var db = CreateContext();
        (await db.SessionQueuedMessages.SingleAsync(m => m.Id == id))
            .Status.ShouldBe(QueuedMessageStatus.Sent); // confirmed by the fresh OnSubmitted path
    }

    [Test]
    public async Task Card0164_ModeNow_grace_confirms_late_record_without_409()
    {
        // (vii) Record lands during PostFailureConfirmGraceSeconds after NoSubmitOutput.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.SubmitAck = "";
        const string body = "CARD0164 ModeNow grace body that is long enough";
        h.Adapter.OnSubmitted = b =>
        {
#pragma warning disable CS4014
            // Past the 3s confirm timeout, inside the 3s grace.
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(4));
                await h.InsertTranscriptEntryAsync(
                    TranscriptKinds.UserPrompt, b, timestamp: DateTime.UtcNow);
            });
#pragma warning restore CS4014
            return Task.CompletedTask;
        };

        var dto = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.Now, CancellationToken.None);
        dto.ShouldNotBeNull();
        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeFalse("grace-confirmed Mode:Now raises no incident");
    }

    [Test]
    public async Task Card0164_ModeNow_grace_expiring_empty_still_409s()
    {
        // (vii) Grace expires empty → 409 + incident.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.SubmitAck = "";
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.EnqueueAsync(
                h.SessionId, "CARD0164 ModeNow grace empty body long enough",
                MessageSendMode.Now, CancellationToken.None));
        ex.Message.ShouldContain("submitting Enter produced no output");
        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeTrue();
    }

    [Test]
    public async Task Card0164_ModeNow_NoComposerEvidence_gets_no_grace()
    {
        // (vii) Enter withheld — grace must not wait 20s to learn nothing.
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.EchoTypedInputToScreen = false;

        var started = DateTime.UtcNow;
        var ex = await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.EnqueueAsync(
                h.SessionId, "CARD0164 ModeNow no-composer body long enough",
                MessageSendMode.Now, CancellationToken.None));
        ex.Message.ShouldContain("never appeared in the composer");
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(5),
            "NoComposerEvidence must not burn the grace window");
    }

    [Test]
    public async Task Mode_Now_response_carries_a_transcript_confirmed_receipt()
    {
        await using var h = await ObservableHarnessAsync(alwaysOn: false);
        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "CARD0180 transcript-confirmed body long enough",
            MessageSendMode.Now, CancellationToken.None);

        dto.LastDelivery.ShouldNotBeNull();
        dto.LastDelivery!.Verdict.ShouldBe("Delivered");
        dto.LastDelivery.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Transcript);
        dto.LastDelivery.Degraded.ShouldBeFalse();
        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified))
            .ShouldBeFalse();
    }

    [Test]
    public async Task Mode_Now_screen_only_fallback_returns_a_degraded_receipt_and_records_DeliveryUnverified_once_per_window()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.OnSubmitted = _ => Task.CompletedTask; // no UserPrompt row — keep the screen-only path
        const string body = "CARD0180 screen-fallback body long enough for identity";

        var first = await h.Queue.EnqueueAsync(h.SessionId, body, MessageSendMode.Now, CancellationToken.None);
        first.LastDelivery.ShouldNotBeNull();
        first.LastDelivery!.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Screen);
        first.LastDelivery.Degraded.ShouldBeTrue();
        first.LastDelivery.Reason.ShouldNotBeNull();
        first.LastDelivery.Reason.ShouldContain("no transcript bound");

        var second = await h.Queue.EnqueueAsync(
            h.SessionId, body + " again", MessageSendMode.Now, CancellationToken.None);
        second.LastDelivery.ShouldNotBeNull();
        second.LastDelivery!.ConfirmedBy.ShouldBe(DeliveryConfirmedBy.Screen);

        await using var db = CreateContext();
        var incidents = await db.AgentIncidents
            .Where(i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified)
            .ToListAsync();
        incidents.Count.ShouldBe(1, "two sends inside 10 min → one incident row");
        incidents[0].Severity.ShouldBe(AlertSeverity.Warning);
        incidents[0].SessionId.ShouldBe(h.SessionId);
    }

    [Test]
    public async Task Mode_Now_degraded_receipt_is_Critical_when_channel_bound()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.OnSubmitted = _ => Task.CompletedTask;
        await h.BindChannelAsync();

        await h.Queue.EnqueueAsync(
            h.SessionId, "CARD0180 channel-bound screen-fallback body long enough",
            MessageSendMode.Now, CancellationToken.None);

        await using var db = CreateContext();
        var incident = await db.AgentIncidents.SingleAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified);
        incident.Severity.ShouldBe(AlertSeverity.Critical);
    }

    [Test]
    public async Task Mode_Now_failure_still_throws_409_with_no_receipt()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        h.Adapter.EchoTypedInputToScreen = false;

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.EnqueueAsync(h.SessionId, "send now please", MessageSendMode.Now, CancellationToken.None));
        ex.Message.ShouldContain("never appeared in the composer");

        await using var db = CreateContext();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryUnverified))
            .ShouldBeFalse("NoComposerEvidence is not the screen-only fallback");
    }

    [Test]
    public async Task Herdr_unreachable_defers_with_zero_attempts_and_does_not_kill_always_on()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.ThrowOnSend = new ServiceUnavailableException(
            "Herdr is unreachable.", HerdrProblemTypes.Unreachable);

        var dto = await h.Queue.EnqueueAsync(
            h.SessionId, "while herdr is down", MessageSendMode.WhenIdle, CancellationToken.None);

        dto.Messages.Count.ShouldBe(1);
        dto.Messages[0].Status.ShouldBe(nameof(QueuedMessageStatus.Pending));

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBe(0, "BackendUnreachable charges no attempt");
        message.SentAt.ShouldBeNull();
        (await db.AgentIncidents.AnyAsync(
            i => i.AgentId == h.AgentId && i.Kind == AgentIncidentKind.DeliveryVerificationFailed))
            .ShouldBeFalse();
        h.Adapter.Killed.ShouldBeFalse("a herdr-unreachable session must never be auto-killed");
        h.Adapter.Inputs.ShouldBeEmpty();
    }

    [Test]
    public async Task Herdr_unreachable_pending_metadata_defers_without_typing()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Runtime.SetTestPending(h.SessionId, HerdrPendingReasons.Unreachable);

        await h.Queue.EnqueueAsync(
            h.SessionId, "pending adoption", MessageSendMode.WhenIdle, CancellationToken.None);

        await using var db = CreateContext();
        var message = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == h.SessionId);
        message.Status.ShouldBe(QueuedMessageStatus.Pending);
        message.DeliveryAttempts.ShouldBe(0);
        h.Adapter.Inputs.ShouldBeEmpty();
        h.Adapter.Killed.ShouldBeFalse();
    }

    [Test]
    public async Task Mode_Now_herdr_unreachable_returns_409_without_killing()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        h.Adapter.ThrowOnSend = new ServiceUnavailableException(
            "Herdr is unreachable.", HerdrProblemTypes.Unreachable);

        var ex = await Should.ThrowAsync<ConflictException>(() =>
            h.Queue.EnqueueAsync(
                h.SessionId, "send now while herdr is down", MessageSendMode.Now, CancellationToken.None));
        ex.Message.ShouldContain("herdr is unreachable");
        h.Adapter.Killed.ShouldBeFalse();
    }

    private static async Task SetKindAsync(Guid sessionId, AgentKind kind)
    {
        await using var db = CreateContext();
        await db.AgentSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.AgentKind, kind));
    }
}
