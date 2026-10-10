using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
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

/// <summary>CARD-0505 V-5. Real CreateAsync against the imported seed and later puts.</summary>
[Category("Integration")]
public class DispatchConcurrencyAdmissionTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Unconfigured_create_keeps_legacy_decisions()
    {
        await using var planned = await OpenAsync();
        await InitAsync(planned.Shop);
        var first = await AdmitAsync(planned, "plan-1", AgentTaskRole.Plan, null);
        var second = await AdmitAsync(planned, "plan-2", AgentTaskRole.Plan, null);
        var third = await AdmitAsync(planned, "plan-3", AgentTaskRole.Plan, null);
        var fourth = Unique("plan-4");
        var refused = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(planned, fourth, AgentTaskRole.Plan, null));
        refused.Code.ShouldBe("concurrency_limit", "legacy-real-create");
        refused.Concurrency.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationOpen, "legacy-real-create");
        refused.Concurrency.Axis.ShouldBe(DispatchConcurrencyPolicy.AxisRole, "legacy-real-create");
        refused.Concurrency.Count.ShouldBe(3, "legacy-real-create");
        refused.Concurrency.Limit.ShouldBe(3, "legacy-real-create");
        refused.Concurrency.Mode.ShouldBe("LegacyOpen", "legacy-real-create");
        await AssertAbsentAsync(planned.Shop, fourth);

        var planOne = DispatchConcurrencyTestHost.BoundSettings();
        planOne.RolePolicy["Plan"].RecommendedInFlight = 1;
        await using var tight = await OpenAsync(planOne);
        await InitAsync(tight.Shop);
        await AdmitAsync(tight, "only", AgentTaskRole.Plan, null);
        var secondGoal = Unique("second");
        var secondRefusal = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(tight, secondGoal, AgentTaskRole.Plan, null));
        secondRefusal.Concurrency.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationOpen, "legacy-real-create");
        secondRefusal.Concurrency.Limit.ShouldBe(1, "legacy-real-create");
        (await CountGoalAsync(tight.Shop, secondGoal)).ShouldBe(0, "legacy-real-create");

        await using var overall = await OpenAsync();
        await InitAsync(overall.Shop);
        await SeedAsync(overall.Shop, overall.Directory, AgentTaskRole.Plan, AgentTaskStatus.Blocked, null);
        await SeedAsync(overall.Shop, overall.Directory, AgentTaskRole.Code, AgentTaskStatus.Succeeded, null);
        await SeedAsync(overall.Shop, overall.Directory, AgentTaskRole.Debug, AgentTaskStatus.Failed, null);
        await SeedAsync(overall.Shop, overall.Directory, AgentTaskRole.Review, AgentTaskStatus.Canceled, null);
        var admitted = new List<Guid>();
        for (var i = 0; i < 9; i++)
            admitted.Add((await AdmitAsync(overall, $"open-{i}", AgentTaskRole.Custom, null)).Id);
        var tenth = Unique("tenth");
        var absolute = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(overall, tenth, AgentTaskRole.Custom, null));
        absolute.Concurrency.Axis.ShouldBe(DispatchConcurrencyPolicy.AxisAbsolute, "legacy-real-create");
        absolute.Concurrency.Count.ShouldBe(9, "legacy-real-create");
        absolute.Concurrency.Limit.ShouldBe(9, "legacy-real-create");
        absolute.Concurrency.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationOpen, "legacy-real-create");
        (await CountGoalAsync(overall.Shop, tenth)).ShouldBe(0, "legacy-real-create");
        await using var verify = overall.Shop.Db();
        var stored = await verify.AgentTasks.Where(t => admitted.Contains(t.Id)).Select(t => t.Id).ToListAsync();
        stored.OrderBy(id => id).ShouldBe(admitted.OrderBy(id => id), "legacy-real-create");
        (await overall.Shop.ReadGlobalAsync()).Effective.Mode.ShouldBe("LegacyOpen", "legacy-real-create");
        first.Status.ShouldBe(AgentTaskStatus.Queued);
        second.Status.ShouldBe(AgentTaskStatus.Queued);
        third.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Global_and_project_put_change_real_create()
    {
        await using var gate = await OpenAsync();
        await InitAsync(gate.Shop);
        await gate.Shop.PutGlobalAsync(1, """{"mode":"LegacyOpen","roles":{"Plan":{"maxParallel":1}}}""", "plan one");
        await AdmitAsync(gate, "first", AgentTaskRole.Plan, null);
        var blocked = Unique("second");
        var refused = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, blocked, AgentTaskRole.Plan, null));
        refused.Concurrency.Limit.ShouldBe(1, "admission-used-revision");
        await gate.Shop.PutGlobalAsync(2, """{"mode":"LegacyOpen","roles":{"Plan":{"maxParallel":2}}}""", "plan two");
        var admitted = await AdmitAsync(gate, "second-now", AgentTaskRole.Plan, null);
        admitted.Status.ShouldBe(AgentTaskStatus.Queued, "admission-used-revision");

        await gate.Shop.PutProjectAsync(
            gate.Shop.ProjectP, 0, 3, """{"roles":{"Plan":{"maxParallel":3}}}""", "p plan three");
        var onP = await AdmitAsync(gate, "p-third", AgentTaskRole.Plan, gate.Shop.ProjectP);
        var qGoal = Unique("q-third");
        var qRefusal = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, qGoal, AgentTaskRole.Plan, gate.Shop.ProjectQ));
        qRefusal.Concurrency.Limit.ShouldBe(2, "admission-used-revision");
        (await CountGoalAsync(gate.Shop, qGoal)).ShouldBe(0, "admission-used-revision");
        await gate.Shop.PutProjectAsync(gate.Shop.ProjectP, 1, 3, "{}", "clear p");
        var restored = Unique("p-after-clear");
        var restoredRefusal = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, restored, AgentTaskRole.Plan, gate.Shop.ProjectP));
        restoredRefusal.Concurrency.Limit.ShouldBe(2, "admission-used-revision");

        var divergent = new DelegationSettings
        {
            MaxOpenTasks = 2,
            MaxDepth = 8,
            MaxTasksPerRoot = 100,
            MaxCostUsdPerRoot = 1000,
        };
        divergent.RolePolicy["Plan"].RecommendedInFlight = 1;
        await using var fresh = gate.Shop.Db();
        var reread = Service(gate.Shop, fresh, divergent);
        var custom = await reread.CreateAsync(
            Request(Unique("saved-cap"), AgentTaskRole.Custom, gate.Directory),
            Caller(gate.Directory, null),
            CancellationToken.None);
        custom.Status.ShouldBe(AgentTaskStatus.Queued, "admission-used-revision");
        onP.Id.ShouldNotBe(Guid.Empty);

        await RaceCreateAgainstPutAsync(DispatchConcurrencyMode.LegacyOpen);
        await RaceCreateAgainstPutAsync(DispatchConcurrencyMode.SeparateQueues);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Project_and_role_queue_boundaries_refuse_without_insert()
    {
        await using var gate = await OpenAsync();
        await InitAsync(gate.Shop);
        await gate.Shop.PutGlobalAsync(
            1,
            """{"mode":"SeparateQueues","maxParallel":3,"maxQueued":4,"roles":{"Code":{"maxParallel":2,"maxQueued":3}}}""",
            "queue bounds");
        await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Code, AgentTaskStatus.Working, null);
        await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Code, AgentTaskStatus.Working, null);
        var accepted = new List<Guid>();
        for (var i = 0; i < 3; i++)
            accepted.Add((await AdmitAsync(gate, $"code-{i}", AgentTaskRole.Code, null)).Id);
        var before = await CensusAsync(gate.Shop);
        var fourth = Unique("code-4");
        var role = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, fourth, AgentTaskRole.Code, null));
        role.Concurrency.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationQueued, "queue-count-only");
        role.Concurrency.Axis.ShouldBe(DispatchConcurrencyPolicy.AxisRole, "queue-refuses");
        role.Concurrency.Count.ShouldBe(3, "queue-count-only");
        role.Concurrency.Limit.ShouldBe(3, "queue-refuses");
        (await CensusAsync(gate.Shop)).ShouldBe(before, "queue-count-only");
        (await CountGoalAsync(gate.Shop, fourth)).ShouldBe(0, "queue-refuses");

        accepted.Add((await AdmitAsync(gate, "plan-slot", AgentTaskRole.Plan, null)).Id);
        var custom = Unique("custom-over");
        before = await CensusAsync(gate.Shop);
        var absolute = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, custom, AgentTaskRole.Custom, null));
        absolute.Concurrency.Axis.ShouldBe(DispatchConcurrencyPolicy.AxisAbsolute, "queue-refuses");
        absolute.Concurrency.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationQueued, "queue-count-only");
        absolute.Concurrency.Count.ShouldBe(4, "queue-count-only");
        (await CensusAsync(gate.Shop)).ShouldBe(before, "queue-count-only");

        await using (var db = gate.Shop.Db())
        {
            var done = await db.AgentTasks.SingleAsync(t => t.Id == accepted[0]);
            done.Status = AgentTaskStatus.Succeeded;
            done.CompletedAt = gate.Shop.Clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }

        (await AdmitAsync(gate, "code-after", AgentTaskRole.Code, null)).Status.ShouldBe(AgentTaskStatus.Queued, "queue-count-only");

        await using var pausedGate = await OpenAsync();
        await InitAsync(pausedGate.Shop);
        await pausedGate.Shop.PutGlobalAsync(
            1,
            """{"mode":"SeparateQueues","maxParallel":2,"maxQueued":0,"roles":{"Code":{"maxParallel":2,"maxQueued":0}}}""",
            "pause empty");
        var paused = Unique("paused");
        before = await CensusAsync(pausedGate.Shop);
        var zero = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(pausedGate, paused, AgentTaskRole.Code, null));
        zero.Concurrency.Count.ShouldBe(0, "queue-refuses");
        zero.Concurrency.Limit.ShouldBe(0, "queue-refuses");
        zero.Concurrency.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationQueued, "queue-refuses");
        (await CensusAsync(pausedGate.Shop)).ShouldBe(before, "queue-refuses");
        var projectPaused = Unique("project-paused");
        var projectZero = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(pausedGate, projectPaused, AgentTaskRole.Plan, null));
        projectZero.Concurrency.Axis.ShouldBe(DispatchConcurrencyPolicy.AxisAbsolute, "queue-refuses");
        projectZero.Concurrency.Count.ShouldBe(0, "queue-refuses");

        var opened = await pausedGate.Shop.PutGlobalAsync(
            2,
            """{"mode":"SeparateQueues","maxParallel":2,"roles":{"Code":{"maxParallel":2,"maxQueued":null}}}""",
            "null queue");
        opened.Effective.MaxQueued.ShouldBeNull("queue-count-only");
        opened.Effective.MaxParallel.ShouldBe(2, "queue-count-only");
        opened.Effective.Roles.Single(role => role.Role == "Code").MaxQueued.ShouldBeNull("queue-count-only");
        (await AdmitAsync(pausedGate, "null-queue", AgentTaskRole.Code, null)).Status
            .ShouldBe(AgentTaskStatus.Queued, "queue-count-only");
    }

    [Test]
    [Timeout(120_000)]
    public async Task Other_project_and_null_bucket_do_not_interfere()
    {
        await using var gate = await OpenAsync();
        await InitAsync(gate.Shop);
        await gate.Shop.PutGlobalAsync(
            1, """{"mode":"SeparateQueues","maxParallel":4,"maxQueued":4}""", "global room");
        await gate.Shop.PutProjectAsync(
            gate.Shop.ProjectP, 0, 2, """{"maxParallel":1,"maxQueued":1}""", "fillable p");
        var occupant = await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Code, AgentTaskStatus.Queued, gate.Shop.ProjectP);
        await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Code, AgentTaskStatus.Working, gate.Shop.ProjectP);
        await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Plan, AgentTaskStatus.Blocked, gate.Shop.ProjectP);
        await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Plan, AgentTaskStatus.Succeeded, gate.Shop.ProjectP);
        var specialist = await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Check, AgentTaskStatus.Queued, gate.Shop.ProjectP);
        var foreign = await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Code, AgentTaskStatus.Queued, gate.Shop.ProjectQ);
        var pGoal = Unique("p-full");
        var refused = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, pGoal, AgentTaskRole.Code, gate.Shop.ProjectP));
        var listed = refused.Concurrency.Open.Select(row => row.TaskId).ToList();
        listed.ShouldContain(occupant.Id, "project-isolation");
        listed.Contains(foreign.Id).ShouldBeFalse("project-isolation");
        listed.Contains(specialist.Id).ShouldBeFalse("project-isolation");
        refused.Concurrency.ProjectId.ShouldBe(gate.Shop.ProjectP, "project-isolation");

        (await AdmitAsync(gate, "q-own", AgentTaskRole.Code, gate.Shop.ProjectQ)).Status
            .ShouldBe(AgentTaskStatus.Queued, "project-isolation");
        (await AdmitAsync(gate, "null-own", AgentTaskRole.Code, null)).Status
            .ShouldBe(AgentTaskStatus.Queued, "project-isolation");

        await gate.Shop.PutProjectAsync(
            gate.Shop.ProjectQ, 0, 2, """{"maxQueued":1}""", "fill q");
        var qFull = Unique("q-full");
        await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, qFull, AgentTaskRole.Plan, gate.Shop.ProjectQ));
        (await AdmitAsync(gate, "null-still", AgentTaskRole.Plan, null)).Status
            .ShouldBe(AgentTaskStatus.Queued, "project-isolation");

        var parentId = Guid.NewGuid();
        await using (var db = gate.Shop.Db())
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = parentId,
                RootTaskId = parentId,
                Title = "parent",
                Goal = Unique("parent"),
                Kind = AgentTaskKind.Orchestrator,
                Role = AgentTaskRole.Custom,
                Status = AgentTaskStatus.Blocked,
                ProjectId = gate.Shop.ProjectP,
                Workspace = WorkspaceMode.ReadOnly,
                WorkingDirectory = gate.Directory,
                CreatedAt = gate.Shop.Clock.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync();
        }

        await gate.Shop.PutProjectAsync(
            gate.Shop.ProjectP, 1, 2, """{"maxParallel":4,"maxQueued":4}""", "room for the parent scope");
        await using var parentDb = gate.Shop.Db();
        var parent = await parentDb.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == parentId);
        var child = await Service(gate.Shop, parentDb).CreateAsync(
            Request(Unique("from-parent"), AgentTaskRole.Debug, gate.Directory),
            new AgentTaskService.Caller(parent, null, gate.Directory, ProjectId: gate.Shop.ProjectQ),
            CancellationToken.None);
        await using var childDb = gate.Shop.Db();
        var stored = await childDb.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == child.Id);
        stored.ProjectId.ShouldBe(gate.Shop.ProjectP, "project-isolation");
    }

    [Test]
    [Timeout(120_000)]
    public async Task Specialists_and_live_followups_keep_admission_exemptions()
    {
        await using var gate = await OpenAsync();
        await InitAsync(gate.Shop);
        await gate.Shop.PutGlobalAsync(
            1, """{"mode":"SeparateQueues","maxParallel":3,"maxQueued":0}""", "closed queue");
        var ordinary = Unique("ordinary");
        var closed = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, ordinary, AgentTaskRole.Code, null));
        closed.Concurrency.Count.ShouldBe(0, "specialist-exempt");
        foreach (var role in new[] { AgentTaskRole.Check, AgentTaskRole.Distill, AgentTaskRole.Diagnose })
        {
            var created = await AdmitAsync(gate, role.ToString(), role, null);
            created.Status.ShouldBe(AgentTaskStatus.Queued, "specialist-exempt");
        }

        var still = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, Unique("still"), AgentTaskRole.Code, null));
        still.Concurrency.Count.ShouldBe(0, "specialist-exempt");

        var agentId = await SeedAgentAsync(gate.Shop, gate.Directory);
        var prior = await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Code, AgentTaskStatus.Succeeded, null);
        await using (var db = gate.Shop.Db())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == prior.Id);
            row.AgentId = agentId;
            await db.SaveChangesAsync();
        }

        var live = await gate.Service.CreateAsync(
            Request(Unique("live"), AgentTaskRole.Code, gate.Directory) with
            {
                FollowUpOnTask = DelegationReportFormatter.Short(prior.Id),
            },
            Caller(gate.Directory, null),
            CancellationToken.None);
        live.FollowUpMessage.ShouldBe("follow-up on the live agent", "followup-only-live");
        var after = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, Unique("after-live"), AgentTaskRole.Code, null));
        after.Concurrency.Count.ShouldBe(1, "followup-only-live");
        after.Concurrency.Open.Select(row => row.TaskId).ShouldContain(live.Id, "followup-only-live");

        var retiredPrior = await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Code, AgentTaskStatus.Succeeded, null);
        await using (var db = gate.Shop.Db())
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == retiredPrior.Id);
            row.AgentId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }

        var retiredGoal = Unique("retired");
        var before = await CensusAsync(gate.Shop);
        var retired = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, retiredGoal, AgentTaskRole.Code, null, followUp: DelegationReportFormatter.Short(retiredPrior.Id)));
        retired.Code.ShouldBe("concurrency_limit", "followup-only-live");
        (await CountGoalAsync(gate.Shop, retiredGoal)).ShouldBe(0, "followup-only-live");
        (await CensusAsync(gate.Shop)).ShouldBe(before, "followup-only-live");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Override_only_bypasses_legacy_open()
    {
        var settings = DispatchConcurrencyTestHost.BoundSettings();
        settings.MaxOpenTasks = 3;
        settings.RolePolicy["Plan"].RecommendedInFlight = 3;
        await using var gate = await OpenAsync(settings);
        await InitAsync(gate.Shop);
        await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Plan, AgentTaskStatus.Queued, null);
        await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Code, AgentTaskStatus.Dispatched, null);
        await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Debug, AgentTaskStatus.Working, null);
        var created = await AdmitAsync(gate, "lifted", AgentTaskRole.Plan, null, ignore: true);
        created.Status.ShouldBe(AgentTaskStatus.Queued, "override-cannot-lift-queue");
        await using (var db = gate.Shop.Db())
        {
            var warning = await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Warning);
            warning.Detail.ShouldContain("ignoreConcurrencyLimit", Case.Sensitive, "override-cannot-lift-queue");
            warning.Detail.ShouldContain("limit 3", Case.Sensitive, "override-cannot-lift-queue");
        }

        await gate.Shop.PutGlobalAsync(1, """{"mode":"LegacyOpen","maxParallel":3,"maxQueued":0}""", "queue closed");
        var queued = Unique("queued");
        var before = await CensusAsync(gate.Shop);
        var queue = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, queued, AgentTaskRole.Plan, null, ignore: true));
        queue.Concurrency.CanOverride.ShouldBeFalse("override-cannot-lift-queue");
        queue.Concurrency.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationQueued, "override-cannot-lift-queue");
        queue.Message.Contains(ConcurrencyLimitException.Coda, StringComparison.Ordinal).ShouldBeFalse("override-cannot-lift-queue");
        (await CensusAsync(gate.Shop)).ShouldBe(before, "override-cannot-lift-queue");

        await gate.Shop.PutGlobalAsync(
            2, """{"mode":"SeparateQueues","maxParallel":1,"maxQueued":0}""", "separate closed");
        var separateGoal = Unique("separate");
        var separate = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, separateGoal, AgentTaskRole.Code, null, ignore: true));
        separate.Concurrency.CanOverride.ShouldBeFalse("override-cannot-lift-queue");
        (await CountGoalAsync(gate.Shop, separateGoal)).ShouldBe(0, "override-cannot-lift-queue");

        await using var world = await ConcurrencyDispatchWorld.Open();
        await world.InitializeAsync();
        await world.Shop.PutGlobalAsync(1, """{"mode":"SeparateQueues","maxParallel":1,"maxQueued":4}""", "running hold");
        await world.InsertAsync(AgentTaskRole.Code, AgentTaskStatus.Working, world.Shop.ProjectP, WorkspaceMode.ReadOnly);
        await using var createDb = world.Shop.Db();
        var tasks = Service(world.Shop, createDb);
        var held = await tasks.CreateAsync(
            new CreateAgentTaskRequest(Unique("held-run"), Role: AgentTaskRole.Plan, Workspace: WorkspaceMode.ReadOnly,
                WorkingDirectory: world.Directory, IgnoreConcurrencyLimit: true),
            Caller(world.Directory, world.Shop.ProjectP),
            CancellationToken.None);
        await world.TickAsync();
        var row = await world.ReloadAsync(held.Id);
        row.Status.ShouldBe(AgentTaskStatus.Queued, "override-cannot-lift-queue");
        world.Launches.Items.Count.ShouldBe(0, "override-cannot-lift-queue");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Concurrent_last_queue_slot_has_one_winner()
    {
        await RaceLastSlotAsync("""{"mode":"SeparateQueues","maxParallel":4,"maxQueued":1}""", true, AgentTaskRole.Custom);
        await RaceLastSlotAsync(
            """{"mode":"SeparateQueues","maxParallel":4,"maxQueued":4,"roles":{"Code":{"maxQueued":1}}}""",
            true, AgentTaskRole.Code);
        await RaceLastSlotAsync("""{"mode":"SeparateQueues","maxParallel":4,"maxQueued":1}""", false, AgentTaskRole.Plan);
    }

    [Test]
    [Timeout(120_000)]
    public async Task Problem_names_population_sources_revisions_and_bounded_occupants()
    {
        await using var gate = await OpenAsync();
        await InitAsync(gate.Shop);
        await gate.Shop.PutGlobalAsync(
            1,
            """{"mode":"SeparateQueues","maxParallel":9,"maxQueued":10,"roles":{"Plan":{"maxQueued":10}}}""",
            "name the queue");
        var ordered = Enumerable.Range(0, 15).Select(_ => Guid.NewGuid()).OrderBy(id => id).ToArray();
        var origin = gate.Shop.Clock.GetUtcNow().UtcDateTime;
        var seeded = new List<(Guid Id, DateTime CreatedAt)>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var created = origin.AddMinutes(ordered.Length - i);
            if (i == ordered.Length - 1)
                created = origin.AddMinutes(2);
            seeded.Add((ordered[i], created));
            await SeedAsync(gate.Shop, gate.Directory, AgentTaskRole.Plan, AgentTaskStatus.Queued, gate.Shop.ProjectP, created, ordered[i]);
        }

        var foreign = await SeedAsync(
            gate.Shop, gate.Directory, AgentTaskRole.Plan, AgentTaskStatus.Queued, gate.Shop.ProjectQ, origin.AddMinutes(-1));
        var specialist = await SeedAsync(
            gate.Shop, gate.Directory, AgentTaskRole.Check, AgentTaskStatus.Queued, gate.Shop.ProjectP, origin.AddMinutes(-2));
        var refusal = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(gate, Unique("sixteenth"), AgentTaskRole.Plan, gate.Shop.ProjectP));
        var problem = refusal.Concurrency;
        problem.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationQueued, "problem-population");
        problem.Axis.ShouldBe(DispatchConcurrencyPolicy.AxisAbsolute, "problem-total");
        problem.CanOverride.ShouldBeFalse("problem-capability");
        problem.Count.ShouldBe(15, "problem-total");
        problem.TotalOccupants.ShouldBe(15, "problem-total");
        problem.OmittedOccupants.ShouldBe(3, "problem-total");
        problem.Open.Count.ShouldBe(12, "problem-total");
        problem.Override.ShouldBe(ConcurrencyLimitException.OverrideFlag, "problem-capability");
        problem.Mode.ShouldBe("SeparateQueues", "problem-population");
        problem.GlobalRevision.ShouldBe(2, "problem-total");
        problem.ProjectRevision.ShouldBe(0, "problem-total");
        problem.MaxQueuedSource.ShouldBe(DispatchConcurrencyPolicy.SourceGlobal, "problem-total");
        problem.Exceeded!.Any(item => item.Axis == DispatchConcurrencyPolicy.AxisAbsolute
            && item.Population == DispatchConcurrencyPolicy.PopulationQueued).ShouldBeTrue("problem-total");
        problem.Exceeded.Any(item => item.Axis == DispatchConcurrencyPolicy.AxisRole
            && item.Role == "Plan").ShouldBeTrue("problem-total");
        var expected = seeded.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Select(row => row.Id).Take(12).ToList();
        problem.Open.Select(row => row.TaskId).ToList().ShouldBe(expected, "problem-total");
        problem.Open.Select(row => row.TaskId).Contains(foreign.Id).ShouldBeFalse("project-isolation");
        problem.Open.Select(row => row.TaskId).Contains(specialist.Id).ShouldBeFalse("specialist-exempt");
        refusal.Message.Contains("queued", StringComparison.Ordinal).ShouldBeTrue("problem-population");
        refusal.Message.Contains("in flight", StringComparison.Ordinal).ShouldBeFalse("problem-population");

        var legacySettings = DispatchConcurrencyTestHost.BoundSettings();
        legacySettings.RolePolicy["Plan"].RecommendedInFlight = 1;
        await using var legacy = await OpenAsync(legacySettings);
        await InitAsync(legacy.Shop);
        var matching = await SeedAsync(legacy.Shop, legacy.Directory, AgentTaskRole.Plan, AgentTaskStatus.Queued, null);
        var other = await SeedAsync(legacy.Shop, legacy.Directory, AgentTaskRole.Code, AgentTaskStatus.Queued, null);
        var open = await Should.ThrowAsync<ConcurrencyLimitException>(() =>
            AdmitAsync(legacy, Unique("legacy-plan"), AgentTaskRole.Plan, null));
        open.Concurrency.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationOpen, "problem-population");
        open.Concurrency.CanOverride.ShouldBeTrue("problem-capability");
        open.Concurrency.Open.Select(row => row.TaskId).ShouldBe(new[] { matching.Id }, "problem-population");
        open.Concurrency.Open.Select(row => row.TaskId).Contains(other.Id).ShouldBeFalse("problem-population");
        open.Message.Contains("in flight", StringComparison.Ordinal).ShouldBeTrue("problem-population");
        open.Message.Contains(ConcurrencyLimitException.Coda, StringComparison.Ordinal).ShouldBeTrue("problem-capability");
    }

    private static async Task RaceLastSlotAsync(string overrides, bool projectScope, AgentTaskRole role)
    {
        await using var gate = await OpenAsync();
        await InitAsync(gate.Shop);
        await gate.Shop.PutGlobalAsync(1, overrides, "last slot");
        Guid? project = projectScope ? gate.Shop.ProjectP : null;
        var pause = new PauseMatchSaveInterceptor { Match = PauseMatchSaveInterceptor.AddedTask };
        await using var left = gate.Shop.Db(pause);
        await using var right = gate.Shop.Db(pause);
        var goalA = Unique("a");
        var goalB = Unique("b");
        var runA = Service(gate.Shop, left).CreateAsync(Request(goalA, role, gate.Directory), Caller(gate.Directory, project), CancellationToken.None);
        var started = await Task.WhenAny(pause.AtSave.Task, runA);
        if (started == runA)
            await runA;
        pause.AtSave.Task.IsCompleted.ShouldBeTrue("one-create-winner");
        var runB = Service(gate.Shop, right).CreateAsync(Request(goalB, role, gate.Directory), Caller(gate.Directory, project), CancellationToken.None);
        try
        {
            await WaitForLockAsync(() => pause.Crossed, gate.Shop.ConnectionString, "create-B-crossed=false");
        }
        finally
        {
            pause.Release.TrySetResult();
        }

        var outcomes = await Task.WhenAll(SettleAsync(runA), SettleAsync(runB));
        outcomes.Count(item => item.Created is not null).ShouldBe(1, "one-create-winner");
        outcomes.Count(item => item.Error is ConcurrencyLimitException).ShouldBe(1, "one-create-winner");
        await using var verify = gate.Shop.Db();
        var rows = await verify.AgentTasks.Where(t => t.Goal == goalA || t.Goal == goalB).ToListAsync();
        rows.Count.ShouldBe(1, "one-create-winner");
        (await verify.AgentTaskEvents.CountAsync(e => rows[0].Id == e.AgentTaskId && e.Type == AgentTaskEventType.Created))
            .ShouldBe(1, "one-create-winner");
    }

    private static async Task RaceCreateAgainstPutAsync(DispatchConcurrencyMode mode)
    {
        var modeName = mode.ToString();
        await using var first = await OpenAsync();
        await InitAsync(first.Shop);
        var pause = new PauseMatchSaveInterceptor { Match = PauseMatchSaveInterceptor.AddedTask };
        await using var createDb = first.Shop.Db(pause);
        var goal = Unique("accepted");
        var creating = Service(first.Shop, createDb).CreateAsync(
            Request(goal, AgentTaskRole.Custom, first.Directory), Caller(first.Directory, null), CancellationToken.None);
        var started = await Task.WhenAny(pause.AtSave.Task, creating);
        if (started == creating)
            await creating;
        var putting = first.Shop.PutGlobalAsync(1, ModeJson(modeName, """{"maxQueued":0,"maxParallel":4}"""), "lower after accept");
        try
        {
            await WaitForLockAsync(() => pause.Crossed, first.Shop.ConnectionString, "create-B-crossed=false");
        }
        finally
        {
            pause.Release.TrySetResult();
        }

        var created = await creating;
        var saved = await putting;
        created.Status.ShouldBe(AgentTaskStatus.Queued, "admission-used-revision");
        saved.Occupancy.QueuedOverage.ShouldBeGreaterThan(0, "admission-used-revision");
        (await CountGoalAsync(first.Shop, goal)).ShouldBe(1, "admission-used-revision");

        await using var second = await OpenAsync();
        await InitAsync(second.Shop);
        var putPause = new PauseSettingsSaveInterceptor();
        var reach = new ReachSaveInterceptor { Match = PauseMatchSaveInterceptor.AddedTask };
        await using var putDb = second.Shop.Db(putPause);
        await using var admitDb = second.Shop.Db(reach);
        var writing = second.Shop.Service(putDb).PutGlobalAsync(
            DispatchConcurrencyTestHost.Put(1, ModeJson(modeName, """{"maxQueued":0,"maxParallel":4}"""), "queue zero"),
            null, CancellationToken.None);
        await putPause.AtSave.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var refusedGoal = Unique("refused");
        var admitting = Service(second.Shop, admitDb).CreateAsync(
            Request(refusedGoal, AgentTaskRole.Custom, second.Directory), Caller(second.Directory, null), CancellationToken.None);
        try
        {
            await WaitForLockAsync(() => Volatile.Read(ref reach.Reached) > 0, second.Shop.ConnectionString, "create-B-crossed=false");
        }
        finally
        {
            putPause.Release.TrySetResult();
        }

        var committed = await writing;
        var denied = await Should.ThrowAsync<ConcurrencyLimitException>(() => admitting);
        denied.Concurrency.GlobalRevision.ShouldBe(committed.Revision, "admission-used-revision");
        denied.Concurrency.Population.ShouldBe(DispatchConcurrencyPolicy.PopulationQueued, "admission-used-revision");
        (await CountGoalAsync(second.Shop, refusedGoal)).ShouldBe(0, "admission-used-revision");
    }

    private static string ModeJson(string mode, string body)
    {
        var trimmed = body.Trim();
        return "{\"mode\":\"" + mode + "\"," + trimmed[1..];
    }

    private static async Task WaitForLockAsync(Func<bool> crossed, string connectionString, string crossedMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            crossed().ShouldBeFalse(crossedMessage);
            if (await DispatchConcurrencyLockProbe.WaitForUngrantedAdvisoryAsync(connectionString, TimeSpan.FromMilliseconds(400)))
                return;
        }

        crossed().ShouldBeFalse(crossedMessage);
        throw new InvalidOperationException("harness deadline");
    }

    private static async Task<(AgentTaskCreatedDto? Created, Exception? Error)> SettleAsync(Task<AgentTaskCreatedDto> run)
    {
        try
        {
            return (await run, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private sealed class ReachSaveInterceptor : SaveChangesInterceptor
    {
        public int Reached;
        public Func<DbContext, bool> Match { get; init; } = _ => false;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context && Match(context))
                Interlocked.Increment(ref Reached);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class Gate : IAsyncDisposable
    {
        public required DispatchConcurrencyShop Shop { get; init; }
        public required ScratchGitRepo Repo { get; init; }
        public required AppDbContext Db { get; init; }
        public required AgentTaskService Service { get; init; }
        public string Directory => Repo.Path;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            Repo.Dispose();
            await Shop.DisposeAsync();
        }
    }

    private static async Task<Gate> OpenAsync(DelegationSettings? settings = null)
    {
        var shop = await DispatchConcurrencyShop.Open(settings);
        var repo = new ScratchGitRepo("c0505-admit");
        var db = shop.Db();
        return new Gate { Shop = shop, Repo = repo, Db = db, Service = Service(shop, db, settings) };
    }

    private static async Task InitAsync(DispatchConcurrencyShop shop)
    {
        await using var db = shop.Db();
        await shop.Service(db).EnsureInitializedAsync(CancellationToken.None);
    }

    private static AgentTaskService Service(DispatchConcurrencyShop shop, AppDbContext db, DelegationSettings? settings = null)
    {
        var options = Options.Create(settings ?? shop.Settings);
        var concurrency = shop.Service(db, settings);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            options,
            shop.Bus,
            new RecordingSessionStopper(),
            shop.Clock,
            NullLogger<AgentTaskService>.Instance,
            openGate: new DelegationOpenGate(db, options, concurrency),
            dispatchConcurrency: concurrency);
    }

    private static async Task<AgentTaskCreatedDto> AdmitAsync(
        Gate gate, string label, AgentTaskRole role, Guid? projectId, bool ignore = false, string? followUp = null)
    {
        try
        {
            return await gate.Service.CreateAsync(
                Request(label, role, gate.Directory, ignore, followUp),
                Caller(gate.Directory, projectId),
                CancellationToken.None);
        }
        catch
        {
            gate.Db.ChangeTracker.Clear();
            throw;
        }
    }

    private static CreateAgentTaskRequest Request(string goal, AgentTaskRole role, string directory, bool ignore = false, string? followUp = null) =>
        new(goal, Role: role, WorkingDirectory: directory, IgnoreConcurrencyLimit: ignore, FollowUpOnTask: followUp);

    private static AgentTaskService.Caller Caller(string directory, Guid? projectId) =>
        new(null, null, directory, ProjectId: projectId);

    private static async Task<AgentTask> SeedAsync(
        DispatchConcurrencyShop shop, string directory, AgentTaskRole role, AgentTaskStatus status, Guid? projectId,
        DateTime? createdAt = null, Guid? id = null)
    {
        var taskId = id ?? Guid.NewGuid();
        var now = createdAt ?? shop.Clock.GetUtcNow().UtcDateTime;
        var task = new AgentTask
        {
            Id = taskId,
            RootTaskId = taskId,
            Title = $"c0505-{role}-{status}",
            Goal = $"c0505-seed-{taskId:N}",
            Kind = AgentTaskKind.Worker,
            Role = role,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Status = status,
            ProjectId = projectId,
            Workspace = WorkspaceMode.ReadOnly,
            WorkingDirectory = directory,
            CreatedAt = DateTime.SpecifyKind(now, DateTimeKind.Utc),
            CapacityWaitRetained = false,
        };
        await using var db = shop.Db();
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<Guid> SeedAgentAsync(DispatchConcurrencyShop shop, string directory)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"c0505-{Guid.NewGuid():N}"[..12],
            Slug = $"c0505-{Guid.NewGuid():N}"[..12],
            WorkingDirectory = directory,
            Details = "Live follow-up agent.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.Low,
            Kind = AgentKind.ClaudeCode,
            CreatedAt = shop.Clock.GetUtcNow().UtcDateTime,
            UpdatedAt = shop.Clock.GetUtcNow().UtcDateTime,
        };
        await using var db = shop.Db();
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private static async Task<(int Tasks, int Events)> CensusAsync(DispatchConcurrencyShop shop)
    {
        await using var db = shop.Db();
        return (await db.AgentTasks.CountAsync(), await db.AgentTaskEvents.CountAsync());
    }

    private static async Task<int> CountGoalAsync(DispatchConcurrencyShop shop, string goal)
    {
        await using var db = shop.Db();
        return await db.AgentTasks.CountAsync(t => t.Goal == goal);
    }

    private static async Task AssertAbsentAsync(DispatchConcurrencyShop shop, string goal) =>
        (await CountGoalAsync(shop, goal)).ShouldBe(0, "legacy-real-create");

    private static string Unique(string label) => $"c0505-{label}-{Guid.NewGuid():N}";
}
