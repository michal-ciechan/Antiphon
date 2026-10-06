using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1079 V-21 to V-23. The route only reads seeded samples.</summary>
[Category("Integration")]
public sealed class HostOccupancySampleEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task C1079_Samples_return_newest_first_within_the_window()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(Now);
        await using var host = await StartAsync(schema, clock);
        var newest = Utc(11, 50);
        var middle = Utc(11, 40);
        var outside = Utc(10, 0);
        var idleSince = Utc(9, 0);
        await SeedAsync(host,
            Sample("local", newest, idleSince, reason: "listed ok"),
            Sample("local", middle, idleSince, reason: "listed ok"),
            Sample("local", outside, idleSince, reason: "too old for the query"),
            Sample("grok-linux", newest, idleSince, reason: "other host"));

        var from = Uri.EscapeDataString(middle.ToString("o"));
        var to = Uri.EscapeDataString(newest.ToString("o"));
        using var response = await host.Http.GetAsync($"/api/hosts/local/occupancy-samples?from={from}&to={to}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("limit").GetInt32().ShouldBe(500);
        var samples = json.RootElement.GetProperty("samples").EnumerateArray().ToList();
        samples.Count.ShouldBe(2);
        Instant(samples[0], "sampledAt").ShouldBe(newest);
        Instant(samples[1], "sampledAt").ShouldBe(middle);
        AssertPopulated(samples[0], "local", newest, idleSince, "listed ok");
        AssertPopulated(samples[1], "local", middle, idleSince, "listed ok");

        using var posted = await host.Http.PostAsJsonAsync("/api/hosts/local/occupancy-samples", new { });
        posted.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task C1079_Unknown_host_is_404()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(Now);
        await using var host = await StartAsync(schema, clock);
        await SeedAsync(host, Sample("nope", Utc(11, 50), Utc(9, 0), reason: "not a host"));

        using var response = await host.Http.GetAsync("/api/hosts/nope/occupancy-samples");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("not_found");
    }

    [Test]
    public async Task C1079_Limit_caps_rows_and_default_window_is_24_hours()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(Now);
        await using var host = await StartAsync(schema, clock);
        var idleSince = Utc(8, 0);
        var recent = new[]
        {
            Utc(11, 0), Utc(11, 10), Utc(11, 20), Utc(11, 30), Utc(11, 40),
        };
        var stale = Now.UtcDateTime.AddHours(-25);
        await SeedAsync(host, recent.Select(at => Sample("local", at, idleSince, reason: "in window"))
            .Append(Sample("local", stale, idleSince, reason: "outside default window")).ToArray());

        using var capped = await host.Http.GetAsync("/api/hosts/local/occupancy-samples?limit=2");
        capped.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var cappedJson = JsonDocument.Parse(await capped.Content.ReadAsStringAsync());
        cappedJson.RootElement.GetProperty("limit").GetInt32().ShouldBe(2);
        Instant(cappedJson.RootElement, "to").ShouldBe(Now.UtcDateTime);
        Instant(cappedJson.RootElement, "from").ShouldBe(Now.UtcDateTime.AddHours(-24));
        var cappedRows = cappedJson.RootElement.GetProperty("samples").EnumerateArray().ToList();
        cappedRows.Count.ShouldBe(2);
        Instant(cappedRows[0], "sampledAt").ShouldBe(Utc(11, 40));
        Instant(cappedRows[1], "sampledAt").ShouldBe(Utc(11, 30));

        using var clamped = await host.Http.GetAsync("/api/hosts/local/occupancy-samples?limit=5000");
        clamped.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var clampedJson = JsonDocument.Parse(await clamped.Content.ReadAsStringAsync());
        clampedJson.RootElement.GetProperty("limit").GetInt32().ShouldBe(2000);
        var clampedRows = clampedJson.RootElement.GetProperty("samples").EnumerateArray().ToList();
        clampedRows.Count.ShouldBe(5);
        clampedRows.ShouldNotContain(row => Instant(row, "sampledAt") == stale);
    }

    [Test]
    public async Task C1101_Limit_below_one_is_400_and_the_cap_and_default_apply()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(Now);
        await using var host = await StartAsync(schema, clock);

        using var omitted = await host.Http.GetAsync("/api/hosts/local/occupancy-samples");
        omitted.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var omittedJson = JsonDocument.Parse(await omitted.Content.ReadAsStringAsync());
        omittedJson.RootElement.GetProperty("limit").GetInt32().ShouldBe(500);

        using var zero = await host.Http.GetAsync("/api/hosts/local/occupancy-samples?limit=0");
        zero.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await zero.Content.ReadAsStringAsync()).ShouldContain("limit_invalid");

        using var negative = await host.Http.GetAsync("/api/hosts/local/occupancy-samples?limit=-1");
        negative.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await negative.Content.ReadAsStringAsync()).ShouldContain("limit_invalid");

        using var clamped = await host.Http.GetAsync("/api/hosts/local/occupancy-samples?limit=2001");
        clamped.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var clampedJson = JsonDocument.Parse(await clamped.Content.ReadAsStringAsync());
        clampedJson.RootElement.GetProperty("limit").GetInt32().ShouldBe(2000);
    }

    [Test]
    public async Task C1101_To_near_minimum_without_from_is_400_window_invalid()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(Now);
        await using var host = await StartAsync(schema, clock);

        foreach (var to in new[] { "0001-01-01T00:00:00.0000000Z", "0001-01-01T23:00:00.0000000Z" })
        {
            using var response = await host.Http.GetAsync(
                "/api/hosts/local/occupancy-samples?to=" + Uri.EscapeDataString(to));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldContain("window_invalid");
            body.ShouldNotContain("An unexpected error occurred.");
        }

        const string edge = "0001-01-02T00:00:00.0000000Z";
        using var ok = await host.Http.GetAsync(
            "/api/hosts/local/occupancy-samples?to=" + Uri.EscapeDataString(edge));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("from").GetDateTime().ShouldBe(DateTime.MinValue);
        json.RootElement.GetProperty("to").GetDateTime().ShouldBe(
            DateTime.Parse(edge, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
    }

    private static Task<PhoneHomeTestHost> StartAsync(IsolatedTestSchema schema, FakeTimeProvider clock) =>
        PhoneHomeTestHost.StartAsync(clock: clock, connectionString: schema.ConnectionString,
            configureServices: services =>
            {
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(TimeProvider)).ToList())
                    services.Remove(descriptor);
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<ISessionRunnerDirectory>(sp => sp.GetRequiredService<PhoneHomeRunnerDirectory>());
                services.AddSingleton<IOptions<DelegationSettings>>(
                    Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 }));
                services.AddScoped<HostBudgetService>();
            }, mapEndpoints: app => app.MapHostEndpoints());

    private static async Task SeedAsync(PhoneHomeTestHost host, params HostOccupancySample[] rows)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.HostOccupancySamples.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static HostOccupancySample Sample(string hostId, DateTime sampledAt, DateTime idleSince, string reason) =>
        new()
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            SampledAt = sampledAt,
            InventoryState = "listed",
            InventoryReason = reason,
            InFlight = 4,
            DispatchedWorking = 1,
            Sessions = 3,
            PendingLaunch = 1,
            InFlightMirrors = 0,
            IdleSeats = 2,
            PooledWarmSeats = 1,
            OrphanSlots = 2,
            EffectiveLimit = 10,
            DeclaredCapacity = 6,
            OldestIdleSince = idleSince,
        };

    private static void AssertPopulated(JsonElement row, string hostId, DateTime sampledAt, DateTime idleSince, string reason)
    {
        row.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        row.GetProperty("hostId").GetString().ShouldBe(hostId);
        Instant(row, "sampledAt").ShouldBe(sampledAt);
        row.GetProperty("inventoryState").GetString().ShouldBe("listed");
        row.GetProperty("inventoryReason").GetString().ShouldBe(reason);
        row.GetProperty("inFlight").GetInt32().ShouldBe(4);
        row.GetProperty("dispatchedWorking").GetInt32().ShouldBe(1);
        row.GetProperty("sessions").GetInt32().ShouldBe(3);
        row.GetProperty("pendingLaunch").GetInt32().ShouldBe(1);
        row.GetProperty("inFlightMirrors").GetInt32().ShouldBe(0);
        row.GetProperty("idleSeats").GetInt32().ShouldBe(2);
        row.GetProperty("pooledWarmSeats").GetInt32().ShouldBe(1);
        row.GetProperty("orphanSlots").GetInt32().ShouldBe(2);
        row.GetProperty("effectiveLimit").GetInt32().ShouldBe(10);
        row.GetProperty("declaredCapacity").GetInt32().ShouldBe(6);
        Instant(row, "oldestIdleSince").ShouldBe(idleSince);
    }

    private static DateTime Instant(JsonElement row, string name) =>
        row.GetProperty(name).GetDateTime().ToUniversalTime();

    private static DateTime Utc(int hour, int minute) =>
        new(2026, 10, 6, hour, minute, 0, DateTimeKind.Utc);
}
