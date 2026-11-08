using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-1743. Desktop storage stays in-memory unless Hangfire:Storage is PostgreSql.</summary>
[Category("Unit")]
[NotInParallel("HangfireStorage")]
public sealed class HangfireStorageSelectionTests
{
    [Test]
    public async Task Default_settings_stay_on_in_memory_storage_for_eight_days()
    {
        var settings = new HangfireSettings();
        settings.Storage.ShouldBe(HangfireSettings.InMemoryStorageName);
        settings.SchemaName.ShouldBe("hangfire");
        settings.UsesPostgreSql.ShouldBeFalse();
        settings.ServerEnabled.ShouldBeTrue();
        settings.HistoryRetentionDays.ShouldBe(8);

        var storage = HangfireConfiguration.CreateStorage(settings, connectionString: null)
            .ShouldBeOfType<InMemoryStorage>();
        storage.Options.MaxExpirationTime.ShouldBe(TimeSpan.FromDays(8));

        var services = new ServiceCollection();
        services.AddHangfire(config => HangfireConfiguration.ConfigureStorage(config, settings, connectionString: null));
        await using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<JobStorage>().ShouldBeOfType<InMemoryStorage>();
        resolved.Options.MaxExpirationTime.ShouldBe(TimeSpan.FromDays(8));
    }

