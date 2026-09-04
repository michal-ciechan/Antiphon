using Antiphon.Server.Application.Settings;
using Hangfire;
using Hangfire.InMemory;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>CARD-0298: Hangfire storage options and recurring-job registration shared by Program and tests.</summary>
internal static class HangfireConfiguration
{
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
}
