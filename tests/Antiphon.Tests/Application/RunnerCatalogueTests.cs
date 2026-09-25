using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0710 V-9. GET /api/session-runners lists the desktop and every configured runner,
/// including one that is offline, and never borrows another runner's status.
/// </summary>
[Category("Integration")]
public sealed class RunnerCatalogueTests
{
    [Test]
    public async Task Catalogue_includes_desktop_and_offline_runners()
    {
        var secretA = "secret-a-" + Guid.NewGuid().ToString("N");
        await using var host = await PhoneHomeTestHost.StartAsync(configured: Settings(secretA));
        host.Local.Capabilities = new RunnerCapabilitiesDto(
            "InboxConhost", "inbox", "test", false,
            Features: [RunnerPlatformWire.Feature], Platform: "windows");

        using var response = await host.Http.GetAsync("/api/session-runners");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = doc.RootElement.EnumerateArray().Select(row => row.GetProperty("runnerId").GetString()).ToArray();
        ids.ShouldContain("desktop");
        ids.ShouldContain("runner-a");
        ids.ShouldContain("runner-b");
        var offline = doc.RootElement.EnumerateArray().Single(row => row.GetProperty("runnerId").GetString() == "runner-b");
        offline.GetProperty("available").GetBoolean().ShouldBeFalse();
        offline.GetProperty("dispatchEligible").GetBoolean().ShouldBeFalse();
        var desktop = doc.RootElement.EnumerateArray().Single(row => row.GetProperty("runnerId").GetString() == "desktop");
        desktop.GetProperty("platform").GetString().ShouldBe("windows");
        desktop.GetProperty("capacityKind").GetString().ShouldBe("delegatedTasks");
    }

    [Test]
    public async Task Unknown_id_has_no_borrowed_status()
    {
        await using var host = await PhoneHomeTestHost.StartAsync();
        await using var peer = await host.ConnectPeerAsync();
        host.Directory.MarkRecovered(await host.WaitLiveAsync());

        using var missing = await host.Http.GetAsync($"/api/session-runners/{host.AllowedRunnerId}/../other/status");
        using var status = await host.Http.GetAsync("/api/session-runners/other-runner/status");
        status.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var own = await host.Http.GetFromJsonAsync<PhoneHomeRunnerStatusDto>(
            $"/api/session-runners/{host.AllowedRunnerId}/status", PhoneHomeFraming.Json);
        own.ShouldNotBeNull();
        own.RunnerStoreId.ShouldBe(host.StoreId);
        missing.StatusCode.ShouldNotBe(HttpStatusCode.OK);
        peer.Epoch.ShouldBeGreaterThan(0);
    }

    private static PhoneHomeRunnerSettings Settings(string secretA) => new()
    {
        Enabled = true,
        Runners = new Dictionary<string, PhoneHomeRunnerEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["runner-a"] = new()
            {
                Enabled = true,
                DisplayName = "Runner A",
                AllowDelegatedTasks = true,
                HostWorkspaceRoot = @"C:\work",
                RunnerWorkspace = "/work/a",
                RunnerRepository = "/work/repos/antiphon",
                CallbackOrigin = "https://antiphon.test",
                SharedSecret = secretA,
                MaxCapacity = 3,
            },
            ["runner-b"] = new()
            {
                Enabled = true,
                DisplayName = "Runner B",
                AllowDelegatedTasks = true,
                HostWorkspaceRoot = @"C:\work",
                RunnerWorkspace = "/work/b",
                RunnerRepository = "/work/repos/antiphon",
                CallbackOrigin = "https://antiphon.test",
                SharedSecret = "secret-b",
                MaxCapacity = 3,
            },
        },
    };
}