    [Test]
    public async Task Validator_rejects_an_unknown_storage_and_the_public_schema()
    {
        var validator = new HangfireSettingsValidator();
        validator.Validate(null, new HangfireSettings()).Succeeded.ShouldBeTrue();
        validator.Validate(null, new HangfireSettings { Storage = "postgresql" }).Failed.ShouldBeTrue();
        validator.Validate(null, new HangfireSettings { Storage = "Memory" }).Failed.ShouldBeTrue();
        validator.Validate(null, new HangfireSettings { SchemaName = "public" })
            .Failures.ShouldContain(failure => failure.Contains("not public", StringComparison.Ordinal));
        validator.Validate(null, new HangfireSettings { SchemaName = "Hangfire" }).Failed.ShouldBeTrue();
        validator.Validate(null, new HangfireSettings { SchemaName = "" }).Failed.ShouldBeTrue();
        validator.Validate(null, new HangfireSettings { HistoryRetentionDays = 0 }).Failed.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Desktop_configuration_does_not_turn_durable_storage_on()
    {
        using var appsettings = System.Text.Json.JsonDocument.Parse(
            DockerStackDocuments.Read("server/appsettings.json"));
        var hangfire = appsettings.RootElement.GetProperty("Hangfire");
        hangfire.GetProperty("ServerEnabled").GetBoolean().ShouldBeTrue();
        hangfire.GetProperty("HistoryRetentionDays").GetInt32().ShouldBe(8);
        hangfire.TryGetProperty("Storage", out _).ShouldBeFalse();
        hangfire.TryGetProperty("SchemaName", out _).ShouldBeFalse();

        var compose = DockerStackDocuments.Read("docker-compose.yml");
        compose.ShouldContain("Hangfire__ServerEnabled: \"false\"");
        compose.ShouldNotContain("Hangfire__Storage");
        compose.ShouldNotContain("Hangfire__SchemaName");

        DockerStackDocuments.Read("Antiphon.AppHost/Program.cs").ShouldNotContain("Hangfire");

        var contract = DockerStackDocuments.Read("docs/docker-stack.md");
        contract.IndexOf("Storage is always in-memory", StringComparison.Ordinal).ShouldBe(-1);
        contract.IndexOf("Hangfire:Storage defaults to InMemory", StringComparison.Ordinal)
            .ShouldBeGreaterThanOrEqualTo(0);
        contract.IndexOf("a second worker against that database is refused", StringComparison.Ordinal)
            .ShouldBeGreaterThanOrEqualTo(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Disabled_census_removes_a_previously_registered_job()
    {
        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        var manager = new RecurringJobManager(storage);
        HangfireConfiguration.AddOrUpdateCensusJob(manager, new ZombieCensusSettings());

        HangfireConfiguration.RegisterCensusJob(manager, new ZombieCensusSettings { Enabled = false });

        using var connection = storage.GetConnection();
        connection.GetRecurringJobs().ShouldNotContain(job => job.Id == "antiphon:zombie-census");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Disabled_residue_removes_a_previously_registered_job()
    {
        await AssertInMemoryRemovalAsync(
            "antiphon:worktree-residue",
            manager => HangfireConfiguration.RegisterWorktreeResidueJob(
                manager, new WorktreeResidueSettings { Enabled = true }),
            manager => HangfireConfiguration.RegisterWorktreeResidueJob(
                manager, new WorktreeResidueSettings { Enabled = false }));
    }

    [Test]
    public async Task Disabled_watchdog_removes_a_previously_registered_job()
    {
        await AssertInMemoryRemovalAsync(
            ExpectationWatchdogJob.RecurringJobId,
            manager => HangfireConfiguration.RegisterExpectationWatchdogJob(
                manager, new HangfireSettings(), new ExpectationWatchdogSettings { Enabled = true }),
            manager => HangfireConfiguration.RegisterExpectationWatchdogJob(
                manager, new HangfireSettings(), new ExpectationWatchdogSettings { Enabled = false }));
    }

    [Test]
    public async Task Disabled_slot_reconcile_removes_a_previously_registered_job()
    {
        await AssertInMemoryRemovalAsync(
            RunnerSlotReconcileJob.RecurringJobId,
            manager => HangfireConfiguration.RegisterRunnerSlotReconcileJob(
                manager, new PhoneHomeRunnerSettings { Enabled = true }),
            manager => HangfireConfiguration.RegisterRunnerSlotReconcileJob(
                manager, new PhoneHomeRunnerSettings { Enabled = false }));
    }

    [Test]
    public async Task Disabled_retire_removes_a_previously_registered_job()
    {
        await AssertInMemoryRemovalAsync(
            RunnerRetireJob.RecurringJobId,
            manager => HangfireConfiguration.RegisterRunnerRetireJob(
                manager, new PhoneHomeRunnerSettings { Enabled = true }),
            manager => HangfireConfiguration.RegisterRunnerRetireJob(
                manager, new PhoneHomeRunnerSettings { Enabled = false }));
    }

    private static async Task AssertInMemoryRemovalAsync(
        string jobId, Action<IRecurringJobManager> enable, Action<IRecurringJobManager> disable)
    {
        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        var manager = new RecurringJobManager(storage);
        enable(manager);
        using (var before = storage.GetConnection())
            before.GetRecurringJobs().ShouldContain(job => job.Id == jobId);

        disable(manager);

        using var connection = storage.GetConnection();
        connection.GetRecurringJobs().ShouldNotContain(job => job.Id == jobId);
        await Task.CompletedTask;
    }

    [Test]
    [Timeout(20_000)]
    public async Task Unreachable_postgresql_storage_refuses_startup()
    {
        const string password = "hangfire-probe-secret";
        var unreachable =
            "Host=127.0.0.1;Port=1;Database=antiphon;Username=antiphon;Password="
            + password + ";Timeout=2;Pooling=false";
        var settings = new HangfireSettings { Storage = HangfireSettings.PostgreSqlStorageName };

        var missing = Should.Throw<InvalidOperationException>(() =>
            HangfireConfiguration.PreparePostgreSqlStorage(settings, connectionString: null));
        missing.Message.ShouldContain("ConnectionStrings:DefaultConnection");

        var refused = Should.Throw<InvalidOperationException>(() =>
            HangfireConfiguration.PreparePostgreSqlStorage(settings, unreachable));
        refused.Message.ShouldContain("not reachable");
        refused.Message.ShouldNotContain("authentication");
        refused.InnerException.ShouldNotBeNull();
        refused.ToString().ShouldNotContain(password);
        await Task.CompletedTask;
    }

    [Test]
    [Timeout(20_000)]
    public async Task Unreachable_tick_retries_until_the_crash_expiry_then_stops_the_host()
    {
        const string password = "hangfire-tick-secret";
        var unreachable =
            "Host=127.0.0.1;Port=1;Database=antiphon;Username=antiphon;Password="
            + password + ";Timeout=1;Pooling=false";
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero));
        using var connection = new NpgsqlConnection(unreachable);
        using var lease = new HangfireWorkerLease(connection, unreachable);
        lease.SetClock(clock);
        lease.WithinCrashExpiryWindow().ShouldBeTrue();

        var builder = Host.CreateApplicationBuilder();
        using var host = builder.Build();
        await host.StartAsync();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
        lease.StartSupervision(lifetime, TimeSpan.FromMilliseconds(50));

        await Task.Delay(TimeSpan.FromMilliseconds(800));
        stopping.Task.IsCompleted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(HangfireWorkerLease.CrashExpirySeconds));
        lease.WithinCrashExpiryWindow().ShouldBeFalse();
        var completed = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.ShouldBe(stopping.Task);
        await host.StopAsync();
    }

    [Test]
    [Timeout(20_000)]
    public async Task PostgreSql_bootstrap_resolves_storage_and_refuses_an_unreachable_database()
    {
        const string password = "hangfire-bootstrap-secret";
        var unreachable =
            "Host=127.0.0.1;Port=1;Database=antiphon;Username=antiphon;Password="
            + password + ";Timeout=2;Pooling=false";
        var settings = new HangfireSettings
        {
            Storage = HangfireSettings.PostgreSqlStorageName,
            SchemaName = "hangfire_card1743"
        };

        var services = new ServiceCollection();
        services.AddHangfire(config => HangfireConfiguration.ConfigureStorage(config, settings, unreachable));
        await using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<JobStorage>().ShouldBeOfType<PostgreSqlStorage>();
        var options = (PostgreSqlStorageOptions)storage.GetType()
            .GetProperty("Options", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(storage)!;
        options.SchemaName.ShouldBe("hangfire_card1743");
        options.PrepareSchemaIfNecessary.ShouldBeFalse();
        options.StartupConnectionMaxRetries.ShouldBe(0);
        options.AllowDegradedModeWithoutStorage.ShouldBeFalse();

        var factory = storage.GetType()
            .GetField("_connectionFactory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(storage)
            .ShouldBeOfType<NpgsqlConnectionFactory>();
        factory.ConnectionString.Host.ShouldBe("127.0.0.1");
        factory.ConnectionString.Port.ShouldBe(1);
        factory.ConnectionString.Database.ShouldBe("antiphon");

        var refused = Should.Throw<InvalidOperationException>(() =>
            HangfireConfiguration.PreparePostgreSqlStorage(settings, unreachable));
        refused.Message.ShouldContain("not reachable");
        refused.Message.ShouldNotContain("authentication");
        refused.InnerException.ShouldNotBeNull();
        refused.ToString().ShouldNotContain(password);
        await Task.CompletedTask;
    }
}

/// <summary>
/// CARD-1743. Recurring rows survive a new storage instance, and a second worker lease is refused.
/// </summary>
[Category("Integration")]
[NotInParallel]
public sealed class HangfirePostgreSqlStorageTests
{
    private const string Schema = "hangfire_card1743";

    [Test]
    [Timeout(180_000)]
    public async Task Recurring_job_survives_a_new_storage_instance_without_duplicating()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = PostgreSqlSettings();
        var first = HangfireConfiguration.CreateStorage(settings, database.ConnectionString);
        first.GetType().Name.ShouldBe("PostgreSqlStorage");
        var manager = new RecurringJobManager(first);
        var residue = new WorktreeResidueSettings();
        HangfireConfiguration.AddOrUpdateWorktreeResidueJob(manager, residue);
        HangfireConfiguration.AddOrUpdateWorktreeResidueJob(manager, residue);

        var second = HangfireConfiguration.CreateStorage(settings, database.ConnectionString);
        using var connection = second.GetConnection();
        var jobs = connection.GetRecurringJobs();
        jobs.ShouldContain(job => job.Id == residue.RecurringJobId);
        jobs.Count(job => job.Id == residue.RecurringJobId).ShouldBe(1);
        jobs.ShouldNotContain(job => job.Id == "antiphon:zombie-census");

        await using var probe = new NpgsqlConnection(database.ConnectionString);
        await probe.OpenAsync();
        var owned = await CountTablesAsync(probe, Schema, "job");
        var publicJob = await CountTablesAsync(probe, "public", "job");
        owned.ShouldBe(1);
        publicJob.ShouldBe(0);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Disabled_census_stays_absent_on_a_new_storage_instance()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = PostgreSqlSettings();
        var first = HangfireConfiguration.CreateStorage(settings, database.ConnectionString);
        var manager = new RecurringJobManager(first);
        HangfireConfiguration.RegisterCensusJob(manager, new ZombieCensusSettings { Enabled = true });

        var restarted = HangfireConfiguration.CreateStorage(settings, database.ConnectionString);
        using (var seeing = restarted.GetConnection())
            seeing.GetRecurringJobs().ShouldContain(job => job.Id == "antiphon:zombie-census");

        HangfireConfiguration.RegisterCensusJob(
            new RecurringJobManager(restarted), new ZombieCensusSettings { Enabled = false });

        var again = HangfireConfiguration.CreateStorage(settings, database.ConnectionString);
        using var connection = again.GetConnection();
        connection.GetRecurringJobs().ShouldNotContain(job => job.Id == "antiphon:zombie-census");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Disabled_residue_stays_absent_on_a_new_storage_instance()
    {
        await AssertDurableRemovalAsync(
            "antiphon:worktree-residue",
            manager => HangfireConfiguration.RegisterWorktreeResidueJob(
                manager, new WorktreeResidueSettings { Enabled = true }),
            manager => HangfireConfiguration.RegisterWorktreeResidueJob(
                manager, new WorktreeResidueSettings { Enabled = false }));
    }

    [Test]
    [Timeout(180_000)]
    public async Task Disabled_watchdog_stays_absent_on_a_new_storage_instance()
    {
        await AssertDurableRemovalAsync(
            ExpectationWatchdogJob.RecurringJobId,
            manager => HangfireConfiguration.RegisterExpectationWatchdogJob(
                manager, new HangfireSettings(), new ExpectationWatchdogSettings { Enabled = true }),
            manager => HangfireConfiguration.RegisterExpectationWatchdogJob(
                manager, new HangfireSettings(), new ExpectationWatchdogSettings { Enabled = false }));
    }

    [Test]
    [Timeout(180_000)]
    public async Task Disabled_slot_reconcile_stays_absent_on_a_new_storage_instance()
    {
        await AssertDurableRemovalAsync(
            RunnerSlotReconcileJob.RecurringJobId,
            manager => HangfireConfiguration.RegisterRunnerSlotReconcileJob(
                manager, new PhoneHomeRunnerSettings { Enabled = true }),
            manager => HangfireConfiguration.RegisterRunnerSlotReconcileJob(
                manager, new PhoneHomeRunnerSettings { Enabled = false }));
    }

    [Test]
    [Timeout(180_000)]
    public async Task Disabled_retire_stays_absent_on_a_new_storage_instance()
    {
        await AssertDurableRemovalAsync(
            RunnerRetireJob.RecurringJobId,
            manager => HangfireConfiguration.RegisterRunnerRetireJob(
                manager, new PhoneHomeRunnerSettings { Enabled = true }),
            manager => HangfireConfiguration.RegisterRunnerRetireJob(
                manager, new PhoneHomeRunnerSettings { Enabled = false }));
    }

    private async Task AssertDurableRemovalAsync(
        string jobId, Action<IRecurringJobManager> enable, Action<IRecurringJobManager> disable)
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = PostgreSqlSettings();
        var first = HangfireConfiguration.CreateStorage(settings, database.ConnectionString);
        enable(new RecurringJobManager(first));

        var restarted = HangfireConfiguration.CreateStorage(settings, database.ConnectionString);
        using (var seeing = restarted.GetConnection())
            seeing.GetRecurringJobs().ShouldContain(job => job.Id == jobId);

        disable(new RecurringJobManager(restarted));

        var again = HangfireConfiguration.CreateStorage(settings, database.ConnectionString);
        using var connection = again.GetConnection();
        connection.GetRecurringJobs().ShouldNotContain(job => job.Id == jobId);
    }

    [Test]
    [Timeout(180_000)]
    public async Task A_second_worker_lease_is_refused_until_the_first_is_released()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var first = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        var refused = Should.Throw<InvalidOperationException>(() =>
            HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString));
        refused.Message.ShouldContain("second worker");
        refused.Message.ShouldNotContain("Password");

        first.Dispose();

        using var restarted = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        restarted.ShouldNotBeNull();
        await Task.CompletedTask;
    }

