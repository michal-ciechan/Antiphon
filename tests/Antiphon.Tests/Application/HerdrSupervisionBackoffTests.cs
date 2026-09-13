using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class HerdrSupervisionBackoffTests
{
    [Test]
    public async Task Three_mixed_qualifying_attempts_hold_the_agent_and_no_fourth_launch_happens()
    {
        var a1 = new FakeAgentProtocolAdapter();
        var a2 = new FakeAgentProtocolAdapter { ThrowOnStart = Timeout() };
        var a3 = new FakeAgentProtocolAdapter();
        await using var f = await Fixture.CreateAsync([a1, a2, a3]);
        await f.TickAsync();
        await f.DueAsync();
        var first = await f.SessionAsync();
        await f.ExitAsync(AgentExitReason.HerdrPaneClosed);
        await using (var reconcileDb = Db())
        {
            var row = await f.SessionAsync();
            var runner = new SessionReconciliationServiceTests.FakeRunnerClient { Sessions = [new SessionRunnerSessionDto(
                row.Id, null, row.StartedAt, "Exited", null, AgentExitReason.HerdrPaneClosed, 0)] };
            await SessionReconciliationServiceTests.BuildService(reconcileDb, runner, new MockEventBus()).ScanAsync(CancellationToken.None);
        }
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(1);
        await f.DueAsync();
        (await f.SessionAsync()).Id.ShouldBe(first.Id);
        (await f.SessionAsync()).HerdrSupervisionFailureKind.ShouldBe(HerdrSupervisionFailureKind.DetectTimeout);
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(2);
        await f.DueAsync();
        (await f.SessionAsync()).Id.ShouldBe(first.Id);
        a3.StartedArgs.ShouldContain("--resume");
        a3.StartedArgs.ShouldNotContain("--session-id");
        await f.ExitAsync(AgentExitReason.HerdrChildGone);
        await f.TickAsync();
        var held = await f.StateAsync();
        held.HerdrConsecutiveFailures.ShouldBe(3);
        held.LastHerdrFailureKind.ShouldBe(HerdrSupervisionFailureKind.ChildGone);
        held.HerdrFailureHeldAt.ShouldNotBeNull();
        held.NextRestartAt.ShouldBeNull();
        held.HerdrHealthySince.ShouldBeNull();
        await f.NoLaunchAsync();
        (await f.StateAsync()).ConsecutiveFailures.ShouldBe(held.ConsecutiveFailures);
        await using var db = Db();
        (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.HerdrSupervisionHeld)).ShouldBe(1);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId && i.CreatedAt >= held.HerdrFailureHeldAt
            && (i.Kind == AgentIncidentKind.RestartScheduled || i.Kind == AgentIncidentKind.BackoffEscalated))).ShouldBe(0);
        var refusal = await Should.ThrowAsync<ConflictException>(() => f.StartAsync());
        refusal.Code.ShouldBe(HerdrSupervisionStateService.HeldCode);
    }

    [Test]
    [Arguments(1, true)] [Arguments(10, false)]
    public async Task A_limit_of_one_holds_on_the_first_qualifying_attempt(int limit, bool held)
    {
        await using var f = await Fixture.CreateAsync([], limit);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.PaneClosed);
        await f.TickAsync();
        ((await f.StateAsync()).HerdrFailureHeldAt is not null).ShouldBe(held);
    }

    [Test]
    public async Task Dedupe_pair_survives_the_timestamp_round_trip()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.PaneClosed, startedAt: DateTime.UtcNow.AddTicks(7));
        await f.TickAsync();
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(1);
        (await f.StateAsync()).LastHerdrObservedStartedAt.ShouldBe((await f.SessionAsync()).StartedAt);
        using var scope = f.Harness.Provider.CreateScope();
        var trackedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tracked = await trackedDb.AgentSessions.SingleAsync(s => s.Cwd == f.Root);
        tracked.StartedAt = new DateTime(DateTime.UtcNow.Ticks / 10 * 10 + 7, DateTimeKind.Utc);
        await trackedDb.SaveChangesAsync();
        var observer = scope.ServiceProvider.GetRequiredService<HerdrSupervisionStateService>();
        await observer.ObserveAsync(f.AgentId, false, false, CancellationToken.None);
        await observer.ObserveAsync(f.AgentId, false, false, CancellationToken.None);
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(2);
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task One_attempt_seen_by_the_launch_catch_runtime_and_reconciler_counts_once(bool exitFirst)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { StartGate = gate, ThrowOnStart = Timeout() };
        await using var f = await Fixture.CreateAsync([adapter]);
        using var scope = f.Harness.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(f.AgentId, new(), CancellationToken.None);
        try
        {
            if (exitFirst) await f.ExitAsync(AgentExitReason.HerdrPaneClosed);
        }
        finally { gate.TrySetResult(); }
        await f.IdleAsync();
        await f.ExitAsync(AgentExitReason.HerdrLaunchDetectTimeout);
        await f.ExitAsync(AgentExitReason.HerdrPaneClosed);
        await using (var reconcileDb = Db())
        {
            var row = await f.SessionAsync();
            var runner = new SessionReconciliationServiceTests.FakeRunnerClient { Sessions = [new SessionRunnerSessionDto(
                row.Id, null, row.StartedAt, "Exited", null, AgentExitReason.HerdrPaneClosed, 0)] };
            await SessionReconciliationServiceTests.BuildService(reconcileDb, runner, new MockEventBus()).ScanAsync(CancellationToken.None);
        }
        await f.TickAsync();
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(1);
        (await f.SessionAsync()).HerdrSupervisionFailureKind.ShouldBe(HerdrSupervisionFailureKind.DetectTimeout);
        await using var db = Db();
        (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.Crash)).ShouldBe(1);
    }

    [Test]
    public async Task Same_row_resumed_twice_and_a_fresh_row_count_three_and_a_sibling_agent_stays_at_zero()
    {
        await using var f = await Fixture.CreateAsync([new FakeAgentProtocolAdapter(), new FakeAgentProtocolAdapter()]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.PaneClosed);
        var id = (await f.SessionAsync()).Id;
        await f.TickAsync();
        await f.StartAsync();
        (await f.SessionAsync()).Id.ShouldBe(id);
        (await f.SessionAsync()).HerdrSupervisionFailureKind.ShouldBeNull();
        await f.ExitAsync(AgentExitReason.HerdrChildGone);
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(2);
        await f.StartAsync(new StartAgentRequest(Fresh: true));
        (await f.SessionAsync()).Id.ShouldNotBe(id);
        (await f.SessionAsync()).HerdrSupervisionFailureKind.ShouldBeNull();
        await f.ExitAsync(AgentExitReason.HerdrLaunchDetectTimeout);
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(3);
        using var siblingScope = f.Harness.Provider.CreateScope();
        var sibling = await siblingScope.ServiceProvider.GetRequiredService<AgentService>().CreateAsync(
            new CreateAgentRequest("Sibling", f.Root, SessionBackend: SessionBackend.Herdr, AlwaysOn: true), CancellationToken.None);
        await using var db = Db();
        var siblingSession = new AgentSession { Id = Guid.NewGuid(), Cwd = f.Root, AgentKind = AgentKind.ClaudeCode,
            SessionBackend = SessionBackend.Herdr, Status = SessionStatus.Failed, CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow, HerdrSupervisionFailureKind = HerdrSupervisionFailureKind.PaneClosed };
        db.AgentSessions.Add(siblingSession);
        (await db.Agents.SingleAsync(a => a.Id == sibling.Id)).PersistentSessionId = siblingSession.Id.ToString("D");
        await db.SaveChangesAsync();
        await f.TickAsync();
        var siblingState = await db.AgentSupervisionStates.AsNoTracking().SingleAsync(s => s.AgentId == sibling.Id);
        siblingState.HerdrConsecutiveFailures.ShouldBe(1);
        siblingState.HerdrFailureHeldAt.ShouldBeNull();
    }

    [Test]
    public async Task A_non_qualifying_terminal_attempt_resets_the_streak()
    {
        await using var f = await Fixture.CreateAsync([new FakeAgentProtocolAdapter { ThrowOnStart = new InvalidOperationException("boom") }]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.PaneClosed, streak: 1);
        await f.TickAsync();
        await f.StartAsync();
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(0);
        (await f.SessionAsync()).HerdrSupervisionFailureKind.ShouldBe(HerdrSupervisionFailureKind.NonQualifying);
    }

    [Test]
    public async Task Null_evidence_neither_counts_nor_resets()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(null, streak: 2);
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(2);
    }

    [Test]
    public async Task Card_owned_sessions_never_move_the_herdr_streak()
    {
        await using var scenario = new AttentionServiceTests.Scenario();
        var (_, boardId, columnId) = await scenario.AddBoardAsync("Herdr ownership");
        var cardId = await scenario.AddCardOnBoardAsync(boardId, columnId);
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.PaneClosed);
        await using var db = Db();
        var session = await f.SessionAsync();
        await db.AgentSessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.CardId, cardId));
        try
        {
            await f.TickAsync();
            (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(0);
        }
        finally { await db.AgentSessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.CardId, (Guid?)null)); }
    }

    [Test]
    [Arguments(SessionBackend.PtyHost, false)] [Arguments(SessionBackend.Herdr, true)]
    public async Task PtyHost_and_pool_sessions_never_move_the_herdr_streak(SessionBackend backend, bool pool)
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.PaneClosed);
        await using var db = Db();
        await db.AgentSessions.Where(s => s.Cwd == f.Root).ExecuteUpdateAsync(u => u.SetProperty(s => s.SessionBackend, backend));
        await db.Agents.Where(a => a.Id == f.AgentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.IsPoolDelegate, pool));
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(0);
    }

    [Test]
    [Arguments(AgentExitReason.HerdrDetached)]
    [Arguments(AgentExitReason.HerdrRestartPresumedDead)]
    [Arguments(AgentExitReason.HerdrPaneLeftOpen)]
    public async Task Intentional_stop_detach_presumed_dead_and_pane_left_open_do_not_count(AgentExitReason reason)
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(null, streak: 2);
        await f.ExitAsync(reason);
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(0);
    }

    [Test]
    public async Task A_launch_still_owned_by_the_queue_is_consumed_only_after_its_catch_classifies_it()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { StartGate = gate, ThrowOnStart = Timeout() };
        await using var f = await Fixture.CreateAsync([adapter]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.PaneClosed);
        await f.TickAsync();
        try
        {
            using var scope = f.Harness.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(f.AgentId, new(), CancellationToken.None);
            var session = await f.SessionAsync();
            await using var db = Db();
            await db.AgentSessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, SessionStatus.Failed)
                .SetProperty(s => s.HerdrSupervisionFailureKind, HerdrSupervisionFailureKind.NonQualifying));
            await f.TickAsync();
            (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(1);
            (await f.StateAsync()).NextRestartAt.ShouldBeNull();
        }
        finally { gate.TrySetResult(); }
        await f.IdleAsync();
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(2);
    }

    [Test]
    public async Task Supervisor_rechecks_the_hold_before_a_due_start_and_writes_no_failure_incident()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, held: true);
        await using var db = Db();
        await db.AgentSupervisionStates.Where(s => s.AgentId == f.AgentId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.NextRestartAt, DateTime.UtcNow.AddHours(-1)));
        await f.NoLaunchAsync();
        (await f.StateAsync()).NextRestartAt.ShouldBeNull();
    }

    [Test]
    public async Task Hold_committed_at_due_handoff_is_rechecked_before_Start_is_invoked()
    {
        var hook = new AfterCommitInterceptor();
        await using var f = await Fixture.CreateAsync([], configureDb: b => b.AddInterceptors(hook));
        await f.SeedTerminalAsync(null);
        await f.TickAsync();
        hook.OnNextCommit = async () =>
        {
            await using var db = Db();
            await db.AgentSupervisionStates.Where(s => s.AgentId == f.AgentId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.HerdrFailureHeldAt, DateTime.UtcNow)
                .SetProperty(s => s.HerdrConsecutiveFailures, 3).SetProperty(s => s.NextRestartAt, (DateTime?)null));
        };
        await f.DueAsync();
        f.Harness.SupervisorLog.ShouldNotContain(s => s.Contains("supervised restart attempt"));
        await f.NoLaunchAsync();
    }

    private sealed class AfterCommitInterceptor : DbTransactionInterceptor
    {
        public Func<Task>? OnNextCommit { get; set; }
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            var action = OnNextCommit;
            OnNextCommit = null;
            if (action is not null) await action();
        }
    }

    [Test]
    public async Task Held_start_without_the_flag_is_409_before_latch_clear_composition_and_the_model_gate()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, held: true);
        await using var db = Db();
        var latch = DateTime.UtcNow.AddMinutes(-5);
        await db.AgentSupervisionStates.Where(s => s.AgentId == f.AgentId).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.Suspended, true).SetProperty(s => s.LivenessLatchedAt, latch));
        await db.Agents.Where(a => a.Id == f.AgentId).ExecuteUpdateAsync(u => u
            .SetProperty(a => a.HerdrWorkspaceLabel, "PredictionMarkets").SetProperty(a => a.HerdrTabLabel, "Orch"));
        f.Harness.Runner.PlacementCheck = _ => throw new ConflictException("preflight must not run", "pane_occupied");
        var before = await f.StateAsync();
        var ex = await Should.ThrowAsync<ConflictException>(() => f.StartAsync());
        f.Harness.Runner.CheckCalls.Count.ShouldBe(0);
        ex.Code.ShouldBe(HerdrSupervisionStateService.HeldCode);
        ex.Message.ShouldContain("Herdr test");
        ex.Message.ShouldContain("3 of 3");
        ex.Message.ShouldContain("DetectTimeout");
        ex.Message.ShouldContain("resetHerdrFailureHold");
        var after = await f.StateAsync();
        after.Suspended.ShouldBeTrue();
        after.LivenessLatchedAt.ShouldBe(before.LivenessLatchedAt);
        f.Sentinel.Started.ShouldBeFalse();
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Explicit_retry_clears_only_the_herdr_hold_and_launches_through_every_normal_guard(bool fresh)
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var f = await Fixture.CreateAsync([adapter]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, held: true);
        var before = await f.StateAsync();
        await f.StartAsync(new StartAgentRequest(Fresh: fresh, ResetHerdrFailureHold: true));
        var after = await f.StateAsync();
        adapter.Started.ShouldBeTrue();
        after.HerdrFailureHeldAt.ShouldBeNull();
        after.HerdrConsecutiveFailures.ShouldBe(0);
        after.HerdrHealthySince.ShouldBeNull();
        after.LastHerdrObservedSessionId.ShouldBe(before.LastHerdrObservedSessionId);
        after.LastHerdrObservedStartedAt.ShouldBe(before.LastHerdrObservedStartedAt);
        after.ConsecutiveFailures.ShouldBe(before.ConsecutiveFailures);
        await using var db = Db();
        (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.HerdrSupervisionRetried)).ShouldBe(1);
    }

    [Test]
    public async Task Explicit_retry_consumes_an_unobserved_outcome_first_so_it_cannot_count_again()
    {
        await using var f = await Fixture.CreateAsync([new FakeAgentProtocolAdapter()]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, streak: 2);
        var previous = await f.SessionAsync();
        await f.StartAsync(new StartAgentRequest(ResetHerdrFailureHold: true));
        var state = await f.StateAsync();
        state.LastHerdrObservedStartedAt.ShouldBe(previous.StartedAt);
        state.HerdrConsecutiveFailures.ShouldBe(0);
        await using var db = Db();
        (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.HerdrSupervisionRetried)).ShouldBe(0);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.HerdrSupervisionHeld)).ShouldBe(0);
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(0);
    }

    [Test]
    public async Task Ten_minutes_of_observed_Running_resets_the_herdr_streak()
    {
        await using var f = await Fixture.CreateAsync([new FakeAgentProtocolAdapter(), new FakeAgentProtocolAdapter()]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.PaneClosed, streak: 1);
        await f.StartAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(2);
        await f.TickAsync();
        f.Harness.Clock.Advance(TimeSpan.FromMinutes(9));
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(2);
        f.Harness.Clock.Advance(TimeSpan.FromMinutes(2));
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(0);
        await f.ExitAsync(AgentExitReason.HerdrChildGone);
        await f.StartAsync();
        (await f.StateAsync()).HerdrHealthySince.ShouldBeNull();
    }

    [Test]
    public async Task Explicit_retry_holds_again_after_three_new_attempts()
    {
        await using var f = await Fixture.CreateAsync([new FakeAgentProtocolAdapter(), new FakeAgentProtocolAdapter(), new FakeAgentProtocolAdapter()]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, held: true);
        await f.StartAsync(new StartAgentRequest(ResetHerdrFailureHold: true));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await f.ExitAsync(AgentExitReason.HerdrChildGone);
            await f.TickAsync();
            (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(attempt);
            if (attempt < 3) await f.StartAsync();
        }
        (await f.StateAsync()).HerdrFailureHeldAt.ShouldNotBeNull();
        await f.NoLaunchAsync();
        await using var db = Db();
        (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.HerdrSupervisionRetried)).ShouldBe(1);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.HerdrSupervisionHeld)).ShouldBe(1);
    }

    [Test]
    public async Task Observed_Running_timestamp_survives_server_restart()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(null, streak: 2);
        await using var db = Db();
        await db.AgentSessions.Where(s => s.Cwd == f.Root).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        await f.TickAsync();
        var first = (await f.StateAsync()).HerdrHealthySince;
        await f.Harness.DisposeAsync();
        f.Harness = AgentSupervisionTests.BuildHarness(f.Root, [], definitionKind: "ClaudeCode");
        f.Harness.Clock.Advance(TimeSpan.FromMinutes(9));
        await f.TickAsync();
        (await f.StateAsync()).HerdrHealthySince.ShouldBe(first);
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(2);
        f.Harness.Clock.Advance(TimeSpan.FromMinutes(2));
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(0);
    }

    [Test]
    public async Task Sixty_seconds_of_Starting_contributes_no_healthy_time()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(null, streak: 2);
        await using var db = Db();
        await db.AgentSessions.Where(s => s.Cwd == f.Root).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Starting));
        await f.TickAsync();
        f.Harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await db.AgentSessions.Where(s => s.Cwd == f.Root).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        var firstRunning = f.Harness.Clock.GetUtcNow().UtcDateTime;
        await f.TickAsync();
        (await f.StateAsync()).HerdrHealthySince!.Value.ShouldBeGreaterThanOrEqualTo(firstRunning.AddMilliseconds(-1));
    }

    [Test]
    public async Task Concurrent_tick_and_start_consume_one_outcome_and_neither_starts_through_the_committed_hold()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, streak: 2);
        await using var db = Db();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Agents.FromSqlInterpolated($"""SELECT * FROM "Agents" WHERE "Id" = {f.AgentId} FOR UPDATE""").SingleAsync();
        var tick = f.TickAsync();
        var start = f.StartAsync();
        await tx.CommitAsync();
        await tick;
        (await Should.ThrowAsync<ConflictException>(start)).Code.ShouldBe(HerdrSupervisionStateService.HeldCode);
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(3);
        (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.HerdrSupervisionHeld)).ShouldBe(1);
        await f.NoLaunchAsync();
    }

    [Test]
    public async Task Changing_limit_backend_labels_or_always_on_does_not_clear_the_hold()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, held: true);
        var held = (await f.StateAsync()).HerdrFailureHeldAt;
        await using var db = Db();
        await db.Agents.Where(a => a.Id == f.AgentId).ExecuteUpdateAsync(u => u
            .SetProperty(a => a.AlwaysOn, false).SetProperty(a => a.SessionBackend, SessionBackend.PtyHost));
        await f.TickAsync();
        (await Should.ThrowAsync<ConflictException>(() => f.StartAsync())).Code.ShouldBe(HerdrSupervisionStateService.HeldCode);
        (await f.StateAsync()).HerdrFailureHeldAt.ShouldBe(held);
        var detail = await f.Harness.Scope.ServiceProvider.GetRequiredService<AgentService>().GetByIdAsync(f.AgentId, CancellationToken.None);
        detail.Supervision!.HerdrFailureHeldAt.ShouldBe(held);
        await db.Agents.Where(a => a.Id == f.AgentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.AlwaysOn, true));
        await f.NoLaunchAsync();
    }

    [Test]
    public async Task Held_start_on_an_already_live_agent_is_idempotent_and_does_not_acknowledge()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, held: true);
        await using var db = Db();
        await db.AgentSessions.Where(s => s.Cwd == f.Root).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Running));
        await f.StartAsync(new StartAgentRequest(ResetHerdrFailureHold: true));
        (await f.StateAsync()).HerdrFailureHeldAt.ShouldNotBeNull();
        f.Sentinel.Started.ShouldBeFalse();
    }

    [Test]
    public async Task Streak_hold_and_dedupe_survive_a_server_restart()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.PaneClosed, streak: 1);
        await f.TickAsync();
        await f.Harness.DisposeAsync();
        f.Harness = AgentSupervisionTests.BuildHarness(f.Root, [], definitionKind: "ClaudeCode");
        await f.TickAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(2);
        await using var db = Db();
        await db.AgentSessions.Where(s => s.Cwd == f.Root).ExecuteUpdateAsync(u => u
            .SetProperty(s => s.StartedAt, DateTime.UtcNow)
            .SetProperty(s => s.HerdrSupervisionFailureKind, HerdrSupervisionFailureKind.ChildGone));
        await f.TickAsync();
        (await f.StateAsync()).HerdrFailureHeldAt.ShouldNotBeNull();
        await f.Harness.DisposeAsync();
        f.Harness = AgentSupervisionTests.BuildHarness(f.Root, [f.Sentinel], new SupervisionSettings { HerdrFailureLimit = 10 }, definitionKind: "ClaudeCode");
        await f.NoLaunchAsync();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(3);
    }

    [Test]
    public async Task Stop_while_held_keeps_the_hold_and_records_SuspendedByUser()
    {
        await using var f = await Fixture.CreateAsync([]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, held: true);
        using var scope = f.Harness.Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AgentControlService>().StopAsync(f.AgentId, CancellationToken.None);
        (await f.StateAsync()).Suspended.ShouldBeTrue();
        (await f.StateAsync()).HerdrFailureHeldAt.ShouldNotBeNull();
        await Should.ThrowAsync<ConflictException>(() => f.StartAsync());
        (await f.StateAsync()).Suspended.ShouldBeTrue();
        await using var db = Db();
        (await db.AgentIncidents.AnyAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.SuspendedByUser)).ShouldBeTrue();
    }

    [Test]
    public async Task A_stale_supervisor_entity_cannot_overwrite_an_explicit_retry()
    {
        await using var f = await Fixture.CreateAsync([new FakeAgentProtocolAdapter()]);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, held: true);
        var supervisor = f.Harness.Supervisor();
        await supervisor.GetOrCreateStateAsync(f.AgentId, CancellationToken.None);
        await f.StartAsync(new StartAgentRequest(ResetHerdrFailureHold: true));
        await supervisor.TickAsync(CancellationToken.None);
        (await f.StateAsync()).HerdrFailureHeldAt.ShouldBeNull();
        (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(0);
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Explicit_retry_still_refuses_a_held_model_and_leaves_the_hold_acknowledged(bool reset)
    {
        await using var f = await Fixture.CreateAsync([], includeModelAvailability: true);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.DetectTimeout, held: true);
        await using var db = Db();
        var alias = ModelAlias.Haiku;
        await db.Agents.Where(a => a.Id == f.AgentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.ModelId, alias));
        var hold = new ModelAvailabilityHold { Id = Guid.NewGuid(), Kind = AgentKind.ClaudeCode, ModelAlias = alias,
            HitAt = DateTime.UtcNow, Reason = "test cap", Source = ModelAvailabilitySource.AutoDetected };
        db.ModelAvailabilityHolds.Add(hold);
        await db.SaveChangesAsync();
        try
        {
            if (reset) await Should.ThrowAsync<ModelDisabledException>(() => f.StartAsync(new StartAgentRequest(ResetHerdrFailureHold: true)));
            else (await Should.ThrowAsync<ConflictException>(() => f.StartAsync())).Code.ShouldBe(HerdrSupervisionStateService.HeldCode);
            ((await f.StateAsync()).HerdrFailureHeldAt is null).ShouldBe(reset);
            (await db.AgentIncidents.CountAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.HerdrSupervisionRetried)).ShouldBe(reset ? 1 : 0);
        }
        finally { await db.ModelAvailabilityHolds.Where(h => h.Id == hold.Id).ExecuteDeleteAsync(); }
    }

    [Test]
    public async Task A_synchronous_refusal_before_any_row_leaves_the_streak_alone()
    {
        await using var f = await Fixture.CreateAsync([], includeModelAvailability: true);
        await f.SeedTerminalAsync(HerdrSupervisionFailureKind.PaneClosed, streak: 1);
        await f.TickAsync();
        await using var db = Db();
        var alias = ModelAlias.Haiku;
        await db.Agents.Where(a => a.Id == f.AgentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.ModelId, alias));
        var hold = new ModelAvailabilityHold { Id = Guid.NewGuid(), Kind = AgentKind.ClaudeCode, ModelAlias = alias,
            HitAt = DateTime.UtcNow, Reason = "test cap", Source = ModelAvailabilitySource.AutoDetected };
        db.ModelAvailabilityHolds.Add(hold);
        await db.SaveChangesAsync();
        try
        {
            await f.DueAsync();
            (await f.StateAsync()).HerdrConsecutiveFailures.ShouldBe(2);
            (await db.AgentSessions.CountAsync(s => s.Cwd == f.Root)).ShouldBe(1);
            (await db.CapacityRecoveryWaits.AnyAsync(w => w.AgentId == f.AgentId
                && w.ConsumerKind == CapacityWaitConsumerKind.StandingStart)).ShouldBeTrue();
            (await db.AgentIncidents.AnyAsync(i => i.AgentId == f.AgentId && i.Kind == AgentIncidentKind.StartFailure)).ShouldBeFalse();
        }
        finally { await db.ModelAvailabilityHolds.Where(h => h.Id == hold.Id).ExecuteDeleteAsync(); }
    }

    internal static ConflictException Timeout() => new("herdr did not detect agent kind 'grok' within 60000ms (last observed: none)", "detect_timeout");
    internal static AppDbContext Db() => new(TestDbFixture.CreateDbContextOptions());

    internal sealed class Fixture : IAsyncDisposable
    {
        public required string Root { get; init; }
        public required Guid AgentId { get; init; }
        public required AgentSupervisionTests.Harness Harness { get; set; }
        public required FakeAgentProtocolAdapter Sentinel { get; init; }

        public static async Task<Fixture> CreateAsync(IReadOnlyList<IAgentProtocolAdapter> adapters, int limit = 3,
            bool includeModelAvailability = false, Action<DbContextOptionsBuilder>? configureDb = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "antiphon-c388-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var sentinel = new FakeAgentProtocolAdapter { ThrowOnStart = new InvalidOperationException("CARD-0388 sentinel: a launch reached the sentinel") };
            var h = AgentSupervisionTests.BuildHarness(root, [..adapters, sentinel],
                new SupervisionSettings { HerdrFailureLimit = limit }, includeModelAvailability: includeModelAvailability,
                definitionKind: "ClaudeCode", configureDb: configureDb);
            var agent = await h.Scope.ServiceProvider.GetRequiredService<AgentService>().CreateAsync(
                new CreateAgentRequest("Herdr test", root, SessionBackend: SessionBackend.Herdr, AlwaysOn: true), CancellationToken.None);
            return new Fixture { Root = root, AgentId = agent.Id, Harness = h, Sentinel = sentinel };
        }

        public async Task SeedTerminalAsync(HerdrSupervisionFailureKind? evidence, int streak = 0, bool held = false, DateTime? startedAt = null)
        {
            await using var db = Db();
            var row = new AgentSession { Id = Guid.NewGuid(), DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode,
                SessionBackend = SessionBackend.Herdr, Cwd = Root, Status = SessionStatus.Failed,
                CreatedAt = DateTime.UtcNow.AddMinutes(-2), StartedAt = startedAt ?? DateTime.UtcNow.AddMinutes(-1),
                LastSeenAt = DateTime.UtcNow, EndedAt = DateTime.UtcNow, HerdrSupervisionFailureKind = evidence,
                TerminationSource = SessionTerminationSource.SystemRequest };
            db.AgentSessions.Add(row);
            var agent = await db.Agents.SingleAsync(a => a.Id == AgentId);
            agent.PersistentSessionId = row.Id.ToString("D");
            agent.Status = AgentStatus.Failed;
            db.AgentSupervisionStates.Add(new AgentSupervisionState { AgentId = AgentId, HerdrConsecutiveFailures = held ? 3 : streak,
                HerdrFailureHeldAt = held ? DateTime.UtcNow.AddSeconds(-1) : null, LastHerdrFailureKind = evidence,
                LastHerdrObservedSessionId = held ? row.Id : null, LastHerdrObservedStartedAt = held ? row.StartedAt : null,
                ConsecutiveFailures = held ? 5 : 0, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        public Task TickAsync() => Harness.Supervisor().TickAsync(CancellationToken.None);
        public async Task DueAsync() { Harness.Clock.Advance(TimeSpan.FromSeconds(30)); await TickAsync(); await IdleAsync(); }
        public Task IdleAsync() => Harness.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        public async Task StartAsync(StartAgentRequest? request = null)
        {
            using var scope = Harness.Provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AgentControlService>().StartAsync(AgentId, request ?? new(), CancellationToken.None);
            await IdleAsync();
        }
        public async Task<AgentSession> SessionAsync()
        {
            await using var db = Db();
            var id = Guid.Parse((await db.Agents.SingleAsync(a => a.Id == AgentId)).PersistentSessionId!);
            return await db.AgentSessions.SingleAsync(s => s.Id == id);
        }
        public async Task<AgentSupervisionState> StateAsync()
        {
            await using var db = Db();
            return await db.AgentSupervisionStates.SingleAsync(s => s.AgentId == AgentId);
        }
        public async Task ExitAsync(AgentExitReason reason)
        {
            var session = await SessionAsync();
            await Harness.Provider.GetRequiredService<AgentSessionRuntime>().ObserveExitAsync(
                new SessionRunnerExitedEvent(session.Id, null, reason, 0, session.StartedAt),
                CancellationToken.None);
        }
        public async Task NoLaunchAsync()
        {
            await using var db = Db();
            var count = await db.AgentSessions.CountAsync(s => s.Cwd == Root);
            Harness.Clock.Advance(TimeSpan.FromHours(1));
            for (var i = 0; i < 3; i++) await TickAsync();
            await IdleAsync();
            (await db.AgentSessions.CountAsync(s => s.Cwd == Root)).ShouldBe(count);
            (await db.AgentSessions.AnyAsync(s => s.Cwd == Root && s.FailureReason != null && s.FailureReason.Contains("CARD-0388 sentinel"))).ShouldBeFalse();
            (await db.AgentIncidents.CountAsync(i => i.AgentId == AgentId && i.Kind == AgentIncidentKind.StartFailure)).ShouldBe(0);
        }
        public async ValueTask DisposeAsync() { await Harness.DisposeAsync(); await AgentSupervisionTests.CleanupAsync(Root); }
    }
}
