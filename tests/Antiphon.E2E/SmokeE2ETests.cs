using System.Net;
using FluentAssertions;
using Xunit;
using Antiphon.E2E.Fixtures;

namespace Antiphon.E2E;

/// <summary>
/// Smoke E2E test that verifies the full stack: browser -> React -> API -> DB.
/// Uses AntiphonAppFixture for the backend and PlaywrightFixture for the browser.
/// </summary>
public class SmokeE2ETests : IAsyncLifetime
{
    private readonly AntiphonAppFixture _appFixture = new();
    private readonly PlaywrightFixture _playwrightFixture = new();

    public async Task InitializeAsync()
    {
        _appFixture.UsePrebuiltFrontend = true;
        await _appFixture.InitializeAsync();
        await _playwrightFixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _playwrightFixture.DisposeAsync();
        await _appFixture.DisposeAsync();
    }

    [Fact]
    public async Task Health_endpoint_returns_ok_via_http_client()
    {
        // Verify the API layer works through WebApplicationFactory
        var response = await _appFixture.HttpClient.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(Skip = "Requires 'npm run build' to have been run and Playwright browsers installed. " +
                 "Run 'npx playwright install chromium' and 'cd client && npm run build' first.")]
    public async Task Full_stack_smoke_test_browser_to_api_to_db()
    {
        // This test verifies the full stack: Playwright browser -> React SPA -> API -> PostgreSQL
        var (page, context) = await _playwrightFixture.NewPageAsync();
        try
        {
            // Navigate to the app root served by WebApplicationFactory
            var response = await page.GotoAsync(_appFixture.BaseAddress);
            Assert.NotNull(response);
            response!.Status.Should().BeLessThan(500);

            // The SPA should load (check for root element or any page content)
            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.DOMContentLoaded);

            // Verify API connectivity from the browser context
            var healthResponse = await page.APIRequest.GetAsync($"{_appFixture.BaseAddress}/health");
            healthResponse.Status.Should().Be(200);
        }
        finally
        {
            await context.DisposeAsync();
        }
    }
}
