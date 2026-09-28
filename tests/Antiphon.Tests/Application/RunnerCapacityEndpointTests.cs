using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class RunnerCapacityEndpointTests
{
    [Test]
    public async Task C654_PUT_pushes_capacity_and_GET_uses_the_budget_minimum()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await Host(schema);
        host.Capacity = 10;
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        peer.Reply = request => request.Operation == PhoneHomeOperation.SetCapacity
            ? Success(request)
            : null;

        using var response = await host.Http.PutAsJsonAsync(Path(host), new { capacity = 6, reason = "scale down" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.Directory.DeclaredCapacity(host.AllowedRunnerId).ShouldBe(6);
        (await peer.WaitForAsync(PhoneHomeOperation.SetCapacity)).Payload!.Value
            .GetProperty("capacity").GetInt32().ShouldBe(6);
        await using var scope = host.App.Services.CreateAsyncScope();
        var budgets = scope.ServiceProvider.GetRequiredService<HostBudgetService>();
        await budgets.UpsertAsync(host.AllowedRunnerId, 3, "reserve", CancellationToken.None);
        using var read = await host.Http.GetAsync(Path(host));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("effectiveLimit").GetInt32().ShouldBe(3);
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().AgentIncidents.CountAsync(i =>
            i.Kind == AgentIncidentKind.RunnerCapacityChanged)).ShouldBe(1);
    }

    [Test]
    public async Task C654_PUT_above_server_bound_sends_no_frame()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await Host(schema);
        host.Capacity = 10;
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        using var response = await host.Http.PutAsJsonAsync(Path(host), new { capacity = 11, reason = "too high" });
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        peer.RequestCount(PhoneHomeOperation.SetCapacity).ShouldBe(0);
    }

    [Test]
    public async Task C654_PUT_uses_runner_entry_bound_when_top_level_is_higher()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await PhoneHomeTestHost.StartAsync(
            connectionString: schema.ConnectionString,
            configureServices: services =>
            {
                services.AddSingleton<ISessionRunnerDirectory>(sp => sp.GetRequiredService<Antiphon.Server.Infrastructure.Agents.SessionRunner.PhoneHomeRunnerDirectory>());
                services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 }));
                services.AddScoped<HostBudgetService>();
            },
            configureRunnerSettings: settings =>
            {
                settings.MaxCapacity = 10;
                settings.Runners["grok-linux"] = PhoneHomeRunnerCatalog.FromLegacy(settings);
                settings.Runners["grok-linux"].MaxCapacity = 2;
            });
        host.Capacity = 2;
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        using var response = await host.Http.PutAsJsonAsync(Path(host), new { capacity = 3, reason = "above entry" });
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        peer.RequestCount(PhoneHomeOperation.SetCapacity).ShouldBe(0);
        host.Directory.DeclaredCapacity(host.AllowedRunnerId).ShouldBe(2);
        await peer.EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Heartbeat, peer.Epoch, Guid.NewGuid(),
            Payload: JsonSerializer.SerializeToElement(new PhoneHomeCapacityHeartbeat(3), PhoneHomeFraming.Json)));
        await Task.Delay(50);
        host.Directory.DeclaredCapacity(host.AllowedRunnerId).ShouldBe(2);
    }

    [Test]
    public async Task C654_PUT_offline_is_a_409_and_does_not_change_capacity()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await Host(schema);
        using var response = await host.Http.PutAsJsonAsync(Path(host), new { capacity = 2, reason = "offline" });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain(PhoneHomeProblemTypes.Unavailable);
        host.Directory.DeclaredCapacity(host.AllowedRunnerId).ShouldBeNull();
    }

    [Test]
    public async Task C654_PUT_unsupported_keeps_the_live_capacity()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await Host(schema);
        host.Capacity = 10;
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        peer.Reply = request => request.Operation == PhoneHomeOperation.SetCapacity
            ? new PhoneHomeFrame(PhoneHomeFrameKind.Error, request.Epoch, request.RequestId,
                request.Operation, ErrorCode: PhoneHomeProblemTypes.UnsupportedOperation,
                ErrorDetail: "old binary", StatusCode: 400)
            : null;
        using var response = await host.Http.PutAsJsonAsync(Path(host), new { capacity = 6, reason = "scale" });
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain(PhoneHomeProblemTypes.UnsupportedOperation);
        host.Directory.DeclaredCapacity(host.AllowedRunnerId).ShouldBe(10);
    }

    [Test]
    public async Task C654_PUT_silent_peer_times_out_without_changing_capacity()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var host = await Host(schema, clock);
        host.Capacity = 10;
        await using var peer = await host.ConnectPeerAsync();
        var live = await host.WaitLiveAsync();
        host.Directory.MarkRecovered(live);
        peer.SilentFor(PhoneHomeOperation.SetCapacity);
        var pending = host.Http.PutAsJsonAsync(Path(host), new { capacity = 6, reason = "scale" });
        await peer.WaitForAsync(PhoneHomeOperation.SetCapacity);
        for (var minute = 0; minute < 7; minute++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await peer.EmitAsync(new PhoneHomeFrame(
                PhoneHomeFrameKind.Heartbeat, peer.Epoch, Guid.NewGuid(),
                Payload: JsonSerializer.SerializeToElement(new PhoneHomeCapacityHeartbeat(10), PhoneHomeFraming.Json)));
            var heartbeatDeadline = DateTime.UtcNow.AddSeconds(3);
            while (live.LastHeartbeatUtc < clock.GetUtcNow() && DateTime.UtcNow < heartbeatDeadline)
                await Task.Delay(20);
        }
        using var response = await pending;
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain(PhoneHomeProblemTypes.RequestTimeout);
        host.Directory.DeclaredCapacity(host.AllowedRunnerId).ShouldBe(10);
        await peer.EmitAsync(new PhoneHomeFrame(
            PhoneHomeFrameKind.Heartbeat, peer.Epoch, Guid.NewGuid(),
            Payload: JsonSerializer.SerializeToElement(new PhoneHomeCapacityHeartbeat(6), PhoneHomeFraming.Json)));
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (host.Directory.DeclaredCapacity(host.AllowedRunnerId) != 6 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        host.Directory.DeclaredCapacity(host.AllowedRunnerId).ShouldBe(6,
            "the runner's applied value catches up even after the request timed out");
    }

    [Test]
    public async Task C654_PUT_waits_for_a_late_persisted_confirmation()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await Host(schema);
        host.Capacity = 10;
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());
        peer.SilentFor(PhoneHomeOperation.SetCapacity);
        var pushing = host.Http.PutAsJsonAsync(Path(host), new { capacity = 6, reason = "busy runner" });
        var request = await peer.WaitForAsync(PhoneHomeOperation.SetCapacity);
        await Task.Delay(TimeSpan.FromSeconds(3.2));
        await peer.EmitAsync(Success(request));
        using var response = await pushing;
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.Directory.DeclaredCapacity(host.AllowedRunnerId).ShouldBe(6);
    }

    private static async Task<PhoneHomeTestHost> Host(IsolatedTestSchema schema, TimeProvider? clock = null) =>
        await PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString, clock: clock,
            configureServices: services =>
            {
                services.AddSingleton<ISessionRunnerDirectory>(sp => sp.GetRequiredService<Antiphon.Server.Infrastructure.Agents.SessionRunner.PhoneHomeRunnerDirectory>());
                services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 }));
                services.AddScoped<HostBudgetService>();
            });

    private static string Path(PhoneHomeTestHost host) => $"/api/session-runners/{host.AllowedRunnerId}/capacity";

    private static PhoneHomeFrame Success(PhoneHomeFrame request)
    {
        var body = request.Payload!.Value.Deserialize<PhoneHomeSetCapacityRequest>(PhoneHomeFraming.Json)!;
        return new PhoneHomeFrame(PhoneHomeFrameKind.Result, request.Epoch, request.RequestId,
            request.Operation, JsonSerializer.SerializeToElement(
                new PhoneHomeSetCapacityResponse(body.Capacity, true, "/state/runner-capacity"), PhoneHomeFraming.Json));
    }
}
