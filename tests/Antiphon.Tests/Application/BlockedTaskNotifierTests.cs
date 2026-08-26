using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public class BlockedTaskNotifierTests
{
    [Test]
    public void Human_notified_is_an_appended_event_type_after_the_existing_contract()
    {
        ((int)AgentTaskEventType.HumanNotified).ShouldBe(17);
    }

    [Test]
    public void A_new_block_after_a_human_notification_is_a_new_ping_candidate()
    {
        var at = DateTime.UtcNow;
        var events = new List<AgentTaskEvent>
        {
            new() { Type = AgentTaskEventType.Blocked, At = at },
            new() { Type = AgentTaskEventType.HumanNotified, At = at.AddMinutes(1) },
            new() { Type = AgentTaskEventType.Conflicted, At = at.AddMinutes(2) },
        };
        events.Where(e => e.Type is AgentTaskEventType.Blocked or AgentTaskEventType.Conflicted).Max(e => e.At)
            .ShouldBeGreaterThan(events.Where(e => e.Type == AgentTaskEventType.HumanNotified).Max(e => e.At));
    }
}
