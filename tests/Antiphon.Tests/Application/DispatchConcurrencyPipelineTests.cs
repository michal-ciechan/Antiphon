using System.Data.Common;
using System.Net;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0505 V-7. Scoped pipeline projection over the durable dispatch policy.</summary>
[Category("Integration")]
[NotInParallel("card-0505-advisory-lock")]
[ParallelLimiter<ProcessSpawnLimit>]
public class DispatchConcurrencyPipelineTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Scoped_pipeline_matches_create_and_dispatch_policy()
    {
        await using var world = await ConcurrencyDispatchWorld.Open();
        await world.InitializeAsync();
        var shop = world.Shop;
        var project = await shop.PutProjectAsync(
            shop.ProjectP, 0, 1,
            """
            {"mode":"SeparateQueues","maxParallel":4,"maxQueued":1,"roles":{"Code":{"maxParallel":1,"maxQueued":1}}}
            """,
            "scope P");
        var working = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Working, shop.ProjectP, WorkspaceMode.ReadOnly, title: "p-working-code");
        var queued = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Queued, shop.ProjectP, WorkspaceMode.ReadOnly, title: "p-queued-code");
        var retained = await world.InsertAsync(
            AgentTaskRole.Plan, AgentTaskStatus.Working, shop.ProjectP, WorkspaceMode.ReadOnly, retained: true, title: "p-retained-plan");
        var specialist = await world.InsertAsync(
            AgentTaskRole.Check, AgentTaskStatus.Working, shop.ProjectP, WorkspaceMode.ReadOnly, title: "p-check");
        var blocked = await world.InsertAsync(
            AgentTaskRole.Review, AgentTaskStatus.Blocked, shop.ProjectP, WorkspaceMode.ReadOnly, title: "p-blocked-review");
        var foreign = await world.InsertAsync(
            AgentTaskRole.Code, AgentTaskStatus.Working, shop.ProjectQ, WorkspaceMode.ReadOnly, title: "q-working-code");
        var unscoped = await world.InsertAsync(
            AgentTaskRole.Plan, AgentTaskStatus.Working, null, WorkspaceMode.ReadOnly, title: "null-working-plan");
        var readyP = await SeedReadyAsync(shop, shop.ProjectP, "CARD-P-READY");
        var readyQ = await SeedReadyAsync(shop, shop.ProjectQ, "CARD-Q-READY");
        var backlogP = await SeedBacklogAsync(shop, shop.ProjectP, "CARD-P-BACKLOG");
        var backlogQ = await SeedBacklogAsync(shop, shop.ProjectQ, "CARD-Q-BACKLOG");
        await BindCardAsync(shop, readyP.TaskId, readyP.CardId, succeededPlan: true);
        await BindCardAsync(shop, readyQ.TaskId, readyQ.CardId, succeededPlan: true);

        await using var db = shop.Db();
        var pipeline = Pipeline(shop, db);
        var dto = await pipeline.GetAsync(new PipelineScope(PipelineScopeKind.Project, shop.ProjectP), CancellationToken.None);
        dto.TaskScope.ShouldBe("project", "scoped-policy-matches");
        var scope = dto.ConcurrencyScopes.ShouldHaveSingleItem("scoped-policy-matches");
        scope.ProjectId.ShouldBe(shop.ProjectP, "scoped-policy-matches");
        scope.NullBucket.ShouldBeFalse();
        scope.Effective.Mode.ShouldBe("SeparateQueues", "scoped-policy-matches");
        scope.Effective.ModeSource.ShouldBe(DispatchConcurrencyPolicy.SourceProject, "scoped-policy-matches");
        scope.Effective.GlobalRevision.ShouldBe(project.GlobalRevision, "scoped-policy-matches");
        scope.Effective.ProjectRevision.ShouldBe(project.Revision, "scoped-policy-matches");
        var codeLimit = scope.Effective.Roles.Single(role => role.Role == "Code");
        codeLimit.MaxParallel.ShouldBe(1, "scoped-policy-matches");
        codeLimit.MaxParallelSource.ShouldBe(DispatchConcurrencyPolicy.SourceProject, "scoped-policy-matches");
        codeLimit.MaxQueued.ShouldBe(1, "scoped-policy-matches");
        scope.Occupancy.Open.ShouldBe(3, "scoped-policy-matches");
        scope.Occupancy.Parallel.ShouldBe(2, "scoped-policy-matches");
        scope.Occupancy.Queued.ShouldBe(1, "scoped-policy-matches");
        scope.Occupancy.ParallelRemaining.ShouldBe(2, "scoped-policy-matches");
        scope.Occupancy.QueuedRemaining.ShouldBe(0, "scoped-policy-matches");
        scope.Occupancy.ParallelOverage.ShouldBe(0, "scoped-policy-matches");
        scope.Occupancy.QueuedOverage.ShouldBe(0, "scoped-policy-matches");
        var codeCounts = scope.Occupancy.Roles.Single(role => role.Role == "Code");
        codeCounts.Parallel.ShouldBe(1, "scoped-policy-matches");
        codeCounts.Queued.ShouldBe(1, "scoped-policy-matches");
        codeCounts.Open.ShouldBe(2, "scoped-policy-matches");

        var ids = StageIds(dto);
        ids.ShouldContain(working.Id, "scoped-policy-matches");
        ids.ShouldContain(queued.Id, "scoped-policy-matches");
        ids.ShouldContain(retained.Id, "scoped-policy-matches");
        ids.ShouldContain(blocked.Id, "scoped-policy-matches");
        ids.ShouldNotContain(specialist.Id, "scoped-policy-matches");
        ids.ShouldNotContain(foreign.Id, "scoped-policy-matches");
        ids.ShouldNotContain(unscoped.Id, "scoped-policy-matches");
        var code = dto.Stages.Single(stage => stage.Role == AgentTaskRole.Code);
        code.RecommendedInFlight.ShouldBe(1, "scoped-policy-matches");
        code.InFlight.Select(row => row.TaskId).ShouldBe([working.Id], "scoped-policy-matches");
        code.Queued.Select(row => row.TaskId).ShouldBe([queued.Id], "scoped-policy-matches");
        code.Queued.Single().QueueReason.ShouldBe(
            AgentTaskPipelineStatusService.QueueReasonRoleParallelLimit, "scoped-policy-matches");
        code.Queued.Single().HeldBy.Select(holder => holder.TaskId).ShouldBe([working.Id], "scoped-policy-matches");
        code.Ready.Select(row => row.Card.Identifier).ShouldBe(["CARD-P-READY"], "scoped-policy-matches");
        code.Ready.ShouldNotContain(row => row.Card.Identifier == "CARD-Q-READY", "scoped-policy-matches");
        dto.InvestigateBacklog.Items.Select(item => item.Identifier).ShouldBe(["CARD-P-BACKLOG"], "scoped-policy-matches");
        dto.InvestigateBacklog.Items.ShouldNotContain(item => item.Identifier == "CARD-Q-BACKLOG", "scoped-policy-matches");

        var queuedQ = await pipeline.GetAsync(new PipelineScope(PipelineScopeKind.Project, shop.ProjectQ), CancellationToken.None);
        queuedQ.ConcurrencyScopes.Single().Effective.Mode.ShouldBe("LegacyOpen", "scoped-policy-matches");
        queuedQ.ConcurrencyScopes.Single().Occupancy.Parallel.ShouldBe(1, "scoped-policy-matches");
        StageIds(queuedQ).OrderBy(id => id).ShouldBe(new[] { foreign.Id }.OrderBy(id => id), "scoped-policy-matches");
        var nullScope = await pipeline.GetAsync(PipelineScope.NullBucket, CancellationToken.None);
        nullScope.TaskScope.ShouldBe("null", "scoped-policy-matches");
        nullScope.ConcurrencyScopes.Single().NullBucket.ShouldBeTrue();
        nullScope.ConcurrencyScopes.Single().Effective.Mode.ShouldBe("LegacyOpen", "scoped-policy-matches");
        StageIds(nullScope).OrderBy(id => id).ShouldBe(new[] { unscoped.Id }.OrderBy(id => id), "scoped-policy-matches");

        world.Detach();
        var goal = $"c0505-refuse-{Guid.NewGuid():N}";
        var refused = await Should.ThrowAsync<ConcurrencyLimitException>(() => world.Tasks.CreateAsync(
            new CreateAgentTaskRequest(
                goal, Role: AgentTaskRole.Code, WorkingDirectory: world.Directory, Workspace: WorkspaceMode.ReadOnly),
            new AgentTaskService.Caller(null, null, world.Directory, ProjectId: shop.ProjectP),
            CancellationToken.None));
        world.Detach();
        refused.Code.ShouldBe("concurrency_limit", "scoped-policy-matches");
        refused.Concurrency.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationQueued, "scoped-policy-matches");
        refused.Concurrency.ProjectId.ShouldBe(shop.ProjectP, "scoped-policy-matches");
        await using (var verify = shop.Db())
        {
            (await verify.AgentTasks.CountAsync(task => task.Goal == goal)).ShouldBe(0, "scoped-policy-matches");
        }

        var tick = await world.TickAsync();
        tick.Dispatched.ShouldBe(0, "scoped-policy-matches");
        world.Launches.Items.Count.ShouldBe(0, "scoped-policy-matches");
        await using var after = shop.Db();
        (await after.AgentTasks.AsNoTracking().Where(task => task.Id == queued.Id).Select(task => task.Status).SingleAsync())
            .ShouldBe(AgentTaskStatus.Queued, "scoped-policy-matches");
        backlogP.ShouldNotBe(Guid.Empty);
        readyQ.CardId.ShouldNotBe(Guid.Empty);
        backlogQ.ShouldNotBe(Guid.Empty);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Empty_project_and_unscoped_have_correct_limits()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await InitAsync(shop);
        var empty = await SeedProjectAsync(shop, "R");
        await shop.PutProjectAsync(
            shop.ProjectP, 0, 1,
            """{"mode":"SeparateQueues","maxParallel":3,"roles":{"Code":{"maxParallel":2}}}""",
            "represented");
        var pTask = await SeedTaskAsync(shop, AgentTaskRole.Code, AgentTaskStatus.Working, shop.ProjectP);
        var nullTask = await SeedTaskAsync(shop, AgentTaskRole.Plan, AgentTaskStatus.Queued, null);
        await SeedBacklogAsync(shop, shop.ProjectP, "CARD-FLEET-P");
        var before = await CensusAsync(shop);

        await using var host = await DispatchConcurrencyWireHost.Start(shop);
        var emptyResponse = await host.GetAsync($"/api/agent-tasks/pipeline?projectId={empty:D}");
        emptyResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var emptyJson = JsonDocument.Parse(await emptyResponse.Content.ReadAsStringAsync());
        emptyJson.RootElement.GetProperty("taskScope").GetString().ShouldBe("project");
        var emptyScope = emptyJson.RootElement.GetProperty("concurrencyScopes").EnumerateArray().Single();
        emptyScope.GetProperty("projectId").GetGuid().ShouldBe(empty);
        emptyScope.GetProperty("occupancy").GetProperty("open").GetInt32().ShouldBe(0);
        emptyScope.GetProperty("occupancy").GetProperty("parallel").GetInt32().ShouldBe(0);
        emptyScope.GetProperty("occupancy").GetProperty("queued").GetInt32().ShouldBe(0);
        emptyScope.GetProperty("effective").GetProperty("mode").GetString().ShouldBe("LegacyOpen");
        EmptyArrays(emptyJson.RootElement);

        var unscopedResponse = await host.GetAsync("/api/agent-tasks/pipeline?unscoped=true");
        unscopedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var unscopedJson = JsonDocument.Parse(await unscopedResponse.Content.ReadAsStringAsync());
        unscopedJson.RootElement.GetProperty("taskScope").GetString().ShouldBe("null");
        var nullScope = unscopedJson.RootElement.GetProperty("concurrencyScopes").EnumerateArray().Single();
        nullScope.GetProperty("nullBucket").GetBoolean().ShouldBeTrue();
        nullScope.GetProperty("projectId").ValueKind.ShouldBe(JsonValueKind.Null);
        StageTaskIds(unscopedJson.RootElement).ShouldBe([nullTask]);
        unscopedJson.RootElement.GetProperty("investigateBacklog").GetProperty("total").GetInt32().ShouldBe(0);
        foreach (var stage in unscopedJson.RootElement.GetProperty("stages").EnumerateArray())
            stage.GetProperty("ready").GetArrayLength().ShouldBe(0);

        var fleetResponse = await host.GetAsync("/api/agent-tasks/pipeline");
        fleetResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var fleetJson = JsonDocument.Parse(await fleetResponse.Content.ReadAsStringAsync());
        var fleetIds = fleetJson.RootElement.GetProperty("concurrencyScopes").EnumerateArray()
            .Select(scope => scope.GetProperty("projectId").ValueKind == JsonValueKind.Null
                ? (Guid?)null
                : scope.GetProperty("projectId").GetGuid())
            .ToList();
        fleetIds.ShouldContain((Guid?)null);
        fleetIds.ShouldContain(shop.ProjectP);
        fleetIds.ShouldNotContain(empty);
        StageTaskIds(fleetJson.RootElement).ShouldContain(pTask);
        StageTaskIds(fleetJson.RootElement).ShouldContain(nullTask);

        var unknown = await host.GetAsync($"/api/agent-tasks/pipeline?projectId={Guid.NewGuid():D}");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain("not_found");

        var malformed = await host.GetAsync("/api/agent-tasks/pipeline?projectId=not-a-guid");
        malformed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await malformed.Content.ReadAsStringAsync()).ShouldContain("validation_failed");

        var badUnscoped = await host.GetAsync("/api/agent-tasks/pipeline?unscoped=false");
        badUnscoped.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await badUnscoped.Content.ReadAsStringAsync()).ShouldContain("validation_failed");

        var both = await host.GetAsync($"/api/agent-tasks/pipeline?projectId={empty:D}&unscoped=true");
        both.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await both.Content.ReadAsStringAsync()).ShouldContain("validation_failed");

        var after = await CensusAsync(shop);
        after.ShouldBe(before);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Fleet_contract_and_host_totals_remain_distinct()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await InitAsync(shop);
        var global = await shop.PutGlobalAsync(1, """{"roles":{"Code":{"maxParallel":4}}}""", "fleet code 4");
        await shop.PutProjectAsync(
            shop.ProjectP, 0, global.Revision,
            """{"roles":{"Code":{"maxParallel":1}}}""",
            "p code 1");
        await SeedTaskAsync(shop, AgentTaskRole.Code, AgentTaskStatus.Working, shop.ProjectP);
        await SeedTaskAsync(shop, AgentTaskRole.Code, AgentTaskStatus.Working, shop.ProjectQ, runnerId: "server2");
        await SeedSessionAsync(shop, "server2");
        await using var db = shop.Db();
        db.HostBudgets.Add(new HostBudget
        {
            HostId = "server2",
            MaxInFlight = 3,
            Reason = "remote cap",
            UpdatedAt = shop.Clock.GetUtcNow().UtcDateTime,
            Revision = 1,
        });
        await db.SaveChangesAsync();
        var budgets = new HostBudgetService(db, new FleetDirectory(), Options.Create(shop.Settings), shop.Clock);
        var pipeline = Pipeline(shop, db, budgets);

        var fleet = await pipeline.GetAsync(CancellationToken.None);
        fleet.RecommendationsAreAdvisory.ShouldBeTrue();
        fleet.TaskScope.ShouldBe("fleet");
        fleet.HostSummaryScope.ShouldBe("fleet");
        fleet.MaxConcurrentTasks.ShouldBe(2);
        fleet.InFlightAgainstCap.ShouldBe(1);
        fleet.Stages.Count.ShouldBe(14);
        var fleetCode = fleet.Stages.Single(stage => stage.Role == AgentTaskRole.Code);
        fleetCode.RecommendedInFlight.ShouldBe(4);
        fleetCode.AtOrAboveRecommendation.ShouldBeFalse();
        fleetCode.InFlightCount.ShouldBe(2);
        var pScope = fleet.ConcurrencyScopes.Single(scope => scope.ProjectId == shop.ProjectP);
        pScope.Occupancy.Parallel.ShouldBe(1);
        pScope.Effective.Roles.Single(role => role.Role == "Code").MaxParallel.ShouldBe(1);
        var qScope = fleet.ConcurrencyScopes.Single(scope => scope.ProjectId == shop.ProjectQ);
        qScope.Occupancy.Parallel.ShouldBe(1);
        qScope.Effective.GlobalRevision.ShouldBe(global.Revision);

        var project = await pipeline.GetAsync(new PipelineScope(PipelineScopeKind.Project, shop.ProjectP), CancellationToken.None);
        var projectCode = project.Stages.Single(stage => stage.Role == AgentTaskRole.Code);
        projectCode.RecommendedInFlight.ShouldBe(1);
        projectCode.InFlightCount.ShouldBe(1);
        projectCode.AtOrAboveRecommendation.ShouldBeTrue();
        var unscoped = await pipeline.GetAsync(PipelineScope.NullBucket, CancellationToken.None);
        SameHosts(fleet, project);
        SameHosts(fleet, unscoped);
        foreach (var host in fleet.Hosts.Concat(project.Hosts).Concat(unscoped.Hosts))
            host.Scope.ShouldBe("fleet");
        var remote = fleet.Hosts.Single(host => host.HostId == "server2");
        remote.InFlight.ShouldBe(1);
        remote.EffectiveLimit.ShouldBe(3);
        var local = fleet.Hosts.Single(host => host.HostId == "local");
        local.InFlight.ShouldBe(1);
        local.EffectiveLimit.ShouldBe(2);
        local.Source.ShouldBe("config");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Queued_hold_and_overage_match_gate_without_writes()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await InitAsync(shop);
        var now = shop.Clock.GetUtcNow().UtcDateTime;
        var project = await shop.PutProjectAsync(
            shop.ProjectP, 0, 1,
            """
            {"mode":"SeparateQueues","maxParallel":4,"maxQueued":1,"roles":{"Code":{"maxParallel":1},"Plan":{"maxParallel":null}}}
            """,
            "holds");
        var roleCard = await SeedBacklogAsync(shop, shop.ProjectP, "CARD-ROLE");
        var landCard = await SeedCardAsync(shop, shop.ProjectP, "CARD-LAND", CardStatus.InProgress);
        var pinCard = await SeedCardAsync(shop, shop.ProjectP, "CARD-PIN", CardStatus.InProgress);
        var working = await SeedTaskAsync(shop, AgentTaskRole.Code, AgentTaskStatus.Working, shop.ProjectP, title: "role holder");
        var roleQueued = await SeedTaskAsync(shop, AgentTaskRole.Code, AgentTaskStatus.Queued, shop.ProjectP, title: "role waiter");
        var projectWorking = await SeedTaskAsync(shop, AgentTaskRole.Review, AgentTaskStatus.Working, shop.ProjectQ, title: "q holder");
        await shop.PutProjectAsync(
            shop.ProjectQ, 0, project.GlobalRevision,
            """{"mode":"SeparateQueues","maxParallel":1,"roles":{"Plan":{"maxParallel":null}}}""",
            "q absolute");
        var projectQueued = await SeedTaskAsync(shop, AgentTaskRole.Plan, AgentTaskStatus.Queued, shop.ProjectQ, title: "q waiter");
        var sibling = await SeedTaskAsync(
            shop, AgentTaskRole.Plan, AgentTaskStatus.Succeeded, shop.ProjectP,
            workspace: WorkspaceMode.Worktree, cardId: landCard, title: "landing sibling");
        var behindLand = await SeedTaskAsync(
            shop, AgentTaskRole.Code, AgentTaskStatus.Queued, shop.ProjectP,
            workspace: WorkspaceMode.Worktree, cardId: landCard, title: "behind land");
        var behindPin = await SeedTaskAsync(
            shop, AgentTaskRole.Review, AgentTaskStatus.Queued, shop.ProjectP, cardId: pinCard, title: "behind pin");
        var leaseDir = Directory.CreateTempSubdirectory("c0505-lease").FullName;
        var holder = await SeedTaskAsync(
            shop, AgentTaskRole.Docs, AgentTaskStatus.Working, shop.ProjectP,
            workspace: WorkspaceMode.Shared, directory: leaseDir, title: "lease holder");
        var behindLease = await SeedTaskAsync(
            shop, AgentTaskRole.Docs, AgentTaskStatus.Queued, shop.ProjectP,
            workspace: WorkspaceMode.Shared, directory: leaseDir, title: "behind lease");
        await using (var db = shop.Db())
        {
            var land = await db.AgentTasks.SingleAsync(task => task.Id == sibling);
            land.WorktreeBranch = "card-land";
            land.LandRequestedAt = now.AddMinutes(-2);
            land.CompletedAt = now.AddMinutes(-5);
            db.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(),
                CardId = pinCard,
                Role = AgentTaskRole.Review,
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                AgentKind = AgentKind.Grok,
                NotBefore = now.AddHours(6),
                Reason = "dated pin outranks the parallel cap",
                CreatedAt = now,
                UpdatedAt = now,
            });
            db.RoutingPins.Add(new RoutingPin
            {
                Id = Guid.NewGuid(),
                Role = AgentTaskRole.Deploy,
                Provenance = RoutingPinProvenance.Human,
                Strength = RoutingPinStrength.Required,
                AgentKind = AgentKind.ClaudeCode,
                NotAfter = now.AddHours(-2),
                Reason = "expired pin stays stored",
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        await using var read = shop.Db();
        var pipeline = Pipeline(shop, read);
        var before = await CensusAsync(shop);
        var dto = await pipeline.GetAsync(new PipelineScope(PipelineScopeKind.Project, shop.ProjectP), CancellationToken.None);
        var fleet = await pipeline.GetAsync(CancellationToken.None);
        var after = await CensusAsync(shop);
        after.ShouldBe(before, "pipeline-readonly");
        roleCard.ShouldNotBe(Guid.Empty);

        var p = dto.ConcurrencyScopes.Single();
        p.Effective.GlobalRevision.ShouldBe(project.GlobalRevision);
        p.Effective.ProjectRevision.ShouldBe(project.Revision);
        p.Occupancy.ParallelOverage.ShouldBe(0);
        p.Occupancy.QueuedOverage.ShouldBeGreaterThan(0);
        p.Occupancy.ParallelRemaining.ShouldBe(2);
        p.Occupancy.QueuedRemaining.ShouldBe(0);
        var above = await shop.PutProjectAsync(
            shop.ProjectP, project.Revision, project.GlobalRevision,
            """{"mode":"SeparateQueues","maxParallel":1,"maxQueued":0,"roles":{"Code":{"maxParallel":1}}}""",
            "above");
        var raised = await pipeline.GetAsync(new PipelineScope(PipelineScopeKind.Project, shop.ProjectP), CancellationToken.None);
        var raisedScope = raised.ConcurrencyScopes.Single();
        raisedScope.Occupancy.ParallelOverage.ShouldBeGreaterThan(0);
        raisedScope.Occupancy.QueuedOverage.ShouldBeGreaterThan(0);
        raisedScope.Occupancy.ParallelRemaining.ShouldBe(0);
        raisedScope.Occupancy.QueuedRemaining.ShouldBe(0);
        raisedScope.Effective.ProjectRevision.ShouldBe(above.Revision);

        var belowShop = await DispatchConcurrencyShop.Open();
        try
        {
            await InitAsync(belowShop);
            await belowShop.PutProjectAsync(
                belowShop.ProjectP, 0, 1,
                """{"mode":"SeparateQueues","maxParallel":4,"maxQueued":3}""",
                "below");
            await using var belowDb = belowShop.Db();
            var below = await Pipeline(belowShop, belowDb).GetAsync(
                new PipelineScope(PipelineScopeKind.Project, belowShop.ProjectP), CancellationToken.None);
            var room = below.ConcurrencyScopes.Single().Occupancy;
            room.ParallelRemaining.ShouldBe(4);
            room.QueuedRemaining.ShouldBe(3);
            room.ParallelOverage.ShouldBe(0);
            room.QueuedOverage.ShouldBe(0);
        }
        finally
        {
            await belowShop.DisposeAsync();
        }

        Queued(dto, AgentTaskRole.Code, roleQueued).QueueReason.ShouldBe(
            AgentTaskPipelineStatusService.QueueReasonRoleParallelLimit);
        Queued(dto, AgentTaskRole.Code, roleQueued).HeldBy.Select(row => row.TaskId).ShouldBe([working]);
        var q = await pipeline.GetAsync(new PipelineScope(PipelineScopeKind.Project, shop.ProjectQ), CancellationToken.None);
        Queued(q, AgentTaskRole.Plan, projectQueued).QueueReason.ShouldBe(
            AgentTaskPipelineStatusService.QueueReasonProjectParallelLimit);
        Queued(q, AgentTaskRole.Plan, projectQueued).HeldBy.Select(row => row.TaskId).ShouldBe([projectWorking]);
        q.ConcurrencyScopes.Single().Effective.ProjectRevision.ShouldBeGreaterThan(0);
        Queued(dto, AgentTaskRole.Code, behindLand).QueueReason.ShouldBe(
            AgentTaskPipelineStatusService.QueueReasonSiblingLandInFlight);
        Queued(dto, AgentTaskRole.Code, behindLand).HeldBy.Select(row => row.TaskId).ShouldBe([sibling]);
        Queued(dto, AgentTaskRole.Review, behindPin).QueueReason.ShouldBe(
            AgentTaskPipelineStatusService.QueueReasonRoutingPinNotBefore);
        Queued(dto, AgentTaskRole.Docs, behindLease).QueueReason.ShouldBe(
            AgentTaskPipelineStatusService.QueueReasonSharedCheckoutLease);
        Queued(dto, AgentTaskRole.Docs, behindLease).HeldBy.Select(row => row.TaskId).ShouldBe([holder]);
        dto.Stages.Single(stage => stage.Role == AgentTaskRole.Deploy).RoutingPin.ShouldBeNull();
        fleet.Stages.Single(stage => stage.Role == AgentTaskRole.Deploy).RoutingPin.ShouldBeNull();
        try { Directory.Delete(leaseDir, recursive: true); } catch (IOException) { }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Effective_policy_changes_after_put_without_restart()
    {
        await using var shop = await DispatchConcurrencyShop.Open();
        await InitAsync(shop);
        await SeedTaskAsync(shop, AgentTaskRole.Code, AgentTaskStatus.Working, shop.ProjectP, title: "live code");
        var ready = await SeedReadyAsync(shop, shop.ProjectP, "CARD-LIVE");
        await BindCardAsync(shop, ready.TaskId, ready.CardId, succeededPlan: true);
        var pause = new PauseFirstSettingsReadInterceptor();
        await using var db = shop.Db(pause);
        var pipeline = Pipeline(shop, db);
        await using var host = await DispatchConcurrencyWireHost.Start(shop);

        var first = await pipeline.GetAsync(new PipelineScope(PipelineScopeKind.Project, shop.ProjectP), CancellationToken.None);
        var firstCode = first.Stages.Single(stage => stage.Role == AgentTaskRole.Code);
        firstCode.RecommendedInFlight.ShouldBe(5, "pipeline-live-revision");
        firstCode.AtOrAboveRecommendation.ShouldBeFalse("pipeline-live-revision");
        firstCode.Ready.Count.ShouldBe(1, "pipeline-live-revision");
        first.ConcurrencyScopes.Single().Effective.Roles.Single(role => role.Role == "Code")
            .MaxParallelSource.ShouldBe(DispatchConcurrencyPolicy.SourceDefault, "pipeline-live-revision");

        var global = await shop.PutGlobalAsync(1, """{"roles":{"Code":{"maxParallel":4}}}""", "live global");
        var afterGlobal = await pipeline.GetAsync(PipelineScope.Fleet, CancellationToken.None);
        afterGlobal.Stages.Single(stage => stage.Role == AgentTaskRole.Code)
            .RecommendedInFlight.ShouldBe(4, "pipeline-live-revision");
        afterGlobal.ConcurrencyScopes.Single(scope => scope.ProjectId == shop.ProjectP)
            .Effective.GlobalRevision.ShouldBe(global.Revision, "pipeline-live-revision");
        afterGlobal.ConcurrencyScopes.Single(scope => scope.ProjectId == shop.ProjectP)
            .Effective.Roles.Single(role => role.Role == "Code")
            .MaxParallelSource.ShouldBe(DispatchConcurrencyPolicy.SourceGlobal, "pipeline-live-revision");

        var project = await shop.PutProjectAsync(
            shop.ProjectP, 0, global.Revision,
            """{"mode":"SeparateQueues","maxParallel":1,"roles":{"Code":{"maxParallel":4}}}""",
            "live project");
        var afterProject = await pipeline.GetAsync(
            new PipelineScope(PipelineScopeKind.Project, shop.ProjectP), CancellationToken.None);
        var projectCode = afterProject.Stages.Single(stage => stage.Role == AgentTaskRole.Code);
        projectCode.RecommendedInFlight.ShouldBe(4, "pipeline-live-revision");
        projectCode.AtOrAboveRecommendation.ShouldBeTrue("pipeline-live-revision");
        projectCode.Ready.Count.ShouldBe(1, "pipeline-live-revision");
        var effective = afterProject.ConcurrencyScopes.Single().Effective;
        effective.ProjectRevision.ShouldBe(project.Revision, "pipeline-live-revision");
        effective.ModeSource.ShouldBe(DispatchConcurrencyPolicy.SourceProject, "pipeline-live-revision");
        effective.Roles.Single(role => role.Role == "Code").MaxParallelSource
            .ShouldBe(DispatchConcurrencyPolicy.SourceProject, "pipeline-live-revision");

        using var http = JsonDocument.Parse(await (await host.GetAsync(
            $"/api/agent-tasks/pipeline?projectId={shop.ProjectP:D}")).Content.ReadAsStringAsync());
        http.RootElement.GetProperty("stages").EnumerateArray()
            .Single(stage => stage.GetProperty("role").GetString() == "Code")
            .GetProperty("recommendedInFlight").GetInt32().ShouldBe(4, "pipeline-live-revision");
        http.RootElement.GetProperty("stages").EnumerateArray()
            .Single(stage => stage.GetProperty("role").GetString() == "Code")
            .GetProperty("atOrAboveRecommendation").GetBoolean().ShouldBeTrue("pipeline-live-revision");

        var cleared = await shop.PutProjectAsync(
            shop.ProjectP, project.Revision, global.Revision, "{}", "live clear");
        var afterClear = await pipeline.GetAsync(
            new PipelineScope(PipelineScopeKind.Project, shop.ProjectP), CancellationToken.None);
        afterClear.Stages.Single(stage => stage.Role == AgentTaskRole.Code)
            .RecommendedInFlight.ShouldBe(4, "pipeline-live-revision");
        afterClear.Stages.Single(stage => stage.Role == AgentTaskRole.Code)
            .AtOrAboveRecommendation.ShouldBeFalse("pipeline-live-revision");
        afterClear.ConcurrencyScopes.Single().Effective.ProjectRevision.ShouldBe(cleared.Revision, "pipeline-live-revision");
        afterClear.ConcurrencyScopes.Single().Effective.ProjectRevision.ShouldBeGreaterThan(0, "pipeline-live-revision");
        afterClear.ConcurrencyScopes.Single().Effective.Roles.Single(role => role.Role == "Code")
            .MaxParallelSource.ShouldBe(DispatchConcurrencyPolicy.SourceGlobal, "pipeline-live-revision");

        var pre = (
            afterClear.ConcurrencyScopes.Single().Effective.GlobalRevision,
            afterClear.ConcurrencyScopes.Single().Effective.ProjectRevision);
        pause.Arm();
        var reading = pipeline.GetAsync(new PipelineScope(PipelineScopeKind.Project, shop.ProjectP), CancellationToken.None);
        await pause.AtRead.Task.WaitAsync(TimeSpan.FromSeconds(20));
        DispatchConcurrencyGlobalDto writtenGlobal;
        DispatchConcurrencyProjectDto writtenProject;
        try
        {
            writtenGlobal = await shop.PutGlobalAsync(pre.Item1, """{"roles":{"Review":{"maxParallel":3}}}""", "barrier global");
            writtenProject = await shop.PutProjectAsync(
                shop.ProjectP, pre.Item2, writtenGlobal.Revision,
                """{"roles":{"Plan":{"maxParallel":2}}}""",
                "barrier project");
        }
        finally
        {
            pause.Release.TrySetResult();
        }

        var coherent = await reading;
        var post = (writtenGlobal.Revision, writtenProject.Revision);
        post.ShouldNotBe(pre, "snapshot-pair-existed");
        var seen = (
            coherent.ConcurrencyScopes.Single().Effective.GlobalRevision,
            coherent.ConcurrencyScopes.Single().Effective.ProjectRevision);
        (seen == pre || seen == post).ShouldBeTrue("snapshot-pair-existed");
        (seen.Item1 == pre.Item1 && seen.Item2 == post.Item2).ShouldBeFalse("snapshot-pair-existed");
        (seen.Item1 == post.Item1 && seen.Item2 == pre.Item2).ShouldBeFalse("snapshot-pair-existed");
    }

    private static AgentTaskPipelineStatusService Pipeline(
        DispatchConcurrencyShop shop, AppDbContext db, HostBudgetService? budgets = null)
    {
        var options = Options.Create(shop.Settings);
        return new AgentTaskPipelineStatusService(
            db,
            options,
            new AreaMapLoader(options, NullLogger<AreaMapLoader>.Instance),
            shop.Clock,
            budgets,
            concurrency: shop.Service(db));
    }

    private static async Task InitAsync(DispatchConcurrencyShop shop)
    {
        await using var db = shop.Db();
        await shop.Service(db).EnsureInitializedAsync(CancellationToken.None);
    }

    private static List<Guid> StageIds(AgentTaskPipelineDto dto) =>
        dto.Stages.SelectMany(stage => stage.InFlight.Select(row => row.TaskId)
            .Concat(stage.Queued.Select(row => row.TaskId))
            .Concat(stage.Blocked.Select(row => row.TaskId)))
            .ToList();

    private static AgentTaskPipelineQueuedDto Queued(AgentTaskPipelineDto dto, AgentTaskRole role, Guid id) =>
        dto.Stages.Single(stage => stage.Role == role).Queued.Single(row => row.TaskId == id);

    private static async Task<Guid> SeedProjectAsync(DispatchConcurrencyShop shop, string name)
    {
        var now = shop.Clock.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        await using var db = shop.Db();
        db.Projects.Add(new Project
        {
            Id = id,
            Name = name,
            GitRepositoryUrl = $"https://example.test/{name}.git",
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> SeedTaskAsync(
        DispatchConcurrencyShop shop,
        AgentTaskRole role,
        AgentTaskStatus status,
        Guid? projectId,
        string? title = null,
        string? runnerId = null,
        WorkspaceMode workspace = WorkspaceMode.ReadOnly,
        Guid? cardId = null,
        string? directory = null)
    {
        var id = Guid.NewGuid();
        var now = shop.Clock.GetUtcNow().UtcDateTime;
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title ?? $"c0505-{role}-{status}",
            Goal = $"c0505-{id:N}",
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Status = status,
            ProjectId = projectId,
            CardId = cardId,
            Workspace = workspace,
            WorkingDirectory = directory ?? Path.GetTempPath(),
            RepoPath = workspace == WorkspaceMode.Shared ? directory : null,
            RunnerId = runnerId,
            CreatedAt = now,
            DispatchedAt = status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working ? now : null,
            ConcurrencyToken = Guid.NewGuid(),
        };
        await using var db = shop.Db();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedSessionAsync(DispatchConcurrencyShop shop, string runnerId)
    {
        var now = shop.Clock.GetUtcNow().UtcDateTime;
        await using var db = shop.Db();
        db.AgentSessions.Add(new AgentSession
        {
            Id = Guid.NewGuid(),
            DefinitionName = "fake",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            RunnerId = runnerId,
            RunnerStoreId = Guid.NewGuid(),
            RunnerCwd = "/work",
            Cols = 80,
            Rows = 24,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedCardAsync(
        DispatchConcurrencyShop shop, Guid projectId, string identifier, CardStatus status)
    {
        var now = shop.Clock.GetUtcNow().UtcDateTime;
        var boardId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        await using var db = shop.Db();
        db.AddRange(
            new Board
            {
                Id = boardId,
                ProjectId = projectId,
                Name = identifier,
                MaxConcurrentSessions = 1,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new BoardColumn
            {
                Id = columnId,
                BoardId = boardId,
                StateKey = status.ToString().ToLowerInvariant(),
                Name = status.ToString(),
                ColumnOrder = 0,
                CardStatus = status,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new Card
            {
                Id = cardId,
                BoardId = boardId,
                BoardColumnId = columnId,
                Identifier = identifier,
                Title = identifier,
                Description = identifier,
                Status = status,
                CreatedAt = now,
                UpdatedAt = now,
            });
        await db.SaveChangesAsync();
        return cardId;
    }

    private static async Task<Guid> SeedBacklogAsync(DispatchConcurrencyShop shop, Guid projectId, string identifier) =>
        await SeedCardAsync(shop, projectId, identifier, CardStatus.Backlog);

    private static async Task<(Guid CardId, Guid TaskId)> SeedReadyAsync(
        DispatchConcurrencyShop shop, Guid projectId, string identifier)
    {
        var cardId = await SeedCardAsync(shop, projectId, identifier, CardStatus.InProgress);
        var taskId = await SeedTaskAsync(
            shop, AgentTaskRole.Plan, AgentTaskStatus.Succeeded, projectId, cardId: cardId, title: identifier + " plan");
        return (cardId, taskId);
    }

    private static async Task BindCardAsync(DispatchConcurrencyShop shop, Guid taskId, Guid cardId, bool succeededPlan)
    {
        await using var db = shop.Db();
        var task = await db.AgentTasks.SingleAsync(row => row.Id == taskId);
        task.CardId = cardId;
        if (succeededPlan)
        {
            task.Status = AgentTaskStatus.Succeeded;
            task.CompletedAt = shop.Clock.GetUtcNow().UtcDateTime;
            task.DeliverablePath = "docs/superpowers/plans/2026-09-30-card-0505.md";
            task.NextStage = null;
        }

        await db.SaveChangesAsync();
    }

    private static async Task<string> CensusAsync(DispatchConcurrencyShop shop)
    {
        await using var db = shop.Db();
        var tasks = await db.AgentTasks.AsNoTracking().OrderBy(task => task.Id)
            .Select(task => task.Id + ":" + task.Status + ":" + task.ProjectId).ToListAsync();
        var settings = await db.DispatchConcurrencySettings.AsNoTracking().OrderBy(row => row.ScopeKey)
            .Select(row => row.ScopeKey + ":" + row.Revision + ":" + row.OverridesJson).ToListAsync();
        var revisions = await db.DispatchConcurrencyRevisions.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => row.SettingsId + ":" + row.Revision).ToListAsync();
        var pins = await db.RoutingPins.AsNoTracking().OrderBy(pin => pin.Id)
            .Select(pin => pin.Id + ":" + pin.ClearedAt + ":" + pin.NotAfter + ":" + pin.UpdatedAt).ToListAsync();
        var events = await db.AgentTaskEvents.AsNoTracking().CountAsync();
        return string.Join("|", tasks) + "#" + string.Join("|", settings) + "#"
            + string.Join("|", revisions) + "#" + string.Join("|", pins) + "#" + events + "#"
            + shop.Bus.PublishedEvents.Count;
    }

    private static void EmptyArrays(JsonElement root)
    {
        foreach (var stage in root.GetProperty("stages").EnumerateArray())
        {
            stage.GetProperty("inFlight").GetArrayLength().ShouldBe(0);
            stage.GetProperty("queued").GetArrayLength().ShouldBe(0);
            stage.GetProperty("blocked").GetArrayLength().ShouldBe(0);
            stage.GetProperty("ready").GetArrayLength().ShouldBe(0);
        }

        root.GetProperty("investigateBacklog").GetProperty("total").GetInt32().ShouldBe(0);
    }

    private static List<Guid> StageTaskIds(JsonElement root) =>
        root.GetProperty("stages").EnumerateArray().SelectMany(stage =>
            stage.GetProperty("inFlight").EnumerateArray().Select(row => row.GetProperty("taskId").GetGuid())
                .Concat(stage.GetProperty("queued").EnumerateArray().Select(row => row.GetProperty("taskId").GetGuid()))
                .Concat(stage.GetProperty("blocked").EnumerateArray().Select(row => row.GetProperty("taskId").GetGuid())))
            .ToList();

    private static void SameHosts(AgentTaskPipelineDto left, AgentTaskPipelineDto right)
    {
        left.HostSummaryScope.ShouldBe("fleet");
        right.HostSummaryScope.ShouldBe("fleet");
        var a = left.Hosts.OrderBy(host => host.HostId, StringComparer.Ordinal)
            .Select(host => host.HostId + ":" + host.InFlight + ":" + host.EffectiveLimit + ":" + host.Scope).ToList();
        var b = right.Hosts.OrderBy(host => host.HostId, StringComparer.Ordinal)
            .Select(host => host.HostId + ":" + host.InFlight + ":" + host.EffectiveLimit + ":" + host.Scope).ToList();
        b.ShouldBe(a);
    }

    private sealed class FleetDirectory : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => null!;
        public ISessionRunnerClient Resolve(string? id) => null!;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct) =>
            Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
        public IReadOnlyList<string> KnownRunnerIds => ["server2"];
        public Guid? GetLiveStoreId(string? id) => null;
        public int? DeclaredCapacity(string id) => id == "server2" ? 10 : null;
    }

    private sealed class PauseFirstSettingsReadInterceptor : DbCommandInterceptor
    {
        private int _paused;

        public TaskCompletionSource AtRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Armed { get; private set; }

        public void Arm() => Armed = true;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed
                && command.CommandText.Contains("DispatchConcurrencySettings", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Exchange(ref _paused, 1) == 0)
            {
                AtRead.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
