using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;
using OwnerFacts = Antiphon.Server.Application.Services.WorkspaceReservationLiveness.OwnerFacts;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0664 D-1: the owner-liveness predicate that decides whether a reservation row blocks a retirement claim.</summary>
[Category("Unit")]
public sealed class WorkspaceReservationLivenessTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);
    private static readonly DateTime Aged = Now - Grace - TimeSpan.FromSeconds(1);
    private static readonly DateTime Fresh = Now - TimeSpan.FromSeconds(1);

    [Test]
    public void C664_FreshRowBlocksWhateverTheOwner()
    {
        OwnerFacts[] owners =
        [
            OwnerFacts.None,
            new(true, AgentTaskStatus.Succeeded, false, false, null),
            new(true, AgentTaskStatus.Failed, false, false, null),
            new(false, null, false, true, SessionStatus.Stopped),
        ];
        foreach (var owner in owners)
            Launch(Fresh, owner).ShouldBeTrue($"a Launch row inside the grace window blocks for {owner}");
    }

    [Test]
    [Arguments(AgentTaskStatus.Queued)]
    [Arguments(AgentTaskStatus.Dispatched)]
    [Arguments(AgentTaskStatus.Working)]
    [Arguments(AgentTaskStatus.Blocked)]
    public void C664_LiveTaskOwnerBlocksAfterGrace(AgentTaskStatus status) =>
        Launch(Aged, new OwnerFacts(true, status, false, false, null)).ShouldBeTrue();

    [Test]
    [Arguments(AgentTaskStatus.Succeeded)]
    [Arguments(AgentTaskStatus.Failed)]
    [Arguments(AgentTaskStatus.Canceled)]
    public void C664_TerminalTaskOwnerDoesNotBlockAfterGrace(AgentTaskStatus status) =>
        Launch(Aged, new OwnerFacts(true, status, false, false, null)).ShouldBeFalse();

    [Test]
    public void C664_PendingLandKeepsTerminalTaskBlocking() =>
        Launch(Aged, new OwnerFacts(true, AgentTaskStatus.Succeeded, true, false, null)).ShouldBeTrue();

    [Test]
    [Arguments(SessionStatus.Created)]
    [Arguments(SessionStatus.Starting)]
    [Arguments(SessionStatus.Running)]
    [Arguments(SessionStatus.Stopping)]
    public void C664_LiveSessionOwnerBlocksAfterGrace(SessionStatus status) =>
        Launch(Aged, new OwnerFacts(false, null, false, true, status)).ShouldBeTrue();

    [Test]
    [Arguments(SessionStatus.Stopped)]
    [Arguments(SessionStatus.Failed)]
    public void C664_EndedSessionOwnerDoesNotBlockAfterGrace(SessionStatus status) =>
        Launch(Aged, new OwnerFacts(false, null, false, true, status)).ShouldBeFalse();

    [Test]
    [Arguments("missing-task")]
    [Arguments("missing-session")]
    [Arguments("unattributed")]
    public void C664_MissingOrUnattributedOwnerDoesNotBlockAfterGrace(string shape)
    {
        // A row naming an owner that no longer exists carries the same facts as an unattributed
        // row: nothing found. The shapes differ only in which lookup came back empty.
        var owner = shape switch
        {
            "missing-task" => new OwnerFacts(false, null, false, false, null),
            "missing-session" => new OwnerFacts(false, null, false, false, null),
            _ => OwnerFacts.None,
        };
        Launch(Aged, owner).ShouldBeFalse();
    }

    [Test]
    [Arguments(WorkspaceReservationKind.Retirement)]
    [Arguments(WorkspaceReservationKind.HistoricalFence)]
    public void C664_RetirementAndFenceRowsAlwaysBlock(WorkspaceReservationKind kind)
    {
        var otherRetirement = Guid.NewGuid();
        var claim = Guid.NewGuid();
        WorkspaceReservationLiveness.Blocks(kind, otherRetirement, claim, Aged, Now, Grace, OwnerFacts.None)
            .ShouldBeTrue("another retirement's row or a historical fence blocks regardless of age and owner");
        WorkspaceReservationLiveness.Blocks(kind, otherRetirement, claim, Aged, Now, Grace,
                new OwnerFacts(true, AgentTaskStatus.Succeeded, false, true, SessionStatus.Stopped))
            .ShouldBeTrue();
        if (kind == WorkspaceReservationKind.Retirement)
            WorkspaceReservationLiveness.Blocks(kind, claim, claim, Aged, Now, Grace, OwnerFacts.None)
                .ShouldBeFalse("the claim's own retirement row never blocks it");
    }

    private static bool Launch(DateTime createdAt, OwnerFacts owner) =>
        WorkspaceReservationLiveness.Blocks(WorkspaceReservationKind.Launch, null, Guid.NewGuid(), createdAt, Now, Grace, owner);
}
