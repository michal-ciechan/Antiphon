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
            throw Unreachable();
        }
    }

    public static HangfireWorkerLease AcquireSingleWorkerLease(string? connectionString)
    {
        var connection = OpenProbe(connectionString);
        try
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
            if (command.ExecuteScalar() is true)
                return new HangfireWorkerLease(connection);

            connection.Dispose();
            throw new InvalidOperationException(
                "Hangfire refused to start a second worker against this database.");
        }
        catch (Exception ex) when (IsConnectivityFailure(ex))
        {
            connection.Dispose();
            throw Unreachable();
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
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Hangfire PostgreSQL storage refused startup because ConnectionStrings:DefaultConnection is missing.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 5,
            Pooling = false
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch (Exception ex) when (IsConnectivityFailure(ex))
        {
            connection.Dispose();
            throw Unreachable();
        }
    }

    private static bool IsConnectivityFailure(Exception ex) =>
        ex is NpgsqlException or TimeoutException or SocketException or ArgumentException
        || (ex is InvalidOperationException && !ex.Message.StartsWith("Hangfire ", StringComparison.Ordinal));

    private static InvalidOperationException Unreachable() =>
        new("Hangfire PostgreSQL storage refused startup because the database is not reachable.");

    /// <summary>
    /// CARD-1768. Released on <see cref="IHostApplicationLifetime.ApplicationStopping"/>.
    /// That event runs before hosted services drain.
    /// </summary>
    public static void ReleaseWorkerLeaseWhenHostStops(
        IHostApplicationLifetime lifetime, HangfireWorkerLease lease) =>
        lifetime.ApplicationStopping.Register(lease.Dispose);
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

    private NpgsqlConnection? _connection;

    internal HangfireWorkerLease(NpgsqlConnection connection) => _connection = connection;

    /// <summary>CARD-1768. The held connection is not re-checked.</summary>
    internal void EnsureHeld()
    {
    }

    /// <summary>CARD-1768. No periodic re-check is armed.</summary>
    internal void StartSupervision(IHostApplicationLifetime lifetime, TimeSpan? interval = null)
    {
        _ = lifetime;
        _ = interval;
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
        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection is null)
            return;

        try
        {
            if (connection.State == System.Data.ConnectionState.Open)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@lockClass, @lockId)";
                command.Parameters.Add(new NpgsqlParameter("lockClass", NpgsqlDbType.Integer) { Value = ClassId });
                command.Parameters.Add(new NpgsqlParameter("lockId", NpgsqlDbType.Integer) { Value = LockId });
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
}
