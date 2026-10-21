using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
public sealed class AgentSessionCancelledTurnTests
{
    [Test]
    public async Task C1322_Interrupted_result_preserves_live_session()
    {
        // Independent controls run before the regression witness, including on the red commit.
        await CheckStartAsync(completed: true, stopReason: null, RunPhase.Succeeded);
        await CheckStartAsync(completed: false, stopReason: null, RunPhase.TimedOut);
        await CheckStartAsync(completed: false, TranscriptKinds.StopReasons.Cancelled, RunPhase.AwaitingInput);
    }

    [Test]
    public async Task C1322_Interrupted_result_survives_stall_scans()
    {
        await CheckStartAsync(completed: false, TranscriptKinds.StopReasons.Cancelled,
            RunPhase.AwaitingInput, scanAfterInterruption: true);
    }

    [Test]
    public async Task C1382_Echo_before_wrapper_does_not_stick_streaming_turn()
    {
        await using var world = await EchoWorld.OpenAsync();

        var echoFirst = await world.StartCancelledAsync();
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(echoFirst, "/status");
        await world.DrainAsync(echoFirst);
        await world.PersistAsync(echoFirst, "<command-name>/status</command-name>");
        await world.DrainAsync(echoFirst);
        var afterEcho = await world.ReloadAsync(echoFirst);
        afterEcho.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        afterEcho.Attempt.LastEventAt.ShouldBe(echoFirst.InputWaitAt);
        afterEcho.Attempt.PhaseStartedAt.ShouldBe(echoFirst.PhaseStartedAt);
        afterEcho.Attempt.PhaseDurationsJson.ShouldBe(echoFirst.Durations);
        afterEcho.Session.Status.ShouldBe(SessionStatus.Running);
        afterEcho.Session.EndedAt.ShouldBeNull();
        echoFirst.Adapter.KillCount.ShouldBe(0);

        var wrapperFirst = await world.StartCancelledAsync();
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(wrapperFirst, "<command-name>/status</command-name>");
        await world.DrainAsync(wrapperFirst);
        await world.PersistAsync(wrapperFirst, "/status");
        await world.DrainAsync(wrapperFirst);
        var afterWrapper = await world.ReloadAsync(wrapperFirst);
        afterWrapper.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        afterWrapper.Attempt.LastEventAt.ShouldBe(wrapperFirst.InputWaitAt);
        afterWrapper.Attempt.PhaseStartedAt.ShouldBe(wrapperFirst.PhaseStartedAt);
        wrapperFirst.Adapter.KillCount.ShouldBe(0);
        afterWrapper.Session.Status.ShouldBe(SessionStatus.Running);

        world.Clock.Advance(TimeSpan.FromMinutes(10));
        (await world.ScanAsync()).ShouldBe(0);
        echoFirst.Adapter.KillCount.ShouldBe(0);
        echoFirst.Adapter.Disposed.ShouldBeFalse();
        wrapperFirst.Adapter.KillCount.ShouldBe(0);
        (await world.ReloadAsync(echoFirst)).Session.EndedAt.ShouldBeNull();
        (await world.ReloadAsync(wrapperFirst)).Session.EndedAt.ShouldBeNull();

        var followUpAt = world.Clock.GetUtcNow().UtcDateTime;
        await world.PersistAsync(echoFirst, "Continue after interruption");
        await world.DrainAsync(echoFirst);
        var armed = await world.ReloadAsync(echoFirst);
        var armedAt = armed.Attempt.LastEventAt;
        await world.DrainAsync(echoFirst);
        var replayed = await world.ReloadAsync(echoFirst);
        replayed.Attempt.LastEventAt.ShouldBe(armedAt);
        (await world.ScanAsync()).ShouldBe(0);
        world.Clock.Advance(TimeSpan.FromMinutes(10));
        var stalled = await world.ScanAsync();
        echoFirst.Adapter.KillCount.ShouldBe(1);
        stalled.ShouldBe(1);
        echoFirst.Adapter.Disposed.ShouldBeTrue();
        armed.Attempt.Phase.ShouldBe(RunPhase.StreamingTurn);
        armedAt.ShouldBe(followUpAt);
        replayed.Attempt.PhaseDurationsJson.ShouldContain("\"AwaitingInput\"");
        wrapperFirst.Adapter.KillCount.ShouldBe(0);

        var explain = await world.StartCancelledAsync();
        var started = DateTime.SpecifyKind(explain.PhaseStartedAt, DateTimeKind.Utc);
        world.Clock.Advance(started.AddMinutes(-2) - world.Clock.GetUtcNow().UtcDateTime);
        await world.PersistAsync(explain, "<command-name>/status</command-name>");
        var explainAt = started.AddMinutes(1);
        world.Clock.Advance(explainAt - world.Clock.GetUtcNow().UtcDateTime);
        await world.PersistAsync(explain, "/status explain the remaining work");
        await world.DrainAsync(explain);
        var explained = await world.ReloadAsync(explain);
        explained.Attempt.Phase.ShouldBe(RunPhase.StreamingTurn);
        explained.Attempt.PhaseStartedAt.ShouldBe(explain.PhaseStartedAt);
        explained.Attempt.LastEventAt.ShouldBe(explainAt);
        explained.Attempt.PhaseDurationsJson.ShouldNotContain("\"AwaitingInput\"");
        world.Clock.Advance(TimeSpan.FromMinutes(10));
        (await world.ScanAsync()).ShouldBe(1);
        explain.Adapter.KillCount.ShouldBe(1);
        echoFirst.Adapter.KillCount.ShouldBe(1);
        wrapperFirst.Adapter.KillCount.ShouldBe(0);
    }

