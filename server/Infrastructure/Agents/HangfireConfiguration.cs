using System.Globalization;
using System.Net.Sockets;
using Antiphon.Server.Application.Settings;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Hangfire.Storage;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NpgsqlTypes;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>CARD-0298: Hangfire storage options and recurring-job registration shared by Program and tests.</summary>
internal static class HangfireConfiguration
{
    public static void AddExpectationWorker(IServiceCollection services, HangfireSettings hangfire,
        ExpectationWatchdogSettings watchdog)
    {
        if (!ExpectationWatchdogJob.ShouldRun(hangfire, watchdog)) return;
        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 1;
            options.Queues = ["expectations"];
        });
    }
    public static InMemoryStorageOptions CreateStorageOptions(HangfireSettings settings) =>
        new() { MaxExpirationTime = TimeSpan.FromDays(settings.HistoryRetentionDays) };

    public static void AddOrUpdateCensusJob(IRecurringJobManager manager, ZombieCensusSettings settings)
    {
        manager.AddOrUpdate<ZombieCensusJob>(
            settings.RecurringJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            settings.Cron,
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId)
            });
    }

    /// <summary>CARD-0653: register the pending slot-release reconcile and run it once now.</summary>
    public static void AddOrUpdateRunnerSlotReconcileJob(
        IRecurringJobManager manager, PhoneHomeRunnerSettings settings)
    {
        manager.AddOrUpdate<RunnerSlotReconcileJob>(
            RunnerSlotReconcileJob.RecurringJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            settings.SlotReconcileCron,
            new RecurringJobOptions());
        manager.Trigger(RunnerSlotReconcileJob.RecurringJobId);
    }

    public static void AddOrUpdateRunnerRetireJob(
        IRecurringJobManager manager, PhoneHomeRunnerSettings settings)
    {
        manager.AddOrUpdate<RunnerRetireJob>(
            RunnerRetireJob.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            settings.RunnerRetireCron,
            new RecurringJobOptions());
    }

    public static void AddOrUpdateWorktreeResidueJob(
        IRecurringJobManager manager, WorktreeResidueSettings settings)
    {
        manager.AddOrUpdate<WorktreeResidueJob>(
            settings.RecurringJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            settings.Cron,
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId)
            });
    }

    /// <summary>CARD-1677 S1. Registered only while RunnerWorktreeRetention:Enabled is true.</summary>
    public static void AddOrUpdateRunnerWorktreeRetentionJob(
        IRecurringJobManager manager, RunnerWorktreeRetentionSettings settings)
    {
        manager.AddOrUpdate<RunnerWorktreeRetentionJob>(
            settings.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            settings.Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    /// <summary>CARD-0824 S2. Registered only while Landing:DeleteSourceBranchAfterPublication is true.</summary>
    public static void AddOrUpdatePublishedBranchDeletionJob(IRecurringJobManager manager, LandingSettings settings)
    {
        manager.AddOrUpdate<PublishedBranchDeletionJob>(
            settings.SourceBranchDeletionJobId,
            job => job.RunAsync(CancellationToken.None),
            settings.SourceBranchDeletionCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    /// <summary>CARD-0824 S4a. Registered only while WorktreeLegacyRelease:Enabled is true (the tick re-reads it).</summary>
    public static void AddOrUpdateDesktopLegacyReleaseJob(
        IRecurringJobManager manager, WorktreeLegacyReleaseSettings settings)
    {
        manager.AddOrUpdate<DesktopLegacyReleasePolicyJob>(
            settings.RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            settings.Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    public static void AddOrUpdateExpectationWatchdogJob(IRecurringJobManager manager)
    {
        manager.AddOrUpdate<ExpectationWatchdogJob>(
            ExpectationWatchdogJob.RecurringJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            "* * * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        manager.Trigger(ExpectationWatchdogJob.RecurringJobId);
    }

    /// <summary>CARD-1320. Minute reminder. The per-agent toggle is the switch. No startup trigger.</summary>
    public static void AddOrUpdateKeepSixJob(IRecurringJobManager manager)
    {
        manager.AddOrUpdate<KeepSixReminderJob>(
            KeepSixReminderJob.RecurringJobId,
            job => job.ExecuteAsync(CancellationToken.None),
            "* * * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    }

    /// <summary>
    /// CARD-1743. Disabled census must not leave a stored recurring row. In-memory storage has
    /// no row to remove; durable storage does.
    /// </summary>
    public static void RegisterCensusJob(IRecurringJobManager manager, ZombieCensusSettings census)
    {
        if (census.Enabled)
            AddOrUpdateCensusJob(manager, census);
        else
            manager.RemoveIfExists(census.RecurringJobId);
    }

    /// <summary>
    /// CARD-1743. A disabled flag removes the recurring row. PostgreSql keeps that row
    /// across a process restart unless startup removes it.
    /// </summary>
    public static void RegisterWorktreeResidueJob(IRecurringJobManager manager, WorktreeResidueSettings settings)
    {
        if (settings.Enabled)
            AddOrUpdateWorktreeResidueJob(manager, settings);
        else
            manager.RemoveIfExists(settings.RecurringJobId);
    }

    public static void RegisterExpectationWatchdogJob(
        IRecurringJobManager manager, HangfireSettings hangfire, ExpectationWatchdogSettings watchdog)
    {
        if (ExpectationWatchdogJob.ShouldRun(hangfire, watchdog))
            AddOrUpdateExpectationWatchdogJob(manager);
        else
            manager.RemoveIfExists(ExpectationWatchdogJob.RecurringJobId);
    }

    public static void RegisterRunnerSlotReconcileJob(
        IRecurringJobManager manager, PhoneHomeRunnerSettings settings)
    {
        if (settings.Enabled)
            AddOrUpdateRunnerSlotReconcileJob(manager, settings);
        else
            manager.RemoveIfExists(RunnerSlotReconcileJob.RecurringJobId);
    }

    public static void RegisterRunnerRetireJob(IRecurringJobManager manager, PhoneHomeRunnerSettings settings)
    {
        if (settings.Enabled)
            AddOrUpdateRunnerRetireJob(manager, settings);
        else
            manager.RemoveIfExists(RunnerRetireJob.RecurringJobId);
    }

    /// <summary>
    /// CARD-1743. Unset storage stays on <see cref="InMemoryStorage"/>. PostgreSql registration
    /// does not open the database; <see cref="PreparePostgreSqlStorage"/> installs the schema
    /// after the server has created it.
    /// </summary>
    public static void ConfigureStorage(
        IGlobalConfiguration configuration, HangfireSettings settings, string? connectionString)
    {
        if (!settings.UsesPostgreSql)
        {
            configuration.UseInMemoryStorage(CreateStorageOptions(settings));
            return;
        }

        configuration.UsePostgreSqlStorage(
            bootstrap => bootstrap.UseNpgsqlConnection(connectionString ?? string.Empty),
            CreatePostgreSqlOptions(settings));
    }

    /// <summary>
    /// CARD-1743. Test twin of Program's <c>AddHangfire</c> call. PostgreSql installs the schema
    /// first; both branches then resolve <see cref="JobStorage"/> through <see cref="ConfigureStorage"/>.
    /// The provider is retained: disposing it would dispose the storage the caller still uses.
    /// </summary>
    public static JobStorage CreateStorage(HangfireSettings settings, string? connectionString)
    {
        if (settings.UsesPostgreSql)
            PreparePostgreSqlStorage(settings, connectionString);

        var services = new ServiceCollection();
        services.AddHangfire(config => ConfigureStorage(config, settings, connectionString));
        var provider = services.BuildServiceProvider();
        ConfiguredStorageProviders.Add(provider);
        return provider.GetRequiredService<JobStorage>();
    }

    /// <summary>
    /// CARD-1743. Installs the Hangfire schema or refuses startup. No retry and no in-memory fallback.
    /// </summary>
    public static void PreparePostgreSqlStorage(HangfireSettings settings, string? connectionString)
    {
        if (!settings.UsesPostgreSql)
            return;

        using var connection = OpenProbe(connectionString);
        try
        {
            PostgreSqlObjectsInstaller.Install(connection, settings.SchemaName);
        }
        catch (Exception ex) when (IsConnectivityFailure(ex))
        {
            throw StartupFailure(ex);
        }
    }

    public static HangfireWorkerLease AcquireSingleWorkerLease(string? connectionString)
    {
        var probe = ProbeConnectionString(connectionString);
        var connection = new NpgsqlConnection(probe);
        try
        {
            connection.Open();
            if (!TryTakeWorkerLock(connection))
            {
                connection.Dispose();
                throw new InvalidOperationException(
                    "Hangfire refused to start a second worker against this database.");
            }

            ApplyCrashExpiry(connection);
            return new HangfireWorkerLease(connection, probe);
        }
        catch (Exception ex) when (IsConnectivityFailure(ex))
        {
            ReleaseConnection(connection);
            throw StartupFailure(ex);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentBag<ServiceProvider> ConfiguredStorageProviders = [];

    private static PostgreSqlStorageOptions CreatePostgreSqlOptions(HangfireSettings settings) =>
        new()
        {
            SchemaName = settings.SchemaName,
            PrepareSchemaIfNecessary = false,
            StartupConnectionMaxRetries = 0,
            AllowDegradedModeWithoutStorage = false
        };

    private static NpgsqlConnection OpenProbe(string? connectionString)
    {
        var probe = ProbeConnectionString(connectionString);
        var connection = new NpgsqlConnection(probe);
        try
        {
            connection.Open();
            return connection;
        }
        catch (Exception ex) when (IsConnectivityFailure(ex))
        {
            connection.Dispose();
            throw StartupFailure(ex);
        }
    }

    private static string ProbeConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Hangfire PostgreSQL storage refused startup because ConnectionStrings:DefaultConnection is missing.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 5,
            Pooling = false
        };
        return builder.ConnectionString;
    }

    internal static bool TryTakeWorkerLock(NpgsqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@lockClass, @lockId)";
        command.Parameters.Add(new NpgsqlParameter("lockClass", NpgsqlDbType.Integer)
        {
            Value = HangfireWorkerLease.ClassId
        });
        command.Parameters.Add(new NpgsqlParameter("lockId", NpgsqlDbType.Integer)
        {
            Value = HangfireWorkerLease.LockId
        });
        return command.ExecuteScalar() is true;
    }

    /// <summary>
    /// CARD-1768. A dead host's session ends after idle plus interval times count.
    /// A failed SET refuses startup.
    /// </summary>
    internal static void ApplyCrashExpiry(NpgsqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT set_config('tcp_keepalives_idle', @idle, false),
                   set_config('tcp_keepalives_interval', @interval, false),
                   set_config('tcp_keepalives_count', @count, false)
            """;
        command.Parameters.Add(new NpgsqlParameter("idle", NpgsqlDbType.Text)
        {
            Value = HangfireWorkerLease.KeepaliveIdleSeconds.ToString(CultureInfo.InvariantCulture)
        });
        command.Parameters.Add(new NpgsqlParameter("interval", NpgsqlDbType.Text)
        {
            Value = HangfireWorkerLease.KeepaliveIntervalSeconds.ToString(CultureInfo.InvariantCulture)
        });
        command.Parameters.Add(new NpgsqlParameter("count", NpgsqlDbType.Text)
        {
            Value = HangfireWorkerLease.KeepaliveCount.ToString(CultureInfo.InvariantCulture)
        });
        command.ExecuteNonQuery();
    }

    internal static bool IsConnectivityFailure(Exception ex) =>
        ex is NpgsqlException or TimeoutException or SocketException or ArgumentException
        || (ex is InvalidOperationException && !ex.Message.StartsWith("Hangfire ", StringComparison.Ordinal));

    internal static InvalidOperationException StartupFailure(Exception exception)
    {
        var postgres = FindPostgresException(exception);
        var reason = postgres?.SqlState switch
        {
            PostgresErrorCodes.InvalidPassword
                or PostgresErrorCodes.InvalidAuthorizationSpecification => "authentication failed",
            PostgresErrorCodes.InsufficientPrivilege => "the database role lacks a required privilege",
            PostgresErrorCodes.InvalidCatalogName => "the database does not exist",
            _ => "the database is not reachable"
        };
        return new InvalidOperationException(
            "Hangfire PostgreSQL storage refused startup because " + reason + ".",
            exception);
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
                return postgres;
        }

        return null;
    }

    internal static void ReleaseConnection(NpgsqlConnection? connection)
    {
        if (connection is null)
            return;

        try
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@lockClass, @lockId)";
                command.Parameters.Add(new NpgsqlParameter("lockClass", NpgsqlDbType.Integer)
                {
                    Value = HangfireWorkerLease.ClassId
                });
                command.Parameters.Add(new NpgsqlParameter("lockId", NpgsqlDbType.Integer)
                {
                    Value = HangfireWorkerLease.LockId
                });
                command.ExecuteScalar();
            }
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
        }
        finally
        {
            connection.Dispose();
        }
    }

    /// <summary>
    /// CARD-1768. Released on <see cref="IHostApplicationLifetime.ApplicationStopped"/>.
    /// That event runs after hosted services drain.
    /// </summary>
    public static void ReleaseWorkerLeaseWhenHostStops(
        IHostApplicationLifetime lifetime, HangfireWorkerLease lease) =>
        lifetime.ApplicationStopped.Register(lease.Dispose);
}

/// <summary>CARD-1743. One held session lock is the only Hangfire worker for a database.</summary>
internal sealed class HangfireWorkerLease : IDisposable
{
    internal const int ClassId = 1743;
    internal const int LockId = 1;

    /// <summary>CARD-1768. Silence before the first TCP keepalive on the lease connection.</summary>
    internal const int KeepaliveIdleSeconds = 30;

    /// <summary>CARD-1768. Gap between TCP keepalive probes after the idle time.</summary>
    internal const int KeepaliveIntervalSeconds = 10;

    /// <summary>CARD-1768. Unanswered probes before Postgres drops the dead session.</summary>
    internal const int KeepaliveCount = 3;

    /// <summary>CARD-1768. Idle plus interval times count. A crashed host's lock ends within this.</summary>
    internal const int CrashExpirySeconds =
        KeepaliveIdleSeconds + (KeepaliveIntervalSeconds * KeepaliveCount);

    /// <summary>
    /// CARD-1768. Longest a Postgres restart leaves this process running without the lock.
    /// The check stores nothing.
    /// </summary>
    internal static readonly TimeSpan ReacquireInterval = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private readonly string _probe;
    private NpgsqlConnection? _connection;
    private Timer? _timer;
    private IHostApplicationLifetime? _lifetime;
    private int _disposed;

    internal HangfireWorkerLease(NpgsqlConnection connection, string probe)
    {
        _connection = connection;
        _probe = probe;
    }

    /// <summary>
    /// CARD-1768. Confirms the lock on this session. A dead session opens a new
    /// non-pooled connection. Nothing is stored between checks.
    /// </summary>
    internal void EnsureHeld()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        lock (_gate)
        {
            if (_disposed != 0)
                return;
            if (SessionStillHoldsLock(_connection))
                return;
            ReplaceConnection();
        }
    }

    /// <summary>CARD-1768. The caller starts the re-check. Acquire does not.</summary>
    internal void StartSupervision(IHostApplicationLifetime lifetime, TimeSpan? interval = null)
    {
        _lifetime = lifetime;
        var period = interval ?? ReacquireInterval;
        var timer = new Timer(static state => ((HangfireWorkerLease)state!).OnTick(), this, period, period);
        if (Interlocked.CompareExchange(ref _timer, timer, null) is not null)
            timer.Dispose();
        if (Volatile.Read(ref _disposed) != 0)
            Interlocked.Exchange(ref _timer, null)?.Dispose();
    }

    private void OnTick()
    {
        try
        {
            EnsureHeld();
        }
        catch (Exception)
        {
            try
            {
                _lifetime?.StopApplication();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void ReplaceConnection()
    {
        var fresh = new NpgsqlConnection(_probe);
        try
        {
            fresh.Open();
            if (!HangfireConfiguration.TryTakeWorkerLock(fresh))
                throw new InvalidOperationException(
                    "Hangfire lost its worker lease and another worker holds it.");

            HangfireConfiguration.ApplyCrashExpiry(fresh);
        }
        catch (Exception ex) when (HangfireConfiguration.IsConnectivityFailure(ex))
        {
            HangfireConfiguration.ReleaseConnection(fresh);
            throw HangfireConfiguration.StartupFailure(ex);
        }
        catch
        {
            HangfireConfiguration.ReleaseConnection(fresh);
            throw;
        }

        var previous = Interlocked.Exchange(ref _connection, fresh);
        HangfireConfiguration.ReleaseConnection(previous);
    }

    private static bool SessionStillHoldsLock(NpgsqlConnection? connection)
    {
        if (connection is null || connection.State != System.Data.ConnectionState.Open)
            return false;

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1
                FROM pg_locks
                WHERE locktype = 'advisory'
                  AND classid = @lockClass
                  AND objid = @lockId
                  AND granted
                  AND pid = pg_backend_pid()
                """;
            command.Parameters.Add(new NpgsqlParameter("lockClass", NpgsqlDbType.Integer)
            {
                Value = ClassId
            });
            command.Parameters.Add(new NpgsqlParameter("lockId", NpgsqlDbType.Integer)
            {
                Value = LockId
            });
            return command.ExecuteScalar() is not null;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>CARD-1768. Reads the session keepalive values the lease connection is using.</summary>
    internal (int IdleSeconds, int IntervalSeconds, int Count) ReadCrashExpiry()
    {
        var connection = _connection ?? throw new InvalidOperationException(
            "Hangfire worker lease has no connection.");
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT current_setting('tcp_keepalives_idle'),
                   current_setting('tcp_keepalives_interval'),
                   current_setting('tcp_keepalives_count')
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("Hangfire worker lease could not read its crash-expiry settings.");
        return (
            int.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
            int.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
            int.Parse(reader.GetString(2), CultureInfo.InvariantCulture));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var timer = Interlocked.Exchange(ref _timer, null);
        timer?.Dispose();
        NpgsqlConnection? connection;
        lock (_gate)
            connection = Interlocked.Exchange(ref _connection, null);
        HangfireConfiguration.ReleaseConnection(connection);
    }
}