    [Test]
    [Timeout(180_000)]
    public async Task Worker_lease_stays_held_until_hosted_services_finish_stopping()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var lease = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        var heldDuringDrain = false;
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHostedService(_ => new DrainProbe(() =>
        {
            try
            {
                using var contender = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
                heldDuringDrain = false;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("second worker", StringComparison.Ordinal))
            {
                heldDuringDrain = true;
            }
        }));
        using var host = builder.Build();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        HangfireConfiguration.ReleaseWorkerLeaseWhenHostStops(lifetime, lease);
        await host.StartAsync();
        // app.Run stops by firing ApplicationStopping, then StopAsync drains hosted services
        // and only then fires ApplicationStopped.
        lifetime.StopApplication();
        await host.StopAsync();

        heldDuringDrain.ShouldBeTrue();
        using var restarted = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        restarted.ShouldNotBeNull();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Worker_lease_is_reacquired_after_its_backend_is_terminated()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var lease = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        var before = await FindWorkerLockPidsAsync(database.ConnectionString);
        before.Count.ShouldBe(1);
        await TerminateBackendAsync(database.ConnectionString, before[0]);
        await WaitUntilWorkerLockDropsAsync(database.ConnectionString, before[0]);
        (await FindWorkerLockPidsAsync(database.ConnectionString)).ShouldBeEmpty();

