using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PhoneHomePendingInventoryTests
{
    [Test]
    public async Task C696Red_Pending_inventory_reads_are_bounded()
    {
        foreach (var empty in new[] { false, true })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync();
            if (empty)
            {
                await using var db = h.Db();
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            }
            for (var i = 0; i < 100; i++) h.Runtime.ListLiveOrUnknownSessions().Contains(h.SessionId).ShouldBe(!empty);
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
                h.Runtime.ListLiveOrUnknownSessions().Contains(h.SessionId).ShouldBe(!empty))));
            h.Probe.Reads.ShouldBe(1, $"132 pending reads share one projection, including empty={empty}");
        }
    }
}
