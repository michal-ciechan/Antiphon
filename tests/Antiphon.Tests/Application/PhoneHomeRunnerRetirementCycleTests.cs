using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class PhoneHomeRunnerRetirementCycleTests
{
    [Test]
    public async Task Retired_placeholder_supports_two_container_cycles_in_one_directory_lifetime()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider();
        var settings = RollingRunnerSettings.Pair("test-main", "test-temp", leaseSeconds: 3600);
        await using var host = await PhoneHomeTestHost.StartAsync(clock, schema.ConnectionString, configured: settings);
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var id = RollingRunnerSettings.Server2Temp;
        var placeholder = new SessionRunnerState
        {
            RunnerId = id, Draining = true, RetiredAt = clock.GetUtcNow(),
            RetireReason = "placeholder", UpdatedAt = clock.GetUtcNow(),
        };
        db.SessionRunnerStates.Add(placeholder);
        await db.SaveChangesAsync();
        host.Directory.ApplyState(id, RunnerStateService.ToState(placeholder));
        var state = new RunnerStateService(new DbRunnerStateStore(db), host.Directory, Options.Create(settings), clock);
        var retire = new RunnerRetireService(db, host.Directory, settings, clock, NullLogger.Instance);
        await state.ClearAsync(id, "reactivate placeholder", null, CancellationToken.None);
        await state.DrainAsync(id, "verify container A", RollingRunnerSettings.Server2, false, CancellationToken.None);
        var storeA = Guid.NewGuid();
        await using var peerA = await host.ConnectPeerAsync(runnerId: id, storeId: storeA, secret: "test-temp");
        var liveA = await host.WaitLiveAsync(runnerId: id);
        host.Directory.MarkRecovered(liveA);
        host.Directory.Status(id).DispatchEligible.ShouldBeTrue();
        host.Directory.Status(id).AcceptingNewWork.ShouldBeFalse();
        await state.ClearAsync(id, "container A verified", null, CancellationToken.None);
        host.Directory.Status(id).AcceptingNewWork.ShouldBeTrue();
        await state.DrainAsync(id, "retire container A", RollingRunnerSettings.Server2, true, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(settings.RetireMinDrainSeconds));
        (await retire.RunAsync(CancellationToken.None)).ShouldBe(0);
        clock.Advance(TimeSpan.FromSeconds(settings.RetireIdleSeconds));
        (await retire.RunAsync(CancellationToken.None)).ShouldBe(1);
        peerA.Retires.Single().Force.ShouldBeFalse();
        placeholder.RetiredAt.ShouldNotBeNull();
        host.Directory.Disconnect(liveA, "container removed");
        peerA.Socket.Abort();
        clock.Advance(TimeSpan.FromSeconds(settings.LeaseSeconds));
        await state.ClearAsync(id, "reactivate second cycle", null, CancellationToken.None);
        placeholder.RetiredAt.ShouldBeNull();
        await state.DrainAsync(id, "verify container B", RollingRunnerSettings.Server2, false, CancellationToken.None);
        var storeB = Guid.NewGuid();
        // This real registration fails StoreMismatch on the original implementation.
        var registration = host.Directory.Register(host.Registration(bootId: Guid.NewGuid(), storeId: storeB, runnerId: id));
        registration.RunnerStoreId.ShouldBe(storeB);
        registration.Epoch.ShouldBeGreaterThan(liveA.Epoch);
        using var socket = System.Net.WebSockets.WebSocket.CreateFromStream(new MemoryStream(),
            new System.Net.WebSockets.WebSocketCreationOptions { IsServer = true });
        var liveB = host.Directory.AcceptConnect(id, registration.Ticket, socket);
        host.Directory.MarkRecovered(liveB);
        host.Directory.Status(id).DispatchEligible.ShouldBeTrue();
        host.Directory.Status(id).AcceptingNewWork.ShouldBeFalse();
        await state.ClearAsync(id, "container B verified", null, CancellationToken.None);
        host.Directory.Status(id).AcceptingNewWork.ShouldBeTrue();
        host.Directory.GetLiveStoreId(id).ShouldBe(storeB);
        host.Directory.GetLiveStoreId(RollingRunnerSettings.Server2).ShouldBeNull();
        host.Directory.Disconnect(liveB, "test ended");
    }
}
