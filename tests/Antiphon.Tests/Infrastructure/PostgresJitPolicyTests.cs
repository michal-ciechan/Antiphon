using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class PostgresJitPolicyTestsSetup
{
    [Test]
    public void suppress_appends_a_final_jit_off_and_is_idempotent()
    {
        var input = Connection(options: "-c statement_timeout=5000 -c jit=on");
        var once = ApplicationPostgresConnection.SuppressJit(input.ConnectionString);
        var twice = ApplicationPostgresConnection.SuppressJit(once);
        string.Equals(once, twice, StringComparison.Ordinal).ShouldBeTrue("repeated application was not idempotent");

        var options = new NpgsqlConnectionStringBuilder(twice).Options ?? "";
        options.EndsWith(ApplicationPostgresConnection.JitOffOption, StringComparison.Ordinal)
            .ShouldBeTrue("final jit option missing");
        Regex.Matches(options, Regex.Escape(ApplicationPostgresConnection.JitOffOption)).Count
            .ShouldBe(1);
        options.Contains("-c statement_timeout=5000", StringComparison.Ordinal)
            .ShouldBeTrue("unrelated startup option missing");
        options.Contains("-c jit=on", StringComparison.Ordinal)
            .ShouldBeTrue("earlier jit=on was removed instead of overridden");

        var upper = Connection(options: "-c JIT=ON");
        var upperOptions = new NpgsqlConnectionStringBuilder(
            ApplicationPostgresConnection.SuppressJit(upper.ConnectionString)).Options ?? "";
        upperOptions.EndsWith(ApplicationPostgresConnection.JitOffOption, StringComparison.Ordinal)
            .ShouldBeTrue("uppercase jit=on still won");

        var already = Connection(options: ApplicationPostgresConnection.JitOffOption);
        var kept = new NpgsqlConnectionStringBuilder(
            ApplicationPostgresConnection.SuppressJit(already.ConnectionString)).Options ?? "";
        Regex.Matches(kept, Regex.Escape(ApplicationPostgresConnection.JitOffOption)).Count
            .ShouldBe(1);
    }

    [Test]
    public void suppress_preserves_unrelated_fields()
    {
        var input = Connection(options: "-c jit=on");
        var applied = new NpgsqlConnectionStringBuilder(ApplicationPostgresConnection.SuppressJit(input.ConnectionString));
        Same("host", applied.Host, input.Host);
        Same("port", applied.Port.ToString(), input.Port.ToString());
        Same("database", applied.Database, input.Database);
        Same("username", applied.Username, input.Username);
        Same("password", applied.Password, input.Password);
        applied.NoResetOnClose.ShouldBeTrue();
        applied.Pooling.ShouldBeTrue();
        applied.Timeout.ShouldBe(15);
        Same("application-name", applied.ApplicationName, input.ApplicationName);
        applied.IncludeErrorDetail.ShouldBeFalse();
    }

    [Test]
    public void disable_false_leaves_the_string_unchanged()
    {
        var input = Connection(options: "-c jit=on").ConnectionString;
        var applied = ApplicationPostgresConnection.ApplyPolicy(input, disableJit: false);
        string.Equals(applied, input, StringComparison.Ordinal).ShouldBeTrue("disableJit false changed the string");
        ApplicationPostgresConnection.ApplyPolicy(null, disableJit: true).ShouldBeNull();
    }

    [Test]
    public void missing_configuration_keeps_the_safe_default_and_false_reverts()
    {
        new PostgresSettings().DisableJit.ShouldBeTrue();
        var missing = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var bound = missing.GetSection(PostgresSettings.SectionName).Get<PostgresSettings>() ?? new PostgresSettings();
        bound.DisableJit.ShouldBeTrue();

        var reverted = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [PostgresSettings.SectionName + ":DisableJit"] = "false"
            })
            .Build()
            .GetSection(PostgresSettings.SectionName)
            .Get<PostgresSettings>();
        reverted.ShouldNotBeNull();
        reverted.DisableJit.ShouldBeFalse();
    }

    [Test]
    public void shipped_appsettings_disables_jit()
    {
        using var doc = JsonDocument.Parse(DockerStackDocuments.Read("server/appsettings.json"));
        doc.RootElement.TryGetProperty(PostgresSettings.SectionName, out var section)
            .ShouldBeTrue("Postgres section missing");
        section.GetProperty(nameof(PostgresSettings.DisableJit)).GetBoolean().ShouldBeTrue();
    }

    [Test]
    public void server_calls_the_helper_before_use_npgsql_and_hangfire_does_not()
    {
        var program = DockerStackDocuments.Read("server/Program.cs");
        var call = program.IndexOf("ApplicationPostgresConnection.ApplyPolicy", StringComparison.Ordinal);
        var use = program.IndexOf(".UseNpgsql(", StringComparison.Ordinal);
        (call >= 0 && use > call).ShouldBeTrue("helper must run before UseNpgsql");

        var life = DockerStackDocuments.Read("tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs");
        var raw = life.IndexOf("GetConnectionString(container)", StringComparison.Ordinal);
        var derive = life.IndexOf("DeriveMaintenance(", StringComparison.Ordinal);
        var suppress = life.IndexOf("ApplicationPostgresConnection.SuppressJit(", StringComparison.Ordinal);
        (raw >= 0 && derive > raw && suppress > derive).ShouldBeTrue(
            "maintenance must be derived from the raw string before the shared string is suppressed");

        DockerStackDocuments.Read("server/Infrastructure/Agents/HangfireConfiguration.cs")
            .Contains("ApplicationPostgresConnection", StringComparison.Ordinal)
            .ShouldBeFalse();
    }

    [Test]
    public void measurement_script_records_statement_time_and_never_resets()
    {
        var text = DockerStackDocuments.Read("scripts/postgres-jit-window.ps1");
        text.Contains("total_exec_time", StringComparison.Ordinal).ShouldBeTrue("statement time column missing");
        text.Contains("jit_", StringComparison.Ordinal).ShouldBeTrue("jit columns missing");
        text.Contains("pg_stat_statements_reset", StringComparison.Ordinal).ShouldBeFalse();
        text.Contains("docker stats", StringComparison.Ordinal).ShouldBeTrue("container sample missing");
    }

    private static NpgsqlConnectionStringBuilder Connection(string options) => new()
    {
        Host = "127.0.0.1",
        Port = 5432,
        Database = "antiphon",
        Username = "antiphon",
        Password = "not-a-secret",
        NoResetOnClose = true,
        Pooling = true,
        Timeout = 15,
        ApplicationName = "antiphon-jit",
        IncludeErrorDetail = false,
        Options = options
    };

    private static void Same(string field, string? actual, string? expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"field {field} was not preserved");
    }
}

