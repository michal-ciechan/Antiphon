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

    [Test]
    public async Task Program_wires_both_sync_debt_sweeps_into_the_dispatcher()
    {
        await using var first = _factory.Services.CreateAsyncScope();
        await using var second = _factory.Services.CreateAsyncScope();
        var dispatcherA = first.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();
        var dispatcherB = second.ServiceProvider.GetRequiredService<AgentTaskDispatcher>();

        dispatcherA.SyncDebtSweepsWired.BlockedTaskSync.ShouldBeTrue(
            "BlockedTaskSyncRecoveryService was not injected");
        dispatcherA.SyncDebtSweepsWired.SettlementSync.ShouldBeTrue(
            "SettlementSyncRecoveryService was not injected");
        dispatcherB.SyncDebtSweepsWired.BlockedTaskSync.ShouldBeTrue(
            "BlockedTaskSyncRecoveryService was not injected");
        dispatcherB.SyncDebtSweepsWired.SettlementSync.ShouldBeTrue(
            "SettlementSyncRecoveryService was not injected");

        first.ServiceProvider.GetRequiredService<BlockedTaskSyncRecoveryService>();
        first.ServiceProvider.GetRequiredService<SettlementSyncRecoveryService>();
        second.ServiceProvider.GetRequiredService<BlockedTaskSyncRecoveryService>();
        second.ServiceProvider.GetRequiredService<SettlementSyncRecoveryService>();
    }
}
