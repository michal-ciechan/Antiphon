using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed partial class DispatchHoldVisibilityTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task C654_Local_budget_holds_new_dispatch_with_its_limit(int budget)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var at = UtcMs();
        await using var world = CreateWorld(schema.ConnectionString,
            new FakeTimeProvider(new DateTimeOffset(at, TimeSpan.Zero)), new FakeLease(), maxConcurrent: 6);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        await SeedDispatchedAsync(schema, workspace.Path, at);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, at);
        await world.Budgets.UpsertAsync("local", budget, "test limit", CancellationToken.None);

        var tick = await world.Dispatcher.TickAsync(CancellationToken.None);
        tick.SkippedConcurrency.ShouldBe(1);
        await using var db = CreateContext(schema);
        var detail = (await HeldAsync(db, queued.Id)).Single().Detail;
        detail.ShouldContain($"local 1/{budget}");
        (await db.AgentTasks.SingleAsync(t => t.Id == queued.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
    }

    [Test]
    public async Task C654_Lowering_budget_leaves_working_tasks_and_sessions_untouched()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var at = UtcMs();
        await using var world = CreateWorld(schema.ConnectionString,
            new FakeTimeProvider(new DateTimeOffset(at, TimeSpan.Zero)), new FakeLease(), maxConcurrent: 6);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var active = await SeedDispatchedAsync(schema, workspace.Path, at);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, at);
        await world.Budgets.UpsertAsync("local", 0, "drain", CancellationToken.None);
        await world.Dispatcher.TickAsync(CancellationToken.None);

        await using var db = CreateContext(schema);
        (await db.AgentTasks.SingleAsync(t => t.Id == active.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
        (await HeldAsync(db, queued.Id)).Single().Detail.ShouldContain("local 1/0");
        world.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task C654_Raising_budget_releases_the_queued_task_on_the_next_tick()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var at = UtcMs();
        await using var world = CreateWorld(schema.ConnectionString,
            new FakeTimeProvider(new DateTimeOffset(at, TimeSpan.Zero)), new FakeLease(), maxConcurrent: 6);
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var queued = await SeedQueuedAsync(schema, workspace.Path, agentId, at);
        await world.Budgets.UpsertAsync("local", 0, "drain", CancellationToken.None);
        (await world.Dispatcher.TickAsync(CancellationToken.None)).Dispatched.ShouldBe(0);
        await world.Budgets.UpsertAsync("local", 1, "resume", CancellationToken.None);
        (await world.Dispatcher.TickAsync(CancellationToken.None)).Dispatched.ShouldBe(1);
        await using var db = CreateContext(schema);
        (await db.AgentTasks.SingleAsync(t => t.Id == queued.Id)).Status.ShouldBe(AgentTaskStatus.Dispatched);
    }

    [Test]
    public async Task C654_Counted_slot_claim_excludes_runner_bound_tasks()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var at = UtcMs();
        await using var world = CreateWorld(schema.ConnectionString,
            new FakeTimeProvider(new DateTimeOffset(at, TimeSpan.Zero)), new FakeLease(), maxConcurrent: 1);
        await SeedDispatchedAsync(schema, workspace.Path, at, runnerId: "server2");
        await using var db = CreateContext(schema);
        (await world.Recovery.TryClaimCountedSlotAsync(db, 1, retainedReturn: true, CancellationToken.None))
            .ShouldBeTrue();
    }

    [Test]
    public async Task C654_Runner_budget_holds_a_queued_task_until_a_seat_opens()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var at = UtcMs();
        await using var world = CreateWorld(schema.ConnectionString,
            new FakeTimeProvider(new DateTimeOffset(at, TimeSpan.Zero)), new FakeLease());
        var (agentId, _) = await ModelAvailabilityDispatcherTests.SeedWarmAgentAsync(
            schema.ConnectionString, workspace.Path);
        var active = await SeedDispatchedAsync(schema, workspace.Path, at, runnerId: "server2");
        await using (var db = CreateContext(schema))
        {
            db.AgentSessions.Add(new AgentSession
            {
                Id = Guid.NewGuid(), DefinitionName = "claude", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running, Cwd = workspace.Path, Cols = 80, Rows = 24,
                CreatedAt = at, StartedAt = at, LastSeenAt = at, RunnerId = "server2",
            });
            await db.SaveChangesAsync();
        }
        var queuedDir = Path.Combine(workspace.Path, "queued-on-runner");
        Directory.CreateDirectory(queuedDir);
        var queued = await SeedQueuedAsync(schema, queuedDir, agentId, at, runnerId: "server2");
        await world.Budgets.UpsertAsync("server2", 1, "reserve", CancellationToken.None);

        (await world.Dispatcher.TickAsync(CancellationToken.None)).Dispatched.ShouldBe(0);
        await using (var db = CreateContext(schema))
        {
            (await HeldAsync(db, queued.Id)).Single().Detail
                .ShouldContain("server2 1/1 (configured 1, runner declares 10)");
            (await db.AgentTasks.SingleAsync(t => t.Id == queued.Id)).Status.ShouldBe(AgentTaskStatus.Queued);
        }

        await SettleAsync(schema, active.Id, at);
    }

    [Test]
    public async Task C654_Local_budget_gates_resuming_a_retained_capacity_wait()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var at = UtcMs();
        await using var world = CreateWorld(schema.ConnectionString,
            new FakeTimeProvider(new DateTimeOffset(at, TimeSpan.Zero)), new FakeLease());
        var retained = await SeedDispatchedAsync(schema, workspace.Path, at);
        await MarkRetainedAsync(schema, retained.Id);
        await world.Budgets.UpsertAsync("local", 0, "drain", CancellationToken.None);

        await world.Dispatcher.TickAsync(CancellationToken.None);
        await using (var db = CreateContext(schema))
            (await db.AgentTasks.SingleAsync(t => t.Id == retained.Id)).CapacityWaitRetained.ShouldBeTrue();

        await world.Budgets.UpsertAsync("local", 1, "resume", CancellationToken.None);
        await world.Dispatcher.TickAsync(CancellationToken.None);
        await using (var db = CreateContext(schema))
            (await db.AgentTasks.SingleAsync(t => t.Id == retained.Id)).CapacityWaitRetained.ShouldBeFalse();
    }

    [Test]
    public async Task C654_Runner_budget_gates_resuming_a_retained_capacity_wait()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var workspace = new TempWorkspace();
        var at = UtcMs();
        await using var world = CreateWorld(schema.ConnectionString,
            new FakeTimeProvider(new DateTimeOffset(at, TimeSpan.Zero)), new FakeLease());
        var retained = await SeedDispatchedAsync(schema, workspace.Path, at, runnerId: "server2");
        await MarkRetainedAsync(schema, retained.Id);
        var active = await SeedDispatchedAsync(schema, workspace.Path, at, runnerId: "server2");
        await world.Budgets.UpsertAsync("server2", 1, "reserve", CancellationToken.None);

        await world.Dispatcher.TickAsync(CancellationToken.None);
        await using (var db = CreateContext(schema))
            (await db.AgentTasks.SingleAsync(t => t.Id == retained.Id)).CapacityWaitRetained.ShouldBeTrue();

        await SettleAsync(schema, active.Id, at);
        await world.Dispatcher.TickAsync(CancellationToken.None);
        await using (var db = CreateContext(schema))
            (await db.AgentTasks.SingleAsync(t => t.Id == retained.Id)).CapacityWaitRetained.ShouldBeFalse();
    }

    private static async Task MarkRetainedAsync(IsolatedTestSchema schema, Guid taskId)
    {
        await using var db = CreateContext(schema);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == taskId);
        task.Status = AgentTaskStatus.Working;
        task.CapacityWaitRetained = true;
        task.CapacityWaitId = Guid.NewGuid();
        await db.SaveChangesAsync();
    }

    [Test]
    public void C654_Host_budget_detail_names_configured_and_declared_limits()
    {
        DispatchHoldDetails.HostBudget("server2", 3, 3, 3, 10)
            .ShouldContain("server2 3/3 (configured 3, runner declares 10)");
    }

    private sealed class BudgetDirectory : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => null!;
        public ISessionRunnerClient Resolve(string? runnerId) => null!;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct) => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
        public IReadOnlyList<string> KnownRunnerIds => ["server2"];
        public Guid? GetLiveStoreId(string? id) => null;
        public int? DeclaredCapacity(string id) => id == "server2" ? 10 : null;
    }
}
