using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.HostLoad;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1744 D-1, beside W-7. With DirectRunnerKey=server2-direct, GET /api/session-runners
/// and /api/session-runners/desktop/status return the shadow stored at server2-direct.
/// </summary>
[Category("Integration")]
public sealed class C1744G2SessionRunnerLoadTests
{
    [Test]
    public async Task Endpoint_desktop_row_reads_the_direct_key_shadow()
    {
        await using var host = await PhoneHomeTestHost.StartAsync(configureServices: services =>
        {
            services.AddSingleton(Options.Create(new HostLoadHistorySettings { DirectRunnerKey = "server2-direct" }));
            services.AddSingleton<HostLoadRunnerKeys>();
            services.AddSingleton<HostLoadShadowCache>();
        });
        var keys = host.App.Services.GetRequiredService<HostLoadRunnerKeys>();
        keys.Canonical("desktop").ShouldBe("server2-direct");
        var shadow = host.App.Services.GetRequiredService<HostLoadShadowCache>();
        shadow.Set("desktop", Advice(1));
        shadow.Set("server2-direct", Advice(2));

        var catalogue = await host.Http.GetAsync("/api/session-runners");
        catalogue.EnsureSuccessStatusCode();
        var rows = await catalogue.Content.ReadFromJsonAsync<JsonElement>();
        var desktop = rows.EnumerateArray().Single(row => row.GetProperty("runnerId").GetString() == "desktop");
        desktop.GetProperty("load").GetProperty("recommendation").GetProperty("recommendedAdditionalTasks")
            .GetInt32().ShouldBe(2);

        var status = await host.Http.GetAsync("/api/session-runners/desktop/status");
        status.EnsureSuccessStatusCode();
        var body = await status.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("load").GetProperty("recommendedAdditionalTasks").GetInt32().ShouldBe(2);
    }

    private static HostLoadAdvice Advice(int recommended) => new(
        new HostLoadShadowDecision(null, null, "unknown", "unknown", [], []),
        new HostLoadRecommendationDto(recommended, "fleet", [], null, 0, 0, null, null, null, null, null, []));
}
