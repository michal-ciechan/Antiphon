using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class HostEndpointTests
{
    [Test]
    public async Task C654_Unknown_host_is_404()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await Host(schema);
        using var response = await host.Http.PutAsJsonAsync("/api/hosts/missing/budget",
            new { maxInFlight = 2, reason = "unknown" });
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("not_found");
    }

    [Test]
    public async Task C654_Budget_write_requires_a_reason()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await Host(schema);
        using var response = await host.Http.PutAsJsonAsync("/api/hosts/local/budget",
            new { maxInFlight = 2, reason = " " });
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task C654_Budget_round_trip_has_revision_and_effective_limit()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await Host(schema);
        using var written = await host.Http.PutAsJsonAsync("/api/hosts/local/budget",
            new { maxInFlight = 1, reason = "leave a seat" });
        written.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var read = await host.Http.GetAsync("/api/hosts");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var local = json.RootElement.EnumerateArray().Single(row =>
            row.GetProperty("hostId").GetString() == "local");
        local.GetProperty("effectiveLimit").GetInt32().ShouldBe(1);
        local.GetProperty("revision").GetInt32().ShouldBe(1);
        local.GetProperty("reason").GetString().ShouldBe("leave a seat");
    }

    [Test]
    public async Task C654_Hosts_and_pipeline_agree_on_the_local_limit()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var host = await Host(schema);
        using var written = await host.Http.PutAsJsonAsync("/api/hosts/local/budget",
            new { maxInFlight = 0, reason = "drain" });
        written.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var read = await host.Http.GetAsync("/api/hosts");
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var effective = json.RootElement.EnumerateArray().Single(row =>
            row.GetProperty("hostId").GetString() == "local").GetProperty("effectiveLimit").GetInt32();
        await using var scope = host.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var options = Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 });
        var pipeline = new AgentTaskPipelineStatusService(db, options,
            new AreaMapLoader(options, NullLogger<AreaMapLoader>.Instance), TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<HostBudgetService>());
        (await pipeline.GetAsync(CancellationToken.None)).MaxConcurrentTasks.ShouldBe(effective);
    }

    private static Task<PhoneHomeTestHost> Host(IsolatedTestSchema schema) =>
        PhoneHomeTestHost.StartAsync(connectionString: schema.ConnectionString,
            configureServices: services =>
            {
                services.AddSingleton<ISessionRunnerDirectory>(sp => sp.GetRequiredService<PhoneHomeRunnerDirectory>());
                services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings { MaxConcurrentTasks = 4 }));
                services.AddScoped<HostBudgetService>();
            }, mapEndpoints: app => app.MapHostEndpoints());
}
