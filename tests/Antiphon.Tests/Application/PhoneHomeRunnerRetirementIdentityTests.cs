using System.Net.WebSockets;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class PhoneHomeRunnerRetirementIdentityTests
{
    [Test]
    public async Task Cleared_retirement_allows_one_new_store_after_disconnect_and_lease_expiry()
    {
        var clock = new FakeTimeProvider();
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        var first = host.Directory.Register(host.Registration());
        RetireAndClear(host, clock);
        // The deployment holds new work away from temp while verifying its new container.
        host.Directory.ApplyState(host.AllowedRunnerId, State(clock, draining: true));
        clock.Advance(TimeSpan.FromSeconds(90));
        var replacement = host.Registration(bootId: Guid.NewGuid(), storeId: Guid.NewGuid());
        var second = host.Directory.Register(replacement);
        second.RunnerStoreId.ShouldBe(replacement.RunnerStoreId);
        second.Epoch.ShouldBeGreaterThan(first.Epoch);
        host.Directory.GetLiveStoreId(host.AllowedRunnerId).ShouldBe(replacement.RunnerStoreId);
        Should.Throw<ConflictException>(() => host.Directory.PeekTicket(host.AllowedRunnerId, first.Ticket))
            .Code.ShouldBe(PhoneHomeProblemTypes.InvalidTicket);
        clock.Advance(TimeSpan.FromSeconds(90));
        RefuseStore(host, host.Registration(storeId: Guid.NewGuid()));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Cleared_retirement_preserves_live_store_even_after_lease_expiry(bool expired)
    {
        var clock = new FakeTimeProvider();
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        using var socket = WebSocket.CreateFromStream(new MemoryStream(), new WebSocketCreationOptions { IsServer = true });
        var ticket = host.Directory.Register(host.Registration());
        var live = host.Directory.AcceptConnect(host.AllowedRunnerId, ticket.Ticket, socket);
        RetireAndClear(host, clock);
        if (expired) clock.Advance(TimeSpan.FromSeconds(90));
        RefuseStore(host, host.Registration(storeId: Guid.NewGuid()));
        RefuseStore(host, host.Registration(bootId: Guid.NewGuid(), storeId: Guid.NewGuid()));
        host.Directory.SnapshotLive().ShouldBeSameAs(live);
        host.Directory.GetLiveStoreId(host.AllowedRunnerId).ShouldBe(host.StoreId);
    }

    [Test]
    public async Task Cleared_retirement_preserves_unexpired_registration_lease_then_allows_replacement()
    {
        var clock = new FakeTimeProvider();
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        host.Directory.Register(host.Registration());
        RetireAndClear(host, clock);
        var request = host.Registration(storeId: Guid.NewGuid());
        clock.Advance(TimeSpan.FromSeconds(89));
        RefuseStore(host, request);
        clock.Advance(TimeSpan.FromSeconds(1));
        host.Directory.Register(request).RunnerStoreId.ShouldBe(request.RunnerStoreId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Ordinary_offline_slot_keeps_store_binding_even_after_drain_clear(bool clearDrain)
    {
        var clock = new FakeTimeProvider();
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        host.Directory.Register(host.Registration());
        if (clearDrain)
        {
            host.Directory.ApplyState(host.AllowedRunnerId, State(clock, draining: true));
            host.Directory.ApplyState(host.AllowedRunnerId, State(clock));
        }
        clock.Advance(TimeSpan.FromSeconds(90));
        RefuseStore(host, host.Registration(storeId: Guid.NewGuid()));
        RefuseStore(host, host.Registration(bootId: Guid.NewGuid(), storeId: Guid.NewGuid()));
    }

    [Test]
    public async Task Same_store_reconnect_consumes_retirement_clear_authorization()
    {
        var clock = new FakeTimeProvider();
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        var first = host.Directory.Register(host.Registration());
        RetireAndClear(host, clock);
        var reconnect = host.Directory.Register(host.Registration());
        reconnect.Epoch.ShouldBeGreaterThan(first.Epoch);
        clock.Advance(TimeSpan.FromSeconds(90));
        RefuseStore(host, host.Registration(storeId: Guid.NewGuid()));
        var newBoot = host.Registration(bootId: Guid.NewGuid());
        host.Directory.Register(newBoot).ProcessBootId.ShouldBe(newBoot.ProcessBootId);
    }

    [Test]
    public async Task Retirement_stamp_alone_refuses_same_and_foreign_stores()
    {
        var clock = new FakeTimeProvider();
        await using var host = await PhoneHomeTestHost.StartAsync(clock);
        host.Directory.Register(host.Registration());
        host.Directory.ApplyState(host.AllowedRunnerId, State(clock, draining: true, retired: true));
        clock.Advance(TimeSpan.FromSeconds(90));
        foreach (var request in new[] { host.Registration(), host.Registration(storeId: Guid.NewGuid()) })
            Should.Throw<ConflictException>(() => host.Directory.Register(request))
                .Code.ShouldBe(PhoneHomeProblemTypes.RunnerRetired);
    }

    private static void RefuseStore(PhoneHomeTestHost host, PhoneHomeRegistrationRequest request) =>
        Should.Throw<ConflictException>(() => host.Directory.Register(request)).Code
            .ShouldBe(PhoneHomeProblemTypes.StoreMismatch);

    private static void RetireAndClear(PhoneHomeTestHost host, FakeTimeProvider clock)
    {
        host.Directory.ApplyState(host.AllowedRunnerId, State(clock, draining: true, retired: true));
        host.Directory.ApplyState(host.AllowedRunnerId, State(clock));
        host.Directory.ApplyState(host.AllowedRunnerId, State(clock));
    }

    private static RunnerState State(FakeTimeProvider clock, bool draining = false, bool retired = false) =>
        new(draining, draining ? clock.GetUtcNow() : null, "test", null, false, null,
            retired ? clock.GetUtcNow() : null, retired ? "retired" : null);
}