    [Test]
    public async Task C1382_Rearm_completed_follow_up_survives_idle()
    {
        await using var world = await EchoWorld.OpenAsync();
        var session = await world.StartCancelledAsync();
        await ArmRealPromptAsync(world, session);

        world.Clock.Advance(TimeSpan.FromSeconds(1));
        await world.PersistAsync(session, "The follow-up is complete.", TranscriptKinds.AssistantText);
        await world.DrainAsync(session);
        (await world.ReloadAsync(session)).Attempt.Phase.ShouldBe(RunPhase.StreamingTurn);

        await world.PersistAsync(session, null, TranscriptKinds.TurnEnd, TranscriptKinds.StopReasons.EndTurn);
        await world.DrainAsync(session);
        var ended = await world.ReloadAsync(session);
        ended.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        ended.Attempt.CompletedAt.ShouldBeNull();
        ended.Session.Status.ShouldBe(SessionStatus.Running);

        await AssertUnscannedAsync(world, session);
    }

    [Test]
    public async Task C1382_Rearm_second_interrupt_returns_to_awaiting_input()
    {
        await using var world = await EchoWorld.OpenAsync();
        var cancelled = await world.StartCancelledAsync();
        await ArmRealPromptAsync(world, cancelled);
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(cancelled, null, TranscriptKinds.TurnEnd, TranscriptKinds.StopReasons.Cancelled);
        await world.DrainAsync(cancelled);
        (await world.ReloadAsync(cancelled)).Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);

