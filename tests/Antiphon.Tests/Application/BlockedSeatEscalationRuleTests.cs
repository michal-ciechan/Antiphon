using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1803 V-2. The seat page qualifies from the snapshot and the undelivered note. No database.</summary>
[Category("Unit")]
public sealed class BlockedSeatEscalationRuleTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc);
    private static readonly Guid TaskId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Test]
    public void C1803_QualifiesAtTheBoundWithAnUndeliveredNote()
    {
        Qualifies(SeatClass.IdleBlocked, occupies: true, "runner", "server2", TimeSpan.FromHours(6), note: true)
            .ShouldBeTrue();
    }

    [Test]
    public void C1803_BelowTheBoundDoesNotQualify()
    {
        var age = TimeSpan.FromHours(6) - TimeSpan.FromTicks(1);
        Qualifies(SeatClass.IdleBlocked, occupies: true, "runner", "server2", age, note: true)
            .ShouldBeFalse();
    }

    [Test]
    public void C1803_MissingNoteDoesNotQualify()
    {
        Qualifies(SeatClass.IdleBlocked, occupies: true, "runner", "server2", TimeSpan.FromHours(6), note: false)
            .ShouldBeFalse();
    }

    [Test]
    public void C1803_LocalHostDoesNotQualify()
    {
        Qualifies(SeatClass.IdleBlocked, occupies: true, "local", "server2", TimeSpan.FromHours(6), note: true)
            .ShouldBeFalse();
        Qualifies(SeatClass.IdleBlocked, occupies: true, "runner", "local", TimeSpan.FromHours(6), note: true)
            .ShouldBeFalse();
    }

    [Test]
    public void C1803_SpecialistRoleDoesNotQualify()
    {
        foreach (var role in new[] { AgentTaskRole.Check, AgentTaskRole.Distill, AgentTaskRole.Diagnose })
        {
            Qualifies(SeatClass.IdleBlocked, occupies: true, "runner", "server2", TimeSpan.FromHours(6), note: true, role)
                .ShouldBeFalse(role.ToString());
        }
    }

    [Test]
    public void C1803_ActiveOrTerminalSeatDoesNotQualify()
    {
        Qualifies(SeatClass.Active, occupies: true, "runner", "server2", TimeSpan.FromHours(6), note: true)
            .ShouldBeFalse();
        Qualifies(SeatClass.IdleTerminal, occupies: true, "runner", "server2", TimeSpan.FromHours(6), note: true)
            .ShouldBeFalse();
    }

    private static bool Qualifies(
        SeatClass seatClass,
        bool occupies,
        string kind,
        string hostId,
        TimeSpan age,
        bool note,
        AgentTaskRole role = AgentTaskRole.Code,
        AgentTaskStatus status = AgentTaskStatus.Blocked)
    {
        var idleSince = Now - age;
        var seat = new SeatObservation(
            "server2", Guid.NewGuid(), "running", 1, idleSince, occupies, false, false,
            "Running", TaskId, status, 1, role, null, null, null, seatClass, idleSince, "unknown");
        var host = new HostOccupancyObservation(
            hostId, kind, "listed", null, 1, 0, 1, 0, 0, 1, 0, 0, null, null, idleSince, [seat]);
        var snapshot = new SeatOccupancySnapshot(Now, [host]);
        return BlockedSeatEscalation.Qualifies(
            status, role, snapshot, TaskId, Now, escalationHours: 6, note);
    }
}
