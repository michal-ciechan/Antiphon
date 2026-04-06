using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Antiphon.E2E.Fixtures;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.E2E;

/// <summary>
/// E2E tests for the delete workflow feature and branch naming conventions.
/// Covers API-level delete semantics, running-workflow guard, branch name format,
/// and the browser-side confirmation flow.
/// </summary>
[Collection("E2E")]
public class WorkflowDeleteTests : IAsyncLifetime
{
    // Seeded template IDs (from DatabaseSeeder)
    private static readonly Guid DocProjectTemplateId = new("b0000000-0000-0000-0000-000000000003");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

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

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a project via the API and returns its ID.
    /// </summary>
    private async Task<Guid> CreateProjectAsync(string name = "E2E Test Project")
    {
        var payload = new
        {
            name,
            gitRepositoryUrl = "https://github.com/example/e2e-test-repo.git",
            constitutionPath = (string?)null,
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false,
            localRepositoryPath = (string?)null
        };

        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            "/api/projects", payload, JsonOptions
        );
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Creates a workflow via the API and returns its ID.
    /// The workflow starts in Running state; the engine launches immediately.
    /// </summary>
    private async Task<Guid> CreateWorkflowAsync(
        Guid projectId,
        string featureName = "e2e test workflow",
        Guid? templateId = null)
    {
        var payload = new
        {
            templateId = templateId ?? DocProjectTemplateId,
            projectId,
            name = (string?)null,
            initialContext = (string?)null,
            featureName,
            selectedStages = (List<string>?)null,
            stageModelOverrides = (Dictionary<string, string>?)null
        };

        var response = await _appFixture.HttpClient.PostAsJsonAsync(
            "/api/workflows", payload, JsonOptions
        );
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Abandons a workflow so it can be deleted (Running → Abandoned).
    /// </summary>
    private async Task AbandonWorkflowAsync(Guid workflowId)
    {
        var response = await _appFixture.HttpClient.PostAsync(
            $"/api/workflows/{workflowId}/abandon", null
        );
        response.EnsureSuccessStatusCode();
    }

    // -------------------------------------------------------------------------
    // Test 1: API delete returns 204, subsequent GET returns 404
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_workflow_via_api_returns_no_content()
    {
        // Arrange
        var projectId = await CreateProjectAsync("Delete Test Project");
        var workflowId = await CreateWorkflowAsync(projectId, "feature to delete");

        // The workflow starts Running; abandon it so deletion is allowed
        await AbandonWorkflowAsync(workflowId);

        // Act — DELETE
        var deleteResponse = await _appFixture.HttpClient.DeleteAsync(
            $"/api/workflows/{workflowId}"
        );

        // Assert — 204 No Content
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Act — GET after delete
        var getResponse = await _appFixture.HttpClient.GetAsync(
            $"/api/workflows/{workflowId}"
        );

        // Assert — 404 Not Found
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // -------------------------------------------------------------------------
    // Test 2: Deleting a running workflow is rejected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_running_workflow_returns_error_status()
    {
        // Arrange — create a workflow, then force its status to Running via the DB.
        // We do this directly because the engine may transition the workflow to Failed
        // quickly in a test environment that has no real LLM credentials.
        var projectId = await CreateProjectAsync("Running Delete Test Project");
        var workflowId = await CreateWorkflowAsync(projectId, "running workflow to delete");

        using (var scope = _appFixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var workflow = await db.Workflows.FindAsync(workflowId);
            if (workflow is not null)
            {
                workflow.Status = WorkflowStatus.Running;
                await db.SaveChangesAsync();
            }
        }

        // Act — attempt DELETE while the workflow is Running
        var deleteResponse = await _appFixture.HttpClient.DeleteAsync(
            $"/api/workflows/{workflowId}"
        );

        // Assert — must not be 2xx; the service throws InvalidOperationException
        ((int)deleteResponse.StatusCode).Should().BeGreaterThanOrEqualTo(400);

        // Assert — workflow still exists after the failed delete
        var getAfterResponse = await _appFixture.HttpClient.GetAsync(
            $"/api/workflows/{workflowId}"
        );
        getAfterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -------------------------------------------------------------------------
    // Test 3: GitBranchName follows "antiphon/<sanitized>" format
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_workflow_uses_antiphon_prefixed_branch_name()
    {
        // Arrange
        var projectId = await CreateProjectAsync("Branch Name Test Project");
        var workflowId = await CreateWorkflowAsync(projectId, "my test feature");

        // Act — query DB directly through the DI container
        using var scope = _appFixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workflow = await db.Workflows.FindAsync(workflowId);

        // Assert
        workflow.Should().NotBeNull();
        workflow!.GitBranchName.Should().Be("antiphon/my-test-feature");
    }

    [Fact]
    public async Task Create_workflow_sanitizes_special_chars_in_branch_name()
    {
        // Arrange — feature name with mixed casing and special characters
        var projectId = await CreateProjectAsync("Branch Sanitize Test Project");
        var workflowId = await CreateWorkflowAsync(projectId, "Fix Bug #42: User's Login!");

        // Act
        using var scope = _appFixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workflow = await db.Workflows.FindAsync(workflowId);

        // Assert — special chars become hyphens, consecutive hyphens collapsed, lowercase
        workflow.Should().NotBeNull();
        workflow!.GitBranchName.Should().StartWith("antiphon/");
        workflow.GitBranchName.Should().NotContain(" ");
        workflow.GitBranchName.Should().NotContain("#");
        workflow.GitBranchName.Should().NotContain(":");
        workflow.GitBranchName.Should().NotContain("'");
        workflow.GitBranchName.Should().Be("antiphon/fix-bug-42-user-s-login");
    }

    // -------------------------------------------------------------------------
    // Test 4: Browser — settings gear on detail page opens delete confirmation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_workflow_via_settings_button_in_detail_page_navigates_to_dashboard()
    {
        // Arrange
        var projectId = await CreateProjectAsync("Detail Delete Test Project");
        var workflowId = await CreateWorkflowAsync(projectId, "detail page delete feature");
        await AbandonWorkflowAsync(workflowId);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            // Navigate directly to the workflow detail page
            var detailUrl = $"{_appFixture.PlaywrightAddress}/workflows/{workflowId}";
            var response = await page.GotoAsync(detailUrl);
            Assert.NotNull(response);
            response!.Status.Should().BeLessThan(500);

            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

            // Screenshot: initial state of the detail page
            await PlaywrightFixture.CapturePageAsync(page, "01_detail_page_loaded");

            // Click the settings gear icon
            var settingsButton = page.GetByRole(
                Microsoft.Playwright.AriaRole.Button,
                new Microsoft.Playwright.LocatorGetByRoleOptions { Name = "Workflow settings" }
            );
            await settingsButton.WaitForAsync(new Microsoft.Playwright.LocatorWaitForOptions { Timeout = 10_000 });
            await settingsButton.ClickAsync();

            // Screenshot: dropdown menu open
            await PlaywrightFixture.CapturePageAsync(page, "02_settings_menu_open");

            // Click "Delete Workflow" in the dropdown
            var deleteMenuItem = page.GetByRole(
                Microsoft.Playwright.AriaRole.Menuitem,
                new Microsoft.Playwright.LocatorGetByRoleOptions { Name = "Delete Workflow" }
            );
            await deleteMenuItem.WaitForAsync(new Microsoft.Playwright.LocatorWaitForOptions { Timeout = 5_000 });
            await deleteMenuItem.ClickAsync();

            // Screenshot: confirmation modal visible
            await PlaywrightFixture.CapturePageAsync(page, "03_confirm_modal_open");

            // Click the "Delete" button in the modal
            var confirmButton = page.GetByRole(
                Microsoft.Playwright.AriaRole.Dialog
            ).GetByRole(
                Microsoft.Playwright.AriaRole.Button,
                new Microsoft.Playwright.LocatorGetByRoleOptions { Name = "Delete", Exact = true }
            );
            await confirmButton.WaitForAsync(new Microsoft.Playwright.LocatorWaitForOptions { Timeout = 5_000 });
            await confirmButton.ClickAsync();

            // Wait for navigation back to dashboard
            await page.WaitForURLAsync("**/", new Microsoft.Playwright.PageWaitForURLOptions { Timeout = 10_000 });
            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

            // Screenshot: landed on dashboard
            await PlaywrightFixture.CapturePageAsync(page, "04_dashboard_after_delete");

            // Assert we're on the dashboard (no longer on the detail page)
            page.Url.Should().NotContain($"/workflows/{workflowId}");

            // Assert the workflow no longer exists via API
            var getResponse = await _appFixture.HttpClient.GetAsync($"/api/workflows/{workflowId}");
            getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Test 5: Browser — delete button shows confirmation modal and removes card
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Delete_workflow_button_shows_confirm_and_removes_card()
    {
        // Arrange — create a workflow through the API so we have a known state
        var projectId = await CreateProjectAsync("UI Delete Test Project");
        var workflowId = await CreateWorkflowAsync(projectId, "ui delete feature");

        // Abandon it so the card reflects a non-running terminal state
        // (the delete button may only appear for abandonable/completed states)
        await AbandonWorkflowAsync(workflowId);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        try
        {
            // Navigate to the dashboard
            var response = await page.GotoAsync(_appFixture.PlaywrightAddress);
            Assert.NotNull(response);
            response!.Status.Should().BeLessThan(500);

            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

            // Find the workflow card — look for an article element containing the workflow name
            var cards = page.Locator("[role='article']");

            // Wait for at least one card to appear
            await cards.First.WaitForAsync(
                new Microsoft.Playwright.LocatorWaitForOptions
                {
                    Timeout = 10_000
                }
            );

            // Find the specific card for the workflow we created.
            // The card shows projectName ("UI Delete Test Project"), not featureName.
            var targetCard = cards.Filter(
                new Microsoft.Playwright.LocatorFilterOptions
                {
                    HasText = "UI Delete Test Project"
                }
            );

            // Hover over the card to reveal action buttons if they are hover-only
            await targetCard.HoverAsync();

            // Find the delete button by its aria-label
            var deleteButton = targetCard.GetByRole(
                Microsoft.Playwright.AriaRole.Button,
                new Microsoft.Playwright.LocatorGetByRoleOptions { Name = "Delete workflow" }
            );

            await deleteButton.ClickAsync();

            // Wait for the confirmation dialog/modal to appear.
            // Use Exact = true so "Delete" doesn't substring-match "Delete workflow" (the ActionIcon).
            var confirmButton = page.GetByRole(
                Microsoft.Playwright.AriaRole.Dialog
            ).GetByRole(
                Microsoft.Playwright.AriaRole.Button,
                new Microsoft.Playwright.LocatorGetByRoleOptions { Name = "Delete", Exact = true }
            );

            await confirmButton.WaitForAsync(
                new Microsoft.Playwright.LocatorWaitForOptions
                {
                    Timeout = 5_000
                }
            );

            // Click the confirm button inside the modal
            await confirmButton.ClickAsync();

            // Wait for the card to disappear from the DOM
            await targetCard.WaitForAsync(
                new Microsoft.Playwright.LocatorWaitForOptions
                {
                    State = Microsoft.Playwright.WaitForSelectorState.Hidden,
                    Timeout = 10_000
                }
            );

            // Assert — the card is gone
            var remainingCards = await cards
                .Filter(new Microsoft.Playwright.LocatorFilterOptions { HasText = "UI Delete Test Project" })
                .CountAsync();

            remainingCards.Should().Be(0);
        }
        finally
        {
            await context.DisposeAsync();
        }
    }
}
