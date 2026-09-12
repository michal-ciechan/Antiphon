using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

internal sealed class ControlledTestDbOperations : TestDbOperations
{
    public const string Shared =
        "Host=127.0.0.1;Port=54329;Database=antiphon_test;Username=test;Password=test";

    public Task? StartGate;
    public Task? AfterStartGate;
    public Task? AfterMigrateGate;
    public Task? AfterIsTemplateGate;
    public Task? BeforeAllowConnectionsGate;
    public string? FaultAt;
    public bool DisposeOwnedThrows;
    public string? CloneFault;
    public string? DropFault;
    public int ClearAll;
    public SynchronizationContext? BootstrapContext;

    public override Task<OwnedTestDbContainer> CreateAsync()
    {
        BootstrapContext ??= SynchronizationContext.Current;
        if (FaultAt == "construct")
            throw new InvalidOperationException("C476-FAULT construct");
        return Task.FromResult(new OwnedTestDbContainer
        {
            ConnectionString = Shared,
            Id = "controlled-container"
        });
    }

    public override async Task StartAsync(OwnedTestDbContainer container)
    {
        BootstrapContext ??= SynchronizationContext.Current;
        if (StartGate is not null)
            await StartGate.ConfigureAwait(false);
        if (FaultAt == "start")
            throw new InvalidOperationException("C476-FAULT start");
        container.ConnectionString = Shared;
        container.Id = "controlled-container";
    }

    public override Task AfterStartAsync() => AfterStartGate ?? Task.CompletedTask;

    public override Task MigrateAsync(string connectionString)
    {
        BootstrapContext ??= SynchronizationContext.Current;
        if (FaultAt == "migrate")
            throw new InvalidOperationException("C476-FAULT migrate");
        return Task.CompletedTask;
    }

    public override Task AfterMigrateAsync() => AfterMigrateGate ?? Task.CompletedTask;

    public override async Task ProtectTemplateAsync(
        string maintenanceConnectionString,
        string sharedDatabaseName,
        string templateDatabaseName)
    {
        BootstrapContext ??= SynchronizationContext.Current;
        if (AfterIsTemplateGate is not null)
            await AfterIsTemplateGate.ConfigureAwait(false);
        if (FaultAt == "protect" || FaultAt == "protect-and-cleanup-fails")
        {
            if (FaultAt == "protect-and-cleanup-fails")
                DisposeOwnedThrows = true;
            throw new InvalidOperationException("C476-FAULT protect");
        }

        if (BeforeAllowConnectionsGate is not null)
            await BeforeAllowConnectionsGate.ConfigureAwait(false);
    }

    public override Task AfterIsTemplateAsync() => Task.CompletedTask;

    public override Task BeforeAllowConnectionsAsync() => Task.CompletedTask;

    public override void ClearPool(string connectionString)
    {
    }

    public override void ClearAllPools() => Interlocked.Increment(ref ClearAll);

    public override Task CloneAsync(TestDbReadyState ready, string databaseName, SemaphoreSlim cloneLock)
    {
        if (CloneFault is not null)
            throw new InvalidOperationException(CloneFault);
        return Task.CompletedTask;
    }

    public override Task DropAsync(TestDbReadyState ready, string databaseName)
    {
        if (DropFault is not null)
            throw new InvalidOperationException(DropFault);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeOwnedAsync(OwnedTestDbContainer container)
    {
        if (DisposeOwnedThrows)
            throw new InvalidOperationException("C476-CLEANUP");
        return ValueTask.CompletedTask;
    }
}

[Category("Unit")]
[NotInParallel]
public sealed class TestDbFixtureLifecycleTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [Test]
    public async Task Eight_concurrent_first_callers_share_one_bootstrap()
    {
        var ops = new ControlledTestDbOperations();
        var start = NewGate();
        ops.StartGate = start.Task;
        var lifecycle = new TestDbFixtureLifecycle(ops);

        var connection = new Task<string>[2];
        var options = new Task<string>[2];
        var clones = new Task<IsolatedTestSchema>[2];
        connection[0] = LongRunning(() => lifecycle.ConnectionString);
        connection[1] = LongRunning(() => lifecycle.ConnectionString);
        var maintenance = LongRunning(() => lifecycle.MaintenanceConnectionString);
        options[0] = LongRunning(() => ConnectionOf(lifecycle.CreateDbContextOptions()));
        options[1] = LongRunning(() => ConnectionOf(lifecycle.CreateDbContextOptions()));
        var context = LongRunning(() => lifecycle.CreateDbContext().Database.GetConnectionString()!);
        clones[0] = Task.Run(() => lifecycle.CreateIsolatedSchemaAsync());
        clones[1] = Task.Run(() => lifecycle.CreateIsolatedSchemaAsync());

        await Task.Delay(200);
        connection[0].IsCompleted.ShouldBeFalse();
        start.TrySetResult();

        var results = new[] { connection[0], connection[1], options[0], options[1], context };
        await Task.WhenAll(connection[0], connection[1], options[0], options[1], context, maintenance, clones[0], clones[1]).WaitAsync(Bound);

        lifecycle.Create.ShouldBe(1);
        lifecycle.Start.ShouldBe(1);
        lifecycle.Migrate.ShouldBe(1);
        lifecycle.Protect.ShouldBe(1);
        foreach (var result in results)
            result.Result.ShouldBe(ControlledTestDbOperations.Shared);
        var names = clones.Select(c => new NpgsqlConnectionStringBuilder(c.Result.ConnectionString).Database).ToArray();
        names.Distinct().Count().ShouldBe(2);
        await clones[0].Result.DisposeAsync();
        await clones[1].Result.DisposeAsync();
        await lifecycle.DisposeAsync();
    }

