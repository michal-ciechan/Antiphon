using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class DispatcherAdmissionSchedulingTests
{
    [Test]
    public async Task C1519_freed_seat_is_admitted_while_maintenance_waits()
    {
        await using var f = await DispatcherAdmissionFixture.CreateAsync();
        await f.StartHeldAsync();
        await f.ArmSweepAsync();
        await f.FreeSeatAsync();
        // The initial full timer was observed armed before entering the real watchdog.
        // On the old serial host no new timer can be armed while that sweep is held.
        f.Clock.Advance(TimeSpan.FromSeconds(5));
        await DispatcherAdmissionFixture.ObserveAsync(f.IsDispatchedAsync);
        await f.AssertOutcomeAsync(dispatched: true);
    }
}
