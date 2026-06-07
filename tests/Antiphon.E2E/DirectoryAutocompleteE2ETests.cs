using Antiphon.E2E.Fixtures;
using Microsoft.Playwright;
using Shouldly;
using TUnit.Core;
using static Microsoft.Playwright.Assertions;

namespace Antiphon.E2E;

/// <summary>
/// Reproduces the "no autocomplete hints in the agent modal's Working directory" bug. A Windows
/// user types a backslash path; the server returns normalized forward-slash suggestions; the
/// Mantine Autocomplete then re-filters those suggestions against the raw backslash input
/// (substring match) and drops them all. The test asserts the backend DOES return the child
/// (isolating the bug to the client), then asserts the suggestion option actually appears.
/// </summary>
[NotInParallel]
public class DirectoryAutocompleteE2ETests
{
    private readonly AntiphonAppFixture _appFixture = new();
    private readonly PlaywrightFixture _playwrightFixture = new();
    private readonly List<string> _tempRoots = [];

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
        foreach (var path in _tempRoots)
            await DeleteBestEffortAsync(path);
    }

    [Test]
    public async Task Working_directory_shows_autocomplete_hints_for_typed_path()
    {
        // A real directory tree the suggestions should surface.
        var parent = Path.Combine(Path.GetTempPath(), "antiphon-ac-" + Guid.NewGuid().ToString("N"));
        var childName = "child-" + Guid.NewGuid().ToString("N")[..8];
        Directory.CreateDirectory(Path.Combine(parent, childName));
        _tempRoots.Add(parent);

        // The native (backslash) path with a trailing separator, as a Windows user types it.
        var typed = parent + Path.DirectorySeparatorChar;

        // Backend sanity: the browse endpoint DOES return the child (normalized forward slashes).
        // So if the dropdown is empty, the fault is purely client-side filtering.
        var apiBody = await _appFixture.HttpClient.GetStringAsync(
            $"/api/filesystem/browse?path={Uri.EscapeDataString(typed)}");
        apiBody.ShouldContain(childName);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            await page.GotoAsync($"{_appFixture.PlaywrightAddress}/agents");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            await page.GetByRole(AriaRole.Button, new() { Name = "New Agent" }).ClickAsync();
            var dialog = page.GetByRole(AriaRole.Dialog);
            var input = dialog.GetByRole(AriaRole.Textbox, new() { Name = "Working directory" });

            await input.ClickAsync();
            await input.PressSequentiallyAsync(typed, new() { Delay = 20 });

            // The suggestion for the child directory must appear in the dropdown.
            var option = page.GetByRole(AriaRole.Option).Filter(new() { HasText = childName });
            await Expect(option).ToBeVisibleAsync(new() { Timeout = 7_000 });

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    private static async Task DeleteBestEffortAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)));
            }
        }
    }
}
