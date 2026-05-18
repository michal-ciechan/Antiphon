using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
            await Expect(cardDialog.GetByText("e2e", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
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

            await DragToAndWaitForMoveAsync(page, page.GetByLabel("Drag CARD-0001"), reviewColumn);

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

            await DragToAndWaitForMoveAsync(page, page.GetByLabel("Drag CARD-0001"), activeColumn);

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

    [Test]
    public async Task Board_user_can_edit_workflow_md_and_reload_persists_version()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var repoPath = await CreateLocalGitRepositoryAsync();
        var projectId = await CreateProjectAsync($"E09 Workflow Project {suffix}", repoPath);
        var boardId = await CreateBoardAsync(projectId, $"E09 Workflow Board {suffix}");
        var marker = $"E09_BROWSER_{suffix}";
        var reloadMarker = $"E09_RELOAD_{suffix}";
        var workflowContent = $$$"""
            ---
            name: E09 Browser {{{suffix}}}
            hooks:
              before_run: echo e09
            ---
            {{{marker}}}
            Work on {{ issue.title }} in {{ workspace.branch }}.
            """;

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/boards/{boardId}");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Workflow" }).ClickAsync();
            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog.GetByText("WORKFLOW.md")).ToBeVisibleAsync();

            var monaco = dialog.Locator(".monaco-editor");
            await monaco.WaitForAsync(new LocatorWaitForOptions { Timeout = 20_000 });
            await monaco.ClickAsync();
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.InsertTextAsync(workflowContent);
            var saveButton = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" });
            await Expect(saveButton).ToBeEnabledAsync();
            var saveResponse = await page.RunAndWaitForResponseAsync(
                () => saveButton.ClickAsync(),
                response => response.Url.Contains($"/api/boards/{boardId}/workflow", StringComparison.Ordinal)
                    && response.Request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase));
            if (saveResponse.Status >= 300)
            {
                var responseBody = await saveResponse.TextAsync();
                throw new InvalidOperationException(
                    $"Workflow save failed with HTTP {saveResponse.Status}: {responseBody}");
            }

            await WaitForWorkflowContentAsync(boardId, marker);
            var workflowFile = Path.Combine(
                repoPath,
                ".antiphon",
                "boards",
                boardId.ToString("N"),
                "WORKFLOW.md");
            File.Exists(workflowFile).ShouldBeTrue();
            (await File.ReadAllTextAsync(workflowFile)).ShouldContain(marker);

            var reloadedContent = $$$"""
                ---
                name: E09 Reload {{{suffix}}}
                hooks:
                  before_run: echo reload
                ---
                {{{reloadMarker}}}
                Work on {{ issue.identifier }} from disk.
                """;
            await File.WriteAllTextAsync(workflowFile, reloadedContent);
            await WaitForWorkflowContentAsync(boardId, reloadMarker);

            await page.ReloadAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Workflow" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Dialog).GetByText("v2")).ToBeVisibleAsync();

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    [Test]
    public async Task Board_user_can_review_real_worktree_diff_and_send_inline_comment()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var repoPath = await CreateLocalGitRepositoryAsync();
        var projectId = await CreateProjectAsync($"E12 Review Project {suffix}", repoPath);
        var boardId = await CreateBoardAsync(projectId, $"E12 Review Board {suffix}");
        var board = await GetBoardAsync(boardId);
        var reviewColumnId = GetColumnId(board, "review");
        var cardTitle = $"E12 Review Card {suffix}";
        var cardId = await CreateCardAsync(boardId, reviewColumnId, cardTitle);
        var sessionId = await SpawnCardAsync(cardId);
        await WaitForSessionRunningAsync(boardId, sessionId);
        await MoveCardAsync(boardId, cardId, reviewColumnId);

        var worktreePath = await GetCurrentWorktreePathAsync(cardId);
        await File.AppendAllTextAsync(Path.Combine(worktreePath, "README.md"), $"changed {suffix}\nchanged again {suffix}\n");
        await File.WriteAllTextAsync(Path.Combine(worktreePath, "new-file.txt"), $"untracked {suffix}\n");

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/boards/{boardId}");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            var card = page.GetByRole(AriaRole.Article, new PageGetByRoleOptions
            {
                NameRegex = new Regex(cardTitle)
            });
            await Expect(card).ToBeVisibleAsync();
            await card.ClickAsync();

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog.GetByText("Diff review")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
            {
                Timeout = 15_000
            });
            await Expect(dialog.GetByText($"+changed {suffix}")).ToBeVisibleAsync();
            await Expect(dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions
            {
                NameRegex = new Regex("^new-file\\.txt")
            })).ToBeVisibleAsync();
            await Expect(dialog.GetByText($"+untracked {suffix}")).ToBeVisibleAsync();

            await dialog.GetByLabel("Comment on README.md new line 2").ClickAsync();
            await page.Keyboard.DownAsync("Shift");
            try
            {
                await dialog.GetByLabel("Comment on README.md new line 3").ClickAsync();
            }
            finally
            {
                await page.Keyboard.UpAsync("Shift");
            }
            await dialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions
            {
                Name = "Comment for README.md new lines 2-3"
            }).FillAsync("Please verify this E12 range.");
            var commentResponse = await page.RunAndWaitForResponseAsync(
                () => dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions
                {
                    Name = "Send comment for README.md new lines 2-3"
                }).ClickAsync(),
                response => response.Url.Contains($"/api/cards/{cardId}/comments", StringComparison.Ordinal)
                    && response.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase));
            commentResponse.Status.ShouldBe(202);
            var commentPostData = commentResponse.Request.PostData;
            commentPostData.ShouldNotBeNull();
            commentPostData!.ShouldContain("\"line\":2");
            commentPostData.ShouldContain("\"endLine\":3");

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    [Test]
    public async Task Board_stopped_claude_session_shows_terminal_overlay_and_resume_button()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectId = await CreateProjectAsync($"Session Overlay Project {suffix}");
        var boardId = await CreateBoardAsync(projectId, $"Session Overlay Board {suffix}");
        var cardTitle = $"Stopped Claude Card {suffix}";
        var cardId = await CreateCardAsync(boardId, boardColumnId: null, cardTitle);
        var sessionCwd = Path.Combine(Path.GetTempPath(), "antiphon-e2e-stopped-sessions", suffix);
        Directory.CreateDirectory(sessionCwd);
        _tempRepoRoots.Add(sessionCwd);
        var sessionId = await AddClaudeSessionAsync(cardId, sessionCwd, SessionStatus.Stopped);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/boards/{boardId}");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            var card = page.GetByRole(AriaRole.Article, new PageGetByRoleOptions
            {
                NameRegex = new Regex(cardTitle)
            });
            await Expect(card).ToBeVisibleAsync();
            await card.ClickAsync();

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog.GetByTestId("session-terminal")).ToBeVisibleAsync();
            await Expect(dialog.GetByTestId("session-terminal-inactive-overlay")).ToBeVisibleAsync();
            await Expect(dialog.GetByText("Session is not running")).ToBeVisibleAsync();
            await Expect(dialog.GetByText(sessionId.ToString())).ToBeVisibleAsync();
            await Expect(dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Resume" })).ToBeVisibleAsync();

            var screenshotPath = Path.Combine(
                FindRepoRoot(),
                "docs",
                "screenshots",
                "toonsharp-antiphon",
                "33-stopped-session-overlay-resume.png");
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

    [Test]
    public async Task Board_failed_claude_resume_missing_session_shows_recovery_actions()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectId = await CreateProjectAsync($"Session Recovery Project {suffix}");
        var boardId = await CreateBoardAsync(projectId, $"Session Recovery Board {suffix}");
        var cardTitle = $"Missing Claude Session Card {suffix}";
        var cardId = await CreateCardAsync(boardId, boardColumnId: null, cardTitle);
        var sessionCwd = Path.Combine(Path.GetTempPath(), "antiphon-e2e-missing-claude-sessions", suffix);
        Directory.CreateDirectory(sessionCwd);
        _tempRepoRoots.Add(sessionCwd);
        await AddClaudeSessionAsync(
            cardId,
            sessionCwd,
            SessionStatus.Failed,
            AgentSessionService.ClaudeSessionNotFoundFailureReason);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/boards/{boardId}");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            var card = page.GetByRole(AriaRole.Article, new PageGetByRoleOptions
            {
                NameRegex = new Regex(cardTitle)
            });
            await Expect(card).ToBeVisibleAsync();
            await card.ClickAsync();

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog.GetByTestId("session-terminal")).ToBeVisibleAsync();
            await Expect(dialog.GetByTestId("claude-session-recovery")).ToBeVisibleAsync();
            await Expect(dialog.GetByText("Claude session was not found")).ToBeVisibleAsync();
            await Expect(dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Resume" })).ToBeVisibleAsync();
            await Expect(dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Continue from context" })).ToBeVisibleAsync();
            await Expect(dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Start new session" })).ToBeVisibleAsync();

            var screenshotPath = Path.Combine(
                FindRepoRoot(),
                "docs",
                "screenshots",
                "toonsharp-antiphon",
                "35-claude-session-recovery-prompt.png");
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

    [Test]
    public async Task Board_running_session_shows_stop_button()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var projectId = await CreateProjectAsync($"Session Stop Project {suffix}");
        var boardId = await CreateBoardAsync(projectId, $"Session Stop Board {suffix}");
        var cardTitle = $"Running Session Card {suffix}";
        var cardId = await CreateCardAsync(boardId, boardColumnId: null, cardTitle);
        var sessionCwd = Path.Combine(Path.GetTempPath(), "antiphon-e2e-running-sessions", suffix);
        Directory.CreateDirectory(sessionCwd);
        _tempRepoRoots.Add(sessionCwd);
        await AddClaudeSessionAsync(cardId, sessionCwd, SessionStatus.Running);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            var response = await page.GotoAsync($"{_appFixture.PlaywrightAddress}/boards/{boardId}");
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            var card = page.GetByRole(AriaRole.Article, new PageGetByRoleOptions
            {
                NameRegex = new Regex(cardTitle)
            });
            await Expect(card).ToBeVisibleAsync();
            await card.ClickAsync();

            var dialog = page.GetByRole(AriaRole.Dialog);
            await Expect(dialog.GetByTestId("session-terminal")).ToBeVisibleAsync();
            await Expect(dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Stop" })).ToBeVisibleAsync();
            await Expect(dialog.GetByTestId("session-terminal-inactive-overlay")).Not.ToBeVisibleAsync();

            var screenshotPath = Path.Combine(
                FindRepoRoot(),
                "docs",
                "screenshots",
                "toonsharp-antiphon",
                "34-running-session-stop-button.png");
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

    private async Task WaitForWorkflowContentAsync(Guid boardId, string expectedText, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var response = await _appFixture.HttpClient.GetAsync($"/api/boards/{boardId}/workflow");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            if (body.GetProperty("content").GetString()?.Contains(expectedText, StringComparison.Ordinal) == true)
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException($"Workflow content for board {boardId} did not contain '{expectedText}'.");
    }

    private async Task<Guid> CreateCardAsync(Guid boardId, Guid? boardColumnId, string title)
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

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> SpawnCardAsync(Guid cardId)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            $"/api/cards/{cardId}/spawn",
            new
            {
                prompt = "echo E12 review target ready",
                cols = 100,
                rows = 24
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("sessionId").GetGuid();
    }

    private async Task<Guid> AddClaudeSessionAsync(
        Guid cardId,
        string cwd,
        SessionStatus status,
        string? failureReason = null)
    {
        using var scope = _appFixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards.SingleAsync(c => c.Id == cardId);
        var now = DateTime.UtcNow;
        var session = new AgentSession
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = status,
            Cwd = cwd,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
            EndedAt = status == SessionStatus.Running ? null : now,
            FailureReason = failureReason,
            Card = card
        };

        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task MoveCardAsync(Guid boardId, Guid cardId, Guid boardColumnId)
    {
        var board = await GetBoardAsync(boardId);
        var card = FindCard(board, cardId);
        var response = await _appFixture.HttpClient.PatchAsJsonAsync(
            $"/api/cards/{cardId}",
            new
            {
                boardColumnId,
                concurrencyToken = card.GetProperty("concurrencyToken").GetString()
            },
            JsonOptions);
        response.EnsureSuccessStatusCode();
    }

    private async Task WaitForSessionRunningAsync(Guid boardId, Guid sessionId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var board = await GetBoardAsync(boardId);
            foreach (var column in board.GetProperty("columns").EnumerateArray())
            {
                foreach (var card in column.GetProperty("cards").EnumerateArray())
                {
                    foreach (var session in card.GetProperty("sessions").EnumerateArray())
                    {
                        if (session.GetProperty("id").GetGuid() != sessionId)
                            continue;

                        var status = session.GetProperty("status").GetString();
                        if (string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase))
                            return;
                    }
                }
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Session {sessionId} did not reach Running status.");
    }

    private async Task<string> GetCurrentWorktreePathAsync(Guid cardId)
    {
        using var scope = _appFixture.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var card = await db.Cards
            .Include(c => c.CurrentWorktree)
            .SingleAsync(c => c.Id == cardId);

        return card.CurrentWorktree?.Path
            ?? throw new InvalidOperationException($"Card {cardId} has no current worktree.");
    }

    private static Guid GetColumnId(JsonElement board, string stateKey)
    {
        return board.GetProperty("columns")
            .EnumerateArray()
            .Single(column => column.GetProperty("stateKey").GetString() == stateKey)
            .GetProperty("id")
            .GetGuid();
    }

    private static JsonElement FindCard(JsonElement board, Guid cardId)
    {
        foreach (var column in board.GetProperty("columns").EnumerateArray())
        {
            foreach (var card in column.GetProperty("cards").EnumerateArray())
            {
                if (card.GetProperty("id").GetGuid() == cardId)
                    return card;
            }
        }

        throw new InvalidOperationException($"Board payload does not contain card {cardId}.");
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

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
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

    private static async Task DragToAndWaitForMoveAsync(IPage page, ILocator source, ILocator target)
    {
        var response = await page.RunAndWaitForResponseAsync(
            () => DragToAsync(page, source, target),
            response => response.Url.Contains("/api/cards/", StringComparison.Ordinal)
                && response.Request.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase));
        if (response.Status >= 300)
        {
            var responseBody = await response.TextAsync();
            throw new InvalidOperationException(
                $"Card move failed with HTTP {response.Status}: {responseBody}");
        }
    }
}
