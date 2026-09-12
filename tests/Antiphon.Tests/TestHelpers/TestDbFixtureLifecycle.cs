using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Antiphon.Server.Infrastructure.Data;

namespace Antiphon.Tests.TestHelpers;

internal sealed class OwnedTestDbContainer
{
    public PostgreSqlContainer? Real { get; init; }
    public string ConnectionString { get; set; } = "";
    public string? Id { get; set; }
}

internal sealed class TestDbReadyState
{
    public required OwnedTestDbContainer Container { get; init; }
    public required string SharedConnectionString { get; init; }
    public required string MaintenanceConnectionString { get; init; }
}

internal class TestDbOperations
{
    public virtual Task<OwnedTestDbContainer> CreateAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase(TestDbFixture.SharedDatabaseName)
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        return Task.FromResult(new OwnedTestDbContainer { Real = container });
    }

    public virtual async Task StartAsync(OwnedTestDbContainer container)
    {
        if (container.Real is null)
            throw new InvalidOperationException("Owned container has no Testcontainers handle.");
        await container.Real.StartAsync().ConfigureAwait(false);
        container.ConnectionString = container.Real.GetConnectionString();
        container.Id = container.Real.Id;
    }

    public virtual string GetConnectionString(OwnedTestDbContainer container) => container.ConnectionString;

    public virtual async Task MigrateAsync(string connectionString)
    {
        var options = TestDbFixtureLifecycle.BuildOptions(connectionString);
        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync().ConfigureAwait(false);
    }

    public virtual async Task ProtectTemplateAsync(
        string maintenanceConnectionString,
        string sharedDatabaseName,
        string templateDatabaseName)
    {
        await using var maintenance = new NpgsqlConnection(maintenanceConnectionString);
        await maintenance.OpenAsync().ConfigureAwait(false);
        await TestDbFixtureLifecycle.TerminateBackendsAsync(maintenance, sharedDatabaseName).ConfigureAwait(false);
        await TestDbFixtureLifecycle.ExecuteNonQueryAsync(
            maintenance,
            $"CREATE DATABASE {templateDatabaseName} TEMPLATE {sharedDatabaseName}").ConfigureAwait(false);
        await AfterIsTemplateAsync().ConfigureAwait(false);
        await TestDbFixtureLifecycle.ExecuteNonQueryAsync(
            maintenance,
            $"ALTER DATABASE {templateDatabaseName} IS_TEMPLATE true").ConfigureAwait(false);
        await BeforeAllowConnectionsAsync().ConfigureAwait(false);
        await TestDbFixtureLifecycle.ExecuteNonQueryAsync(
            maintenance,
            $"ALTER DATABASE {templateDatabaseName} ALLOW_CONNECTIONS false").ConfigureAwait(false);
    }

    public virtual Task AfterIsTemplateAsync() => Task.CompletedTask;

    public virtual Task BeforeAllowConnectionsAsync() => Task.CompletedTask;

    public virtual Task AfterStartAsync() => Task.CompletedTask;

    public virtual Task AfterMigrateAsync() => Task.CompletedTask;

    public virtual void ClearPool(string connectionString)
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
    }

    public virtual void ClearAllPools() => NpgsqlConnection.ClearAllPools();

    public virtual async Task CloneAsync(TestDbReadyState ready, string databaseName, SemaphoreSlim cloneLock)
    {
        TestDbFixtureLifecycle.ValidateDatabaseName(databaseName);
        await cloneLock.WaitAsync().ConfigureAwait(false);
        try
        {
            const int maxAttempts = 5;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    await using var maintenance = new NpgsqlConnection(ready.MaintenanceConnectionString);
                    await maintenance.OpenAsync().ConfigureAwait(false);
                    await TestDbFixtureLifecycle.TerminateBackendsAsync(
                        maintenance,
                        TestDbFixture.TemplateDatabaseName).ConfigureAwait(false);
                    await TestDbFixtureLifecycle.ExecuteNonQueryAsync(
                        maintenance,
                        $"CREATE DATABASE {databaseName} TEMPLATE {TestDbFixture.TemplateDatabaseName}")
                        .ConfigureAwait(false);
                    return;
                }
                catch (PostgresException ex) when (
                    ex.SqlState == PostgresErrorCodes.ObjectInUse && attempt < maxAttempts)
                {
                    await Task.Delay(50 * attempt).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException(
                $"Failed to clone '{databaseName}' from template '{TestDbFixture.TemplateDatabaseName}'.");
        }
        finally
        {
            cloneLock.Release();
        }
    }

    public virtual async Task DropAsync(TestDbReadyState ready, string databaseName)
    {
        TestDbFixtureLifecycle.ValidateDatabaseName(databaseName);
        var clonedConnectionString = new NpgsqlConnectionStringBuilder(ready.SharedConnectionString)
        {
            Database = databaseName
        }.ConnectionString;
        NpgsqlConnection.ClearPool(new NpgsqlConnection(clonedConnectionString));

        await using var maintenance = new NpgsqlConnection(ready.MaintenanceConnectionString);
        await maintenance.OpenAsync().ConfigureAwait(false);
        await TestDbFixtureLifecycle.ExecuteNonQueryAsync(
            maintenance,
            $"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)").ConfigureAwait(false);
    }

    public virtual async ValueTask DisposeOwnedAsync(OwnedTestDbContainer container)
    {
        if (container.Real is not null)
            await container.Real.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class TestDbFixtureLifecycle
{
    private static readonly System.Text.RegularExpressions.Regex SafeDatabaseName =
        new("^[a-z][a-z0-9_]{0,62}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly TestDbOperations _ops;
    private readonly SemaphoreSlim _cloneLock = new(1, 1);
    private Task<TestDbReadyState>? _task;
    private Task? _disposeTask;
    private bool _disposeStarted;
    private TestDbReadyState? _ready;

    public TestDbFixtureLifecycle(TestDbOperations? operations = null)
    {
        _ops = operations ?? new TestDbOperations();
    }

    public int Create;
    public int Start;
    public int Migrate;
    public int Protect;
    public int DisposeOwned;
    public int Drop;
    public int TeardownDispose;
    public string? ContainerId { get; private set; }
    public ConcurrentBag<string> ClearedPools { get; } = [];
    public SynchronizationContext? BootstrapContext { get; private set; }

    public bool IsRequested
    {
        get { lock (_gate) return _task is not null; }
    }

    public string State
    {
        get
        {
            Task<TestDbReadyState>? task;
            lock (_gate) task = _task;
            if (task is null) return "never-requested";
            if (!task.IsCompleted) return "starting";
            if (task.IsFaulted) return "faulted";
            return "ready";
        }
    }

    public string ConnectionString =>
        EnsureReadyAsync().ConfigureAwait(false).GetAwaiter().GetResult().SharedConnectionString;

    public string MaintenanceConnectionString =>
        EnsureReadyAsync().ConfigureAwait(false).GetAwaiter().GetResult().MaintenanceConnectionString;

    public DbContextOptions<AppDbContext> CreateDbContextOptions() =>
        BuildOptions(ConnectionString);

    public AppDbContext CreateDbContext() => new(CreateDbContextOptions());

    public Task<TestDbReadyState> EnsureReadyAsync()
    {
        lock (_gate)
        {
            if (_disposeStarted)
                throw new ObjectDisposedException(nameof(TestDbFixture));
            _task ??= Task.Run(() => BootstrapAsync());
            return _task;
        }
    }

    public async Task<IsolatedTestSchema> CreateIsolatedSchemaAsync()
    {
        var ready = await EnsureReadyAsync().ConfigureAwait(false);
        var databaseName = $"test_{Guid.NewGuid():N}";
        try
        {
            await _ops.CloneAsync(ready, databaseName, _cloneLock).ConfigureAwait(false);
            var connectionString = new NpgsqlConnectionStringBuilder(ready.SharedConnectionString)
            {
                Database = databaseName
            }.ConnectionString;
            return new IsolatedTestSchema(databaseName, connectionString, this);
        }
        catch (Exception ex)
        {
            try
            {
                await DropClonedDatabaseAsync(databaseName).ConfigureAwait(false);
            }
            catch (Exception dropEx)
            {
                throw new AggregateException(ex, dropEx);
            }

            throw;
        }
    }

    public async Task DropClonedDatabaseAsync(string databaseName)
    {
        TestDbReadyState ready;
        lock (_gate)
        {
            if (_disposeStarted)
                throw new ObjectDisposedException(nameof(TestDbFixture));
            ready = _ready ?? throw new ObjectDisposedException(nameof(TestDbFixture));
        }

        Interlocked.Increment(ref Drop);
        await _ops.DropAsync(ready, databaseName).ConfigureAwait(false);
    }

    public Task DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null)
                return _disposeTask;
            _disposeStarted = true;
            var init = _task;
            _disposeTask = DisposeCoreAsync(init);
            return _disposeTask;
        }
    }

    private async Task DisposeCoreAsync(Task<TestDbReadyState>? init)
    {
        if (init is null)
            return;

        TestDbReadyState ready;
        try
        {
            ready = await init.ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        await _ops.DisposeOwnedAsync(ready.Container).ConfigureAwait(false);
        Interlocked.Increment(ref DisposeOwned);
        Interlocked.Increment(ref TeardownDispose);
    }

    private async Task<TestDbReadyState> BootstrapAsync()
    {
        BootstrapContext = SynchronizationContext.Current;
        OwnedTestDbContainer? container = null;
        try
        {
            Interlocked.Increment(ref Create);
            container = await _ops.CreateAsync().ConfigureAwait(false);
            ContainerId = container.Id;

            await _ops.StartAsync(container).ConfigureAwait(false);
            Interlocked.Increment(ref Start);
            await _ops.AfterStartAsync().ConfigureAwait(false);
            ContainerId = container.Id ?? ContainerId;
            var shared = _ops.GetConnectionString(container);
            var maintenance = DeriveMaintenance(shared);

            await _ops.MigrateAsync(shared).ConfigureAwait(false);
            Interlocked.Increment(ref Migrate);
            await _ops.AfterMigrateAsync().ConfigureAwait(false);
            ThrowIfProbeFault("after-migrate");

            _ops.ClearPool(shared);
            ClearedPools.Add(shared);

            await _ops.ProtectTemplateAsync(
                maintenance,
                TestDbFixture.SharedDatabaseName,
                TestDbFixture.TemplateDatabaseName).ConfigureAwait(false);
            Interlocked.Increment(ref Protect);

            var ready = new TestDbReadyState
            {
                Container = container,
                SharedConnectionString = shared,
                MaintenanceConnectionString = maintenance
            };
            lock (_gate)
                _ready = ready;
            return ready;
        }
        catch (Exception ex)
        {
            Exception? cleanup = null;
            try
            {
                if (container is not null)
                {
                    Interlocked.Increment(ref DisposeOwned);
                    await _ops.DisposeOwnedAsync(container).ConfigureAwait(false);
                }
            }
            catch (Exception c)
            {
                cleanup = c;
            }

            if (cleanup is not null)
                throw new AggregateException(ex, cleanup);
            throw;
        }
    }

    internal static DbContextOptions<AppDbContext> BuildOptions(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            })
            .Options;

    internal static string DeriveMaintenance(string sharedConnectionString) =>
        new NpgsqlConnectionStringBuilder(sharedConnectionString)
        {
            Database = "postgres",
            Pooling = false
        }.ConnectionString;

    internal static void ValidateDatabaseName(string databaseName)
    {
        if (!SafeDatabaseName.IsMatch(databaseName))
            throw new ArgumentException($"Unsafe database name '{databaseName}'.", nameof(databaseName));
    }

    internal static async Task TerminateBackendsAsync(NpgsqlConnection connection, string databaseName)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = @db AND pid <> pg_backend_pid()
            """,
            connection);
        command.Parameters.AddWithValue("db", databaseName);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    internal static async Task ExecuteNonQueryAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 60
        };
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void ThrowIfProbeFault(string stage)
    {
        var raw = Environment.GetEnvironmentVariable(TestDbFixture.ProbeMarker);
        if (string.IsNullOrEmpty(raw))
            return;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("fault", out var fault)
                && string.Equals(fault.GetString(), stage, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("C476-FAULT " + stage);
            }
        }
        catch (JsonException)
        {
            // Marker is present but not JSON; ignore for fault injection.
        }
    }
}
