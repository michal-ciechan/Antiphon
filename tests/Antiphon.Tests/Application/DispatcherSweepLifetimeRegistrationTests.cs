using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0633 D-3: owned-scope sweeps (and abandonment) are armed only when the dispatcher receives
/// the <see cref="SweepInFlightState"/> singleton, so the real host must register it. Resolved from
/// the booted Program's own service collection; nothing here registers it.
/// </summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
public sealed class DispatcherSweepLifetimeRegistrationTests
{
    private readonly AntiphonWebAppFactory _factory;
    public DispatcherSweepLifetimeRegistrationTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    public async Task Program_registers_the_sweep_in_flight_state_as_one_singleton()
    {
        var root = _factory.Services.GetService<SweepInFlightState>();
        root.ShouldNotBeNull("Program.cs must register SweepInFlightState or every sweep silently runs awaited on the tick's context");

        await using var a = _factory.Services.CreateAsyncScope();
        await using var b = _factory.Services.CreateAsyncScope();
        a.ServiceProvider.GetRequiredService<SweepInFlightState>().ShouldBeSameAs(root);
        b.ServiceProvider.GetRequiredService<SweepInFlightState>().ShouldBeSameAs(root,
            "a per-scope instance would forget an abandoned sweep on the next tick");
    }
}