        var marker = await world.StartCancelledAsync();
        await ArmRealPromptAsync(world, marker);
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(marker, "[Request interrupted by user]");
        await world.DrainAsync(marker);
        (await world.ReloadAsync(marker)).Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);

        await AssertUnscannedAsync(world, cancelled, marker);
    }

    [Test]
    public async Task C1382_Rearm_echo_after_second_interrupt_restores_input_wait()
    {
        await using var world = await EchoWorld.OpenAsync();
        var session = await world.StartCancelledAsync();
        await ArmRealPromptAsync(world, session);
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(session, null, TranscriptKinds.TurnEnd, TranscriptKinds.StopReasons.Cancelled);
        await world.DrainAsync(session);
        var waiting = await world.ReloadAsync(session);
        waiting.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        var waitAt = waiting.Attempt.PhaseStartedAt;
        var durations = waiting.Attempt.PhaseDurationsJson;

        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(session, "/status");
        await world.DrainAsync(session);
        var armed = await world.ReloadAsync(session);
        armed.Attempt.Phase.ShouldBe(RunPhase.StreamingTurn);
        armed.Attempt.PhaseStartedAt.ShouldBe(waitAt);
        armed.Attempt.PhaseDurationsJson.ShouldContain("\"AwaitingInput\"");

        await world.PersistAsync(session, "<command-name>/status</command-name>");
        await world.DrainAsync(session);
        var restored = await world.ReloadAsync(session);
        restored.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        restored.Attempt.PhaseStartedAt.ShouldBe(waitAt);
        restored.Attempt.LastEventAt.ShouldBe(waitAt);
        restored.Attempt.PhaseDurationsJson.ShouldBe(durations);
        restored.Session.Status.ShouldBe(SessionStatus.Running);

        await AssertUnscannedAsync(world, session);
    }

    [Test]
    public async Task C1382_Rearm_saved_turn_end_before_processing_survives_idle()
    {
        await using var world = await EchoWorld.OpenAsync();
        var session = await world.StartCancelledAsync();
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(session, "Continue after interruption");
        world.Clock.Advance(TimeSpan.FromSeconds(2));
        await world.PersistAsync(session, "The follow-up is complete.", TranscriptKinds.AssistantText);
        await world.PersistAsync(session, null, TranscriptKinds.TurnEnd, TranscriptKinds.StopReasons.EndTurn);
        world.Clock.Advance(TimeSpan.FromMilliseconds(50));
        await world.DrainAsync(session);

        var ended = await world.ReloadAsync(session);
        ended.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        ended.Attempt.CompletedAt.ShouldBeNull();
        ended.Session.Status.ShouldBe(SessionStatus.Running);
        await AssertUnscannedAsync(world, session);
    }

    [Test]
    public async Task C1382_Rearm_saved_interrupt_before_processing_returns_to_input()
    {
        await using var world = await EchoWorld.OpenAsync();
        var session = await world.StartCancelledAsync();
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(session, "Continue after interruption");
        world.Clock.Advance(TimeSpan.FromSeconds(2));
        await world.PersistAsync(session, "[Request interrupted by user]");
        world.Clock.Advance(TimeSpan.FromMilliseconds(50));
        await world.DrainAsync(session);

        (await world.ReloadAsync(session)).Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        await AssertUnscannedAsync(world, session);
    }

    [Test]
    public async Task C1382_Rearm_follow_up_saved_before_turn_end_still_arms()
    {
        await using var world = await EchoWorld.OpenAsync();
        var session = await world.StartCancelledAsync();
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(session, "Continue after interruption");
        world.Clock.Advance(TimeSpan.FromSeconds(2));
        await world.PersistAsync(session, null, TranscriptKinds.TurnEnd, TranscriptKinds.StopReasons.EndTurn);
        world.Clock.Advance(TimeSpan.FromSeconds(1));
        await world.PersistAsync(session, "Ask the next question");
        world.Clock.Advance(TimeSpan.FromMilliseconds(50));
        world.Events.Clear();
        await world.DrainAsync(session);

        PhaseChanges(world).ShouldBe(
        [
            nameof(RunPhase.StreamingTurn),
            nameof(RunPhase.AwaitingInput),
            nameof(RunPhase.StreamingTurn)
        ]);
        (await world.ReloadAsync(session)).Attempt.Phase.ShouldBe(RunPhase.StreamingTurn);

        world.Clock.Advance(TimeSpan.FromSeconds(1));
        await world.PersistAsync(session, null, TranscriptKinds.TurnEnd, TranscriptKinds.StopReasons.EndTurn);
        await world.DrainAsync(session);
        (await world.ReloadAsync(session)).Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        await AssertUnscannedAsync(world, session);
    }

    [Test]
    public async Task C1382_Rearm_previous_turn_end_does_not_end_current_turn()
    {
        await using var world = await EchoWorld.OpenAsync();
        var session = await world.StartCancelledAsync();
        await ArmRealPromptAsync(world, session);
        world.Clock.Advance(TimeSpan.FromSeconds(1));
        await world.PersistAsync(session, null, TranscriptKinds.TurnEnd, TranscriptKinds.StopReasons.EndTurn);
        await world.DrainAsync(session);
        var previousEnd = await world.LatestTurnEndSequenceAsync(session);
        (await world.ReloadAsync(session)).Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);

        await ArmRealPromptAsync(world, session);
        var armed = await world.ReloadAsync(session);
        armed.Attempt.Phase.ShouldBe(RunPhase.StreamingTurn);
        var armedAt = armed.Attempt.PhaseStartedAt;

        // The previous turn's row keeps its sequence and is persisted again after this
        // re-arm, so its save clock is newer than PhaseStartedAt.
        world.Clock.Advance(TimeSpan.FromSeconds(1));
        await world.ReplayTurnEndAsync(session, previousEnd);
        await world.DrainAsync(session);
        var held = await world.ReloadAsync(session);
        held.Attempt.Phase.ShouldBe(RunPhase.StreamingTurn);
        held.Attempt.PhaseStartedAt.ShouldBe(armedAt);
        held.Session.Status.ShouldBe(SessionStatus.Running);

        world.Clock.Advance(TimeSpan.FromSeconds(1));
        await world.PersistAsync(session, null, TranscriptKinds.TurnEnd, TranscriptKinds.StopReasons.EndTurn);
        await world.DrainAsync(session);
        (await world.ReloadAsync(session)).Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        await AssertUnscannedAsync(world, session);
    }

    [Test]
    public async Task C1382_Rearm_non_end_turn_stop_returns_to_awaiting_input()
    {
        await using var world = await EchoWorld.OpenAsync();
        var sessions = new List<LiveAttempt>();
        foreach (var reason in new string?[] { "max_tokens", "error", "stop_sequence", "refusal", null })
        {
            var session = await world.StartCancelledAsync();
            sessions.Add(session);
            await ArmRealPromptAsync(world, session);
            world.Clock.Advance(TimeSpan.FromSeconds(1));
            await world.PersistAsync(session, null, TranscriptKinds.TurnEnd, reason);
            await world.DrainAsync(session);
            var ended = await world.ReloadAsync(session);
            ended.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput, reason ?? "null");
            ended.Attempt.CompletedAt.ShouldBeNull();
            ended.Session.Status.ShouldBe(SessionStatus.Running);
        }

        await AssertUnscannedAsync(world, sessions.ToArray());
    }

    [Test]
    public async Task C1382_Rearm_pre_deploy_slash_prompt_is_not_killed()
    {
        await using var world = await EchoWorld.OpenAsync();
        var session = await world.StartCancelledAsync();
        (await world.ReloadAsync(session)).Attempt.PhaseDurationsJson.ShouldContain("OpenInputWait");
        await world.RemoveOpenInputWaitAsync(session);
        (await world.ReloadAsync(session)).Attempt.PhaseDurationsJson.ShouldNotContain("OpenInputWait");

        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(session, "/status");
        await world.DrainAsync(session);
        await world.PersistAsync(session, "<command-name>/status</command-name>");
        await world.DrainAsync(session);

        var restored = await world.ReloadAsync(session);
        restored.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        restored.Attempt.PhaseStartedAt.ShouldBe(session.PhaseStartedAt);
        restored.Session.Status.ShouldBe(SessionStatus.Running);
        await AssertUnscannedAsync(world, session);
    }

    [Test]
    public async Task C1382_Order_real_prompt_only_and_wrapper_only()
    {
        await using var world = await EchoWorld.OpenAsync();
        var real = await world.StartCancelledAsync();
        var wrapper = await world.StartCancelledAsync();

        world.Clock.Advance(TimeSpan.FromMinutes(1));
        await world.PersistAsync(wrapper, "<command-name>/status</command-name>");
        await world.DrainAsync(wrapper);
        var wrapped = await world.ReloadAsync(wrapper);
        wrapped.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        wrapped.Attempt.LastEventAt.ShouldBe(wrapper.InputWaitAt);
        wrapped.Attempt.PhaseStartedAt.ShouldBe(wrapper.PhaseStartedAt);

        await ArmRealPromptAsync(world, real);
        world.Clock.Advance(TimeSpan.FromMinutes(10));
        (await world.ScanAsync()).ShouldBe(1);
        real.Adapter.KillCount.ShouldBe(1);
        real.Adapter.Disposed.ShouldBeTrue();
        wrapper.Adapter.KillCount.ShouldBe(0);
        wrapper.Adapter.Disposed.ShouldBeFalse();
        var stillWaiting = await world.ReloadAsync(wrapper);
        stillWaiting.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
        stillWaiting.Session.Status.ShouldBe(SessionStatus.Running);
        stillWaiting.Session.EndedAt.ShouldBeNull();
    }

    [Test]
    public async Task C1382_Echo_text_does_not_postpone_stall_after_real_prompt()
    {
        await using var world = await EchoWorld.OpenAsync();
        var session = await world.StartCancelledAsync();
        var promptAt = await ArmRealPromptAsync(world, session);

        for (var i = 0; i < 4; i++)
        {
            world.Clock.Advance(TimeSpan.FromMinutes(1));
            await world.PersistAsync(session, "/status");
            await world.DrainAsync(session);
            await world.PersistAsync(session, "<command-name>/status</command-name>");
            await world.DrainAsync(session);
            await world.PersistAsync(session, "/status", TranscriptKinds.AssistantText);
            await world.DrainAsync(session);
        }

        var echoed = await world.ReloadAsync(session);
        echoed.Attempt.Phase.ShouldBe(RunPhase.StreamingTurn);
        echoed.Attempt.LastEventAt.ShouldBe(promptAt);
        world.Clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        (await world.ScanAsync()).ShouldBe(1);
        session.Adapter.KillCount.ShouldBe(1);
        session.Adapter.Disposed.ShouldBeTrue();
    }

    private static async Task<DateTime> ArmRealPromptAsync(EchoWorld world, LiveAttempt session)
    {
        world.Clock.Advance(TimeSpan.FromMinutes(1));
        var at = world.Clock.GetUtcNow().UtcDateTime;
        await world.PersistAsync(session, "Continue after interruption");
        await world.DrainAsync(session);
        var armed = await world.ReloadAsync(session);
        armed.Attempt.Phase.ShouldBe(RunPhase.StreamingTurn);
        armed.Attempt.LastEventAt.ShouldBe(at);
        armed.Attempt.PhaseDurationsJson.ShouldContain("\"AwaitingInput\"");
        return at;
    }

    private static List<string> PhaseChanges(EchoWorld world) =>
        world.Events.PublishedEvents
            .Where(e => e.EventName == "RunAttemptChanged")
            .Select(e => e.Payload.GetType().GetProperty("phase")?.GetValue(e.Payload) as string ?? "")
            .ToList();

    private static async Task AssertUnscannedAsync(EchoWorld world, params LiveAttempt[] sessions)
    {
        world.Clock.Advance(TimeSpan.FromMinutes(10));
        (await world.ScanAsync()).ShouldBe(0);
        foreach (var session in sessions)
        {
            session.Adapter.KillCount.ShouldBe(0);
            session.Adapter.Disposed.ShouldBeFalse();
            var reloaded = await world.ReloadAsync(session);
            reloaded.Session.Status.ShouldBe(SessionStatus.Running);
            reloaded.Session.EndedAt.ShouldBeNull();
            reloaded.Attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
            reloaded.Attempt.CompletedAt.ShouldBeNull();
        }
    }

    private static async Task CheckStartAsync(
        bool completed, string? stopReason, RunPhase expectedPhase, bool scanAfterInterruption = false)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        await using var db = new AppDbContext(options);
        var scratch = Directory.CreateTempSubdirectory("c1322-session-");
        try
        {
            var card = CreateCard(scratch.FullName);
            db.Add(card.Board.Project);
            await db.SaveChangesAsync();

            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(builder => builder.UseNpgsql(schema.ConnectionString));
            await using var provider = services.BuildServiceProvider();
            var scopes = provider.GetRequiredService<IServiceScopeFactory>();
            var events = new MockEventBus();
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var settings = Options.Create(new AgentSessionSettings
            {
                FirstDeltaTimeoutMs = 1_000,
                KillGraceMs = 100,
                SessionLogPath = Path.Combine(scratch.FullName, "logs")
            });
            var runtime = new AgentSessionRuntime(
                events, settings, scopes, clock, NullLogger<AgentSessionRuntime>.Instance);
            var adapter = new FakeAgentProtocolAdapter
            {
                RegisterOnStart = runtime,
                PromptOutput = "Partial diagnostic response",
                TurnCompleted = completed,
                TurnStopReason = stopReason
            };
            var hooks = new RecordingHooks();
            var service = new AgentSessionService(
                db,
                new FakeWorktrees(Path.Combine(scratch.FullName, "worktree")),
                new WorkspaceHookService(hooks, NullLogger<WorkspaceHookService>.Instance),
                new AdapterFactory(adapter),
                runtime,
                events,
                new SessionMessageQueueService(
                    scopes, runtime, events, clock, NullLogger<SessionMessageQueueService>.Instance),
                scopes,
                settings,
                Options.Create(new SupervisionSettings()),
                clock,
                NullLogger<AgentSessionService>.Instance);

            var result = await service.StartAsync(
                new StartAgentSessionRequest(card.Id, "fake-codex", AgentKind.Codex, "Run the assigned work"),
                new AgentLaunchSpec("fake-codex", AgentKind.Codex, "fake-only", [],
                    new Dictionary<string, string>(), scratch.FullName, 120, 30),
                CancellationToken.None);

            if (scanAfterInterruption)
            {
                // Exercise the exact scanner called by RunAttemptStallHostedService, with
                // fresh DB contexts. A stale streaming control proves the scanner is active.
                var controlSessionId = Guid.NewGuid();
                var control = new FakeAgentProtocolAdapter();
                runtime.Register(controlSessionId, control);
                await using (var seed = new AppDbContext(options))
                {
                    var now = clock.GetUtcNow().UtcDateTime;
                    seed.AgentSessions.Add(new AgentSession
                    {
                        Id = controlSessionId, CardId = card.Id, DefinitionName = "stall-control",
                        AgentKind = AgentKind.Raw, Status = SessionStatus.Running, Cwd = scratch.FullName,
                        CreatedAt = now, StartedAt = now, LastSeenAt = now
                    });
                    seed.RunAttempts.Add(new RunAttempt
                    {
                        Id = Guid.NewGuid(), CardId = card.Id, AgentSessionId = controlSessionId,
                        AttemptNumber = 2, Phase = RunPhase.StreamingTurn, CreatedAt = now,
                        StartedAt = now, LastEventAt = now, PhaseStartedAt = now, Prompt = "stall control"
                    });
                    await seed.SaveChangesAsync();
                }

                clock.Advance(TimeSpan.FromMinutes(10));
                await using (var scanDb = new AppDbContext(options))
                {
                    var scan = new RunAttemptStallDetector(
                        scanDb, runtime, events, settings, clock,
                        NullLogger<RunAttemptStallDetector>.Instance);
                    var stalled = await scan.ScanAsync(CancellationToken.None);
                    adapter.KillCount.ShouldBe(0,
                        "a cancelled boundary must not become an automatic stall kill after five minutes");
                    adapter.Disposed.ShouldBeFalse();
                    stalled.ShouldBe(1, "only the genuinely streaming control is eligible");
                    control.KillCount.ShouldBe(1);
                    control.Disposed.ShouldBeTrue();
                }

                // Refreshing LastEventAt once would merely defer the same kill. Retention must
                // survive another independent scan well beyond every refreshed timestamp.
                clock.Advance(TimeSpan.FromDays(1));
                await using (var scanDb = new AppDbContext(options))
                {
                    var scan = new RunAttemptStallDetector(
                        scanDb, runtime, events, settings, clock,
                        NullLogger<RunAttemptStallDetector>.Instance);
                    (await scan.ScanAsync(CancellationToken.None)).ShouldBe(0);
                    adapter.KillCount.ShouldBe(0);
                    adapter.Disposed.ShouldBeFalse();
                }
            }

            // Read through a new context: tracked in-memory values are not persistence evidence.
            await using var reloaded = new AppDbContext(options);
            var session = await reloaded.AgentSessions.SingleAsync(s => s.Id == result.SessionId);
            var attempt = await reloaded.RunAttempts.SingleAsync(a => a.Id == result.RunAttemptId);
            var storedCard = await reloaded.Cards.SingleAsync(c => c.Id == card.Id);
            var worktree = await reloaded.Worktrees.SingleAsync(w => w.Id == result.WorktreeId);
            result.FirstDeltaReceived.ShouldBeTrue();
            adapter.Started.ShouldBeTrue();
            adapter.SentPrompt.ShouldBe("Run the assigned work");
            adapter.SnapshotRawOutput().ShouldContain("Partial diagnostic response");
            attempt.Phase.ShouldBe(expectedPhase,
                "an interrupted turn must not take the completion-timeout/kill branch");
            attempt.AgentSessionId.ShouldBe(session.Id);
            attempt.WorktreeId.ShouldBe(worktree.Id);
            attempt.ErrorDetails.ShouldBeNull();
            attempt.ExitCode.ShouldBeNull();
            storedCard.OwnerSessionId.ShouldBe(session.Id);
            storedCard.CurrentWorktreeId.ShouldBe(worktree.Id);
            worktree.Status.ShouldBe(WorktreeStatus.Active);
            hooks.Calls.ShouldContain("before_run");

            if (expectedPhase == RunPhase.TimedOut)
            {
                session.Status.ShouldBe(SessionStatus.Failed);
                session.FailureReason.ShouldBe("Timed out waiting for the agent turn to complete.");
                session.EndedAt.ShouldNotBeNull();
                session.TerminationSource.ShouldBe(SessionTerminationSource.SystemRequest);
                attempt.CompletedAt.ShouldNotBeNull();
                adapter.KillCount.ShouldBe(1);
                adapter.Disposed.ShouldBeTrue();
                adapter.Lifecycle.ShouldBe(new[] { "Kill", "Dispose" });
                hooks.Calls.ShouldNotContain("after_run");
            }
            else
            {
                session.Status.ShouldBe(SessionStatus.Running);
                session.EndedAt.ShouldBeNull();
                session.FailureReason.ShouldBeNull();
                session.TerminationSource.ShouldBe(SessionTerminationSource.Unknown);
                session.ExitCode.ShouldBeNull();
                adapter.KillCount.ShouldBe(0);
                adapter.Disposed.ShouldBeFalse();
                adapter.Lifecycle.ShouldBeEmpty();
                adapter.Exited.IsCompleted.ShouldBeFalse();
                if (stopReason == TranscriptKinds.StopReasons.Cancelled)
                {
                    attempt.CompletedAt.ShouldBeNull();
                    hooks.Calls.ShouldNotContain("after_run");
                }
                else
                {
                    attempt.CompletedAt.ShouldNotBeNull();
                    hooks.Calls.Count(c => c == "after_run").ShouldBe(1);
                }
            }
        }
        finally
        {
            Directory.Delete(scratch.FullName, recursive: true);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class EchoWorld : IAsyncDisposable
    {
        private int _sequence;
        public required MutableTimeProvider Clock { get; init; }
        public required AgentSessionRuntime Runtime { get; init; }
        public required SessionRuntimeActionCoordinator Actions { get; init; }
        public required DbContextOptions<AppDbContext> DbOptions { get; init; }
        public required ServiceProvider Provider { get; init; }
        public required IsolatedTestSchema Schema { get; init; }
        public required AppDbContext Db { get; init; }
        public required string Scratch { get; init; }
        public required IOptions<AgentSessionSettings> Settings { get; init; }
        public required MockEventBus Events { get; init; }
        public required IServiceScopeFactory Scopes { get; init; }

        public static async Task<EchoWorld> OpenAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
            var scratch = Directory.CreateTempSubdirectory("c1382-session-").FullName;
            var instant = DateTime.UtcNow;
            var whole = new DateTime(instant.Year, instant.Month, instant.Day,
                instant.Hour, instant.Minute, instant.Second, DateTimeKind.Utc);
            var clock = new MutableTimeProvider(new DateTimeOffset(whole));
            var events = new MockEventBus();
            var settings = Options.Create(new AgentSessionSettings
            {
                FirstDeltaTimeoutMs = 1_000,
                KillGraceMs = 100,
                SessionLogPath = Path.Combine(scratch, "logs")
            });
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IEventBus>(events);
            services.AddSingleton<IOptions<AgentSessionSettings>>(settings);
            services.AddSingleton(Options.Create(new SessionStateSettings()));
            services.AddSingleton<ISessionStateLoader, SessionStateLoader>();
            services.AddSingleton<SessionStateStore>();
            services.AddSingleton<SessionRuntimeActionCoordinator>();
            services.AddSingleton<AgentSessionRuntime>();
            services.AddDbContext<AppDbContext>(builder => builder.UseNpgsql(schema.ConnectionString));
            var provider = services.BuildServiceProvider();
            return new EchoWorld
            {
                Clock = clock,
                Runtime = provider.GetRequiredService<AgentSessionRuntime>(),
                Actions = provider.GetRequiredService<SessionRuntimeActionCoordinator>(),
                DbOptions = options,
                Provider = provider,
                Schema = schema,
                Db = new AppDbContext(options),
                Scratch = scratch,
                Settings = settings,
                Events = events,
                Scopes = provider.GetRequiredService<IServiceScopeFactory>()
            };
        }

        public async Task<LiveAttempt> StartCancelledAsync()
        {
            var adapter = new FakeAgentProtocolAdapter
            {
                RegisterOnStart = Runtime,
                PromptOutput = "Partial diagnostic response",
                TurnCompleted = false,
                TurnStopReason = TranscriptKinds.StopReasons.Cancelled
            };
            var card = CreateCard(Scratch);
            Db.Add(card.Board.Project);
            await Db.SaveChangesAsync();
            var service = new AgentSessionService(
                Db,
                new FakeWorktrees(Path.Combine(Scratch, Guid.NewGuid().ToString("N"))),
                new WorkspaceHookService(new RecordingHooks(), NullLogger<WorkspaceHookService>.Instance),
                new AdapterFactory(adapter),
                Runtime,
                Events,
                new SessionMessageQueueService(
                    Scopes, Runtime, Events, Clock, NullLogger<SessionMessageQueueService>.Instance),
                Scopes,
                Settings,
                Options.Create(new SupervisionSettings()),
                Clock,
                NullLogger<AgentSessionService>.Instance);
            var result = await service.StartAsync(
                new StartAgentSessionRequest(card.Id, "fake-codex", AgentKind.Codex, "Run the assigned work"),
                new AgentLaunchSpec("fake-codex", AgentKind.Codex, "fake-only", [],
                    new Dictionary<string, string>(), Scratch, 120, 30),
                CancellationToken.None);
            await using var reloaded = new AppDbContext(DbOptions);
            var attempt = await reloaded.RunAttempts.SingleAsync(a => a.Id == result.RunAttemptId);
            attempt.Phase.ShouldBe(RunPhase.AwaitingInput);
            return new LiveAttempt(result.SessionId, result.RunAttemptId, adapter,
                attempt.LastEventAt, attempt.PhaseStartedAt, attempt.PhaseDurationsJson);
        }

        public Task PersistAsync(LiveAttempt session, string? text) =>
            PersistAsync(session, text, TranscriptKinds.UserPrompt, null);

        public async Task PersistAsync(LiveAttempt session, string? text, string kind, string? stopReason = null)
        {
            var role = kind == TranscriptKinds.AssistantText ? "assistant"
                : kind == TranscriptKinds.UserPrompt ? "user"
                : null;
            var stored = await Runtime.PersistTranscriptAsync(session.SessionId,
            [
                new SessionRunnerTranscriptEvent(session.SessionId, Interlocked.Increment(ref _sequence),
                    kind, Guid.NewGuid().ToString("D"), null, Clock.GetUtcNow(),
                    role, text, null, null, null, null, stopReason)
            ], CancellationToken.None);
            if (stored.LastStoredSeq is null)
            {
                Runtime.TryGetTranscriptPersistFailure(session.SessionId, out var failure);
                throw new InvalidOperationException(failure?.Detail ?? "transcript persist stored nothing");
            }
        }

        public Task DrainAsync(LiveAttempt session) =>
            Actions.DrainPageAsync(session.SessionId, CancellationToken.None);

        public async Task<long> LatestTurnEndSequenceAsync(LiveAttempt session)
        {
            await using var db = new AppDbContext(DbOptions);
            return await db.TranscriptEntries
                .Where(t => t.AgentSessionId == session.SessionId && t.Kind == TranscriptKinds.TurnEnd)
                .MaxAsync(t => t.Sequence);
        }

        public async Task ReplayTurnEndAsync(LiveAttempt session, long sequence)
        {
            await using var db = new AppDbContext(DbOptions);
            var row = await db.TranscriptEntries.SingleAsync(t =>
                t.AgentSessionId == session.SessionId && t.Sequence == sequence && t.Kind == TranscriptKinds.TurnEnd);
            var now = Clock.GetUtcNow().UtcDateTime;
            row.CreatedAt = now;
            var pending = await db.SessionRuntimeActions
                .Where(a => a.AgentSessionId == session.SessionId && a.Sequence == sequence && a.TriggerKind == "transcript")
                .ToListAsync();
            db.SessionRuntimeActions.RemoveRange(pending);
            var generation = await db.AgentSessions.Where(s => s.Id == session.SessionId)
                .Select(s => s.StartedAt).SingleAsync();
            var projection = TranscriptUiProjection.Stored(row, row.Sequence);
            db.SessionRuntimeActions.Add(new SessionRuntimeAction
            {
                Id = Guid.NewGuid(),
                AgentSessionId = session.SessionId,
                TranscriptEntryId = row.Id,
                AcceptedGeneration = generation,
                Sequence = row.Sequence,
                ProducerSequence = row.Sequence,
                TriggerKind = "transcript",
                CreatedAt = now,
                DueAt = now,
                UiProjectionVersion = 1,
                UiProjectionDelta = TranscriptUiProjection.Capture(projection, row)
            });
            await db.SaveChangesAsync();
        }

        public async Task RemoveOpenInputWaitAsync(LiveAttempt session)
        {
            await using var db = new AppDbContext(DbOptions);
            var attempt = await db.RunAttempts.SingleAsync(a => a.Id == session.AttemptId);
            var durations = JsonSerializer.Deserialize<Dictionary<string, double>>(attempt.PhaseDurationsJson)
                ?? new Dictionary<string, double>();
            durations.Remove("OpenInputWait");
            attempt.PhaseDurationsJson = JsonSerializer.Serialize(durations);
            await db.SaveChangesAsync();
        }

        public async Task<Reloaded> ReloadAsync(LiveAttempt session)
        {
            await using var db = new AppDbContext(DbOptions);
            return new Reloaded(
                await db.AgentSessions.SingleAsync(s => s.Id == session.SessionId),
                await db.RunAttempts.SingleAsync(a => a.Id == session.AttemptId));
        }

        public async Task<int> ScanAsync()
        {
            await using var scanDb = new AppDbContext(DbOptions);
            var scan = new RunAttemptStallDetector(
                scanDb, Runtime, Events, Settings, Clock, NullLogger<RunAttemptStallDetector>.Instance);
            return await scan.ScanAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Provider.DisposeAsync();
            await Schema.DisposeAsync();
            if (Directory.Exists(Scratch))
                Directory.Delete(Scratch, recursive: true);
        }
    }

    private sealed record LiveAttempt(
        Guid SessionId, Guid AttemptId, FakeAgentProtocolAdapter Adapter,
        DateTime InputWaitAt, DateTime PhaseStartedAt, string Durations);

    private sealed record Reloaded(AgentSession Session, RunAttempt Attempt);

    private static Card CreateCard(string repoPath)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(), Name = $"C1322-{Guid.NewGuid():N}",
            LocalRepositoryPath = repoPath, BaseBranch = "master", CreatedAt = now, UpdatedAt = now
        };
        var board = new Board
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Project = project,
            Name = $"C1322-{Guid.NewGuid():N}", CreatedAt = now, UpdatedAt = now
        };
        project.Boards.Add(board);
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(), BoardId = board.Id, Board = board,
            StateKey = "backlog", Name = "Backlog", CardStatus = CardStatus.Backlog,
            CreatedAt = now, UpdatedAt = now
        };
        board.Columns.Add(column);
        board.WorkflowDefinitions.Add(new BoardWorkflowDefinition
        {
            Id = Guid.NewGuid(), BoardId = board.Id, Board = board,
            Version = 1, Name = "C1322 hooks", IsActive = true, CreatedAt = now, UpdatedAt = now,
            Content = """
                hooks:
                  before_run: before-run-witness
                  after_run: after-run-witness
                """
        });
        var card = new Card
        {
            Id = Guid.NewGuid(), BoardId = board.Id, Board = board,
            BoardColumnId = column.Id, BoardColumn = column,
            Identifier = $"C1322-{Guid.NewGuid():N}"[..20], Title = "Interrupted first turn",
            Status = CardStatus.Backlog, CreatedAt = now, UpdatedAt = now
        };
        board.Cards.Add(card);
        column.Cards.Add(card);
        return card;
    }

    private sealed class AdapterFactory(FakeAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }

    private sealed class RecordingHooks : IWorkspaceHookRunner
    {
        public List<string> Calls { get; } = [];

        public Task<WorkspaceHookResult?> RunAsync(
            WorkspaceHookContext context, string hookName, WorkspaceHookDefinition? hook, CancellationToken ct)
        {
            Calls.Add(hookName);
            return Task.FromResult<WorkspaceHookResult?>(hook is null ? null :
                new WorkspaceHookResult(hookName, hook.Command, 0, false, "", "", TimeSpan.Zero));
        }

        public Task<WorkspaceHookResult> RunAsync(
            WorkspaceHookContext context, string hookName, string command, TimeSpan timeout, CancellationToken ct)
        {
            Calls.Add(hookName);
            return Task.FromResult(new WorkspaceHookResult(hookName, command, 0, false, "", "", TimeSpan.Zero));
        }
    }

    private sealed class FakeWorktrees(string path) : IWorktreeManager
    {
        public Task<WorktreeInfo> CreateAsync(string repoPath, string cardId, string baseRef, CancellationToken ct)
        {
            Directory.CreateDirectory(path);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new WorktreeInfo(cardId, repoPath, path, $"feat/card-{cardId}", baseRef, now, now));
        }

        public Task<IReadOnlyList<WorktreeInfo>> ListAsync(string repoPath, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WorktreeInfo>>([]);
        public Task RemoveAsync(string repoPath, string worktreePath, CancellationToken ct) =>
            throw new InvalidOperationException("Start must retain its workspace.");
        public Task TouchAsync(string worktreePath, CancellationToken ct) => Task.CompletedTask;
        public Task<int> PruneStaleAsync(CancellationToken ct) =>
            throw new InvalidOperationException("Start must not prune workspaces.");
    }
}
