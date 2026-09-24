using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Hangfire;
using Hangfire.InMemory;
using Hangfire.Storage;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0653: which remembered runner sessions count, and which an operator sweep may evict.</summary>
[Category("Unit")]
public class RunnerSlotRulesTests
{
    [Test]
    public async Task Exited_records_do_not_occupy_and_a_warm_pool_is_not_an_orphan()
    {
        RunnerSlotService.OccupiesCapacity("Running").ShouldBeTrue();
        RunnerSlotService.OccupiesCapacity("Starting").ShouldBeTrue();
        RunnerSlotService.OccupiesCapacity("Exited").ShouldBeFalse();
        RunnerSlotService.OccupiesCapacity("Failed").ShouldBeFalse();
        RunnerSlotService.OccupiesCapacity(null).ShouldBeFalse();

        RunnerSlotService.IsOrphan(desktopLive: false, openTask: false, pooledWarm: false).ShouldBeTrue();
        RunnerSlotService.IsOrphan(desktopLive: true, openTask: false, pooledWarm: false).ShouldBeTrue();
        RunnerSlotService.IsOrphan(desktopLive: true, openTask: true, pooledWarm: false).ShouldBeFalse();
        RunnerSlotService.IsOrphan(desktopLive: true, openTask: false, pooledWarm: true).ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Pending_release_reconcile_is_scheduled_and_triggered_at_startup()
    {
        var storage = new InMemoryStorage(HangfireConfiguration.CreateStorageOptions(new HangfireSettings()));
        var manager = new RecurringJobManager(storage);

        HangfireConfiguration.AddOrUpdateRunnerSlotReconcileJob(
            manager, new PhoneHomeRunnerSettings { SlotReconcileCron = "*/3 * * * *" });

        using var connection = storage.GetConnection();
        var job = connection.GetRecurringJobs().ShouldHaveSingleItem();
        job.Id.ShouldBe(RunnerSlotReconcileJob.RecurringJobId);
        job.Cron.ShouldBe("*/3 * * * *");
        storage.GetMonitoringApi().EnqueuedCount("default").ShouldBe(1);
        await Task.CompletedTask;
    }
}
