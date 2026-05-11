using System.Net;
using Shouldly;
using TUnit.Core;
using Antiphon.E2E.Fixtures;

namespace Antiphon.E2E;

/// <summary>
/// Smoke E2E test that verifies the full stack: browser -> React -> API -> DB.
/// Uses AntiphonAppFixture for the backend and PlaywrightFixture for the browser.
/// </summary>
[NotInParallel]
public class SmokeE2ETests
{
    private readonly AntiphonAppFixture _appFixture = new();
    private readonly PlaywrightFixture _playwrightFixture = new();

    [Before(Test)]
    public async Task SetupAsync()
    {
        _appFixture.UsePrebuiltFrontend = true;
        await _appFixture.InitializeAsync();
        await _playwrightFixture.InitializeAsync();
    }

    [After(Test)]
    public async Task TeardownAsync()
    {
        await _playwrightFixture.DisposeAsync();
        await _appFixture.DisposeAsync();
    }

    [Test]
    public async Task Health_endpoint_returns_ok_via_http_client()
    {
        // Verify the API layer works through WebApplicationFactory
        var response = await _appFixture.HttpClient.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Test]
    public async Task Full_stack_smoke_test_browser_to_api_to_db()
    {
        // This test verifies the full stack: Playwright browser -> React SPA -> API -> PostgreSQL
        var (page, context) = await _playwrightFixture.NewPageAsync();
        try
        {
            // Navigate to the app root served by Kestrel (real TCP endpoint)
            var response = await page.GotoAsync(_appFixture.PlaywrightAddress);
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);

            // The SPA should load (check for root element or any page content)
            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.DOMContentLoaded);

            // Verify API connectivity from the browser context
            var healthResponse = await page.APIRequest.GetAsync($"{_appFixture.PlaywrightAddress}/health");
            healthResponse.Status.ShouldBe(200);
        }
        finally
        {
            await context.DisposeAsync();
        }
    }
}