    [Test]
    [Arguments("connection")]
    [Arguments("maintenance")]
    [Arguments("options")]
    [Arguments("context")]
    [Arguments("clone")]
    public async Task Each_default_entry_point_initializes_exactly_once(string kind)
    {
        var ops = new ControlledTestDbOperations();
        var lifecycle = new TestDbFixtureLifecycle(ops);
        await Invoke(lifecycle, kind);
        lifecycle.Create.ShouldBe(1);
        await Invoke(lifecycle, kind);
        lifecycle.Create.ShouldBe(1);
        if (kind == "maintenance")
        {
            var builder = new NpgsqlConnectionStringBuilder(lifecycle.MaintenanceConnectionString);
            builder.Database.ShouldBe("postgres");
            builder.Pooling.ShouldBeFalse();
        }

        await lifecycle.DisposeAsync();
    }

    [Test]
    [Arguments("after-start")]
    [Arguments("after-migrate")]
    [Arguments("after-IS_TEMPLATE")]
    [Arguments("before-ALLOW_CONNECTIONS")]
    public async Task Readiness_waits_for_template_protection(string stage)
    {
        var ops = new ControlledTestDbOperations();
        var gate = NewGate();
        switch (stage)
        {
            case "after-start": ops.AfterStartGate = gate.Task; break;
            case "after-migrate": ops.AfterMigrateGate = gate.Task; break;
            case "after-IS_TEMPLATE": ops.AfterIsTemplateGate = gate.Task; break;
            default: ops.BeforeAllowConnectionsGate = gate.Task; break;
        }

        var lifecycle = new TestDbFixtureLifecycle(ops);
        var sync = LongRunning(() => lifecycle.ConnectionString);
        var asyncWaiter = lifecycle.CreateIsolatedSchemaAsync();
        await Task.Delay(200);
        sync.IsCompleted.ShouldBeFalse();
        asyncWaiter.IsCompleted.ShouldBeFalse();
        gate.TrySetResult();
        await sync.WaitAsync(Bound);
        await asyncWaiter.WaitAsync(Bound);
        await asyncWaiter.Result.DisposeAsync();
        await lifecycle.DisposeAsync();
    }

