using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.WallRerouteFixture;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0090 S5: reactive reroute on a usage wall. Isolated schema: unique chain index plus holds.
/// </summary>
[Category("Integration")]
public class ComplexityWallRerouteTests
{
    [Test]
    public async Task Fable_5_wall_on_a_Working_Hard_task_requeues_on_the_next_candidate()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedHardChainAsync(schema);
        var (task, sessionId, agentId) = await SeedWorkingChainTaskAsync(schema, workspace.Path);
        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.FableModelCapIncidentText);

        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        stored.ModelLevel.ShouldBe(AgentModelLevel.High);
        stored.Complexity.ShouldBe(TaskComplexity.Hard);
        stored.AgentSessionId.ShouldBeNull();
        stored.AgentId.ShouldBeNull();
        stored.Attempt.ShouldBe(2);
        stored.FailureReason.ShouldContain("fable hit a usage wall");
        stored.FailureReason.ShouldContain("opus");
        stored.FailureReason.ShouldContain("NO report");
        stored.FailureReason.ShouldContain(workspace.Path);
        stored.FailureReason.ShouldContain("Hard chain 2/3");

        var rerouted = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted);
        rerouted.Detail.ShouldContain("fable hit a usage wall");
        rerouted.Detail.ShouldContain("rerouted to opus");
        rerouted.Detail.ShouldContain("Hard chain 2/3");

        (await verify.Agents.CountAsync(a => a.Id == agentId)).ShouldBe(0);
        harness.Stopper.Killed.ShouldContain(sessionId);

        var hold = await verify.ModelAvailabilityHolds.SingleAsync(
            h => h.ModelAlias == "fable" && h.ClearedAt == null);
        hold.Kind.ShouldBe(AgentKind.ClaudeCode);
        hold.Source.ShouldBe(ModelAvailabilitySource.AutoDetected);

        var incident = await verify.AgentIncidents.SingleAsync(
            i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.ApiErrorTurnDied);
        incident.Message.ShouldContain("rerouted to opus as task attempt 2");
        incident.FailureReason.ShouldBe(ApiErrorRecoveryReasons.WallModelPaused);
    }

    [Test]
    public async Task Session_limit_with_an_alternative_switches_immediately()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedHardChainAsync(schema);
        var (task, sessionId, _) = await SeedWorkingChainTaskAsync(schema, workspace.Path);
        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.SessionLimitFixtureText);

        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.ModelLevel.ShouldBe(AgentModelLevel.High);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.ApiErrorDeferred))
            .ShouldBe(0);
        var recovery = await verify.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == sessionId);
        recovery.ResolvedAt.ShouldNotBeNull();
        recovery.ResolvedReason.ShouldBe(ApiErrorRecoveryReasons.Rerouted);
        recovery.NextAttemptAt.ShouldBeNull();
        harness.Stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task Session_limit_with_no_alternative_keeps_the_CARD_0022_resume()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedChainAsync(schema, (AgentKind.ClaudeCode, AgentModelLevel.Frontier));
        var (task, sessionId, agentId) = await SeedWorkingChainTaskAsync(schema, workspace.Path);
        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.SessionLimitFixtureText);

        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Working);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        stored.AgentSessionId.ShouldBe(sessionId);
        stored.AgentId.ShouldBe(agentId);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(0);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.ApiErrorDeferred))
            .ShouldBe(1);
        var recovery = await verify.ApiErrorRecoveries.SingleAsync(r => r.AgentSessionId == sessionId);
        recovery.ResolvedAt.ShouldBeNull();
        recovery.NextAttemptAt.ShouldNotBeNull();
        harness.Stopper.Killed.ShouldBeEmpty();
        (await verify.Agents.CountAsync(a => a.Id == agentId)).ShouldBe(1);
    }

    [Test]
    public async Task Chain_exhausted_at_the_wall_blocks_instead_of_failing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedHardChainAsync(schema);
        await SeedHoldAsync(schema, AgentKind.ClaudeCode, "opus");
        await SeedHoldAsync(schema, AgentKind.Grok, "grok-4.6");
        var parentSessionId = await SeedSessionAsync(schema, workspace.Path);
        var (task, sessionId, _) = await SeedWorkingChainTaskAsync(
            schema, workspace.Path, parentSessionId);
        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.FableModelCapIncidentText);

        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Blocked);
        stored.Status.ShouldNotBe(AgentTaskStatus.Failed);
        stored.FailureReason.ShouldContain(ComplexityRoutingService.RoutingExhaustedPrefix);
        stored.AgentSessionId.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Failed)).ShouldBe(0);
        (await verify.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == task.Id)).ShouldBe(1);
        harness.Stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task Non_chain_task_fails_on_Fable_5_as_today()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedHardChainAsync(schema);
        var (task, sessionId, _) = await SeedWorkingChainTaskAsync(
            schema, workspace.Path, complexity: null);
        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.FableModelCapIncidentText);

        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Failed);
        stored.FailureReason.ShouldContain(ApiErrorRecoveryReasons.WallModelPaused);
        stored.Complexity.ShouldBeNull();
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(0);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Failed)).ShouldBe(1);
    }

    [Test]
    public async Task Required_pinned_task_is_untouched_on_a_Fable_5_wall()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedHardChainAsync(schema);
        Card card;
        await using (var db = CreateContext(schema))
            card = await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
        await using (var db = CreateContext(schema))
        {
            db.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                Role = AgentTaskRole.Plan,
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                AgentKind = AgentKind.ClaudeCode,
                ModelLevel = AgentModelLevel.Frontier,
                Reason = "CARD-0301 stays on fable",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var (task, sessionId, _) = await SeedWorkingChainTaskAsync(
            schema, workspace.Path, cardId: card.Id);
        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.FableModelCapIncidentText);

        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Failed);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        stored.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        stored.FailureReason.ShouldContain(ApiErrorRecoveryReasons.WallModelPaused);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(0);
        harness.Stopper.Killed.ShouldBeEmpty("Required pin keeps CARD-0022; Fail releases later, this path does not requeue");
    }

    [Test]
    public async Task Second_wall_on_the_rerouted_attempt_takes_the_next_candidate()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedHardChainAsync(schema);
        var (task, sessionId, _) = await SeedWorkingChainTaskAsync(schema, workspace.Path);
        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.FableModelCapIncidentText);

        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        var (session2, agent2) = await SeedSessionAndAgentAsync(schema, workspace.Path);
        await using (var db = CreateContext(schema))
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            row.Status = AgentTaskStatus.Working;
            row.AgentSessionId = session2;
            row.AgentId = agent2;
            row.Ephemeral = true;
            row.DispatchedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await StampSessionModelAsync(schema, session2, "opus");
        await SeedApiErrorStubTurnAsync(
            schema, session2, task.Id,
            "You've reached your Opus 4.6 limit. Run /usage-credits to continue or switch models with /model.");

        await harness.Reply.OnTurnEndAsync(session2, CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        stored.AgentKind.ShouldBe(AgentKind.Grok);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        stored.Attempt.ShouldBe(3);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(2);
        var last = await verify.AgentTaskEvents
            .Where(e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)
            .OrderByDescending(e => e.At)
            .FirstAsync();
        last.Detail.ShouldContain("opus hit a usage wall");
        last.Detail.ShouldContain("grok-4.6");
        last.Detail.ShouldContain("Hard chain 3/3");
        harness.Stopper.Killed.ShouldContain(session2);
    }

    [Test]
    public async Task Nth_plus_one_wall_is_Blocked_by_the_loop_guard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedHardChainAsync(schema);
        var (task, sessionId, _) = await SeedWorkingChainTaskAsync(schema, workspace.Path);
        await using (var db = CreateContext(schema))
        {
            for (var i = 0; i < 3; i++)
            {
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(),
                    AgentTaskId = task.Id,
                    Type = AgentTaskEventType.Rerouted,
                    Detail = $"prior cascade {i}",
                    At = DateTime.UtcNow.AddMinutes(-3 + i),
                });
            }

            await db.SaveChangesAsync();
        }

        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.FableModelCapIncidentText);

        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Blocked);
        stored.FailureReason.ShouldContain(ComplexityRoutingService.RoutingExhaustedPrefix);
        stored.FailureReason.ShouldContain("already rerouted 3/3");
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier, "loop guard must not walk onto opus");
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(3);
        harness.Stopper.Killed.ShouldContain(sessionId);
    }

    [Test]
    public async Task A_later_UserPrompt_does_not_reroute_or_kill_an_in_flight_turn()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedHardChainAsync(schema);
        var (task, sessionId, agentId) = await SeedWorkingChainTaskAsync(schema, workspace.Path);
        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.FableModelCapIncidentText);
        await SeedInFlightTurnAfterStubAsync(schema, sessionId, TranscriptKinds.UserPrompt);
        await ClearHoldsAsync(schema);

        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Working);
        stored.AgentSessionId.ShouldBe(sessionId);
        stored.AgentId.ShouldBe(agentId);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(0);
        harness.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task A_later_QueuedUserPrompt_does_not_reroute_or_kill_an_in_flight_turn()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedHardChainAsync(schema);
        var (task, sessionId, agentId) = await SeedWorkingChainTaskAsync(schema, workspace.Path);
        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.FableModelCapIncidentText);
        await SeedInFlightTurnAfterStubAsync(schema, sessionId, TranscriptKinds.QueuedUserPrompt);
        await ClearHoldsAsync(schema);

        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Working);
        stored.AgentSessionId.ShouldBe(sessionId);
        stored.AgentId.ShouldBe(agentId);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(0);
        harness.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task Loop_guard_Block_is_not_resumed_by_a_dispatcher_tick()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        using var harness = new WallRerouteHarness(schema.ConnectionString, workspace.Path);
        await SeedHardChainAsync(schema);
        var (task, sessionId, _) = await SeedWorkingChainTaskAsync(schema, workspace.Path);
        await using (var db = CreateContext(schema))
        {
            for (var i = 0; i < 3; i++)
            {
                db.AgentTaskEvents.Add(new AgentTaskEvent
                {
                    Id = Guid.NewGuid(),
                    AgentTaskId = task.Id,
                    Type = AgentTaskEventType.Rerouted,
                    Detail = $"prior cascade {i}",
                    At = DateTime.UtcNow.AddMinutes(-3 + i),
                });
            }

            await db.SaveChangesAsync();
        }

        await StampSessionModelAsync(schema, sessionId, "fable");
        await SeedApiErrorStubTurnAsync(
            schema, sessionId, task.Id, UsageLimitWallParser.FableModelCapIncidentText);
        await harness.Reply.OnTurnEndAsync(sessionId, CancellationToken.None);

        var dispatcher = CreateDispatcher(schema);
        var result = await dispatcher.TickAsync(CancellationToken.None);

        result.ResumedRoutingBlocked.ShouldBe(0);
        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Blocked);
        stored.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        (await verify.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Rerouted)).ShouldBe(3);
    }

}
