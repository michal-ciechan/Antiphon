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
        await SeedInFlightTurnAfterStubAsync(schema, sessionId);
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

    private static async Task SeedHardChainAsync(IsolatedTestSchema schema) =>
        await SeedChainAsync(
            schema,
            (AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            (AgentKind.ClaudeCode, AgentModelLevel.High),
            (AgentKind.Grok, AgentModelLevel.Frontier));

    private static async Task SeedChainAsync(
        IsolatedTestSchema schema,
        params (AgentKind Kind, AgentModelLevel Level)[] pairs)
    {
        await using var db = CreateContext(schema);
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Role = null,
            Complexity = TaskComplexity.Hard,
            CandidatesJson = ComplexityChain.SerializeCandidates(
                pairs.Select(p => new ComplexityCandidatePair(p.Kind, p.Level)).ToList()),
            Provenance = RoutingPinProvenance.Human,
            Reason = "test Hard chain",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedHoldAsync(IsolatedTestSchema schema, AgentKind kind, string alias)
    {
        await using var db = CreateContext(schema);
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = null,
            HitAt = DateTime.UtcNow,
            Reason = "manual hold",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<(AgentTask Task, Guid SessionId, Guid AgentId)> SeedWorkingChainTaskAsync(
        IsolatedTestSchema schema,
        string directory,
        Guid? parentSessionId = null,
        Guid? cardId = null,
        TaskComplexity? complexity = TaskComplexity.Hard)
    {
        var (sessionId, agentId) = await SeedSessionAndAgentAsync(schema, directory);
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentSessionId = parentSessionId,
            ReplyTo = parentSessionId is null ? AgentTaskReplyTo.None : AgentTaskReplyTo.Session,
            Title = "hard chain wall",
            Goal = "plan it",
            Role = AgentTaskRole.Plan,
            CardId = cardId,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            Complexity = complexity,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            Status = AgentTaskStatus.Working,
            AgentSessionId = sessionId,
            AgentId = agentId,
            Ephemeral = true,
            CreatedAt = DateTime.UtcNow,
            DispatchedAt = DateTime.UtcNow,
        };
        await using var db = CreateContext(schema);
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return (task, sessionId, agentId);
    }

    private static async Task<(Guid SessionId, Guid AgentId)> SeedSessionAndAgentAsync(
        IsolatedTestSchema schema, string directory)
    {
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var name = $"wall-{agentId:N}"[..16];
        await using var db = CreateContext(schema);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            EffectiveModelId = "fable",
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = name,
            Slug = name,
            WorkingDirectory = directory,
            Details = "Ephemeral pool delegate.",
            Status = AgentStatus.Running,
            Kind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            IsPoolDelegate = true,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return (sessionId, agentId);
    }

    private static async Task<Guid> SeedSessionAsync(IsolatedTestSchema schema, string directory)
    {
        var sessionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var db = CreateContext(schema);
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private static async Task StampSessionModelAsync(
        IsolatedTestSchema schema, Guid sessionId, string alias)
    {
        await using var db = CreateContext(schema);
        await db.AgentSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.EffectiveModelId, alias));
    }

    private static async Task SeedApiErrorStubTurnAsync(
        IsolatedTestSchema schema, Guid sessionId, Guid taskId, string errorText)
    {
        await using var db = CreateContext(schema);
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;

        db.TranscriptEntries.Add(NewEntry(
            sessionId, ++seq, TranscriptKinds.UserPrompt,
            DelegationReportFormatter.TaskMarker(taskId) + "\n\nDo the thing."));

        var stubText = NewEntry(sessionId, ++seq, TranscriptKinds.AssistantText, errorText);
        stubText.IsApiError = true;
        stubText.ApiErrorClass = "rate_limit";
        stubText.ApiErrorStatus = 429;
        db.TranscriptEntries.Add(stubText);

        var stubEnd = NewEntry(sessionId, ++seq, TranscriptKinds.TurnEnd, null);
        stubEnd.StopReason = "stop_sequence";
        stubEnd.IsApiError = true;
        stubEnd.ApiErrorClass = "rate_limit";
        stubEnd.ApiErrorStatus = 429;
        db.TranscriptEntries.Add(stubEnd);
        await db.SaveChangesAsync();
    }

    private static async Task SeedInFlightTurnAfterStubAsync(IsolatedTestSchema schema, Guid sessionId)
    {
        await using var db = CreateContext(schema);
        var seq = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId)
            .MaxAsync(t => (long?)t.Sequence) ?? 0;
        db.TranscriptEntries.Add(NewEntry(
            sessionId, ++seq, TranscriptKinds.UserPrompt, "continue the work"));
        db.TranscriptEntries.Add(NewEntry(
            sessionId, ++seq, TranscriptKinds.AssistantText, "I'll keep going on the same session."));
        await db.SaveChangesAsync();
    }

    private static async Task ClearHoldsAsync(IsolatedTestSchema schema)
    {
        await using var db = CreateContext(schema);
        var now = DateTime.UtcNow;
        await db.ModelAvailabilityHolds
            .Where(h => h.ClearedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(h => h.ClearedAt, now));
    }

    private static AgentTaskDispatcher CreateDispatcher(IsolatedTestSchema schema)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(schema.ConnectionString));
        services.AddSingleton<IEventBus, MockEventBus>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new SupervisionSettings()));
        services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
        services.AddSingleton(Options.Create(new DelegationSettings
        {
            PoolReservedForCallerMinutes = 2,
            PoolIdleRetireMinutes = 5,
            PoolMaxIdlePerDirectory = 3,
            MaxConcurrentTasks = 512,
        }));
        services.AddOptions<AgentRegistrySettings>().Configure(s =>
        {
            s.DefaultDefinition = "claude";
            s.Definitions["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "claude" };
        });
        services.AddSingleton<AgentRegistry>();
        services.AddSingleton<AgentSessionLaunchQueue>();
        services.AddSingleton<AgentSessionRuntime>();
        services.AddSingleton<SessionMessageQueueService>();
        services.AddSingleton<IDelegateSessionStopper, RecordingSessionStopper>();
        services.AddSingleton<DelegationWorkspaceResolver>();
        services.AddDelegationWorktreeGraph(new GitSettings
        {
            WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-wall-reroute-tick"),
        });
        services.AddScoped<AgentTaskService>();
        services.AddScoped<RoutingPinService>();
        services.AddScoped<ModelAvailability>();
        services.AddScoped<ComplexityRoutingService>();
        services.AddScoped<AgentTaskDispatcher>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<AgentTaskDispatcher>();
    }

    private static TranscriptEntry NewEntry(Guid sessionId, long sequence, string kind, string? text) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        CreatedAt = DateTime.UtcNow,
    };

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class WallRerouteHarness : IServiceScopeFactory, IDisposable
    {
        private readonly ServiceProvider _provider;

        public RecordingSessionStopper Stopper { get; } = new();
        public AgentTaskReplyService Reply { get; }

        public WallRerouteHarness(string connectionString, string workspace)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
            services.AddSingleton<IEventBus, MockEventBus>();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(new SupervisionSettings()));
            services.AddSingleton(Options.Create(new ChannelBridgeSettings()));
            services.AddSingleton(Options.Create(new DelegationSettings { AllowedRoots = [workspace] }));
            services.AddSingleton(Options.Create(new AgentSessionSettings()));
            services.AddSingleton<AgentSessionRuntime>();
            services.AddSingleton<SessionMessageQueueService>();
            services.AddSingleton<ApiErrorRecoveryService>();
            services.AddSingleton<IDelegateSessionStopper>(Stopper);
            services.AddSingleton<DelegationWorkspaceResolver>();
            services.AddDelegationWorktreeGraph(new GitSettings
            {
                WorktreeBasePath = Path.Combine(Path.GetTempPath(), "antiphon-wall-reroute-wt"),
            });
            services.AddScoped<ModelAvailability>();
            services.AddScoped<RoutingPinService>();
            services.AddScoped<ComplexityRoutingService>();
            services.AddScoped<AgentTaskService>();
            _provider = services.BuildServiceProvider();
            Reply = new AgentTaskReplyService(
                this,
                Options.Create(new DelegationSettings { ReplyInlineMaxChars = 20_000 }),
                new MockEventBus(),
                TimeProvider.System,
                NullLogger<AgentTaskReplyService>.Instance);
        }

        public IServiceScope CreateScope() => _provider.CreateScope();

        public void Dispose() => _provider.Dispose();
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-wall-reroute").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
