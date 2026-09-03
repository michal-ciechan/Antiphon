using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0147 S1: create-time 409 <c>concurrency_limit</c>. Isolated schema so fleet-wide
/// counts do not collide with other suites writing the shared Postgres container.
/// </summary>
[Category("Integration")]
public class AgentTaskConcurrencyLimitTests
{
    [Test]
    public void max_open_tasks_defaults_to_three_and_does_not_change_the_hard_process_cap()
    {
        var settings = new DelegationSettings();
        settings.MaxOpenTasks.ShouldBe(3);
        settings.MaxConcurrentTasks.ShouldBe(6, "the dispatcher process cap is independent of the create gate");
        settings.MaxOpenTasks = 10;
        settings.MaxConcurrentTasks.ShouldBe(6);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public void max_open_tasks_rejects_zero_and_negatives(int value)
    {
        var settings = new DelegationSettings { MaxOpenTasks = value };
        var result = new DelegationSettingsValidator().Validate(null, settings);
        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("MaxOpenTasks"));
    }

    [Test]
    public void max_open_tasks_accepts_a_positive_integer()
    {
        var settings = new DelegationSettings { MaxOpenTasks = 1 };
        new DelegationSettingsValidator().Validate(null, settings).Failed.ShouldBeFalse();
    }

    [Test]
    public async Task fourth_create_at_three_open_returns_409_concurrency_limit()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var seeded = await SeedOpenAsync(db, workspace.Path,
            (AgentTaskRole.Custom, AgentTaskStatus.Queued),
            (AgentTaskRole.Custom, AgentTaskStatus.Dispatched),
            (AgentTaskRole.Custom, AgentTaskStatus.Working));
        var goal = Unique("fourth");

        var ex = await Should.ThrowAsync<ConcurrencyLimitException>(
            () => CreateService(db).CreateAsync(
                Request(goal, AgentTaskRole.Plan),
                ManualCaller(workspace.Path),
                CancellationToken.None));

