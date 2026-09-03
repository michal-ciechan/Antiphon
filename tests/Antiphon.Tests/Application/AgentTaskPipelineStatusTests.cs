using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0304 pipeline read model. Isolated schema so empty-stage and fleet-wide assertions do
/// not collide with other suites writing the shared Postgres container.
/// </summary>
[Category("Integration")]
public class AgentTaskPipelineStatusTests
{
    [Test]
    public async Task empty_fleet_returns_every_visible_stage_and_omits_check()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var pipeline = CreateService(db);

        var dto = await pipeline.GetAsync(CancellationToken.None);

        dto.RecommendationsAreAdvisory.ShouldBeTrue();
        dto.MaxConcurrentTasks.ShouldBe(6);
        dto.InFlightAgainstCap.ShouldBe(0);
        dto.Stages.Select(s => s.Role).ShouldBe([
            AgentTaskRole.Custom, AgentTaskRole.Plan, AgentTaskRole.Code, AgentTaskRole.Review,
            AgentTaskRole.Debug, AgentTaskRole.Coverage, AgentTaskRole.Docs, AgentTaskRole.Commit,
            AgentTaskRole.Test, AgentTaskRole.Deploy, AgentTaskRole.Merge,
            AgentTaskRole.Investigate, AgentTaskRole.TestDesign,
        ]);
        dto.Stages.ShouldNotContain(s => s.Role == AgentTaskRole.Check);
        dto.Stages.ShouldNotContain(s => s.Role == AgentTaskRole.Distill);
        dto.Stages.ShouldNotContain(s => s.Role == AgentTaskRole.Diagnose);
        foreach (var stage in dto.Stages)
        {
            stage.InFlight.ShouldBeEmpty();
            stage.Queued.ShouldBeEmpty();
            stage.Blocked.ShouldBeEmpty();
            stage.Ready.ShouldBeEmpty();
            stage.InFlightCount.ShouldBe(0);
            stage.AtOrAboveRecommendation.ShouldBeFalse();
            stage.RoutingPin.ShouldBeNull();
        }
    }

    [Test]
    public async Task shipped_limits_are_one_and_custom_is_unbounded()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var pipeline = CreateService(db);

        var dto = await pipeline.GetAsync(CancellationToken.None);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Plan).RecommendedInFlight.ShouldBe(1);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Code).RecommendedInFlight.ShouldBe(1);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Investigate).RecommendedInFlight.ShouldBe(1);
        dto.Stages.Single(s => s.Role == AgentTaskRole.TestDesign).RecommendedInFlight.ShouldBe(1);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Custom).RecommendedInFlight.ShouldBeNull();
    }

    [Test]
    public async Task a_configured_limit_and_absent_limit_are_reflected()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var settings = new DelegationSettings();
        settings.RolePolicy["Code"].RecommendedInFlight = 4;
        settings.RolePolicy["Plan"].RecommendedInFlight = null;
        var pipeline = CreateService(db, settings);

        var dto = await pipeline.GetAsync(CancellationToken.None);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Code).RecommendedInFlight.ShouldBe(4);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Plan).RecommendedInFlight.ShouldBeNull();
    }

    [Test]
    public async Task working_and_dispatched_are_in_flight_and_blocked_is_separate()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var dispatchedAt = DateTime.UtcNow.AddMinutes(-10);
        var working = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Working,
            dispatchedAt: dispatchedAt, createdAt: dispatchedAt.AddMinutes(-2), title: "working code");
        var dispatched = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Dispatched,
            dispatchedAt: dispatchedAt.AddMinutes(1), createdAt: dispatchedAt.AddMinutes(-1),
            title: "dispatched code");
        var blocked = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Blocked,
            title: "blocked code");
        var check = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Check, AgentTaskStatus.Working,
            dispatchedAt: dispatchedAt, title: "check");

        var dto = await CreateService(db).GetAsync(CancellationToken.None);
        var code = dto.Stages.Single(s => s.Role == AgentTaskRole.Code);
        code.InFlight.Select(t => t.TaskId).ShouldBe([working.Id, dispatched.Id], ignoreOrder: false);
        code.InFlightCount.ShouldBe(2);
        code.AtOrAboveRecommendation.ShouldBeTrue();
        code.Blocked.Select(t => t.TaskId).ShouldBe([blocked.Id]);
        code.InFlight.ShouldNotContain(t => t.TaskId == blocked.Id);
        dto.Stages.SelectMany(s => s.InFlight).ShouldNotContain(t => t.TaskId == check.Id);
        foreach (var row in code.InFlight)
            row.LastActivityAt.ShouldBe(row.DispatchedAt!.Value);
    }

    [Test]
    public async Task last_activity_uses_transcript_after_dispatch_and_falls_back_otherwise()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var dispatchedAt = DateTime.UtcNow.AddMinutes(-30);
        var sessionId = Guid.NewGuid();
        await SeedSessionAsync(db, sessionId, workspace.Path);
        var task = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Debug, AgentTaskStatus.Working,
            dispatchedAt: dispatchedAt, sessionId: sessionId, title: "debug");
        // Npgsql timestamptz stores microseconds; DateTime has 100 ns ticks. Seed at the stored
        // precision so LastActivityAt compares equal instead of 200 ns off (CARD-0336).
        var tooEarly = Truncate(dispatchedAt.AddMinutes(-5))!.Value;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = "assistant",
            Timestamp = tooEarly,
            CreatedAt = tooEarly,
        });
        var later = Truncate(dispatchedAt.AddMinutes(12))!.Value;
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 2,
            Kind = "assistant",
            Timestamp = later,
            CreatedAt = Truncate(later.AddSeconds(1))!.Value,
        });
        await db.SaveChangesAsync();

        var dto = await CreateService(db).GetAsync(CancellationToken.None);
        var row = dto.Stages.Single(s => s.Role == AgentTaskRole.Debug).InFlight.Single(t => t.TaskId == task.Id);
        row.LastActivityAt.ShouldBe(later);
    }

    [Test]
    public async Task queued_work_is_awaiting_dispatch_unless_a_live_lease_holds_it()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var heldDir = new TempWorkspace();
        using var freeDir = new TempWorkspace();
        var holder = await SeedTaskAsync(db, heldDir.Path, AgentTaskRole.Docs, AgentTaskStatus.Working,
            dispatchedAt: DateTime.UtcNow.AddMinutes(-2), title: "the holder",
            workspace: WorkspaceMode.Shared, repoPath: heldDir.Path);
        var waiting = await SeedTaskAsync(db, heldDir.Path, AgentTaskRole.Docs, AgentTaskStatus.Queued,
            title: "waiting", workspace: WorkspaceMode.Shared, repoPath: heldDir.Path,
            createdAt: DateTime.UtcNow.AddMinutes(-1));
        var ordinary = await SeedTaskAsync(db, freeDir.Path, AgentTaskRole.Docs, AgentTaskStatus.Queued,
            title: "ordinary", workspace: WorkspaceMode.Shared, repoPath: freeDir.Path);
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = ordinary.Id,
            Type = AgentTaskEventType.Held,
            Detail = "historical hold that has released",
            At = DateTime.UtcNow.AddHours(-1),
        });
        await db.SaveChangesAsync();

        var docs = (await CreateService(db).GetAsync(CancellationToken.None))
            .Stages.Single(s => s.Role == AgentTaskRole.Docs);
        var heldRow = docs.Queued.Single(t => t.TaskId == waiting.Id);
        heldRow.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonSharedCheckoutLease);
        heldRow.HeldBy.Select(h => h.TaskId).ShouldBe([holder.Id]);
        var ordinaryRow = docs.Queued.Single(t => t.TaskId == ordinary.Id);
        ordinaryRow.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonAwaitingDispatch);
        ordinaryRow.HeldBy.ShouldBeEmpty();
    }

    [Test]
    public async Task queued_work_is_concurrency_cap_when_in_flight_fills_the_cap()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var flyingDir = new TempWorkspace();
        using var queuedDir = new TempWorkspace();
        var flying = await SeedTaskAsync(db, flyingDir.Path, AgentTaskRole.Code, AgentTaskStatus.Working,
            dispatchedAt: DateTime.UtcNow.AddMinutes(-2), title: "own flying",
            workspace: WorkspaceMode.Shared, repoPath: flyingDir.Path);
        var waiting = await SeedTaskAsync(db, queuedDir.Path, AgentTaskRole.Docs, AgentTaskStatus.Queued,
            title: "own waiting", workspace: WorkspaceMode.Shared, repoPath: queuedDir.Path);

        var settings = new DelegationSettings { MaxConcurrentTasks = 1 };
        var dto = await CreateService(db, settings).GetAsync(CancellationToken.None);

        dto.MaxConcurrentTasks.ShouldBe(1);
        dto.InFlightAgainstCap.ShouldBe(1);
        dto.Stages.SelectMany(s => s.InFlight).ShouldContain(t => t.TaskId == flying.Id);
        var row = dto.Stages.SelectMany(s => s.Queued).Single(t => t.TaskId == waiting.Id);
        row.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonConcurrencyCap);
        row.HeldBy.ShouldBeEmpty();
    }

    [Test]
    public async Task a_lease_hold_outranks_the_concurrency_cap()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var heldDir = new TempWorkspace();
        var holder = await SeedTaskAsync(db, heldDir.Path, AgentTaskRole.Code, AgentTaskStatus.Working,
            dispatchedAt: DateTime.UtcNow.AddMinutes(-2), title: "own holder",
            workspace: WorkspaceMode.Shared, repoPath: heldDir.Path);
        var waiting = await SeedTaskAsync(db, heldDir.Path, AgentTaskRole.Docs, AgentTaskStatus.Queued,
            title: "own waiting on lease", workspace: WorkspaceMode.Shared, repoPath: heldDir.Path);

        var settings = new DelegationSettings { MaxConcurrentTasks = 1 };
        var dto = await CreateService(db, settings).GetAsync(CancellationToken.None);

        dto.InFlightAgainstCap.ShouldBe(1);
        var row = dto.Stages.SelectMany(s => s.Queued).Single(t => t.TaskId == waiting.Id);
        row.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonSharedCheckoutLease);
        row.HeldBy.Select(h => h.TaskId).ShouldBe([holder.Id]);
    }

    [Test]
    [Arguments(AgentTaskRole.Check)]
    [Arguments(AgentTaskRole.Diagnose)]
    public async Task a_specialist_role_working_task_does_not_count_against_the_cap(AgentTaskRole role)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var checkDir = new TempWorkspace();
        using var queuedDir = new TempWorkspace();
        var check = await SeedTaskAsync(db, checkDir.Path, role, AgentTaskStatus.Working,
            dispatchedAt: DateTime.UtcNow.AddMinutes(-2), title: "own check",
            workspace: WorkspaceMode.Shared, repoPath: checkDir.Path);
        var waiting = await SeedTaskAsync(db, queuedDir.Path, AgentTaskRole.Docs, AgentTaskStatus.Queued,
            title: "own waiting beside a check", workspace: WorkspaceMode.Shared,
            repoPath: queuedDir.Path);

        var settings = new DelegationSettings { MaxConcurrentTasks = 1 };
        var dto = await CreateService(db, settings).GetAsync(CancellationToken.None);

        dto.InFlightAgainstCap.ShouldBe(0);
        dto.Stages.SelectMany(s => s.InFlight).ShouldNotContain(t => t.TaskId == check.Id);
        var row = dto.Stages.SelectMany(s => s.Queued).Single(t => t.TaskId == waiting.Id);
        row.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonAwaitingDispatch);
    }

    [Test]
    public async Task a_ready_plan_appears_on_the_code_stage()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var card = await SeedCardAsync(db, CardStatus.Review, "CARD-9304");
        var completedAt = DateTime.UtcNow.AddMinutes(-8);
        var plan = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "plan CARD-9304", cardId: card.Id, completedAt: completedAt,
            createdAt: completedAt.AddMinutes(-20),
            deliverablePath: "docs/superpowers/plans/2026-09-01-card-9304.md");

        var dto = await CreateService(db).GetAsync(CancellationToken.None);
        var ready = dto.Stages.Single(s => s.Role == AgentTaskRole.Code).Ready.ShouldHaveSingleItem();
        ready.Card.Identifier.ShouldBe("CARD-9304");
        ready.SourcePlanTaskId.ShouldBe(plan.Id);
        ready.ReadySince.ShouldBe(completedAt, TimeSpan.FromMilliseconds(1));
        ready.DeliverablePath.ShouldBe("docs/superpowers/plans/2026-09-01-card-9304.md");
        dto.Stages.Where(s => s.Role != AgentTaskRole.Code).ShouldAllBe(s => s.Ready.Count == 0);
        ready.RoutingPin.ShouldBeNull();
    }

    [Test]
    public async Task a_ready_row_carries_the_card_code_pin_when_one_is_set()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var card = await SeedCardAsync(db, CardStatus.Review, "CARD-0305");
        var completedAt = DateTime.UtcNow.AddMinutes(-8);
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "plan CARD-0305", cardId: card.Id, completedAt: completedAt,
            createdAt: completedAt.AddMinutes(-20),
            deliverablePath: "docs/superpowers/plans/2026-09-01-card-0305.md");
        db.RoutingPins.Add(new RoutingPin
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            Role = AgentTaskRole.Code,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Required,
            AgentKind = AgentKind.Grok,
            Reason = "operator: execute CARD-0305 on Grok",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var ready = (await CreateService(db).GetAsync(CancellationToken.None))
            .Stages.Single(s => s.Role == AgentTaskRole.Code).Ready.ShouldHaveSingleItem();
        var pin = ready.RoutingPin.ShouldNotBeNull();
        pin.CardIdentifier.ShouldBe("CARD-0305");
        pin.AgentKind.ShouldBe(AgentKind.Grok);
        pin.Provenance.ShouldBe(RoutingPinProvenance.Human);
    }

    [Test]
    public async Task a_dated_pin_is_the_queued_reason_when_nothing_else_holds_the_task()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var card = await SeedCardAsync(db, CardStatus.InProgress, "CARD-0301");
        var task = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Queued,
            title: "dated plan", cardId: card.Id);
        db.RoutingPins.Add(new RoutingPin
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            Role = AgentTaskRole.Plan,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Required,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Frontier,
            NotBefore = DateTime.UtcNow.AddHours(6),
            Reason = "operator: plan on fable after the weekly cap",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var plan = (await CreateService(db).GetAsync(CancellationToken.None))
            .Stages.Single(s => s.Role == AgentTaskRole.Plan);
        plan.Queued.Single(t => t.TaskId == task.Id).QueueReason
            .ShouldBe(AgentTaskPipelineStatusService.QueueReasonRoutingPinNotBefore);
        plan.RoutingPin.ShouldBeNull("the dated pin is on the card, not stage-wide");
    }

    [Test]
    public async Task later_or_open_code_and_a_canceled_never_dispatched_code_are_classified()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var readyCard = await SeedCardAsync(db, CardStatus.Review, "CARD-9401");
        var openCard = await SeedCardAsync(db, CardStatus.InProgress, "CARD-9402");
        var laterCard = await SeedCardAsync(db, CardStatus.Review, "CARD-9403");
        var planDone = DateTime.UtcNow.AddHours(-2);

        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "ready plan", cardId: readyCard.Id, completedAt: planDone,
            createdAt: planDone.AddHours(-1),
            deliverablePath: "docs/superpowers/plans/ready.md");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Canceled,
            title: "never dispatched", cardId: readyCard.Id, createdAt: planDone.AddMinutes(1));

        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "open plan", cardId: openCard.Id, completedAt: planDone,
            createdAt: planDone.AddHours(-1),
            deliverablePath: "docs/superpowers/plans/open.md");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Queued,
            title: "open code", cardId: openCard.Id, createdAt: planDone.AddMinutes(2));

        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "later plan", cardId: laterCard.Id, completedAt: planDone,
            createdAt: planDone.AddHours(-1),
            deliverablePath: "docs/superpowers/plans/later.md");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Succeeded,
            title: "later code", cardId: laterCard.Id, createdAt: planDone.AddMinutes(3),
            dispatchedAt: planDone.AddMinutes(4), completedAt: planDone.AddMinutes(30));

        var ready = (await CreateService(db).GetAsync(CancellationToken.None))
            .Stages.Single(s => s.Role == AgentTaskRole.Code).Ready;
        ready.Select(r => r.Card.Identifier).ShouldBe(["CARD-9401"]);
    }

    [Test]
    public async Task non_success_latest_plan_wrong_deliverable_and_card_state_suppress_ready()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var failed = await SeedCardAsync(db, CardStatus.Review, "CARD-9501");
        var missing = await SeedCardAsync(db, CardStatus.Review, "CARD-9502");
        var wrong = await SeedCardAsync(db, CardStatus.Review, "CARD-9503");
        var terminal = await SeedCardAsync(db, CardStatus.Done, "CARD-9504");
        var decision = await SeedCardAsync(db, CardStatus.NeedsDecision, "CARD-9505");
        var archived = await SeedCardAsync(db, CardStatus.Review, "CARD-9506", archived: true);
        var done = DateTime.UtcNow.AddHours(-1);

        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Failed,
            title: "failed plan", cardId: failed.Id, completedAt: done, createdAt: done.AddHours(-1),
            deliverablePath: "docs/superpowers/plans/failed.md");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "missing path", cardId: missing.Id, completedAt: done, createdAt: done.AddHours(-1));
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "wrong path", cardId: wrong.Id, completedAt: done, createdAt: done.AddHours(-1),
            deliverablePath: "docs/other.md");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "done card", cardId: terminal.Id, completedAt: done, createdAt: done.AddHours(-1),
            deliverablePath: "docs/superpowers/plans/done.md");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "decision card", cardId: decision.Id, completedAt: done, createdAt: done.AddHours(-1),
            deliverablePath: "docs/superpowers/plans/decision.md");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "archived card", cardId: archived.Id, completedAt: done, createdAt: done.AddHours(-1),
            deliverablePath: "docs/superpowers/plans/archived.md");

        (await CreateService(db).GetAsync(CancellationToken.None))
            .Stages.Single(s => s.Role == AgentTaskRole.Code).Ready.ShouldBeEmpty();
    }

    [Test]
    public async Task collections_sort_by_created_then_id_and_ready_by_since_then_identifier()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var t0 = DateTime.UtcNow.AddHours(-3);
        var older = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Review, AgentTaskStatus.Working,
            dispatchedAt: t0, createdAt: t0, title: "older");
        var newer = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Review, AgentTaskStatus.Working,
            dispatchedAt: t0.AddHours(1), createdAt: t0.AddHours(1), title: "newer");
        var cardB = await SeedCardAsync(db, CardStatus.Review, "CARD-9602");
        var cardA = await SeedCardAsync(db, CardStatus.Review, "CARD-9601");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "plan b", cardId: cardB.Id, completedAt: t0.AddMinutes(10), createdAt: t0,
            deliverablePath: "docs/superpowers/plans/b.md");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "plan a", cardId: cardA.Id, completedAt: t0.AddMinutes(10), createdAt: t0.AddMinutes(1),
            deliverablePath: "docs/superpowers/plans/a.md");

        var dto = await CreateService(db).GetAsync(CancellationToken.None);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Review).InFlight.Select(t => t.TaskId)
            .ShouldBe([older.Id, newer.Id]);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Code).Ready.Select(r => r.Card.Identifier)
            .ShouldBe(["CARD-9601", "CARD-9602"]);
    }

    [Test]
    public async Task rows_carry_agent_kind_model_level_and_workspace()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var flying = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Working,
            title: "grok execute", dispatchedAt: DateTime.UtcNow.AddMinutes(-4),
            agentKind: AgentKind.Grok, modelLevel: AgentModelLevel.Frontier,
            workspace: WorkspaceMode.Worktree);
        var waiting = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Docs, AgentTaskStatus.Queued,
            title: "codex docs", agentKind: AgentKind.Codex, modelLevel: AgentModelLevel.High,
            workspace: WorkspaceMode.Shared, repoPath: workspace.Path);
        var blocked = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Deploy, AgentTaskStatus.Blocked,
            title: "claude deploy", agentKind: AgentKind.ClaudeCode, modelLevel: AgentModelLevel.Medium);

        var dto = await CreateService(db).GetAsync(CancellationToken.None);
        var inFlight = dto.Stages.Single(s => s.Role == AgentTaskRole.Code).InFlight
            .Single(t => t.TaskId == flying.Id);
        inFlight.AgentKind.ShouldBe(AgentKind.Grok);
        inFlight.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        inFlight.Workspace.ShouldBe(WorkspaceMode.Worktree);

        var queued = dto.Stages.Single(s => s.Role == AgentTaskRole.Docs).Queued
            .Single(t => t.TaskId == waiting.Id);
        queued.AgentKind.ShouldBe(AgentKind.Codex);
        queued.ModelLevel.ShouldBe(AgentModelLevel.High);
        queued.Workspace.ShouldBe(WorkspaceMode.Shared);

        var blockedRow = dto.Stages.Single(s => s.Role == AgentTaskRole.Deploy).Blocked
            .Single(t => t.TaskId == blocked.Id);
        blockedRow.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        blockedRow.ModelLevel.ShouldBe(AgentModelLevel.Medium);
    }

    [Test]
    public async Task a_sibling_land_in_flight_is_the_queued_reason_after_the_lease()
    {
        // CARD-0331: the dispatcher holds on AgentTasks.LandRequestedAt, not the latest
        // LandRequested / Landed / LandRefused event. The projection matches that column.
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var card = await SeedCardAsync(db, CardStatus.InProgress, "CARD-0301");
        var sibling = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "plan sibling landing", cardId: card.Id, workspace: WorkspaceMode.Worktree,
            repoPath: workspace.Path, worktreeBranch: "card-0301-plan",
            landRequestedAt: DateTime.UtcNow.AddMinutes(-2), completedAt: DateTime.UtcNow.AddMinutes(-5));
        var waiting = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Queued,
            title: "execute behind land", cardId: card.Id, workspace: WorkspaceMode.Worktree,
            repoPath: workspace.Path);
        db.RoutingPins.Add(new RoutingPin
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            Role = AgentTaskRole.Code,
            Provenance = RoutingPinProvenance.Human,
            Strength = RoutingPinStrength.Required,
            AgentKind = AgentKind.Grok,
            NotBefore = DateTime.UtcNow.AddHours(6),
            Reason = "dated pin must lose to sibling land",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var code = (await CreateService(db).GetAsync(CancellationToken.None))
            .Stages.Single(s => s.Role == AgentTaskRole.Code);
        var row = code.Queued.Single(t => t.TaskId == waiting.Id);
        row.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonSiblingLandInFlight);
        row.HeldBy.Select(h => h.TaskId).ShouldBe([sibling.Id]);
        row.HeldBy.Single().Title.ShouldBe("plan sibling landing");

        sibling.LandRequestedAt = null;
        await db.SaveChangesAsync();

        var afterLand = (await CreateService(db).GetAsync(CancellationToken.None))
            .Stages.Single(s => s.Role == AgentTaskRole.Code).Queued.Single(t => t.TaskId == waiting.Id);
        afterLand.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonRoutingPinNotBefore);
        afterLand.HeldBy.ShouldBeEmpty();

        using var heldDir = new TempWorkspace();
        var holder = await SeedTaskAsync(db, heldDir.Path, AgentTaskRole.Docs, AgentTaskStatus.Working,
            dispatchedAt: DateTime.UtcNow.AddMinutes(-2), title: "shared holder",
            workspace: WorkspaceMode.Shared, repoPath: heldDir.Path);
        var sharedWaiting = await SeedTaskAsync(db, heldDir.Path, AgentTaskRole.Docs, AgentTaskStatus.Queued,
            title: "shared waiting on lease", cardId: card.Id, workspace: WorkspaceMode.Shared,
            repoPath: heldDir.Path);
        sibling.LandRequestedAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var leaseRow = (await CreateService(db).GetAsync(CancellationToken.None))
            .Stages.Single(s => s.Role == AgentTaskRole.Docs).Queued.Single(t => t.TaskId == sharedWaiting.Id);
        leaseRow.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonSharedCheckoutLease);
        leaseRow.HeldBy.Select(h => h.TaskId).ShouldBe([holder.Id]);
    }

    [Test]
    public async Task a_shared_task_never_reports_a_sibling_land()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var card = await SeedCardAsync(db, CardStatus.InProgress, "CARD-0215");
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Plan, AgentTaskStatus.Succeeded,
            title: "landing sibling", cardId: card.Id, workspace: WorkspaceMode.Worktree,
            repoPath: workspace.Path, worktreeBranch: "card-0215-plan",
            landRequestedAt: DateTime.UtcNow.AddMinutes(-2), completedAt: DateTime.UtcNow.AddMinutes(-5));
        var shared = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Queued,
            title: "shared execute", cardId: card.Id, workspace: WorkspaceMode.Shared,
            repoPath: workspace.Path);

        var row = (await CreateService(db).GetAsync(CancellationToken.None))
            .Stages.Single(s => s.Role == AgentTaskRole.Code).Queued.Single(t => t.TaskId == shared.Id);
        row.QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonAwaitingDispatch);
        row.HeldBy.ShouldBeEmpty();
    }

    [Test]
    public void verified_plan_deliverable_accepts_only_the_plans_folder()
    {
        AgentTaskPipelineStatusService.IsVerifiedPlanDeliverable(
            "docs/superpowers/plans/2026-09-01-card-0304.md").ShouldBeTrue();
        AgentTaskPipelineStatusService.IsVerifiedPlanDeliverable(
            @"docs\superpowers\plans\x.md").ShouldBeTrue();
        AgentTaskPipelineStatusService.IsVerifiedPlanDeliverable("docs/other.md").ShouldBeFalse();
        AgentTaskPipelineStatusService.IsVerifiedPlanDeliverable("docs/superpowers/plans.md").ShouldBeFalse();
        AgentTaskPipelineStatusService.IsVerifiedPlanDeliverable("docs/superpowers/plans/.md").ShouldBeFalse();
        AgentTaskPipelineStatusService.IsVerifiedPlanDeliverable(null).ShouldBeFalse();
    }

    private static AgentTaskPipelineStatusService CreateService(
        AppDbContext db, DelegationSettings? settings = null)
    {
        var resolved = settings ?? new DelegationSettings();
        var options = Options.Create(resolved);
        return new AgentTaskPipelineStatusService(
            db,
            options,
            new AreaMapLoader(options, NullLogger<AreaMapLoader>.Instance),
            TimeProvider.System);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static DateTime? Truncate(DateTime? value) =>
        value is DateTime utc
            ? DateTime.SpecifyKind(new DateTime(utc.Ticks / 10 * 10, DateTimeKind.Utc), DateTimeKind.Utc)
            : null;

    private static async Task<AgentTask> SeedTaskAsync(
        AppDbContext db,
        string directory,
        AgentTaskRole role,
        AgentTaskStatus status,
        string title,
        DateTime? createdAt = null,
        DateTime? dispatchedAt = null,
        DateTime? completedAt = null,
        Guid? cardId = null,
        Guid? sessionId = null,
        WorkspaceMode workspace = WorkspaceMode.Shared,
        string? repoPath = null,
        string? deliverablePath = null,
        string? scope = null,
        AgentKind agentKind = AgentKind.ClaudeCode,
        AgentModelLevel modelLevel = AgentModelLevel.High,
        string? worktreeBranch = null,
        DateTime? landRequestedAt = null)
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = role,
            Status = status,
            Workspace = workspace,
            WorkingDirectory = directory,
            RepoPath = repoPath,
            Scope = scope,
            CardId = cardId,
            AgentSessionId = sessionId,
            AgentKind = agentKind,
            ModelLevel = modelLevel,
            WorktreeBranch = worktreeBranch,
            LandRequestedAt = Truncate(landRequestedAt),
            AgentName = status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working ? "delegate" : null,
            DispatchedAt = Truncate(dispatchedAt),
            CompletedAt = Truncate(completedAt),
            DeliverablePath = deliverablePath,
            CreatedAt = Truncate(createdAt ?? now)!.Value,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task SeedSessionAsync(AppDbContext db, Guid sessionId, string cwd)
    {
        var now = DateTime.UtcNow;
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Card> SeedCardAsync(
        AppDbContext db, CardStatus status, string identifier, bool archived = false)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"pipe-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/pipe.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var board = new Board
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Name = $"Pipe {identifier}",
            MaxConcurrentSessions = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            StateKey = status.ToString().ToLowerInvariant(),
            Name = status.ToString(),
            ColumnOrder = 0,
            CardStatus = status,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var card = new Card
        {
            Id = Guid.NewGuid(),
            BoardId = board.Id,
            BoardColumnId = column.Id,
            Identifier = identifier,
            Title = $"{identifier} title",
            Description = "Pipeline test.",
            Status = status,
            ArchivedAt = archived ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(project, board, column, card);
        await db.SaveChangesAsync();
        return card;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-pipe").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}

/// <summary>
/// HTTP contract for <c>GET /api/agent-tasks/pipeline</c>. Shares the session factory, so it is
/// <c>[NotInParallel]</c> like every other Program-host consumer.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
public class AgentTaskPipelineEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    private readonly AntiphonWebAppFactory _factory;

    public AgentTaskPipelineEndpointTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task pipeline_route_is_literal_and_returns_the_advisory_contract()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/agent-tasks/pipeline");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.ShouldContain("\"asOf\"");
        json.ShouldContain("\"recommendationsAreAdvisory\"");
        json.ShouldContain("\"maxConcurrentTasks\"");
        json.ShouldContain("\"inFlightAgainstCap\"");
        json.ShouldContain("\"stages\"");
        json.ShouldContain("\"recommendedInFlight\"");
        json.ShouldContain("\"inFlightCount\"");
        json.ShouldContain("\"atOrAboveRecommendation\"");
        json.ShouldNotContain("\"AsOf\"", Case.Sensitive);

        var dto = JsonSerializer.Deserialize<AgentTaskPipelineDto>(json, Json);
        dto.ShouldNotBeNull();
        dto.RecommendationsAreAdvisory.ShouldBeTrue();
        dto.MaxConcurrentTasks.ShouldBe(6);
        dto.InFlightAgainstCap.ShouldBe(0);
        dto.Stages.Count.ShouldBe(13);
        dto.Stages.ShouldNotContain(s => s.Role == AgentTaskRole.Check);
        dto.Stages.ShouldNotContain(s => s.Role == AgentTaskRole.Distill);
        dto.Stages.ShouldNotContain(s => s.Role == AgentTaskRole.Diagnose);
        dto.Stages.ShouldContain(s => s.Role == AgentTaskRole.Plan && s.RecommendedInFlight == 1);
        foreach (var stage in dto.Stages)
        {
            stage.InFlight.ShouldBeEmpty();
            stage.Queued.ShouldBeEmpty();
            stage.Blocked.ShouldBeEmpty();
            stage.Ready.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task pipeline_json_includes_agent_kind_on_a_row()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "kind row",
            Goal = "kind row",
            Role = AgentTaskRole.Docs,
            Status = AgentTaskStatus.Working,
            AgentKind = AgentKind.Grok,
            ModelLevel = AgentModelLevel.Frontier,
            Workspace = WorkspaceMode.Worktree,
            WorkingDirectory = @"C:\tmp\pipe-kind",
            AgentName = "kind-row",
            DispatchedAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-2),
        });
        await db.SaveChangesAsync();

        using var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/api/agent-tasks/pipeline");
        json.ShouldContain("\"agentKind\"");
        json.ShouldContain("\"Grok\"");
        json.ShouldContain("\"modelLevel\"");
        json.ShouldContain("\"workspace\"");
    }

    [Test]
    public async Task existing_agent_task_routes_are_unchanged()
    {
        using var client = _factory.CreateClient();
        (await client.GetAsync("/api/agent-tasks/summary")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/agent-tasks")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
