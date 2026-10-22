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
    private static readonly DateTime T0 = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

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

    private static BuildSlotRequest Request(int pid, string label) => new(pid, T0, label, "session-" + pid, "task-" + pid);
}
