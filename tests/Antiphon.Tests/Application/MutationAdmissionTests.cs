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

[Category("Integration")]
public class MutationAdmissionTests
{
    [Test]
    public async Task C470_concurrent_mutation_creates_admit_only_one()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        await using var first = CreateContext(schema);
        await using var second = CreateContext(schema);
        var project = await SeedProjectAsync(first);
        var caller = new AgentTaskService.Caller(null, null, workspace.Path, ProjectId: project);
        async Task<Exception?> Attempt(AppDbContext db)
        {
            try { await CreateService(db).CreateAsync(Request(Unique("race"), AgentTaskRole.Mutation), caller, default); return null; }
            catch (Exception ex) { return ex; }
        }
        var results = await Task.WhenAll(Attempt(first), Attempt(second));
        results.Count(r => r is null).ShouldBe(1);
        var refusal = results.OfType<ConcurrencyLimitException>().ShouldHaveSingleItem();
        refusal.Concurrency.Axis.ShouldBe("role");
        refusal.Concurrency.Role.ShouldBe("Mutation");
        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.CountAsync(t => t.ProjectId == project)).ShouldBe(1);
    }

    [Test]
    public async Task C470_mutation_routing_does_not_inherit_code()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var options = Options.Create(new DelegationSettings());
        var routing = new ComplexityRoutingService(db, options, TimeProvider.System);
        var chains = new ComplexityChainService(db, TimeProvider.System, routing, options, NullLogger<ComplexityChainService>.Instance);
        var pins = new RoutingPinService(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance, routing);
        var pin = await pins.UpsertAsync(new PutRoutingPinRequest(AgentTaskRole.Code,
            AgentKind: AgentKind.Grok, ModelLevel: AgentModelLevel.High,
            Provenance: RoutingPinProvenance.Human, Strength: RoutingPinStrength.Required, Reason: "Code only"), null, default);
        var codePin = await db.RoutingPins.AsNoTracking().SingleAsync(p => p.Id == pin.Id);
        await chains.UpsertAsync(AgentTaskRole.Code, TaskComplexity.Hard,
            new PutComplexityChainRequest([new ComplexityCandidateRequest(AgentKind.ClaudeCode, AgentModelLevel.Low)],
                RoutingPinProvenance.Human, "Code cell"), null, default);
        var codeCell = await db.ComplexityChains.AsNoTracking().SingleAsync(c => c.Role == AgentTaskRole.Code);
        var put = new PutComplexityChainRequest([new ComplexityCandidateRequest(AgentKind.Codex, AgentModelLevel.Frontier)], RoutingPinProvenance.Human, "Mutation only");
        Exception? error = null;
        try { await chains.UpsertAsync(AgentTaskRole.Mutation, TaskComplexity.Hard, put, null, default); }
        catch (Exception ex) { error = ex; }
        error.ShouldBeNull("Mutation cell API must accept the role: " + error);
        var service = new AgentTaskService(db, new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            options, new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System, NullLogger<AgentTaskService>.Instance,
            routingPins: pins, complexityRouting: routing);
        async Task AssertRouted(AgentKind kind, AgentModelLevel level)
        {
            var created = await service.CreateAsync(new CreateAgentTaskRequest(Goal: Unique("routing"), Role: AgentTaskRole.Mutation,
                Complexity: TaskComplexity.Hard), ManualCaller(workspace.Path), default);
            await using var read = CreateContext(schema);
            var row = await read.AgentTasks.SingleAsync(t => t.Id == created.Id);
            row.AgentKind.ShouldBe(kind);
            row.ModelLevel.ShouldBe(level);
            row.Kind.ShouldBe(AgentTaskKind.Worker);
            row.Status.ShouldBe(AgentTaskStatus.Queued);
            row.RoutingPinId.ShouldBeNull("the Code pin must never govern Mutation");
            row.Status = AgentTaskStatus.Succeeded;
            await read.SaveChangesAsync();
        }
        await AssertRouted(AgentKind.Codex, AgentModelLevel.Frontier);
        await chains.UpsertAsync(AgentTaskRole.Mutation, TaskComplexity.Hard,
            put with { Candidates = [new ComplexityCandidateRequest(AgentKind.ClaudeCode, AgentModelLevel.Medium)] }, null, default);
        await AssertRouted(AgentKind.ClaudeCode, AgentModelLevel.Medium);
        await chains.UpsertAsync(TaskComplexity.Hard,
            put with { Candidates = [new ComplexityCandidateRequest(AgentKind.Codex, AgentModelLevel.Low)] }, null, default);
        await chains.ClearAsync(AgentTaskRole.Mutation, TaskComplexity.Hard, default);
        (await chains.GetEffectiveAsync(AgentTaskRole.Mutation, TaskComplexity.Hard, default)).ResolvedFrom.ShouldBe("any");
        await AssertRouted(AgentKind.Codex, AgentModelLevel.Low);
        await chains.ClearAsync(TaskComplexity.Hard, default);
        options.Value.ComplexityChains["Hard"] = [new() { Kind = AgentKind.ClaudeCode, Level = AgentModelLevel.High }];
        (await chains.GetEffectiveAsync(AgentTaskRole.Mutation, TaskComplexity.Hard, default)).ResolvedFrom.ShouldBe("config");
        await AssertRouted(AgentKind.ClaudeCode, AgentModelLevel.High);
        await using var verify = CreateContext(schema);
        var preserved = await verify.RoutingPins.SingleAsync(p => p.Id == pin.Id);
        preserved.Role.ShouldBe(AgentTaskRole.Code);
        preserved.AgentKind.ShouldBe(AgentKind.Grok);
        preserved.ClearedAt.ShouldBeNull();
        preserved.ModelLevel.ShouldBe(AgentModelLevel.High);
        preserved.UpdatedAt.ShouldBe(codePin.UpdatedAt);
        var preservedCell = await verify.ComplexityChains.SingleAsync(c => c.Id == codeCell.Id);
        preservedCell.CandidatesJson.ShouldBe(codeCell.CandidatesJson);
        preservedCell.UpdatedAt.ShouldBe(codeCell.UpdatedAt);
        preservedCell.ClearedAt.ShouldBeNull();
        var mutationPin = await pins.UpsertAsync(new PutRoutingPinRequest(AgentTaskRole.Mutation,
            AgentKind: AgentKind.Codex, ModelLevel: AgentModelLevel.Frontier, Reason: "Mutation role accepted"), null, default);
        mutationPin.Role.ShouldBe(AgentTaskRole.Mutation);
    }

    [Test]
    [Arguments(AgentTaskStatus.Queued, false)]
    [Arguments(AgentTaskStatus.Dispatched, false)]
    [Arguments(AgentTaskStatus.Working, false)]
    [Arguments(AgentTaskStatus.Queued, true)]
    [Arguments(AgentTaskStatus.Dispatched, true)]
    [Arguments(AgentTaskStatus.Working, true)]
    public async Task C470_code_and_mutation_have_separate_slots(AgentTaskStatus status, bool reverse)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var occupied = reverse ? AgentTaskRole.Mutation : AgentTaskRole.Code;
        var requested = reverse ? AgentTaskRole.Code : AgentTaskRole.Mutation;
        await SeedOpenAsync(db, workspace.Path, (occupied, status));
        AgentTaskCreatedDto? created = null;
        Exception? error = null;
        try { created = await CreateService(db).CreateAsync(Request(Unique("independent"), requested), ManualCaller(workspace.Path), default); }
        catch (Exception ex) { error = ex; }
        error.ShouldBeNull("distinct Code and Mutation slots must admit: " + error);
        created.ShouldNotBeNull().Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    [Arguments(AgentTaskStatus.Queued, true)]
    [Arguments(AgentTaskStatus.Dispatched, true)]
    [Arguments(AgentTaskStatus.Working, true)]
    [Arguments(AgentTaskStatus.Succeeded, false)]
    [Arguments(AgentTaskStatus.Failed, false)]
    [Arguments(AgentTaskStatus.Canceled, false)]
    [Arguments(AgentTaskStatus.Blocked, false)]
    public async Task C470_second_mutation_is_role_limited(AgentTaskStatus status, bool limited)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var occupant = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Mutation, status, Unique("occupant"));
        var goal = Unique("second");
        var service = CreateService(db);
        if (limited)
        {
            var ex = await Should.ThrowAsync<ConcurrencyLimitException>(() => service.CreateAsync(Request(goal, AgentTaskRole.Mutation), ManualCaller(workspace.Path), default));
            ex.Concurrency.Axis.ShouldBe("role");
            ex.Concurrency.Role.ShouldBe("Mutation");
            ex.Concurrency.Count.ShouldBe(1);
            ex.Concurrency.Limit.ShouldBe(1);
            ex.Concurrency.Open.ShouldHaveSingleItem().TaskId.ShouldBe(occupant.Id);
            await using var verify = CreateContext(schema);
            (await verify.AgentTasks.CountAsync(t => t.Goal == goal)).ShouldBe(0);
        }
        else (await service.CreateAsync(Request(goal, AgentTaskRole.Mutation), ManualCaller(workspace.Path), default)).Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task C470_absolute_cap_includes_mutation()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedOpenAsync(db, workspace.Path, (AgentTaskRole.Mutation, AgentTaskStatus.Working),
            (AgentTaskRole.Custom, AgentTaskStatus.Queued), (AgentTaskRole.Debug, AgentTaskStatus.Dispatched));
        var goal = Unique("absolute");
        var ex = await Should.ThrowAsync<ConcurrencyLimitException>(() => CreateService(db).CreateAsync(Request(goal, AgentTaskRole.Code), ManualCaller(workspace.Path), default));
        ex.Concurrency.Axis.ShouldBe("absolute");
        ex.Concurrency.Count.ShouldBe(3);
        ex.Concurrency.Limit.ShouldBe(3);
        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.CountAsync(t => t.Goal == goal)).ShouldBe(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C470_readonly_mutation_is_refused(bool followUp)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var prior = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Succeeded, Unique("prior"));
        prior.AgentId = await SeedAgentAsync(db, workspace.Path);
        await db.SaveChangesAsync();
        var goal = Unique("readonly");
        var request = new CreateAgentTaskRequest(Goal: goal, Role: AgentTaskRole.Mutation,
            Workspace: WorkspaceMode.ReadOnly, FollowUpOnTask: followUp ? prior.Id.ToString() : null);
        var ex = await Should.ThrowAsync<ValidationException>(() => CreateService(db).CreateAsync(request, ManualCaller(workspace.Path), default));
        ex.StatusCode.ShouldBe(422);
        ex.Errors.ShouldContainKey("Workspace");
        ex.Errors["Workspace"].ShouldContain(e => e.Contains("writable"));
        await using var verify = CreateContext(schema);
        (await verify.AgentTasks.CountAsync(t => t.Goal == goal)).ShouldBe(0);
    }

    [Test]
    [Arguments(AgentTaskRole.Mutation, WorkspaceMode.Shared)]
    [Arguments(AgentTaskRole.Review, WorkspaceMode.ReadOnly)]
    [Arguments(AgentTaskRole.Test, WorkspaceMode.ReadOnly)]
    public async Task C470_shared_mutation_is_writable_worker(AgentTaskRole role, WorkspaceMode mode)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var created = await CreateService(db).CreateAsync(new CreateAgentTaskRequest(Goal: Unique("access"), Role: role, Workspace: mode), ManualCaller(workspace.Path), default);
        await using var verify = CreateContext(schema);
        var row = await verify.AgentTasks.SingleAsync(t => t.Id == created.Id);
        row.Role.ShouldBe(role);
        row.Workspace.ShouldBe(mode);
        row.Kind.ShouldBe(AgentTaskKind.Worker);
    }

    [Test]
    public async Task C470_project_and_null_buckets_stay_separate()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var project = await SeedProjectAsync(db);
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Mutation, AgentTaskStatus.Working, Unique("project"), project);
        var service = CreateService(db);
        await Should.ThrowAsync<ConcurrencyLimitException>(() => service.CreateAsync(Request(Unique("same"), AgentTaskRole.Mutation), new(null, null, workspace.Path, ProjectId: project), default));
        (await service.CreateAsync(Request(Unique("null"), AgentTaskRole.Mutation), ManualCaller(workspace.Path), default)).Status.ShouldBe(AgentTaskStatus.Queued);
        var other = await SeedProjectAsync(db);
        (await service.CreateAsync(Request(Unique("other"), AgentTaskRole.Mutation), new(null, null, workspace.Path, ProjectId: other), default)).Status.ShouldBe(AgentTaskStatus.Queued);
        await Should.ThrowAsync<ConcurrencyLimitException>(() => service.CreateAsync(Request(Unique("null-again"), AgentTaskRole.Mutation), ManualCaller(workspace.Path), default));
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

    private static AgentTaskService.Caller ParentCaller(AgentTask parent, string directory) =>
        new(parent, null, directory);

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

    private static async Task<Guid> SeedProjectAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"c0366-{Guid.NewGuid():N}",
            GitRepositoryUrl = "https://example.test/c0366.git",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static async Task<AgentTask> SeedOrchestratorParentAsync(
        AppDbContext db, string directory, Guid projectId)
    {
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = Unique("parent"),
            Goal = Unique("parent"),
            Kind = AgentTaskKind.Orchestrator,
            Role = AgentTaskRole.Custom,
            ProjectId = projectId,
            Status = AgentTaskStatus.Working,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = directory,
            CreatedAt = DateTime.UtcNow,
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync();
        return task;
    }

    private static async Task<AgentTask> SeedTaskAsync(
        AppDbContext db,
        string directory,
        AgentTaskRole role,
        AgentTaskStatus status,
        string title,
        Guid? projectId = null)
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
            ProjectId = projectId,
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
