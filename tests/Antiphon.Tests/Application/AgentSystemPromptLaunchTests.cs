using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// PR 5 of the Telegram bot agents plan: <c>Agent.SystemPromptAppend</c> flows into
/// <c>--append-system-prompt</c> on every interactive ClaudeCode launch (fresh, resume, and the
/// resume→fresh fallback), and the launch notes (bootstrap / restart) are delivered through the
/// verified queue path with the right body per branch. Launches run through the REAL
/// AgentControlService → AgentSessionLaunchQueue → AgentSessionService chain; the adapter factory
/// hands out FakeAgentProtocolAdapters that self-register in the runtime so note delivery reaches
/// a live composer.
/// </summary>
[Category("Integration")]
[NotInParallel("MessageQueue")]
public class AgentSystemPromptLaunchTests
{
    private const string Template = "You are {agentName}. Channels: {channels}.";
    private const string RenderedForHarnessAgent = "You are BridgeQueue. Channels: none yet.";

    [Test]
    public async Task Start_with_system_prompt_append_passes_flag_on_fresh_launch()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Failed);

        var started = await StartAsync(h, fresh: true);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        var args = adapter.StartedArgs;
        var flagIndex = args.ToList().IndexOf("--append-system-prompt");
        flagIndex.ShouldBeGreaterThanOrEqualTo(0, $"launch args must carry the flag; args were [{string.Join(", ", args)}]");
        args[flagIndex + 1].ShouldBe(RenderedForHarnessAgent);
        args.ShouldContain("--session-id");

        // The bootstrap is delivered exactly once, verified, and leaves no pending rows.
        adapter.SubmittedBodies.ShouldBe([ChannelPreamble.BootstrapBody]);
        await using var db = CreateContext();
        var newSessionId = Guid.Parse(started.PersistentSessionId!);
        (await db.SessionQueuedMessages.Where(m => m.AgentSessionId == newSessionId).ToListAsync())
            .ShouldAllBe(m => m.Status == QueuedMessageStatus.Sent);
        (await db.AgentIncidents.AnyAsync(i => i.AgentId == h.AgentId)).ShouldBeFalse();
    }

    [Test]
    public async Task Second_start_on_a_live_session_is_a_no_op_and_does_not_rebootstrap()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Failed);

        await StartAsync(h, fresh: true);
        Factory(h).Created.Count.ShouldBe(1);

        await StartAsync(h, fresh: true); // live session — idempotent no-op

        Factory(h).Created.Count.ShouldBe(1, "no second process, no second bootstrap");
        Factory(h).Created[0].SubmittedBodies.ShouldBe([ChannelPreamble.BootstrapBody]);
    }

    [Test]
    public async Task Resume_launch_also_carries_append_system_prompt_and_delivers_restart_note()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Stopped);
        // The relaunch's adapter re-registers under the SAME session id; drop the harness's
        // pre-registered one first (in prod nothing is registered for an ended session).
        await h.Runtime.DisposeSessionAsync(h.SessionId);

        var started = await StartAsync(h, fresh: false);

        Guid.Parse(started.PersistentSessionId!).ShouldBe(h.SessionId, "a resumable session keeps its id");
        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        adapter.StartedArgs.ShouldContain("--resume");
        adapter.StartedArgs.ShouldContain("--append-system-prompt");
        adapter.SubmittedBodies.ShouldBe([ChannelPreamble.RestartResumeBody],
            "a successful resume gets the restart note, NOT the bootstrap");
    }

    [Test]
    public async Task Agent_without_preamble_launches_without_preamble_or_notes_but_is_still_named()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await EndSessionAsync(h, SessionStatus.Failed);

        var started = await StartAsync(h, fresh: true);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        adapter.StartedArgs.ShouldNotContain("--append-system-prompt");
        // Every ClaudeCode session is still named by the agent, preamble or not.
        AssertNamed(adapter.StartedArgs, "BridgeQueue");
        adapter.SubmittedBodies.ShouldBeEmpty();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages
                .AnyAsync(m => m.AgentSessionId == Guid.Parse(started.PersistentSessionId!)))
            .ShouldBeFalse();
    }

    // CARD-0283: Details is standing-job metadata (CLAUDE.md "## Your job"), not a first prompt.
    // The gym-stat-weightsteps incident stuffed a task into Details, started, and got a healthy
    // Running session with an empty transcript. Empty-shell start is the designed contract.
    [Test]
    public async Task Cardless_start_does_not_deliver_Details_as_a_prompt()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Details, "Plan CARD-0027 on the Gym Stat board"));
        }
        await EndSessionAsync(h, SessionStatus.Failed);

        var started = await StartAsync(h, fresh: true);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        adapter.SubmittedBodies.ShouldBeEmpty();
        await using var verify = CreateContext();
        (await verify.SessionQueuedMessages
                .AnyAsync(m => m.AgentSessionId == Guid.Parse(started.PersistentSessionId!)))
            .ShouldBeFalse();
    }

    [Test]
    public async Task Cardless_start_with_Prompt_delivers_it_and_not_Details()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        const string details = "Standing job written into CLAUDE.md";
        const string prompt = "Do this task now: plan CARD-0027";
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Details, details));
        }
        await EndSessionAsync(h, SessionStatus.Failed);

        var started = await StartAsync(h, fresh: true, prompt: prompt);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        adapter.SubmittedBodies.ShouldBe([prompt]);
        adapter.SubmittedBodies.ShouldNotContain(details);
        await using var verify = CreateContext();
        var queued = await verify.SessionQueuedMessages
            .SingleAsync(m => m.AgentSessionId == Guid.Parse(started.PersistentSessionId!));
        queued.Body.ShouldBe(prompt);
        queued.Status.ShouldBe(QueuedMessageStatus.Sent);
        queued.Origin.ShouldBe(QueuedMessageOrigin.Ui);
    }

    [Test]
    public async Task Interactive_launch_names_the_session_by_agent_name()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Failed);

        await StartAsync(h, fresh: true);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        AssertNamed(adapter.StartedArgs, "BridgeQueue");
    }

    private static void AssertNamed(IReadOnlyList<string> args, string expectedName)
    {
        var i = args.ToList().IndexOf("--name");
        i.ShouldBeGreaterThanOrEqualTo(0, $"launch args must carry --name; args were [{string.Join(", ", args)}]");
        args[i + 1].ShouldBe(expectedName);
    }

    // The generic model LEVEL rides every ClaudeCode launch mapped to the family alias
    // (Frontier→fable, High→opus, Medium→sonnet, Low→haiku), never a full versioned model id —
    // launches always pick up the family's current model.
    [Test]
    public async Task Interactive_launch_passes_the_default_model_level_as_opus()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await EndSessionAsync(h, SessionStatus.Failed);

        await StartAsync(h, fresh: true);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        AssertModel(adapter.StartedArgs, "opus");   // High (the Opus tier) is the default level
    }

    [Test]
    public async Task Interactive_launch_maps_frontier_level_to_fable()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.ModelLevel, AgentModelLevel.Frontier));
        }
        await EndSessionAsync(h, SessionStatus.Failed);

        await StartAsync(h, fresh: true);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        AssertModel(adapter.StartedArgs, "fable");
    }

    private static void AssertModel(IReadOnlyList<string> args, string expectedAlias)
    {
        var i = args.ToList().IndexOf("--model");
        i.ShouldBeGreaterThanOrEqualTo(0, $"launch args must carry --model; args were [{string.Join(", ", args)}]");
        args[i + 1].ShouldBe(expectedAlias);
    }

    [Test]
    public async Task Resume_not_found_fallback_delivers_fresh_bootstrap()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Stopped);
        await h.Runtime.DisposeSessionAsync(h.SessionId);

        // First adapter (the --resume attempt) dies with Claude's session-not-found message; the
        // fallback relaunch (same session row, fresh conversation) must BOOTSTRAP, not restart-note.
        Factory(h).ConfigureNext.Enqueue(a =>
            a.ThrowOnStart = new InvalidOperationException(
                "No conversation found with session ID: " + h.SessionId));

        await StartAsync(h, fresh: false);

        var factory = Factory(h);
        factory.Created.Count.ShouldBe(2, "resume attempt + fresh fallback");
        factory.Created[1].StartedArgs.ShouldContain("--session-id");
        factory.Created[1].StartedArgs.ShouldNotContain("--resume");
        factory.Created[1].SubmittedBodies.ShouldBe([ChannelPreamble.BootstrapBody]);
    }

    [Test]
    public async Task Fallback_with_stale_mid_turn_transcript_still_delivers_bootstrap()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        // The reused session row carries activity after its last TurnEnd — IsWorkingAsync reads
        // true, which would strand a WhenIdle note forever. This pins the Now-mode rationale.
        await h.MarkWorkingAsync();
        await EndSessionAsync(h, SessionStatus.Stopped);
        await h.Runtime.DisposeSessionAsync(h.SessionId);
        Factory(h).ConfigureNext.Enqueue(a =>
            a.ThrowOnStart = new InvalidOperationException(
                "No conversation found with session ID: " + h.SessionId));

        await StartAsync(h, fresh: false);

        Factory(h).Created[1].SubmittedBodies.ShouldBe([ChannelPreamble.BootstrapBody]);
    }

    // CARD-0233 S4: a Mode.Now launch note typed into a composer that was already answering a
    // Telegram message landed as QueuedUserPrompt inside that turn and stole its identity. When
    // a Channel-origin row is still owed, the note is WhenIdle / Origin=System and waits for
    // that turn's TurnEnd. The clean-start Now-mode case above is unchanged.
    [Test]
    public async Task Launch_note_yields_to_an_owed_channel_row()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        var chatId = await h.BindChannelAsync();
        const string channelBody = "[Telegram \"AZ Care\" — Mike 21:03] Give me message to Phil";
        await using (var db = CreateContext())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Body = channelBody,
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.Channel,
                ConversationKey = $"telegram:{chatId}",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        await EndSessionAsync(h, SessionStatus.Stopped);
        await h.Runtime.DisposeSessionAsync(h.SessionId);

        Factory(h).ConfigureNext.Enqueue(a =>
        {
            a.OnSubmitted = async submitted =>
            {
                if (a.StartedSessionId is not Guid sessionId)
                    return;
                await BridgeQueueHarness.InsertEntryAsync(
                    sessionId, TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow);
            };
        });

        await StartAsync(h, fresh: false);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        adapter.SubmittedBodies.ShouldBe([channelBody],
            "the launch note must not type until the owed channel turn ends");

        await using (var db = CreateContext())
        {
            var note = await db.SessionQueuedMessages.SingleAsync(m =>
                m.AgentSessionId == h.SessionId && m.Origin == QueuedMessageOrigin.System);
            note.Body.ShouldBe(ChannelPreamble.RestartResumeBody);
            note.Status.ShouldBe(QueuedMessageStatus.Pending);
        }

        await h.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn");
        await h.Queue.FlushIfIdleAsync(h.SessionId, CancellationToken.None);

        adapter.SubmittedBodies.ShouldBe([channelBody, ChannelPreamble.RestartResumeBody]);
        await using (var db = CreateContext())
        {
            var note = await db.SessionQueuedMessages.SingleAsync(m =>
                m.AgentSessionId == h.SessionId && m.Origin == QueuedMessageOrigin.System);
            note.Status.ShouldBe(QueuedMessageStatus.Sent);
        }
    }

    [Test]
    public async Task Note_delivery_failure_falls_back_to_queue_and_does_not_fail_launch()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: false);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Failed);
        // Wedged composer from the first keystroke: Now-mode verification fails.
        Factory(h).ConfigureNext.Enqueue(a => a.EchoTypedInputToScreen = false);

        var started = await StartAsync(h, fresh: true);

        var newSessionId = Guid.Parse(started.PersistentSessionId!);
        await using var db = CreateContext();
        (await db.AgentSessions.SingleAsync(s => s.Id == newSessionId)).Status
            .ShouldBe(SessionStatus.Running, "a note-delivery failure must never fail the launch");
        var note = await db.SessionQueuedMessages.SingleAsync(m => m.AgentSessionId == newSessionId);
        note.Body.ShouldBe(ChannelPreamble.BootstrapBody);
        note.Status.ShouldBe(QueuedMessageStatus.Pending, "failed Now delivery falls back to a queued note");
    }

    [Test]
    public async Task Bootstrap_produces_no_channel_reply()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Failed);

        var started = await StartAsync(h, fresh: true);
        var newSessionId = Guid.Parse(started.PersistentSessionId!);

        (await h.Dispatcher.PendingCountAsync(newSessionId)).ShouldBe(0, "launch notes never track a reply correlation");
        await h.InsertTurnAsync(ChannelPreamble.BootstrapBody, "READY", newSessionId);
        await h.Dispatcher.OnTurnEndAsync(newSessionId, CancellationToken.None);
        h.Messaging.SentReplies.ShouldBeEmpty();
    }

    [Test]
    public async Task The_standing_check_interpreter_gets_no_bootstrap_note()
    {
        // The notes gate is "has a SystemPromptAppend", which meant "has a channel preamble" when it
        // was written. CARD-0047's specialist uses the same field for a standing contract, so it was
        // handed "Follow your CLAUDE.md session-start ritual" — with no CLAUDE.md in its scratch
        // directory and a deny-all PreToolUse hook that refuses every read it would need.
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Failed);
        var slug = CheckInterpreterProvisioner.Slug(
            h.Provider.GetRequiredService<IOptions<DelegationSettings>>().Value);
        await using (var db = CreateContext())
        {
            await db.Agents.Where(a => a.Id == h.AgentId)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.Slug, slug));
        }

        var started = await StartAsync(h, fresh: true);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        adapter.SubmittedBodies.ShouldBeEmpty("an agent with no tools cannot perform a workspace ritual");
        // The contract itself must still ride the launch — suppressing the note must not suppress
        // the thing that makes the specialist a specialist.
        adapter.StartedArgs.ShouldContain("--append-system-prompt");

        await using var verify = CreateContext();
        var newSessionId = Guid.Parse(started.PersistentSessionId!);
        (await verify.SessionQueuedMessages.Where(m => m.AgentSessionId == newSessionId).ToListAsync())
            .ShouldBeEmpty("nothing queued either — the note is not deferred, it is not sent at all");
    }

    // ---------- reply style (CARD-0060) ----------

    [Test]
    public async Task A_normal_style_agent_launches_with_exactly_the_arguments_it_did_before()
    {
        // The migration's whole claim, measured on the real launch path rather than on the composer:
        // ReplyStyle defaults to Normal, Normal resolves to no bundle, and the append is the rendered
        // preamble with nothing before it. Sibling of Start_with_system_prompt_append_passes_flag_on_
        // fresh_launch, kept separate because THIS is the one that must go red if Normal ever composes.
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Failed);
        await using (var db = CreateContext())
        {
            (await db.Agents.SingleAsync(a => a.Id == h.AgentId)).ReplyStyle
                .ShouldBe(AgentReplyStyle.Normal, "the default a created agent gets");
        }

        await StartAsync(h, fresh: true);

        var args = Factory(h).Created.ShouldHaveSingleItem().StartedArgs;
        args[args.ToList().IndexOf("--append-system-prompt") + 1].ShouldBe(
            RenderedForHarnessAgent, "byte for byte — no header, no separator, no style block");
    }

    [Test]
    public async Task A_styled_agent_carries_its_style_block_before_its_own_contract()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await SetStyleAsync(h, AgentReplyStyle.Caveman);
        await EndSessionAsync(h, SessionStatus.Failed);

        await StartAsync(h, fresh: true);

        var args = Factory(h).Created.ShouldHaveSingleItem().StartedArgs;
        var append = args[args.ToList().IndexOf("--append-system-prompt") + 1];
        append.ShouldStartWith("[bundle:style-caveman v");
        append.ShouldContain(AgentReplyStyles.CorrectnessSentence);
        append.ShouldEndWith(
            RenderedForHarnessAgent,
            customMessage: "the agent's own contract keeps the last word over a style from a dropdown");
        // Substitution runs over the WHOLE append, so the placeholders must still have resolved —
        // and the style block must not have been mangled by it.
        append.ShouldNotContain("{agentName}");
    }

    [Test]
    public async Task A_style_alone_produces_the_flag_but_still_no_launch_notes()
    {
        // Adding a style must not start handing a workspace ritual to agents that never had one: the
        // notes gate stays keyed on SystemPromptAppend, not on "composes something".
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetStyleAsync(h, AgentReplyStyle.Terse);
        await EndSessionAsync(h, SessionStatus.Failed);

        var started = await StartAsync(h, fresh: true);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        var args = adapter.StartedArgs;
        var flagIndex = args.ToList().IndexOf("--append-system-prompt");
        flagIndex.ShouldBeGreaterThanOrEqualTo(0);
        args[flagIndex + 1].ShouldBe(InstructionBundles.Get("style-terse").Render());
        adapter.SubmittedBodies.ShouldBeEmpty("no preamble, so no bootstrap — style is not a preamble");
        await using var db = CreateContext();
        (await db.SessionQueuedMessages
                .AnyAsync(m => m.AgentSessionId == Guid.Parse(started.PersistentSessionId!)))
            .ShouldBeFalse();
    }

    private static async Task SetStyleAsync(BridgeQueueHarness h, AgentReplyStyle style)
    {
        await using var db = CreateContext();
        await db.Agents.Where(a => a.Id == h.AgentId)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.ReplyStyle, style));
    }

    // ---------- per-agent attachments and the drift badge (CARD-0058 slice 6) ----------

    [Test]
    public async Task An_attached_bundle_rides_the_launch_of_a_standing_agent_that_has_no_role()
    {
        // The card this slice closes, on the path that proves it: a standing agent belongs to no
        // delegate role, so before attachments there was NO way for it to carry board-api short of
        // pasting the text into its own system prompt by hand.
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await AttachAsync(h, InstructionBundles.BoardApi);
        await EndSessionAsync(h, SessionStatus.Failed);

        var started = await StartAsync(h, fresh: true);

        var args = Factory(h).Created.ShouldHaveSingleItem().StartedArgs;
        var append = args[args.ToList().IndexOf("--append-system-prompt") + 1];
        var boardApi = InstructionBundles.Get(InstructionBundles.BoardApi);
        append.ShouldStartWith($"[bundle:board-api v{boardApi.Version}]");
        append.ShouldContain(boardApi.Text);
        append.ShouldEndWith(RenderedForHarnessAgent, customMessage:
            "the agent's own contract still keeps the last word over an attached bundle");
        started.ComposedBundles.ShouldBe([boardApi.Stamp]);

        // What the launch recorded — stamps only. This is the ONLY composed state stored anywhere,
        // and it is what the drift comparison matches against.
        await using var db = CreateContext();
        var session = await db.AgentSessions.AsNoTracking()
            .SingleAsync(s => s.Id == Guid.Parse(started.PersistentSessionId!));
        session.ComposedBundleStamp.ShouldBe(boardApi.Stamp);
        session.ComposedBundleStamp.ShouldNotContain(boardApi.Text);
        started.BundlesOutOfDate.ShouldBeFalse("it launched with exactly what the repo says now");
        started.AttachedBundleKeys.ShouldBe([InstructionBundles.BoardApi]);
    }

    [Test]
    public async Task Attaching_a_bundle_to_a_running_agent_raises_the_drift_badge_and_touches_nothing_else()
    {
        // The constraint that outranks the feature: reconciliation is recompute-AT-LAUNCH. Attaching
        // a bundle to a live session must change the badge and NOTHING about the session — no second
        // launch, no text typed into a composer that is already running under the old instructions.
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Failed);
        var started = await StartAsync(h, fresh: true);
        started.BundlesOutOfDate.ShouldBeFalse();
        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        var submittedBefore = adapter.SubmittedBodies.Count;

        await AttachAsync(h, InstructionBundles.BoardApi);

        using var scope = h.Provider.CreateScope();
        var detail = await scope.ServiceProvider.GetRequiredService<AgentService>()
            .GetByIdAsync(h.AgentId, CancellationToken.None);
        detail.BundlesOutOfDate.ShouldBeTrue("it is running with instructions the repo has moved past");
        detail.ComposedBundles.ShouldBe([InstructionBundles.Get(InstructionBundles.BoardApi).Stamp],
            "and the list already shows what the NEXT launch will carry");
        Factory(h).Created.Count.ShouldBe(1, "no relaunch — a badge is not a trigger");
        adapter.SubmittedBodies.Count.ShouldBe(
            submittedBefore, "and nothing was typed into the live session");
    }

    [Test]
    public async Task The_badge_clears_at_the_next_launch_because_a_launch_is_the_reconcile_point()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Failed);
        var started = await StartAsync(h, fresh: true);
        await AttachAsync(h, InstructionBundles.BoardApi);

        // End the session the agent is ACTUALLY on (the one the fresh start just made), not the
        // harness's original — a still-live session makes the next start an idempotent no-op.
        var live = Guid.Parse(started.PersistentSessionId!);
        await EndSessionAsync(h, live, SessionStatus.Stopped);
        await h.Runtime.DisposeSessionAsync(live);

        // A RESUME, deliberately: launch args are rebuilt per invocation, so a resumed process picks
        // up the new bundle too — and the stamp has to be rewritten or the badge would never clear.
        var resumed = await StartAsync(h, fresh: false);

        Guid.Parse(resumed.PersistentSessionId!).ShouldBe(live, "a resumable session keeps its id");

        resumed.BundlesOutOfDate.ShouldBeFalse();
        var args = Factory(h).Created[^1].StartedArgs.ToList();
        args[args.IndexOf("--append-system-prompt") + 1].ShouldStartWith("[bundle:board-api v");
    }

    [Test]
    public async Task An_agent_with_no_attachments_and_no_style_records_an_empty_stamp_not_a_null_one()
    {
        // "" and null are different answers and the difference is load-bearing: "" says this launch
        // carried nothing, which attaching a first bundle then contradicts. Were it null, the badge
        // could never appear for the overwhelmingly common agent.
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await SetPreambleAsync(h, Template);
        await EndSessionAsync(h, SessionStatus.Failed);

        var started = await StartAsync(h, fresh: true);

        await using var db = CreateContext();
        (await db.AgentSessions.AsNoTracking()
                .SingleAsync(s => s.Id == Guid.Parse(started.PersistentSessionId!)))
            .ComposedBundleStamp.ShouldBe(string.Empty);
        started.BundlesOutOfDate.ShouldBeFalse();
    }

    private static async Task AttachAsync(BridgeQueueHarness h, params string[] keys)
    {
        using var scope = h.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
        await AgentBundleAttachments.SetAsync(db, agent, keys, DateTime.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();
    }

    // ---------- harness ----------

    private static AppDbContext CreateContext() => BridgeQueueHarness.CreateContext();

    private static RegisteringAdapterFactory Factory(BridgeQueueHarness h) =>
        (RegisteringAdapterFactory)h.Provider.GetRequiredService<IAgentProtocolAdapterFactory>();

    private static Task<BridgeQueueHarness> CreateHarnessAsync(bool alwaysOn) =>
        BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
        {
            AlwaysOn = alwaysOn,
            ConfigureServices = services =>
            {
                // ClaudeCode-kind definition (cmd.exe stays the spawn-check target) so the
                // preamble/notes gate opens; a factory that hands out self-registering fakes so
                // the real launch chain runs end-to-end without processes.
                services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(
                    new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new AgentRegistrySettings
                    {
                        DefaultDefinition = "fake",
                        Definitions =
                        {
                            ["fake"] = new AgentDefinition
                            {
                                Kind = "ClaudeCode",
                                Exe = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                            },
                        },
                    }));
                services.AddSingleton<IAgentProtocolAdapterFactory>(sp =>
                    new RegisteringAdapterFactory(sp.GetRequiredService<AgentSessionRuntime>()));
            },
        });

    private static async Task SetPreambleAsync(BridgeQueueHarness h, string template)
    {
        await using var db = CreateContext();
        await db.Agents.Where(a => a.Id == h.AgentId)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.SystemPromptAppend, template));
    }

    private static Task EndSessionAsync(BridgeQueueHarness h, SessionStatus status) =>
        EndSessionAsync(h, h.SessionId, status);

    private static async Task EndSessionAsync(BridgeQueueHarness h, Guid sessionId, SessionStatus status)
    {
        _ = h;
        await using var db = CreateContext();
        await db.AgentSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, status));
    }

    private static async Task<AgentDetailDto> StartAsync(
        BridgeQueueHarness h, bool fresh, string? prompt = null)
    {
        // Fresh scope per start: the harness scope's DbContext tracks stale agent state.
        using var scope = h.Provider.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
        await control.StartAsync(
            h.AgentId,
            new StartAgentRequest(RemoteControl: false, Fresh: fresh, Prompt: prompt),
            CancellationToken.None);
        await h.Provider.GetRequiredService<AgentSessionLaunchQueue>()
            .WaitForIdleAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        // Re-read: the background launch may have updated the session/agent after StartAsync returned.
        var refresh = scope.ServiceProvider.GetRequiredService<AgentService>();
        return await refresh.GetByIdAsync(h.AgentId, CancellationToken.None);
    }

    private sealed class RegisteringAdapterFactory(AgentSessionRuntime runtime) : IAgentProtocolAdapterFactory
    {
        public List<FakeAgentProtocolAdapter> Created { get; } = [];

        /// <summary>Applied to the next created adapter (one action per adapter, FIFO).</summary>
        public Queue<Action<FakeAgentProtocolAdapter>> ConfigureNext { get; } = new();

        public IAgentProtocolAdapter Create(AgentKind kind)
        {
            var adapter = new FakeAgentProtocolAdapter { RegisterOnStart = runtime };
            // Model the whole round trip, as the harness does for its own adapter: a real Claude
            // records the submitted bootstrap in its JSONL (stamped) and the tailer turns it into
            // the UserPrompt row the delivery is confirmed against. Without it the bootstrap's
            // delivery has no record, waits out the confirm deadline, and takes the screen-only
            // fallback — which CARD-0180 S3 records as a DeliveryUnverified incident, failing
            // every "leaves no incident" assertion in this suite (CARD-0201).
            adapter.OnSubmitted = async submitted =>
            {
                if (adapter.StartedSessionId is not Guid sessionId)
                    return;
                await BridgeQueueHarness.InsertEntryAsync(
                    sessionId, TranscriptKinds.UserPrompt, submitted, timestamp: DateTime.UtcNow);
                await BridgeQueueHarness.InsertEntryAsync(sessionId, TranscriptKinds.TurnEnd, stopReason: "end_turn");
            };
            if (ConfigureNext.Count > 0)
                ConfigureNext.Dequeue()(adapter);
            Created.Add(adapter);
            return adapter;
        }
    }
}