[Category("Integration")]
public sealed class PostgresJitPolicyTests
{
    [Test]
    public async Task shared_application_connection_reports_jit_off()
    {
        await using var connection = new NpgsqlConnection(TestDbFixture.ConnectionString);
        await connection.OpenAsync();
        (await JitAsync(connection)).ShouldBe("off");
    }

    [Test]
    public async Task pooled_reopen_and_unpooled_open_report_jit_off()
    {
        var pooled = new NpgsqlConnectionStringBuilder(TestDbFixture.ConnectionString)
        {
            ApplicationName = "jit-policy-" + Guid.NewGuid().ToString("N")
        }.ConnectionString;

        int firstPid;
        await using (var first = new NpgsqlConnection(pooled))
        {
            await first.OpenAsync();
            (await JitAsync(first)).ShouldBe("off");
            firstPid = await BackendPidAsync(first);
        }

        int secondPid;
        await using (var second = new NpgsqlConnection(pooled))
        {
            await second.OpenAsync();
            (await JitAsync(second)).ShouldBe("off");
            secondPid = await BackendPidAsync(second);
        }
        secondPid.ShouldBe(firstPid);

        var physical = new NpgsqlConnectionStringBuilder(pooled) { Pooling = false }.ConnectionString;
        await using var third = new NpgsqlConnection(physical);
        await third.OpenAsync();
        (await JitAsync(third)).ShouldBe("off");
        (await BackendPidAsync(third)).ShouldNotBe(firstPid);
    }