        ex.Code.ShouldBe("concurrency_limit");
        ex.Message.ShouldContain("3");
        ex.Message.ShouldContain("limit 3");
        ex.Message.ShouldContain(ConcurrencyLimitException.Coda);
        var payload = ex.Concurrency;
        payload.Axis.ShouldBe("absolute");
        payload.Role.ShouldBeNull();
        payload.Count.ShouldBe(3);
        payload.Limit.ShouldBe(3);
        payload.Override.ShouldBe("ignoreConcurrencyLimit");
        payload.Open.Count.ShouldBe(3);
        payload.Open.Select(o => o.TaskId).OrderBy(id => id).ShouldBe(seeded.Select(t => t.Id).OrderBy(id => id));

        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.CountAsync(t => t.Goal == goal))
            .ShouldBe(0, "a refused create must not leave a queued row");
        foreach (var task in seeded)
        {
            var reloaded = await verify.AgentTasks.SingleAsync(t => t.Id == task.Id);
            reloaded.Status.ShouldBe(task.Status);
            reloaded.Title.ShouldBe(task.Title);
        }
    }

    [Test]
    public async Task second_debug_returns_409_on_the_role_axis()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var open = await SeedOpenAsync(db, workspace.Path, (AgentTaskRole.Debug, AgentTaskStatus.Working));
        var goal = Unique("second-debug");

        var ex = await Should.ThrowAsync<ConcurrencyLimitException>(
            () => CreateService(db).CreateAsync(
                Request(goal, AgentTaskRole.Debug),
                ManualCaller(workspace.Path),
                CancellationToken.None));

        ex.Code.ShouldBe("concurrency_limit");
        ex.Message.ShouldContain("Debug");
        ex.Message.ShouldContain("limit 1");
        ex.Concurrency.Axis.ShouldBe("role");
        ex.Concurrency.Role.ShouldBe("Debug");
        ex.Concurrency.Count.ShouldBe(1);
        ex.Concurrency.Limit.ShouldBe(1);
        ex.Concurrency.Open.ShouldHaveSingleItem().TaskId.ShouldBe(open[0].Id);

        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.CountAsync(t => t.Goal == goal)).ShouldBe(0);
    }

    [Test]
    public async Task mixed_plan_code_debug_plus_a_fourth_is_409_absolute()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedOpenAsync(db, workspace.Path,
            (AgentTaskRole.Plan, AgentTaskStatus.Working),
            (AgentTaskRole.Code, AgentTaskStatus.Queued),
            (AgentTaskRole.Debug, AgentTaskStatus.Dispatched));
        var goal = Unique("mixed-fourth");

        var ex = await Should.ThrowAsync<ConcurrencyLimitException>(
            () => CreateService(db).CreateAsync(
                Request(goal, AgentTaskRole.Test),
                ManualCaller(workspace.Path),
                CancellationToken.None));

        ex.Concurrency.Axis.ShouldBe("absolute");
        ex.Concurrency.Count.ShouldBe(3);
        ex.Concurrency.Limit.ShouldBe(3);
        await using (var verify = CreateContext(schema))
            (await verify.AgentTasks.CountAsync(t => t.Goal == goal)).ShouldBe(0);
    }

    [Test]
    public async Task custom_has_no_per_role_gate_until_the_absolute_cap()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedOpenAsync(db, workspace.Path,
            (AgentTaskRole.Custom, AgentTaskStatus.Queued),
            (AgentTaskRole.Plan, AgentTaskStatus.Working));

        var third = await CreateService(db).CreateAsync(
            Request(Unique("custom-third"), AgentTaskRole.Custom),
            ManualCaller(workspace.Path),
            CancellationToken.None);
        third.Status.ShouldBe(AgentTaskStatus.Queued);

        var goal = Unique("custom-fourth");
        var ex = await Should.ThrowAsync<ConcurrencyLimitException>(
            () => CreateService(db).CreateAsync(
                Request(goal, AgentTaskRole.Custom),
                ManualCaller(workspace.Path),
                CancellationToken.None));
        ex.Concurrency.Axis.ShouldBe("absolute");
        ex.Concurrency.Count.ShouldBe(3);
        await using (var verify = CreateContext(schema))
            (await verify.AgentTasks.CountAsync(t => t.Goal == goal)).ShouldBe(0);
    }

    [Test]
    public async Task three_custom_plus_a_fourth_is_409_absolute()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedOpenAsync(db, workspace.Path,
            (AgentTaskRole.Custom, AgentTaskStatus.Queued),
            (AgentTaskRole.Custom, AgentTaskStatus.Queued),
            (AgentTaskRole.Custom, AgentTaskStatus.Queued));

        var ex = await Should.ThrowAsync<ConcurrencyLimitException>(
            () => CreateService(db).CreateAsync(
                Request(Unique("custom-cap"), AgentTaskRole.Custom),
                ManualCaller(workspace.Path),
                CancellationToken.None));
        ex.Concurrency.Axis.ShouldBe("absolute");
        ex.Message.ShouldContain("3");
        ex.Message.ShouldContain("limit 3");
    }

    [Test]
    public async Task blocked_does_not_count_as_open()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Debug, AgentTaskStatus.Blocked, Unique("blocked"));
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Debug, AgentTaskStatus.Succeeded, Unique("done"));
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Debug, AgentTaskStatus.Failed, Unique("failed"));
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Debug, AgentTaskStatus.Canceled, Unique("canceled"));

        var created = await CreateService(db).CreateAsync(
            Request(Unique("debug-after-blocked"), AgentTaskRole.Debug),
            ManualCaller(workspace.Path),
            CancellationToken.None);
        created.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task specialist_at_the_cap_still_creates()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedOpenAsync(db, workspace.Path,
            (AgentTaskRole.Plan, AgentTaskStatus.Working),
            (AgentTaskRole.Code, AgentTaskStatus.Queued),
            (AgentTaskRole.Debug, AgentTaskStatus.Dispatched));

        var created = await CreateService(db).CreateAsync(
            Request(Unique("check-at-cap"), AgentTaskRole.Check),
            ManualCaller(workspace.Path),
            CancellationToken.None);
        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.Id.ShouldNotBe(Guid.Empty);
    }

    [Test]
    public async Task live_follow_up_at_the_cap_still_creates()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var agentId = await SeedAgentAsync(db, workspace.Path);
        var prior = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Succeeded, Unique("prior"));
        prior.AgentId = agentId;
        await db.SaveChangesAsync();
        await SeedOpenAsync(db, workspace.Path,
            (AgentTaskRole.Plan, AgentTaskStatus.Working),
            (AgentTaskRole.Code, AgentTaskStatus.Queued),
            (AgentTaskRole.Debug, AgentTaskStatus.Dispatched));

        var created = await CreateService(db).CreateAsync(
            Request(Unique("live-follow-up"), AgentTaskRole.Code) with
            {
                FollowUpOnTask = DelegationReportFormatter.Short(prior.Id),
            },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.FollowUpMessage.ShouldBe("follow-up on the live agent");
    }

    [Test]
    public async Task retired_agent_follow_up_at_the_cap_is_refused()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var prior = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Succeeded, Unique("retired-prior"));
        prior.AgentId = Guid.NewGuid();
        await db.SaveChangesAsync();
        await SeedOpenAsync(db, workspace.Path,
            (AgentTaskRole.Plan, AgentTaskStatus.Working),
            (AgentTaskRole.Code, AgentTaskStatus.Queued),
            (AgentTaskRole.Debug, AgentTaskStatus.Dispatched));
        var goal = Unique("retired-follow-up");

        var ex = await Should.ThrowAsync<ConcurrencyLimitException>(
            () => CreateService(db).CreateAsync(
                Request(goal, AgentTaskRole.Code) with
                {
                    FollowUpOnTask = DelegationReportFormatter.Short(prior.Id),
                },
                ManualCaller(workspace.Path),
                CancellationToken.None));
        ex.Code.ShouldBe("concurrency_limit");
        await using (var verify = CreateContext(schema))
            (await verify.AgentTasks.CountAsync(t => t.Goal.Contains(goal))).ShouldBe(0);
    }

    [Test]
    public async Task ignore_concurrency_limit_at_the_cap_creates_and_writes_a_warning()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedOpenAsync(db, workspace.Path,
            (AgentTaskRole.Plan, AgentTaskStatus.Working),
            (AgentTaskRole.Code, AgentTaskStatus.Queued),
            (AgentTaskRole.Debug, AgentTaskStatus.Dispatched));

        var created = await CreateService(db).CreateAsync(
            Request(Unique("override"), AgentTaskRole.Plan) with { IgnoreConcurrencyLimit = true },
            ManualCaller(workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued, "override queues; it does not dispatch");

        await using var verify = CreateContext(schema);
        var stored = await verify.AgentTasks.SingleAsync(t => t.Id == created.Id);
        stored.Status.ShouldBe(AgentTaskStatus.Queued);
        var warning = await verify.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Warning);
        warning.Detail.ShouldContain("3/3");
        warning.Detail.ShouldContain("limit 3");
        warning.Detail.ShouldContain("Plan");
        warning.Detail.ShouldContain("ignoreConcurrencyLimit");
    }

    [Test]
    [Timeout(60_000)]
    public async Task two_concurrent_creates_when_max_open_is_one_yield_one_200_and_one_409(
        CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var settings = new DelegationSettings
        {
            MaxOpenTasks = 1,
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 50.00m,
        };
        var goalA = Unique("concurrent-a");
        var goalB = Unique("concurrent-b");

        await using var dbA = CreateContext(schema);
        await using var dbB = CreateContext(schema);
        var serviceA = CreateService(dbA, settings);
        var serviceB = CreateService(dbB, settings);
        var caller = ManualCaller(workspace.Path);

        var attemptA = AttemptCreate(serviceA, goalA, caller, ct);
        var attemptB = AttemptCreate(serviceB, goalB, caller, ct);
        await Task.WhenAll(attemptA, attemptB);

        var outcomes = new[] { attemptA.Result, attemptB.Result };
        outcomes.Count(o => o.Created is not null).ShouldBe(1);
        outcomes.Count(o => o.Error is ConcurrencyLimitException).ShouldBe(1);
        var refused = outcomes.Select(o => o.Error).OfType<ConcurrencyLimitException>().Single();
        refused.Code.ShouldBe("concurrency_limit");
        refused.Concurrency.Limit.ShouldBe(1);

        await using var verify = CreateContext(schema);
        var inserted = await verify.AgentTasks
            .Where(t => t.Goal == goalA || t.Goal == goalB)
            .ToListAsync();
        inserted.ShouldHaveSingleItem();
    }

    [Test]
    public async Task a_stuck_finding_is_named_on_the_occupant()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var open = await SeedOpenAsync(db, workspace.Path,
            (AgentTaskRole.Plan, AgentTaskStatus.Working),
            (AgentTaskRole.Code, AgentTaskStatus.Queued),
            (AgentTaskRole.Debug, AgentTaskStatus.Dispatched));
        var stuckTask = open[0];
        db.WorktreeHealthFindings.Add(new WorktreeHealthFinding
        {
            Id = Guid.NewGuid(),
            RepoPath = workspace.Path,
            Branch = $"feat/card-task-{DelegationReportFormatter.Short(stuckTask.Id)}",
            Path = Path.Combine(workspace.Path, "gone"),
            TaskId = stuckTask.Id,
            Shape = WorktreeHealthShape.LockedMissing,
            Detail = $"feat/card-task-{DelegationReportFormatter.Short(stuckTask.Id)} locked initializing; directory gone",
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var goal = Unique("stuck-fourth");

        var ex = await Should.ThrowAsync<ConcurrencyLimitException>(
            () => CreateService(db).CreateAsync(
                Request(goal, AgentTaskRole.Test),
                ManualCaller(workspace.Path),
                CancellationToken.None));

        var occupant = ex.Concurrency.Open.Single(o => o.TaskId == stuckTask.Id);
        occupant.Stuck.ShouldNotBeNull();
        occupant.Stuck.ShouldContain("locked initializing");
        occupant.Stuck.ShouldContain("directory gone");
        ex.Message.ShouldContain("stuck:");
        ex.Concurrency.Open.Where(o => o.TaskId != stuckTask.Id).ShouldAllBe(o => o.Stuck == null);
    }

    [Test]
    public void compact_stuck_drops_the_branch_prefix()
    {
        DelegationOpenGate.CompactStuck(
                "feat/card-task-aabbccdd locked initializing; directory C:\\trees\\x is gone")
            .ShouldBe("locked initializing; directory C:\\trees\\x is gone");
    }

    [Test]
    public async Task a_plan_and_a_code_together_are_under_the_absolute_cap()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedOpenAsync(db, workspace.Path, (AgentTaskRole.Plan, AgentTaskStatus.Working));

        var created = await CreateService(db).CreateAsync(
            Request(Unique("code-beside-plan"), AgentTaskRole.Code),
            ManualCaller(workspace.Path),
            CancellationToken.None);
        created.Status.ShouldBe(AgentTaskStatus.Queued);
    }

    private static async Task<(AgentTaskCreatedDto? Created, Exception? Error)> AttemptCreate(
        AgentTaskService service,
        string goal,
        AgentTaskService.Caller caller,
        CancellationToken ct)
    {
        try
        {
            return (await service.CreateAsync(Request(goal, AgentTaskRole.Custom), caller, ct), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static AgentTaskService CreateService(AppDbContext db, DelegationSettings? settings = null)
    {
        var resolved = settings ?? new DelegationSettings
        {
            MaxOpenTasks = 3,
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 50.00m,
        };
        var options = Options.Create(resolved);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            options,
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            openGate: new DelegationOpenGate(db, options));
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static CreateAgentTaskRequest Request(string goal, AgentTaskRole role) =>
        new(Goal: goal, Role: role);

    private static AgentTaskService.Caller ManualCaller(string directory) => new(null, null, directory);

    private static string Unique(string label) => $"c0147-{label}-{Guid.NewGuid():N}";

    private static async Task<List<AgentTask>> SeedOpenAsync(
        AppDbContext db,
        string directory,
        params (AgentTaskRole Role, AgentTaskStatus Status)[] rows)
    {
        var seeded = new List<AgentTask>(rows.Length);
        foreach (var (role, status) in rows)
            seeded.Add(await SeedTaskAsync(db, directory, role, status, Unique($"{role}-{status}")));
        return seeded;
    }

    private static async Task<AgentTask> SeedTaskAsync(
        AppDbContext db,
        string directory,
        AgentTaskRole role,
        AgentTaskStatus status,
        string title)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = title,
            Goal = title,
            Role = role,
            Status = status,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<Guid> SeedAgentAsync(AppDbContext db, string directory)
    {
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = $"c0147-{Guid.NewGuid():N}"[..13],
            Slug = $"c0147-{Guid.NewGuid():N}"[..13],
            WorkingDirectory = directory,
            Details = "Live follow-up agent.",
            Status = AgentStatus.Idle,
            ModelLevel = AgentModelLevel.Low,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Agents.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c0147").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
