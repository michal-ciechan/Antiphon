using Antiphon.Server.Application.Services;
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
}
