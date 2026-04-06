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
/// E2E tests for the workflow outputs / artifacts tab.
///
/// Uses MockExecutor so stages complete immediately with deterministic output,
/// without requiring real LLM credentials.
///
/// Covers:
/// - Artifacts API returns content for completed stages
/// - Individual artifact content is retrievable
/// - Browser outputs tab shows artifact entries after completion
/// - Screenshot capture of the outputs tab for visual inspection
/// </summary>
[Collection("E2E")]
public class WorkflowOutputTests : IAsyncLifetime
{
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
        _appFixture.UseMockExecutor = true;
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

    private async Task<Guid> CreateProjectAsync(string name)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync("/api/projects", new
        {
            name,
            gitRepositoryUrl = "https://github.com/example/test.git",
            constitutionPath = (string?)null,
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false,
            localRepositoryPath = (string?)null
        }, JsonOptions);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateWorkflowAsync(Guid projectId, string featureName)
    {
        var response = await _appFixture.HttpClient.PostAsJsonAsync("/api/workflows", new
        {
            templateId = DocProjectTemplateId,
            projectId,
            name = (string?)null,
            initialContext = (string?)null,
            featureName,
            selectedStages = (List<string>?)null,
            stageModelOverrides = (Dictionary<string, string>?)null
        }, JsonOptions);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Waits up to <paramref name="timeoutMs"/> for the workflow to reach Completed status.
    /// Auto-approves any gates encountered (MockExecutor output doesn't need human review).
    /// </summary>
    private async Task WaitForWorkflowCompletedAsync(Guid workflowId, int timeoutMs = 30_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var resp = await _appFixture.HttpClient.GetAsync($"/api/workflows/{workflowId}");
            if (!resp.IsSuccessStatusCode)
            {
                await Task.Delay(200);
                continue;
            }

            var wf = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
            var status = wf.GetProperty("status").GetString();

            if (status == "Completed") return;
            if (status == "Failed")
                throw new InvalidOperationException($"Workflow {workflowId} failed instead of completing.");

            // Auto-approve gates so the workflow can continue past gated stages
            if (status == "GateWaiting")
            {
                await _appFixture.HttpClient.PostAsync(
                    $"/api/workflows/{workflowId}/gates/approve", null
                );
                await Task.Delay(500);
                continue;
            }

            await Task.Delay(300);
        }

        throw new TimeoutException($"Workflow {workflowId} did not complete within {timeoutMs}ms.");
    }

    // -------------------------------------------------------------------------
    // Test 1: Artifacts API returns content for all completed stages
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Completed_workflow_artifacts_api_returns_entries()
    {
        // Arrange
        var projectId = await CreateProjectAsync("Artifacts API Test Project");
        var workflowId = await CreateWorkflowAsync(projectId, "artifacts-api-test");
        await WaitForWorkflowCompletedAsync(workflowId);

        // Act
        var response = await _appFixture.HttpClient.GetAsync(
            $"/api/workflows/{workflowId}/artifacts"
        );

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var artifacts = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOptions);
        artifacts.Should().NotBeNull();
        artifacts!.Should().NotBeEmpty("completed workflow should have at least one artifact");

