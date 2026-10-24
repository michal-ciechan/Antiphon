using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class PhoneHomeRecoveryEligibilityTests
{
    [Test]
    [Timeout(30_000)]
    public async Task Lease_expiry_recovers_on_the_same_connection_after_validated_catch_up()
    {
        await using var world = await LeaseWorld.StartAsync();
        var epoch = world.Live.Epoch;
        var reconnects = world.Host.Directory.Status(world.Host.AllowedRunnerId).Reconnects;
        for (var episode = 0; episode < 2; episode++)
        {
            world.Expire();
            var stale = await world.Host.Directory.DescribeAsync(world.Host.AllowedRunnerId, CancellationToken.None);
            stale!.Capacity.ShouldBeNull();
            await world.HeartbeatAsync();
            world.Live.DispatchEligible.ShouldBeFalse("a heartbeat alone is not validated recovery");

            (await world.Pump.RunCycleAsync(world.Stop.Token)).ShouldBeTrue(
                "a resumed heartbeat must trigger catch-up on the existing connection");

            world.Host.Directory.SnapshotLive().ShouldBeSameAs(world.Live);
            world.Live.Epoch.ShouldBe(epoch);
            world.Host.Directory.Status(world.Host.AllowedRunnerId).Reconnects.ShouldBe(reconnects);
            world.Host.Directory.Status(world.Host.AllowedRunnerId).AcceptingNewWork.ShouldBeTrue();
            var recovered = await world.Host.Directory.DescribeAsync(world.Host.AllowedRunnerId, CancellationToken.None);
            recovered!.Capacity.ShouldBe(world.Live.Capacity);
            recovered.Stale.ShouldBeFalse();
            world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(episode + 2);
        }
        world.Observer.Ids.Count.ShouldBe(5, "initial recovery and two expiry/recovery pairs notify once each");
    }

    [Test]
    [Timeout(30_000)]
    public async Task Lease_recovery_ignores_a_foreign_epoch_heartbeat()
    {
        await using var world = await LeaseWorld.StartAsync();
        world.Expire();
        var before = world.Live.LastHeartbeatUtc;
        await world.Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Heartbeat, world.Live.Epoch + 1, Guid.Empty));
        // A same-epoch RPC response fences receipt of the preceding, ignored heartbeat.
        await new PhoneHomeRunnerClient(world.Live).ListAsync(world.Stop.Token);
        world.Live.LastHeartbeatUtc.ShouldBe(before);
        (await world.Pump.RunCycleAsync(world.Stop.Token)).ShouldBeFalse();
        world.Live.DispatchEligible.ShouldBeFalse();

        await world.HeartbeatAsync();
        (await world.Pump.RunCycleAsync(world.Stop.Token)).ShouldBeTrue();
        world.Live.DispatchEligible.ShouldBeTrue();
    }

    [Test]
    [Timeout(30_000)]
    public async Task Lease_recovery_retries_a_failed_inventory_without_restoring_eligibility()
    {
        await using var world = await LeaseWorld.StartAsync();
        world.Expire();
        await world.HeartbeatAsync();
        world.Peer.Reply = frame => frame.Operation == PhoneHomeOperation.List
            ? new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                ErrorCode: "runner_list_failed", ErrorDetail: "inventory unavailable", StatusCode: 503)
            : null;
        (await world.Pump.RunCycleAsync(world.Stop.Token)).ShouldBeFalse();
        world.Live.DispatchEligible.ShouldBeFalse();
        world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(2, "recovery must actually validate inventory");
        (await world.Pump.RunCycleAsync(world.Stop.Token)).ShouldBeFalse();
        world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(2, "the normal retry budget still applies");

        world.Peer.Reply = null;
        world.Clock.Advance(TimeSpan.FromSeconds(Settings(world.Host).CatchUpRetrySeconds));
        await world.HeartbeatAsync();
        (await world.Pump.RunCycleAsync(world.Stop.Token)).ShouldBeTrue();
        world.Live.DispatchEligible.ShouldBeTrue();
    }

    [Test]
    [Arguments("disconnect")]
    [Arguments("draining")]
    [Arguments("retired")]
    [Arguments("backlog")]
    [Arguments("expired")]
    [Arguments("epoch")]
    [Arguments("replacement")]
    [Timeout(30_000)]
    public async Task Lease_recovery_rechecks_refusals_after_inventory(string refusal)
    {
        await using var world = await LeaseWorld.StartAsync();
        world.Expire();
        await world.HeartbeatAsync();
        world.Peer.SilentFor(PhoneHomeOperation.List);
        var cycle = world.Pump.RunCycleAsync(world.Stop.Token);
        await WaitAsync(() => cycle.IsCompleted || world.Peer.RequestCount(PhoneHomeOperation.List) == 2);
        world.Peer.RequestCount(PhoneHomeOperation.List).ShouldBe(2, "same-connection recovery must read inventory");
        var request = world.Peer.Incoming.Last(f => f.Operation == PhoneHomeOperation.List);
        PhoneHomeQueuedEvent? held = null;
        try
        {
            switch (refusal)
            {
                case "disconnect": world.Live.TryRecordDisconnect("transport_abort"); break;
                case "draining":
                case "retired":
                    world.Host.Directory.ApplyState(world.Host.AllowedRunnerId,
                        new RunnerState(true, world.Clock.GetUtcNow(), "maintenance", null, false, null,
                            refusal == "retired" ? world.Clock.GetUtcNow() : null, "maintenance"));
                    break;
                case "backlog":
                    await world.Pump.EndPumpForTest();
                    await world.Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Event, world.Live.Epoch,
                        Guid.Empty, EventName: "held-event", Payload: JsonSerializer.SerializeToElement(new { })));
                    held = await world.Live.Events.ReadAsync(world.Stop.Token);
                    world.Live.PendingEvents.ShouldBe(1);
                    break;
                case "expired": world.Clock.Advance(TimeSpan.FromSeconds(91)); break;
                case "epoch": await world.Host.RegisterAsync(); break;
                case "replacement":
                    await using (var replacement = await world.Host.ConnectPeerAsync())
                        world.Host.Directory.SnapshotLive().ShouldNotBeSameAs(world.Live);
                    break;
            }
            if (refusal != "replacement")
                await world.Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Result, request.Epoch,
                    request.RequestId, request.Operation, JsonSerializer.SerializeToElement(Array.Empty<RunnerSessionDto>())));

            (await cycle.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse(refusal);
            world.Live.DispatchEligible.ShouldBeFalse(refusal);
            world.Host.Directory.Status(world.Host.AllowedRunnerId).AcceptingNewWork.ShouldBeFalse(refusal);
        }
        finally
        {
            held?.Release();
        }
    }

    private static async Task WaitAsync(Func<bool> done)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!done()) await Task.Delay(10, deadline.Token);
    }

    private sealed class LeaseObserver : IRunnerEligibilityObserver
    {
        public List<string> Ids { get; } = [];
        public void Changed(string runnerId) => Ids.Add(runnerId);
    }

    // Each test exercises the connection lifecycle; it needs its own socket/clock. No database
    // or AppHost is started. Existing inventory/transcript persistence coverage shares TestDbFixture.
    private sealed class LeaseWorld : IAsyncDisposable
    {
        public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
        public LeaseObserver Observer { get; } = new();
        public PhoneHomeTestHost Host { get; private set; } = null!;
        public PhoneHomeScriptedPeer Peer { get; private set; } = null!;
        public PhoneHomeLiveConnection Live { get; private set; } = null!;
        public PhoneHomeRecoveryPump Pump { get; private set; } = null!;
        public CancellationTokenSource Stop { get; } = new(TimeSpan.FromSeconds(20));

        public static async Task<LeaseWorld> StartAsync()
        {
            var world = new LeaseWorld();
            world.Host = await PhoneHomeTestHost.StartAsync(world.Clock, observer: world.Observer);
            world.Peer = await world.Host.ConnectPeerAsync();
            world.Live = await world.Host.WaitLiveAsync();
            world.Pump = PhoneHomeRecoveryEligibilityTests.Pump(world.Host, Settings(world.Host),
                world.Host.App.Services.GetRequiredService<IServiceScopeFactory>());
            (await world.Pump.RunCycleAsync(world.Stop.Token)).ShouldBeTrue();
            return world;
        }

        public void Expire()
        {
            Clock.Advance(TimeSpan.FromSeconds(91));
            Host.Directory.Status(Host.AllowedRunnerId).DispatchEligible.ShouldBeFalse();
            Live.SocketOpen.ShouldBeTrue();
        }

        public async Task HeartbeatAsync()
        {
            await Peer.EmitAsync(new PhoneHomeFrame(PhoneHomeFrameKind.Heartbeat, Live.Epoch, Guid.Empty));
            await WaitAsync(() => Live.LastHeartbeatUtc == Clock.GetUtcNow());
        }

        public async ValueTask DisposeAsync()
        {
            Stop.Cancel();
            await Pump.EndPumpForTest();
            Pump.Dispose();
            await Peer.DisposeAsync();
            await Host.DisposeAsync();
            Stop.Dispose();
        }
    }
}
