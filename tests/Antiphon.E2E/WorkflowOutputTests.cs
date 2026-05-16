using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;
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
[NotInParallel]
public class WorkflowOutputTests
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
    private readonly List<string> _tempRepoRoots = [];

    [Before(Test)]
    public async Task SetupAsync()
    {
        _appFixture.UsePrebuiltFrontend = true;
        _appFixture.UseMockExecutor = true;
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

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateProjectAsync(string name)
    {
        var repoPath = await CreateLocalGitRepositoryAsync();
        var response = await _appFixture.HttpClient.PostAsJsonAsync("/api/projects", new
        {
            name,
            gitRepositoryUrl = Path.Combine(Path.GetDirectoryName(repoPath)!, "origin.git"),
            constitutionPath = (string?)null,
            baseBranch = "main",
            gitHubIntegrationEnabled = false,
            notificationsEnabled = false,
            localRepositoryPath = repoPath
        }, JsonOptions);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<string> CreateLocalGitRepositoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-e2e-workflow-repos", Guid.NewGuid().ToString("N"));
        var repoPath = Path.Combine(root, "work");
        var originPath = Path.Combine(root, "origin.git");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(repoPath);
        _tempRepoRoots.Add(root);

        await RunGitAsync(root, "init", "--bare", originPath);
        await RunGitAsync(repoPath, "init");
        await RunGitAsync(repoPath, "config", "user.email", "e2e@example.test");
        await RunGitAsync(repoPath, "config", "user.name", "Antiphon E2E");
        await File.WriteAllTextAsync(Path.Combine(repoPath, "README.md"), "# E2E repository\n");
        await RunGitAsync(repoPath, "add", "README.md");
        await RunGitAsync(repoPath, "commit", "-m", "Initial commit");
        await RunGitAsync(repoPath, "branch", "-M", "main");
        await RunGitAsync(repoPath, "remote", "add", "origin", originPath);
        await RunGitAsync(repoPath, "push", "-u", "origin", "main");

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
    private async Task WaitForWorkflowCompletedAsync(Guid workflowId, int timeoutMs = 60_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var approvedStageIds = new HashSet<Guid>();
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
                var gatedStage = wf.GetProperty("stages")
                    .EnumerateArray()
                    .Where(stage => stage.GetProperty("status").GetString() == "Completed")
                    .OrderByDescending(stage => stage.GetProperty("stageOrder").GetInt32())
                    .FirstOrDefault();
                if (gatedStage.ValueKind == JsonValueKind.Undefined)
                {
                    await Task.Delay(300);
                    continue;
                }

                var stageId = gatedStage.GetProperty("id").GetGuid();
                if (!approvedStageIds.Add(stageId))
                {
                    await Task.Delay(500);
                    continue;
                }

                var approve = await _appFixture.HttpClient.PostAsync(
                    $"/api/workflows/{workflowId}/gates/approve", null
                );
                if (!approve.IsSuccessStatusCode)
                {
                    var body = await approve.Content.ReadAsStringAsync();
                    throw new InvalidOperationException(
                        $"Gate approval failed with {(int)approve.StatusCode}: {body}");
                }

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

    [Test]
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
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var artifacts = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOptions);
        artifacts.ShouldNotBeNull();
        artifacts!.ShouldNotBeEmpty("completed workflow should have at least one artifact");

        // Each artifact should have required fields
        foreach (var artifact in artifacts)
        {
            artifact.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
            artifact.GetProperty("stageName").GetString().ShouldNotBeNullOrEmpty();
            artifact.GetProperty("fileName").GetString().ShouldEndWith(".md");
            artifact.GetProperty("version").GetInt32().ShouldBeGreaterThan(0);
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: Artifact content endpoint returns MockExecutor output
    // -------------------------------------------------------------------------

    [Test]
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
        artifacts.ShouldNotBeEmpty();

        // Act — fetch content of the first (primary) artifact
        var firstArtifact = artifacts.First();
        var stageId = firstArtifact.GetProperty("stageId").GetGuid();

        var contentResp = await _appFixture.HttpClient.GetAsync(
            $"/api/workflows/{workflowId}/artifacts/{stageId}"
        );

        // Assert
        contentResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = await contentResp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var content = detail.GetProperty("content").GetString();

        content.ShouldNotBeNullOrEmpty("artifact must have output content from MockExecutor");
        content.ShouldContain("Output");
    }

    // -------------------------------------------------------------------------
    // Test 3: OutputContent is stored in DB after stage completion
    // -------------------------------------------------------------------------

    [Test]
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
        executions.ShouldNotBeEmpty("workflow should have completed stage executions");

        foreach (var execution in executions)
        {
            execution.OutputContent.ShouldNotBeNullOrEmpty(
                $"StageExecution {execution.Id} should have OutputContent stored"
            );
        }
    }

    // -------------------------------------------------------------------------
    // Test 4: Browser outputs tab shows artifact entries — with screenshots
    // -------------------------------------------------------------------------

    [Test]
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
            response.ShouldNotBeNull();
            response!.Status.ShouldBeLessThan(500);

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

            // The outputs panel should now show generated output files.
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
            count.ShouldBeGreaterThan(0, "outputs tab should show at least one generated file");

            // Screenshot: outputs with generated files visible
            await PlaywrightFixture.CapturePageAsync(page, "03_outputs_with_artifacts");

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
