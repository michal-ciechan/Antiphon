using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class AgentTaskPipelineStatusTests
{
    [Test]
    public async Task C654_Pipeline_local_in_flight_excludes_runner_bound_tasks()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Working, "local");
        var remote = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code, AgentTaskStatus.Working, "remote");
        remote.RunnerId = "server2";
        await db.SaveChangesAsync();

        var dto = await CreateService(db).GetAsync(CancellationToken.None);
        dto.InFlightAgainstCap.ShouldBe(1);
    }

    [Test]
    public async Task C654_Pipeline_local_in_flight_excludes_retained_capacity_wait()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var retained = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code,
            AgentTaskStatus.Working, "retained");
        retained.CapacityWaitRetained = true;
        await db.SaveChangesAsync();

        var options = Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 });
        var budgets = new HostBudgetService(db, new PipelineDirectory(), options, TimeProvider.System);
        var dto = await CreateService(db, options.Value, budgets: budgets).GetAsync(CancellationToken.None);
        dto.InFlightAgainstCap.ShouldBe(0);
        dto.Hosts.Single(h => h.HostId == "local").InFlight.ShouldBe(0);
    }

    [Test]
    public async Task C654_Pipeline_reports_local_and_runner_limits()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        var options = Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 });
        var budgets = new HostBudgetService(db, new PipelineDirectory(), options, TimeProvider.System);
        await budgets.UpsertAsync("server2", 3, "reserve", CancellationToken.None);

        var dto = await CreateService(db, options.Value, budgets: budgets).GetAsync(CancellationToken.None);
        dto.Hosts.Count.ShouldBe(2);
        dto.Hosts.Single(h => h.HostId == "local").EffectiveLimit.ShouldBe(4);
        dto.Hosts.Single(h => h.HostId == "server2").EffectiveLimit.ShouldBe(3);
    }

    [Test]
    public async Task C654_Queued_remote_task_names_its_full_host_budget()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var options = Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 });
        var budgets = new HostBudgetService(db, new PipelineDirectory(), options, TimeProvider.System);
        await budgets.UpsertAsync("server2", 0, "drain", CancellationToken.None);
        var queued = await SeedTaskAsync(db, workspace.Path, AgentTaskRole.Code,
            AgentTaskStatus.Queued, "remote queued", workspace: WorkspaceMode.ReadOnly);
        queued.RunnerId = "server2";
        await db.SaveChangesAsync();

        var dto = await CreateService(db, options.Value, budgets: budgets).GetAsync(CancellationToken.None);
        dto.Stages.Single(s => s.Role == AgentTaskRole.Code).Queued.Single(t => t.TaskId == queued.Id)
            .QueueReason.ShouldBe(AgentTaskPipelineStatusService.QueueReasonHostBudget);
    }

    private sealed class PipelineDirectory : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => null!;
        public ISessionRunnerClient Resolve(string? id) => null!;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid id, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? id, CancellationToken ct) => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
        public IReadOnlyList<string> KnownRunnerIds => ["server2"];
        public Guid? GetLiveStoreId(string? id) => null;
        public int? DeclaredCapacity(string id) => id == "server2" ? 10 : null;
    }
}
