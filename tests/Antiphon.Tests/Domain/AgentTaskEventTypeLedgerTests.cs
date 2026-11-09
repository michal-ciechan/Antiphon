using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Domain;

[Category("Unit")]
public class AgentTaskEventTypeLedgerTests
{
    /// <summary>
    /// CARD-0824 S2. AgentTaskEventType is stored as its integer and appended, never renumbered, so
    /// two members sharing a value read back as whichever name the runtime picks. S2 first gave 41 to
    /// LandingBranchDeletion while master's S1c had already given it to WorkspaceSlotDecision; both
    /// branches compiled alone and only the combined tree carried the duplicate.
    /// </summary>
    [Test]
    public void Every_event_type_has_its_own_value()
    {
        var duplicates = Enum.GetNames<AgentTaskEventType>()
            .GroupBy(name => (int)Enum.Parse<AgentTaskEventType>(name))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group)}")
            .ToList();

        duplicates.ShouldBeEmpty("an appended AgentTaskEventType takes the next free value");
    }

    /// <summary>CARD-1803. The digest seat page is a new event, appended after LandingBranchDeletion.</summary>
    [Test]
    public void C1803_BlockedSeatEscalatedIs43()
    {
        var names = Enum.GetNames<AgentTaskEventType>();
        names.ShouldContain("BlockedSeatEscalated");
        ((int)Enum.Parse<AgentTaskEventType>("BlockedSeatEscalated")).ShouldBe(43);
        ((int)AgentTaskEventType.LandingBranchDeletion).ShouldBe(42);
        Enum.GetValues<AgentTaskEventType>().Select(value => (int)value).Max().ShouldBe(43);
    }
}