    [Test]
    public async Task cloned_database_reports_jit_off()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var connection = new NpgsqlConnection(isolated.ConnectionString);
        await connection.OpenAsync();
        (await JitAsync(connection)).ShouldBe("off");
    }

    [Test]
    public async Task build_options_and_factory_shape_report_jit_off()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var throughBuildOptions = new AppDbContext(
            TestDbFixtureLifecycle.BuildOptions(isolated.ConnectionString));
        await throughBuildOptions.Database.OpenConnectionAsync();
        (await JitAsync(NpgsqlOf(throughBuildOptions))).ShouldBe("off");

        var factoryShape = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(isolated.ConnectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            })
            .Options;
        await using var throughFactory = new AppDbContext(factoryShape);
        await throughFactory.Database.OpenConnectionAsync();
        (await JitAsync(NpgsqlOf(throughFactory))).ShouldBe("off");
    }

    [Test]
    public async Task session_and_hot_path_fixtures_report_jit_off()
    {
        await using var session = await SessionStateTestFixture.CreateAsync();
        await using var sessionDb = session.Db();
        await sessionDb.Database.OpenConnectionAsync();
        (await JitAsync(NpgsqlOf(sessionDb))).ShouldBe("off");

        await using var hot = await TranscriptHotPathFixture.CreateAsync();
        await using var hotConnection = new NpgsqlConnection(hot.ConnectionString);
        await hotConnection.OpenAsync();
        (await JitAsync(hotConnection)).ShouldBe("off");
    }

    [Test]
    public async Task startup_options_keep_timezone_and_final_jit_off_wins()
    {
        var raw = new NpgsqlConnectionStringBuilder(TestDbFixture.MaintenanceConnectionString)
        {
            Database = TestDbFixture.SharedDatabaseName,
            Pooling = false,
            Options = "-c timezone=UTC -c jit=on"
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(ApplicationPostgresConnection.SuppressJit(raw));
        await connection.OpenAsync();
        var jit = await JitAsync(connection);
        var zone = await SettingAsync(connection, "TimeZone");
        (string.Equals(jit, "off", StringComparison.OrdinalIgnoreCase)
            && string.Equals(zone, "UTC", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue($"startup settings jit={jit} TimeZone={zone}");
    }

    [Test]
    public async Task high_estimate_plan_has_zero_jit_functions()
    {
        await using var connection = new NpgsqlConnection(TestDbFixture.ConnectionString);
        await connection.OpenAsync();
        var (functions, rootCost) = await ExplainAsync(connection, """
            SELECT count(*)::bigint FROM generate_series(1, 10000000) AS g(i)
            """);
        if (rootCost <= 100_000 || functions != 0)
            throw new InvalidOperationException($"high-estimate plan rootCost={rootCost} jitFunctions={functions}");
    }

    [Test]
    public async Task zero_threshold_plan_has_zero_jit_functions()
    {
        await using var connection = new NpgsqlConnection(TestDbFixture.ConnectionString);
        await connection.OpenAsync();
        await using (var set = new NpgsqlCommand("""
            SELECT set_config('jit_above_cost', '0', false),
                   set_config('jit_inline_above_cost', '0', false),
                   set_config('jit_optimize_above_cost', '0', false)
            """, connection))
        await using (var _ = await set.ExecuteReaderAsync())
        {
        }

        var (functions, _) = await ExplainAsync(connection, "SELECT 1");
        functions.ShouldBe(0);
    }

    [Test]
    public async Task maintenance_connection_keeps_the_server_default()
    {
        var options = new NpgsqlConnectionStringBuilder(TestDbFixture.MaintenanceConnectionString).Options ?? "";
        options.Contains("jit=off", StringComparison.OrdinalIgnoreCase)
            .ShouldBeFalse("maintenance connection carries the application JIT policy");
        await using var connection = new NpgsqlConnection(TestDbFixture.MaintenanceConnectionString);
        await connection.OpenAsync();
        (await JitAsync(connection)).ShouldBe("on");
    }

    private static NpgsqlConnection NpgsqlOf(AppDbContext db)
    {
        if (db.Database.GetDbConnection() is not NpgsqlConnection connection)
            throw new InvalidOperationException("application context did not open an Npgsql connection");
        return connection;
    }

    private static async Task<string> JitAsync(NpgsqlConnection connection)
    {
        await using var show = new NpgsqlCommand("SHOW jit", connection);
        var shown = await show.ExecuteScalarAsync() as string ?? "";
        var setting = await SettingAsync(connection, "jit");
        if (!string.Equals(shown, setting, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SHOW jit and current_setting('jit') disagreed");
        return shown;
    }

    private static async Task<string> SettingAsync(NpgsqlConnection connection, string name)
    {
        if (name is not ("jit" or "TimeZone"))
            throw new ArgumentOutOfRangeException(nameof(name));
        await using var command = new NpgsqlCommand("SELECT current_setting(@name)", connection);
        command.Parameters.AddWithValue("name", name);
        return await command.ExecuteScalarAsync() as string ?? "";
    }

    private static async Task<int> BackendPidAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<(int Functions, double RootCost)> ExplainAsync(NpgsqlConnection connection, string query)
    {
        await using var command = new NpgsqlCommand(
            "EXPLAIN (ANALYZE, FORMAT JSON, SETTINGS, TIMING OFF) " + query, connection);
        var json = await command.ExecuteScalarAsync() as string
            ?? throw new InvalidOperationException("explain did not return text");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement[0];
        var plan = root.GetProperty("Plan");
        var rootCost = plan.GetProperty("Total Cost").GetDouble();
        var functions = 0;
        if (root.TryGetProperty("JIT", out var jit) && jit.TryGetProperty("Functions", out var count))
            functions = count.GetInt32();
        return (functions, rootCost);
    }
}
