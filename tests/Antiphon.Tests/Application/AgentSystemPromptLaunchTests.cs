using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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
    public async Task Agent_without_preamble_launches_with_unchanged_args_and_no_notes()
    {
        await using var h = await CreateHarnessAsync(alwaysOn: true);
        await EndSessionAsync(h, SessionStatus.Failed);

        var started = await StartAsync(h, fresh: true);

        var adapter = Factory(h).Created.ShouldHaveSingleItem();
        adapter.StartedArgs.ShouldNotContain("--append-system-prompt");
        adapter.SubmittedBodies.ShouldBeEmpty();
        await using var db = CreateContext();
        (await db.SessionQueuedMessages
                .AnyAsync(m => m.AgentSessionId == Guid.Parse(started.PersistentSessionId!)))
            .ShouldBeFalse();
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

        h.Dispatcher.PendingCount(newSessionId).ShouldBe(0, "launch notes never track a reply correlation");
        await h.InsertTurnAsync(ChannelPreamble.BootstrapBody, "READY", newSessionId);
        await h.Dispatcher.OnTurnEndAsync(newSessionId, CancellationToken.None);
        h.Messaging.SentReplies.ShouldBeEmpty();
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

    private static async Task EndSessionAsync(BridgeQueueHarness h, SessionStatus status)
    {
        await using var db = CreateContext();
        await db.AgentSessions.Where(s => s.Id == h.SessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, status));
    }

    private static async Task<AgentDetailDto> StartAsync(BridgeQueueHarness h, bool fresh)
    {
        // Fresh scope per start: the harness scope's DbContext tracks stale agent state.
        using var scope = h.Provider.CreateScope();
        var control = scope.ServiceProvider.GetRequiredService<AgentControlService>();
        await control.StartAsync(
            h.AgentId, new StartAgentRequest(RemoteControl: false, Fresh: fresh), CancellationToken.None);
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
            if (ConfigureNext.Count > 0)
                ConfigureNext.Dequeue()(adapter);
            Created.Add(adapter);
            return adapter;
        }
    }
}