        // Each artifact should have required fields
        foreach (var artifact in artifacts)
        {
            artifact.GetProperty("id").GetGuid().Should().NotBeEmpty();
            artifact.GetProperty("stageName").GetString().Should().NotBeNullOrEmpty();
            artifact.GetProperty("fileName").GetString().Should().EndWith(".md");
            artifact.GetProperty("version").GetInt32().Should().BeGreaterThan(0);
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: Artifact content endpoint returns MockExecutor output
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Artifact_content_endpoint_returns_output_content()
    {
        // Arrange
        var projectId = await CreateProjectAsync("Artifact Content Test Project");
        var workflowId = await CreateWorkflowAsync(projectId, "artifact-content-test");
        await WaitForWorkflowCompletedAsync(workflowId);

        // Get artifact list
        var listResp = await _appFixture.HttpClient.GetAsync(
            $"/api/workflows/{workflowId}/artifacts"
        );
        listResp.EnsureSuccessStatusCode();

        var artifacts = (await listResp.Content.ReadFromJsonAsync<JsonElement[]>(JsonOptions))!;
        artifacts.Should().NotBeEmpty();

        // Act — fetch content of the first (primary) artifact
        var firstArtifact = artifacts.First();
        var stageId = firstArtifact.GetProperty("stageId").GetGuid();

        var contentResp = await _appFixture.HttpClient.GetAsync(
            $"/api/workflows/{workflowId}/artifacts/{stageId}"
        );

        // Assert
        contentResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await contentResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var content = detail.GetProperty("content").GetString();

        content.Should().NotBeNullOrEmpty("artifact must have output content from MockExecutor");
        content.Should().Contain("Output", "MockExecutor includes stage name in output");
    }

    // -------------------------------------------------------------------------
    // Test 3: OutputContent is stored in DB after stage completion
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Stage_execution_stores_output_content_in_db()
    {
        // Arrange
        var projectId = await CreateProjectAsync("DB Content Test Project");
        var workflowId = await CreateWorkflowAsync(projectId, "db-content-test");
        await WaitForWorkflowCompletedAsync(workflowId);

        // Act — check the DB directly
        using var scope = _appFixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var executions = await db.Set<Antiphon.Server.Domain.Entities.StageExecution>()
            .Where(e => e.WorkflowId == workflowId && e.Status == StageStatus.Completed)
            .ToListAsync();

        // Assert
        executions.Should().NotBeEmpty("workflow should have completed stage executions");

        foreach (var execution in executions)
        {
            execution.OutputContent.Should().NotBeNullOrEmpty(
                $"StageExecution {execution.Id} should have OutputContent stored"
            );
        }
    }

    // -------------------------------------------------------------------------
    // Test 4: Browser outputs tab shows artifact entries — with screenshots
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Outputs_tab_shows_artifacts_after_workflow_completes()
    {
        // Arrange
        var projectId = await CreateProjectAsync("UI Outputs Test Project");
        var workflowId = await CreateWorkflowAsync(projectId, "ui-outputs-test");
        await WaitForWorkflowCompletedAsync(workflowId);

        var (page, context) = await _playwrightFixture.NewPageAsync();
        var passed = false;
        try
        {
            // Note the log position before navigating so we can capture new server logs
            var logStart = PlaywrightFixture.GetCurrentLogPosition();

            // Navigate to the workflow detail page
            var response = await page.GotoAsync(
                $"{_appFixture.PlaywrightAddress}/workflow/{workflowId}"
            );
            Assert.NotNull(response);
            response!.Status.Should().BeLessThan(500);

            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

            // Screenshot: full page after initial load
            await PlaywrightFixture.CapturePageAsync(page, "01_workflow_detail_loaded");

            // Click the Outputs tab
            var outputsTab = page.GetByRole(
                Microsoft.Playwright.AriaRole.Tab,
                new Microsoft.Playwright.PageGetByRoleOptions { Name = "Outputs" }
            );
            await outputsTab.WaitForAsync(new Microsoft.Playwright.LocatorWaitForOptions
            {
                Timeout = 10_000
            });
            await outputsTab.ClickAsync();

            await page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

            // Screenshot: outputs tab visible
            await PlaywrightFixture.CapturePageAsync(page, "02_outputs_tab_clicked");

            // The outputs panel should now show artifact entries
            // Mantine hides inactive panels with display:none; :visible matches only the active (Outputs) panel
            var outputsPanel = page.Locator("[data-context-panel-content]").First;

            // Screenshot: the outputs panel component
            await PlaywrightFixture.CaptureComponentAsync(outputsPanel, "outputs_panel");

            // Wait for artifact items to appear
            var artifactItems = outputsPanel.GetByRole(Microsoft.Playwright.AriaRole.Button);
            await artifactItems.First.WaitForAsync(new Microsoft.Playwright.LocatorWaitForOptions
            {
                Timeout = 10_000
            });

            var count = await artifactItems.CountAsync();
            count.Should().BeGreaterThan(0, "outputs tab should show at least one artifact");

            // Screenshot: outputs with artifacts visible
            await PlaywrightFixture.CapturePageAsync(page, "03_outputs_with_artifacts");

            // Capture the outputs panel component for baseline comparison
            await PlaywrightFixture.AssertComponentMatchesBaselineAsync(
                outputsPanel,
                "outputs_panel_with_artifacts",
                maxDiffPercent: 5.0  // Allow 5% diff for font rendering variation
            );

            // Capture server logs from this test run
            var newLogs = PlaywrightFixture.ReadNewLogLines(logStart);
            var artifactLogs = newLogs.Where(l => l.Contains("artifact", StringComparison.OrdinalIgnoreCase)).ToArray();
            Console.WriteLine($"[test] {artifactLogs.Length} artifact-related log lines captured");

            passed = true;
        }
        finally
        {
            await PlaywrightFixture.CaptureOnCompletionAsync(page, passed);
            await context.DisposeAsync();
        }
    }
}
