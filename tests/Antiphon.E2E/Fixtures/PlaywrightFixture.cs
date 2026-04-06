using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace Antiphon.E2E.Fixtures;

/// <summary>
/// Manages the Playwright browser lifecycle for E2E tests.
/// Creates a single chromium headless browser instance shared across test methods,
/// with a fresh BrowserContext per test for isolation.
///
/// Features:
/// - Screenshot capture (full page and component-level)
/// - Browser console log recording
/// - Visual comparison against baselines stored in TestOutput/Baselines/
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    /// <summary>Root directory for all screenshot output.</summary>
    public static readonly string ScreenshotRoot = Path.Combine(
        FindRepoRoot(),
        "tests", "Antiphon.E2E", "TestOutput", "Screenshots"
    );

    /// <summary>Root directory for baseline screenshots used in visual comparison.</summary>
    public static readonly string BaselineRoot = Path.Combine(
        FindRepoRoot(),
        "tests", "Antiphon.E2E", "TestOutput", "Baselines"
    );

    public IBrowser Browser => _browser;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        Directory.CreateDirectory(ScreenshotRoot);
        Directory.CreateDirectory(BaselineRoot);
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    /// <summary>
    /// Creates a new page in a fresh browser context.
    /// Console messages and errors are automatically recorded on the returned page.
    /// Caller is responsible for disposing the context.
    /// </summary>
    public async Task<(IPage Page, IBrowserContext Context)> NewPageAsync()
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true
        });

        var page = await context.NewPageAsync();

        // Record all browser console output so tests can inspect or assert on it
        page.Console += (_, msg) =>
        {
            if (msg.Type is "error" or "warning")
                Console.WriteLine($"[browser:{msg.Type}] {msg.Text}");
        };

        page.PageError += (_, err) =>
        {
            Console.WriteLine($"[browser:pageerror] {err}");
        };

        return (page, context);
    }

    // -------------------------------------------------------------------------
    // Screenshot helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Captures a full-page screenshot and saves it under TestOutput/Screenshots/{testName}/.
    /// Returns the path of the saved file.
    /// </summary>
    public static async Task<string> CapturePageAsync(
        IPage page,
        string label,
        [CallerMemberName] string testName = "unknown")
    {
        var dir = Path.Combine(ScreenshotRoot, Sanitize(testName));
        Directory.CreateDirectory(dir);

        var fileName = $"{Sanitize(label)}_{DateTime.Now:HHmmss}.png";
        var path = Path.Combine(dir, fileName);

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = true
        });

        Console.WriteLine($"[screenshot] {path}");
        return path;
    }

    /// <summary>
    /// Captures a screenshot of a specific element/component.
    /// Returns the path of the saved file.
    /// </summary>
    public static async Task<string> CaptureComponentAsync(
        ILocator locator,
        string label,
        [CallerMemberName] string testName = "unknown")
    {
        var dir = Path.Combine(ScreenshotRoot, Sanitize(testName), "components");
        Directory.CreateDirectory(dir);

        var fileName = $"{Sanitize(label)}_{DateTime.Now:HHmmss}.png";
        var path = Path.Combine(dir, fileName);

        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });

        Console.WriteLine($"[screenshot:component] {path}");
        return path;
    }

    /// <summary>
    /// Captures a screenshot and saves it on test failure. Call from a finally block.
    /// If the test succeeded, the screenshot is still saved but labelled as "pass".
    /// </summary>
    public static async Task CaptureOnCompletionAsync(
        IPage page,
        bool passed,
        [CallerMemberName] string testName = "unknown")
    {
        var label = passed ? "pass_final" : "FAIL_final";
        await CapturePageAsync(page, label, testName);
    }

    /// <summary>
    /// Compares a screenshot of the page against a stored baseline.
    /// On first run (no baseline), the current screenshot IS saved as the baseline.
    /// On subsequent runs, the pixel difference is computed and reported.
    /// Fails the test if the difference exceeds <paramref name="maxDiffPercent"/>.
    /// </summary>
    public static async Task AssertMatchesBaselineAsync(
        IPage page,
        string baselineName,
        double maxDiffPercent = 1.0,
        [CallerMemberName] string testName = "unknown")
    {
        var baselinePath = Path.Combine(BaselineRoot, $"{Sanitize(baselineName)}.png");
        var actualPath = await CapturePageAsync(page, $"baseline_check_{baselineName}", testName);

        if (!File.Exists(baselinePath))
        {
            // First run — persist as baseline
            File.Copy(actualPath, baselinePath, overwrite: false);
            Console.WriteLine($"[baseline] Created new baseline: {baselinePath}");
            return;
        }

        var diff = ComputePixelDiff(baselinePath, actualPath);
        Console.WriteLine($"[baseline] Pixel diff vs '{baselineName}': {diff:F2}%");

        if (diff > maxDiffPercent)
        {
            var diffLabel = $"DIFF_{diff:F1}pct";
            var diffPath = Path.Combine(ScreenshotRoot, Sanitize(testName), $"{Sanitize(baselineName)}_{diffLabel}.png");
            File.Copy(actualPath, diffPath, overwrite: true);

            throw new Xunit.Sdk.XunitException(
                $"Screenshot '{baselineName}' differs from baseline by {diff:F2}% " +
                $"(threshold: {maxDiffPercent}%). Actual: {actualPath} Baseline: {baselinePath}");
        }
    }

    /// <summary>
    /// Compares a component screenshot against a stored baseline.
    /// </summary>
    public static async Task AssertComponentMatchesBaselineAsync(
        ILocator locator,
        string baselineName,
        double maxDiffPercent = 1.0,
        [CallerMemberName] string testName = "unknown")
    {
        var baselinePath = Path.Combine(BaselineRoot, $"component_{Sanitize(baselineName)}.png");
        var actualPath = await CaptureComponentAsync(locator, $"baseline_check_{baselineName}", testName);

        if (!File.Exists(baselinePath))
        {
            File.Copy(actualPath, baselinePath, overwrite: false);
            Console.WriteLine($"[baseline:component] Created new baseline: {baselinePath}");
            return;
        }

        var diff = ComputePixelDiff(baselinePath, actualPath);
        Console.WriteLine($"[baseline:component] Pixel diff vs '{baselineName}': {diff:F2}%");

        if (diff > maxDiffPercent)
        {
            throw new Xunit.Sdk.XunitException(
                $"Component '{baselineName}' differs from baseline by {diff:F2}% " +
                $"(threshold: {maxDiffPercent}%). Actual: {actualPath} Baseline: {baselinePath}");
        }
    }

    // -------------------------------------------------------------------------
    // Application log capture
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads new application log lines that appeared since <paramref name="startPosition"/>.
    /// Useful for asserting server-side behavior after a UI action.
    /// </summary>
    public static string[] ReadNewLogLines(long startPosition, string? logPath = null)
    {
        logPath ??= Path.Combine(
            "C:", "MavLog", "Antiphon",
            $"antiphon-{DateTime.Now:yyyyMMdd}.log"
        );

        if (!File.Exists(logPath))
            return [];

        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(startPosition, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);

        return [.. lines];
    }

    /// <summary>
    /// Returns the current end-of-file position in the application log.
    /// Call before a test action, then call <see cref="ReadNewLogLines"/> after.
    /// </summary>
    public static long GetCurrentLogPosition(string? logPath = null)
    {
        logPath ??= Path.Combine(
            "C:", "MavLog", "Antiphon",
            $"antiphon-{DateTime.Now:yyyyMMdd}.log"
        );

        if (!File.Exists(logPath))
            return 0;

        return new FileInfo(logPath).Length;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static string Sanitize(string s) =>
        string.Join("_", s.Split(Path.GetInvalidFileNameChars()))
              .Replace(' ', '_')
              .Trim('_');

    /// <summary>
    /// Walks up the directory tree from the test binary to find the repo root
    /// (identified by the presence of a .git folder).
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        // Fall back to CWD if .git not found (e.g., in Docker)
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Computes approximate pixel difference between two PNG files as a percentage.
    /// Uses raw byte comparison on the PNG data sections (fast, not exact).
    /// For pixel-perfect comparison, swap this with a proper image diff library.
    /// </summary>
    private static double ComputePixelDiff(string baselinePath, string actualPath)
    {
        var baselineBytes = File.ReadAllBytes(baselinePath);
        var actualBytes = File.ReadAllBytes(actualPath);

        if (baselineBytes.Length == 0 || actualBytes.Length == 0)
            return 100.0;

        var minLen = Math.Min(baselineBytes.Length, actualBytes.Length);
        var maxLen = Math.Max(baselineBytes.Length, actualBytes.Length);

        long diffBytes = maxLen - minLen;
        for (var i = 0; i < minLen; i++)
        {
            if (baselineBytes[i] != actualBytes[i])
                diffBytes++;
        }

        return (double)diffBytes / maxLen * 100.0;
    }
}