        lease.EnsureHeld();

        var reacquired = await FindWorkerLockPidsAsync(database.ConnectionString);
        reacquired.Count.ShouldBe(1);
        reacquired[0].ShouldNotBe(before[0]);
        var refused = Should.Throw<InvalidOperationException>(() =>
            HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString));
        refused.Message.ShouldContain("second worker");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Worker_lease_refuses_to_continue_when_another_worker_holds_the_lock()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var first = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        var pid = (await FindWorkerLockPidsAsync(database.ConnectionString)).Single();
        await TerminateBackendAsync(database.ConnectionString, pid);
        await WaitUntilWorkerLockDropsAsync(database.ConnectionString, pid);
        using var second = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);

        var lost = Should.Throw<InvalidOperationException>(first.EnsureHeld);
        lost.Message.ShouldContain("another worker");
        (await FindWorkerLockPidsAsync(database.ConnectionString)).Count.ShouldBe(1);
        second.ShouldNotBeNull();
    }

    [Test]
    [Timeout(180_000)]
    public async Task Worker_lease_stops_the_host_when_another_worker_holds_the_lock()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var first = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        var pid = (await FindWorkerLockPidsAsync(database.ConnectionString)).Single();
        await TerminateBackendAsync(database.ConnectionString, pid);
        await WaitUntilWorkerLockDropsAsync(database.ConnectionString, pid);
        using var second = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);

        var builder = Host.CreateApplicationBuilder();
        using var host = builder.Build();
        await host.StartAsync();
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
        first.StartSupervision(lifetime, TimeSpan.FromMilliseconds(200));

        var completed = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.ShouldBe(stopping.Task);
        second.ShouldNotBeNull();
        await host.StopAsync();
    }

    /// <summary>
    /// CARD-1774 D-1. <c>pg_locks</c> is cluster-wide. A second database holding
    /// (1743, 1), and a one-key lock that reuses classid/objid with objsubid 1,
    /// are not another worker. A holder in this database still stops the host.
    /// </summary>
    [Test]
    [Timeout(180_000)]
    public async Task Worker_lease_ignores_another_database_holder_and_stops_for_its_own()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var lease = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        var ownPid = (await FindWorkerLockPidsAsync(database.ConnectionString)).Single();
        var siblingName = "hf_sib_" + Guid.NewGuid().ToString("N");
        await CreateDatabaseAsync(siblingName);
        NpgsqlConnection? sibling = null;
        NpgsqlConnection? decoy = null;
        try
        {
            var siblingConnection = new NpgsqlConnectionStringBuilder(database.ConnectionString)
            {
                Database = siblingName,
                Pooling = false
            }.ConnectionString;
            sibling = new NpgsqlConnection(siblingConnection);
            await sibling.OpenAsync();
            await using (var take = new NpgsqlCommand(
                "SELECT pg_try_advisory_lock(@lockClass, @lockId)", sibling))
            {
                take.Parameters.AddWithValue("lockClass", HangfireWorkerLease.ClassId);
                take.Parameters.AddWithValue("lockId", HangfireWorkerLease.LockId);
                (await take.ExecuteScalarAsync()).ShouldBe(true);
            }

            decoy = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(database.ConnectionString)
            {
                Pooling = false
            }.ConnectionString);
            await decoy.OpenAsync();
            await using (var takeDecoy = new NpgsqlCommand(
                $"SELECT pg_try_advisory_lock(({HangfireWorkerLease.ClassId}::bigint << 32) | {HangfireWorkerLease.LockId})",
                decoy))
            {
                (await takeDecoy.ExecuteScalarAsync()).ShouldBe(true);
            }

            (await CountGrantedHoldersAsync(database.ConnectionString, siblingName)).ShouldBe(1);
            (await CountObjSubIdAsync(database.ConnectionString, objSubId: 1)).ShouldBe(1);

            await TerminateBackendAsync(database.ConnectionString, ownPid);
            await WaitUntilWorkerLockDropsAsync(database.ConnectionString, ownPid);

            var builder = Host.CreateApplicationBuilder();
            using var host = builder.Build();
            await host.StartAsync();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());

            lease.EnsureHeld();

            stopping.Task.IsCompleted.ShouldBeFalse();
            var reacquired = await FindWorkerLockPidsAsync(database.ConnectionString);
            reacquired.Count.ShouldBe(1);
            reacquired[0].ShouldNotBe(ownPid);
            var refused = Should.Throw<InvalidOperationException>(() =>
                HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString));
            refused.Message.ShouldContain("second worker");

            lease.StartSupervision(lifetime, TimeSpan.FromMilliseconds(50));
            var quiet = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            quiet.ShouldNotBe(stopping.Task);

            await TerminateBackendAsync(database.ConnectionString, reacquired[0]);
            await WaitUntilWorkerLockDropsAsync(database.ConnectionString, reacquired[0]);
            using var second = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
            var stopped = await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            stopped.ShouldBe(stopping.Task);
            (await FindWorkerLockPidsAsync(database.ConnectionString)).Count.ShouldBe(1);
            second.ShouldNotBeNull();
            await host.StopAsync();
        }
        finally
        {
            if (sibling is not null)
                await sibling.DisposeAsync();
            if (decoy is not null)
                await decoy.DisposeAsync();
            await DropDatabaseAsync(siblingName);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Worker_lease_sets_a_sixty_second_crash_expiry()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var lease = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        HangfireWorkerLease.ClassId.ShouldBe(1743);
        HangfireWorkerLease.LockId.ShouldBe(1);
        HangfireWorkerLease.KeepaliveIdleSeconds.ShouldBe(30);
        HangfireWorkerLease.KeepaliveIntervalSeconds.ShouldBe(10);
        HangfireWorkerLease.KeepaliveCount.ShouldBe(3);
        HangfireWorkerLease.CrashExpirySeconds.ShouldBe(60);
        HangfireWorkerLease.ReacquireInterval.ShouldBe(TimeSpan.FromSeconds(15));

        var expiry = lease.ReadCrashExpiry();
        expiry.IdleSeconds.ShouldBe(HangfireWorkerLease.KeepaliveIdleSeconds);
        expiry.IntervalSeconds.ShouldBe(HangfireWorkerLease.KeepaliveIntervalSeconds);
        expiry.Count.ShouldBe(HangfireWorkerLease.KeepaliveCount);

        var pids = await FindWorkerLockPidsAsync(database.ConnectionString);
        pids.Count.ShouldBe(1);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Worker_lease_keeps_its_own_backend_when_the_check_query_fails()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var lease = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        var before = (await FindWorkerLockPidsAsync(database.ConnectionString)).Single();
        lease.AbandonOpenTransaction();

        lease.EnsureHeld();

        var after = await FindWorkerLockPidsAsync(database.ConnectionString);
        after.Count.ShouldBe(1);
        after[0].ShouldBe(before);
        var refused = Should.Throw<InvalidOperationException>(() =>
            HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString));
        refused.Message.ShouldContain("second worker");
    }

    [Test]
    [Timeout(180_000)]
    public async Task Worker_lease_bounds_the_unlock_command_timeout()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        using var lease = HangfireConfiguration.AcquireSingleWorkerLease(database.ConnectionString);
        var timeout = lease.ReadUnlockCommandTimeoutSeconds();
        timeout.ShouldBeGreaterThan(0);
        timeout.ShouldBeLessThanOrEqualTo(5);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Authentication_failure_names_authentication_and_keeps_the_inner_exception()
    {
        const string password = "hangfire-auth-secret";
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var builder = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Password = password };
        var settings = PostgreSqlSettings();

        var refused = Should.Throw<InvalidOperationException>(() =>
            HangfireConfiguration.PreparePostgreSqlStorage(settings, builder.ConnectionString));
        refused.Message.ShouldContain("authentication");
        refused.Message.ShouldNotContain("not reachable");
        PostgresFailure(refused).SqlState.ShouldBe(PostgresErrorCodes.InvalidPassword);
        refused.ToString().ShouldNotContain(password);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Privilege_failure_names_privilege_and_keeps_the_inner_exception()
    {
        const string password = "hf-priv-secret";
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var adminBuilder = new NpgsqlConnectionStringBuilder(database.ConnectionString);
        var role = "hf_nopriv_" + Guid.NewGuid().ToString("N")[..16];
        RequireSqlName(role);
        RequireSqlName(adminBuilder.Database!);
        await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
        await admin.OpenAsync();
        try
        {
            await using (var create = new NpgsqlCommand(
                $"CREATE ROLE {role} LOGIN PASSWORD '{password}' NOSUPERUSER NOCREATEDB NOCREATEROLE", admin))
                await create.ExecuteNonQueryAsync();
            await using (var grant = new NpgsqlCommand(
                $"GRANT CONNECT ON DATABASE {adminBuilder.Database} TO {role}", admin))
                await grant.ExecuteNonQueryAsync();

            var limited = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
            {
                Username = role,
                Password = password
            }.ConnectionString;
            var refused = Should.Throw<InvalidOperationException>(() =>
                HangfireConfiguration.PreparePostgreSqlStorage(PostgreSqlSettings(), limited));
            refused.Message.ShouldContain("privilege");
            refused.Message.ShouldNotContain("not reachable");
            PostgresFailure(refused).SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
            refused.ToString().ShouldNotContain(password);
        }
        finally
        {
            await using (var revoke = new NpgsqlCommand(
                $"REVOKE ALL PRIVILEGES ON DATABASE {adminBuilder.Database} FROM {role}", admin))
                await revoke.ExecuteNonQueryAsync();
            await using var drop = new NpgsqlCommand($"DROP ROLE IF EXISTS {role}", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Missing_database_names_the_missing_database_and_keeps_the_inner_exception()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var builder = new NpgsqlConnectionStringBuilder(database.ConnectionString)
        {
            Database = "hangfire_missing_c1768"
        };
        var refused = Should.Throw<InvalidOperationException>(() =>
            HangfireConfiguration.PreparePostgreSqlStorage(PostgreSqlSettings(), builder.ConnectionString));
        refused.Message.ShouldContain("does not exist");
        refused.Message.ShouldNotContain("not reachable");
        PostgresFailure(refused).SqlState.ShouldBe(PostgresErrorCodes.InvalidCatalogName);
    }

    [Test]
    [Timeout(180_000)]
    public async Task Recurring_job_type_is_rewritten_and_an_enqueued_row_is_left_alone()
    {
        await using var database = await TestDbFixture.CreateIsolatedSchemaAsync();
        var settings = PostgreSqlSettings();
        var storage = HangfireConfiguration.CreateStorage(settings, database.ConnectionString);
        var residue = new WorktreeResidueSettings();
        HangfireConfiguration.AddOrUpdateWorktreeResidueJob(new RecurringJobManager(storage), residue);

        await using var db = new NpgsqlConnection(database.ConnectionString);
        await db.OpenAsync();
        var stored = await ReadRecurringTypeAsync(db);
        var current = typeof(WorktreeResidueJob).FullName!;
        stored.ShouldContain(current);
        stored.ShouldContain("Antiphon.Server");
        var retired = stored.Replace(current, current + "Retired", StringComparison.Ordinal);
        retired.ShouldContain("WorktreeResidueJobRetired");
        (await RewriteRecurringTypeAsync(db, retired)).ShouldBe(1);
        await InsertEnqueuedJobAsync(db, retired);

        HangfireConfiguration.AddOrUpdateWorktreeResidueJob(new RecurringJobManager(storage), residue);

        var rewritten = await ReadRecurringTypeAsync(db);
        rewritten.ShouldContain(current);
        rewritten.ShouldNotContain("WorktreeResidueJobRetired");
        (await ReadEnqueuedInvocationAsync(db)).ShouldContain("WorktreeResidueJobRetired");

        var again = HangfireConfiguration.CreateStorage(settings, database.ConnectionString);
        using var connection = again.GetConnection();
        var job = connection.GetRecurringJobs().Single(item => item.Id == residue.RecurringJobId);
        job.Job.ShouldNotBeNull();
        job.Job!.Type.ShouldBe(typeof(WorktreeResidueJob));
    }

    private static HangfireSettings PostgreSqlSettings() => new()
    {
        Storage = HangfireSettings.PostgreSqlStorageName,
        SchemaName = Schema
    };

    private static async Task<long> CountTablesAsync(NpgsqlConnection connection, string schema, string table)
    {
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table",
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static PostgresException PostgresFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
                return postgres;
        }

        throw new ShouldAssertException("Expected a PostgresException on the startup failure.");
    }

    private static void RequireSqlName(string name)
    {
        foreach (var character in name)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
                throw new InvalidOperationException("SQL name has a character this test will not interpolate.");
        }
    }

    private static async Task<List<int>> FindWorkerLockPidsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT pid
            FROM pg_locks
            WHERE locktype = 'advisory'
              AND classid = @lockClass
              AND objid = @lockId
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
              AND objsubid = 2
              AND granted
            """, connection);
        command.Parameters.AddWithValue("lockClass", HangfireWorkerLease.ClassId);
        command.Parameters.AddWithValue("lockId", HangfireWorkerLease.LockId);
        var pids = new List<int>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pids.Add(Convert.ToInt32(reader.GetValue(0)));
        return pids;
    }

    private static async Task WaitUntilWorkerLockDropsAsync(string connectionString, int pid)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            var holders = await FindWorkerLockPidsAsync(connectionString);
            if (!holders.Contains(pid))
                return;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    "Backend " + pid + " still holds the Hangfire worker lock after pg_terminate_backend.");
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private static async Task<int> CountGrantedHoldersAsync(string connectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM pg_locks l
            JOIN pg_database d ON d.oid = l.database
            WHERE l.locktype = 'advisory'
              AND l.classid = @lockClass
              AND l.objid = @lockId
              AND l.objsubid = 2
              AND l.granted
              AND d.datname = @databaseName
            """, connection);
        command.Parameters.AddWithValue("lockClass", HangfireWorkerLease.ClassId);
        command.Parameters.AddWithValue("lockId", HangfireWorkerLease.LockId);
        command.Parameters.AddWithValue("databaseName", databaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountObjSubIdAsync(string connectionString, int objSubId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM pg_locks
            WHERE locktype = 'advisory'
              AND classid = @lockClass
              AND objid = @lockId
              AND objsubid = @objSubId
              AND granted
              AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
            """, connection);
        command.Parameters.AddWithValue("lockClass", HangfireWorkerLease.ClassId);
        command.Parameters.AddWithValue("lockId", HangfireWorkerLease.LockId);
        command.Parameters.AddWithValue("objSubId", objSubId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task CreateDatabaseAsync(string name)
    {
        RequireSqlName(name);
        await using var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString);
        await maintenance.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", maintenance);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string name)
    {
        RequireSqlName(name);
        await using var maintenance = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString);
        await maintenance.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", maintenance);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task TerminateBackendAsync(string connectionString, int pid)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", connection);
        command.Parameters.AddWithValue("pid", pid);
        var ended = await command.ExecuteScalarAsync();
        ended.ShouldBe(true);
    }

    private static async Task<string> ReadRecurringTypeAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            $"""SELECT value FROM "{Schema}".hash WHERE field = 'Job' AND value LIKE '%WorktreeResidueJob%'""",
            connection);
        var value = await command.ExecuteScalarAsync();
        return value.ShouldBeOfType<string>();
    }

    private static async Task<int> RewriteRecurringTypeAsync(NpgsqlConnection connection, string value)
    {
        await using var command = new NpgsqlCommand(
            $"""UPDATE "{Schema}".hash SET value = @value WHERE field = 'Job' AND value LIKE '%WorktreeResidueJob%'""",
            connection);
        command.Parameters.AddWithValue("value", value);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertEnqueuedJobAsync(NpgsqlConnection connection, string invocation)
    {
        await using var command = new NpgsqlCommand($"""
            INSERT INTO "{Schema}".job (invocationdata, arguments, createdat, statename)
            VALUES (CAST(@invocation AS jsonb), CAST('[]' AS jsonb), NOW() AT TIME ZONE 'UTC', 'Scheduled')
            """, connection);
        command.Parameters.AddWithValue("invocation", invocation);
        (await command.ExecuteNonQueryAsync()).ShouldBe(1);
    }

    private static async Task<string> ReadEnqueuedInvocationAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            $"""SELECT invocationdata::text FROM "{Schema}".job""",
            connection);
        var value = await command.ExecuteScalarAsync();
        return value.ShouldBeOfType<string>();
    }

    private sealed class DrainProbe : IHostedService
    {
        private readonly Action _onStop;

        public DrainProbe(Action onStop) => _onStop = onStop;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _onStop();
            return Task.CompletedTask;
        }
    }
}

/// <summary>CARD-1774. Prose and Program wiring pins stay out of <see cref="HangfireStorageSelectionTests"/>.</summary>
[Category("Unit")]
public sealed class HangfireLeaseDocumentationTests
{
    [Test]
    public async Task Program_releases_the_worker_lease_when_the_host_stops()
    {
        var program = DockerStackDocuments.Read("server/Program.cs");
        program.ShouldContain("HangfireConfiguration.ReleaseWorkerLeaseWhenHostStops(");
        program.ShouldContain("heldLease.StartSupervision(");
        program.ShouldNotContain("ApplicationStopping.Register(heldLease");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Durable_hangfire_documents_the_lease_the_package_and_renames()
    {
        new BackgroundJobServerOptions().ShutdownTimeout.ShouldBe(TimeSpan.FromSeconds(15));
        new HostOptions().ShutdownTimeout.ShouldBe(TimeSpan.FromSeconds(30));

        var contract = DockerStackDocuments.Read("docs/docker-stack.md");
        contract.ShouldContain("That 15 seconds is the detection bound");
        contract.ShouldContain(
            "plus the Hangfire drain (`BackgroundJobServerOptions.ShutdownTimeout`, 15 seconds by default) plus host shutdown (`HostOptions.ShutdownTimeout`, 30 seconds by default)");
        contract.ShouldContain("A holder that is this process's own previous backend is not another worker.");
        contract.ShouldContain("When another worker holds the lock, the host stops on that tick.");
        contract.ShouldContain(
            "A tick that cannot reach Postgres retries until 60 seconds after the last confirmed hold, then the host stops.");
        contract.ShouldContain("The host does not stop on the first unreachable tick.");
        contract.ShouldContain("Dispose bounds the unlock command timeout at 5 seconds.");
        contract.ShouldContain("CommandTimeout=5 applies to every lease command.");
        contract.ShouldContain(
            "SELECT pg_terminate_backend(pid) FROM pg_locks WHERE locktype = 'advisory' AND classid = 1743 AND objid = 1 AND granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database()) AND objsubid = 2");
        contract.ShouldContain("A paused process still answers TCP keepalives");
        contract.ShouldContain("Windows-native Postgres reports `tcp_keepalives_count` as 0.");
        contract.ShouldContain("pg_try_advisory_lock(1743, 1)");
        contract.ShouldContain("released on `ApplicationStopped`");
        contract.ShouldContain("Every 15 seconds");
        contract.ShouldContain("The check stores nothing.");
        contract.ShouldContain("within 60 seconds of silence");
        contract.ShouldContain("`tcp_keepalives_idle` to 30");
        contract.ShouldContain("`tcp_keepalives_interval` to 10");
        contract.ShouldContain("`tcp_keepalives_count` to 3");
        contract.ShouldContain("pg_terminate_backend(pid)");
        contract.ShouldContain("`Hangfire.PostgreSql` 1.21.1 is LGPLv3");
        contract.ShouldContain("Dapper.AOT.Analyzers.dll");
        contract.ShouldContain("Microsoft.SqlServer.TransactSql.ScriptDom.dll");
        contract.ShouldContain("resolves Npgsql 9.0.3");
        contract.ShouldContain("database CREATE privilege");
        contract.ShouldContain("Enqueued or scheduled instances");
        await Task.CompletedTask;
    }
}
