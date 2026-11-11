using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    internal const string PostgresImage = "postgres:16-alpine";
    internal const string DatabaseUser = "test";

    public virtual Task<OwnedTestDbContainer> CreateAsync() => CreateFromImageAsync(PostgresImage);

    /// <summary>The same container from another image: a baked one already holds the schema.</summary>
    public virtual Task<OwnedTestDbContainer> CreateFromImageAsync(string image)
    {
        var builder = new PostgreSqlBuilder(image)
            .WithDatabase(TestDbFixture.SharedDatabaseName)
            .WithUsername(DatabaseUser)
            .WithPassword("test")
            .Tuned()
            .Own();
        if (!string.Equals(image, PostgresImage, StringComparison.Ordinal))
            builder = TestDbBakedImages.RunFromVolume(builder);
        return Task.FromResult(new OwnedTestDbContainer { Real = builder.Build() });
    }

    public virtual async Task StartAsync(OwnedTestDbContainer container)
    {
        using var phase = TestTiming.BeginFixturePhase("db-assembly", TestPhaseKind.DbCreate, "start");
        System.Diagnostics.Activity.Current?.SetTag("antiphon.reaper", TestDbContainerOwnership.Reaper);
        System.Diagnostics.Activity.Current?.SetTag("antiphon.pg.max_connections", TestDbServerSettings.MaxConnections);
        if (container.Real is null)
            throw new InvalidOperationException("Owned container has no Testcontainers handle.");
        await container.Real.StartAsync().ConfigureAwait(false);
        container.ConnectionString = container.Real.GetConnectionString();
        container.Id = container.Real.Id;
    }

    public virtual string GetConnectionString(OwnedTestDbContainer container) => container.ConnectionString;

    public virtual async Task MigrateAsync(string connectionString)
    {
        using var phase = TestTiming.BeginFixturePhase("db-assembly", TestPhaseKind.DbMigrate, "migrate");
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

    /// <summary>
    /// True when the clone template already exists as a protected template (a baked image ships
    /// it), so <see cref="ProtectTemplateAsync"/> and its ~1 s database copy can be skipped.
    /// </summary>
    public virtual async Task<bool> TemplateIsProtectedAsync(string maintenanceConnectionString)
    {
        await using var maintenance = new NpgsqlConnection(maintenanceConnectionString);
        await maintenance.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT datistemplate AND NOT datallowconn FROM pg_database WHERE datname = @t", maintenance);
        command.Parameters.AddWithValue("t", TestDbFixture.TemplateDatabaseName);
        return await command.ExecuteScalarAsync().ConfigureAwait(false) is true;
    }

    /// <summary>Drops a baked template so a reset shared database can be re-protected.</summary>
    public virtual async Task DropTemplateAsync(string maintenanceConnectionString)
    {
        await using var maintenance = new NpgsqlConnection(maintenanceConnectionString);
        await maintenance.OpenAsync().ConfigureAwait(false);
        var name = TestDbFixture.TemplateDatabaseName;
        await TestDbFixtureLifecycle.ExecuteNonQueryAsync(maintenance,
            $"DO $$ BEGIN IF EXISTS (SELECT 1 FROM pg_database WHERE datname = '{name}') THEN " +
            $"ALTER DATABASE {name} IS_TEMPLATE false; END IF; END $$").ConfigureAwait(false);
        await TestDbFixtureLifecycle.ExecuteNonQueryAsync(maintenance,
            $"DROP DATABASE IF EXISTS {name} WITH (FORCE)").ConfigureAwait(false);
    }

    public virtual Task BeforeAllowConnectionsAsync() => Task.CompletedTask;

    public virtual Task AfterStartAsync() => Task.CompletedTask;

    /// <summary>Removes test Postgres containers left by dead processes on this host (no Ryuk).</summary>
    public virtual Task SweepOrphanedContainersAsync() => TestDbContainerOwnership.SweepAsync();

    public virtual Task AfterMigrateAsync() => Task.CompletedTask;

    public virtual void ClearPool(string connectionString)
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
    }

    public virtual void ClearAllPools() => NpgsqlConnection.ClearAllPools();

    public virtual async Task CloneAsync(TestDbReadyState ready, string databaseName, SemaphoreSlim cloneLock)
    {
        TestDbFixtureLifecycle.ValidateDatabaseName(databaseName);
        using (TestTiming.BeginPhase(TestPhaseKind.DbCreate, "queue"))
            await cloneLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var clone = TestTiming.BeginPhase(TestPhaseKind.DbCreate, "clone");
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
                    // FILE_COPY copies the template's files (one checkpoint) instead of streaming
                    // every page through WAL (the PG15+ default, WAL_LOG). Measured on a fresh
                    // container: the first WAL_LOG clone 1.1 s, later ones ~60 ms; FILE_COPY ~50 ms
                    // every time. Clones run up to TestDbServerSettings.CloneWidth at a time under
                    // cloneLock (parallel clones of the protected template do not block each other).
                    await TestDbFixtureLifecycle.ExecuteNonQueryAsync(
                        maintenance,
                        $"CREATE DATABASE {databaseName} TEMPLATE {TestDbFixture.TemplateDatabaseName} STRATEGY FILE_COPY")
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
        using var drop = TestTiming.BeginPhase(TestPhaseKind.DbCleanup, "drop");
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

    /// <summary>
    /// CARD-1420: plain-SQL dump (schema + data) of the owned container's shared database, taken by
    /// the container's own pg_dump. D-8: only an owned handle and only <c>antiphon_test</c>.
    /// </summary>
    public virtual async Task<byte[]> CaptureSnapshotAsync(OwnedTestDbContainer container, string databaseName)
    {
        var real = RequireOwnedSharedTarget(container, databaseName);
        var path = $"/tmp/c1420-capture-{Guid.NewGuid():N}.sql";
        try
        {
            var dump = await real.ExecAsync(
            [
                "pg_dump", "-U", DatabaseUser, "-d", TestDbFixture.SharedDatabaseName,
                "--no-owner", "--no-privileges", "-f", path
            ]).ConfigureAwait(false);
            ThrowIfExecFailed("pg_dump", dump);
            return await real.ReadFileAsync(path).ConfigureAwait(false);
        }
        finally
        {
            await RemoveContainerFileAsync(real, path).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// CARD-1420: replays a snapshot into the owned container's shared database with psql
    /// <c>ON_ERROR_STOP=1 --single-transaction</c>; a non-zero exit or any error line fails it.
    /// </summary>
    public virtual async Task RestoreSnapshotAsync(OwnedTestDbContainer container, string databaseName, byte[] sql)
    {
        var real = RequireOwnedSharedTarget(container, databaseName);
        var path = $"/tmp/c1420-restore-{Guid.NewGuid():N}.sql";
        await real.CopyAsync(sql, path).ConfigureAwait(false);
        try
        {
            var restore = await real.ExecAsync(
            [
                "psql", "-X", "-q", "-v", "ON_ERROR_STOP=1", "--single-transaction",
                "-U", DatabaseUser, "-d", TestDbFixture.SharedDatabaseName, "-f", path
            ]).ConfigureAwait(false);
            ThrowIfExecFailed("psql", restore);
        }
        finally
        {
            await RemoveContainerFileAsync(real, path).ConfigureAwait(false);
        }
    }

    public virtual async Task<IReadOnlyList<string>> ReadAppliedMigrationsAsync(string connectionString)
    {
        var unpooled = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        await using var connection = new NpgsqlConnection(unpooled);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"",
            connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var applied = new List<string>();
        while (await reader.ReadAsync().ConfigureAwait(false))
            applied.Add(reader.GetString(0));
        return applied;
    }

    /// <summary>The migrations assembly's ordered migration IDs (reflection only, no connection).</summary>
    public virtual IReadOnlyList<string> GetAssemblyMigrations() => AssemblyMigrations.Value;

    /// <summary>CARD-1420: drops and recreates the owned container's shared database after a failed restore.</summary>
    public virtual async Task ResetSharedDatabaseAsync(string maintenanceConnectionString)
    {
        await using var maintenance = new NpgsqlConnection(maintenanceConnectionString);
        await maintenance.OpenAsync().ConfigureAwait(false);
        await TestDbFixtureLifecycle.ExecuteNonQueryAsync(
            maintenance,
            $"DROP DATABASE IF EXISTS {TestDbFixture.SharedDatabaseName} WITH (FORCE)").ConfigureAwait(false);
        await TestDbFixtureLifecycle.ExecuteNonQueryAsync(
            maintenance,
            $"CREATE DATABASE {TestDbFixture.SharedDatabaseName}").ConfigureAwait(false);
    }

    // The fingerprint's reflection list (the same IDs EF's GetMigrations returns, pinned by
    // TestMigrationFingerprintTests) without building an AppDbContext, which costs ~2 s of JIT.
    private static readonly Lazy<IReadOnlyList<string>> AssemblyMigrations = new(() =>
        TestMigrationFingerprint.Default.MigrationIds);

    private static PostgreSqlContainer RequireOwnedSharedTarget(OwnedTestDbContainer container, string databaseName)
    {
        if (!string.Equals(databaseName, TestDbFixture.SharedDatabaseName, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Schema snapshots target only {TestDbFixture.SharedDatabaseName}, not '{databaseName}'.",
                nameof(databaseName));
        return container.Real
            ?? throw new InvalidOperationException("Owned container has no Testcontainers handle.");
    }

    private static void ThrowIfExecFailed(string tool, DotNet.Testcontainers.Containers.ExecResult result)
    {
        var errors = (result.Stderr ?? "")
            .Split('\n')
            .Where(line => line.Contains("ERROR:", StringComparison.Ordinal)
                || line.Contains("FATAL:", StringComparison.Ordinal)
                || line.Contains("PANIC:", StringComparison.Ordinal))
            .ToArray();
        if (result.ExitCode != 0 || errors.Length > 0)
        {
            var detail = errors.Length > 0 ? string.Join(" | ", errors.Take(5)) : (result.Stderr ?? "").Trim();
            throw new InvalidOperationException($"{tool} exited {result.ExitCode}: {detail}");
        }
    }

    private static async Task RemoveContainerFileAsync(PostgreSqlContainer real, string path)
    {
        try
        {
            await real.ExecAsync(["rm", "-f", path]).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort: the file lives in this process's own container, which teardown removes.
        }
    }

    public virtual async ValueTask DisposeOwnedAsync(OwnedTestDbContainer container)
    {
        if (container.Real is null)
            return;
        var id = container.Real.Id;
        await container.Real.DisposeAsync().ConfigureAwait(false);
        // Without the reaper, Testcontainers' dispose stops the container but keeps it.
        await TestDbContainerOwnership.RemoveAsync(id).ConfigureAwait(false);
    }
}

internal sealed class TestDbFixtureLifecycle
{
    private static readonly System.Text.RegularExpressions.Regex SafeDatabaseName =
        new("^[a-z][a-z0-9_]{0,62}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly object _gate = new();
    private readonly TestDbOperations _ops;
    private readonly TestSchemaCache _cache;
    private readonly TestDbBakedImages _images;
    private readonly int _cloneWidth = TestDbServerSettings.CloneWidth;
    private readonly SemaphoreSlim _cloneLock;
    private Task<TestDbReadyState>? _task;
    private Task? _disposeTask;
    private Task _orphanSweep = Task.CompletedTask;
    private readonly int _poolCapacity;
    private TestDbClonePool? _pool;
    private bool _disposeStarted;
    private TestDbReadyState? _ready;

    /// <summary>
    /// CARD-1420 D-7: the default lifecycle reads the schema cache from the environment; injected
    /// operations without a cache keep the migrate path.
    /// </summary>
    public TestDbFixtureLifecycle(
        TestDbOperations? operations = null, TestSchemaCache? cache = null, TestDbBakedImages? images = null,
        int? poolCapacity = null)
    {
        _poolCapacity = poolCapacity ?? (operations is null ? TestDbClonePool.CapacityFromEnvironment() : 0);
        _cloneLock = new SemaphoreSlim(_cloneWidth, _cloneWidth);
        _ops = operations ?? new TestDbOperations();
        _cache = cache ?? (operations is null ? TestSchemaCache.FromEnvironment() : TestSchemaCache.Disabled);
        _images = images ?? (operations is null ? TestDbBakedImages.FromEnvironment() : TestDbBakedImages.Disabled);
    }

    public int Create;
    public int Start;
    public int Migrate;
    public int Protect;
    public int Restore;
    public int ImageStart;
    public int Capture;
    public int CacheFallback;
    public int ResetSharedDatabase;
    public int DisposeOwned;
    public int Drop;
    public int TeardownDispose;
    public int PoolHit;
    public int PoolMiss;
    public TestDbClonePool? Pool => _pool;
    public string? ContainerId { get; private set; }
    public string? SchemaSource { get; private set; }
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
        string? pooled = null;
        if (_pool is { } pool)
        {
            using (TestTiming.BeginPhase(TestPhaseKind.DbCreate, "pool"))
            {
                var hit = pool.TryTake(out pooled);
                if (hit)
                    Interlocked.Increment(ref PoolHit);
                else
                    Interlocked.Increment(ref PoolMiss);
                System.Diagnostics.Activity.Current?.SetTag("antiphon.pool", hit ? "hit" : "miss");
                System.Diagnostics.Activity.Current?.SetTag("antiphon.pool.depth", pool.Depth);
                if (!hit)
                    pooled = null;
            }
        }

        var databaseName = pooled ?? $"test_{Guid.NewGuid():N}";
        try
        {
            if (pooled is null)
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
            // Name the clone: run ae84478d g80 showed only "disposed object", not which drop lost the race.
            if (_disposeStarted)
                throw new ObjectDisposedException(
                    nameof(TestDbFixture),
                    $"Clone {databaseName} was dropped after the assembly teardown started.");
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

        // Before the container goes: an in-flight fill would otherwise fail against it.
        if (_pool is { } pool)
            await pool.StopAsync().ConfigureAwait(false);
        await _ops.DisposeOwnedAsync(ready.Container).ConfigureAwait(false);
        Interlocked.Increment(ref DisposeOwned);
        await _images.WaitForBakesAsync().ConfigureAwait(false);
        try { await _orphanSweep.ConfigureAwait(false); }
        catch (Exception) { /* the sweep reports its own failures */ }
        Interlocked.Increment(ref TeardownDispose);
    }

    private async Task<TestDbReadyState> BootstrapAsync()
    {
        BootstrapContext = SynchronizationContext.Current;
        OwnedTestDbContainer? container = null;
        try
        {
            Interlocked.Increment(ref Create);
            // Off the startup path: a crashed process's container costs disk, not this run's time.
            _orphanSweep = Task.Run(_ops.SweepOrphanedContainersAsync);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var schemaKey = ComputeSchemaKey();
            var baked = schemaKey is { } k && await BakedImageExistsAsync(k.Key).ConfigureAwait(false);
            container = baked
                ? await _ops.CreateFromImageAsync(TestDbBakedImages.ImageName(schemaKey!.Value.Key)).ConfigureAwait(false)
                : await _ops.CreateAsync().ConfigureAwait(false);
            ContainerId = container.Id;

            await _ops.StartAsync(container).ConfigureAwait(false);
            Interlocked.Increment(ref Start);
            await _ops.AfterStartAsync().ConfigureAwait(false);
            ContainerId = container.Id ?? ContainerId;
            var shared = _ops.GetConnectionString(container);
            var maintenance = DeriveMaintenance(shared);

            var templateReady = false;
            if (baked)
                templateReady = await PrepareFromBakedImageAsync(shared, maintenance, schemaKey!.Value.Key, timer).ConfigureAwait(false);
            else if (await PrepareSharedSchemaAsync(container, shared, maintenance, schemaKey).ConfigureAwait(false) is { } sql)
                _images.BakeInBackground(schemaKey!.Value.Key, sql);

            _ops.ClearPool(shared);
            ClearedPools.Add(shared);

            if (!templateReady)
            {
                await _ops.ProtectTemplateAsync(
                    maintenance,
                    TestDbFixture.SharedDatabaseName,
                    TestDbFixture.TemplateDatabaseName).ConfigureAwait(false);
                Interlocked.Increment(ref Protect);
            }

            var ready = new TestDbReadyState
            {
                Container = container,
                SharedConnectionString = shared,
                MaintenanceConnectionString = maintenance
            };
            lock (_gate)
            {
                _ready = ready;
                if (_poolCapacity > 0 && !_disposeStarted)
                    _pool = TestDbClonePool.Start(
                        _poolCapacity, name => _ops.CloneAsync(ready, name, _cloneLock), _cloneWidth);
            }
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

    /// <summary>
    /// CARD-1420 D-6: restore the shared database from the schema cache on a hit, otherwise migrate
    /// and publish a snapshot. A failed restore resets the database and migrates; a failed capture
    /// is logged and ignored. One stderr line per bootstrap names the outcome.
    /// </summary>
    /// <summary>
    /// The schema key when the cache or baked images will use it; null when both are off or the
    /// key cannot be computed (then the shared schema is migrated, uncached).
    /// </summary>
    private (string Key, string ServerSha)? ComputeSchemaKey()
    {
        if (!_cache.IsEnabled && !_images.IsEnabled)
            return null;
        try
        {
            return TestSchemaCache.ComputeDefaultKey();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"C1420 schema-cache key unavailable: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<bool> BakedImageExistsAsync(string key)
    {
        try
        {
            return await _images.ExistsAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"C1420 pg-image lookup failed key={key}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// A baked image already holds the migrated shared database and, normally, its protected clone
    /// template. Its history must equal the assembly's migrations; anything else drops both and
    /// migrates. Returns whether the template is ready to clone.
    /// </summary>
    private async Task<bool> PrepareFromBakedImageAsync(
        string shared, string maintenance, string key, System.Diagnostics.Stopwatch timer)
    {
        Interlocked.Increment(ref ImageStart);
        string? failure = null;
        var templateReady = false;
        try
        {
            using var verify = TestTrace.Span("db-image verify");
            var applied = await _ops.ReadAppliedMigrationsAsync(shared).ConfigureAwait(false);
            var expected = _ops.GetAssemblyMigrations();
            if (!applied.SequenceEqual(expected, StringComparer.Ordinal))
                failure = $"image history has {applied.Count} migrations, assembly has {expected.Count}";
            else
                templateReady = await _ops.TemplateIsProtectedAsync(maintenance).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name + ": " + ex.Message;
        }

        if (failure is null)
        {
            ReportSchemaSource("image", key, timer);
            return templateReady;
        }

        Console.Error.WriteLine($"C1420 pg-image unusable key={key}: {failure}");
        _ops.ClearPool(shared);
        await _ops.DropTemplateAsync(maintenance).ConfigureAwait(false);
        await _ops.ResetSharedDatabaseAsync(maintenance).ConfigureAwait(false);
        Interlocked.Increment(ref ResetSharedDatabase);
        await MigrateSharedAsync(shared).ConfigureAwait(false);
        ReportSchemaSource("image-fallback", key, timer);
        return false;
    }

    /// <returns>The schema SQL a baked image can be made from, or null.</returns>
    private async Task<byte[]?> PrepareSharedSchemaAsync(
        OwnedTestDbContainer container, string shared, string maintenance, (string Key, string ServerSha)? schemaKey)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        if (!_cache.IsEnabled || schemaKey is null)
        {
            await MigrateSharedAsync(shared).ConfigureAwait(false);
            ReportSchemaSource("off", "-", timer);
            if (schemaKey is null || !_images.IsEnabled)
                return null;
            return await TryCaptureAsync(container, shared, schemaKey.Value.Key).ConfigureAwait(false);
        }

        var (key, serverSha) = schemaKey.Value;

        if (_cache.TryRead(key, out var snapshot) && snapshot is not null)
        {
            string? failure = null;
            try
            {
                var expected = _ops.GetAssemblyMigrations();
                if (!snapshot.MigrationIds.SequenceEqual(expected, StringComparer.Ordinal))
                    throw new InvalidOperationException("snapshot migrations differ from the assembly migrations");
                await _ops.RestoreSnapshotAsync(container, TestDbFixture.SharedDatabaseName, snapshot.Sql)
                    .ConfigureAwait(false);
                var applied = await _ops.ReadAppliedMigrationsAsync(shared).ConfigureAwait(false);
                if (!applied.SequenceEqual(expected, StringComparer.Ordinal))
                    throw new InvalidOperationException(
                        $"restored history has {applied.Count} migrations, assembly has {expected.Count}");
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
            }

            if (failure is null)
            {
                Interlocked.Increment(ref Restore);
                _cache.Touch(key);
                ReportSchemaSource("hit", key, timer);
                return snapshot.Sql;
            }

            Console.Error.WriteLine($"C1420 schema-cache restore failed key={key}: {failure}");
            Interlocked.Increment(ref CacheFallback);
            _cache.Discard(key);
            _ops.ClearPool(shared);
            await _ops.ResetSharedDatabaseAsync(maintenance).ConfigureAwait(false);
            Interlocked.Increment(ref ResetSharedDatabase);
            await MigrateSharedAsync(shared).ConfigureAwait(false);
            ReportSchemaSource("fallback", key, timer);
            return null;
        }

        await MigrateSharedAsync(shared).ConfigureAwait(false);
        byte[]? captured = null;
        try
        {
            var sql = await _ops.CaptureSnapshotAsync(container, TestDbFixture.SharedDatabaseName).ConfigureAwait(false);
            var applied = await _ops.ReadAppliedMigrationsAsync(shared).ConfigureAwait(false);
            if (!applied.SequenceEqual(_ops.GetAssemblyMigrations(), StringComparer.Ordinal))
                throw new InvalidOperationException("migrated history differs from the assembly migrations");
            Interlocked.Increment(ref Capture);
            captured = sql;
            if (_cache.Publish(key, new TestSchemaSnapshot(sql, [.. applied], serverSha, TestDbOperations.PostgresImage)))
                _cache.Prune(DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"C1420 schema-cache capture failed key={key}: {ex.GetType().Name}: {ex.Message}");
        }

        ReportSchemaSource("miss", key, timer);
        return captured;
    }

    private async Task<byte[]?> TryCaptureAsync(OwnedTestDbContainer container, string shared, string key)
    {
        try
        {
            var sql = await _ops.CaptureSnapshotAsync(container, TestDbFixture.SharedDatabaseName).ConfigureAwait(false);
            var applied = await _ops.ReadAppliedMigrationsAsync(shared).ConfigureAwait(false);
            if (!applied.SequenceEqual(_ops.GetAssemblyMigrations(), StringComparer.Ordinal))
                throw new InvalidOperationException("migrated history differs from the assembly migrations");
            Interlocked.Increment(ref Capture);
            return sql;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"C1420 pg-image capture failed key={key}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private async Task MigrateSharedAsync(string shared)
    {
        await _ops.MigrateAsync(shared).ConfigureAwait(false);
        Interlocked.Increment(ref Migrate);
        await _ops.AfterMigrateAsync().ConfigureAwait(false);
        ThrowIfProbeFault("after-migrate");
    }

    private void ReportSchemaSource(string source, string key, System.Diagnostics.Stopwatch timer)
    {
        SchemaSource = source;
        Console.Error.WriteLine(
            $"C1420 schema-cache {source} key={key} ms={timer.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }

    internal static DbContextOptions<AppDbContext> BuildOptions(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            })
            .Options;

    internal static DbContextOptions<AppDbContext> BuildOptions(
        string connectionString, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            });
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return builder.Options;
    }

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
