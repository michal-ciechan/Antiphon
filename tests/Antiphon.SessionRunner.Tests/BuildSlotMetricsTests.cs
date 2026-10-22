using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1489 S1. The broker's read-only lifecycle metrics (<c>GET /build-slots/metrics</c>): grants,
/// explicit releases, reaps and refusal polls kept apart, acquire-wait and held durations, a 3-hour
/// window and a 4096-record bound that is disclosed when it drops anything. Fake clock throughout.
/// </summary>
[Category("Unit")]
public sealed class BuildSlotMetricsTests
{
    // The same instant as BuildSlotBrokerTests.T0, which its Fixture clock starts at.
    private static readonly DateTime T0 = new(2026, 9, 25, 6, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Queued_wait_and_completed_lease_are_counted_once()
    {
        var time = new FakeTimeProvider(T0);
        var liveness = new BuildSlotBrokerTests.FakeLiveness();
        foreach (var pid in new[] { 101, 102 }) liveness.Start(pid, T0);
        await using var host = await BuildSlotTestHost.StartAsync(
            new() { ["SessionRunner:BuildSlots:MaxConcurrent"] = "1" },
            liveness,
            new BuildSlotBrokerTests.FakeMemory { AvailableBytes = 64L * 1024 * 1024 * 1024 },
            time);

        var holder = await (await host.Http.PostAsJsonAsync("build-slots", Request(101, "holder")))
            .Content.ReadFromJsonAsync<BuildSlotGrant>();
        (await host.Http.PostAsJsonAsync("build-slots", Request(102, "waiter"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        time.Advance(TimeSpan.FromSeconds(30));
        (await host.Http.PostAsJsonAsync("build-slots", Request(102, "waiter"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        time.Advance(TimeSpan.FromSeconds(15));
        (await host.Http.DeleteAsync($"build-slots/{holder!.LeaseId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var waiter = await (await host.Http.PostAsJsonAsync("build-slots", Request(102, "waiter")))
            .Content.ReadFromJsonAsync<BuildSlotGrant>();
        // The holder asking again (a lost response) gets its own lease back: not a second grant.
        (await (await host.Http.PostAsJsonAsync("build-slots", Request(102, "waiter")))
            .Content.ReadFromJsonAsync<BuildSlotGrant>())!.LeaseId.ShouldBe(waiter!.LeaseId);
        time.Advance(TimeSpan.FromSeconds(120));
        (await host.Http.DeleteAsync($"build-slots/{waiter.LeaseId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var response = await host.Http.GetAsync("build-slots/metrics");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var m = body.RootElement;
        m.GetProperty("brokerStartedAtUtc").GetDateTime().ToUniversalTime().ShouldBe(T0);
        m.GetProperty("windowStartUtc").GetDateTime().ToUniversalTime().ShouldBe(T0);
        m.GetProperty("nowUtc").GetDateTime().ToUniversalTime().ShouldBe(T0.AddSeconds(165));
        m.GetProperty("partialWindow").GetBoolean().ShouldBeTrue();
        m.GetProperty("truncated").GetBoolean().ShouldBeFalse();
        m.GetProperty("grants").GetInt32().ShouldBe(2);
        m.GetProperty("releases").GetInt32().ShouldBe(2);
        m.GetProperty("reaps").GetInt32().ShouldBe(0);
        m.GetProperty("busyRefusals").GetInt32().ShouldBe(2);
        m.GetProperty("memoryFloorRefusals").GetInt32().ShouldBe(0);
        var wait = m.GetProperty("acquireWait");
        wait.GetProperty("count").GetInt32().ShouldBe(2);
        wait.GetProperty("meanSeconds").GetDouble().ShouldBe(22.5);
        wait.GetProperty("p95Seconds").GetDouble().ShouldBe(45);
        wait.GetProperty("maxSeconds").GetDouble().ShouldBe(45);
        var held = m.GetProperty("held");
        held.GetProperty("count").GetInt32().ShouldBe(2);
        held.GetProperty("meanSeconds").GetDouble().ShouldBe(82.5);
        held.GetProperty("p95Seconds").GetDouble().ShouldBe(120);
        m.GetProperty("releasesPerHour").GetDouble().ShouldBe(2 / (165 / 3600.0), 1e-9);
    }

    [Test]
    public void Retried_acquire_and_double_release_count_once_and_reaps_are_separate()
    {
        var f = new BuildSlotBrokerTests.Fixture(maxConcurrent: 1);
        var a = f.Granted(f.Request(101, "a"));
        f.Granted(f.Request(101, "a")).LeaseId.ShouldBe(a.LeaseId);
        f.Busy(f.Request(201, "silent"));
        f.Time.Advance(TimeSpan.FromSeconds(61));
        f.Broker.Sweep().ShouldBe(0); // drops the silent waiter, reaps nothing
        f.Broker.Release(a.LeaseId!.Value).ShouldBeTrue();
        f.Broker.Release(a.LeaseId!.Value).ShouldBeFalse();
        f.Granted(f.Request(102, "dies"));
        f.Time.Advance(TimeSpan.FromSeconds(20));
        f.Liveness.Kill(102);
        f.Broker.Sweep().ShouldBe(1);

        var m = f.Broker.Metrics();

        (m.Grants, m.Releases, m.Reaps, m.WaiterDrops, m.BusyRefusals).ShouldBe((2, 1, 1, 1, 1));
        m.AcquireWait.ShouldBe(new BuildSlotDurationStats(2, 0, 0, 0));
        m.Held.ShouldBe(new BuildSlotDurationStats(2, 40.5, 61, 61));
    }

    [Test]
    public void Records_older_than_three_hours_leave_the_window()
    {
        var f = new BuildSlotBrokerTests.Fixture(maxConcurrent: 1);
        var old = f.Granted(f.Request(101, "old"));
        f.Busy(f.Request(201, "old-waiter"));
        f.Time.Advance(TimeSpan.FromSeconds(10));
        f.Broker.Release(old.LeaseId!.Value);
        f.Time.Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(2));
        var recent = f.Granted(f.Request(102, "recent")); // its sweep drops the silent old waiter
        f.Time.Advance(TimeSpan.FromSeconds(5));
        f.Broker.Release(recent.LeaseId!.Value);

        var m = f.Broker.Metrics();

        m.NowUtc.ShouldBe(T0.AddHours(3).AddMinutes(2).AddSeconds(15));
        m.WindowStartUtc.ShouldBe(m.NowUtc.AddHours(-3));
        (m.WindowSeconds, m.PartialWindow, m.Truncated).ShouldBe((10_800d, false, false));
        (m.Grants, m.Releases, m.Reaps, m.WaiterDrops, m.BusyRefusals).ShouldBe((1, 1, 0, 1, 0));
        m.Held.ShouldBe(new BuildSlotDurationStats(1, 5, 5, 5));
        m.ReleasesPerHour.ShouldBe(1 / 3d);
        // The out-of-window grant and release were pruned when the newer records arrived.
        m.RetainedRecords.ShouldBe(3);
    }

    [Test]
    public void Capacity_drops_oldest_records_and_discloses_truncation()
    {
        var f = new BuildSlotBrokerTests.Fixture(maxConcurrent: 1);
        for (var i = 0; i < 2100; i++)
        {
            f.Broker.Release(f.Granted(f.Request(101, "cycle")).LeaseId!.Value).ShouldBeTrue();
            f.Time.Advance(TimeSpan.FromSeconds(1));
        }

        var m = f.Broker.Metrics();

        (m.RecordCapacity, m.RetainedRecords, m.DroppedRecords).ShouldBe((BuildSlotMetricsRecorder.Capacity, 4096, 104L));
        m.Truncated.ShouldBeTrue();
        // 4200 records, 104 dropped: the window starts at the oldest retained one, cycle 52's grant.
        m.WindowStartUtc.ShouldBe(T0.AddSeconds(52));
        (m.Grants, m.Releases).ShouldBe((2048, 2048));
        m.PartialWindow.ShouldBeTrue();
    }

    [Test]
    public void Empty_and_partial_windows_and_reads_do_not_mutate_admission()
    {
        var f = new BuildSlotBrokerTests.Fixture(maxConcurrent: 1, floorMb: 6144);
        var empty = f.Broker.Metrics();
        empty.ShouldBe(new BuildSlotMetrics(T0, T0, T0, 0, PartialWindow: true, Truncated: false, DroppedRecords: 0,
            RetainedRecords: 0, RecordCapacity: BuildSlotMetricsRecorder.Capacity, Grants: 0, Releases: 0, Reaps: 0,
            WaiterDrops: 0, BusyRefusals: 0, MemoryFloorRefusals: 0, AcquireWait: new BuildSlotDurationStats(0, null, null, null),
            Held: new BuildSlotDurationStats(0, null, null, null), ReleasesPerHour: null));

        f.Memory.AvailableBytes = 1024L * 1024 * 1024;
        f.Broker.TryAcquire(f.Request(101, "starved"), out var outcome).ShouldBeFalse();
        outcome.ShouldBeOfType<BuildSlotOutcome.MemoryFloor>();
        f.Memory.AvailableBytes = 64L * 1024 * 1024 * 1024;
        f.Granted(f.Request(101, "starved"));
        f.Liveness.Kill(101);
        f.Time.Advance(TimeSpan.FromHours(1));

        var first = f.Broker.Metrics();
        var second = f.Broker.Metrics();

        second.ShouldBe(first);
        (first.WindowSeconds, first.PartialWindow, first.Grants, first.MemoryFloorRefusals, first.Reaps).ShouldBe((3600d, true, 1, 1, 0));
        // A read is not a sweep: the dead holder keeps its lease until the broker itself sweeps.
        f.Broker.List().Leases.Select(l => l.Pid).ShouldBe([101]);
        f.Broker.Sweep().ShouldBe(1);
        f.Broker.Metrics().Reaps.ShouldBe(1);
    }

    private static BuildSlotRequest Request(int pid, string label) => new(pid, T0, label, "session-" + pid, "task-" + pid);
}
