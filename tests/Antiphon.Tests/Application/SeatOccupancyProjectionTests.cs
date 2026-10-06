using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1079 V-1 to V-4. Classification, idle clock, severity, divergence and pushed.</summary>
[Category("Unit")]
public sealed class SeatOccupancyProjectionTests
{
    [Test]
    [Arguments("c1079-class-Queued", AgentTaskStatus.Queued, SeatClass.IdleUnbound)]
    [Arguments("c1079-class-Dispatched", AgentTaskStatus.Dispatched, SeatClass.Active)]
    [Arguments("c1079-class-Working", AgentTaskStatus.Working, SeatClass.Active)]
    [Arguments("c1079-class-Blocked", AgentTaskStatus.Blocked, SeatClass.IdleBlocked)]
    [Arguments("c1079-class-Succeeded", AgentTaskStatus.Succeeded, SeatClass.IdleTerminal)]
    [Arguments("c1079-class-Failed", AgentTaskStatus.Failed, SeatClass.IdleTerminal)]
    [Arguments("c1079-class-Canceled", AgentTaskStatus.Canceled, SeatClass.IdleTerminal)]
    [Arguments("c1079-class-null", null, SeatClass.IdleUnbound)]
    public void C1079_Classify_names_blocked_terminal_and_unbound_seats_idle_and_working_seats_active(
        string label, AgentTaskStatus? status, SeatClass expected)
    {
        SeatOccupancyProjection.Classify(occupies: true, desktopLive: true, pooledWarm: false, status)
            .ShouldBe(expected, label);
    }

    [Test]
    public void C1079_Pooled_warm_and_exited_seats_are_never_idle_or_orphan()
    {
        AgentTaskStatus?[] statuses =
        [
            AgentTaskStatus.Queued,
            AgentTaskStatus.Dispatched,
            AgentTaskStatus.Working,
            AgentTaskStatus.Blocked,
            AgentTaskStatus.Succeeded,
            AgentTaskStatus.Failed,
            AgentTaskStatus.Canceled,
            null,
        ];
        foreach (var status in statuses)
        {
            SeatOccupancyProjection.Classify(occupies: false, desktopLive: true, pooledWarm: false, status)
                .ShouldBe(SeatClass.Exited, status?.ToString() ?? "null");
        }

        SeatOccupancyProjection.Classify(occupies: false, desktopLive: true, pooledWarm: true, AgentTaskStatus.Blocked)
            .ShouldBe(SeatClass.Exited);
        SeatOccupancyProjection.Classify(occupies: true, desktopLive: true, pooledWarm: true, AgentTaskStatus.Blocked)
            .ShouldBe(SeatClass.PooledWarm);
        SeatOccupancyProjection.Classify(occupies: true, desktopLive: true, pooledWarm: true, AgentTaskStatus.Working)
            .ShouldBe(SeatClass.PooledWarm);
        RunnerSlotService.IsOrphan(desktopLive: true, openTask: false, pooledWarm: true).ShouldBeFalse();
        RunnerSlotService.IsOrphan(desktopLive: false, openTask: true, pooledWarm: true).ShouldBeFalse();

        SeatOccupancyProjection.Classify(occupies: true, desktopLive: true, pooledWarm: false, AgentTaskStatus.Blocked)
            .ShouldBe(SeatClass.IdleBlocked);
        RunnerSlotService.IsOrphan(desktopLive: true, openTask: false, pooledWarm: false).ShouldBeTrue();
    }

    [Test]
    [Arguments(29, null)]
    [Arguments(30, AlertSeverity.Warning)]
    [Arguments(179, AlertSeverity.Warning)]
    [Arguments(180, AlertSeverity.Error)]
    [Arguments(1000, AlertSeverity.Error)]
    public void C1079_Severity_ladder_is_null_below_warning_then_warning_then_error(int minutes, AlertSeverity? expected)
    {
        SeatOccupancyProjection.Severity(TimeSpan.FromMinutes(minutes), new AttentionSettings()).ShouldBe(expected);
    }

    [Test]
    public void C1079_Idle_since_prefers_blocked_event_then_completion_then_runner_start()
    {
        var start = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);
        var blocked = start.AddMinutes(12);
        var completed = start.AddHours(2);

        SeatOccupancyProjection.IdleSince(SeatClass.IdleBlocked, blocked, completed, start).ShouldBe(blocked);
        SeatOccupancyProjection.IdleSince(SeatClass.IdleBlocked, null, completed, start).ShouldBe(start);
        SeatOccupancyProjection.IdleSince(SeatClass.IdleTerminal, blocked, completed, start).ShouldBe(completed);
        SeatOccupancyProjection.IdleSince(SeatClass.IdleUnbound, blocked, completed, start).ShouldBe(start);
        SeatOccupancyProjection.Pushed(true).ShouldBe("yes");
        SeatOccupancyProjection.Pushed(false).ShouldBe("unknown");
        SeatOccupancyProjection.Divergence(4, 1).ShouldBe(3);
    }
}
