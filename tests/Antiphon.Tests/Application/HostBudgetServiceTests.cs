using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class HostBudgetServiceTests
{
    [Test]
    public async Task Runner_limit_is_the_minimum_of_budget_and_declaration()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var service = Service(db);
        (await service.UpsertAsync("server2", 3, "reserve room", CancellationToken.None)).Effective.ShouldBe(3);
        (await service.EffectiveAsync("server2", CancellationToken.None)).Declared.ShouldBe(10);
    }

    [Test]
    public async Task Null_budget_uses_local_config_and_remote_declaration()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var service = Service(db);
        (await service.EffectiveAsync("local", CancellationToken.None)).Effective.ShouldBe(4);
        (await service.EffectiveAsync("server2", CancellationToken.None)).Effective.ShouldBe(10);
    }

    [Test]
    public async Task Zero_budget_drains_without_changing_declaration()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var limit = await Service(db).UpsertAsync("server2", 0, "drain", CancellationToken.None);
        limit.Effective.ShouldBe(0);
        limit.Declared.ShouldBe(10);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(513)]
    public async Task Out_of_range_budget_is_rejected(int value)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await Should.ThrowAsync<ValidationException>(() =>
            Service(db).UpsertAsync("local", value, "bad", CancellationToken.None));
    }

    [Test]
    public async Task Missing_reason_is_rejected()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await Should.ThrowAsync<ValidationException>(() =>
            Service(db).UpsertAsync("local", 2, " ", CancellationToken.None));
    }

    [Test]
    public async Task Unknown_host_is_not_created()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        await Should.ThrowAsync<NotFoundException>(() =>
            Service(db).UpsertAsync("missing", 2, "test", CancellationToken.None));
    }

    [Test]
    public async Task Each_write_revises_the_budget_and_audits_old_and_new_values()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var service = Service(db);
        (await service.UpsertAsync("local", 3, "first", CancellationToken.None)).Revision.ShouldBe(1);
        (await service.UpsertAsync("local", 2, "second", CancellationToken.None)).Revision.ShouldBe(2);
        var incidents = db.AgentIncidents.Where(i => i.Message.Contains("budget")).ToList();
        incidents.Count.ShouldBe(2);
        incidents[1].Message.ShouldContain("3 -> 2");
        incidents[1].Message.ShouldContain("second");
    }

    private static HostBudgetService Service(AppDbContext db) =>
        new(db, new Directory(), Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 }), TimeProvider.System);

    private sealed class Directory : ISessionRunnerDirectory
    {
        public ISessionRunnerClient Local => null!;
        public ISessionRunnerClient Resolve(string? runnerId) => null!;
        public Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerOwner?>(null);
        public Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct) => Task.FromResult<SessionRunnerBinding>(SessionRunnerBinding.Local.Instance);
        public Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct) => Task.FromResult<RunnerInventory>(new RunnerInventory.Available([]));
        public IReadOnlyList<string> KnownRunnerIds => ["local", "server2"];
        public Guid? GetLiveStoreId(string? runnerId) => null;
        public int? DeclaredCapacity(string runnerId) => runnerId == "server2" ? 10 : null;
    }
}
