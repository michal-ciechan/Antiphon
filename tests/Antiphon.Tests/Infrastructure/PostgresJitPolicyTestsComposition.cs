extern alias server;

using ServerProgram = server::Program;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Security;
using Antiphon.Tests.TestHelpers;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-1886. Boots Program's own EF, health-check, and Hangfire registration.
// This host must not clear health checks or replace DbContext options: those
// replacements are what hid the wiring from the earlier factory-shaped test.
[Category("Integration")]
[Category("Slow")]
[NotInParallel("PostgresJitComposition")]
public sealed class PostgresJitPolicyTestsComposition
{
    [Test]
    [Timeout(300_000)]
    public async Task program_composition_suppresses_application_connections_and_leaves_administration_raw(
        CancellationToken cancellationToken)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var database = new NpgsqlConnectionStringBuilder(schema.ConnectionString).Database
            ?? throw new InvalidOperationException("clone database missing");
        var raw = new NpgsqlConnectionStringBuilder(TestDbFixture.MaintenanceConnectionString)
        {
            Database = database,
            Pooling = false,
            Options = null
        }.ConnectionString;

        await AssertModeAsync(raw, "absent", expectSuppressed: true, cancellationToken);
        await AssertModeAsync(raw, "enabled", expectSuppressed: true, cancellationToken);
        await AssertModeAsync(raw, "disabled", expectSuppressed: false, cancellationToken);
    }

    private static async Task AssertModeAsync(
        string raw, string mode, bool expectSuppressed, CancellationToken cancellationToken)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "antiphon-jit-composition", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        await using var factory = new JitCompositionFactory(raw, workspace, mode);
        using var client = factory.CreateClient();

        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var configured = configuration.GetConnectionString("DefaultConnection");
        if (!string.Equals(configured, raw, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "mode " + mode + " configuration length=" + (configured?.Length ?? -1).ToString(CultureInfo.InvariantCulture));
        }

        var bound = configuration["Postgres:DisableJit"];
        if (mode == "absent")
        {
            if (bound is not null)
                throw new InvalidOperationException("mode absent still published Postgres:DisableJit");
        }
        else if (!string.Equals(bound, expectSuppressed ? "true" : "false", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("mode " + mode + " published DisableJit length=" + (bound?.Length ?? -1).ToString(CultureInfo.InvariantCulture));
        }

        var settings = factory.Services.GetRequiredService<IOptions<PostgresSettings>>().Value;
        if (settings.DisableJit != expectSuppressed)
            throw new InvalidOperationException("mode " + mode + " DisableJit=" + settings.DisableJit);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await AssertApplicationAsync(mode, "ef", db.Database.GetConnectionString(), expectSuppressed, cancellationToken);

        var options = db.GetService<IDbContextOptions>() as DbContextOptions<AppDbContext>
            ?? throw new InvalidOperationException("mode " + mode + " context options were not AppDbContext options");
        await using var clone = new AppDbContext(options);
        await AssertApplicationAsync(mode, "clone", clone.Database.GetConnectionString(), expectSuppressed, cancellationToken);

        await AssertApplicationAsync(mode, "health", HealthConnection(factory.Services), expectSuppressed, cancellationToken);
        await AssertAdministrationAsync(mode, "hangfire", HangfireConnection(factory.Services), raw, databaseIsPostgres: false, cancellationToken);

        var administrator = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" }.ConnectionString;
        await AssertAdministrationAsync(mode, "create-database", administrator, raw, databaseIsPostgres: true, cancellationToken);
    }

    private static async Task AssertApplicationAsync(
        string mode, string label, string? connectionString, bool expectSuppressed, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("mode " + mode + " " + label + " connection missing");

        var options = new NpgsqlConnectionStringBuilder(connectionString).Options ?? "";
        var suppressed = options.EndsWith(ApplicationPostgresConnection.JitOffOption, StringComparison.Ordinal);
        if (suppressed != expectSuppressed)
        {
            throw new InvalidOperationException(
                "mode " + mode + " " + label + " suppressed=" + suppressed
                + " optionsLength=" + options.Length.ToString(CultureInfo.InvariantCulture));
        }

        await AssertJitAsync(mode, label, connectionString, expectSuppressed ? "off" : "on", cancellationToken);
    }

    private static async Task AssertAdministrationAsync(
        string mode,
        string label,
        string connectionString,
        string raw,
        bool databaseIsPostgres,
        CancellationToken cancellationToken)
    {
        var actual = new NpgsqlConnectionStringBuilder(connectionString);
        var expected = new NpgsqlConnectionStringBuilder(raw);
        if (!string.Equals(actual.Host, expected.Host, StringComparison.Ordinal))
            throw new InvalidOperationException("mode " + mode + " " + label + " host diverged");
        if (actual.Port != expected.Port)
            throw new InvalidOperationException("mode " + mode + " " + label + " port diverged");
        var expectedDatabase = databaseIsPostgres ? "postgres" : expected.Database;
        if (!string.Equals(actual.Database, expectedDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException("mode " + mode + " " + label + " database diverged");
        if (!string.Equals(actual.Username, expected.Username, StringComparison.Ordinal))
            throw new InvalidOperationException("mode " + mode + " " + label + " username diverged");
        if (!string.Equals(actual.Password, expected.Password, StringComparison.Ordinal))
            throw new InvalidOperationException("mode " + mode + " " + label + " password diverged");

        var options = actual.Options ?? "";
        if (options.Length != 0)
        {
            throw new InvalidOperationException(
                "mode " + mode + " " + label + " optionsLength=" + options.Length.ToString(CultureInfo.InvariantCulture));
        }

        await AssertJitAsync(mode, label, connectionString, "on", cancellationToken);
    }

    private static async Task AssertJitAsync(
        string mode, string label, string connectionString, string expected, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SHOW jit", connection);
        var shown = await command.ExecuteScalarAsync(cancellationToken) as string ?? "";
        if (!string.Equals(shown, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("mode " + mode + " " + label + " jit=" + shown);
    }

    private static string HealthConnection(IServiceProvider services)
    {
        var registration = services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .SingleOrDefault(candidate => string.Equals(candidate.Name, "postgresql", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("postgresql health check was not registered");
        var check = registration.Factory(services);
        var field = check.GetType().GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("health check options field missing");
        var options = field.GetValue(check)
            ?? throw new InvalidOperationException("health check options missing");
        var connection = options.GetType().GetProperty("ConnectionString")?.GetValue(options) as string;
        if (string.IsNullOrEmpty(connection))
            throw new InvalidOperationException("health check connection missing");
        return connection;
    }

    private static string HangfireConnection(IServiceProvider services)
    {
        var storage = services.GetRequiredService<JobStorage>();
        if (storage is not PostgreSqlStorage)
            throw new InvalidOperationException("hangfire storage was " + storage.GetType().Name);

        var factory = storage.GetType()
            .GetField("_connectionFactory", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(storage);
        if (factory is not NpgsqlConnectionFactory npgsql)
            throw new InvalidOperationException("hangfire factory was " + (factory?.GetType().Name ?? "null"));

        return npgsql.ConnectionString.ConnectionString;
    }

    private sealed class JitCompositionFactory : WebApplicationFactory<ServerProgram>
    {
        private readonly string _raw;
        private readonly string _workspace;
        private readonly string _mode;

        public JitCompositionFactory(string raw, string workspace, string mode)
        {
            _raw = raw;
            _workspace = workspace;
            _mode = mode;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Program reads the connection string, Postgres:DisableJit, and Hangfire
            // storage before Build. ConfigureAppConfiguration is applied during Build,
            // so those reads never see it. Host settings become the command-line args
            // CreateBuilder loads. A content root whose appsettings omits DisableJit
            // is what makes the absent mode actually absent: command line cannot delete a key.
            var contentRoot = WriteContentRoot(_raw, _mode);
            builder.UseContentRoot(contentRoot);

            var values = EarlySettings();
            foreach (var pair in values)
            {
                if (pair.Value is not null)
                    builder.UseSetting(pair.Key, pair.Value);
            }

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(values);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISessionRunnerClient>();
                services.AddSingleton<ISessionRunnerClient>(new RefusingSessionRunnerClient());
                services.RemoveAll<IZombieProcessCensus>();
                services.AddSingleton<IZombieProcessCensus>(new RefusingZombieProcessCensus());
            });
        }

        private Dictionary<string, string?> EarlySettings()
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _raw,
                ["Git:WorkspacePath"] = _workspace,
                ["Git:WorktreeBasePath"] = Path.Combine(_workspace, "worktrees"),
                ["AgentTui:KeyRingPath"] = Path.Combine(_workspace, "data-protection-keys"),
                ["AgentTui:ImportProfilesOnStartup"] = "false",
                ["GitHub:Enabled"] = "false",
                ["Agents:DefaultDefinition"] = "test-raw",
                ["Agents:Definitions:test-raw:Kind"] = "Raw",
                ["Agents:Definitions:test-raw:Exe"] = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["SessionRunner:BaseUrl"] = ProductionRunnerGuard.DeadRunnerBaseUrl,
                ["SessionRunner:Enabled"] = "false",
                ["Delegation:CheckInterpreterEnabled"] = "false",
                ["Delegation:CheckInterpreterWorkingDirectory"] = Path.Combine(_workspace, "check-interpreter"),
                ["Delegation:DiagnoseEnabled"] = "false",
                ["Delegation:DiagnoseWorkingDirectory"] = Path.Combine(_workspace, "diagnose"),
                ["Delegation:OutputDistillerEnabled"] = "false",
                ["Delegation:OutputDistillerWorkingDirectory"] = Path.Combine(_workspace, "output-distiller"),
                ["Hangfire:Storage"] = HangfireSettings.PostgreSqlStorageName,
                ["Hangfire:ServerEnabled"] = "false",
                [OperatorTokenFile.PathKey] = Path.Combine(_workspace, "operator-token"),
            };
            if (_mode == "enabled")
                values["Postgres:DisableJit"] = "true";
            else if (_mode == "disabled")
                values["Postgres:DisableJit"] = "false";
            return values;
        }

        private static string WriteContentRoot(string raw, string mode)
        {
            var root = Path.Combine(Path.GetTempPath(), "antiphon-jit-content", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var node = JsonNode.Parse(DockerStackDocuments.Read("server/appsettings.json"))?.AsObject()
                ?? throw new InvalidOperationException("server appsettings could not be parsed");
            var connections = node["ConnectionStrings"]?.AsObject()
                ?? throw new InvalidOperationException("server appsettings has no ConnectionStrings");
            connections["DefaultConnection"] = raw;
            var postgres = node["Postgres"]?.AsObject()
                ?? throw new InvalidOperationException("server appsettings has no Postgres");
            if (mode == "absent")
                postgres.Remove("DisableJit");
            else
                postgres["DisableJit"] = mode == "enabled";
            var hangfire = node["Hangfire"]?.AsObject()
                ?? throw new InvalidOperationException("server appsettings has no Hangfire");
            hangfire["ServerEnabled"] = false;
            hangfire["Storage"] = HangfireSettings.PostgreSqlStorageName;
            File.WriteAllText(Path.Combine(root, "appsettings.json"), node.ToJsonString());
            return root;
        }
    }
}
