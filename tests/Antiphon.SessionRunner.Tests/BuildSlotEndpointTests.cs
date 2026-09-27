using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0589 V-2 (S2, D-3). The runner's <c>/build-slots</c> routes over real loopback HTTP: the
/// grant body, the 409 problem answers a waiting wrapper parses, release and the occupancy listing.
/// </summary>
[Category("Integration")]
public sealed class BuildSlotEndpointTests
{
    private static readonly DateTime T0 = new(2026, 9, 25, 6, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Post_grants_a_lease_with_its_cpu_count_occupancy_and_expiry()
    {
        await using var host = await StartAsync(maxConcurrent: 2, maxCpuCount: 5);

        using var response = await host.Http.PostAsJsonAsync("build-slots", Request(101, "cp-2"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Guid.TryParse(root.GetProperty("leaseId").GetString(), out _).ShouldBeTrue(root.ToString());
        root.GetProperty("maxCpuCount").GetInt32().ShouldBe(5);
        root.GetProperty("occupied").GetInt32().ShouldBe(1);
        root.GetProperty("budget").GetInt32().ShouldBe(2);
        root.GetProperty("unlimited").GetBoolean().ShouldBeFalse();
        root.GetProperty("expiresAtUtc").GetDateTime().ToUniversalTime().ShouldBe(T0.AddMinutes(90));
    }

    [Test]
    public async Task Post_answers_409_problems_a_wrapper_can_parse_and_400_for_an_invalid_body()
    {
        await using var host = await StartAsync(maxConcurrent: 1, maxCpuCount: 4, retryAfterMs: 2500);
        (await host.Http.PostAsJsonAsync("build-slots", Request(101, "holder"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var busy = await host.Http.PostAsJsonAsync("build-slots", Request(102, "waiter"));
        busy.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        busy.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using (var doc = JsonDocument.Parse(await busy.Content.ReadAsStringAsync()))
        {
            var p = doc.RootElement;
            p.GetProperty("type").GetString().ShouldBe(BuildSlotProblemTypes.Busy);
            p.GetProperty("occupied").GetInt32().ShouldBe(1);
            p.GetProperty("budget").GetInt32().ShouldBe(1);
            p.GetProperty("queuePosition").GetInt32().ShouldBe(1);
            p.GetProperty("retryAfterMs").GetInt32().ShouldBe(2500);
        }

        await using var floorHost = await StartAsync(maxConcurrent: 1, maxCpuCount: 4, floorMb: 8192, availableMb: 1024);
        using var floor = await floorHost.Http.PostAsJsonAsync("build-slots", Request(103, "starved"));
        floor.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var doc = JsonDocument.Parse(await floor.Content.ReadAsStringAsync()))
        {
            var p = doc.RootElement;
            p.GetProperty("type").GetString().ShouldBe(BuildSlotProblemTypes.MemoryFloor);
            p.GetProperty("availableMb").GetInt64().ShouldBe(1024);
            p.GetProperty("floorMb").GetInt64().ShouldBe(8192);
            p.GetProperty("retryAfterMs").GetInt32().ShouldBeGreaterThan(0);
        }

        using var invalid = await host.Http.PostAsJsonAsync("build-slots", new BuildSlotRequest(0, T0, " "));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using (var doc = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()))
            doc.RootElement.GetProperty("type").GetString().ShouldBe(BuildSlotProblemTypes.Invalid);
    }

    [Test]
    public async Task Delete_releases_once_then_answers_404_unknown()
    {
        await using var host = await StartAsync(maxConcurrent: 1, maxCpuCount: 4);
        var grant = await (await host.Http.PostAsJsonAsync("build-slots", Request(101, "a")))
            .Content.ReadFromJsonAsync<BuildSlotGrant>();

        using var first = await host.Http.DeleteAsync($"build-slots/{grant!.LeaseId}");
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        host.Broker.List().Occupied.ShouldBe(0);

        using var second = await host.Http.DeleteAsync($"build-slots/{grant.LeaseId}");
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("type").GetString().ShouldBe(BuildSlotProblemTypes.Unknown);
    }

    [Test]
    public async Task Get_lists_budget_occupancy_memory_leases_and_waiters()
    {
        await using var host = await StartAsync(maxConcurrent: 1, maxCpuCount: 6, floorMb: 512, availableMb: 4096);
        await host.Http.PostAsJsonAsync("build-slots", Request(101, "holder"));
        await host.Http.PostAsJsonAsync("build-slots", Request(102, "waiter"));

        var listing = await host.Http.GetFromJsonAsync<BuildSlotListing>("build-slots");

        listing.ShouldNotBeNull();
        (listing.Enabled, listing.Budget, listing.MaxCpuCount, listing.Occupied).ShouldBe((true, 1, 6, 1));
        listing.Memory.ShouldBe(new BuildSlotMemory(AvailableMb: 4096, FloorMb: 512));
        var lease = listing.Leases.Single();
        (lease.Pid, lease.Label, lease.SessionId, lease.TaskId, lease.HolderAlive).ShouldBe((101, "holder", "session-101", "task-101", true));
        listing.Waiters.Select(w => (w.Pid, w.Label, w.Position)).ShouldBe([(102, "waiter", 1)]);
    }

    [Test]
    public async Task Post_renew_answers_204_for_a_held_lease_and_404_otherwise()
    {
        await using var host = await StartAsync(maxConcurrent: 1, maxCpuCount: 4);
        var grant = await (await host.Http.PostAsJsonAsync("build-slots", Request(101, "renew")))
            .Content.ReadFromJsonAsync<BuildSlotGrant>();
        (await host.Http.PostAsync($"build-slots/{grant!.LeaseId}/renew", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await host.Http.PostAsync($"build-slots/{Guid.NewGuid()}/renew", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    [Timeout(60_000)]
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task Build_slots_only_host_serves_health_and_build_slots_and_nothing_else()
    {
        var runnerDll = Path.Combine(AppContext.BaseDirectory, "Antiphon.SessionRunner.dll");
        var logs = Path.Combine(Path.GetTempPath(), "c727-broker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logs);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = logs,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { runnerDll, "--urls", "http://127.0.0.1:0", "--SessionRunner:BuildSlotsOnly", "true",
                     "--SessionRunner:SessionLogPath", logs, "--Serilog:LogPath", logs })
            start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        var listening = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Capture(string? line)
        {
            if (line is null) return;
            output.Enqueue(line);
            var match = Regex.Match(line, @"Now listening on:\s+(http://127\.0\.0\.1:\d+)");
            if (match.Success) listening.TrySetResult(new Uri(match.Groups[1].Value));
        }
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);
        try
        {
            process.Start().ShouldBeTrue();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            Uri address;
            try { address = await listening.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException ex) { throw new TimeoutException(string.Join("\n", output.TakeLast(20)), ex); }
            address.Port.ShouldNotBe(17204);
            using var http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
            (await http.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await http.GetAsync("/build-slots")).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await http.GetAsync("/sessions")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await http.GetAsync("/capabilities")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            Directory.Delete(logs, recursive: true);
        }
    }

    private static BuildSlotRequest Request(int pid, string label) => new(pid, T0, label, "session-" + pid, "task-" + pid);

    private static Task<BuildSlotTestHost> StartAsync(int maxConcurrent, int maxCpuCount, int floorMb = 0,
        long availableMb = 64 * 1024, int retryAfterMs = 15_000)
    {
        var liveness = new BuildSlotBrokerTests.FakeLiveness();
        foreach (var pid in new[] { 101, 102, 103 }) liveness.Start(pid, T0);
        return BuildSlotTestHost.StartAsync(
            new()
            {
                ["SessionRunner:BuildSlots:MaxConcurrent"] = maxConcurrent.ToString(),
                ["SessionRunner:BuildSlots:MaxCpuCount"] = maxCpuCount.ToString(),
                ["SessionRunner:BuildSlots:MinAvailableMemoryMb"] = floorMb.ToString(),
                ["SessionRunner:BuildSlots:RetryAfterMs"] = retryAfterMs.ToString(),
            },
            liveness,
            new BuildSlotBrokerTests.FakeMemory { AvailableBytes = availableMb * 1024 * 1024 },
            new FakeTimeProvider(T0));
    }
}