    [Test]
    public async Task Synchronous_access_completes_under_a_single_threaded_synchronization_context()
    {
        var ops = new ControlledTestDbOperations();
        var start = NewGate();
        ops.StartGate = start.Task;
        var lifecycle = new TestDbFixtureLifecycle(ops);
        var ctx = new QueuedSynchronizationContext();
        var call = Task.Factory.StartNew(
            () =>
            {
                SynchronizationContext.SetSynchronizationContext(ctx);
                return lifecycle.ConnectionString;
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        _ = Task.Delay(100).ContinueWith(_ => start.TrySetResult());
        var cs = await call.WaitAsync(Bound);
        cs.ShouldBe(ControlledTestDbOperations.Shared);
        ops.BootstrapContext.ShouldBeNull();
        await lifecycle.DisposeAsync();
    }

    [Test]
    public async Task Explicit_options_never_initialize()
    {
        var ops = new ControlledTestDbOperations();
        var lifecycle = new TestDbFixtureLifecycle(ops);
        var options = TestDbFixtureLifecycle.BuildOptions(
            "Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p");
        ConnectionOf(options).ShouldBe("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p");
        _ = new TestDbFixture();
        lifecycle.Create.ShouldBe(0);
        lifecycle.IsRequested.ShouldBeFalse();
        await lifecycle.DisposeAsync();
    }

    [Test]
    [Arguments("never-requested")]
    [Arguments("starting")]
    [Arguments("ready")]
    [Arguments("faulted")]
    [Arguments("repeated")]
    [Arguments("access-after")]
    [Arguments("clone-dispose-after")]
    public async Task Teardown_matrix(string shape)
    {
        var ops = new ControlledTestDbOperations();
        var lifecycle = new TestDbFixtureLifecycle(ops);
        switch (shape)
        {
            case "never-requested":
                await lifecycle.DisposeAsync().WaitAsync(Bound);
                lifecycle.Create.ShouldBe(0);
                lifecycle.DisposeOwned.ShouldBe(0);
                break;
            case "starting":
            {
                var start = NewGate();
                ops.StartGate = start.Task;
                var waiter = LongRunning(() => lifecycle.ConnectionString);
                while (Volatile.Read(ref lifecycle.Create) == 0)
                    await Task.Delay(10);
                var teardown = lifecycle.DisposeAsync();
                await Task.Delay(50);
                teardown.IsCompleted.ShouldBeFalse();
                start.TrySetResult();
                await teardown.WaitAsync(Bound);
                try { await waiter.WaitAsync(Bound); } catch (ObjectDisposedException) { }
                lifecycle.DisposeOwned.ShouldBe(1);
                break;
            }
            case "ready":
                _ = lifecycle.ConnectionString;
                await lifecycle.DisposeAsync().WaitAsync(Bound);
                lifecycle.DisposeOwned.ShouldBe(1);
                break;
            case "faulted":
                ops.FaultAt = "start";
                (await Should.ThrowAsync<InvalidOperationException>(() => lifecycle.EnsureReadyAsync()))
                    .Message.ShouldContain("C476-FAULT");
                lifecycle.DisposeOwned.ShouldBe(1);
                var owned = lifecycle.DisposeOwned;
                await lifecycle.DisposeAsync().WaitAsync(Bound);
                lifecycle.DisposeOwned.ShouldBe(owned);
                break;
            case "repeated":
                _ = lifecycle.ConnectionString;
                await Task.WhenAll(
                    lifecycle.DisposeAsync(),
                    lifecycle.DisposeAsync(),
                    lifecycle.DisposeAsync()).WaitAsync(Bound);
                lifecycle.DisposeOwned.ShouldBe(1);
                break;
            case "access-after":
                _ = lifecycle.ConnectionString;
                var created = lifecycle.Create;
                await lifecycle.DisposeAsync();
                Should.Throw<ObjectDisposedException>(() => _ = lifecycle.ConnectionString)
                    .ObjectName.ShouldBe(nameof(TestDbFixture));
                lifecycle.Create.ShouldBe(created);
                break;
            case "clone-dispose-after":
            {
                var clone = await lifecycle.CreateIsolatedSchemaAsync();
                var createdClones = lifecycle.Create;
                await lifecycle.DisposeAsync();
                (await Should.ThrowAsync<ObjectDisposedException>(() => clone.DisposeAsync().AsTask()))
                    .ObjectName.ShouldBe(nameof(TestDbFixture));
                lifecycle.Create.ShouldBe(createdClones);
                break;
            }
        }
    }

    [Test]
    [Arguments("construct")]
    [Arguments("start")]
    [Arguments("migrate")]
    [Arguments("protect")]
    [Arguments("protect-and-cleanup-fails")]
    public async Task Bootstrap_fault_is_terminal_for_all_waiters(string stage)
    {
        var ops = new ControlledTestDbOperations { FaultAt = stage };
        var lifecycle = new TestDbFixtureLifecycle(ops);
        var waiters = Enumerable.Range(0, 4)
            .Select(_ => LongRunning(() => lifecycle.ConnectionString))
            .ToArray();
        foreach (var waiter in waiters)
        {
            var ex = await Should.ThrowAsync<Exception>(async () => await waiter.WaitAsync(Bound));
            Flatten(ex).ShouldContain(m => m.Contains("C476-FAULT", StringComparison.Ordinal));
        }

        var created = lifecycle.Create;
        var fifth = await Should.ThrowAsync<Exception>(async () =>
            await LongRunning(() => lifecycle.ConnectionString).WaitAsync(Bound));
        Flatten(fifth).ShouldContain(m => m.Contains("C476-FAULT", StringComparison.Ordinal));
        lifecycle.Create.ShouldBe(created);
        if (stage == "construct")
            lifecycle.DisposeOwned.ShouldBe(0);
        else
            lifecycle.DisposeOwned.ShouldBe(1);
        if (stage == "protect-and-cleanup-fails")
        {
            Flatten(waiters[0].Exception!).ShouldContain(m => m.Contains("C476-FAULT", StringComparison.Ordinal));
            Flatten(waiters[0].Exception!).ShouldContain(m => m.Contains("C476-CLEANUP", StringComparison.Ordinal));
        }

        await lifecycle.DisposeAsync();
    }

    [Test]
    public async Task Clone_request_on_a_faulted_lifecycle_does_not_drop()
    {
        var ops = new ControlledTestDbOperations { FaultAt = "migrate" };
        var lifecycle = new TestDbFixtureLifecycle(ops);
        await Should.ThrowAsync<InvalidOperationException>(() => lifecycle.EnsureReadyAsync());
        var ex = await Should.ThrowAsync<Exception>(() => lifecycle.CreateIsolatedSchemaAsync());
        Flatten(ex).ShouldContain(m => m.Contains("C476-FAULT", StringComparison.Ordinal));
        lifecycle.Drop.ShouldBe(0);
        lifecycle.Create.ShouldBe(1);
        await lifecycle.DisposeAsync();
    }

    [Test]
    [Arguments("drop-succeeds")]
    [Arguments("drop-fails")]
    public async Task Clone_creation_failure_drops_once_and_preserves_the_clone_error(string shape)
    {
        var ops = new ControlledTestDbOperations
        {
            CloneFault = "C476-CLONE",
            DropFault = shape == "drop-fails" ? "C476-DROP" : null
        };
        var lifecycle = new TestDbFixtureLifecycle(ops);
        var ex = await Should.ThrowAsync<Exception>(() => lifecycle.CreateIsolatedSchemaAsync());
        lifecycle.Drop.ShouldBe(1);
        Flatten(ex).ShouldContain(m => m.Contains("C476-CLONE", StringComparison.Ordinal));
        if (shape == "drop-fails")
            Flatten(ex).ShouldContain(m => m.Contains("C476-DROP", StringComparison.Ordinal));
        await lifecycle.DisposeAsync();
    }

    [Test]
    public async Task Bootstrap_clears_only_the_shared_pool()
    {
        var ops = new ControlledTestDbOperations();
        var lifecycle = new TestDbFixtureLifecycle(ops);
        var shared = lifecycle.ConnectionString;
        lifecycle.ClearedPools.ToArray().ShouldBe([shared]);
        ops.ClearAll.ShouldBe(0);
        await lifecycle.DisposeAsync();
    }

    [Test]
    public async Task Explicit_options_are_not_blocked_by_an_in_flight_bootstrap()
    {
        var ops = new ControlledTestDbOperations();
        var start = NewGate();
        ops.StartGate = start.Task;
        var lifecycle = new TestDbFixtureLifecycle(ops);
        var waiter = LongRunning(() => lifecycle.ConnectionString);
        while (Volatile.Read(ref lifecycle.Create) == 0)
            await Task.Delay(10);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var options = TestDbFixtureLifecycle.BuildOptions(
            "Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p");
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
        waiter.IsCompleted.ShouldBeFalse();
        ConnectionOf(options).ShouldBe("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p");
        start.TrySetResult();
        await waiter.WaitAsync(Bound);
        await lifecycle.DisposeAsync();
    }

    private static async Task Invoke(TestDbFixtureLifecycle lifecycle, string kind)
    {
        switch (kind)
        {
            case "connection":
                _ = lifecycle.ConnectionString;
                break;
            case "maintenance":
                _ = lifecycle.MaintenanceConnectionString;
                break;
            case "options":
                _ = lifecycle.CreateDbContextOptions();
                break;
            case "context":
                _ = lifecycle.CreateDbContext();
                break;
            default:
                await using (var clone = await lifecycle.CreateIsolatedSchemaAsync())
                {
                }

                break;
        }
    }

    private static string ConnectionOf(Microsoft.EntityFrameworkCore.DbContextOptions<Antiphon.Server.Infrastructure.Data.AppDbContext> options) =>
        new Antiphon.Server.Infrastructure.Data.AppDbContext(options).Database.GetConnectionString()!;

    private static Task<string> LongRunning(Func<string> work) =>
        Task.Factory.StartNew(
            work,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static IEnumerable<string> Flatten(Exception ex)
    {
        yield return ex.Message;
        if (ex.InnerException is not null)
        {
            foreach (var inner in Flatten(ex.InnerException))
                yield return inner;
        }

        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten))
                yield return inner;
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Queue without a pump so a captured-context bootstrap deadlocks the sync adapter.
        }

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }
}
