using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
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
public sealed class HostStatsPollServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private sealed class EmptyCounters : IHostStatsAntiphonCounters
    {
        public Task<IReadOnlyDictionary<string, HostStatsAntiphonDto>> ReadAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, HostStatsAntiphonDto>>(new Dictionary<string, HostStatsAntiphonDto>());
    }

    private static HostStatsPollService Service(PhoneHomeTestHost host, FakeTimeProvider time,
        HostStatsCache cache, RecordingHostStatsEventBus bus) =>
        new(host.Directory, cache, new EmptyCounters(), bus, Options.Create(new HostStatsSettings()),
            time, NullLogger<HostStatsPollService>.Instance);

    private static PhoneHomeFrame Reply(PhoneHomeFrame request, RunnerHostStatsDto dto) =>
        new(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId, request.Operation,
            JsonSerializer.SerializeToElement(dto, PhoneHomeFraming.Json));

    private static async Task<(PhoneHomeTestHost Host, PhoneHomeScriptedPeer Peer, FakeTimeProvider Time,
        HostStatsCache Cache, RecordingHostStatsEventBus Bus, HostStatsPollService Poll)> SetupAsync()
    {
        var time = new FakeTimeProvider(Now);
        var host = await PhoneHomeTestHost.StartAsync(clock: time);
        host.Local.HostStats = HostStatsCacheTests.Sample(cpu: 25);
        var peer = await host.ConnectPeerAsync();
        peer.Reply = frame => frame.Operation == PhoneHomeOperation.HostStats
            ? Reply(frame, HostStatsCacheTests.Sample(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), 42))
            : null;
        var cache = new HostStatsCache(new HostStatsSettings(), time);
        var bus = new RecordingHostStatsEventBus();
        return (host, peer, time, cache, bus, Service(host, time, cache, bus));
    }

    [Test]
    public async Task Tick_fills_both_hosts_live_and_publishes_once_to_group_hosts()
    {
        var (host, peer, _, cache, bus, poll) = await SetupAsync();
        await using (host)
        await using (peer)
        {
            await poll.TickOnceAsync();
            var rows = cache.Project();
            rows.Count.ShouldBe(2);
            rows.ShouldAllBe(r => r.State == "live");
            rows.Single(r => r.HostId == host.AllowedRunnerId).Current!.CpuPercent.ShouldBe(42);
            bus.Events.Count.ShouldBe(1);
            bus.Events[0].Group.ShouldBe("hosts");
            bus.Events[0].EventName.ShouldBe("HostStatsUpdated");
            await poll.TickOnceAsync();
            bus.Events.Count.ShouldBe(1);
        }
    }

    [Test]
    public async Task Silent_peer_goes_stale_while_desktop_stays_live()
    {
        var (host, peer, time, cache, _, poll) = await SetupAsync();
        await using (host)
        await using (peer)
        {
            await poll.TickOnceAsync();
            peer.SilentFor(PhoneHomeOperation.HostStats);
            var tick = poll.TickOnceAsync();
            while (peer.RequestCount(PhoneHomeOperation.HostStats) < 2)
                await Task.Delay(10);
            time.Advance(TimeSpan.FromMilliseconds(3000));
            await tick.WaitAsync(TimeSpan.FromSeconds(10));
            time.Advance(TimeSpan.FromMilliseconds(12001));
            var rows = cache.Project();
            rows.Single(r => r.HostId == host.AllowedRunnerId).State.ShouldBe("stale");
            rows.Single(r => r.HostId == host.AllowedRunnerId).Current!.CpuPercent.ShouldBe(42);
            rows.Single(r => r.HostId == "desktop").State.ShouldBe("live");
        }
    }

    [Test]
    public async Task Unsupported_peer_projects_unsupported()
    {
        var (host, peer, _, cache, _, poll) = await SetupAsync();
        await using (host)
        await using (peer)
        {
            peer.Reply = frame => frame.Operation == PhoneHomeOperation.HostStats
                ? new PhoneHomeFrame(PhoneHomeFrameKind.Error, frame.Epoch, frame.RequestId, frame.Operation,
                    ErrorCode: PhoneHomeProblemTypes.UnsupportedOperation, StatusCode: 400) : null;
            await poll.TickOnceAsync();
            var row = cache.Project().Single(r => r.HostId == host.AllowedRunnerId);
            row.State.ShouldBe("unsupported");
            row.Current.ShouldBeNull();
            cache.Project().Single(r => r.HostId == "desktop").State.ShouldBe("live");
        }
    }

    [Test]
    public async Task Local_client_fault_does_not_fault_the_tick()
    {
        var (host, peer, time, cache, _, poll) = await SetupAsync();
        await using (host)
        await using (peer)
        {
            host.Local.HostStatsFault = new HttpRequestException("fault");
            await Should.NotThrowAsync(() => poll.TickOnceAsync());
            cache.Project().Single(r => r.HostId == "desktop").State.ShouldBe("offline");
            cache.Project().Single(r => r.HostId == host.AllowedRunnerId).State.ShouldBe("live");
            host.Local.HostStatsFault = null;
            await poll.TickOnceAsync();
            host.Local.HostStatsFault = new HttpRequestException("fault again");
            await poll.TickOnceAsync();
            time.Advance(TimeSpan.FromMilliseconds(15001));
            var desktop = cache.Project().Single(r => r.HostId == "desktop");
            desktop.State.ShouldBe("stale");
            desktop.Current!.CpuPercent.ShouldBe(25);
        }
    }

    [Test]
    public async Task Disconnected_peer_projects_offline_and_recovers_live_on_reconnect()
    {
        var (host, peer, _, cache, _, poll) = await SetupAsync();
        await using (host)
        {
            await poll.TickOnceAsync();
            await peer.DisposeAsync();
            await poll.TickOnceAsync();
            var offline = cache.Project().Single(r => r.HostId == host.AllowedRunnerId);
            offline.State.ShouldBe("offline");
            offline.Current!.CpuPercent.ShouldBe(42);
            await using var next = await host.ConnectPeerAsync();
            next.Reply = frame => frame.Operation == PhoneHomeOperation.HostStats
                ? Reply(frame, HostStatsCacheTests.Sample(cpu: 51)) : null;
            await poll.TickOnceAsync();
            cache.Project().Single(r => r.HostId == host.AllowedRunnerId).State.ShouldBe("live");
        }
    }

    [Test]
    public async Task Publish_failure_does_not_fault_the_tick_and_the_next_tick_republishes()
    {
        var (host, peer, _, _, bus, poll) = await SetupAsync();
        await using (host)
        await using (peer)
        {
            bus.ThrowOnce = true;
            await Should.NotThrowAsync(() => poll.TickOnceAsync());
            bus.Events.Count.ShouldBe(0);
            await poll.TickOnceAsync();
            bus.Events.Count.ShouldBe(1);
        }
    }
}
