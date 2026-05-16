using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Antiphon.E2E.Fixtures;
using Microsoft.Playwright;
using Shouldly;
using TUnit.Core;
using static Microsoft.Playwright.Assertions;

namespace Antiphon.E2E;

/// <summary>
/// Browser-level coverage for the board-driven E08 workflow.
/// </summary>
[NotInParallel]
public class BoardE2ETests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly AntiphonAppFixture _appFixture = new();
    private readonly PlaywrightFixture _playwrightFixture = new();
    private readonly List<string> _tempRepoRoots = [];

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
        foreach (var repoRoot in _tempRepoRoots)
            await DeleteDirectoryBestEffortAsync(repoRoot);
    }

    [Test]
    public async Task Board_user_can_create_board_create_card_and_open_card_modal()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectName = $"E08 Board Project {suffix}";
        var boardName = $"E08 Delivery {suffix}";
        var cardTitle = $"E08 Browser Card {suffix}";

        await CreateProjectAsync(projectName);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/boards");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Board" }).ClickAsync();
            var newBoardDialog = page.GetByRole(AriaRole.Dialog);
            await newBoardDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Project" }).ClickAsync();
            await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = projectName }).ClickAsync();
            await newBoardDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Name" }).FillAsync(boardName);
            await newBoardDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create" }).ClickAsync();

            await Expect(page.Locator("p").Filter(new LocatorFilterOptions { HasText = $"{projectName} / {boardName}" }))
                .ToBeVisibleAsync();
            await Expect(page.GetByTestId("board-column-backlog")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("board-column-in-progress")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("board-column-review")).ToBeVisibleAsync();
            await Expect(page.GetByTestId("board-column-done")).ToBeVisibleAsync();

            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "New Card" }).ClickAsync();
            var newCardDialog = page.GetByRole(AriaRole.Dialog);
            await newCardDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Title" }).FillAsync(cardTitle);
            await newCardDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Description" }).FillAsync("Created through the E08 board UI.");
            await newCardDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Labels" }).FillAsync("e2e,ui");
            await newCardDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Create" }).ClickAsync();

            await Expect(page.GetByRole(AriaRole.Article, new PageGetByRoleOptions { NameRegex = new Regex(cardTitle) }))
                .ToBeVisibleAsync();

            await page.GetByRole(AriaRole.Article, new PageGetByRoleOptions { NameRegex = new Regex(cardTitle) }).ClickAsync();
            var cardDialog = page.GetByRole(AriaRole.Dialog);
            await Expect(cardDialog).ToBeVisibleAsync();
            await Expect(cardDialog.GetByText(cardTitle)).ToBeVisibleAsync();
            await Expect(cardDialog.GetByText("e2e")).ToBeVisibleAsync();
            await Expect(cardDialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Spawn" })).ToBeVisibleAsync();

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    [Test]
    public async Task Board_user_can_drag_card_between_columns_and_reload_persists_move()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectId = await CreateProjectAsync($"E08 Drag Project {suffix}");
        var boardId = await CreateBoardAsync(projectId, $"E08 Drag Board {suffix}");
        var board = await GetBoardAsync(boardId);
        var activeColumnId = board.GetProperty("columns")
            .EnumerateArray()
            .Single(column => column.GetProperty("stateKey").GetString() == "in-progress")
            .GetProperty("id")
            .GetGuid();
        var cardTitle = $"E08 Drag Card {suffix}";
        await CreateCardAsync(boardId, activeColumnId, cardTitle);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/boards/{boardId}");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            var activeColumn = page.GetByTestId("board-column-in-progress");
            var reviewColumn = page.GetByTestId("board-column-review");
            await Expect(activeColumn.GetByRole(AriaRole.Article, new LocatorGetByRoleOptions
            {
                NameRegex = new Regex(cardTitle)
            })).ToBeVisibleAsync();

            await DragToAsync(page, page.GetByLabel("Drag CARD-0001"), reviewColumn);

            await Expect(reviewColumn.GetByRole(AriaRole.Article, new LocatorGetByRoleOptions
            {
                NameRegex = new Regex(cardTitle)
            })).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

            await page.ReloadAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await Expect(page.GetByTestId("board-column-review").GetByRole(AriaRole.Article, new LocatorGetByRoleOptions
            {
                NameRegex = new Regex(cardTitle)
            })).ToBeVisibleAsync();

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    [Test]
    public async Task Board_user_can_drag_backlog_card_to_in_progress_and_open_terminal_session()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var repoPath = await CreateLocalGitRepositoryAsync();
        var projectId = await CreateProjectAsync($"E08 Spawn Project {suffix}", repoPath);
        var boardId = await CreateBoardAsync(projectId, $"E08 Spawn Board {suffix}");
        var cardTitle = $"E08 Spawn Card {suffix}";
        await CreateCardAsync(boardId, boardColumnId: null, cardTitle);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/boards/{boardId}");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            var backlogColumn = page.GetByTestId("board-column-backlog");
            var activeColumn = page.GetByTestId("board-column-in-progress");
            await Expect(backlogColumn.GetByRole(AriaRole.Article, new LocatorGetByRoleOptions
            {
                NameRegex = new Regex(cardTitle)
            })).ToBeVisibleAsync();

            await DragToAsync(page, page.GetByLabel("Drag CARD-0001"), activeColumn);

            var movedCard = activeColumn.GetByRole(AriaRole.Article, new LocatorGetByRoleOptions
            {
                NameRegex = new Regex(cardTitle)
            });
            await Expect(movedCard).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

            await movedCard.ClickAsync();
            var cardDialog = page.GetByRole(AriaRole.Dialog);
            await Expect(cardDialog.GetByText("Session 1")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
            {
                Timeout = 30_000
            });
            await Expect(cardDialog.GetByTestId("session-terminal")).ToBeVisibleAsync();
            await Expect(cardDialog.Locator(".xterm")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
            {
                Timeout = 10_000
            });

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    private async Task<Guid> CreateProjectAsync(string name, string? localRepositoryPath = null)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            "/api/projects",
            new
            {
                name,
                gitRepositoryUrl = "https://github.com/example/e08-board-e2e.git",
                localRepositoryPath,
                baseBranch = "main",
                constitutionPath = (string?)null,
                gitHubIntegrationEnabled = false,
                notificationsEnabled = false
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBoardAsync(Guid projectId, string name)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            "/api/boards",
            new
            {
                projectId,
                name
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> GetBoardAsync(Guid boardId)
    {
        var response = await _appFixture.HttpClient.GetAsync($"/api/boards/{boardId}");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task CreateCardAsync(Guid boardId, Guid? boardColumnId, string title)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            $"/api/boards/{boardId}/cards",
            new
            {
                boardColumnId,
                title,
                description = "Moved through the real board UI.",
                labels = new[] { "e2e", "drag" }
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> CreateLocalGitRepositoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-e2e-board-repos", Guid.NewGuid().ToString("N"));
        var repoPath = Path.Combine(root, "work");
        Directory.CreateDirectory(repoPath);
        _tempRepoRoots.Add(root);

        await RunGitAsync(repoPath, "init");
        await RunGitAsync(repoPath, "config", "user.email", "e2e@example.test");
        await RunGitAsync(repoPath, "config", "user.name", "Antiphon E2E");
        await File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), "# E2E board repository\n");
        await RunGitAsync(repoPath, "add", "README.md");
        await RunGitAsync(repoPath, "commit", "-m", "Initial commit");
        await RunGitAsync(repoPath, "branch", "-M", "main");

        return repoPath;
    }

    private static async Task RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed with exit code {process.ExitCode}: {stdout}{stderr}");
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

    private static async Task DragToAsync(IPage page, ILocator source, ILocator target)
    {
        var sourceBox = await source.BoundingBoxAsync();
        sourceBox.ShouldNotBeNull();
        var targetBox = await target.BoundingBoxAsync();
        targetBox.ShouldNotBeNull();

        var startX = sourceBox!.X + sourceBox.Width / 2;
        var startY = sourceBox.Y + sourceBox.Height / 2;
        var endX = targetBox!.X + targetBox.Width / 2;
        var endY = targetBox.Y + Math.Min(120, targetBox.Height / 2);

        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(startX + 10, startY + 10, new MouseMoveOptions { Steps = 5 });
        await page.Mouse.MoveAsync(endX, endY, new MouseMoveOptions { Steps = 20 });
        await page.Mouse.UpAsync();
    }
}
