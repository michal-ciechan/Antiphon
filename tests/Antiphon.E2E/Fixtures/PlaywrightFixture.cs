using Microsoft.Playwright;
using Xunit;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// Manages the Playwright browser lifecycle for E2E tests.
/// Creates a single chromium headless browser instance shared across test methods,
/// with a fresh BrowserContext per test for isolation.
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    /// <summary>
    /// The shared browser instance (chromium headless).
    /// </summary>
    public IBrowser Browser => _browser;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    /// <summary>
    /// Creates a new isolated browser context for a single test.
    /// Each test should call this and dispose the context when done.
    /// </summary>
    public async Task<IBrowserContext> NewContextAsync()
    {
        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });
    }

    /// <summary>
    /// Creates a new page in a fresh browser context.
    /// Caller is responsible for disposing both the page and context.
    /// </summary>
    public async Task<(IPage Page, IBrowserContext Context)> NewPageAsync()
    {
        var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        return (page, context);
    }
}
