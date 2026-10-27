using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Shouldly;
using TUnit.Core;
using static Microsoft.Playwright.Assertions;

namespace Antiphon.E2E;

[NotInParallel]
[Category("OptIn")]
public class AgentE2ETests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

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
            await DeleteDirectoryBestEffortAsync(path);
    }

    [Test]
    public async Task Agents_page_creates_agent_and_assigns_card_to_queue()
    {
        const string suffix = "foundation";
        var cardTitle = $"Agent Queue Card {suffix}";
        var cardDescription = "Created through the agents E2E Add Card flow.";
        var agentName = $"E2E Agent {suffix}";
        var workingDirectory = CreateWorkingDirectory(suffix);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/agents");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Agent" }).ClickAsync();
            var createDialog = page.GetByRole(AriaRole.Dialog);
            await Expect(createDialog.GetByLabel("Runner profile")).ToHaveValueAsync("e2e-raw (Raw) · default");
            var nameInput = createDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name" });
            var directoryInput = createDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Working directory" });
            await nameInput.ClickAsync();
            await nameInput.PressSequentiallyAsync(agentName);
            await directoryInput.ClickAsync();
            await directoryInput.PressSequentiallyAsync(workingDirectory);
            await createDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Details" }).FillAsync("Created through the agents E2E flow.");
            await Expect(nameInput).ToHaveValueAsync(agentName);
            await Expect(directoryInput).ToHaveValueAsync(workingDirectory);
            var createButton = createDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create" });
            await Expect(createButton).ToBeEnabledAsync();
            var createResponse = await page.RunAndWaitForResponseAsync(
                () => createButton.ClickAsync(),
                apiResponse => apiResponse.Url.EndsWith("/api/agents", StringComparison.Ordinal)
                    && apiResponse.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase));
            if (createResponse.Status >= 300)
            {
                var responseBody = await createResponse.TextAsync();
                throw new InvalidOperationException(
                    $"Agent creation failed with HTTP {createResponse.Status}: {responseBody}");
            }

            var agentTile = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Agent {agentName}" });
            await Expect(agentTile).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
            await agentTile.ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new PageGetByRoleOptions { Name = agentName })).ToBeVisibleAsync();

            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Add Card" }).ClickAsync();
            var addDialog = page.GetByRole(AriaRole.Dialog);
            await addDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Title" }).FillAsync(cardTitle);
            await addDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Description" }).FillAsync(cardDescription);

            var createCardResponse = await page.RunAndWaitForResponseAsync(
                () => addDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add" }).ClickAsync(),
                apiResponse => apiResponse.Url.Contains("/api/boards/", StringComparison.Ordinal)
                    && apiResponse.Url.EndsWith("/cards", StringComparison.Ordinal)
                    && apiResponse.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase));
            if (createCardResponse.Status >= 300)
            {
                var responseBody = await createCardResponse.TextAsync();
                throw new InvalidOperationException(
                    $"Card creation failed with HTTP {createCardResponse.Status}: {responseBody}");
            }

            using var cardBody = JsonDocument.Parse(await createCardResponse.TextAsync());
            var cardId = cardBody.RootElement.GetProperty("id").GetGuid();
            var identifier = cardBody.RootElement.GetProperty("identifier").GetString()!;

            var queueRow = page.GetByRole(AriaRole.Row).Filter(new LocatorFilterOptions { HasText = cardTitle });
            await Expect(queueRow).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
            await Expect(queueRow).ToContainTextAsync($"{identifier} - {cardTitle}");
            await Expect(queueRow.GetByRole(AriaRole.Cell).Last).ToHaveTextAsync("-");

            await AssertCardAssignedAsync(cardId);
            await page.GetByText("Agent created").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 10_000
            });
            var screenshotPath = Path.Combine(
                FindRepoRoot(),
                "docs",
                "screenshots",
                "agents",
                "01-agent-queue-foundation.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = screenshotPath,
                FullPage = true
            });
            Console.WriteLine($"[screenshot] {screenshotPath}");

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    // CARD-0105: the terminal icon's context menu (right-click, Shift+F10) reaches the same
    // destinations as the kebab. The agent and its board are seeded through the API; the create
    // dialog is covered by the test above.
    [Test]
    public async Task Agents_page_terminal_icon_context_menu_reaches_settings()
    {
        const string suffix = "contextmenu";
        var agentName = $"E2E Agent {suffix}";
        var workingDirectory = CreateWorkingDirectory(suffix);
        var boardId = await SeedBoardAsync(suffix);
        var seed = await _appFixture.HttpClient.PostAsJsonAsync("/api/agents", new
        {
            name = agentName,
            workingDirectory,
            boardId,
        }, JsonOptions);
        seed.EnsureSuccessStatusCode();

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/agents");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await Expect(page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Agent {agentName}" }))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

            var icon = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Terminal {agentName}" });
            var menu = page.GetByRole(AriaRole.Menu, new PageGetByRoleOptions { Name = $"Terminal menu {agentName}" });

            await icon.ClickAsync(new LocatorClickOptions { Button = MouseButton.Right });
            await Expect(menu).ToBeVisibleAsync();
            await Expect(menu.GetByRole(AriaRole.Menuitem)).ToHaveTextAsync(
                new[] { "Open terminal", "Open files view", "Open board", "Edit settings" });

            await menu.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Edit settings" }).ClickAsync();
            var settingsDialog = page.GetByRole(AriaRole.Dialog, new PageGetByRoleOptions { IncludeHidden = true });
            await Expect(settingsDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Details" }))
                .ToBeVisibleAsync();
            await Expect(menu).ToBeHiddenAsync();
            await page.Keyboard.PressAsync("Escape");
            await Expect(settingsDialog).ToBeHiddenAsync();
            // Wait for the closing modal's focus trap and focus return to finish before
            // sending a shortcut to the terminal icon behind it.
            await settingsDialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

            await icon.PressAsync("Shift+F10");
            await Expect(menu).ToBeVisibleAsync();
            await page.Keyboard.PressAsync("Escape");
            await Expect(menu).ToBeHiddenAsync();
            await Expect(icon).ToBeFocusedAsync();

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    private async Task<Guid> SeedBoardAsync(string suffix)
    {
        var project = await _appFixture.HttpClient.PostAsJsonAsync("/api/projects", new
        {
            name = $"E2E Project {suffix}",
            gitRepositoryUrl = "https://github.com/example/e2e-contextmenu.git",
            baseBranch = "main",
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false,
        }, JsonOptions);
        project.EnsureSuccessStatusCode();
        var projectId = (await project.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
        var board = await _appFixture.HttpClient.PostAsJsonAsync(
            "/api/boards", new { projectId, name = $"E2E Board {suffix}" }, JsonOptions);
        board.EnsureSuccessStatusCode();
        return (await board.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
    }

    private async Task AssertCardAssignedAsync(Guid cardId)
    {
        using var scope = _appFixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards.SingleAsync(c => c.Id == cardId);

        card.AssignedAgentId.ShouldNotBeNull();
        card.AgentQueuePosition.ShouldBe(1);
        card.ActiveWorkflowRunId.ShouldBeNull();
        (await db.CardWorkflowRuns.CountAsync(r => r.CardId == cardId)).ShouldBe(0);
    }

    private string CreateWorkingDirectory(string suffix)
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-e2e-agent-working-directories", suffix);
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private static async Task DeleteDirectoryBestEffortAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;

                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

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
