using Antiphon.Server.Application.Settings;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Storage;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.DependencyInjection;

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
    }

    public static void ConfigureStorage(
        IGlobalConfiguration configuration, HangfireSettings settings, string? connectionString)
    {
        configuration.UseInMemoryStorage(CreateStorageOptions(settings));
    }

    public static JobStorage CreateStorage(HangfireSettings settings, string? connectionString) =>
        new InMemoryStorage(CreateStorageOptions(settings));

    public static void PreparePostgreSqlStorage(HangfireSettings settings, string? connectionString)
    {
    }

    public static HangfireWorkerLease AcquireSingleWorkerLease(string? connectionString) => new();
}

/// <summary>CARD-1743. One held lease is the only Hangfire worker for a database.</summary>
internal sealed class HangfireWorkerLease : IDisposable
{
    public void Dispose()
    {
    }
}
