using Antiphon.E2E.Fixtures;
using Microsoft.Playwright;
using Shouldly;
using TUnit.Core;

namespace Antiphon.E2E;

[Category("OptIn")]
[ParallelLimiter<ProcessSpawnLimit>]
public class ChromiumRuntimeSmokeTests
{
    [Test]
    [Timeout(60_000)]
    public async Task Chromium_launches_and_renders_local_content(CancellationToken cancellationToken)
    {
        var profile = Directory.CreateTempSubdirectory("antiphon-chromium-smoke-");
        try
        {
            using var playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken);
            IBrowserContext? context = null;
            try
            {
                context = await playwright.Chromium.LaunchPersistentContextAsync(profile.FullName,
                    new BrowserTypeLaunchPersistentContextOptions
                    {
                        Headless = true,
                        Timeout = 30_000
                    });
                var page = context.Pages.Single();
                await page.RouteAsync("**/*", route => route.AbortAsync());
                await page.SetContentAsync("""
                    <!doctype html><html><body>
                    <p id="status">pending</p>
                    <script>document.querySelector('#status').textContent = 'Chromium rendered local content';</script>
                    </body></html>
                    """, new PageSetContentOptions { Timeout = 5_000 });
                (await page.Locator("#status").InnerTextAsync(new LocatorInnerTextOptions { Timeout = 5_000 }))
                    .ShouldBe("Chromium rendered local content");
            }
            finally
            {
                if (context is not null)
                    await context.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
        finally
        {
            // Playwright disposal has stopped its driver and browser before deleting our profile.
            profile.Delete(recursive: true);
        }
    }
}
