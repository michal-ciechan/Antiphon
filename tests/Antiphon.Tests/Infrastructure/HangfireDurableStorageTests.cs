using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-1743. Desktop storage stays in-memory unless Hangfire:Storage is PostgreSql.</summary>
[Category("Unit")]
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
}
